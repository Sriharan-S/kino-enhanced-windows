using Kino.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kino;

/// <summary>The viewers side panel: who's here, their roles, and moderation / co-host / role actions.</summary>
public sealed partial class MainWindow
{
    private const double SidePanelWidth = 290;

    private void OnViewersToggle(object sender, RoutedEventArgs e)
    {
        SideColumn.Width = new GridLength(ViewersToggle.IsChecked == true ? SidePanelWidth : 0);
        RenderViewers();
    }

    private void RenderViewers()
    {
        if (_session is not { } s)
        {
            ViewersList.ItemsSource = null;
            return;
        }
        var roles = s.Roles;
        var ordered = s.Viewers
            .OrderByDescending(v => Permissions.RankOf(v, roles))
            .ThenBy(v => v.CoHost ?? int.MaxValue)
            .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase);
        ViewersList.ItemsSource = ordered.Select(v => new ViewerRow(v, roles, v.Id == s.Self?.Id)).ToList();
        ViewersHeader.Text = $"Viewers ({s.Viewers.Count})";

        var present = s.Viewers.Select(v => v.Id).ToHashSet();
        var away = s.CoHosts.Select((c, i) => (c, i)).Where(x => !present.Contains(x.c.Id)).ToList();
        AwayCoHostsText.Text = "Away: " + string.Join(", ", away.Select(x => $"{x.c.Name} (co-host #{x.i + 1})"));
        AwayCoHostsText.Visibility = away.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        RolesButton.Visibility = s.Can(Permissions.RolesManage) ? Visibility.Visible : Visibility.Collapsed;
        CloseScreenButton.Visibility = s.IsHost ? Visibility.Visible : Visibility.Collapsed;
        ReadyCheckButton.Visibility = s.Can(Permissions.ReadyCheck) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Shows a rejected action's reason where the user is looking.</summary>
    private void ShowActionError(string message)
    {
        if (_page == Page.Roles)
        {
            RolesMessage.Text = message;
            RolesMessage.Visibility = Visibility.Visible;
        }
        else
        {
            OverlayText.Text = message;
        }
    }

    private void OnViewerClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ViewerRow row || _session is not { Self: { } me } s) return;
        if (ViewersList.ContainerFromItem(e.ClickedItem) is not FrameworkElement anchor) return;
        BuildViewerMenu(row.Viewer, me).ShowAt(anchor);
    }

    /// <summary>
    /// Offers only what the theatre would allow: permission plus the strictly-below rule; host-only actions for the host.
    /// The theatre enforces it all anyway.
    /// </summary>
    private MenuFlyout BuildViewerMenu(ViewerInfo target, ViewerInfo me)
    {
        var s = _session!;
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false }; // windowed, so it can overlap the video
        bool self = target.Id == me.Id;
        int myRank = Permissions.RankOf(me, s.Roles);
        bool below = !self && myRank > Permissions.RankOf(target, s.Roles);

        void Add(string text, Func<Task> action, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }

        void Confirm(string text, string confirm, Func<Task> action)
        {
            var sub = new MenuFlyoutSubItem { Text = text };
            var yes = new MenuFlyoutItem { Text = confirm };
            yes.Click += async (_, _) => await action();
            sub.Items.Add(yes);
            menu.Items.Add(sub);
        }

        // Host-only: co-hosts and ownership (§7).
        if (me.IsHost && !self)
        {
            int index = s.CoHosts.ToList().FindIndex(c => c.Id == target.Id);
            if (index < 0)
            {
                Add("Make co-host", () => s.SetCoHostAsync(target.Id, true));
            }
            else
            {
                Add("Remove co-host", () => s.SetCoHostAsync(target.Id, false));
                if (index > 0) Add("Move up (takes over sooner)", () => s.MoveCoHostAsync(target.Id, -1));
                if (index < s.CoHosts.Count - 1) Add("Move down", () => s.MoveCoHostAsync(target.Id, +1));
            }
            Confirm("Make host", $"Yes, make {target.Name} the host (you become co-host #1)", () => s.TransferHostAsync(target.Id));
        }

        // Roles (§8).
        var assignable = s.Roles.Where(r => !r.Builtin && r.Position < myRank).OrderByDescending(r => r.Position).ToList();
        if (below && me.Can(Permissions.RolesAssign) && assignable.Count > 0)
        {
            var sub = new MenuFlyoutSubItem { Text = "Roles" };
            foreach (var role in assignable)
            {
                bool has = target.Roles.Contains(role.Id);
                var item = new ToggleMenuFlyoutItem { Text = role.Name, IsChecked = has };
                item.Click += async (_, _) => await s.AssignRoleAsync(target.Id, role.Id, !has);
                sub.Items.Add(item);
            }
            menu.Items.Add(sub);
        }
        if (below && me.Can(Permissions.RolesManage))
        {
            Add("Permissions…", () =>
            {
                OpenRolesPage(target);
                return Task.CompletedTask;
            });
        }

        // Moderation (§9).
        var moderation = new List<Action>();
        if (below && me.Can(Permissions.ViewersMute))
            moderation.Add(() => Add(target.Muted ? "Unmute chat" : "Mute chat", () => s.MuteAsync(target.Id, !target.Muted)));
        if (below && me.Can(Permissions.ViewersKick))
            moderation.Add(() => Add("Kick (can rejoin)", () => s.KickAsync(target.Id)));
        if (below && me.Can(Permissions.ViewersBan))
            moderation.Add(() => Confirm("Ban", $"Yes, ban {target.Name} from this screen", () => s.BanAsync(target.Id)));
        if (moderation.Count > 0)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var add in moderation) add();
        }

        if (menu.Items.Count == 0)
        {
            string why = self ? "That's you" : below ? "You don't have permission to manage viewers" : $"{target.Name} ranks the same as or above you";
            Add(why, () => Task.CompletedTask, enabled: false);
        }
        return menu;
    }

    private void OnCloseScreenClick(object sender, RoutedEventArgs e)
    {
        if (_session is not { IsHost: true } s) return;
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };
        var yes = new MenuFlyoutItem { Text = "Close the screen for everyone" };
        yes.Click += async (_, _) => await s.CloseScreenAsync();
        menu.Items.Add(yes);
        menu.ShowAt(CloseScreenButton);
    }
}
