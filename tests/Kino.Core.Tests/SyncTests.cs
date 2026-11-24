using Kino.Core.Protocol;
using Kino.Core.Sync;

namespace Kino.Core.Tests;

public class RateControllerTests
{
    private static readonly SyncSettings S = SyncSettings.Default;

    /// <summary>Closed-loop simulation: drift changes by (rate − 1)·dt.</summary>
    private static (double Time, double MaxRateDev, double MaxStep, double Overshoot, double FinalDrift) Simulate(double drift0)
    {
        var c = new RateController(S);
        double d = drift0, t = 0, prev = 1, maxDev = 0, maxStep = 0, overshoot = 0;
        const double dt = 0.25;
        double? reached = null;

        while (t < 300)
        {
            double rate = c.Update(d, dt);
            maxDev = Math.Max(maxDev, Math.Abs(rate - 1));
            maxStep = Math.Max(maxStep, Math.Abs(rate - prev));
            prev = rate;
            d += (rate - 1) * dt;
            t += dt;
            if (Math.Sign(d) != Math.Sign(drift0)) overshoot = Math.Max(overshoot, Math.Abs(d));
            if (reached is null && !c.Correcting) reached = t;
            if (reached is not null && rate == 1.0) break;
        }
        return (reached ?? double.NaN, maxDev, maxStep, overshoot, d);
    }

    [Theory]
    [InlineData(0.3, 25)]
    [InlineData(-1.0, 40)]
    [InlineData(3.0, 85)]
    [InlineData(-3.0, 85)]
    public void Converges_smoothly_without_overshoot(double drift0, double maxSeconds)
    {
        var r = Simulate(drift0);
        Assert.True(r.Time <= maxSeconds, $"took {r.Time}s");
        Assert.True(r.MaxRateDev <= S.MaxDeviation + 1e-9, $"rate deviation {r.MaxRateDev}");
        Assert.True(r.MaxStep <= S.SlewPerSecond * 0.25 + 1e-9, $"rate step {r.MaxStep}");
        Assert.True(r.Overshoot < 0.02, $"overshoot {r.Overshoot}s");
        Assert.True(Math.Abs(r.FinalDrift) < S.ExitCorrection + 0.01, $"final drift {r.FinalDrift}");
    }

    [Fact]
    public void Ignores_drift_inside_the_green_zone()
    {
        var c = new RateController(S);
        for (int i = 0; i < 100; i++) Assert.Equal(1.0, c.Update(0.2, 0.25));
    }

    [Fact]
    public void Hysteresis_keeps_correcting_until_exit_threshold()
    {
        var c = new RateController(S);
        c.Update(0.3, 0.25);
        Assert.True(c.Correcting);
        c.Update(0.15, 0.25); // below enter, above exit
        Assert.True(c.Correcting);
        c.Update(0.05, 0.25);
        Assert.False(c.Correcting);
    }
}

public class ClockSyncTests
{
    [Fact]
    public void Estimates_offset_from_lowest_rtt_samples()
    {
        double local = 0;
        var clock = new ClockSync(() => local);
        const double trueOffset = 5_000;
        var rng = new Random(42);

        for (int i = 0; i < 8; i++)
        {
            double up = 20 + rng.NextDouble() * 5, down = 20 + rng.NextDouble() * 5;
            if (i % 3 == 0) up += 150; // congested, asymmetric samples should be discarded
            double t0 = local;
            double ts = t0 + up + trueOffset;
            double t1 = t0 + up + down;
            clock.AddSample(t0, ts, t1);
            local = t1 + 100;
        }

        Assert.True(clock.Commit());
        Assert.InRange(clock.Offset, trueOffset - 3, trueOffset + 3);
        Assert.InRange(clock.RttMs, 40, 50);
    }

    [Fact]
    public void Blends_small_changes_and_replaces_large_jumps()
    {
        var clock = new ClockSync(() => 0);
        clock.AddSample(0, 1000, 0); clock.Commit();
        clock.AddSample(0, 1050, 0); clock.Commit();
        Assert.Equal(1010, clock.Offset, 3); // 0.8·1000 + 0.2·1050
        clock.AddSample(0, 9000, 0); clock.Commit();
        Assert.Equal(9000, clock.Offset, 3);
    }
}

public class SyncEngineTests
{
    private static Anchor Playing(double position, double atMs, long seq = 1) => new("i1", "playing", position, 1, atMs, seq);

    [Fact]
    public void Viewer_behind_by_1_5s_catches_up_with_rate_only()
    {
        var h = new SimHarness();
        h.Player.Pos = 8.5; h.Player.Paused = false;
        h.Engine.ApplyAnchor(Playing(10, h.TheatreMs));

        h.Run(50); // ~45 s to reach the green zone from 1.5 s, plus easing back to 1.0×

        Assert.Equal(0, h.Player.Seeks);
        Assert.InRange(h.TrueDrift, -0.1, 0.1);
        Assert.All(h.Player.SpeedHistory, s => Assert.InRange(s, 0.95, 1.05));
        Assert.Equal(1.0, h.Player.Speed);
    }

    [Fact]
    public void Viewer_far_off_hard_seeks_then_converges()
    {
        var h = new SimHarness();
        h.Player.Pos = 30; h.Player.Paused = false; h.Player.SeekLatency = 0.4;
        h.Engine.ApplyAnchor(Playing(100, h.TheatreMs));

        h.Run(20);

        // The first seek assumes 0.3 s latency but the player takes 0.4 s, so it lands ~0.1 s behind:
        // inside the green zone, so no further correction. The engine learns the real latency for next time.
        Assert.Equal(1, h.Player.Seeks);
        Assert.InRange(h.TrueDrift, -SyncSettings.Default.EnterCorrection, SyncSettings.Default.EnterCorrection);
        Assert.InRange(h.Engine.SeekLatency, 0.35, 0.45);
    }

    [Fact]
    public void Paused_anchor_pauses_exactly_at_position()
    {
        var h = new SimHarness();
        h.Player.Pos = 12; h.Player.Paused = false;
        h.Engine.ApplyAnchor(new Anchor("i1", "paused", 42.5, 1, h.TheatreMs, 1));

        h.Run(2);

        Assert.True(h.Player.Paused);
        Assert.Equal(42.5, h.Player.Pos, 3);
    }

    [Fact]
    public void Scheduled_start_begins_on_time()
    {
        var h = new SimHarness();
        h.Player.Pos = 0; h.Player.Paused = true;
        double startAt = h.TheatreMs + 600;
        h.Engine.ApplyAnchor(Playing(0, startAt));

        h.Run(0.5);
        Assert.True(h.Player.Paused);
        h.Run(5);

        Assert.False(h.Player.Paused);
        Assert.InRange(h.TrueDrift, -0.03, 0.03);
    }

    [Fact]
    public void Host_reanchors_instead_of_bending_playback()
    {
        var h = new SimHarness();
        h.Engine.IsHost = true;
        double? reported = null;
        h.Engine.ReanchorRequested += (pos, _) => reported = pos;
        h.Player.Pos = 20.6; h.Player.Paused = false;
        h.Engine.ApplyAnchor(Playing(20, h.TheatreMs));

        h.Run(3);

        Assert.NotNull(reported);
        Assert.Empty(h.Player.SpeedHistory);
    }

    [Fact]
    public void Stale_anchors_are_ignored()
    {
        var h = new SimHarness();
        h.Engine.ApplyAnchor(Playing(10, 0, seq: 5));
        h.Engine.ApplyAnchor(Playing(99, 0, seq: 4));
        Assert.Equal(10, h.Engine.Anchor!.Position);
    }

    [Fact]
    public void Personal_offset_shifts_target()
    {
        var h = new SimHarness();
        h.Engine.PersonalOffsetMs = 500;
        h.Player.Pos = 10; h.Player.Paused = false;
        h.Engine.ApplyAnchor(Playing(10, h.TheatreMs));

        h.Run(40);

        Assert.InRange(h.TrueDrift, 0.4, 0.6);
    }
}

public class AnchorTests
{
    [Fact]
    public void Expected_position()
    {
        var a = new Anchor("i1", "playing", 10, 1, 1000, 1);
        Assert.Equal(10, a.ExpectedAt(500));    // scheduled, not started
        Assert.Equal(12, a.ExpectedAt(3000), 6);
        Assert.Equal(10, (a with { State = "paused" }).ExpectedAt(9000));
    }
}
