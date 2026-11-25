using Kino.Core.Media;
using Kino.Core.Protocol;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Kino;

/// <summary>Display model for one playlist item, with this viewer's match status (§13.5) (x:Bind source).</summary>
public sealed class PlaylistRow
{
    public PlaylistRow(Item item, int index, bool isCurrent, LocalFile? myFile)
    {
        Item = item;
        MyFile = myFile;
        Title = $"{(isCurrent ? "▶ " : "")}{index + 1}. {item.Name}";
        Weight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal;

        string length = item.Duration is double d ? Format(d) : "";
        string sep = length.Length > 0 ? " · " : "";
        if (item.IsYouTube)
        {
            Detail = $"{length}{sep}YouTube: everyone streams it";
        }
        else if (!item.IsLocal)
        {
            Detail = $"{length}{sep}Link: everyone streams it";
        }
        else if (myFile is null)
        {
            Detail = $"{length}{sep}Pick your file";
            Warn = true;
        }
        else
        {
            Match = MediaMatch.Compare(item, myFile);
            Detail = Match switch
            {
                MatchKind.Match => $"{length}{sep}✓ {myFile.Name}",
                MatchKind.LikelyMatch => $"{length}{sep}✓ same length, different name: {myFile.Name}",
                _ => $"{length}{sep}⚠ length differs: yours {(myFile.Duration is double m ? Format(m) : "unknown")} ({myFile.Name})",
            };
            Warn = Match == MatchKind.Mismatch;
        }
        DetailBrush = Warn
            ? (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    public Item Item { get; }
    public LocalFile? MyFile { get; }
    public MatchKind? Match { get; }
    public bool Warn { get; }
    public string Title { get; }
    public string Detail { get; }
    public FontWeight Weight { get; }
    public Brush DetailBrush { get; }

    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}
