using System.Text.Json;

namespace Kino.Core.Tests;

public class UpdatesTests
{
    [Theory]
    [InlineData("0.1.0", "0.2.0", -1)]
    [InlineData("v1.0.0", "0.9.9", 1)]
    [InlineData("1.0.0-beta.1", "1.0.0", -1)]     // a release beats its pre-releases
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10", -1)] // numeric identifiers compare as numbers
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.1.1", -1)]
    [InlineData("1.2.3+abc", "1.2.3", 0)]           // build metadata is ignored
    [InlineData("garbage", "0.0.1", -1)]
    public void SemVer_precedence(string a, string b, int expected) => Assert.Equal(expected, Math.Sign(SemVer.Compare(a, b)));

    private static Updates.Release? Check(string json, string current) =>
        Updates.Parse(JsonDocument.Parse(json).RootElement, current);

    [Fact]
    public void Only_newer_stable_releases_count()
    {
        const string page = "https://github.com/Sriharan-S/kino-enhanced-windows/releases/tag/v0.2.0";
        var newer = Check($$"""{"tag_name":"v0.2.0","html_url":"{{page}}","draft":false,"prerelease":false}""", "0.1.0");
        Assert.Equal("0.2.0", newer?.Version);
        Assert.Equal(page, newer?.Page.ToString());

        Assert.Null(Check("""{"tag_name":"v0.1.0","draft":false,"prerelease":false}""", "0.1.0"));     // same
        Assert.Null(Check("""{"tag_name":"v0.0.9","draft":false,"prerelease":false}""", "0.1.0"));     // older
        Assert.Null(Check("""{"tag_name":"v0.3.0-beta.1","draft":false,"prerelease":true}""", "0.1.0")); // pre-release
        Assert.Null(Check("""{"tag_name":"nightly","draft":false,"prerelease":false}""", "0.1.0"));    // not a version
    }

    [Fact]
    public async Task Network_problems_mean_no_update_rather_than_an_error()
    {
        var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));
        Assert.Null(await Updates.NewerReleaseAsync(http, "0.1.0"));
        var notFound = new HttpClient(new StubHandler(_ => Task.FromResult(StubHandler.Json("{}", System.Net.HttpStatusCode.NotFound))));
        Assert.Null(await Updates.NewerReleaseAsync(notFound, "0.1.0")); // before the first release
    }
}
