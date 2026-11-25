using Kino.Core;
using Kino.Core.Lobby;
using Kino.Core.Net;
using Kino.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Kino;

/// <summary>Theatres, the unified lobby, joining and creating screens.</summary>
public sealed partial class MainWindow
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan ProbeMaxAge = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LobbyRefreshEvery = TimeSpan.FromSeconds(15);

    private TheatreNetwork? _network;
    private Task<TheatreProbe[]>? _probes;
    private DateTime _probedAt;
    private readonly Dictionary<string, double> _pings = [];

    private TheatreLobby[]? _lobbies;
    private DateTime _lobbyUpdated;
    private bool _refreshing;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _lobbyTimer;
    private bool _lobbyVisible;

    /// <summary>A resolved private screen waiting for its password.</summary>
    private ResolvedScreen? _pendingJoin;

    // ---- Lobby visibility -------------------------------------------------------------------

    private enum Page { Video, Lobby, Roles }

    private Page _page = Page.Video;

    private void ShowLobby(bool show) => ShowPage(show ? Page.Lobby : Page.Video);

    /// <summary>Lobby and roles pages cover the stage. The native video window is hidden then (XAML can't draw over it).</summary>
    private void ShowPage(Page page)
    {
        _page = page;
        bool show = page == Page.Lobby;
        _lobbyVisible = show;
        LobbyPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RolesPanel.Visibility = page == Page.Roles ? Visibility.Visible : Visibility.Collapsed;
        _videoHost.Visible = page == Page.Video;
        OverlayPopup.IsOpen = page == Page.Video && OverlayToggle.IsChecked == true;
        LobbyButton.Content = page == Page.Video ? "Lobby" : "Back to video";

        if (_lobbyTimer is null)
        {
            _lobbyTimer = _ui.CreateTimer();
            _lobbyTimer.Interval = LobbyRefreshEvery;
            _lobbyTimer.Tick += async (_, _) => await RefreshLobbyAsync(probe: false);
        }
        if (show)
        {
            _ = RefreshLobbyAsync(probe: false);
            _lobbyTimer.Start();
        }
        else
        {
            _lobbyTimer.Stop();
        }
    }

    private void OnLobbyButtonClick(object sender, RoutedEventArgs e) => ShowPage(_page == Page.Video ? Page.Lobby : Page.Video);

    // ---- Theatres ----------------------------------------------------------------------------

    /// <summary>Loads the theatre list (directory → cache → bundled, or the --theatre override).</summary>
    private async Task LoadTheatresAsync()
    {
        TheatreList list;
        if (_opt.Theatre is { } url)
        {
            var u = new Uri(url);
            list = new([new TheatreEntry("custom", u.Authority, url)], DirectorySource.Override);
        }
        else
        {
            var dir = _opt.Directory is { } d ? new Uri(d) : TheatreDirectory.DefaultUrl;
            list = await TheatreDirectory.LoadAsync(Http, dir, TheatreDirectory.DefaultCachePath);
        }

        _network = new TheatreNetwork(list.Theatres, Http);
        ToolTipService.SetToolTip(TheatrePicker, $"Theatre list: {list.Source.ToString().ToLowerInvariant()}");
        RefreshTheatrePicker(null);
        TheatrePicker.IsEnabled = true;

        TheatreFilterBox.Items.Clear();
        TheatreFilterBox.Items.Add("All theatres");
        foreach (var t in list.Theatres) TheatreFilterBox.Items.Add(t.Name);
        TheatreFilterBox.SelectedIndex = 0;

        if (_lobbyVisible) _ = RefreshLobbyAsync(probe: true);
        else _ = ProbeTheatresAsync();
    }

    /// <summary>Pings every theatre (two /info requests each; the second, on a warm connection, is timed).</summary>
    private async Task<TheatreProbe[]> ProbeTheatresAsync()
    {
        _probedAt = DateTime.UtcNow;
        var task = _network!.ProbeAllAsync();
        _probes = task;
        var probes = await task;
        if (_probes == task)
        {
            foreach (var p in probes)
            {
                if (p.Usable && p.RttMs is double rtt) _pings[p.Theatre.Id] = rtt;
                else _pings.Remove(p.Theatre.Id);
            }
            RefreshTheatrePicker(probes);
        }
        return probes;
    }

    /// <summary>Item 0 is "Fastest"; item i is <c>_network.Theatres[i - 1]</c>.</summary>
    private void RefreshTheatrePicker(TheatreProbe[]? probes)
    {
        int selected = Math.Max(0, TheatrePicker.SelectedIndex);
        var fastest = probes is null ? null : TheatreNetwork.Fastest(probes);

        TheatrePicker.Items.Clear();
        TheatrePicker.Items.Add(fastest is null ? "Fastest theatre" : $"Fastest ({TheatreName(fastest)})");
        for (int i = 0; i < _network!.Theatres.Count; i++)
        {
            var p = probes?[i];
            string name = p is null ? _network.Theatres[i].Name : TheatreName(p);
            string state = p is null ? "" : p.Usable ? $" · {p.RttMs:0} ms" : " · unavailable";
            TheatrePicker.Items.Add(name + state);
        }
        TheatrePicker.SelectedIndex = Math.Min(selected, TheatrePicker.Items.Count - 1);
    }

    private static string TheatreName(TheatreProbe p) => p.Info?.Name ?? p.Theatre.Name;

    /// <summary>The picked theatre, or the fastest usable one. Re-pings if the last ping is stale.</summary>
    private async Task<TheatreEntry> TheatreForNewScreenAsync()
    {
        if (_network is null || _probes is null) throw new InvalidOperationException("The theatre list is still loading.");
        var probes = DateTime.UtcNow - _probedAt > ProbeMaxAge ? await ProbeTheatresAsync() : await _probes;

        int idx = TheatrePicker.SelectedIndex;
        if (idx > 0)
        {
            var p = probes[idx - 1];
            return p.Usable ? p.Theatre
                : throw new InvalidOperationException($"{TheatreName(p)} is unavailable right now ({p.Failure ?? p.Info?.Status ?? "unknown"}).");
        }
        return TheatreNetwork.Fastest(probes)?.Theatre ?? throw new InvalidOperationException("No theatre is reachable right now.");
    }

    // ---- Lobby list ---------------------------------------------------------------------------

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshLobbyAsync(probe: true);

    /// <summary>Fetches every theatre's lobby in parallel. Auto-refresh skips the ping to save requests.</summary>
    private async Task RefreshLobbyAsync(bool probe)
    {
        if (_network is null || _refreshing) return;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        try
        {
            if (probe || _pings.Count == 0) await ProbeTheatresAsync();
            _lobbies = await _network.LobbyAllAsync();
            _lobbyUpdated = DateTime.Now;
            RenderLobby();
        }
        finally
        {
            _refreshing = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void OnLobbyQueryChanged(object sender, object e) => RenderLobby();

    private LobbyQuery CurrentQuery() => new(
        Search: SearchBox.Text,
        Visibility: (VisibilityFilter)Math.Max(0, VisibilityFilterBox.SelectedIndex),
        TheatreId: TheatreFilterBox.SelectedIndex > 0 && _network is not null ? _network.Theatres[TheatreFilterBox.SelectedIndex - 1].Id : null,
        Sort: (LobbySort)Math.Max(0, SortBox.SelectedIndex));

    private void RenderLobby()
    {
        if (_lobbies is null || LobbyList is null) return; // also called while XAML is still initialising

        var all = LobbyView.Merge(_lobbies, _pings);
        var shown = LobbyView.Apply(all, CurrentQuery());
        LobbyList.ItemsSource = shown.Select(e => new LobbyRow(e)).ToList();

        TheatreStrip.Children.Clear();
        foreach (var st in LobbyView.Statuses(_lobbies, _pings))
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                Opacity = st.Failure is null ? 1 : 0.5,
                Child = new TextBlock
                {
                    FontSize = 12,
                    Text = st.Failure is not null ? $"{st.Theatre.Name} · unavailable"
                         : st.PingMs is double p ? $"{st.Theatre.Name} · {p:0} ms" : st.Theatre.Name,
                },
            };
            if (st.Failure is not null) ToolTipService.SetToolTip(badge, st.Failure);
            TheatreStrip.Children.Add(badge);
        }

        string updated = $"updated {_lobbyUpdated:HH:mm:ss}";
        LobbyStatusText.Text = all.Count == 0 ? $"No open screens right now. Create one, or join with a code · {updated}"
            : shown.Count == 0 ? $"No screens match · {all.Count} open · {updated}"
            : $"{shown.Count} of {all.Count} screens · {updated}";
    }

    private async void OnLobbyItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LobbyRow { Entry: var entry } || _network is null) return;

        if (_session is not null && _sessionTheatre?.Id == entry.Theatre.Id && _session.Code == entry.Screen.Code)
        {
            ShowLobby(false); // already in it
            return;
        }
        if (entry.IsFull)
        {
            ShowJoinMessage($"“{entry.Screen.Name}” is full.");
            return;
        }

        CodeBox.Text = entry.Screen.Code;
        var api = _network.Api(entry.Theatre);
        ResolveResponse? screen;
        try { screen = await api.ResolveAsync(entry.Screen.Code); }
        catch (Exception ex) { ShowJoinMessage(FriendlyError(ex)); return; }

        if (screen is null)
        {
            ShowJoinMessage($"“{entry.Screen.Name}” has closed.");
            await RefreshLobbyAsync(probe: false);
            return;
        }
        await BeginJoinAsync(new ResolvedScreen(api, entry.Theatre, screen));
    }

    // ---- Join ----------------------------------------------------------------------------------

    private async void OnJoinClick(object sender, RoutedEventArgs e) => await JoinFromFormAsync();

    private async void OnCodeKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) await JoinFromFormAsync();
    }

    private async void OnJoinPasswordKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) await JoinFromFormAsync();
    }

    private void OnCodeChanged(object sender, TextChangedEventArgs e)
    {
        if (_pendingJoin is not null && !string.Equals(CodeBox.Text.Trim(), _pendingJoin.Screen.Code, StringComparison.OrdinalIgnoreCase))
            ResetJoinForm(keepCode: true);
    }

    private void OnCancelJoinClick(object sender, RoutedEventArgs e) => ResetJoinForm(keepCode: false);

    /// <summary>Join button / Enter: resolve the code on every theatre, or submit the password for a pending screen.</summary>
    private async Task JoinFromFormAsync()
    {
        if (_pendingJoin is { } pending)
        {
            await JoinAsync(pending, JoinPasswordBox.Password);
            return;
        }

        string code = CodeBox.Text.Trim();
        if (code.Length == 0) { ShowJoinMessage("Enter a screen code."); return; }
        await JoinByCodeAsync(code);
    }

    /// <summary>Codes are looked up on every theatre in parallel, so the viewer never has to pick one.</summary>
    private async Task JoinByCodeAsync(string code)
    {
        if (_network is null) { ShowJoinMessage("The theatre list is still loading."); return; }
        JoinButton.IsEnabled = false;
        try
        {
            var (found, unreachable) = await _network.ResolveAsync(code);
            if (found is null)
            {
                ShowJoinMessage(unreachable > 0
                    ? $"No screen with code {code} ({unreachable} theatre{(unreachable == 1 ? " is" : "s are")} unreachable, so it may be there)."
                    : $"No screen with code {code}.");
                return;
            }
            await BeginJoinAsync(found);
        }
        catch (Exception ex) { ShowJoinMessage(FriendlyError(ex)); }
        finally { JoinButton.IsEnabled = true; }
    }

    /// <summary>Public screens join straight away; private ones ask for the password first.</summary>
    private async Task BeginJoinAsync(ResolvedScreen found)
    {
        if (!found.Screen.RequiresPassword)
        {
            await JoinAsync(found, null);
            return;
        }
        _pendingJoin = found;
        if (!_lobbyVisible) ShowLobby(true); // e.g. --join on a private screen: the prompt lives in the lobby
        CodeBox.Text = found.Screen.Code;
        JoinTargetText.Text = $"“{found.Screen.Name}” on {found.Theatre.Name} is private. Enter its password.";
        JoinTargetText.Visibility = JoinPasswordBox.Visibility = CancelJoinButton.Visibility = Visibility.Visible;
        JoinMessage.Visibility = Visibility.Collapsed;
        JoinPasswordBox.Password = "";
        JoinPasswordBox.Focus(FocusState.Programmatic);
    }

    private async Task JoinAsync(ResolvedScreen found, string? password)
    {
        bool ok = await RunSessionAction(async () =>
        {
            var session = await ScreenSession.JoinResolvedAsync(found.Api, _identity, _player, found.Screen, DisplayName(), password);
            return (session, found.Theatre);
        }, ShowJoinMessage);

        if (ok) ResetJoinForm(keepCode: false);
        else if (_pendingJoin is not null)
        {
            JoinPasswordBox.Password = "";
            JoinPasswordBox.Focus(FocusState.Programmatic);
        }
    }

    private void ResetJoinForm(bool keepCode)
    {
        _pendingJoin = null;
        if (!keepCode) CodeBox.Text = "";
        JoinPasswordBox.Password = "";
        JoinTargetText.Visibility = JoinPasswordBox.Visibility = CancelJoinButton.Visibility = Visibility.Collapsed;
        JoinMessage.Visibility = Visibility.Collapsed;
    }

    private void ShowJoinMessage(string text)
    {
        if (!_lobbyVisible) ShowLobby(true); // errors are shown in the lobby, which may be hidden after --join
        JoinMessage.Text = text;
        JoinMessage.Visibility = Visibility.Visible;
    }

    // ---- Create --------------------------------------------------------------------------------

    private bool CreatePrivate => PrivateSwitch.IsOn;

    private void OnCreateFormChanged(object sender, object e)
    {
        if (CreateButton is null) return; // XAML still initialising
        var priv = CreatePrivate ? Visibility.Visible : Visibility.Collapsed;
        CreatePasswordBox.Visibility = HideFromLobbyBox.Visibility = priv;
        CreateButton.IsEnabled = ScreenNameBox.Text.Trim().Length > 0 && (!CreatePrivate || CreatePasswordBox.Password.Length > 0);
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        int max = double.IsNaN(MaxViewersBox.Value) ? 10 : (int)Math.Clamp(Math.Round(MaxViewersBox.Value), 2, 25);
        var req = CreatePrivate
            ? new CreateScreenRequest(ScreenNameBox.Text.Trim(), "private", CreatePasswordBox.Password, HideFromLobbyBox.IsChecked == true, max)
            : new CreateScreenRequest(ScreenNameBox.Text.Trim(), "public", MaxViewers: max);

        CreateMessage.Visibility = Visibility.Collapsed;
        if (await CreateScreenAsync(req))
        {
            CreatePasswordBox.Password = "";
        }
    }

    private Task<bool> CreateScreenAsync(CreateScreenRequest req) =>
        RunSessionAction(async () =>
        {
            var theatre = await TheatreForNewScreenAsync();
            var (session, created) = await ScreenSession.CreateAsync(_network!.Api(theatre), _identity, _player, req, DisplayName());
            if (_opt.CodeFile is not null) await File.WriteAllTextAsync(_opt.CodeFile, created.Code);
            return (session, theatre);
        }, text =>
        {
            if (!_lobbyVisible) ShowLobby(true);
            CreateMessage.Text = text;
            CreateMessage.Visibility = Visibility.Visible;
        });

    // ---- Shared ----------------------------------------------------------------------------------

    /// <summary>Leaves any current screen, runs <paramref name="action"/>, and attaches the new session.</summary>
    private async Task<bool> RunSessionAction(Func<Task<(ScreenSession Session, TheatreEntry Theatre)>> action, Action<string> showError)
    {
        CreateButton.IsEnabled = JoinButton.IsEnabled = false;
        try
        {
            await LeaveSessionAsync();
            SessionText.Text = "Connecting…";
            var (s, theatre) = await action();
            _sessionTheatre = theatre;
            AttachSession(s);
            return true;
        }
        catch (Exception ex)
        {
            SessionText.Text = "Not in a screen";
            showError(FriendlyError(ex));
            return false;
        }
        finally
        {
            JoinButton.IsEnabled = true;
            OnCreateFormChanged(this, EventArgs.Empty);
        }
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        TheatreException { Code: "wrong_password" } => "Wrong password.",
        TheatreException { Code: "full" } => "That screen is full.",
        TheatreException { Code: "banned" } => "You are banned from that screen.",
        TheatreException { Code: "closed" } => "That screen has closed.",
        TheatreException { Code: "update_required" } => "This version of Kino is too old for that theatre. Please update.",
        TheatreException { Status: 429 } => "Too many attempts. Wait a minute and try again.",
        HttpRequestException or TaskCanceledException => "Couldn't reach the theatre. Check your connection and try again.",
        _ => ex.Message,
    };
}
