using System.Globalization;
using System.Text;
using Kino.Core.Protocol;

namespace Kino.Core.Media;

/// <summary>A media file on this device. Its path never leaves the device; only name and duration are shared (§13.2).</summary>
public sealed record LocalFile(string Path, double? Duration)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>How a viewer's file compares with the host's (§13.5). The host's file is the reference.</summary>
public enum MatchKind
{
    /// <summary>Duration within tolerance and the same name.</summary>
    Match,

    /// <summary>Duration within tolerance, different name: almost certainly the same video.</summary>
    LikelyMatch,

    /// <summary>Duration differs. Shown as a warning; never blocks playback.</summary>
    Mismatch,
}

/// <summary>File matching for local playlist items: natural sort, the §13.5 check, and auto-mapping (§13.4).</summary>
public static class MediaMatch
{
    /// <summary>Media extensions offered when adding a folder (libmpv plays far more; this keeps folders tidy).</summary>
    public static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".webm", ".wmv", ".flv", ".ts", ".m2ts", ".mts", ".mpg", ".mpeg",
        ".ogv", ".3gp", ".vob", ".divx", ".rmvb", ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wav",
    };

    /// <summary>Same video if durations differ by at most max(2 s, 0.5% of the host's duration).</summary>
    public static double Tolerance(double hostDuration) => Math.Max(2.0, hostDuration * 0.005);

    public static bool DurationMatches(double? host, double? local) =>
        host is double h && local is double l && Math.Abs(h - l) <= Tolerance(h);

    /// <summary>Ignores case and extension, and treats runs of whitespace, '.', '_' and '-' as one space.</summary>
    public static string NormaliseName(string name)
    {
        string stem = Path.GetExtension(name) is { Length: > 1 and <= 6 } ext && !ext.Contains(' ') ? name[..^ext.Length] : name;
        var sb = new StringBuilder(stem.Length);
        bool gap = false;
        foreach (char c in stem.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c) || c is '.' or '_' or '-')
            {
                gap = sb.Length > 0;
                continue;
            }
            if (gap) sb.Append(' ');
            gap = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static bool NameMatches(string a, string b) => NormaliseName(a) == NormaliseName(b);

    /// <summary>Duration first, then name. An unknown duration on either side counts as a mismatch only if names differ.</summary>
    public static MatchKind Compare(Item hostItem, LocalFile file)
    {
        bool nameOk = NameMatches(hostItem.Name, file.Name);
        if (hostItem.Duration is null || file.Duration is null) return nameOk ? MatchKind.Match : MatchKind.Mismatch;
        bool durationOk = DurationMatches(hostItem.Duration, file.Duration);
        return durationOk ? (nameOk ? MatchKind.Match : MatchKind.LikelyMatch) : MatchKind.Mismatch;
    }

    /// <summary>
    /// Maps local playlist items to candidate files: duration first, then name (§13.4). Each file is used once. Items
    /// already in <paramref name="existing"/> keep their mapping. Items with no acceptable file stay unmapped.
    /// </summary>
    public static Dictionary<string, LocalFile> AutoMap(IEnumerable<Item> items, IReadOnlyCollection<LocalFile> candidates,
                                                        IReadOnlyDictionary<string, LocalFile>? existing = null)
    {
        var map = new Dictionary<string, LocalFile>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var local = items.Where(i => i.IsLocal).ToList();

        foreach (var item in local)
        {
            if (existing is not null && existing.TryGetValue(item.Id, out var kept))
            {
                map[item.Id] = kept;
                used.Add(kept.Path);
            }
        }

        // Best matches first, so a weaker match can't take a file that is a perfect match for a later item.
        var pairs =
            from item in local
            where !map.ContainsKey(item.Id)
            from file in candidates
            let score = Score(item, file)
            where score > 0
            orderby score descending, DurationGap(item, file)
            select (item, file);

        foreach (var (item, file) in pairs)
        {
            if (map.ContainsKey(item.Id) || used.Contains(file.Path)) continue;
            map[item.Id] = file;
            used.Add(file.Path);
        }
        return map;
    }

    /// <summary>3 = duration and name, 2 = duration only, 1 = name only (shown as a mismatch), 0 = no match.</summary>
    private static int Score(Item item, LocalFile file)
    {
        bool name = NameMatches(item.Name, file.Name);
        bool duration = DurationMatches(item.Duration, file.Duration);
        return duration && name ? 3 : duration ? 2 : name ? 1 : 0;
    }

    private static double DurationGap(Item item, LocalFile file) =>
        item.Duration is double h && file.Duration is double l ? Math.Abs(h - l) : double.MaxValue;

    /// <summary>A–Z with numbers compared by value ("EP 2" before "EP 10"), ignoring case (§13.3).</summary>
    public static int NaturalCompare(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var da = a.AsSpan(si, i - si).TrimStart('0');
                var db = b.AsSpan(sj, j - sj).TrimStart('0');
                if (da.Length != db.Length) return da.Length.CompareTo(db.Length);
                int c = da.CompareTo(db, StringComparison.Ordinal);
                if (c != 0) return c;
                continue;
            }
            int cc = string.Compare(a, i, b, j, 1, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
            if (cc != 0) return cc;
            i++;
            j++;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    public static readonly IComparer<string?> NaturalComparer = Comparer<string?>.Create(NaturalCompare);
}
