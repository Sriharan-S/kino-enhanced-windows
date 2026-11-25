using System.Diagnostics;
using System.Text.Json;
using Kino.Core.Media;
using Kino.Core.Sync;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Kino;

/// <summary>
/// YouTube items through the official IFrame Player API in a WebView2 (spec §13.6, §14.13), as an <see cref="IPlayback"/>
/// for the sync engine. The page lives on the directory site so it has a real https origin.
/// Engine calls arrive on its own thread and are marshalled to the UI thread.
/// </summary>
internal sealed class YouTubePlayer : IPlayback
{
    // YT.PlayerState
    private const int Unstarted = -1, Ended = 0, Playing = 1, Paused = 2, Buffering = 3, Cued = 5;

    private readonly WebView2 _view;
    private readonly DispatcherQueue _ui;
    private readonly Lock _lock = new();
    private Task? _init;
    private bool _pageReady;
    private string? _pendingVideo;

    private double _time, _duration;
    private long _timeAt;          // Stopwatch timestamp of _time
    private int _state = Unstarted;
    private bool _wantPlaying;
    private string? _videoId;

    /// <summary>The cued or playing video's id, once YouTube has accepted it.</summary>
    public event Action<string>? VideoReady;

    /// <summary>The video reached its end (state ENDED).</summary>
    public event Action<string>? VideoEnded;

    public event Action<string>? Error;

    public YouTubePlayer(WebView2 view, DispatcherQueue ui)
    {
        _view = view;
        _ui = ui;
    }

    public string? VideoId => _videoId;

    /// <summary>Creates the WebView2 (once) and opens the player page.</summary>
    private Task EnsureInitAsync() => _init ??= InitAsync();

    private async Task InitAsync()
    {
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kino", "WebView2");
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required" };
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, data, options);
        await _view.EnsureCoreWebView2Async(env);
        var core = _view.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.WebMessageReceived += OnMessage;
        core.NewWindowRequested += (_, e) => e.Handled = true; // no pop-ups from the embed
        core.Navigate(YouTube.PlayerPage.ToString());
    }

    /// <summary>Cues a video (paused at 0); the sync engine then seeks and starts it.</summary>
    public async void Load(string videoId)
    {
        lock (_lock)
        {
            _videoId = videoId;
            _state = Unstarted;
            _time = 0;
            _duration = 0;
            _timeAt = Stopwatch.GetTimestamp();
            _wantPlaying = false;
        }
        await EnsureInitAsync();
        if (_pageReady) Send(new { cmd = "load", id = videoId });
        else _pendingVideo = videoId;
    }

    public void Stop()
    {
        lock (_lock) _wantPlaying = false;
        if (_pageReady) Post(new { cmd = "stop" });
    }

    public void SetVolume(double percent) { if (_pageReady) Post(new { cmd = "volume", v = Math.Clamp(percent, 0, 100) }); }
    public void SetMuted(bool muted) { if (_pageReady) Post(new { cmd = "mute", on = muted }); }

    // ---- IPlayback ------------------------------------------------------------------------------

    public double? Position
    {
        get
        {
            lock (_lock)
            {
                if (_videoId is null || _state is Unstarted && _duration <= 0) return null;
                double t = _time;
                if (_state == Playing) t += Stopwatch.GetElapsedTime(_timeAt).TotalSeconds; // extrapolate between reports
                return _duration > 0 ? Math.Min(t, _duration) : t;
            }
        }
    }

    public double? MediaDuration { get { lock (_lock) return _duration > 0 ? _duration : null; } }

    public bool IsPaused { get { lock (_lock) return _state is Paused or Cued or Ended || (_state == Unstarted && !_wantPlaying); } }

    /// <summary>Buffering, or an ad / not started yet although we asked it to play.</summary>
    public bool IsBuffering { get { lock (_lock) return _state == Buffering || (_state == Unstarted && _wantPlaying); } }

    public void SetPaused(bool paused)
    {
        lock (_lock) _wantPlaying = !paused;
        Post(new { cmd = paused ? "pause" : "play" });
    }

    public void Seek(double seconds)
    {
        lock (_lock)
        {
            _time = Math.Max(0, seconds);
            _timeAt = Stopwatch.GetTimestamp();
        }
        Post(new { cmd = "seek", t = Math.Max(0, seconds) });
    }

    /// <summary>YouTube only has coarse rates, so the engine never bends speed for it (rate correction is off).</summary>
    public void SetSpeed(double speed) { }

    // ---- Messages ------------------------------------------------------------------------------

    private void Post(object command) => _ui.TryEnqueue(() => Send(command));

    private void Send(object command)
    {
        if (_view.CoreWebView2 is { } core) core.PostWebMessageAsJson(JsonSerializer.Serialize(command));
    }

    private void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        // Only from our own player page.
        if (!args.Source.StartsWith(YouTube.PlayerPage.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)) return;
        using var doc = JsonDocument.Parse(args.WebMessageAsJson);
        var m = doc.RootElement;
        switch (m.GetProperty("type").GetString())
        {
            case "ready":
                _pageReady = true;
                if (_pendingVideo is { } id) { _pendingVideo = null; Send(new { cmd = "load", id }); }
                break;
            case "time":
                lock (_lock)
                {
                    _time = m.GetProperty("t").GetDouble();
                    _duration = m.GetProperty("d").GetDouble();
                    _state = m.GetProperty("s").GetInt32();
                    _timeAt = Stopwatch.GetTimestamp();
                }
                break;
            case "state":
                int state = m.GetProperty("state").GetInt32();
                string? video;
                lock (_lock)
                {
                    _state = state;
                    video = _videoId;
                }
                if (video is null) break;
                if (state is Cued or Playing or Paused) VideoReady?.Invoke(video);
                if (state == Ended) VideoEnded?.Invoke(video);
                break;
            case "error":
                // 2: bad id, 5: HTML5 error, 100: not found/private, 101/150: embedding disabled by the owner.
                int code = m.GetProperty("code").GetInt32();
                Error?.Invoke(code is 101 or 150 ? "The owner of this YouTube video doesn't allow it to be played in other apps."
                    : code == 100 ? "This YouTube video is private or no longer available."
                    : $"YouTube couldn't play this video (error {code}).");
                break;
        }
    }
}
