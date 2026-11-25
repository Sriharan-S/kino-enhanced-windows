using System.Text.Json;

namespace Kino.Core.Media;

/// <summary>
/// Picture/sound preferences and resume positions, stored only on this device (spec §13.7, §16):
/// <c>%LOCALAPPDATA%\Kino\&lt;profile&gt;\prefs.json</c>. Nothing here is ever sent to a theatre.
/// </summary>
public sealed class LocalPrefs
{
    public const int MaxResumeEntries = 500;

    /// <summary>Not worth resuming near the start; treated as finished near the end.</summary>
    public const double ResumeMinSeconds = 10, ResumeEndMarginSeconds = 15;

    public ViewSettings Defaults { get; set; } = new();
    public Dictionary<string, ViewSettings> PerFile { get; set; } = [];
    public double Volume { get; set; } = 100;

    /// <summary>The one-time volume-boost disclaimer was accepted (spec §16).</summary>
    public bool BoostAccepted { get; set; }

    public Dictionary<string, ResumeEntry> Resume { get; set; } = [];

    public sealed record ResumeEntry(double Position, double? Duration, DateTimeOffset UpdatedAt);

    /// <summary>Paths compare case-insensitively on Windows; links as-is.</summary>
    public static string KeyFor(string pathOrUrl) => pathOrUrl.Contains("://") ? pathOrUrl : Path.GetFullPath(pathOrUrl).ToLowerInvariant();

    public ViewSettings ViewFor(string? key) =>
        (key is not null && PerFile.TryGetValue(key, out var v) ? v : Defaults).Clamped();

    public bool HasOverride(string? key) => key is not null && PerFile.ContainsKey(key);

    /// <summary>Where to resume this file, or null.</summary>
    public double? ResumeAt(string key) =>
        Resume.TryGetValue(key, out var e) && e.Position >= ResumeMinSeconds ? e.Position : null;

    /// <summary>Remembers the position; forgets it near the start or end; keeps the newest entries only.</summary>
    public void SetResume(string key, double position, double? duration, DateTimeOffset now)
    {
        if (!double.IsFinite(position)) return;
        bool finished = duration is double d && position >= d - ResumeEndMarginSeconds;
        if (position < ResumeMinSeconds || finished)
        {
            Resume.Remove(key);
            return;
        }
        Resume[key] = new ResumeEntry(Math.Round(position, 1), duration, now);
        if (Resume.Count > MaxResumeEntries)
            foreach (var old in Resume.OrderBy(kv => kv.Value.UpdatedAt).Take(Resume.Count - MaxResumeEntries).Select(kv => kv.Key).ToList())
                Resume.Remove(old);
    }

    // ---- Storage --------------------------------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static string PathFor(string profile) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kino", profile, "prefs.json");

    public static LocalPrefs Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<LocalPrefs>(File.ReadAllText(file), Json) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(string file)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}
