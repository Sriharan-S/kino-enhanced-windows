using System.Runtime.InteropServices;

namespace Kino.Player;

/// <summary>
/// Reads media durations with a headless libmpv instance (no video, no audio): any format libmpv plays, no extra codecs.
/// Durations are what the mismatch check compares (spec §13.5). Calls are serialised; use from a background thread.
/// </summary>
public sealed class MediaProbe : IDisposable
{
    private static readonly TimeSpan PerFileTimeout = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IntPtr _ctx;

    public MediaProbe()
    {
        _ctx = LibMpv.Create();
        if (_ctx == IntPtr.Zero) throw new InvalidOperationException("mpv_create failed");
        foreach (var (k, v) in new[]
        {
            ("vo", "null"), ("ao", "null"), ("idle", "yes"), ("pause", "yes"), ("load-scripts", "no"), ("ytdl", "no"),
            ("audio-display", "no"), ("sub-auto", "no"), ("audio-file-auto", "no"), ("cover-art-auto", "no"),
            ("input-default-bindings", "no"), ("terminal", "no"),
        })
        {
            LibMpv.SetOptionString(_ctx, k, v);
        }
        int rc = LibMpv.Initialize(_ctx);
        if (rc < 0) throw new InvalidOperationException($"mpv_initialize: {LibMpv.Error(rc)}");
    }

    /// <summary>Duration in seconds, or null if it can't be read (not media, unreadable, or no fixed length).</summary>
    public async Task<double?> DurationAsync(string path, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await Task.Run(() => Probe(path), ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private double? Probe(string path)
    {
        if (_ctx == IntPtr.Zero) return null;
        Command("loadfile", path, "replace");
        var deadline = DateTime.UtcNow + PerFileTimeout;
        double? result = null;
        while (DateTime.UtcNow < deadline)
        {
            var evPtr = LibMpv.WaitEvent(_ctx, 0.5);
            var ev = Marshal.PtrToStructure<LibMpv.Event>(evPtr);
            if (ev.EventId == LibMpv.EventId.FileLoaded)
            {
                if (LibMpv.GetProperty(_ctx, "duration", LibMpv.Format.Double, out double d) >= 0 && d > 0 && double.IsFinite(d))
                    result = Math.Round(d, 3);
                break;
            }
            if (ev.EventId == LibMpv.EventId.EndFile) break; // couldn't open it
            if (ev.EventId == LibMpv.EventId.Shutdown) return null;
        }
        Command("stop");
        Drain();
        return result;
    }

    /// <summary>Consumes the events from "stop" so the next file starts clean.</summary>
    private void Drain()
    {
        for (int i = 0; i < 50; i++)
        {
            var ev = Marshal.PtrToStructure<LibMpv.Event>(LibMpv.WaitEvent(_ctx, 0.05));
            if (ev.EventId == LibMpv.EventId.None) return;
        }
    }

    private void Command(params string[] args)
    {
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            LibMpv.Command(_ctx, ptrs);
        }
        finally
        {
            foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
        }
    }

    public void Dispose()
    {
        if (_ctx == IntPtr.Zero) return;
        _lock.Wait();
        try
        {
            LibMpv.TerminateDestroy(_ctx);
            _ctx = IntPtr.Zero;
        }
        finally
        {
            _lock.Release();
        }
    }
}
