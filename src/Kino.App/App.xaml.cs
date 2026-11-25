using Microsoft.UI.Xaml;

namespace Kino;

public partial class App : Application
{
    private MainWindow? _window;
    private SingleInstance? _instance;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] argv = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var options = LaunchOptions.Parse(argv);

        // Already running with this profile (e.g. an invite link was clicked): hand over and quit.
        if (SingleInstance.TryForward(options.Profile, argv))
        {
            Exit();
            return;
        }

        ProtocolRegistration.EnsureRegistered();
        _window = new MainWindow(options);
        _window.Activate();

        _instance = new SingleInstance(options.Profile);
        _instance.Listen(forwarded => _window.DispatcherQueue.TryEnqueue(() => _window.OnForwardedLaunch(LaunchOptions.Parse(forwarded))));
        _window.Closed += (_, _) => _instance.Dispose();
    }
}
