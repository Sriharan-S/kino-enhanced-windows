namespace Kino.Core;

/// <summary>
/// Where Kino keeps its data: settings, preferences, device identity, caches, logs.
/// Installed: %LOCALAPPDATA%\Kino. Portable edition (a file named "portable" next to Kino.exe): a "Data" folder next to
/// Kino.exe, so the whole app, identity included, can live on a USB stick.
/// </summary>
public static class KinoPaths
{
    public const string PortableMarker = "portable";

    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarker));

    public static string DataRoot { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "Data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kino");

    /// <summary>Path under the data root, e.g. <c>For("settings.json")</c> or <c>For(profile, "prefs.json")</c>.</summary>
    public static string For(params string[] parts) => Path.Combine([DataRoot, .. parts]);
}
