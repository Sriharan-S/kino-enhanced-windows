using System.Diagnostics;
using System.Globalization;
using Kino.Core;
using Kino.Core.Identity;
using Kino.Core.Net;
using Kino.Core.Protocol;
using Kino.Core.Sync;
using Kino.Player;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Storage.Pickers;

namespace Kino;

public sealed partial class MainWindow : Window
{
    private readonly LaunchOptions _opt;
    private readonly IntPtr _hwnd;
    private readonly VideoHost _videoHost;
    private readonly MpvPlayer _player;
    private readonly DispatcherQueue _ui;
    private readonly DeviceIdentity _identity;

    private ScreenSession? _session;
    private string? _loadedFileName;
    private bool _announcePending;
    private bool _paused = true;
    private double _duration;
    private bool _seekDragging;

    private StreamWriter? _syncLog;
    private double? _firstInSyncAt;
    private bool _perturbed;

    public MainWindow(LaunchOptions options)
    {
        _opt = options;
        InitializeComponent();
        _ui = DispatcherQueue.GetForCurrentThread();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _identity = DeviceIdentity.LoadOrCreate(options.Profile);

        if (options.Window is [int x, int y, int w, int h])
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
        else
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        Title = options.Profile == "default" ? "Kino" : $"Kino — {options.Profile}";

        TheatreBox.Text = options.Theatre;
        NameBox.Text = options.Name;
        VolumeSlider.Value = options.Volume;

        _videoHost = new VideoHost(_hwnd);
        _player = new MpvPlayer(_videoHost.Hwnd);
        _player.SetVolume(options.Volume); // the slider fired before the player existed

        _player.PositionChanged += pos => _ui.TryEnqueue(() => OnPosition(pos));
        _player.DurationChanged += d => _ui.TryEnqueue(() => { _duration = d; SeekSlider.Maximum = Math.Max(1, d); TryAnnounceItem(); });
        _player.PausedChanged += p => _ui.TryEnqueue(() => { _paused = p; PlayPauseButton.Content = p ? "Play" : "Pause"; });
        _player.FileLoaded += _ => _ui.TryEnqueue(TryAnnounceItem);
        _player.Error += msg => _ui.TryEnqueue(() => OverlayText.Text = msg);

        SeekSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _seekDragging = true), true);

        VideoArea.SizeChanged += (_, _) => LayoutOverlays();
        Root.Loaded += async (_, _) =>
        {
            LayoutOverlays();
            OverlayPopup.IsOpen = OverlayToggle.IsChecked == true;
            await AutoStartAsync();
        };
        Closed += async (_, _) =>
        {
            if (_session is not null) await _session.DisposeAsync();
            _syncLog?.Dispose();
            _player.Dispose();
            _videoHost.Dispose();
        };

        if (options.SyncLog is not null)
        {
            _syncLog = new StreamWriter(options.SyncLog, append: false) { AutoFlush = true };
            _syncLog.WriteLine("qpc_ms,position,drift,rate,zone,anchor_seq");
        }
    }

    // ---- Automation (command line) ------------------------------------------------------

    private async Task AutoStartAsync()
    {
        if (_opt.Media is not null) OpenMedia(_opt.Media);
        if (_opt.Create is not null) await CreateScreenAsync(_opt.Create);
        else if (_opt.Join is not null) await JoinScreenAsync(_opt.Join);
    }

    // ---- Screen create / join -----------------------------------------------------------

    private async void OnCreateClick(object sender, RoutedEventArgs e) => await CreateScreenAsync("Kino screen");

    private async void OnJoinClick(object sender, RoutedEventArgs e) => await JoinScreenAsync(CodeBox.Text);

    private async Task CreateScreenAsync(string screenName)
    {
        await RunSessionAction(async () =>
        {
            var api = new TheatreApi(new Uri(TheatreBox.Text.Trim()));
            var (session, created) = await ScreenSession.CreateAsync(api, _identity, _player,
                new CreateScreenRequest(screenName, "public"), DisplayName());
            if (_opt.CodeFile is not null) await File.WriteAllTextAsync(_opt.CodeFile, created.Code);
            return session;
        });
    }

    private async Task JoinScreenAsync(string code)
    {
        await RunSessionAction(async () =>
        {
            var api = new TheatreApi(new Uri(TheatreBox.Text.Trim()));
            return await ScreenSession.JoinByCodeAsync(api, _identity, _player, code.Trim(), DisplayName());
        });
    }

    private async Task RunSessionAction(Func<Task<ScreenSession>> action)
    {
        CreateButton.IsEnabled = JoinButton.IsEnabled = false;
        try
        {
            if (_session is not null) { await _session.DisposeAsync(); _session = null; }
            SessionText.Text = "Connecting…";
            var s = await action();
            AttachSession(s);
        }
        catch (Exception ex)
        {
            SessionText.Text = $"Failed: {ex.Message}";
            CreateButton.IsEnabled = JoinButton.IsEnabled = true;
        }
    }

    private string DisplayName() => string.IsNullOrWhiteSpace(NameBox.Text) ? "Viewer" : NameBox.Text.Trim();

    private void AttachSession(ScreenSession s)
    {
        _session = s;
        CodeBox.Text = s.Code;
        UpdateSessionText();
        PlayPauseButton.IsEnabled = SeekSlider.IsEnabled = s.IsHost;

        s.StatusChanged += st => _ui.TryEnqueue(() => OnSyncStatus(st));
        s.ViewersChanged += _ => _ui.TryEnqueue(UpdateSessionText);
        s.ItemChanged += item => _ui.TryEnqueue(() => OnItemChanged(item));
        s.Error += err => _ui.TryEnqueue(() => OverlayText.Text = $"{err.Code}: {err.Message}");
        s.Closed += reason => _ui.TryEnqueue(() =>
        {
            SessionText.Text = $"Disconnected ({reason})";
            CreateButton.IsEnabled = JoinButton.IsEnabled = true;
            PlayPauseButton.IsEnabled = SeekSlider.IsEnabled = true;
        });

        OnItemChanged(s.CurrentItem);
        _announcePending = s.IsHost;
        TryAnnounceItem();
    }

    private void UpdateSessionText()
    {
        if (_session is null) return;
        string role = _session.IsHost ? "Host" : "Viewer";
        int count = _session.Viewers.Count;
        SessionText.Text = $"{_session.Joined!.Screen.Name} · code {_session.Code} · {role} · {count} viewer{(count == 1 ? "" : "s")}";
    }

    private void OnItemChanged(Item? item)
    {
        if (_session is null || _session.IsHost || item is null) return;
        OverlayTitle.Text = $"Host is playing: {item.Name}";
        if (_loadedFileName is null) OverlayText.Text = "Open your copy of this file";
    }

    /// <summary>Host: once a file is loaded and its duration known, tell the screen about it.</summary>
    private async void TryAnnounceItem()
    {
        if (_session is null || !_session.IsHost || !_announcePending || _loadedFileName is null || !(_duration > 0)) return;
        _announcePending = false;
        await _session.LoadLocalAsync(_loadedFileName, Math.Round(_duration, 3));
        if (_opt.AutoPlay)
        {
            await Task.Delay(1500);
            await _session.PlayAsync();
        }
    }

    // ---- Sync status ---------------------------------------------------------------------

    private void OnSyncStatus(SyncStatus st)
    {
        if (_session is null) return;
        string rtt = double.IsNaN(_session.Clock.RttMs) ? "–" : $"{_session.Clock.RttMs:0} ms";
        OverlayTitle.Text = _session.IsHost ? "Host" : $"Viewer · {st.Zone}";
        OverlayText.Text = _session.IsHost
            ? $"rtt {rtt} · drift {st.Drift:+0.000;-0.000} s · {st.Zone}"
            : $"drift {st.Drift:+0.000;-0.000} s · {st.Rate:0.0000}× · rtt {rtt}";

        double qpc = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
        double pos = _player.GetPosition();
        _syncLog?.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{qpc:0.0},{pos:0.0000},{st.Drift:0.0000},{st.Rate:0.00000},{st.Zone},{_session.Engine.Anchor?.Seq}"));

        // Test hook: after being in sync for a while, knock the local player off by N seconds.
        if (_opt.Perturb != 0 && !_session.IsHost && !_perturbed)
        {
            if (st.Zone == SyncZone.InSync) _firstInSyncAt ??= qpc;
            if (_firstInSyncAt is double t && qpc - t > _opt.PerturbAfter * 1000)
            {
                _perturbed = true;
                _player.SeekAbsolute(pos + _opt.Perturb);
                _syncLog?.WriteLine($"# perturbed by {_opt.Perturb.ToString(CultureInfo.InvariantCulture)} s");
            }
        }
    }

    // ---- Player UI --------------------------------------------------------------------------

    /// <summary>Keeps the native video window and the overlay popup aligned with the XAML layout.</summary>
    private void LayoutOverlays()
    {
        if (VideoArea.XamlRoot is null) return;
        double scale = VideoArea.XamlRoot.RasterizationScale;
        Point origin = VideoArea.TransformToVisual(null).TransformPoint(new Point(0, 0));

        _videoHost.SetBounds(
            (int)Math.Round(origin.X * scale), (int)Math.Round(origin.Y * scale),
            (int)Math.Round(VideoArea.ActualWidth * scale), (int)Math.Round(VideoArea.ActualHeight * scale));

        OverlayPopup.HorizontalOffset = origin.X + 16;
        OverlayPopup.VerticalOffset = origin.Y + 16;
    }

    private void OnPosition(double pos)
    {
        if (!_seekDragging) SeekSlider.Value = Math.Min(pos, SeekSlider.Maximum);
        TimeText.Text = $"{Format(pos)} / {Format(_duration)}";
    }

    private static string Format(double s)
    {
        if (double.IsNaN(s) || s < 0) s = 0;
        var t = TimeSpan.FromSeconds(s);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is not null) OpenMedia(file.Path);
    }

    private void OpenMedia(string pathOrUrl)
    {
        _loadedFileName = pathOrUrl.Contains("://") ? pathOrUrl : Path.GetFileName(pathOrUrl);
        _duration = 0;
        _player.Load(pathOrUrl);
        if (_session is null)
        {
            _player.SetPaused(false); // solo playback
        }
        else
        {
            _player.SetPaused(true); // the screen's anchor decides when to play
            _announcePending = _session.IsHost;
        }
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_session is { IsHost: true })
        {
            if (_session.Engine.Anchor?.IsPlaying == true) await _session.PauseAsync();
            else await _session.PlayAsync();
        }
        else if (_session is null)
        {
            _player.SetPaused(!_paused);
        }
    }

    private void OnSeekCommitted(object sender, PointerRoutedEventArgs e) => CommitSeek();

    private void OnSeekKey(object sender, KeyRoutedEventArgs e) => CommitSeek();

    private async void CommitSeek()
    {
        _seekDragging = false;
        double to = SeekSlider.Value;
        if (_session is { IsHost: true }) await _session.SeekAsync(to);
        else if (_session is null) _player.SeekAbsolute(to);
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e) => _player?.SetVolume(e.NewValue);

    private void OnOverlayToggle(object sender, RoutedEventArgs e) => OverlayPopup.IsOpen = OverlayToggle.IsChecked == true;
}
