namespace Kino.Core.Media;

/// <summary>How the picture fills the video area (spec §16).</summary>
public enum FitMode { Fit, FitWidth, FitHeight, Stretch, Fill }

/// <summary>
/// Local-only picture and sound preferences (spec §16). Never part of any theatre message.
/// </summary>
public sealed record ViewSettings
{
    /// <summary>1.0 = 100%. Range 0.5–4.0.</summary>
    public double Zoom { get; init; } = 1.0;

    /// <summary>Pan as a fraction of the picture size, −1..1 (0 = centred).</summary>
    public double PanX { get; init; }
    public double PanY { get; init; }

    /// <summary>"original" or a ratio such as "16:9", "4:3", "21:9", "2.35:1", "1:1".</summary>
    public string Aspect { get; init; } = "original";

    public FitMode Fit { get; init; } = FitMode.Fit;

    /// <summary>Personal sync offset in ms (e.g. Bluetooth delay), ±2000. Positive = play later.</summary>
    public double SyncOffsetMs { get; init; }

    public static readonly string[] Aspects = ["original", "16:9", "4:3", "21:9", "2.35:1", "1:1"];
    public const double MinZoom = 0.5, MaxZoom = 4.0, MaxOffsetMs = 2000;

    public ViewSettings Clamped() => this with
    {
        Zoom = Math.Clamp(Zoom, MinZoom, MaxZoom),
        PanX = Math.Clamp(PanX, -1, 1),
        PanY = Math.Clamp(PanY, -1, 1),
        Aspect = Aspects.Contains(Aspect) ? Aspect : "original",
        SyncOffsetMs = Math.Clamp(SyncOffsetMs, -MaxOffsetMs, MaxOffsetMs),
    };
}

/// <summary>Maps <see cref="ViewSettings"/> onto libmpv's video properties (spec §15.3). Pure, so it is unit tested.</summary>
public static class ViewMath
{
    /// <summary>libmpv's <c>video-zoom</c> is log2-scaled: 0 = 100%, 1 = 200%, −1 = 50%.</summary>
    public static double MpvZoom(double zoom) => Math.Log2(Math.Clamp(zoom, ViewSettings.MinZoom, ViewSettings.MaxZoom));

    /// <summary>
    /// Fit width / fit height reduce to letterboxing or cropping, depending on whether the picture is wider than the window.
    /// Fit width: if the picture is already relatively wider, fitting already matches the width; otherwise fill (crop).
    /// </summary>
    public static (bool KeepAspect, double Panscan) FitProperties(FitMode fit, double videoAspect, double windowAspect)
    {
        bool wider = videoAspect >= windowAspect;
        return fit switch
        {
            FitMode.Stretch => (false, 0),
            FitMode.Fill => (true, 1),
            FitMode.FitWidth => (true, wider ? 0 : 1),
            FitMode.FitHeight => (true, wider ? 1 : 0),
            _ => (true, 0),
        };
    }

    /// <summary>"original" → "-1" (no override); otherwise the ratio as libmpv accepts it.</summary>
    public static string MpvAspect(string aspect) => aspect == "original" ? "-1" : aspect;
}
