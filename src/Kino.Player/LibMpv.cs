using System.Runtime.InteropServices;

namespace Kino.Player;

/// <summary>Raw P/Invoke bindings for the subset of the libmpv client API Kino uses.</summary>
public static partial class LibMpv
{
    private const string Dll = "libmpv-2.dll";

    public enum Format
    {
        None = 0,
        String = 1,
        OsdString = 2,
        Flag = 3,
        Int64 = 4,
        Double = 5,
        Node = 6,
    }

    public enum EventId
    {
        None = 0,
        Shutdown = 1,
        LogMessage = 2,
        GetPropertyReply = 3,
        SetPropertyReply = 4,
        CommandReply = 5,
        StartFile = 6,
        EndFile = 7,
        FileLoaded = 8,
        Idle = 11,
        Tick = 14,
        ClientMessage = 16,
        VideoReconfig = 17,
        AudioReconfig = 18,
        Seek = 20,
        PlaybackRestart = 21,
        PropertyChange = 22,
        QueueOverflow = 24,
        Hook = 25,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Event
    {
        public EventId EventId;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventProperty
    {
        public IntPtr Name;
        public Format Format;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventEndFile
    {
        public int Reason;
        public int Error;
        public long PlaylistEntryId;
        public long PlaylistInsertId;
        public int PlaylistInsertNumEntries;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventLogMessage
    {
        public IntPtr Prefix;
        public IntPtr Level;
        public IntPtr Text;
        public int LogLevel;
    }

    [LibraryImport(Dll, EntryPoint = "mpv_create")]
    public static partial IntPtr Create();

    [LibraryImport(Dll, EntryPoint = "mpv_initialize")]
    public static partial int Initialize(IntPtr ctx);

    [LibraryImport(Dll, EntryPoint = "mpv_terminate_destroy")]
    public static partial void TerminateDestroy(IntPtr ctx);

    [LibraryImport(Dll, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetOptionString(IntPtr ctx, string name, string data);

    [LibraryImport(Dll, EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetPropertyString(IntPtr ctx, string name, string data);

    [LibraryImport(Dll, EntryPoint = "mpv_set_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetProperty(IntPtr ctx, string name, Format format, ref double data);

    [LibraryImport(Dll, EntryPoint = "mpv_set_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetProperty(IntPtr ctx, string name, Format format, ref int data);

    [LibraryImport(Dll, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int GetProperty(IntPtr ctx, string name, Format format, out double data);

    /// <summary>Returns a UTF-8 string that must be released with <see cref="Free"/>, or IntPtr.Zero.</summary>
    [LibraryImport(Dll, EntryPoint = "mpv_get_property_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GetPropertyString(IntPtr ctx, string name);

    [LibraryImport(Dll, EntryPoint = "mpv_free")]
    public static partial void Free(IntPtr data);

    [LibraryImport(Dll, EntryPoint = "mpv_request_log_messages", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int RequestLogMessages(IntPtr ctx, string minLevel);

    [LibraryImport(Dll, EntryPoint = "mpv_command")]
    public static partial int Command(IntPtr ctx, IntPtr[] args);

    [LibraryImport(Dll, EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int ObserveProperty(IntPtr ctx, ulong replyUserdata, string name, Format format);

    [LibraryImport(Dll, EntryPoint = "mpv_wait_event")]
    public static partial IntPtr WaitEvent(IntPtr ctx, double timeout);

    [LibraryImport(Dll, EntryPoint = "mpv_wakeup")]
    public static partial void Wakeup(IntPtr ctx);

    [LibraryImport(Dll, EntryPoint = "mpv_error_string")]
    public static partial IntPtr ErrorString(int error);

    public static string Error(int code) => Marshal.PtrToStringUTF8(ErrorString(code)) ?? code.ToString();
}
