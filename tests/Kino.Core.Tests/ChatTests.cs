using Kino.Core.Chat;
using Kino.Core.Protocol;

namespace Kino.Core.Tests;

public class ChatTests
{
    private const string GiphyResponse = """
        { "data": [
            { "title": "Popcorn GIF", "images": {
                "fixed_height": { "url": "https://media2.giphy.com/media/abc/200.gif", "width": "356", "height": "200" },
                "fixed_width_small": { "url": "https://media2.giphy.com/media/abc/100w.gif", "width": "100", "height": "56" } } },
            { "title": "", "images": { "original": { "url": "https://media.giphy.com/media/def/giphy.gif", "width": "480", "height": "270" },
                                         "fixed_width": { "url": "https://media.giphy.com/media/def/200w.gif" } } },
            { "title": "broken", "images": {} }
          ], "pagination": { "count": 3 } }
        """;

    [Fact]
    public async Task Giphy_results_give_a_preview_and_the_gif_to_send()
    {
        var http = new HttpClient(new StubHandler(req =>
        {
            Assert.Contains("api_key=KEY", req.RequestUri!.Query);
            Assert.Contains("q=popcorn%20time", req.RequestUri.Query);
            return Task.FromResult(StubHandler.Json(GiphyResponse));
        }));
        var results = await new GiphyClient("KEY", http).SearchAsync("popcorn time");

        Assert.Equal(2, results.Count); // the one without images is skipped
        Assert.Equal("https://media2.giphy.com/media/abc/100w.gif", results[0].PreviewUrl);
        Assert.Equal(new Gif("https://media2.giphy.com/media/abc/200.gif", 356, 200, "Popcorn GIF"), results[0].Gif);
        Assert.Null(results[1].Gif.Title);
        Assert.All(results, r => Assert.True(GiphyClient.IsGifLink(r.Gif.Url)));
    }

    [Theory]
    [InlineData("https://media.giphy.com/media/abc/giphy.gif", true)]
    [InlineData("https://media4.giphy.com/media/abc/giphy.gif", true)]
    [InlineData("https://media.tenor.com/x/tenor.gif", true)]
    [InlineData("http://media.giphy.com/media/abc/giphy.gif", false)]
    [InlineData("https://media.giphy.com.evil.example/x.gif", false)]
    [InlineData("https://giphy.com/gifs/abc", false)] // the web page, not the media
    public void Gif_links_match_the_theatre_allow_list(string url, bool ok) => Assert.Equal(ok, GiphyClient.IsGifLink(url));

    [Fact]
    public void Chat_and_reaction_messages_parse()
    {
        var chat = Assert.IsType<ServerMessage.ChatMsg>(ServerMessage.Parse(
            """{"type":"chat","data":{"id":7,"at":1,"kind":"gif","from":"0f9e8d7c6b5a","name":"Bob","text":null,"gif":{"url":"https://media.giphy.com/x.gif","width":480,"height":270,"title":null}}}""")).Data;
        Assert.Equal("gif", chat.Kind);
        Assert.Equal(480, chat.Gif!.Width);
        var react = Assert.IsType<ServerMessage.ReactMsg>(ServerMessage.Parse("""{"type":"react","data":{"from":"a","name":"A","emoji":"🎉"}}""")).Data;
        Assert.Equal("🎉", react.Emoji);
    }
}
