using Kino.Core.Media;
using Kino.Core.Protocol;
using Kino.Core.Sync;

namespace Kino.Core.Tests;

public class MediaMatchTests
{
    private static Item Local(string id, string name, double? duration) => new(id, "local", name, duration, null);
    private static LocalFile File(string name, double? duration) => new($@"C:\Videos\{name}", duration);

    [Fact]
    public void Tolerance_is_the_larger_of_2_s_and_half_a_percent()
    {
        Assert.True(MediaMatch.DurationMatches(100, 101.9));   // short files: 2 s floor
        Assert.False(MediaMatch.DurationMatches(100, 102.1));
        Assert.True(MediaMatch.DurationMatches(1420, 1427));   // 24 min: 0.5% = 7.1 s
        Assert.False(MediaMatch.DurationMatches(1420, 1428));
        Assert.True(MediaMatch.DurationMatches(7200, 7235));   // 2 h: 0.5% = 36 s
        Assert.False(MediaMatch.DurationMatches(7200, 7237));
        Assert.False(MediaMatch.DurationMatches(null, 100));
    }

    [Theory]
    [InlineData("EP 01.mkv", "ep_01.mp4", true)]
    [InlineData("Movie.2019.1080p.mkv", "movie 2019 1080p.avi", true)]
    [InlineData("A -- B.mkv", "a b.mkv", true)]
    [InlineData("EP 01.mkv", "EP 02.mkv", false)]
    [InlineData("Clip C", "clip_c", true)]
    public void Names_ignore_case_extension_and_separators(string a, string b, bool same) =>
        Assert.Equal(same, MediaMatch.NameMatches(a, b));

    [Fact]
    public void Compare_checks_duration_first_then_name()
    {
        var host = Local("i1", "EP 01.mkv", 1420.5);
        Assert.Equal(MatchKind.Match, MediaMatch.Compare(host, File("ep.01.mp4", 1420.0)));
        Assert.Equal(MatchKind.LikelyMatch, MediaMatch.Compare(host, File("random name.mkv", 1421.0)));
        Assert.Equal(MatchKind.Mismatch, MediaMatch.Compare(host, File("EP 01.mkv", 1300.0)));   // same name, wrong cut
        Assert.Equal(MatchKind.Match, MediaMatch.Compare(Local("i2", "x.mkv", null), File("x.mp4", 50)));
    }

    [Fact]
    public void AutoMap_prefers_duration_then_name_and_uses_each_file_once()
    {
        Item[] items = [Local("i1", "EP 01.mkv", 1420), Local("i2", "EP 02.mkv", 1380), Local("i3", "EP 03.mkv", 1500), new("i4", "url", "Clip", null, "https://x/c.mp4")];
        LocalFile[] files =
        [
            File("Show - 02.mkv", 1380.5), // duration match for i2, different name
            File("EP 01.mkv", 1420.2),     // perfect for i1
            File("EP 03.mkv", 1200),       // name only for i3 (wrong length) → still mapped, shown as mismatch
            File("EP 01 (copy).mkv", 1420.1),
        ];
        var map = MediaMatch.AutoMap(items, files);

        Assert.Equal("EP 01.mkv", map["i1"].Name);
        Assert.Equal("Show - 02.mkv", map["i2"].Name);
        Assert.Equal("EP 03.mkv", map["i3"].Name);
        Assert.False(map.ContainsKey("i4")); // external items aren't mapped
        Assert.Equal(MatchKind.Mismatch, MediaMatch.Compare(items[2], map["i3"]));
    }

    [Fact]
    public void AutoMap_keeps_manual_mappings()
    {
        Item[] items = [Local("i1", "EP 01.mkv", 1420)];
        var manual = new Dictionary<string, LocalFile> { ["i1"] = File("my pick.mkv", 900) };
        var map = MediaMatch.AutoMap(items, [File("EP 01.mkv", 1420)], manual);
        Assert.Equal("my pick.mkv", map["i1"].Name);
    }

    [Fact]
    public void Natural_sort_orders_numbers_by_value()
    {
        string[] names = ["EP 10.mkv", "ep 2.mkv", "EP 1.mkv", "Extra.mkv", "EP 02b.mkv"];
        var sorted = names.Order(MediaMatch.NaturalComparer).ToArray();
        Assert.Equal(["EP 1.mkv", "ep 2.mkv", "EP 02b.mkv", "EP 10.mkv", "Extra.mkv"], sorted);
    }
}

public class PlaylistEngineTests
{
    private static Anchor Playing(string item, double position, double atMs, long seq) => new(item, "playing", position, 1, atMs, seq);

    [Fact]
    public void Holds_while_the_current_item_has_no_file_loaded()
    {
        var h = new SimHarness();
        h.Engine.RequireLoadedItem = true;
        h.Engine.LoadedItemId = "i1";
        h.Player.Pos = 50; h.Player.Paused = false;

        h.Engine.ApplyAnchor(Playing("i2", 0, h.TheatreMs, 1));
        h.Run(1);
        Assert.Equal(SyncZone.NoFile, h.Engine.Status.Zone);
        Assert.True(h.Player.Paused);   // the previous file doesn't keep playing
        Assert.Equal(0, h.Player.Seeks); // and isn't dragged to the new item's timeline

        h.Engine.LoadedItemId = "i2";   // new file is in
        h.Player.Pos = 0;
        h.Run(3);
        Assert.False(h.Player.Paused);
        Assert.InRange(h.TrueDrift, -0.25, 0.25);
    }

    [Fact]
    public void A_shorter_copy_waits_at_its_end_without_seek_loops()
    {
        var h = new SimHarness();
        h.Player.Length = 95; // host's file is longer
        h.Player.Pos = 90; h.Player.Paused = false;
        h.Engine.ApplyAnchor(Playing("i1", 90, h.TheatreMs, 1));

        h.Run(15);

        Assert.Equal(SyncZone.Waiting, h.Engine.Status.Zone);
        Assert.True(h.Player.Paused);
        Assert.Equal(0, h.Player.Seeks);
    }

    [Fact]
    public void Host_at_its_end_does_not_reanchor_the_screen_backwards()
    {
        var h = new SimHarness();
        h.Engine.IsHost = true;
        var reanchors = new List<double>();
        h.Engine.ReanchorRequested += (pos, _) => reanchors.Add(pos);
        h.Player.Length = 100;
        h.Player.Pos = 98; h.Player.Paused = false;
        h.Engine.ApplyAnchor(Playing("i1", 98, h.TheatreMs, 1));

        h.Run(6); // the theatre would advance at 100 s; until then the host just waits

        Assert.Empty(reanchors);
        Assert.Equal(SyncZone.Waiting, h.Engine.Status.Zone);
    }
}
