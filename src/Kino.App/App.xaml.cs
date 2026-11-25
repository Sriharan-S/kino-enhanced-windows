using Microsoft.UI.Xaml;

namespace Kino;

public partial class App : Application
{
    private MainWindow? _window;
    private SingleInstance? _instance;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => WriteCrashLog(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved(); // background work must never take the app down
    }

    /// <summary>Appends to %LOCALAPPDATA%\Kino\crash.log, so failures can be diagnosed.</summary>
    internal static void WriteCrashLog(Exception? ex)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kino");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTimeOffset.Now:O}] {ex}\n\n");
        }
        catch (Exception) { /* nothing more we can do */ }
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
        try
        {
            _window = new MainWindow(options);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
        _window.Activate();

        _instance = new SingleInstance(options.Profile);
        _instance.Listen(forwarded => _window.DispatcherQueue.TryEnqueue(() => _window.OnForwardedLaunch(LaunchOptions.Parse(forwarded))));
        _window.Closed += (_, _) => _instance.Dispose();
    }
}
