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
- Optional, for a local theatre: Node.js 20+ and a checkout of [`kino-enhanced`](https://github.com/Sriharan-S/kino-enhanced) (`theatre/`)

## Theatres

At startup the app loads the theatre list from the
[directory](https://kino-directory.srie.workers.dev/servers.json). If that fails, it uses the last good copy
(`%LOCALAPPDATA%\Kino\servers.json`), and after that the list built into the app (`TheatreDirectory.Bundled`).

The app opens on the **lobby**:

- **Screens:** every listed screen from every theatre in one list, with its theatre badge, viewers and ping. You can search, filter by public/private or theatre, and sort by viewers, ping or name. The list refreshes every 15 s and when you click Refresh. A theatre that is down shows as *unavailable* and doesn't hold up the others.
- **Join:** click a screen, or enter a code. Codes are looked up on every theatre in parallel, so you never have to say which theatre a screen is on. Private screens ask for their password in place.
- **Create a screen:** a name is required. The theatre defaults to **Fastest** (the reachable, compatible theatre with the lowest ping). Private screens need a password and can be hidden from the lobby. Max viewers is 2–25.
- **Lobby / Back to video** switches between the lobby and the player. Playback keeps running while the lobby is open.
- `--theatre http://127.0.0.1:8787` uses only a local `wrangler dev` theatre instead of the directory.

Opt-in live test against a real theatre: `$env:KINO_LIVE_THEATRE="https://kino-aurora.srie.workers.dev"; dotnet test tests\Kino.Core.Tests`.

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
# 1. A seekable test video (no ffmpeg needed; uses libmpv's encoder)
dotnet run --project tools\Kino.TestMedia -p:Platform=x64 -- testdata\sync-test.mkv 300

# 2. Host and viewer (separate profiles = separate device identities); uses the live theatres
& $kino testdata\sync-test.mkv --profile host --create "Sync test" --autoplay --codefile code.txt --synclog host.csv
& $kino testdata\sync-test.mkv --profile viewer --join (Get-Content code.txt) --synclog viewer.csv --perturb 1.5

# 3. Compare the two logs (same PC = same clock)
node tools\analyze-sync.mjs host.csv viewer.csv
```

To test against a local theatre, run `npm install; npm run dev` in `kino-enhanced/theatre` and add
`--theatre http://127.0.0.1:8787` to both commands.

Between two PCs, just open the same file on both, click **Create screen** on one, and enter its code on the other.

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

## Sync results (two windows on one PC)

| Scenario | Local theatre | Aurora (Cloudflare, ~65 ms RTT) |
|---|---|---|
| Late join during playback | Joined 0.40 s off; eased into sync in ~18 s | Joined 0.29 s off; eased in smoothly |
| Viewer knocked 1.5 s ahead | Slowed smoothly to 0.95×, back in sync in ~45 s, no jump or overshoot | Same: 0.95× max, in sync in ~40 s |
| Host seeks (+208 s, −128 s, +67 s, −59 s 1.5 s apart) | n/a | Both start together on schedule; viewer in sync ~1.7 s after each seek; no speed bending |
| Steady state | Viewer within ~0.04–0.07 s of the host | Viewer within ±0.03 s of the host |

Repeat the seek test with `--seeks 20=226.8,28=99.1,33.5=165.6,35=106.2` on the host
(seconds after autoplay = target position).
