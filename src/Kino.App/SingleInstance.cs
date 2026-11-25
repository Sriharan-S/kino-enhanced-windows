using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace Kino;

/// <summary>
/// One Kino window per profile. A second launch (e.g. clicking an invite link while Kino is open) hands its
/// arguments to the running window and exits. Otherwise both would use the same device token, and joining the same
/// screen would replace the first one's connection.
/// </summary>
internal sealed partial class SingleInstance : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();

    public SingleInstance(string profile) => _pipeName = PipeName(profile);

    private static string PipeName(string profile) => $"Kino-{Environment.UserName}-{profile}";

    /// <summary>Sends the arguments to a running instance of this profile. True if one took them.</summary>
    public static bool TryForward(string profile, string[] args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName(profile), PipeDirection.Out);
            pipe.Connect(300);
            AllowSetForegroundWindow(ASFW_ANY); // let the running window come to the front
            using var writer = new StreamWriter(pipe);
            writer.Write(JsonSerializer.Serialize(args));
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false; // nobody listening: we are the first instance
        }
    }

    /// <summary>Receives arguments from later launches. <paramref name="onArgs"/> runs on a background thread.</summary>
    public void Listen(Action<string[]> onArgs) => _ = Task.Run(async () =>
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(_cts.Token);
                using var reader = new StreamReader(pipe);
                string json = await reader.ReadToEndAsync(_cts.Token);
                if (JsonSerializer.Deserialize<string[]>(json) is { } args) onArgs(args);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) when (e is IOException or JsonException) { /* bad message; keep listening */ }
        }
    });

    public void Dispose() => _cts.Cancel();

    private const int ASFW_ANY = -1;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}

/// <summary>Registers the <c>kino://</c> link scheme for the current user (no admin rights needed).</summary>
internal static class ProtocolRegistration
{
    public static void EnsureRegistered()
    {
        try
        {
            if (Environment.ProcessPath is not { } exe) return;
            string command = $"\"{exe}\" \"%1\"";
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Kino.Core.InviteLink.Scheme);
            key.SetValue(null, "URL:Kino invite link");
            key.SetValue("URL Protocol", "");
            using var open = key.CreateSubKey(@"shell\open\command");
            if (open.GetValue(null) as string != command) open.SetValue(null, command);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Links just won't open the app; everything else works.
        }
    }
}
