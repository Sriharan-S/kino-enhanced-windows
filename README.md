# Kino Enhanced — Windows

Native Windows client for [Kino Enhanced](https://github.com/Sriharan-S/kino-enhanced), a cross-platform synced media player.

- **Stack:** C# / .NET 10 + WinUI 3 (Windows App SDK, unpackaged, self-contained), playback via **libmpv**
- **Spec:** [Wiki → docs](https://github.com/Sriharan-S/kino-enhanced/wiki/docs)
- **TODO / progress:** tracked in the [main README checklist](https://github.com/Sriharan-S/kino-enhanced#todo)

## Prerequisites

- Windows 10 2004 (build 19041) or later, x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download). No admin rights needed if you use the per-user install:
  ```powershell
  Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
  .\dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
  ```
  If a system-wide .NET *runtime* without an SDK is installed in `C:\Program Files\dotnet`, it is found first on `PATH`.
  In that case, call the per-user SDK explicitly: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" ...`

Visual Studio is optional. Everything builds with the `dotnet` CLI.

## Build & run

```powershell
# 1. Fetch libmpv (pinned build, SHA-1 verified, stored in libs/mpv and ignored by git)
.\tools\Fetch-LibMpv.ps1

# 2. Build
dotnet build Kino.slnx -c Debug -p:Platform=x64

# 3. Run (optionally pass a file path or URL to open on start)
.\src\Kino.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\Kino.exe "av://lavfi:testsrc2=size=1280x720:rate=30"
```

## Layout

```
Kino.slnx
src/Kino.App/
  App.xaml(.cs)          Application entry
  MainWindow.xaml(.cs)   Player window (prototype UI)
  Player/LibMpv.cs       P/Invoke bindings for the libmpv client API
  Player/MpvPlayer.cs    Managed libmpv wrapper (commands, observed properties, event thread)
  Player/VideoHost.cs    Native child window that libmpv renders into
tools/Fetch-LibMpv.ps1   Downloads the pinned libmpv build
libs/mpv/                libmpv binaries (not committed)
```

## How video embedding works

1. A native child window (`VideoHost`) is placed exactly over a XAML placeholder and kept aligned on resize and DPI changes.
2. libmpv renders into it through the `wid` option (`vo=gpu-next`, hardware decoding `auto-safe`).
3. XAML can't draw on top of a native child window (the "airspace" limitation). Overlays such as controls, chat and reactions therefore use **windowed popups** (`Popup.ShouldConstrainToRootBounds = false`). These are separate windows owned by the main window, so they always stay above the video.

## Status

Prototype: libmpv playback embedded in WinUI 3, with play/pause, seek, fine-grained speed (pitch-corrected), volume up to 200%, and an overlay above the video.
