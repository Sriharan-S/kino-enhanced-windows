using Kino.Core.Lobby;
using Microsoft.UI.Xaml;

namespace Kino;

/// <summary>Display model for one lobby row (x:Bind source).</summary>
public sealed class LobbyRow(LobbyEntry entry)
{
    public LobbyEntry Entry { get; } = entry;

    public string Name => Entry.Screen.Name;

    public string Subtitle =>
        (Entry.Screen.Host is { Length: > 0 } host ? $"Host {host} · " : "") +
        $"code {Entry.Screen.Code}" +
        (Entry.IsFull ? " · full" : "");

    public string Theatre => Entry.Theatre.Name;

    public string Viewers => $"{Entry.Screen.Viewers}/{Entry.Screen.MaxViewers}";

    public string Ping => Entry.PingMs is double p ? $"{p:0} ms" : "–";

    public Visibility LockVisibility => Entry.IsPrivate ? Visibility.Visible : Visibility.Collapsed;

    public double RowOpacity => Entry.IsFull ? 0.5 : 1.0;
}
