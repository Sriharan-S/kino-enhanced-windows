using System.Collections.ObjectModel;
using Kino.Core;
using Kino.Core.Chat;
using Kino.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Kino;

/// <summary>Chat, GIFs and reactions (§18).</summary>
public sealed partial class MainWindow
{
    private readonly ObservableCollection<ChatRow> _chatItems = [];
    private readonly KinoSettings _settings = KinoSettings.Load();
    private int _unread;
    private bool _gifsLoaded;

    private void AttachChat(ScreenSession s)
    {
        ChatList.ItemsSource = _chatItems;
        RebuildChat();
        BuildReactionBar();
        s.ChatReceived += m => _ui.TryEnqueue(() => { if (_session == s) AppendChat(m, countUnread: true); });
        s.ChatReset += () => _ui.TryEnqueue(() => { if (_session == s) RebuildChat(); });
        s.ReactionReceived += r => _ui.TryEnqueue(() => { if (_session == s) ShowReaction(r); });
        ReactionPopup.IsOpen = _page == Page.Video;
    }

    private void DetachChat()
    {
        _chatItems.Clear();
        _unread = 0;
        ChatTab.Text = "Chat";
        ReactionPopup.IsOpen = false;
        ReactionStack.Children.Clear();
        GifPicker.Visibility = Visibility.Collapsed;
        GifToggle.IsChecked = false;
    }

    private bool ChatVisible => SideColumn.Width.Value > 0 && SideTabs.SelectedItem == ChatTab;

    private void RebuildChat()
    {
        if (_session is not { } s) return;
        _chatItems.Clear();
        foreach (var m in s.Chat) AppendChat(m, countUnread: false);
    }

    private void AppendChat(ChatMessage m, bool countUnread)
    {
        var last = _chatItems.LastOrDefault()?.Message;
        bool continuation = last is { IsSystem: false } && !m.IsSystem && last.From == m.From && m.At - last.At < 120_000;
        _chatItems.Add(new ChatRow(m, NameBrushFor(m.From), continuation));
        ChatList.ScrollIntoView(_chatItems[^1]);
        if (countUnread && !m.IsSystem && m.From != _session?.Self?.Id && !ChatVisible)
        {
            _unread++;
            ChatTab.Text = $"Chat ({_unread})";
        }
    }

    /// <summary>Names take the colour of the sender's top role, like the viewers panel.</summary>
    private Brush NameBrushFor(string? viewerId)
    {
        var fallback = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        if (_session is not { } s || viewerId is null || s.Viewers.FirstOrDefault(v => v.Id == viewerId) is not { } v) return fallback;
        var row = new ViewerRow(v, s.Roles, isSelf: false);
        return v.IsHost || v.CoHost is not null || v.Roles.Length > 0 ? row.ColourBrush : fallback;
    }

    /// <summary>Who may chat right now (permissions and mute). The theatre enforces it regardless.</summary>
    private void UpdateChatPermissions()
    {
        if (_session is not { } s) return;
        bool muted = s.Self?.Muted == true;
        ChatInput.IsEnabled = SendChatButton.IsEnabled = s.Can(Permissions.ChatSend) && !muted;
        ChatInput.PlaceholderText = muted ? "You are muted in this screen" : s.Can(Permissions.ChatSend) ? "Message" : "You can't send messages here";
        GifToggle.Visibility = s.Can(Permissions.ChatGif) && !muted ? Visibility.Visible : Visibility.Collapsed;
        ReactionBar.Visibility = s.Can(Permissions.ChatReact) && !muted ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Sending -------------------------------------------------------------------------------

    private async void OnSendChatClick(object sender, RoutedEventArgs e) => await SendChatAsync();

    private async void OnChatInputKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) await SendChatAsync();
    }

    private async Task SendChatAsync()
    {
        if (_session is not { } s) return;
        string text = ChatInput.Text.Trim();
        if (text.Length == 0) return;
        ChatInput.Text = "";
        // A pasted GIF link is sent as a GIF, so it shows inline.
        if (GiphyClient.IsGifLink(text) && s.Can(Permissions.ChatGif)) await s.SendGifAsync(new Gif(text, null, null, null));
        else await s.SendChatAsync(text);
    }

    private void BuildReactionBar()
    {
        if (ReactionBar.Children.Count > 0) return;
        foreach (string emoji in ScreenSession.Reactions)
        {
            var b = new Button { Content = emoji, Padding = new Thickness(6, 2, 6, 2), FontSize = 16, MinWidth = 0 };
            b.Click += async (_, _) => { if (_session is { } s) await s.ReactAsync(emoji); };
            ReactionBar.Children.Add(b);
        }
    }

    /// <summary>Floats "🎉 Alex" over the video for a few seconds.</summary>
    private void ShowReaction(Reaction r)
    {
        var bubble = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB0, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4, 12, 4),
            Child = new TextBlock { Text = $"{r.Emoji}  {r.Name}", FontSize = 18, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
        };
        ReactionStack.Children.Add(bubble);
        while (ReactionStack.Children.Count > 6) ReactionStack.Children.RemoveAt(0);
        ReactionPopup.IsOpen = _page == Page.Video;
        LayoutOverlays();

        var timer = _ui.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(3);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => ReactionStack.Children.Remove(bubble);
        timer.Start();
    }

    // ---- GIF picker ------------------------------------------------------------------------------

    private async void OnGifToggle(object sender, RoutedEventArgs e)
    {
        bool open = GifToggle.IsChecked == true;
        GifPicker.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (!open) return;
        GifKeyRow.Visibility = string.IsNullOrWhiteSpace(_settings.GiphyApiKey) ? Visibility.Visible : Visibility.Collapsed;
        if (GifKeyRow.Visibility == Visibility.Collapsed && !_gifsLoaded) await LoadGifsAsync(null);
        GifSearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnGifSearchKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) await LoadGifsAsync(GifSearchBox.Text.Trim());
    }

    private async void OnSaveGifKeyClick(object sender, RoutedEventArgs e)
    {
        string key = GifKeyBox.Text.Trim();
        if (key.Length == 0) return;
        _settings.GiphyApiKey = key;
        try { _settings.Save(); } catch (IOException) { /* still usable for this run */ }
        GifKeyRow.Visibility = Visibility.Collapsed;
        await LoadGifsAsync(null);
    }

    /// <summary>Trending when the query is empty. The client talks to GIPHY directly; only the chosen URL is sent.</summary>
    private async Task LoadGifsAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(_settings.GiphyApiKey)) { GifKeyRow.Visibility = Visibility.Visible; return; }
        GifSearchBox.Header = query is { Length: > 0 } ? $"Searching “{query}”…" : "Trending";
        try
        {
            var giphy = new GiphyClient(_settings.GiphyApiKey!, Http);
            var results = query is { Length: > 0 } ? await giphy.SearchAsync(query) : await giphy.TrendingAsync();
            GifResults.ItemsSource = results.Select(r => new GifTile(r)).ToList();
            GifSearchBox.Header = results.Count == 0 ? "No GIFs found" : query is { Length: > 0 } ? null : "Trending";
            _gifsLoaded = true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            GifSearchBox.Header = "GIPHY rejected the API key. Check it and save it again.";
            GifKeyRow.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            GifSearchBox.Header = "Couldn't reach GIPHY. You can paste a GIF link into the message box instead.";
        }
    }

    private async void OnGifClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GifTile tile || _session is not { } s) return;
        GifToggle.IsChecked = false;
        GifPicker.Visibility = Visibility.Collapsed;
        await s.SendGifAsync(tile.Gif);
    }
}
