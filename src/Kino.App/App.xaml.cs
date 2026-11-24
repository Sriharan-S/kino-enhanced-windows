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
        var options = LaunchOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
        _window = new MainWindow(options);
        _window.Activate();
    }
}
