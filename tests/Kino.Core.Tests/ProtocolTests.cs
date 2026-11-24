using Kino.Core.Identity;
using Kino.Core.Protocol;

namespace Kino.Core.Tests;

public class ProtocolTests
{
    /// <summary>
    /// Contract test: parses the canonical server examples from the kino-enhanced repo.
    /// Looks for a sibling checkout (../kino or ../kino-enhanced) or KINO_PROTOCOL_DIR.
    /// </summary>
    private static string? ExamplesDir()
    {
        var env = Environment.GetEnvironmentVariable("KINO_PROTOCOL_DIR");
        if (env is not null) return Path.Combine(env, "examples", "server");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kino.slnx"))) dir = dir.Parent;
        if (dir?.Parent is null) return null;

        foreach (var name in new[] { "kino", "kino-enhanced" })
        {
            var p = Path.Combine(dir.Parent.FullName, name, "protocol", "examples", "server");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    [Fact]
    public void Parses_all_canonical_server_examples()
    {
        var dir = ExamplesDir();
        if (dir is null) return; // protocol repo not checked out next to this one

        var files = Directory.GetFiles(dir, "*.json");
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            var msg = ServerMessage.Parse(File.ReadAllText(f));
            Assert.IsNotType<ServerMessage.Unknown>(msg);
        }
    }

    [Fact]
    public void Welcome_fields_round_trip()
    {
        const string json = """
            {"type":"welcome","data":{"self":{"id":"a","name":"Al","device":"windows","isHost":true,"rtt":null,"buffering":false},
             "screen":{"code":"7K3QXM","name":"M","visibility":"public","maxViewers":10,"showDevices":true},
             "viewers":[],"item":null,"anchor":{"itemId":null,"state":"paused","position":0,"rate":1,"at":5,"seq":0},"serverTime":9}}
            """;
        var w = Assert.IsType<ServerMessage.WelcomeMsg>(ServerMessage.Parse(json)).Data;
        Assert.True(w.Self.IsHost);
        Assert.Equal("7K3QXM", w.Screen.Code);
        Assert.False(w.Anchor.IsPlaying);
    }

    [Fact]
    public void Unknown_types_are_tolerated()
    {
        Assert.IsType<ServerMessage.Unknown>(ServerMessage.Parse("""{"type":"future.thing","data":{"x":1}}"""));
    }

    [Fact]
    public void Tokens_are_stable_per_screen_and_differ_across_screens()
    {
        var id = DeviceIdentity.FromSecret(new byte[32]);
        Assert.Equal(id.TokenFor("screen-a"), id.TokenFor("screen-a"));
        Assert.NotEqual(id.TokenFor("screen-a"), id.TokenFor("screen-b"));
        Assert.Matches("^[0-9a-f]{64}$", id.TokenFor("screen-a"));
    }

    [Fact]
    public void Token_matches_theatre_side_hmac()
    {
        // Same derivation as protocol §3 / theatre smoke test: hex(HMAC-SHA256(secret, screenId)).
        var secret = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(secret, "abc"u8.ToArray()));
        Assert.Equal(expected, DeviceIdentity.FromSecret(secret).TokenFor("abc"));
    }
}
