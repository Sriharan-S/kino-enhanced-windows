using System.Runtime.InteropServices;
using Kino.Core;
using Kino.Core.Net;
using Kino.Core.Protocol;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Kino;

/// <summary>Invite (code, links, regenerate) and screen settings; invite links and forwarded launches.</summary>
public sealed partial class MainWindow
{
    private void OnScreenClick(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        SettingsMessage.Visibility = Visibility.Collapsed;
        ShowPage(Page.Screen);
        RenderScreenPage(resetFields: true);
    }

    private void OnScreenDoneClick(object sender, RoutedEventArgs e) => ShowPage(Page.Video);

    private InviteLink? CurrentInvite() =>
        _session is { Code.Length: > 0 } s && _sessionTheatre is { } t ? new InviteLink(t.Id, s.Code) : null;

    /// <summary>Fills the page. Settings fields are reset only when asked, so a refresh doesn't wipe edits in progress.</summary>
    private void RenderScreenPage(bool resetFields)
    {
        if (_session is not { Screen: { } screen } s) return;

        bool listed = _sessionTheatre is { Id: not "custom" };
        InviteCodeText.Text = s.Code;
        InviteLinkText.Text = listed ? CurrentInvite()!.WebLink : "";
        CopyInviteButton.IsEnabled = listed;
        RegenerateCodeButton.Visibility = s.Can(Permissions.ScreenInvite) ? Visibility.Visible : Visibility.Collapsed;
        InviteHint.Text = !listed ? "This theatre isn't in the directory, so share the code instead of a link."
            : screen.IsPrivate ? "Anyone with the link or code also needs the password."
            : "Anyone with the link or code can join.";

        bool canEdit = s.Can(Permissions.ScreenSettings);
        SettingsHint.Text = canEdit ? "Changes apply to everyone at once. Viewers already here keep their place."
                                    : "You can see the settings, but not change them.";
        SaveSettingsButton.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
        foreach (var c in new Control[] { SettingsNameBox, SettingsPrivateSwitch, SettingsMaxViewersBox, SettingsPasswordBox, SettingsHiddenBox, SettingsDevicesSwitch })
            c.IsEnabled = canEdit;

        if (!resetFields) return;
        SettingsNameBox.Text = screen.Name;
        SettingsPrivateSwitch.IsOn = screen.IsPrivate;
        SettingsMaxViewersBox.Value = screen.MaxViewers;
        SettingsHiddenBox.IsChecked = screen.Hidden;
        SettingsDevicesSwitch.IsOn = screen.ShowDevices;
        SettingsPasswordBox.Password = "";
        OnSettingsPrivateToggled(this, new RoutedEventArgs());
    }

    private void OnSettingsPrivateToggled(object sender, RoutedEventArgs e)
    {
        if (SettingsPasswordBox is null) return; // XAML still initialising
        bool priv = SettingsPrivateSwitch.IsOn;
        SettingsPasswordBox.Visibility = SettingsHiddenBox.Visibility = priv ? Visibility.Visible : Visibility.Collapsed;
        SettingsPasswordBox.PlaceholderText = _session?.Screen?.IsPrivate == true ? "Leave empty to keep the current password" : "Required";
    }

    private async void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_session is not { Screen: { } screen } s) return;
        SettingsMessage.Visibility = Visibility.Collapsed;

        string name = SettingsNameBox.Text.Trim();
        bool priv = SettingsPrivateSwitch.IsOn;
        string password = SettingsPasswordBox.Password;
        if (name.Length == 0) { ShowActionError("The screen needs a name."); return; }
        if (priv && !screen.IsPrivate && password.Length == 0) { ShowActionError("Set a password to make the screen private."); return; }
        int max = double.IsNaN(SettingsMaxViewersBox.Value) ? screen.MaxViewers : (int)Math.Clamp(Math.Round(SettingsMaxViewersBox.Value), 2, 25);

        await s.UpdateSettingsAsync(
            name: name == screen.Name ? null : name,
            isPrivate: priv == screen.IsPrivate && password.Length == 0 ? null : priv,
            password: priv ? password : null,
            hidden: priv ? SettingsHiddenBox.IsChecked == true : null,
            maxViewers: max == screen.MaxViewers ? null : max,
            showDevices: SettingsDevicesSwitch.IsOn == screen.ShowDevices ? null : SettingsDevicesSwitch.IsOn);
        SettingsPasswordBox.Password = "";
    }

    private void OnCopyInviteClick(object sender, RoutedEventArgs e)
    {
        if (CurrentInvite() is not { } link) return;
        string text = _session?.Screen?.IsPrivate == true
            ? $"Join my screen on Kino: {link.WebLink} (code {link.Code}; ask me for the password)"
            : $"Join my screen on Kino: {link.WebLink} (code {link.Code})";
        CopyText(text);
        InviteHint.Text = "Invite copied.";
    }

    private void OnCopyCodeClick(object sender, RoutedEventArgs e)
    {
        if (_session is { Code.Length: > 0 } s) CopyText(s.Code);
    }

    private static void CopyText(string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
    }

    private void OnRegenerateCodeClick(object sender, RoutedEventArgs e)
    {
        if (_session is not { } s) return;
        var menu = new MenuFlyout();
        var yes = new MenuFlyoutItem { Text = "Make a new code (the old code and links stop working)" };
        yes.Click += async (_, _) => await s.RegenerateCodeAsync();
        menu.Items.Add(yes);
        menu.ShowAt(RegenerateCodeButton);
    }

    // ---- Invite links and later launches ---------------------------------------------------------

    /// <summary>Joins from an invite: looks on the named theatre first, then everywhere.</summary>
    private async Task JoinInviteAsync(InviteLink link)
    {
        if (_network is null) { ShowJoinMessage("The theatre list is still loading."); return; }
        if (_session is not null && _session.Code == link.Code && _sessionTheatre?.Id == link.TheatreId)
        {
            ShowPage(Page.Video); // already there
            return;
        }

        var theatre = _network.Theatres.FirstOrDefault(t => t.Id == link.TheatreId);
        if (theatre is null)
        {
            await JoinByCodeAsync(link.Code); // a theatre we don't know (e.g. an older list): try them all
            return;
        }
        try
        {
            var api = _network.Api(theatre);
            var screen = await api.ResolveAsync(link.Code);
            if (screen is null)
            {
                ShowJoinMessage($"That invite has expired: there's no screen {link.Code} on {theatre.Name}.");
                return;
            }
            await BeginJoinAsync(new ResolvedScreen(api, theatre, screen));
        }
        catch (Exception ex)
        {
            ShowJoinMessage(FriendlyError(ex));
        }
    }

    /// <summary>A second launch handed its arguments to this window (see <see cref="SingleInstance"/>).</summary>
    internal async void OnForwardedLaunch(LaunchOptions options)
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
        SetForegroundWindow(_hwnd);

        if (options.Media is not null) OpenMedia(options.Media);
        if (options.Invite is { } link) await JoinInviteAsync(link);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
