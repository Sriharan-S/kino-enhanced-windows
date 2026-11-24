namespace Kino.Core.Sync;

/// <summary>
/// Smooth playback-rate controller (wiki docs §14.8).
/// Proportional, capped at ±MaxDeviation, slew-limited, and braking early (√(2·S·|d|)) so it never overshoots.
/// Hysteresis: correction starts at EnterCorrection and runs until |drift| &lt; ExitCorrection.
/// </summary>
public sealed class RateController(SyncSettings settings)
{
    /// <summary>Current deviation from 1.0× (e.g. +0.03 = 1.03×).</summary>
    public double Deviation { get; private set; }
    public bool Correcting { get; private set; }

    public double Rate => 1.0 + Deviation;

    /// <param name="drift">Seconds; positive = ahead of the expected position.</param>
    /// <param name="dt">Seconds since the previous update.</param>
    public double Update(double drift, double dt)
    {
        double abs = Math.Abs(drift);
        if (!Correcting && abs >= settings.EnterCorrection) Correcting = true;
        else if (Correcting && abs < settings.ExitCorrection) Correcting = false;

        double want = 0;
        if (Correcting)
        {
            double magnitude = Math.Min(settings.Gain * abs,
                               Math.Min(settings.MaxDeviation, Math.Sqrt(2 * settings.SlewPerSecond * abs)));
            want = -Math.Sign(drift) * magnitude; // ahead → slow down, behind → speed up
        }

        double maxStep = settings.SlewPerSecond * dt;
        Deviation += Math.Clamp(want - Deviation, -maxStep, maxStep);
        if (Math.Abs(Deviation) < 1e-9) Deviation = 0;
        return Rate;
    }

    public void Reset()
    {
        Deviation = 0;
        Correcting = false;
    }
}
