# Kino Enhanced — Windows

Native Windows client for [Kino Enhanced](https://github.com/Sriharan-S/kino-enhanced), a cross-platform synced media player.

- **Stack:** C# / .NET 10 + WinUI 3 (Windows App SDK, unpackaged, self-contained), playback via **libmpv**
- **Spec:** [Wiki → docs](https://github.com/Sriharan-S/kino-enhanced/wiki/docs) · [Protocol](https://github.com/Sriharan-S/kino-enhanced/tree/main/protocol)
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
- For a local theatre: Node.js 20+ and a checkout of [`kino-enhanced`](https://github.com/Sriharan-S/kino-enhanced) (`theatre/`)

Visual Studio is optional. Everything builds with the `dotnet` CLI.

## Build, test, run

```powershell
.\tools\Fetch-LibMpv.ps1                      # pinned libmpv, SHA-1 verified, into libs/mpv (git-ignored)
dotnet build Kino.slnx -p:Platform=x64
dotnet test tests\Kino.Core.Tests             # sync engine, clock sync, protocol contract tests

$kino = ".\src\Kino.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\Kino.exe"
& $kino                                        # then use Open…, Create screen or Join
```

## Try sync with two windows on one PC

```powershell
# 1. Local theatre (in kino-enhanced/theatre)
npm install; npm run dev

# 2. A seekable test video (no ffmpeg needed; uses libmpv's encoder)
dotnet run --project tools\Kino.TestMedia -p:Platform=x64 -- testdata\sync-test.mkv 300

# 3. Host and viewer (separate profiles = separate device identities)
& $kino testdata\sync-test.mkv --profile host --create "Sync test" --autoplay --codefile code.txt --synclog host.csv
& $kino testdata\sync-test.mkv --profile viewer --join (Get-Content code.txt) --synclog viewer.csv --perturb 1.5

# 4. Compare the two logs (same PC = same clock)
node tools\analyze-sync.mjs host.csv viewer.csv
```

`--perturb 1.5` knocks the viewer 1.5 s off after it has been in sync for 20 s, so you can watch the smooth correction.
The other options are listed in `src/Kino.App/LaunchOptions.cs`.

## Layout

```
Kino.slnx
src/Kino.Core/            Platform-neutral core (net10.0): protocol, theatre API, WebSocket session,
                          clock sync, rate controller, sync engine, device identity
src/Kino.Player/          libmpv bindings + MpvPlayer (implements IPlayback) + native VideoHost window
src/Kino.App/             WinUI 3 app
tests/Kino.Core.Tests/    xUnit tests, including a simulated player for the sync engine
tools/Fetch-LibMpv.ps1    Downloads the pinned libmpv build
tools/Kino.TestMedia/     Generates seekable test videos
tools/analyze-sync.mjs    Compares host/viewer sync logs
```

## How video embedding works

1. A native child window (`VideoHost`) is placed exactly over a XAML placeholder and kept aligned on resize and DPI changes.
2. libmpv renders into it through the `wid` option (`vo=gpu-next`, hardware decoding `auto-safe`).
3. XAML can't draw on top of a native child window (the "airspace" limitation). Overlays therefore use **windowed popups** (`Popup.ShouldConstrainToRootBounds = false`). These are separate windows owned by the main window, so they always stay above the video.

## Sync results (local, two windows)

| Scenario | Result |
|---|---|
| Late join during playback | Joined 0.40 s off; eased into sync in ~18 s |
| Viewer knocked 1.5 s ahead | Slowed smoothly to 0.95×, back in sync in ~45 s, no jump or overshoot |
| Steady state | Viewer within ~0.04–0.07 s of the host |
