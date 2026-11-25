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

/// <summary>Player, screen session and automation. Lobby, join and create live in MainWindow.Lobby.cs.</summary>
public sealed partial class MainWindow : Window
{
    private readonly LaunchOptions _opt;
    private readonly IntPtr _hwnd;
    private readonly VideoHost _videoHost;
    private readonly MpvPlayer _player;
    private readonly DispatcherQueue _ui;
    private readonly DeviceIdentity _identity;

    private ScreenSession? _session;
    private TheatreEntry? _sessionTheatre;
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

        NameBox.Text = options.Name;
        VolumeSlider.Value = options.Volume;

        _videoHost = new VideoHost(_hwnd);
        _player = new MpvPlayer(_videoHost.Hwnd);
        _player.SetVolume(options.Volume); // the slider fired before the player existed

        _player.PositionChanged += pos => _ui.TryEnqueue(() => OnPosition(pos));
        _player.DurationChanged += d => _ui.TryEnqueue(() => { _duration = d; SeekSlider.Maximum = Math.Max(1, d); TryAnnounceItem(); });
        _player.PausedChanged += p => _ui.TryEnqueue(() =>
        {
            _paused = p;
            PlayPauseButton.Content = p || _loadedFileName is null ? "Play" : "Pause";
        });
        _player.FileLoaded += _ => _ui.TryEnqueue(TryAnnounceItem);
        _player.Error += msg => _ui.TryEnqueue(() => OverlayText.Text = msg);

        SeekSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _seekDragging = true), true);

        VideoArea.SizeChanged += (_, _) => LayoutOverlays();
        Root.Loaded += async (_, _) =>
        {
            LayoutOverlays();
            bool direct = _opt.Media is not null || _opt.Create is not null || _opt.Join is not null;
            ShowLobby(!direct);
            await LoadTheatresAsync();
            await AutoStartAsync();
        };
        Closed += async (_, _) =>
        {
            _lobbyTimer?.Stop();
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
        if (_opt.Create is not null) await CreateScreenAsync(new CreateScreenRequest(_opt.Create, "public"));
        else if (_opt.Join is not null) await JoinByCodeAsync(_opt.Join.Trim());
    }

    // ---- Session -------------------------------------------------------------------------

    private string DisplayName() => string.IsNullOrWhiteSpace(NameBox.Text) ? "Viewer" : NameBox.Text.Trim();

    private void AttachSession(ScreenSession s)
    {
        _session = s;
        _fatalMessage = null;
        UpdateSessionText();
        LeaveButton.Visibility = ViewersToggle.Visibility = Visibility.Visible;
        ShowLobby(false);
        OnPermissionsChanged();

        s.StatusChanged += st => _ui.TryEnqueue(() => { if (_session == s) OnSyncStatus(st); });
        s.ViewersChanged += _ => _ui.TryEnqueue(() =>
        {
            if (_session != s) return;
            UpdateSessionText();
            OnPermissionsChanged();
        });
        s.RolesChanged += _ => _ui.TryEnqueue(() => { if (_session == s) OnPermissionsChanged(); });
        s.CoHostsChanged += _ => _ui.TryEnqueue(() => { if (_session == s) RenderViewers(); });
        s.ItemChanged += item => _ui.TryEnqueue(() => { if (_session == s) OnItemChanged(item); });
        s.Error += err => _ui.TryEnqueue(() =>
        {
            if (_session != s) return;
            if (err.Code is "kicked" or "banned" or "closed") _fatalMessage = err.Message;
            else ShowActionError(err.Message);
        });
        s.Closed += reason => _ui.TryEnqueue(async () =>
        {
            if (_session != s) return; // we left on purpose
            string? why = _fatalMessage;
            await LeaveSessionAsync();
            if (why is not null)
            {
                SessionText.Text = "Not in a screen";
                ShowJoinMessage(char.ToUpperInvariant(why[0]) + why[1..] + ".");
            }
            else
            {
                SessionText.Text = $"Disconnected ({reason})";
            }
        });

        OnItemChanged(s.CurrentItem);
        _announcePending = s.IsHost;
        TryAnnounceItem();

        if (_opt.Panel is "viewers" or "roles")
        {
            ViewersToggle.IsChecked = true;
            OnViewersToggle(this, new RoutedEventArgs());
        }
        if (_opt.Panel == "roles") OpenRolesPage(null);
    }

    /// <summary>Message from kick / ban / close, shown in the lobby once the connection drops.</summary>
    private string? _fatalMessage;

    /// <summary>Roles, co-host status or permissions changed: update everything that depends on them.</summary>
    private void OnPermissionsChanged()
    {
        if (_session is not { } s) return;
        PlayPauseButton.IsEnabled = s.Can(Permissions.PlaybackToggle);
        SeekSlider.IsEnabled = s.Can(Permissions.PlaybackSeek);
        RenderViewers();
        if (_page == Page.Roles) RefreshRolesPage();
    }

    private async Task LeaveSessionAsync()
    {
        if (_session is not { } s) return;
        _session = null; // first, so its Closed event is ignored
        _sessionTheatre = null;
        await s.DisposeAsync();
        _player.SetSpeed(1.0);
        SessionText.Text = "Not in a screen";
        LeaveButton.Visibility = ViewersToggle.Visibility = Visibility.Collapsed;
        ViewersToggle.IsChecked = false;
        SideColumn.Width = new GridLength(0);
        if (_page == Page.Roles) ShowPage(Page.Video);
        PlayPauseButton.IsEnabled = SeekSlider.IsEnabled = true;
        OverlayTitle.Text = "Kino";
        OverlayText.Text = "Open a file or join a screen";
    }

    private async void OnLeaveClick(object sender, RoutedEventArgs e)
    {
        await LeaveSessionAsync();
        ShowLobby(true);
    }

    private void UpdateSessionText()
    {
        if (_session is null) return;
        string role = _session.IsHost ? "Host"
            : _session.Self?.ActingHost == true ? "Acting host"
            : _session.Self?.CoHost is not null ? "Co-host"
            : "Viewer";
        int count = _session.Viewers.Count;
        string theatre = _sessionTheatre is null ? "" : $" · {_sessionTheatre.Name}";
        SessionText.Text = $"{_session.Joined!.Screen.Name}{theatre} · code {_session.Code} · {role} · {count} viewer{(count == 1 ? "" : "s")}";
    }

    private void OnItemChanged(Item? item)
    {
        if (_session is null || item is null || item.Name == _loadedFileName) return;
        OverlayTitle.Text = $"Host is playing: {item.Name}";
        if (_loadedFileName is null) OverlayText.Text = "Open your copy of this file";
    }

    /// <summary>
    /// Once a file is loaded and its duration known, make it the screen's item, if allowed. Opening your own copy of
    /// the current item (same name) just plays along and doesn't replace it.
    /// </summary>
    private async void TryAnnounceItem()
    {
        if (_session is null || !_announcePending || _loadedFileName is null || !(_duration > 0)) return;
        if (!_session.Can(Permissions.PlaylistAdd) || _session.CurrentItem?.Name == _loadedFileName)
        {
            _announcePending = false;
            return;
        }
        _announcePending = false;
        await _session.LoadLocalAsync(_loadedFileName, Math.Round(_duration, 3));
        if (_opt.AutoPlay)
        {
            await Task.Delay(1500);
            await _session.PlayAsync();
            _ = RunScriptedSeeksAsync(_session);
        }
    }

    /// <summary>Test hook (--seeks): host seeks on a schedule, for repeatable live tests.</summary>
    private async Task RunScriptedSeeksAsync(ScreenSession session)
    {
        double elapsed = 0;
        foreach (var (after, to) in _opt.Seeks.OrderBy(s => s.After))
        {
            await Task.Delay(TimeSpan.FromSeconds(after - elapsed));
            elapsed = after;
            if (_session != session) return;
            _syncLog?.WriteLine($"# host seek to {to.ToString(CultureInfo.InvariantCulture)}");
            await session.SeekAsync(to);
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
        ShowLobby(false);
        if (_session is null)
        {
            _player.SetPaused(false); // solo playback
        }
        else
        {
            _player.SetPaused(true); // the screen's anchor decides when to play
            _announcePending = true; // TryAnnounceItem checks permission and whether it's the current item
        }
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_session is not null)
        {
            if (!_session.Can(Permissions.PlaybackToggle)) return;
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
        if (_session is null) _player.SeekAbsolute(to);
        else if (_session.Can(Permissions.PlaybackSeek)) await _session.SeekAsync(to);
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e) => _player?.SetVolume(e.NewValue);

    private void OnOverlayToggle(object sender, RoutedEventArgs e) =>
        OverlayPopup.IsOpen = OverlayToggle.IsChecked == true && _page == Page.Video;
}
