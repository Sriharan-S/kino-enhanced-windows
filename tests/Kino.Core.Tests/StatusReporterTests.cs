using Kino.Core.Protocol;
using Kino.Core.Sync;

namespace Kino.Core.Tests;

public class StatusReporterTests
{
    /// <summary>Feeds one zone every 250 ms for <paramref name="seconds"/>; returns what would be sent.</summary>
    private static List<string> Run(StatusReporter r, SyncZone zone, double seconds, ref double t, bool mismatch = false)
    {
        var sent = new List<string>();
        for (double end = t + seconds * 1000; t < end; t += 250)
            if (r.Update(zone, mismatch, t) is { } s) sent.Add(s);
        return sent;
    }

    [Fact]
    public void Short_buffering_is_not_reported_but_two_seconds_is()
    {
        var r = new StatusReporter();
        double t = 0;
        Assert.Empty(Run(r, SyncZone.Buffering, 1.5, ref t));
        Assert.Empty(Run(r, SyncZone.InSync, 3, ref t)); // recovered before 2 s: nothing to report
        Assert.Equal(["buffering"], Run(r, SyncZone.Buffering, 3, ref t));
        Assert.Equal(["sync"], Run(r, SyncZone.InSync, 3, ref t));
    }

    [Fact]
    public void Brief_flaps_are_debounced_and_transients_keep_the_last_status()
    {
        var r = new StatusReporter();
        double t = 0;
        Assert.Empty(Run(r, SyncZone.Correcting, 0.5, ref t)); // under the 1 s debounce
        Assert.Empty(Run(r, SyncZone.InSync, 2, ref t));
        Assert.Equal(["catching"], Run(r, SyncZone.Correcting, 2, ref t));
        Assert.Empty(Run(r, SyncZone.Settling, 3, ref t));     // settling keeps "catching"
        Assert.Equal(["sync"], Run(r, SyncZone.InSync, 2, ref t));
    }

    [Fact]
    public void Waiting_no_file_and_mismatch_are_reported()
    {
        var r = new StatusReporter();
        double t = 0;
        Assert.Equal(["mismatch"], Run(r, SyncZone.InSync, 2, ref t, mismatch: true));
        Assert.Equal(["waiting"], Run(r, SyncZone.Waiting, 2, ref t, mismatch: true)); // waiting wins over mismatch
        Assert.Equal(["nofile"], Run(r, SyncZone.NoFile, 2, ref t));
    }

    [Fact]
    public void Ready_and_hold_messages_parse()
    {
        var ready = Assert.IsType<ServerMessage.ReadyMsg>(ServerMessage.Parse(
            """{"type":"ready","data":{"id":3,"by":"Alice","deadline":5,"answers":{"a1b2c3d4e5f6":true},"done":false}}""")).Data!;
        Assert.True(ready.Answers["a1b2c3d4e5f6"]);
        Assert.Null(Assert.IsType<ServerMessage.HoldMsg>(ServerMessage.Parse("""{"type":"hold","data":null}""")).Data);
        Assert.Equal(["Bob"], Assert.IsType<ServerMessage.HoldMsg>(ServerMessage.Parse("""{"type":"hold","data":["Bob"]}""")).Data!);
    }
}
