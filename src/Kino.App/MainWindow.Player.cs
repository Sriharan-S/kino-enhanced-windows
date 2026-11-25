using Kino.Core.Media;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Kino;

/// <summary>
/// Local player features (spec §15.4, §16): picture and sound settings, audio tracks, personal sync offset, volume boost,
/// mute, fullscreen, keyboard shortcuts and resume positions. None of this is ever sent to a theatre.
/// </summary>
public sealed partial class MainWindow
{
    private LocalPrefs _prefs = new();
    private string _prefsFile = "";
    private string? _viewKey;          // prefs key of the loaded file
    private bool _syncingViewControls; // true while filling the flyout from settings
    private bool _fullscreen;
    private GridLength _sideWidthBeforeFullscreen;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _resumeTimer;

    private void InitPlayerFeatures()
    {
        _prefsFile = LocalPrefs.PathFor(_opt.Profile);
        _prefs = LocalPrefs.Load(_prefsFile);

        foreach (var a in ViewSettings.Aspects) AspectBox.Items.Add(a == "original" ? "Original" : a);
        foreach (var (label, _) in FitModes) FitBox.Items.Add(label);
        InitSubtitles();

        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnShortcutKey), handledEventsToo: false);

        _resumeTimer = _ui.CreateTimer();
        _resumeTimer.Interval = TimeSpan.FromSeconds(5);
        _resumeTimer.Tick += (_, _) => RememberPosition();
        _resumeTimer.Start();
    }

    private static readonly (string Label, FitMode Mode)[] FitModes =
        [("Fit", FitMode.Fit), ("Fit width", FitMode.FitWidth), ("Fit height", FitMode.FitHeight), ("Stretch", FitMode.Stretch), ("Fill (crop)", FitMode.Fill)];

    // ---- Per-file view settings ------------------------------------------------------------------

    private ViewSettings CurrentView => _prefs.ViewFor(_viewKey);

    /// <summary>A file finished loading: apply its picture settings, offset and (solo only) resume position.</summary>
    private void OnFileReady(string pathOrUrl)
    {
        _viewKey = LocalPrefs.KeyFor(pathOrUrl);
        ApplyView();
        _ = ApplySubtitlesAsync();
        if (_session is null && _prefs.ResumeAt(_viewKey) is double at)
        {
            _player.SeekAbsolute(at);
            OverlayText.Text = $"Resumed at {PlaylistRow.Format(at)} · Home restarts";
        }
    }

    private void ApplyView()
    {
        var v = CurrentView;
        double aspect = VideoArea.ActualHeight > 0 ? VideoArea.ActualWidth / VideoArea.ActualHeight : 16.0 / 9;
        _player.ApplyView(v, aspect);
        if (_session is { } s) s.Engine.PersonalOffsetMs = v.SyncOffsetMs; // applied inside the drift formula (§14.15)
    }

    private void SaveView(ViewSettings v)
    {
        v = v.Clamped();
        if (_viewKey is not null && (PerFileBox.IsChecked == true || _prefs.HasOverride(_viewKey))) _prefs.PerFile[_viewKey] = v;
        else _prefs.Defaults = v;
        _prefs.Save(_prefsFile);
        ApplyView();
    }

    private void OnPictureFlyoutOpening(object sender, object e)
    {
        _syncingViewControls = true;
        var v = CurrentView;
        ZoomSlider.Value = Math.Round(v.Zoom * 100);
        PanXSlider.Value = v.PanX * 100;
        PanYSlider.Value = v.PanY * 100;
        PanRow.Visibility = v.Zoom > 1.0 ? Visibility.Visible : Visibility.Collapsed;
        AspectBox.SelectedIndex = Math.Max(0, Array.IndexOf(ViewSettings.Aspects, v.Aspect));
        FitBox.SelectedIndex = Math.Max(0, Array.FindIndex(FitModes, f => f.Mode == v.Fit));
        SyncOffsetBox.Value = v.SyncOffsetMs;
        SyncOffsetBox.IsEnabled = _session is not null;
        PerFileBox.IsEnabled = _viewKey is not null;
        PerFileBox.IsChecked = _prefs.HasOverride(_viewKey);

        AudioTrackBox.Items.Clear();
        var tracks = _player.Tracks("audio");
        foreach (var t in tracks) AudioTrackBox.Items.Add(new ComboBoxItem { Content = t.Label, Tag = t.Id });
        AudioTrackBox.SelectedIndex = tracks.ToList().FindIndex(t => t.Selected);
        AudioTrackBox.IsEnabled = tracks.Count > 1;
        AudioTrackBox.PlaceholderText = tracks.Count == 0 ? "No audio" : "";
        _syncingViewControls = false;
    }

    private void OnViewControlChanged(object sender, object e)
    {
        if (_syncingViewControls || _player is null || _prefsFile.Length == 0) return; // also fires while XAML loads
        double zoom = ZoomSlider.Value / 100;
        PanRow.Visibility = zoom > 1.0 ? Visibility.Visible : Visibility.Collapsed;
        SaveView(CurrentView with
        {
            Zoom = zoom,
            PanX = zoom > 1.0 ? PanXSlider.Value / 100 : 0,
            PanY = zoom > 1.0 ? PanYSlider.Value / 100 : 0,
            Aspect = ViewSettings.Aspects[Math.Max(0, AspectBox.SelectedIndex)],
            Fit = FitModes[Math.Max(0, FitBox.SelectedIndex)].Mode,
        });
    }

    private void OnSyncOffsetChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncingViewControls || _player is null || _prefsFile.Length == 0 || double.IsNaN(args.NewValue)) return;
        SaveView(CurrentView with { SyncOffsetMs = Math.Round(args.NewValue) });
    }

    private void OnPerFileToggle(object sender, RoutedEventArgs e)
    {
        if (_viewKey is null) return;
        if (PerFileBox.IsChecked == true) _prefs.PerFile[_viewKey] = CurrentView;
        else _prefs.PerFile.Remove(_viewKey);
        _prefs.Save(_prefsFile);
        ApplyView();
    }

    private void OnResetViewClick(object sender, RoutedEventArgs e)
    {
        if (_viewKey is not null) _prefs.PerFile.Remove(_viewKey);
        _prefs.Defaults = new ViewSettings();
        _prefs.Save(_prefsFile);
        ApplyView();
        OnPictureFlyoutOpening(this, EventArgs.Empty);
    }

    private void OnAudioTrackChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingViewControls || _player is null || AudioTrackBox.SelectedItem is not ComboBoxItem { Tag: int id }) return;
        _player.SelectAudio(id);
    }

    // ---- Volume, boost and mute --------------------------------------------------------------------

    private bool _restoringVolume;

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_player is null || _restoringVolume) return;
        if (e.NewValue > 100 && !_prefs.BoostAccepted)
        {
            // One-time disclaimer before going above 100% (spec §16).
            _restoringVolume = true;
            VolumeSlider.Value = 100;
            _restoringVolume = false;
            FlyoutBase.ShowAttachedFlyout(VolumeSlider);
            return;
        }
        _player.SetVolume(e.NewValue);
        _yt?.SetVolume(Math.Min(100, e.NewValue)); // YouTube has no boost
        _prefs.Volume = e.NewValue;
    }

    private void OnBoostAccept(object sender, RoutedEventArgs e)
    {
        _prefs.BoostAccepted = true;
        _prefs.Save(_prefsFile);
        BoostFlyout.Hide();
        VolumeSlider.Value = 120;
    }

    private void OnBoostDecline(object sender, RoutedEventArgs e) => BoostFlyout.Hide();

    private void OnMuteToggle(object sender, RoutedEventArgs e) => SetMuted(MuteToggle.IsChecked == true);

    private void SetMuted(bool muted)
    {
        MuteToggle.IsChecked = muted;
        MuteToggle.Content = muted ? "\U0001F507" : "\U0001F50A";
        _player.SetMuted(muted);
        _yt?.SetMuted(muted);
    }

    // ---- Fullscreen ----------------------------------------------------------------------------------

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => SetFullscreen(!_fullscreen);

    private void SetFullscreen(bool on)
    {
        if (on == _fullscreen) return;
        _fullscreen = on;
        AppWindow.SetPresenter(on ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        TopBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on)
        {
            _sideWidthBeforeFullscreen = SideColumn.Width;
            SideColumn.Width = new GridLength(0);
            if (_page != Page.Video) ShowPage(Page.Video);
        }
        else
        {
            SideColumn.Width = _sideWidthBeforeFullscreen;
        }
        FullscreenButton.Content = on ? "✕" : "⛶";
        LayoutOverlays();
    }

    // ---- Keyboard shortcuts --------------------------------------------------------------------------

    /// <summary>Space/K play-pause, ←/→ ±5 s, J/L ±10 s, ↑/↓ volume, M mute, F/F11 fullscreen, Esc exit, N/P next/previous, Home restart.</summary>
    private void OnShortcutKey(object sender, KeyRoutedEventArgs e)
    {
        // Never steal keys from text entry.
        if (FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or PasswordBox or NumberBox or AutoSuggestBox or ComboBox) return;
        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl) return;

        switch (e.Key)
        {
            case VirtualKey.Space or VirtualKey.K: OnPlayPauseClick(this, new RoutedEventArgs()); break;
            case VirtualKey.Left: SeekBy(-5); break;
            case VirtualKey.Right: SeekBy(5); break;
            case VirtualKey.J: SeekBy(-10); break;
            case VirtualKey.L: SeekBy(10); break;
            case VirtualKey.Up: VolumeSlider.Value = Math.Min(VolumeSlider.Value + 5, _prefs.BoostAccepted ? 200 : 100); break;
            case VirtualKey.Down: VolumeSlider.Value = Math.Max(VolumeSlider.Value - 5, 0); break;
            case VirtualKey.M: SetMuted(MuteToggle.IsChecked != true); break;
            case VirtualKey.F or VirtualKey.F11: SetFullscreen(!_fullscreen); break;
            case VirtualKey.Escape when _fullscreen: SetFullscreen(false); break;
            case VirtualKey.N when NextButton.Visibility == Visibility.Visible && NextButton.IsEnabled: OnNextClick(this, new RoutedEventArgs()); break;
            case VirtualKey.P when PreviousButton.Visibility == Visibility.Visible && PreviousButton.IsEnabled: OnPreviousClick(this, new RoutedEventArgs()); break;
            case VirtualKey.Home: SeekTo(0); break;
            default: return;
        }
        e.Handled = true;
    }

    private void SeekBy(double seconds)
    {
        double pos = _player.GetPosition();
        if (double.IsFinite(pos)) SeekTo(Math.Max(0, pos + seconds));
    }

    /// <summary>Solo: seek here. In a screen: seek for everyone, if allowed.</summary>
    private async void SeekTo(double seconds)
    {
        if (_session is null) _player.SeekAbsolute(seconds);
        else if (_session.Can(Kino.Core.Protocol.Permissions.PlaybackSeek)) await _session.SeekAsync(seconds);
    }

    // ---- Resume positions (§13.7) ----------------------------------------------------------------------

    private void RememberPosition()
    {
        if (_viewKey is null || _paused) return;
        double pos = _player.GetPosition();
        if (!double.IsFinite(pos)) return;
        _prefs.SetResume(_viewKey, pos, double.IsNaN(_duration) ? null : _duration, DateTimeOffset.Now);
        _prefs.Save(_prefsFile);
    }
}
