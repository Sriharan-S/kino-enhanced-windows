namespace Kino.Core.Sync;

/// <summary>
/// Turns the sync engine's zone into the status other viewers see (spec §12): "sync", "catching", "buffering",
/// "mismatch", "waiting" or "nofile". Reports only changes, and debounces them, so the theatre sees state changes and not
/// a stream (spec §20). Buffering is only reported once it has lasted 2 s (spec §14.10).
/// </summary>
public sealed class StatusReporter
{
    public const double BufferingDelayMs = 2000, DebounceMs = 1000;

    private string _sent = "sync";
    private string? _candidate;
    private double _candidateSince;
    private double _bufferingSince = double.NaN;

    public string Current => _sent;

    /// <summary>Returns the status to send now, or null if nothing changed.</summary>
    public string? Update(SyncZone zone, bool mismatch, double nowMs)
    {
        bool buffering = zone == SyncZone.Buffering;
        if (!buffering) _bufferingSince = double.NaN;
        else if (double.IsNaN(_bufferingSince)) _bufferingSince = nowMs;
        bool longBuffering = buffering && nowMs - _bufferingSince >= BufferingDelayMs;

        string desired = longBuffering ? "buffering"
            : zone == SyncZone.Waiting ? "waiting"
            : zone == SyncZone.NoFile ? "nofile"
            : mismatch ? "mismatch"
            : zone is SyncZone.Correcting or SyncZone.Seeking ? "catching"
            : zone is SyncZone.Settling or SyncZone.Starting or SyncZone.Buffering or SyncZone.Idle ? _sent // transient: keep
            : "sync";

        if (desired == _sent)
        {
            _candidate = null;
            return null;
        }
        // Buffering already waited 2 s; anything else must hold for the debounce period.
        if (desired == "buffering" || (_candidate == desired && nowMs - _candidateSince >= DebounceMs))
        {
            _sent = desired;
            _candidate = null;
            return desired;
        }
        if (_candidate != desired)
        {
            _candidate = desired;
            _candidateSince = nowMs;
        }
        return null;
    }
}
