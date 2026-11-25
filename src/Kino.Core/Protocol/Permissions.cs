namespace Kino.Core.Protocol;

/// <summary>Permission keys (wiki docs §8.1), with labels for the UI. The theatre is the enforcer.</summary>
public static class Permissions
{
    public const string PlaybackToggle = "playback.toggle";
    public const string PlaybackSeek = "playback.seek";
    public const string PlaybackSkip = "playback.skip";
    public const string PlaylistAdd = "playlist.add";
    public const string PlaylistAddExternal = "playlist.add_external";
    public const string PlaylistRemove = "playlist.remove";
    public const string PlaylistReorder = "playlist.reorder";
    public const string SubtitlesShare = "subtitles.share";
    public const string ReadyCheck = "screen.ready_check";
    public const string ScreenSettings = "screen.settings";
    public const string ScreenInvite = "screen.invite";
    public const string ChatSend = "chat.send";
    public const string ChatGif = "chat.gif";
    public const string ChatReact = "chat.react";
    public const string ViewersMute = "viewers.mute";
    public const string ViewersKick = "viewers.kick";
    public const string ViewersBan = "viewers.ban";
    public const string RolesAssign = "roles.assign";
    public const string RolesManage = "roles.manage";

    public const string CoHostRole = "cohost";
    public const string ViewerRole = "viewer";

    /// <summary>All permissions in catalogue order, grouped for display.</summary>
    public static readonly (string Group, string Key, string Label)[] Catalogue =
    [
        ("Playback", PlaybackToggle, "Play and pause"),
        ("Playback", PlaybackSeek, "Seek"),
        ("Playback", PlaybackSkip, "Next / previous item"),
        ("Playlist", PlaylistAdd, "Add local files"),
        ("Playlist", PlaylistAddExternal, "Add YouTube / links"),
        ("Playlist", PlaylistRemove, "Remove items"),
        ("Playlist", PlaylistReorder, "Reorder items"),
        ("Screen", SubtitlesShare, "Share subtitles"),
        ("Screen", ReadyCheck, "Start a ready check"),
        ("Screen", ScreenSettings, "Edit screen settings"),
        ("Screen", ScreenInvite, "See and regenerate the invite code"),
        ("Chat", ChatSend, "Send messages"),
        ("Chat", ChatGif, "Send GIFs"),
        ("Chat", ChatReact, "Send reactions"),
        ("Moderation", ViewersMute, "Chat-mute viewers"),
        ("Moderation", ViewersKick, "Kick viewers"),
        ("Moderation", ViewersBan, "Ban viewers"),
        ("Roles", RolesAssign, "Assign roles"),
        ("Roles", RolesManage, "Create and edit roles, set overrides"),
    ];

    public const int HostRank = 1000, CoHostRank = 900;

    /// <summary>Same ranking as the theatre: host 1000, co-host / acting host 900, else highest custom role, else 0.</summary>
    public static int RankOf(ViewerInfo v, IEnumerable<Role> roles) =>
        v.IsHost ? HostRank
        : v.CoHost is not null || v.ActingHost ? CoHostRank
        : roles.Where(r => !r.Builtin && v.Roles.Contains(r.Id)).Select(r => r.Position).DefaultIfEmpty(0).Max();
}
