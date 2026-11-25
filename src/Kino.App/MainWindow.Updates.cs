using Kino.Core;
using Microsoft.UI.Xaml;

namespace Kino;

/// <summary>Shows the version, and a notice in the lobby when a newer release is out (one request per launch).</summary>
public sealed partial class MainWindow
{
    private Updates.Release? _update;

    private async Task CheckForUpdatesAsync()
    {
        VersionText.Text = $"Kino {Updates.CurrentVersion}";
        if (!_settings.CheckForUpdates) return;
        await Task.Delay(TimeSpan.FromSeconds(5)); // don't compete with joining at startup
        _update = await Updates.NewerReleaseAsync(Http, Updates.CurrentVersion);
        if (_update is null) return;
        UpdateBar.Title = $"Kino {_update.Version} is available";
        UpdateBar.Message = $"You have {Updates.CurrentVersion}.";
        UpdateBar.IsOpen = true;
        VersionText.Text = $"Kino {Updates.CurrentVersion} · {_update.Version} available";
    }

    private async void OnDownloadUpdateClick(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(_update?.Page ?? Updates.LatestReleasePage);
}
