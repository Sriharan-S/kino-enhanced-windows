using Kino.Core.Media;
using Kino.Core.Net;

namespace Kino.Core.Tests;

public class SubtitleTests
{
    [Fact]
    public void Style_maps_to_libmpv_properties()
    {
        var props = new SubtitleStyle { Size = 50, Colour = "#FFE34D", Outline = 2 }.MpvProperties(offsetMs: -250).ToDictionary();
        Assert.Equal("-0.25", props["sub-delay"]);
        Assert.Equal("50", props["sub-font-size"]);
        Assert.Equal("#FFE34D", props["sub-color"]);
        Assert.Equal("outline-and-shadow", props["sub-border-style"]);
        Assert.Equal("#00000000", props["sub-back-color"]);

        var boxed = new SubtitleStyle { Background = true, BackgroundOpacity = 0.5 }.MpvProperties(0).ToDictionary();
        Assert.Equal("background-box", boxed["sub-border-style"]);
        Assert.Equal("#80000000", boxed["sub-back-color"]);
    }

    [Fact]
    public void Style_is_clamped()
    {
        var s = new SubtitleStyle { Size = 500, Outline = -3, Position = 140, Font = " " }.Clamped();
        Assert.Equal(90, s.Size);
        Assert.Equal(0, s.Outline);
        Assert.Equal(100, s.Position);
        Assert.Equal("Segoe UI", s.Font);
    }

    [Theory]
    [InlineData(@"C:\subs\EP 01.en.SRT", "srt")]
    [InlineData("x.vtt", "vtt")]
    [InlineData("x.ass", "ass")]
    [InlineData("x.txt", null)]
    public void Subtitle_formats_come_from_the_extension(string path, string? format) =>
        Assert.Equal(format, ScreenSession.SubtitleFormatOf(path));

    [Fact]
    public void Gzip_round_trips()
    {
        byte[] data = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("1\n00:00:01,000 --> 00:00:02,000\nHello\n\n", 200)));
        byte[] gz = TheatreApi.Gzip(data);
        Assert.True(gz.Length < data.Length / 5);
        using var back = new System.IO.Compression.GZipStream(new MemoryStream(gz), System.IO.Compression.CompressionMode.Decompress);
        using var ms = new MemoryStream();
        back.CopyTo(ms);
        Assert.Equal(data, ms.ToArray());
    }
}
