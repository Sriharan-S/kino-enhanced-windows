using System.Runtime.InteropServices;
using Kino.Core.Sync;

namespace Kino.Player;

/// <summary>
/// Managed wrapper around one libmpv instance that renders into a native window (HWND).
/// Property changes are raised on the mpv event thread; callers must marshal to the UI thread.
/// libmpv's client API is thread-safe, so <see cref="IPlayback"/> members may be called from any thread.
/// </summary>
public sealed class MpvPlayer : IDisposable, IPlayback
{
    private enum Prop : ulong { TimePos = 1, Duration, Pause, PausedForCache, Speed, EofReached }

    private IntPtr _ctx;
    private readonly Thread _eventThread;
    private volatile bool _stopping;
    private volatile bool _paused = true;
    private volatile bool _buffering;

    public double Duration { get; private set; } = double.NaN;

    double? IPlayback.MediaDuration => double.IsNaN(Duration) || Duration <= 0 ? null : Duration;

    // ---- IPlayback ------------------------------------------------------------------------
    double? IPlayback.Position => GetPosition() is var p && double.IsFinite(p) ? p : null;
    bool IPlayback.IsPaused => _paused;
    bool IPlayback.IsBuffering => _buffering;
    void IPlayback.Seek(double seconds) => SeekAbsolute(seconds);

    public event Action<double>? PositionChanged;
    public event Action<double>? DurationChanged;
    public event Action<bool>? PausedChanged;
    public event Action<bool>? BufferingChanged;
    public event Action<double>? SpeedChanged;
    public event Action<bool>? EofChanged;
    public event Action<string>? FileLoaded;
    public event Action<string>? Error;

    public MpvPlayer(IntPtr hostHwnd)
    {
        _ctx = LibMpv.Create();
        if (_ctx == IntPtr.Zero) throw new InvalidOperationException("mpv_create failed");

        // Options must be set before mpv_initialize.
        Set("wid", ((long)hostHwnd).ToString());
        Set("vo", "gpu-next");
        Set("hwdec", "auto-safe");
        Set("keep-open", "yes");               // stay on last frame at EOF; sync engine decides what comes next
        Set("audio-pitch-correction", "yes");  // speed changes without pitch shift (scaletempo2)
        Set("volume-max", "200");              // allow volume boost up to 200%
        Set("input-default-bindings", "no");   // Kino owns all input
        Set("input-vo-keyboard", "no");
        Set("osc", "no");
        Set("osd-level", "0");
        Set("hr-seek", "yes");                 // exact seeks

        Check(LibMpv.Initialize(_ctx), "mpv_initialize");

        LibMpv.ObserveProperty(_ctx, (ulong)Prop.TimePos, "time-pos", LibMpv.Format.Double);
        LibMpv.ObserveProperty(_ctx, (ulong)Prop.Duration, "duration", LibMpv.Format.Double);
        LibMpv.ObserveProperty(_ctx, (ulong)Prop.Pause, "pause", LibMpv.Format.Flag);
        LibMpv.ObserveProperty(_ctx, (ulong)Prop.PausedForCache, "paused-for-cache", LibMpv.Format.Flag);
        LibMpv.ObserveProperty(_ctx, (ulong)Prop.Speed, "speed", LibMpv.Format.Double);
        LibMpv.ObserveProperty(_ctx, (ulong)Prop.EofReached, "eof-reached", LibMpv.Format.Flag);

        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
        _eventThread.Start();
    }

    // ---- Commands -------------------------------------------------------------------------

    public void Load(string pathOrUrl)
    {
        Duration = double.NaN; // unknown until the new file reports it
        Command("loadfile", pathOrUrl, "replace");
    }

    public void SetPaused(bool paused)
    {
        _paused = paused; // optimistic; the observed property confirms it shortly after
        int flag = paused ? 1 : 0;
        LibMpv.SetProperty(_ctx, "pause", LibMpv.Format.Flag, ref flag);
    }

    public void SeekAbsolute(double seconds) =>
        Command("seek", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute+exact");

    /// <summary>Playback rate. The sync engine uses fine steps (e.g. 0.95–1.05); pitch is preserved.</summary>
    public void SetSpeed(double speed) => LibMpv.SetProperty(_ctx, "speed", LibMpv.Format.Double, ref speed);

    public void SetVolume(double percent) => LibMpv.SetProperty(_ctx, "volume", LibMpv.Format.Double, ref percent);

    public double GetPosition() =>
        LibMpv.GetProperty(_ctx, "time-pos", LibMpv.Format.Double, out var v) >= 0 ? v : double.NaN;

    // ---- Internals ------------------------------------------------------------------------

    private void Set(string name, string value) => Check(LibMpv.SetOptionString(_ctx, name, value), $"option {name}");

    private void Command(params string[] args)
    {
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            int rc = LibMpv.Command(_ctx, ptrs);
            if (rc < 0) Error?.Invoke($"{args[0]} failed: {LibMpv.Error(rc)}");
        }
        finally
        {
            foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
        }
    }

    private static void Check(int rc, string what)
    {
        if (rc < 0) throw new InvalidOperationException($"{what}: {LibMpv.Error(rc)}");
    }

    private void EventLoop()
    {
        while (!_stopping)
        {
            var evPtr = LibMpv.WaitEvent(_ctx, -1);
            if (_stopping) break;
            var ev = Marshal.PtrToStructure<LibMpv.Event>(evPtr);

            switch (ev.EventId)
            {
                case LibMpv.EventId.Shutdown:
                    return;
                case LibMpv.EventId.FileLoaded:
                    FileLoaded?.Invoke("loaded");
                    break;
                case LibMpv.EventId.EndFile:
                    var ef = Marshal.PtrToStructure<LibMpv.EventEndFile>(ev.Data);
                    if (ef.Error < 0) Error?.Invoke($"Playback error: {LibMpv.Error(ef.Error)}");
                    break;
                case LibMpv.EventId.PropertyChange:
                    OnPropertyChange((Prop)ev.ReplyUserdata, Marshal.PtrToStructure<LibMpv.EventProperty>(ev.Data));
                    break;
            }
        }
    }

    private void OnPropertyChange(Prop id, LibMpv.EventProperty p)
    {
        if (p.Format == LibMpv.Format.None || p.Data == IntPtr.Zero) return; // property unavailable

        switch (id)
        {
            case Prop.TimePos: PositionChanged?.Invoke(ReadDouble(p.Data)); break;
            case Prop.Duration: Duration = ReadDouble(p.Data); DurationChanged?.Invoke(Duration); break;
            case Prop.Speed: SpeedChanged?.Invoke(ReadDouble(p.Data)); break;
            case Prop.Pause: _paused = Marshal.ReadInt32(p.Data) != 0; PausedChanged?.Invoke(_paused); break;
            case Prop.PausedForCache: _buffering = Marshal.ReadInt32(p.Data) != 0; BufferingChanged?.Invoke(_buffering); break;
            case Prop.EofReached: EofChanged?.Invoke(Marshal.ReadInt32(p.Data) != 0); break;
        }
    }

    private static double ReadDouble(IntPtr p) => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p));

    public void Dispose()
    {
        if (_ctx == IntPtr.Zero) return;
        _stopping = true;
        LibMpv.Wakeup(_ctx);
        _eventThread.Join(TimeSpan.FromSeconds(2));
        LibMpv.TerminateDestroy(_ctx);
        _ctx = IntPtr.Zero;
    }
}
