using Kino.Player;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Windows.Storage.Pickers;

namespace Kino;

public sealed partial class MainWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly VideoHost _videoHost;
    private readonly MpvPlayer _player;
    private readonly DispatcherQueue _ui;

    private bool _paused = true;
    private double _duration;
    private bool _updatingSeek; // true while we set the slider from playback, so it doesn't seek back

    public MainWindow(string? startMedia)
    {
        InitializeComponent();
        _ui = DispatcherQueue.GetForCurrentThread();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));

        _videoHost = new VideoHost(_hwnd);
        _player = new MpvPlayer(_videoHost.Hwnd);

        _player.PositionChanged += pos => _ui.TryEnqueue(() => OnPosition(pos));
        _player.DurationChanged += d => _ui.TryEnqueue(() => { _duration = d; SeekSlider.Maximum = Math.Max(1, d); });
        _player.PausedChanged += p => _ui.TryEnqueue(() => { _paused = p; PlayPauseButton.Content = p ? "Play" : "Pause"; });
        _player.SpeedChanged += s => _ui.TryEnqueue(() => SpeedText.Text = $"{s:0.000}×");
        _player.BufferingChanged += b => _ui.TryEnqueue(() => OverlayText.Text = b ? "Buffering…" : "Drawn above the video");
        _player.Error += msg => _ui.TryEnqueue(() => OverlayText.Text = msg);

        VideoArea.SizeChanged += (_, _) => LayoutOverlays();
        Root.Loaded += (_, _) => { LayoutOverlays(); OverlayPopup.IsOpen = OverlayToggle.IsChecked == true; };
        Closed += (_, _) => { _player.Dispose(); _videoHost.Dispose(); };

        if (!string.IsNullOrWhiteSpace(startMedia))
        {
            _player.Load(startMedia);
            _player.SetPaused(false);
        }
    }

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
        _updatingSeek = true;
        SeekSlider.Value = Math.Min(pos, SeekSlider.Maximum);
        _updatingSeek = false;
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
        if (file is null) return;
        _player.Load(file.Path);
        _player.SetPaused(false);
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _player.SetPaused(!_paused);

    private void OnSeekChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSeek) return;
        _player.SeekAbsolute(e.NewValue);
    }

    private void OnSpeedChanged(object sender, RangeBaseValueChangedEventArgs e) => _player?.SetSpeed(e.NewValue);

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e) => _player?.SetVolume(e.NewValue);

    private void OnOverlayToggle(object sender, RoutedEventArgs e) => OverlayPopup.IsOpen = OverlayToggle.IsChecked == true;
}
