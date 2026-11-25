using Kino.Core;
using Kino.Core.Media;
using Kino.Core.Protocol;
using Kino.Core.Sync;
using Kino.Player;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;

namespace Kino;

/// <summary>
/// The playlist (§13): adding items, mapping each local item to this viewer's own file (duration first, then name),
/// the mismatch indicator, and loading whichever file the screen's current item needs.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Files this viewer has offered (added, opened or "match my files"). Paths never leave the device.</summary>
    private readonly List<LocalFile> _candidates = [];

    /// <summary>Item id → file. Manual picks win over automatic matches.</summary>
    private Dictionary<string, LocalFile> _mapping = [];
    private readonly Dictionary<string, LocalFile> _manual = [];

    private MediaProbe? _probe;
    private string? _loadedPath;   // file or URL currently in the player
    private string? _loadingPath;
    private string? _loadingItemId;

    // ---- Tabs ----------------------------------------------------------------------------------

    private void OnSideTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        bool playlist = sender.SelectedItem == PlaylistTab;
        ViewersSection.Visibility = playlist ? Visibility.Collapsed : Visibility.Visible;
        PlaylistSection.Visibility = playlist ? Visibility.Visible : Visibility.Collapsed;
        if (playlist) RenderPlaylist();
    }

    // ---- Session hooks ---------------------------------------------------------------------------

    private void AttachPlaylist(ScreenSession s)
    {
        s.Engine.RequireLoadedItem = true; // never drive the wrong file while the right one loads
        _manual.Clear();                   // item ids are per screen
        s.PlaylistChanged += _ => _ui.TryEnqueue(() => { if (_session == s) Remap(); });
        Remap();
    }

    /// <summary>Re-matches local items to candidate files, refreshes the list and loads the current item.</summary>
    private void Remap()
    {
        if (_session is not { } s) return;
        foreach (var id in _manual.Keys.Where(id => s.Playlist.All(i => i.Id != id)).ToList()) _manual.Remove(id);
        _mapping = MediaMatch.AutoMap(s.Playlist, _candidates, _manual);
        RenderPlaylist();
        EnsureCurrentLoaded();
    }

    /// <summary>Loads the file (or link) for the screen's current item, if it isn't loaded already.</summary>
    private void EnsureCurrentLoaded()
    {
        if (_session is not { } s) return;
        var item = s.CurrentItem;
        if (item is null)
        {
            s.Engine.LoadedItemId = null;
            return;
        }

        string? source = item.IsLocal ? (_mapping.TryGetValue(item.Id, out var f) ? f.Path : null) : item.Url;
        if (source is null)
        {
            s.Engine.LoadedItemId = null;
            _loadingItemId = null;
            return; // the overlay asks them to pick their file (NoFile zone)
        }
        if (_loadingItemId == item.Id && _loadingPath == source) return; // already on its way
        if (_loadingPath is null && _loadedPath == source && !double.IsNaN(_player.Duration))
        {
            s.Engine.LoadedItemId = item.Id; // already playing this file (e.g. opened before joining)
            return;
        }

        _loadingItemId = item.Id;
        _loadingPath = source;
        s.Engine.LoadedItemId = null;
        _loadedFileName = item.Name;
        _player.SetPaused(true);
        _player.Load(source);
    }

    /// <summary>The player finished opening a file: if it's the current item's, the engine may drive it now.</summary>
    private void OnPlayerFileLoaded()
    {
        if (_loadingPath is not null)
        {
            _loadedPath = _loadingPath;
            _loadingPath = null;
        }
        if (_session is { } s && _loadingItemId is { } id)
        {
            _loadingItemId = null;
            if (s.CurrentItem?.Id == id) s.Engine.LoadedItemId = id;
            else EnsureCurrentLoaded(); // the current item changed while loading
        }
    }

    // ---- Adding and matching files -----------------------------------------------------------------

    private async Task<LocalFile> ProbeAsync(string path)
    {
        var known = _candidates.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase));
        if (known is not null) return known;
        _probe ??= new MediaProbe();
        var file = new LocalFile(path, await _probe.DurationAsync(path));
        _candidates.Add(file);
        return file;
    }

    /// <summary>
    /// Reads the files' durations, offers them for matching, and (if allowed and asked) adds them to the playlist.
    /// <paramref name="openNow"/>: a single file becomes the current item ("Open…").
    /// </summary>
    private async Task OfferFilesAsync(IReadOnlyList<string> paths, bool addToPlaylist, bool openNow = false)
    {
        var s = _session;
        var ordered = paths.OrderBy(Path.GetFileName, MediaMatch.NaturalComparer).ToList();
        var files = new List<LocalFile>();
        for (int i = 0; i < ordered.Count; i++)
        {
            ShowPlaylistStatus(ordered.Count > 1 ? $"Reading {i + 1} of {ordered.Count}: {Path.GetFileName(ordered[i])}" : $"Reading {Path.GetFileName(ordered[i])}…");
            files.Add(await ProbeAsync(ordered[i]));
        }
        ShowPlaylistStatus(null);
        if (s is null || _session != s) return;

        if (addToPlaylist && s.Can(Permissions.PlaylistAdd))
        {
            // Don't add the same file twice: skip files that match an item already on the playlist.
            _mapping = MediaMatch.AutoMap(s.Playlist, _candidates, _manual);
            var mapped = _mapping.Values.Select(m => m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var fresh = files.Where(f => !mapped.Contains(f.Path)).ToList();
            if (openNow && fresh.Count == 1)
            {
                await s.LoadLocalAsync(fresh[0].Name, fresh[0].Duration);
                if (_opt.AutoPlay) _ = AutoPlayAsync(s);
            }
            else if (fresh.Count > 0)
            {
                foreach (var chunk in fresh.Chunk(100))
                    await s.AddToPlaylistAsync(chunk.Select(f => ScreenSession.NewItem.Local(f.Name, f.Duration)));
            }
        }
        Remap();
    }

    /// <summary>Test hook (--autoplay): host starts playback once the item is in, then runs --seeks.</summary>
    private async Task AutoPlayAsync(ScreenSession s)
    {
        await Task.Delay(1500);
        if (_session != s) return;
        await s.PlayAsync();
        _ = RunScriptedSeeksAsync(s);
    }

    private async void OnAddFilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count > 0) await OfferFilesAsync(files.Select(f => f.Path).ToList(), addToPlaylist: true);
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        if (await PickFolderMediaAsync() is { Count: > 0 } paths) await OfferFilesAsync(paths, addToPlaylist: true);
    }

    /// <summary>"Match my files": offer a folder of my copies for matching, without adding anything.</summary>
    private async void OnMatchFilesClick(object sender, RoutedEventArgs e)
    {
        if (await PickFolderMediaAsync() is { Count: > 0 } paths)
        {
            await OfferFilesAsync(paths, addToPlaylist: false);
            int matched = _session?.Playlist.Count(i => i.IsLocal && _mapping.ContainsKey(i.Id)) ?? 0;
            int local = _session?.Playlist.Count(i => i.IsLocal) ?? 0;
            ShowPlaylistStatus($"Matched {matched} of {local} items.");
        }
    }

    /// <summary>A folder's media files (top level only).</summary>
    private async Task<List<string>?> PickFolderMediaAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return null;
        var paths = Directory.EnumerateFiles(folder.Path)
            .Where(p => MediaMatch.MediaExtensions.Contains(Path.GetExtension(p)))
            .ToList();
        if (paths.Count == 0) ShowPlaylistStatus("No media files in that folder.");
        return paths;
    }

    // ---- Links ---------------------------------------------------------------------------------------

    private void OnAddLinkClick(object sender, RoutedEventArgs e)
    {
        AddLinkRow.Visibility = Visibility.Visible;
        LinkBox.Text = "";
        LinkBox.Focus(FocusState.Programmatic);
    }

    private void OnAddLinkCancel(object sender, RoutedEventArgs e) => AddLinkRow.Visibility = Visibility.Collapsed;

    private async void OnLinkKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) await AddLinkAsync();
    }

    private async void OnAddLinkConfirm(object sender, RoutedEventArgs e) => await AddLinkAsync();

    private async Task AddLinkAsync()
    {
        if (_session is not { } s) return;
        string text = LinkBox.Text.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            ShowPlaylistStatus("Enter a full http(s) link to a video file.");
            return;
        }
        string name = Uri.UnescapeDataString(uri.Segments.LastOrDefault()?.Trim('/') ?? "");
        if (name.Length == 0) name = uri.Host;
        AddLinkRow.Visibility = Visibility.Collapsed;
        await s.AddToPlaylistAsync([ScreenSession.NewItem.Link(uri.ToString(), name)]);
    }

    // ---- The list ---------------------------------------------------------------------------------------

    private void RenderPlaylist()
    {
        if (_session is not { } s)
        {
            PlaylistList.ItemsSource = null;
            return;
        }
        var rows = s.Playlist.Select((item, i) =>
            new PlaylistRow(item, i, item.Id == s.CurrentItem?.Id, _mapping.GetValueOrDefault(item.Id))).ToList();
        PlaylistList.ItemsSource = rows;
        int warnings = rows.Count(r => r.Warn);
        PlaylistHeader.Text = s.Playlist.Count == 0 ? "Playlist (empty)"
            : warnings > 0 ? $"Playlist ({s.Playlist.Count}) · {warnings} need attention"
            : $"Playlist ({s.Playlist.Count})";

        AddFilesButton.Visibility = AddFolderButton.Visibility = s.Can(Permissions.PlaylistAdd) ? Visibility.Visible : Visibility.Collapsed;
        AddLinkButton.Visibility = s.Can(Permissions.PlaylistAddExternal) ? Visibility.Visible : Visibility.Collapsed;
        PreviousButton.Visibility = NextButton.Visibility = s.Can(Permissions.PlaybackSkip) ? Visibility.Visible : Visibility.Collapsed;
        int index = s.Playlist.ToList().FindIndex(i => i.Id == s.CurrentItem?.Id);
        PreviousButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index >= 0 && index < s.Playlist.Count - 1;
    }

    private void ShowPlaylistStatus(string? text)
    {
        PlaylistStatus.Text = text ?? "";
        PlaylistStatus.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPlaylistItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PlaylistRow row || _session is not { } s) return;
        if (PlaylistList.ContainerFromItem(e.ClickedItem) is not FrameworkElement anchor) return;

        var item = row.Item;
        int index = s.Playlist.ToList().FindIndex(i => i.Id == item.Id);
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };
        void Add(string text, Func<Task> action)
        {
            var mi = new MenuFlyoutItem { Text = text };
            mi.Click += async (_, _) => await action();
            menu.Items.Add(mi);
        }

        if (s.Can(Permissions.PlaybackSkip) && item.Id != s.CurrentItem?.Id) Add("Play now", () => s.PlayItemAsync(item.Id));
        if (item.IsLocal)
        {
            Add(row.MyFile is null ? "Choose my file…" : "Choose a different file…", () => ChooseFileForAsync(item));
            if (_manual.ContainsKey(item.Id)) Add("Use the automatic match", () => { _manual.Remove(item.Id); Remap(); return Task.CompletedTask; });
        }
        if (s.Can(Permissions.PlaylistReorder))
        {
            if (index > 0) Add("Move up", () => s.MoveItemAsync(item.Id, index - 1));
            if (index < s.Playlist.Count - 1) Add("Move down", () => s.MoveItemAsync(item.Id, index + 1));
        }
        if (s.Can(Permissions.PlaylistRemove))
        {
            if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
            Add("Remove from playlist", () => s.RemoveItemAsync(item.Id));
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = "No actions available", IsEnabled = false });
        menu.ShowAt(anchor);
    }

    /// <summary>Manually map an item to a file (§13.4 step 3). A mismatch only warns; it never blocks.</summary>
    private async Task ChooseFileForAsync(Item item)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var picked = await picker.PickSingleFileAsync();
        if (picked is null) return;
        var file = await ProbeAsync(picked.Path);
        _manual[item.Id] = file;
        Remap();
        if (MediaMatch.Compare(item, file) == MatchKind.Mismatch)
            ShowPlaylistStatus($"⚠ {file.Name} isn't the same length as the host's file. It will play anyway, but may drift out of step.");
    }

    private async void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        if (_session is { } s) await s.SkipAsync(next: false);
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_session is { } s) await s.SkipAsync(next: true);
    }

    /// <summary>Overlay line for the current item (mismatch warning, missing file, waiting).</summary>
    private string? PlaylistOverlayHint(SyncZone zone)
    {
        if (_session is not { CurrentItem: { } item }) return null;
        if (zone == SyncZone.NoFile)
            return item.IsLocal && !_mapping.ContainsKey(item.Id) ? $"Pick your file for “{item.Name}” (Playlist tab)" : $"Loading “{item.Name}”…";
        if (zone == SyncZone.Waiting) return "Your copy has ended. Waiting for the others…";
        if (item.IsLocal && _mapping.TryGetValue(item.Id, out var f) && MediaMatch.Compare(item, f) == MatchKind.Mismatch)
            return "⚠ Your file's length differs from the host's";
        return null;
    }
}
