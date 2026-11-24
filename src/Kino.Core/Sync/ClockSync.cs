using System.Diagnostics;

namespace Kino.Core.Sync;

/// <summary>
/// NTP-style clock offset estimation against the theatre (wiki docs §14.3).
/// theatreNow ≈ localNow + Offset, where localNow is a monotonic clock.
/// </summary>
public sealed class ClockSync
{
    private readonly Func<double> _localNowMs;
    private readonly List<(double Rtt, double Offset)> _samples = [];
    private readonly object _gate = new();

    public ClockSync(Func<double>? localNowMs = null) => _localNowMs = localNowMs ?? MonotonicMs;

    public static double MonotonicMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    public double Offset { get; private set; }
    public double RttMs { get; private set; } = double.NaN;
    public bool IsSynced { get; private set; }

    public double LocalNowMs => _localNowMs();
    public double TheatreNowMs => _localNowMs() + Offset;

    /// <summary>Records one ping/pong exchange: t0/t1 are local send/receive times, ts is theatre time.</summary>
    public void AddSample(double t0, double ts, double t1)
    {
        double rtt = t1 - t0;
        if (rtt < 0 || rtt > 10_000) return;
        lock (_gate) _samples.Add((rtt, ts - (t0 + t1) / 2.0));
    }

    /// <summary>Applies the collected samples: best 3 by RTT, median offset; blended unless the jump is large.</summary>
    public bool Commit()
    {
        double offset, rtt;
        lock (_gate)
        {
            if (_samples.Count == 0) return false;
            var best = _samples.OrderBy(s => s.Rtt).Take(3).ToArray();
            offset = Median(best.Select(s => s.Offset));
            rtt = Median(best.Select(s => s.Rtt));
            _samples.Clear();
        }

        Offset = !IsSynced || Math.Abs(offset - Offset) > SyncSettings.OffsetReplaceThresholdMs
            ? offset
            : SyncSettings.OffsetBlendOld * Offset + (1 - SyncSettings.OffsetBlendOld) * offset;
        RttMs = rtt;
        IsSynced = true;
        return true;
    }

    internal static double Median(IEnumerable<double> values)
    {
        var a = values.OrderBy(v => v).ToArray();
        if (a.Length == 0) return double.NaN;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2.0;
    }
}
