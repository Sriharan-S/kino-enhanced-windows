// Usage: Kino.TestMedia <output.mkv> [seconds=300]
// Encodes a test pattern with a beeping 440 Hz tone, so sync can be checked by eye and ear.
using System.Runtime.InteropServices;
using Kino.Player;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: Kino.TestMedia <output.mkv> [seconds=300]");
    return 2;
}

string output = Path.GetFullPath(args[0]);
int seconds = args.Length > 1 ? int.Parse(args[1]) : 300;

IntPtr ctx = LibMpv.Create();
void Opt(string k, string v)
{
    int rc = LibMpv.SetOptionString(ctx, k, v);
    if (rc < 0) throw new InvalidOperationException($"{k}={v}: {LibMpv.Error(rc)}");
}

Opt("o", output);
Opt("of", "matroska");
Opt("ovc", "mpeg4");
Opt("ovcopts", "qscale=3,g=30");   // keyframe every second: fast, accurate seeks
Opt("oac", "aac");
Opt("audio-files", $"av://lavfi:sine=frequency=440:beep_factor=4:duration={seconds}");
Opt("vo", "null");
Opt("terminal", "no");

if (LibMpv.Initialize(ctx) < 0) throw new InvalidOperationException("mpv_initialize failed");

string src = $"av://lavfi:testsrc2=size=1280x720:rate=30:duration={seconds}";
var argv = new[] { Marshal.StringToCoTaskMemUTF8("loadfile"), Marshal.StringToCoTaskMemUTF8(src), IntPtr.Zero };
LibMpv.Command(ctx, argv);

Console.WriteLine($"Encoding {seconds}s test video to {output} ...");
while (true)
{
    var ev = Marshal.PtrToStructure<LibMpv.Event>(LibMpv.WaitEvent(ctx, -1));
    if (ev.EventId == LibMpv.EventId.EndFile)
    {
        var ef = Marshal.PtrToStructure<LibMpv.EventEndFile>(ev.Data);
        if (ef.Error < 0) Console.Error.WriteLine($"error: {LibMpv.Error(ef.Error)}");
        break;
    }
    if (ev.EventId == LibMpv.EventId.Shutdown) break;
}

LibMpv.TerminateDestroy(ctx); // flushes and finalises the output file
Console.WriteLine($"Done: {new FileInfo(output).Length / 1_000_000.0:0.0} MB");
return 0;
