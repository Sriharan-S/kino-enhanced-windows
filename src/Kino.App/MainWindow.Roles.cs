using Kino.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kino;

/// <summary>
/// The roles page: create, edit and delete roles, and edit one viewer's permission overrides.
/// It only offers what the theatre would accept (rank and "can't grant what you lack"); the theatre enforces it.
/// </summary>
public sealed partial class MainWindow
{
    private string? _editRoleId;
    private bool _creatingRole;
    private string? _overridesFor;
    private string? _selectRoleNamed;
    private string _editorSignature = "";
    private bool _suppressRoleSelection;
    private readonly Dictionary<string, CheckBox> _permChecks = [];
    private readonly Dictionary<string, ComboBox> _overrideBoxes = [];

    private void OnRolesClick(object sender, RoutedEventArgs e) => OpenRolesPage(null);

    private void OnRolesDoneClick(object sender, RoutedEventArgs e) => ShowPage(Page.Video);

    private void OpenRolesPage(ViewerInfo? overridesFor)
    {
        if (_session is null) return;
        _overridesFor = overridesFor?.Id;
        _creatingRole = false;
        _editorSignature = "";
        RolesMessage.Visibility = Visibility.Collapsed;
        if (RoleColourBox.Items.Count == 0)
            foreach (var (label, _) in RoleColours.Presets) RoleColourBox.Items.Add(label);
        ShowPage(Page.Roles);
        RefreshRolesPage();
    }

    /// <summary>Re-renders after roles or viewers change. The editor is rebuilt only if what it shows changed.</summary>
    private void RefreshRolesPage()
    {
        if (_session is not { Self: { } me } s) return;

        var ordered = s.Roles.OrderByDescending(r => r.Position).ToList();
        if (_selectRoleNamed is { } wanted && ordered.FirstOrDefault(r => r.Name == wanted && !r.Builtin) is { } created)
        {
            _editRoleId = created.Id;
            _selectRoleNamed = null;
            _creatingRole = false;
        }
        if (_editRoleId is not null && ordered.All(r => r.Id != _editRoleId)) _editRoleId = null; // deleted
        _editRoleId ??= _overridesFor is null && !_creatingRole ? ordered.FirstOrDefault()?.Id : null;

        _suppressRoleSelection = true;
        var rows = ordered.Select(r => new RoleRow(r)).ToList();
        RolesList.ItemsSource = rows;
        RolesList.SelectedItem = _overridesFor is null && !_creatingRole ? rows.FirstOrDefault(r => r.Role.Id == _editRoleId) : null;
        _suppressRoleSelection = false;

        NewRoleButton.Visibility = s.Can(Permissions.RolesManage) ? Visibility.Visible : Visibility.Collapsed;

        string mine = $"{Permissions.RankOf(me, s.Roles)}|{string.Join(",", me.Permissions)}";
        if (_overridesFor is { } id)
        {
            var target = s.Viewers.FirstOrDefault(v => v.Id == id);
            string sig = $"o|{id}|{mine}|{target?.Name}|{string.Join(",", target?.Allow ?? [])}|{string.Join(",", target?.Deny ?? [])}|{string.Join(",", target?.Roles ?? [])}|{target?.CoHost}|{RolesSignature(s.Roles)}";
            if (sig != _editorSignature) RenderOverridesEditor(target, me);
            _editorSignature = sig;
        }
        else
        {
            var role = _creatingRole ? null : s.Roles.FirstOrDefault(r => r.Id == _editRoleId);
            string sig = _creatingRole ? $"new|{mine}" : $"r|{mine}|{(role is null ? "" : RolesSignature([role]))}";
            if (sig != _editorSignature) RenderRoleEditor(role, me);
            _editorSignature = sig;
        }
    }

    private static string RolesSignature(IEnumerable<Role> roles) =>
        string.Join(";", roles.Select(r => $"{r.Id}:{r.Name}:{r.Colour}:{r.Position}:{string.Join(",", r.Permissions)}"));

    private void OnRoleSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRoleSelection || RolesList.SelectedItem is not RoleRow row) return;
        _overridesFor = null;
        _creatingRole = false;
        _editRoleId = row.Role.Id;
        RolesMessage.Visibility = Visibility.Collapsed;
        RefreshRolesPage();
    }

    private void OnNewRoleClick(object sender, RoutedEventArgs e)
    {
        _overridesFor = null;
        _creatingRole = true;
        _editRoleId = null;
        RolesMessage.Visibility = Visibility.Collapsed;
        RefreshRolesPage();
    }

    // ---- Role editor ------------------------------------------------------------------------

    private void RenderRoleEditor(Role? role, ViewerInfo me)
    {
        var s = _session!;
        int myRank = Permissions.RankOf(me, s.Roles);
        bool isNew = role is null;
        bool coHost = role?.Id == Permissions.CoHostRole;
        bool viewerRole = role?.Id == Permissions.ViewerRole;
        bool editable = s.Can(Permissions.RolesManage) && !coHost && (isNew || role!.Position < myRank) && myRank > 1;

        EditorTitle.Text = isNew ? "New role" : role!.Name;
        EditorHint.Text = coHost ? "Co-hosts have every permission and take over, in priority order, while the host is away. The host picks co-hosts from the viewers list."
            : viewerRole ? "Everyone in the screen has this role." + (editable ? "" : " Only viewers who can manage roles can change it.")
            : !s.Can(Permissions.RolesManage) ? "You can see roles, but not change them."
            : !editable ? $"You can only edit roles ranked below your own (your rank is {myRank})."
            : "Viewers with this role get these permissions. You can't grant permissions you don't have.";

        RoleFields.Visibility = coHost || viewerRole ? Visibility.Collapsed : Visibility.Visible;
        RoleNameBox.Text = role?.Name ?? "New role";
        int colour = Array.FindIndex(RoleColours.Presets, p => p.Hex == role?.Colour);
        RoleColourBox.SelectedIndex = Math.Max(0, colour);
        RoleRankBox.Maximum = Math.Clamp(myRank - 1, 1, 899);
        RoleRankBox.Value = role?.Position ?? DefaultRolePosition(myRank, s.Roles);
        RoleNameBox.IsEnabled = RoleColourBox.IsEnabled = RoleRankBox.IsEnabled = editable;

        var mine = me.Permissions.ToHashSet();
        var has = (role?.Permissions ?? []).ToHashSet();
        BuildPermissionList((key, label) =>
        {
            var box = new CheckBox { Content = label, IsChecked = has.Contains(key), IsEnabled = editable && (mine.Contains(key) || has.Contains(key)) };
            if (editable && !mine.Contains(key)) ToolTipService.SetToolTip(box, "You don't have this permission, so you can't grant it.");
            _permChecks[key] = box;
            return box;
        });

        SaveRoleButton.Content = isNew ? "Create role" : "Save";
        SaveRoleButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        DeleteRoleButton.Visibility = editable && !isNew && !role!.Builtin ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Same default as the theatre: just above the highest custom role you can manage, below your own rank.</summary>
    private static int DefaultRolePosition(int myRank, IEnumerable<Role> roles)
    {
        var below = roles.Where(r => !r.Builtin && r.Position < myRank).Select(r => r.Position).ToList();
        int pos = below.Count > 0 ? below.Max() + 1 : 1;
        return Math.Clamp(Math.Min(pos, myRank - 1), 1, 899);
    }

    private void BuildPermissionList(Func<string, string, FrameworkElement> make)
    {
        PermissionChecks.Children.Clear();
        _permChecks.Clear();
        _overrideBoxes.Clear();
        string? group = null;
        foreach (var (g, key, label) in Permissions.Catalogue)
        {
            if (g != group)
            {
                group = g;
                PermissionChecks.Children.Add(new TextBlock
                {
                    Text = g,
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                    Margin = new Thickness(0, PermissionChecks.Children.Count == 0 ? 0 : 10, 0, 2),
                });
            }
            PermissionChecks.Children.Add(make(key, label));
        }
    }

    private async void OnSaveRoleClick(object sender, RoutedEventArgs e)
    {
        if (_session is not { } s) return;
        RolesMessage.Visibility = Visibility.Collapsed;

        if (_overridesFor is { } viewerId)
        {
            var allow = _overrideBoxes.Where(kv => kv.Value.SelectedIndex == 1).Select(kv => kv.Key);
            var deny = _overrideBoxes.Where(kv => kv.Value.SelectedIndex == 2).Select(kv => kv.Key);
            await s.SetOverridesAsync(viewerId, allow, deny);
            return;
        }

        var perms = _permChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        string name = RoleNameBox.Text.Trim();
        string? colour = RoleColours.Presets[Math.Max(0, RoleColourBox.SelectedIndex)].Hex;
        int rank = double.IsNaN(RoleRankBox.Value) ? 1 : (int)Math.Round(RoleRankBox.Value);

        if (_creatingRole)
        {
            if (name.Length == 0) { ShowActionError("Give the role a name."); return; }
            _selectRoleNamed = name;
            await s.CreateRoleAsync(name, colour, perms, rank);
        }
        else if (_editRoleId == Permissions.ViewerRole)
        {
            await s.UpdateRoleAsync(_editRoleId, perms);
        }
        else if (_editRoleId is { } id)
        {
            if (name.Length == 0) { ShowActionError("Give the role a name."); return; }
            await s.UpdateRoleAsync(id, perms, (name, colour, rank));
        }
    }

    private void OnDeleteRoleClick(object sender, RoutedEventArgs e)
    {
        if (_session is not { } s || _editRoleId is not { } id) return;
        var menu = new MenuFlyout();
        var yes = new MenuFlyoutItem { Text = $"Delete “{RoleNameBox.Text}” and remove it from everyone" };
        yes.Click += async (_, _) => await s.DeleteRoleAsync(id);
        menu.Items.Add(yes);
        menu.ShowAt(DeleteRoleButton);
    }

    // ---- Per-viewer overrides (§8.4) ------------------------------------------------------------

    private void RenderOverridesEditor(ViewerInfo? target, ViewerInfo me)
    {
        var s = _session!;
        RoleFields.Visibility = DeleteRoleButton.Visibility = Visibility.Collapsed;
        if (target is null)
        {
            EditorTitle.Text = "Viewer left";
            EditorHint.Text = "They're no longer in the screen.";
            PermissionChecks.Children.Clear();
            SaveRoleButton.Visibility = Visibility.Collapsed;
            return;
        }

        EditorTitle.Text = $"Permissions for {target.Name}";
        EditorHint.Text = "Overrides beat roles: Deny wins over Allow, and Allow wins over roles. You can only allow permissions you have.";
        SaveRoleButton.Content = "Save overrides";
        SaveRoleButton.Visibility = Visibility.Visible;

        // What they'd get from roles alone, to label "Inherit".
        var fromRoles = target.CoHost is not null || target.ActingHost
            ? Permissions.Catalogue.Select(c => c.Key).ToHashSet()
            : s.Roles.Where(r => r.Id == Permissions.ViewerRole || (!r.Builtin && target.Roles.Contains(r.Id)))
                     .SelectMany(r => r.Permissions).ToHashSet();
        var mine = me.Permissions.ToHashSet();

        BuildPermissionList((key, label) =>
        {
            var box = new ComboBox { Width = 190 };
            box.Items.Add(fromRoles.Contains(key) ? "Inherit (allowed)" : "Inherit (not allowed)");
            box.Items.Add(new ComboBoxItem { Content = "Allow", IsEnabled = mine.Contains(key) || target.Allow.Contains(key) });
            box.Items.Add("Deny");
            box.SelectedIndex = target.Deny.Contains(key) ? 2 : target.Allow.Contains(key) ? 1 : 0;
            _overrideBoxes[key] = box;

            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            return row;
        });
    }
}
