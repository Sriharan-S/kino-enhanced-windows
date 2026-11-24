using Microsoft.UI.Xaml;

namespace Kino;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Optional first command-line argument: a file path or URL to open on start (useful for testing).
        var cli = Environment.GetCommandLineArgs();
        string? startMedia = cli.Length > 1 ? cli[1] : null;

        _window = new MainWindow(startMedia);
        _window.Activate();
    }
}
