using Kino.Core.Protocol;

namespace Kino.Core.Sync;

/// <summary>What the sync engine needs from a media player. Implementations must be thread-safe.</summary>
public interface IPlayback
{
    /// <summary>Current position in seconds, or null when nothing is loaded.</summary>
    double? Position { get; }

    /// <summary>Length of the loaded file in seconds, or null if unknown.</summary>
    double? MediaDuration { get; }
    bool IsPaused { get; }
    bool IsBuffering { get; }
    void SetPaused(bool paused);
    void Seek(double seconds);
    void SetSpeed(double speed);
}

/// <summary>
/// <see cref="Waiting"/>: this copy has ended before the screen's timeline (a shorter file); holds until the next anchor.
/// <see cref="NoFile"/>: the current item has no file loaded here yet (unmapped, or still loading).
/// </summary>
public enum SyncZone { Idle, Paused, Starting, Settling, InSync, Correcting, Seeking, Buffering, Host, Waiting, NoFile }

public readonly record struct SyncStatus(SyncZone Zone, double Drift, double Rate, double SeekLatency);

/// <summary>
/// Keeps a player aligned with the screen's anchor (wiki docs §14).
/// Call <see cref="Tick"/> periodically (every ~250 ms). Anchors may arrive on any thread.
/// </summary>
public sealed class SyncEngine
{
    private readonly IPlayback _player;
    private readonly Func<double> _theatreNowMs;
    private readonly Func<double> _localNowMs;
    private readonly Action<double, Action> _schedule;
    private SyncSettings _s;
    private RateController _rate;
    private readonly object _gate = new();

    private Anchor? _anchor;
    private readonly Queue<double> _drifts = new();
    private double _lastDisruption = double.NegativeInfinity;
    private double _lastHardSeek = double.NegativeInfinity;
    private double _lastPausedSeek = double.NegativeInfinity;
    private double _lastReanchor = double.NegativeInfinity;
    private double _appliedSpeed = 1.0;
    private long _scheduledStartSeq = -1;
    private bool _calibrateAfterSeek;
    private bool _checkJump;        // first playing step after a new anchor: obey a big jump at once
    private double? _anchorJump;    // |E_new − E_old| at arrival; null when there was no comparable previous anchor
    private readonly Queue<double> _seekLatencies = new();

    public SyncEngine(IPlayback player, Func<double> theatreNowMs, Func<double> localNowMs,
                      SyncSettings? settings = null, Action<double, Action>? schedule = null)
    {
        _player = player;
        _theatreNowMs = theatreNowMs;
        _localNowMs = localNowMs;
        _s = settings ?? SyncSettings.Default;
        _rate = new RateController(_s);
        _schedule = schedule ?? ((delayMs, action) => _ = Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, delayMs))).ContinueWith(_ => action()));
        SeekLatency = _s.InitialSeekLatency;
    }

    /// <summary>
    /// When false, anchors are stored but not acted on. A session keeps this off until its clock is synced, so an
    /// anchor is never applied against an unsynced clock.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Zones and rate limits; switched per item (e.g. <see cref="SyncSettings.YouTube"/>).</summary>
    public SyncSettings Settings
    {
        get { lock (_gate) return _s; }
        set
        {
            lock (_gate)
            {
                if (ReferenceEquals(value, _s)) return;
                ResetRate();
                _s = value;
                _rate = new RateController(value);
                _drifts.Clear();
            }
        }
    }

    /// <summary>
    /// When true, the engine only drives the player if <see cref="LoadedItemId"/> is the anchor's item. Otherwise it would
    /// apply the new item's timeline to the previous file while the new one loads.
    /// </summary>
    public bool RequireLoadedItem { get; set; }

    /// <summary>The playlist item whose file is loaded in the player (set by the app).</summary>
    public string? LoadedItemId { get; set; }

    /// <summary>The host's real playback defines the truth: it re-anchors instead of correcting itself.</summary>
    public bool IsHost { get; set; }

    /// <summary>Local-only nudge in ms (e.g. Bluetooth delay). Positive = play later.</summary>
    public double PersonalOffsetMs { get; set; }

    /// <summary>Seconds a seek takes to resume playback on this player; learned from hard seeks.</summary>
    public double SeekLatency { get; private set; }

    /// <summary>Raised (host only) with (position, theatreTimeMs) when the host should send a re-anchor.</summary>
    public event Action<double, double>? ReanchorRequested;

    public SyncStatus Status { get; private set; } = new(SyncZone.Idle, 0, 1, 0);

    public Anchor? Anchor { get { lock (_gate) return _anchor; } }

    /// <summary>
    /// Applies a new anchor and acts on it immediately. Waiting for the next <see cref="Tick"/> (250 ms) could miss
    /// a scheduled start entirely, because the theatre's minimum lead is only 200 ms.
    /// </summary>
    public void ApplyAnchor(Anchor anchor)
    {
        lock (_gate)
        {
            var prev = _anchor;
            if (prev is not null && anchor.Seq < prev.Seq) return; // stale
            if (prev is not null && anchor.Seq == prev.Seq) { _anchor = anchor; return; }

            double now = _theatreNowMs();
            _anchorJump = prev is null || prev.ItemId != anchor.ItemId
                ? null
                : Math.Abs(anchor.ExpectedAt(now) - prev.ExpectedAt(now));
            _anchor = anchor;
            _checkJump = true;
            _lastHardSeek = double.NegativeInfinity; // the throttle stops seek loops, not deliberate host seeks
            Disrupt(_localNowMs());
            if (Enabled) Status = Step(0);
        }
    }

    public void Tick(double dtSeconds)
    {
        lock (_gate) if (Enabled) Status = Step(dtSeconds);
    }

    private SyncStatus Step(double dt)
    {
        var a = _anchor;
        double? posOrNull = _player.Position;
        if (a is null) return new(SyncZone.Idle, 0, _appliedSpeed, SeekLatency);
        if (RequireLoadedItem && a.ItemId != LoadedItemId)
        {
            if (posOrNull is not null && !_player.IsPaused) _player.SetPaused(true); // don't play the previous file
            ResetRate();
            return new(SyncZone.NoFile, 0, 1, SeekLatency);
        }
        if (posOrNull is not double pos) return new(SyncZone.Idle, 0, _appliedSpeed, SeekLatency);

        double now = _theatreNowMs();
        double local = _localNowMs();
        double offset = PersonalOffsetMs / 1000.0;

        if (_player.IsBuffering)
        {
            ResetRate();
            Disrupt(local);
            return new(SyncZone.Buffering, 0, 1, SeekLatency);
        }

        // ---- Paused: hold exactly at the anchor position (exact seeks are invisible while paused).
        if (!a.IsPlaying)
        {
            if (!_player.IsPaused) _player.SetPaused(true);
            ResetRate();
            if (Math.Abs(pos - a.Position) > _s.PausedSeekTolerance && local - _lastPausedSeek > 500)
            {
                _player.Seek(a.Position);
                _lastPausedSeek = local;
            }
            return new(SyncZone.Paused, pos - a.Position, 1, SeekLatency);
        }

        // ---- Scheduled start pending: wait paused at the start position, then start on time.
        if (now < a.At)
        {
            if (!_player.IsPaused) _player.SetPaused(true);
            ResetRate();
            if (Math.Abs(pos - (a.Position + offset)) > _s.PausedSeekTolerance && local - _lastPausedSeek > 500)
            {
                _player.Seek(Math.Max(0, a.Position + offset));
                _lastPausedSeek = local;
            }
            double wait = a.At - now;
            if (_scheduledStartSeq != a.Seq && wait <= _s.Tick.TotalMilliseconds * 1.5)
            {
                _scheduledStartSeq = a.Seq;
                long seq = a.Seq;
                _schedule(wait, () => { lock (_gate) if (_anchor?.Seq == seq) { _player.SetPaused(false); Disrupt(_localNowMs()); } });
            }
            return new(SyncZone.Starting, 0, 1, SeekLatency);
        }

        double expected = a.ExpectedAt(now) + offset;

        // ---- This copy is over but the screen's timeline isn't (shorter file): wait for the next anchor (§14.12).
        //      Before the host branch too: a host at its end must not re-anchor the screen backwards.
        if (_player.MediaDuration is double length && expected >= length - EndMargin)
        {
            if (pos < length - EndMargin * 2 && !_player.IsPaused)
            {
                // Not at the end yet (e.g. just joined late): jump to the end and hold there.
                _player.Seek(Math.Max(0, length - EndMargin));
            }
            if (!_player.IsPaused) _player.SetPaused(true);
            ResetRate();
            return new(SyncZone.Waiting, pos - expected, 1, SeekLatency);
        }

        // ---- Should be playing but isn't (late join, missed scheduled start): jump in if needed and start.
        if (_player.IsPaused)
        {
            if (Math.Abs(pos - expected) > _s.EnterCorrection)
            {
                _player.Seek(Math.Max(0, expected + SeekLatency));
                _calibrateAfterSeek = true; // learn this player's real seek latency from the join seek too
            }
            _player.SetPaused(false);
            Disrupt(local);
            _checkJump = false;
            return new(SyncZone.Starting, pos - expected, _appliedSpeed, SeekLatency);
        }

        // ---- First playing step after a new anchor that arrived too late for its scheduled start (lag spike,
        //      slow network). Obey a real jump right away instead of settling and measuring first.
        //      Host: any jump the anchor made, since it never bends its playback and would otherwise re-anchor
        //      straight back, undoing its own seek. Viewer: only gaps that rate correction can't close smoothly.
        if (_checkJump)
        {
            _checkJump = false;
            double gap = Math.Abs(pos - expected);
            bool obey = gap > _s.HardSeek ||
                        (IsHost && _anchorJump > _s.EnterCorrection && gap > _s.EnterCorrection);
            if (obey) return HardSeek(expected, pos - expected, local);
        }

        if (local - _lastDisruption < _s.Settle.TotalMilliseconds)
            return new(SyncZone.Settling, pos - expected, _appliedSpeed, SeekLatency);

        // ---- Measure drift (median filter).
        _drifts.Enqueue(pos - expected);
        while (_drifts.Count > _s.DriftWindow) _drifts.Dequeue();
        if (_drifts.Count < Math.Min(3, _s.DriftWindow)) return new(SyncZone.Settling, pos - expected, _appliedSpeed, SeekLatency);
        double drift = ClockSync.Median(_drifts);

        if (_calibrateAfterSeek)
        {
            // drift < 0 after a hard seek means we landed behind: seeks take longer than we assumed.
            _calibrateAfterSeek = false;
            _seekLatencies.Enqueue(Math.Clamp(SeekLatency - drift, 0, 2));
            while (_seekLatencies.Count > 5) _seekLatencies.Dequeue();
            SeekLatency = _seekLatencies.Average();
        }

        // ---- Host: never bend its own playback; publish its real position instead.
        if (IsHost)
        {
            if (Math.Abs(drift) > _s.HostReanchor && local - _lastReanchor > _s.MinReanchorInterval.TotalMilliseconds)
            {
                _lastReanchor = local;
                _drifts.Clear();
                ReanchorRequested?.Invoke(pos - offset, now);
            }
            return new(SyncZone.Host, drift, _appliedSpeed, SeekLatency);
        }

        // ---- Viewer: hard seek when far off, otherwise smooth rate correction.
        if (Math.Abs(drift) > _s.HardSeek)
        {
            if (local - _lastHardSeek > _s.MinHardSeekInterval.TotalMilliseconds)
                return HardSeek(expected, drift, local);

            // Throttled (repeated local jumps): wait at normal speed. Bending speed can't close a gap this
            // big in any reasonable time, and would leave a lingering slowdown after the next seek.
            ResetRate();
            return new(SyncZone.Seeking, drift, 1, SeekLatency);
        }

        double rate = _rate.Update(drift, dt);
        if (Math.Abs(rate - _appliedSpeed) >= _s.MinRateStep || (rate == 1.0 && _appliedSpeed != 1.0))
        {
            _player.SetSpeed(rate);
            _appliedSpeed = rate;
        }
        return new(_rate.Correcting ? SyncZone.Correcting : SyncZone.InSync, drift, _appliedSpeed, SeekLatency);
    }

    /// <summary>How close to the end of a file counts as "at the end".</summary>
    private const double EndMargin = 0.25;

    private SyncStatus HardSeek(double expected, double drift, double local)
    {
        _lastHardSeek = local;
        ResetRate();
        _player.Seek(Math.Max(0, expected + SeekLatency));
        _calibrateAfterSeek = true;
        Disrupt(local);
        return new(SyncZone.Seeking, drift, 1, SeekLatency);
    }

    private void Disrupt(double local)
    {
        _lastDisruption = local;
        _drifts.Clear();
    }

    private void ResetRate()
    {
        _rate.Reset();
        if (_appliedSpeed != 1.0)
        {
            _player.SetSpeed(1.0);
            _appliedSpeed = 1.0;
        }
    }
}
