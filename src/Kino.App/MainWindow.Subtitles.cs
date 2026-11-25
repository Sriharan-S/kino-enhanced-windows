using Kino.Core;
using Kino.Core.Media;
using Kino.Core.Net;
using Kino.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Kino;

/// <summary>
/// Subtitles (§17): each viewer chooses, per item, off / an embedded track / any shared file / their own file.
/// Sharing uploads the file to the screen (needs <c>subtitles.share</c>). Timing and look are local only.
/// </summary>
public sealed partial class MainWindow
{
    private abstract record SubChoice;
    private sealed record SubOff : SubChoice;
    private sealed record SubEmbedded(int TrackId) : SubChoice;
    private sealed record SubShared(string Id) : SubChoice;
    private sealed record SubOwn(string Path) : SubChoice;

    /// <summary>Per item (screen item id, or the file's prefs key when playing solo). Local only.</summary>
    private readonly Dictionary<string, SubChoice> _subChoices = [];
    private readonly Dictionary<string, string> _ownSubs = []; // item key → this viewer's own file
    private readonly List<SubChoice> _subOptions = [];
    private bool _fillingSubs;
    private bool _subsReady;

    private string? SubKey => _session?.CurrentItem?.Id ?? _viewKey;

    private void InitSubtitles()
    {
        foreach (var f in SubtitleStyle.Fonts) SubFontBox.Items.Add(f);
        foreach (var (label, _) in SubtitleStyle.Colours) SubColourBox.Items.Add(label);
        _subsReady = true;
    }

    /// <summary>After a file loads: re-apply style, offset and this item's choice (external tracks vanish on reload).</summary>
    private async Task ApplySubtitlesAsync()
    {
        _player.ApplySubtitleStyle(_prefs.Subtitles, CurrentSubOffset);
        if (SubKey is { } key && _subChoices.TryGetValue(key, out var choice)) await ApplyChoiceAsync(choice);
    }

    private double CurrentSubOffset => _viewKey is not null && _prefs.SubtitleOffsets.TryGetValue(_viewKey, out var ms) ? ms : 0;

    private async Task ApplyChoiceAsync(SubChoice choice)
    {
        try
        {
            switch (choice)
            {
                case SubOff: _player.SelectSubtitle(null); break;
                case SubEmbedded e: _player.SelectSubtitle(e.TrackId); break;
                case SubOwn o: _player.AddSubtitle(o.Path); break;
                case SubShared sh when await SharedSubtitleFileAsync(sh.Id) is { } path: _player.AddSubtitle(path); break;
            }
            _syncLog?.WriteLine($"# subtitles: {choice} sid={_player.GetString("sid")}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TheatreException or IOException or TaskCanceledException)
        {
            ShowSubStatus($"Couldn't load the subtitles: {FriendlyError(ex)}");
        }
    }

    /// <summary>Downloads a shared file once into a temp cache, keeping its extension for libmpv.</summary>
    private async Task<string?> SharedSubtitleFileAsync(string id)
    {
        if (_session is not { } s || s.Subs.FirstOrDefault(x => x.Id == id) is not { } meta) return null;
        string dir = Path.Combine(Path.GetTempPath(), "Kino", "subs", s.Code);
        string path = Path.Combine(dir, $"{meta.Id}.{meta.Format}");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(path, await s.DownloadSubtitleAsync(meta));
        }
        return path;
    }

    // ---- Flyout ------------------------------------------------------------------------------------

    private void OnSubtitlesFlyoutOpening(object sender, object e)
    {
        _fillingSubs = true;
        SubStatus.Visibility = Visibility.Collapsed;

        _subOptions.Clear();
        SubSourceChoice.Items.Clear();
        void Option(string label, SubChoice choice)
        {
            _subOptions.Add(choice);
            SubSourceChoice.Items.Add(label);
        }
        Option("Off", new SubOff());
        foreach (var t in _player.Tracks("sub").Where(t => !t.External))
            Option($"In the file: {t.Label}", new SubEmbedded(t.Id));
        if (_session is { CurrentItem: { } item } s)
            foreach (var sub in s.Subs.Where(x => x.ItemId == item.Id))
                Option($"Shared by {sub.FromName}: {sub.Name}", new SubShared(sub.Id));
        if (SubKey is { } key && _ownSubs.TryGetValue(key, out var own)) Option($"Your file: {Path.GetFileName(own)}", new SubOwn(own));

        var current = SubKey is { } k && _subChoices.TryGetValue(k, out var c) ? c : null;
        int selected = current is null ? -1 : _subOptions.IndexOf(current);
        if (selected < 0 && current is null)
        {
            // No choice yet: show what libmpv picked (e.g. a default embedded track), else Off.
            var shown = _player.Tracks("sub").FirstOrDefault(t => t.Selected);
            selected = shown is null ? 0 : _subOptions.FindIndex(o => o is SubEmbedded e2 && e2.TrackId == shown.Id);
        }
        SubSourceChoice.SelectedIndex = Math.Max(0, selected);

        bool canShare = _session is { CurrentItem: not null } ss && ss.Can(Permissions.SubtitlesShare);
        ShareSubBox.Visibility = canShare ? Visibility.Visible : Visibility.Collapsed;

        var st = _prefs.Subtitles.Clamped();
        SubOffsetBox.Value = CurrentSubOffset;
        SubOffsetBox.IsEnabled = _viewKey is not null;
        SubFontBox.SelectedIndex = Math.Max(0, Array.IndexOf(SubtitleStyle.Fonts, st.Font));
        SubColourBox.SelectedIndex = Math.Max(0, Array.FindIndex(SubtitleStyle.Colours, x => x.Hex == st.Colour));
        SubSizeSlider.Value = st.Size;
        SubOutlineSlider.Value = st.Outline;
        SubBackgroundSwitch.IsOn = st.Background;
        SubBoxOpacitySlider.Value = st.BackgroundOpacity * 100;
        SubPositionSlider.Value = st.Position;
        _fillingSubs = false;
    }

    private async void OnSubSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingSubs || !_subsReady || SubKey is not { } key) return;
        int i = SubSourceChoice.SelectedIndex;
        if (i < 0 || i >= _subOptions.Count) return;
        _subChoices[key] = _subOptions[i];
        await ApplyChoiceAsync(_subOptions[i]);
    }

    private async void OnLoadSubtitleClick(object sender, RoutedEventArgs e)
    {
        if (SubKey is not { } key) { ShowSubStatus("Open a video first."); return; }
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        foreach (var ext in ScreenSession.SubtitleFormats) picker.FileTypeFilter.Add("." + ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        _ownSubs[key] = file.Path;
        _subChoices[key] = new SubOwn(file.Path);
        await ApplyChoiceAsync(_subChoices[key]);

        if (ShareSubBox.IsChecked == true && _session is { CurrentItem: { } item } s)
        {
            try
            {
                ShowSubStatus("Sharing…");
                await s.ShareSubtitleAsync(item.Id, file.Path);
                ShowSubStatus($"Shared {file.Name} with everyone.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TheatreException or IOException)
            {
                ShowSubStatus($"Couldn't share: {(ex is TheatreException { Status: 413 } ? "the file is over 1 MB compressed" : FriendlyError(ex))}");
            }
        }
        OnSubtitlesFlyoutOpening(this, EventArgs.Empty); // refresh the list
    }

    private void OnSubOffsetChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_fillingSubs || !_subsReady || _viewKey is null || double.IsNaN(args.NewValue)) return;
        _prefs.SubtitleOffsets[_viewKey] = Math.Round(args.NewValue);
        if (args.NewValue == 0) _prefs.SubtitleOffsets.Remove(_viewKey);
        _prefs.Save(_prefsFile);
        _player.ApplySubtitleStyle(_prefs.Subtitles, CurrentSubOffset);
    }

    private void OnSubStyleChanged(object sender, object e)
    {
        if (_fillingSubs || !_subsReady || _player is null) return;
        _prefs.Subtitles = new SubtitleStyle
        {
            Font = SubtitleStyle.Fonts[Math.Max(0, SubFontBox.SelectedIndex)],
            Colour = SubtitleStyle.Colours[Math.Max(0, SubColourBox.SelectedIndex)].Hex,
            Size = SubSizeSlider.Value,
            Outline = SubOutlineSlider.Value,
            Background = SubBackgroundSwitch.IsOn,
            BackgroundOpacity = SubBoxOpacitySlider.Value / 100,
            Position = SubPositionSlider.Value,
        }.Clamped();
        _prefs.Save(_prefsFile);
        _player.ApplySubtitleStyle(_prefs.Subtitles, CurrentSubOffset);
    }

    /// <summary>Test hooks (--sub, --sub-share, --sub-pick shared).</summary>
    private bool _subHookDone;

    private async Task RunSubtitleHooksAsync()
    {
        if (_subHookDone || _session is not { CurrentItem: { } item } s || s.Engine.LoadedItemId != item.Id) return;
        if (_opt.Sub is { } file)
        {
            _subHookDone = true;
            string path = Path.GetFullPath(file);
            _ownSubs[item.Id] = path;
            _subChoices[item.Id] = new SubOwn(path);
            await ApplyChoiceAsync(_subChoices[item.Id]);
            if (_opt.SubShare) await s.ShareSubtitleAsync(item.Id, path);
        }
        else if (_opt.SubPick == "shared" && s.Subs.FirstOrDefault(x => x.ItemId == item.Id) is { } shared)
        {
            _subHookDone = true;
            _subChoices[item.Id] = new SubShared(shared.Id);
            await ApplyChoiceAsync(_subChoices[item.Id]);
        }
    }

    private void ShowSubStatus(string text)
    {
        SubStatus.Text = text;
        SubStatus.Visibility = Visibility.Visible;
    }
}
