using System.Text.RegularExpressions;
using Kino.Core.Sync;

namespace Kino.Core.Media;

/// <summary>YouTube links (spec §13.6). Same rules as the theatre: watch, shorts, embed, live and youtu.be links, or a bare id.</summary>
public static partial class YouTube
{
    /// <summary>The 11-character video id, or null if this isn't a YouTube video link.</summary>
    public static string? VideoId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim();
        if (BareId().IsMatch(t)) return t;
        var m = Link().Match(t);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string WatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    /// <summary>The page that hosts the IFrame player inside the app (a real https origin, which embeds require).</summary>
    public static readonly Uri PlayerPage = new("https://kino-directory.srie.workers.dev/yt");

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex BareId();

    [GeneratedRegex(@"^https://(?:www\.|m\.|music\.)?(?:youtube\.com/(?:watch\?(?:.*&)?v=|shorts/|embed/|live/)|youtu\.be/)([A-Za-z0-9_-]{11})(?:[?&#/].*)?$")]
    private static partial Regex Link();
}

/// <summary>
/// The player the sync engine drives: libmpv for files and links, the YouTube player for YouTube items.
/// The app switches <see cref="Active"/> when the current item's kind changes.
/// </summary>
public sealed class PlaybackSwitch(IPlayback initial) : IPlayback
{
    private volatile IPlayback _active = initial;

    public IPlayback Active
    {
        get => _active;
        set => _active = value;
    }

    public double? Position => _active.Position;
    public double? MediaDuration => _active.MediaDuration;
    public bool IsPaused => _active.IsPaused;
    public bool IsBuffering => _active.IsBuffering;
    public void SetPaused(bool paused) => _active.SetPaused(paused);
    public void Seek(double seconds) => _active.Seek(seconds);
    public void SetSpeed(double speed) => _active.SetSpeed(speed);
}
