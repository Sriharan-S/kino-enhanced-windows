using System.Globalization;

namespace Kino.Core.Media;

/// <summary>Local-only subtitle look (spec §17.4). Applied to libmpv's <c>sub-*</c> properties.</summary>
public sealed record SubtitleStyle
{
    public string Font { get; init; } = "Segoe UI";

    /// <summary>libmpv's <c>sub-font-size</c> (scaled to a 720-line video). 20–90.</summary>
    public double Size { get; init; } = 46;

    /// <summary>"#RRGGBB".</summary>
    public string Colour { get; init; } = "#FFFFFF";
    public string OutlineColour { get; init; } = "#000000";

    /// <summary>Outline width, 0–8.</summary>
    public double Outline { get; init; } = 3;

    /// <summary>Draw a box behind the text instead of an outline.</summary>
    public bool Background { get; init; }

    /// <summary>Background box opacity, 0–1.</summary>
    public double BackgroundOpacity { get; init; } = 0.6;

    /// <summary>Vertical position: 100 = bottom of the picture, 0 = top.</summary>
    public double Position { get; init; } = 100;

    public static readonly string[] Fonts = ["Segoe UI", "Arial", "Verdana", "Tahoma", "Georgia", "Consolas", "Comic Sans MS"];
    public static readonly (string Label, string Hex)[] Colours =
        [("White", "#FFFFFF"), ("Yellow", "#FFE34D"), ("Cyan", "#7FE8FF"), ("Green", "#8CF08C"), ("Pink", "#FF9EDB")];

    public SubtitleStyle Clamped() => this with
    {
        Size = Math.Clamp(Size, 20, 90),
        Outline = Math.Clamp(Outline, 0, 8),
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0, 1),
        Position = Math.Clamp(Position, 0, 100),
        Font = string.IsNullOrWhiteSpace(Font) ? "Segoe UI" : Font,
    };

    /// <summary>The libmpv properties for this style, plus the timing offset (ms, positive = later).</summary>
    public IReadOnlyList<(string Name, string Value)> MpvProperties(double offsetMs)
    {
        var s = Clamped();
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string alpha = ((int)Math.Round(s.BackgroundOpacity * 255)).ToString("X2", CultureInfo.InvariantCulture);
        return
        [
            ("sub-delay", F(offsetMs / 1000.0)),
            ("sub-font", s.Font),
            ("sub-font-size", F(s.Size)),
            ("sub-color", s.Colour),
            ("sub-border-color", s.Background ? $"#{alpha}000000" : s.OutlineColour),
            ("sub-border-size", F(s.Background ? Math.Max(2, s.Outline) : s.Outline)),
            ("sub-back-color", s.Background ? $"#{alpha}000000" : "#00000000"),
            ("sub-border-style", s.Background ? "background-box" : "outline-and-shadow"),
            ("sub-pos", F(s.Position)),
        ];
    }
}
