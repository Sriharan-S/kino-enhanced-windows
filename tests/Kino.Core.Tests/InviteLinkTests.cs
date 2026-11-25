namespace Kino.Core.Tests;

public class InviteLinkTests
{
    [Theory]
    [InlineData("kino://join/aurora/7K3QXM")]
    [InlineData("kino://join/aurora/7K3QXM/")]
    [InlineData("https://kino-directory.srie.workers.dev/join#aurora/7K3QXM")]
    [InlineData("https://kino-directory.srie.workers.dev/join/#aurora/7k3qxm")]
    [InlineData("  aurora/7k3qxm  ")]
    public void Parses_app_web_and_short_forms(string text)
    {
        var link = InviteLink.TryParse(text);
        Assert.NotNull(link);
        Assert.Equal("aurora", link.TheatreId);
        Assert.Equal("7K3QXM", link.Code);
    }

    [Fact]
    public void Normalises_look_alike_characters_like_the_theatre()
    {
        Assert.Equal("10A1B0", InviteLink.TryParse("kino://join/nova/IoaLbO")!.Code);
    }

    [Theory]
    [InlineData("kino://join/aurora/7K3QX")]       // too short
    [InlineData("kino://join/aurora/7K3QXMM")]     // too long
    [InlineData("kino://join/aurora/7K3QXU")]      // U is not in Crockford base32
    [InlineData("kino://join/9aurora/7K3QXM")]     // theatre ids start with a letter
    [InlineData("kino://watch/aurora/7K3QXM")]
    [InlineData("http://evil.example/join#aurora/7K3QXM")] // web form must be https
    [InlineData("kino://join/aurora/7K3QXM?x=<script>")]
    [InlineData("C:\\Videos\\movie.mkv")]
    [InlineData("")]
    public void Rejects_anything_else(string text) => Assert.Null(InviteLink.TryParse(text));

    [Fact]
    public void Builds_both_link_forms()
    {
        var link = new InviteLink("aurora", "7K3QXM");
        Assert.Equal("kino://join/aurora/7K3QXM", link.AppLink);
        Assert.Equal("https://kino-directory.srie.workers.dev/join#aurora/7K3QXM", link.WebLink);
        Assert.Equal(link, InviteLink.TryParse(link.WebLink));
    }
}
