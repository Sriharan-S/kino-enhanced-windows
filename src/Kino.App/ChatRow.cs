using Kino.Core.Chat;
using Kino.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI.Text;

namespace Kino;

/// <summary>Display model for one chat line (x:Bind source). Animated GIFs play via BitmapImage.</summary>
public sealed class ChatRow
{
    public ChatRow(ChatMessage message, Brush nameBrush, bool continuation)
    {
        Message = message;
        bool system = message.IsSystem;
        Name = message.Name ?? "";
        Time = "  " + DateTimeOffset.FromUnixTimeMilliseconds((long)message.At).ToLocalTime().ToString("HH:mm");
        NameBrush = nameBrush;
        HeaderVisibility = system || continuation ? Visibility.Collapsed : Visibility.Visible;
        Text = message.Text ?? "";
        TextVisibility = Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        TextOpacity = system ? 0.65 : 1;
        TextStyle = system ? FontStyle.Italic : FontStyle.Normal;
        TextSize = system ? 12 : 14;
        if (message.Gif is { } gif && Uri.TryCreate(gif.Url, UriKind.Absolute, out var uri))
        {
            GifSource = new BitmapImage(uri);
            GifVisibility = Visibility.Visible;
        }
    }

    public ChatMessage Message { get; }
    public string Name { get; }
    public string Time { get; }
    public Brush NameBrush { get; }
    public Visibility HeaderVisibility { get; }
    public string Text { get; }
    public Visibility TextVisibility { get; }
    public double TextOpacity { get; }
    public FontStyle TextStyle { get; }
    public double TextSize { get; }
    public ImageSource? GifSource { get; }
    public Visibility GifVisibility { get; } = Visibility.Collapsed;
}

/// <summary>One GIF search result in the picker (x:Bind source).</summary>
public sealed class GifTile(GifResult result)
{
    public Gif Gif { get; } = result.Gif;
    public ImageSource Preview { get; } = new BitmapImage(new Uri(result.PreviewUrl));
}
