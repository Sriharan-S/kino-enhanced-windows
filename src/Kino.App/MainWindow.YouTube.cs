using Kino.Core;
using Kino.Core.Media;
using Kino.Core.Protocol;
using Kino.Core.Sync;
using Microsoft.UI.Xaml;

namespace Kino;

/// <summary>YouTube items (§13.6, §14.13): shown in a WebView2 over the video area while they're current.</summary>
public sealed partial class MainWindow
{
    private YouTubePlayer? _yt;
    private bool _ytActive;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _ytUiTimer;

    private YouTubePlayer YouTubePlayer
    {
        get
        {
            if (_yt is not null) return _yt;
            _yt = new YouTubePlayer(YouTubeView, _ui);
            _yt.VideoReady += id => _ui.TryEnqueue(() => OnYouTubeReady(id));
            _yt.VideoEnded += id => _ui.TryEnqueue(() => OnYouTubeEnded(id));
            _yt.Error += msg => _ui.TryEnqueue(() => OverlayText.Text = msg);
            _yt.SetVolume(Math.Min(100, VolumeSlider.Value));
            return _yt;
        }
    }

    /// <summary>Switches the stage (and the engine's player) between libmpv and YouTube.</summary>
    private void ShowYouTube(bool on)
    {
        if (on == _ytActive) return;
        _ytActive = on;
        YouTubeView.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _videoHost.Visible = !on && _page == Page.Video;
        if (on)
        {
            _player.SetPaused(true);
            _switch.Active = YouTubePlayer;
            _ytUiTimer ??= CreateYouTubeUiTimer();
            _ytUiTimer.Start();
        }
        else
        {
            _yt?.Stop();
            _switch.Active = _player;
            _ytUiTimer?.Stop();
        }
    }

    /// <summary>libmpv drives the time display through its events; for YouTube, poll the player instead.</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateYouTubeUiTimer()
    {
        var t = _ui.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(500);
        t.Tick += (_, _) =>
        {
            if (_yt is null) return;
            if (_yt.MediaDuration is double d) { _duration = d; SeekSlider.Maximum = Math.Max(1, d); }
            if (_yt.Position is double p) OnPosition(p);
        };
        return t;
    }

    /// <summary>Loads the current YouTube item (cued, paused); the engine starts it once YouTube accepts it.</summary>
    private void EnsureYouTubeLoaded(ScreenSession s, Item item)
    {
        ShowYouTube(true);
        s.Engine.Settings = SyncSettings.YouTube; // no rate correction; seek past 1.5 s
        _loadedFileName = item.Name;
        if (YouTube.VideoId(item.Url) is not { } id)
        {
            OverlayText.Text = "This YouTube link isn't valid.";
            s.Engine.LoadedItemId = null;
            return;
        }
        if (YouTubePlayer.VideoId == id && _loadingItemId != item.Id && s.Engine.LoadedItemId == item.Id) return;
        if (_loadingItemId == item.Id) return;
        _loadingItemId = item.Id;
        _loadingPath = null;
        s.Engine.LoadedItemId = null;
        YouTubePlayer.Load(id);
    }

    private void OnYouTubeReady(string videoId)
    {
        if (_session is not { CurrentItem: { IsYouTube: true } item } s || YouTube.VideoId(item.Url) != videoId) return;
        if (_loadingItemId == item.Id) _loadingItemId = null;
        s.Engine.LoadedItemId = item.Id;
    }

    /// <summary>The screen moves on only when everyone has finished (§14.13).</summary>
    private async void OnYouTubeEnded(string videoId)
    {
        if (_session is { CurrentItem: { IsYouTube: true } item } s && YouTube.VideoId(item.Url) == videoId)
            await s.ReportEndedAsync(item.Id);
    }

    /// <summary>A title for a pasted YouTube link (oEmbed, no API key), or a fallback.</summary>
    private static async Task<string> YouTubeTitleAsync(string videoId)
    {
        try
        {
            var url = $"https://www.youtube.com/oembed?format=json&url={Uri.EscapeDataString(YouTube.WatchUrl(videoId))}";
            using var doc = System.Text.Json.JsonDocument.Parse(await Http.GetStringAsync(url));
            if (doc.RootElement.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } title)
                return title.Length > 200 ? title[..200] : title;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { }
        return $"YouTube video {videoId}";
    }
}
