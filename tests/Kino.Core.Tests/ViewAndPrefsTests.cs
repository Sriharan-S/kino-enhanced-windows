using Kino.Core.Media;

namespace Kino.Core.Tests;

public class ViewMathTests
{
    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(0.5, -1.0)]
    [InlineData(4.0, 2.0)]
    [InlineData(9.0, 2.0)] // clamped to 400%
    public void Zoom_is_log2_scaled_and_clamped(double zoom, double mpv) => Assert.Equal(mpv, ViewMath.MpvZoom(zoom), 6);

    [Fact]
    public void Fit_width_and_height_become_letterbox_or_crop()
    {
        const double wide = 2.39, tv = 16.0 / 9, square = 1.0;
        Assert.Equal((true, 0.0), ViewMath.FitProperties(FitMode.FitWidth, wide, tv));   // already as wide: letterbox
        Assert.Equal((true, 1.0), ViewMath.FitProperties(FitMode.FitWidth, square, tv)); // narrower: crop top/bottom
        Assert.Equal((true, 1.0), ViewMath.FitProperties(FitMode.FitHeight, wide, tv));  // wider: crop the sides
        Assert.Equal((true, 0.0), ViewMath.FitProperties(FitMode.FitHeight, square, tv));
        Assert.Equal((false, 0.0), ViewMath.FitProperties(FitMode.Stretch, wide, tv));
        Assert.Equal((true, 1.0), ViewMath.FitProperties(FitMode.Fill, wide, tv));
        Assert.Equal((true, 0.0), ViewMath.FitProperties(FitMode.Fit, wide, tv));
    }

    [Fact]
    public void Settings_are_clamped()
    {
        var v = new ViewSettings { Zoom = 10, PanX = 3, Aspect = "7:5", SyncOffsetMs = -5000 }.Clamped();
        Assert.Equal(4.0, v.Zoom);
        Assert.Equal(1.0, v.PanX);
        Assert.Equal("original", v.Aspect);
        Assert.Equal(-2000, v.SyncOffsetMs);
        Assert.Equal("-1", ViewMath.MpvAspect("original"));
        Assert.Equal("2.35:1", ViewMath.MpvAspect("2.35:1"));
    }
}

public class LocalPrefsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resume_skips_the_start_and_forgets_finished_files()
    {
        var p = new LocalPrefs();
        p.SetResume("a", 5, 1000, T0);
        Assert.Null(p.ResumeAt("a"));          // too close to the start
        p.SetResume("a", 400, 1000, T0);
        Assert.Equal(400, p.ResumeAt("a"));
        p.SetResume("a", 990, 1000, T0);       // within 15 s of the end: finished
        Assert.Null(p.ResumeAt("a"));
        p.SetResume("b", 400, null, T0);       // unknown length still resumes
        Assert.Equal(400, p.ResumeAt("b"));
    }

    [Fact]
    public void Resume_keeps_only_the_newest_entries()
    {
        var p = new LocalPrefs();
        for (int i = 0; i < LocalPrefs.MaxResumeEntries + 20; i++) p.SetResume($"f{i}", 100, 1000, T0.AddMinutes(i));
        Assert.Equal(LocalPrefs.MaxResumeEntries, p.Resume.Count);
        Assert.Null(p.ResumeAt("f0"));
        Assert.Equal(100, p.ResumeAt($"f{LocalPrefs.MaxResumeEntries + 19}"));
    }

    [Fact]
    public void Per_file_overrides_win_over_defaults_and_round_trip()
    {
        var p = new LocalPrefs { Defaults = new ViewSettings { Zoom = 1.5 } };
        p.PerFile["x"] = new ViewSettings { Fit = FitMode.Fill };
        Assert.Equal(1.5, p.ViewFor("y").Zoom);
        Assert.Equal(FitMode.Fill, p.ViewFor("x").Fit);
        Assert.True(p.HasOverride("x"));

        string file = Path.Combine(Path.GetTempPath(), $"kino-prefs-{Guid.NewGuid():N}.json");
        try
        {
            p.SetResume("x", 123.4, 600, T0);
            p.BoostAccepted = true;
            p.Save(file);
            var back = LocalPrefs.Load(file);
            Assert.Equal(FitMode.Fill, back.ViewFor("x").Fit);
            Assert.Equal(123.4, back.ResumeAt("x"));
            Assert.True(back.BoostAccepted);
        }
        finally { File.Delete(file); }
    }
}
