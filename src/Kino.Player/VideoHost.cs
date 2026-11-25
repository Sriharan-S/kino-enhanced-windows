using System.Runtime.InteropServices;

namespace Kino.Player;

/// <summary>
/// A native child window placed over a XAML element; libmpv renders video into it via the "wid" option.
/// Because it's a real HWND, XAML can't draw on top of it ("airspace"); overlays use windowed popups.
/// </summary>
public sealed partial class VideoHost : IDisposable
{
    public IntPtr Hwnd { get; private set; }

    public VideoHost(IntPtr parentHwnd)
    {
        Hwnd = CreateWindowExW(
            0, "STATIC", null,
            WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN | SS_BLACKRECT,
            0, 0, 1, 1, parentHwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (Hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
    }

    /// <summary>
    /// Hidden while XAML content (e.g. the lobby) must be visible in the same area: XAML can't draw over this window.
    /// Playback keeps running while hidden.
    /// </summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            _visible = value;
            ShowWindow(Hwnd, value ? SW_SHOWNA : SW_HIDE);
        }
    }

    private bool _visible = true;

    /// <summary>Position in physical pixels relative to the parent window's client area. Keeps the host on top.</summary>
    public void SetBounds(int x, int y, int width, int height) =>
        SetWindowPos(Hwnd, HWND_TOP, x, y, Math.Max(1, width), Math.Max(1, height),
                     SWP_NOACTIVATE | (_visible ? SWP_SHOWWINDOW : 0));

    public void Dispose()
    {
        if (Hwnd != IntPtr.Zero) DestroyWindow(Hwnd);
        Hwnd = IntPtr.Zero;
    }

    private const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000,
                       WS_CLIPSIBLINGS = 0x04000000, WS_CLIPCHILDREN = 0x02000000, SS_BLACKRECT = 0x4;
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const int SW_HIDE = 0, SW_SHOWNA = 8;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int cmd);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowExW(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hwnd);
}
