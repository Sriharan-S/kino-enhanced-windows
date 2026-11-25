using Kino.Core.Sync;

namespace Kino.Core.Tests;

/// <summary>Deterministic player model driven by simulated time. Seeks take <see cref="SeekLatency"/> to resume.</summary>
internal sealed class SimPlayer : IPlayback
{
    private double _frozenUntil = double.NegativeInfinity;

    public double Pos { get; set; }
    public double Speed { get; private set; } = 1.0;
    public bool Paused { get; set; } = true;
    public bool Buffering { get; set; }
    public double SeekLatency { get; set; } = 0.2;
    public int Seeks { get; private set; }
    public double NowSeconds { get; private set; }
    public List<double> SpeedHistory { get; } = [];

    public double? Position => Pos;
    public double? Length { get; set; }
    public double? MediaDuration => Length;
    public bool IsPaused => Paused;
    public bool IsBuffering => Buffering;

    public void SetPaused(bool paused) => Paused = paused;

    public void Seek(double seconds)
    {
        Pos = seconds;
        Seeks++;
        _frozenUntil = NowSeconds + SeekLatency;
    }

    public void SetSpeed(double speed)
    {
        Speed = speed;
        SpeedHistory.Add(speed);
    }

    public void Advance(double dt)
    {
        double start = NowSeconds;
        NowSeconds += dt;
        if (Paused || Buffering) return;
        double playable = Math.Max(0, NowSeconds - Math.Max(start, _frozenUntil));
        Pos += Speed * Math.Min(dt, playable);
        if (Length is double len && Pos >= len) { Pos = len; Paused = true; } // keep-open: stop on the last frame
    }
}

/// <summary>Runs an engine against a <see cref="SimPlayer"/> with a fake clock and scheduler.</summary>
internal sealed class SimHarness
{
    private readonly List<(double AtMs, Action Action)> _scheduled = [];

    public SimPlayer Player { get; } = new();
    public SyncEngine Engine { get; }
    public double TheatreMs => Player.NowSeconds * 1000 + 1_000_000; // arbitrary epoch
    public List<(double T, SyncStatus S)> Log { get; } = [];

    public SimHarness(SyncSettings? settings = null)
    {
        Engine = new SyncEngine(Player, () => TheatreMs, () => Player.NowSeconds * 1000, settings,
            (delayMs, action) => _scheduled.Add((TheatreMs + delayMs, action)));
    }

    /// <summary>Simulates <paramref name="seconds"/> with fine physics steps and an engine tick every 250 ms.</summary>
    public void Run(double seconds, double tick = 0.25, double step = 0.005)
    {
        double untilTick = 0;
        for (double t = 0; t < seconds; t += step)
        {
            Player.Advance(step);
            for (int i = _scheduled.Count - 1; i >= 0; i--)
            {
                if (_scheduled[i].AtMs <= TheatreMs)
                {
                    var a = _scheduled[i].Action;
                    _scheduled.RemoveAt(i);
                    a();
                }
            }
            untilTick -= step;
            if (untilTick <= 0)
            {
                Engine.Tick(tick);
                Log.Add((Player.NowSeconds, Engine.Status));
                untilTick += tick;
            }
        }
    }

    /// <summary>True drift (not the engine's filtered estimate) against an anchor.</summary>
    public double TrueDrift => Player.Pos - Engine.Anchor!.ExpectedAt(TheatreMs);
}
