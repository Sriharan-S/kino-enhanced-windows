namespace Kino.Core.Sync;

/// <summary>Sync engine constants (wiki docs §14 and §22). All tunable.</summary>
public sealed record SyncSettings
{
    public static readonly SyncSettings Default = new();

    // Clock sync
    public int BurstSamples { get; init; } = 8;
    public TimeSpan BurstInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public int ResyncSamples { get; init; } = 3;
    public TimeSpan ResyncEvery { get; init; } = TimeSpan.FromMinutes(2);
    public const double OffsetReplaceThresholdMs = 100;
    public const double OffsetBlendOld = 0.8;

    // Drift measurement
    public TimeSpan Tick { get; init; } = TimeSpan.FromMilliseconds(250);
    public int DriftWindow { get; init; } = 5;
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(1);

    // Zones
    public double EnterCorrection { get; init; } = 0.25;
    public double ExitCorrection { get; init; } = 0.08;
    public double HardSeek { get; init; } = 3.0;
    public double PausedSeekTolerance { get; init; } = 0.05;
    public TimeSpan MinHardSeekInterval { get; init; } = TimeSpan.FromSeconds(5);
    public double InitialSeekLatency { get; init; } = 0.3;

    // Rate controller
    public double MaxDeviation { get; init; } = 0.05;   // ±5 %
    public double SlewPerSecond { get; init; } = 0.005; // 0.5 % per second
    public double Gain { get; init; } = 0.10;           // per second
    public double MinRateStep { get; init; } = 0.0005;  // don't spam the player with tiny changes

    // Host
    public double HostReanchor { get; init; } = 0.25;
    public TimeSpan MinReanchorInterval { get; init; } = TimeSpan.FromSeconds(2);
}
