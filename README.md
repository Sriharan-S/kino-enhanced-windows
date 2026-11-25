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

## Roles, co-hosts and moderation

Once in a screen, **Viewers** opens a panel beside the video. It lists everyone, with Host / Acting host / Co-host #n, their roles,
device and ping. Click a viewer for what you're allowed to do:

- **Host only:** make or remove co-hosts and set their priority, make someone else the host, close the screen.
- **With permission, on viewers ranked below you:** assign roles, set per-viewer overrides (**Permissions…**), mute chat, kick, ban.

**Roles…** (for viewers with `roles.manage`) lets you create, edit and delete roles below your own rank. You can't grant permissions
you don't have. When the host drops out, the top co-host who is present takes over as **acting host**; the host takes back control on
return. The theatre enforces all of this. The app just hides or disables what would be refused.

## Playlist

The **Playlist** tab in the side panel holds the screen's items: local files and links.

- **Add files… / Add folder…** (with `playlist.add`): Kino reads each file's duration and sends only its **name and duration**.
  Paths and contents never leave your PC. Folders are added A–Z in natural order (`EP 2` before `EP 10`).
- **Add link…** (with `playlist.add_external`): a direct video URL, or a **YouTube** link. Everyone streams it straight from
  the source. YouTube plays in the official YouTube player (in a WebView2 over the video area). Because YouTube only supports
  coarse speed steps, there's no gentle speed correction: up to 0.5 s apart is fine, and beyond 1.5 s Kino seeks. Ads count as
  buffering. A YouTube item moves on only when everyone has finished it, unless the host skips. (Some owners don't allow
  playback in other apps; Kino says so.)
- **Your copies:** Kino matches each local item to a file you've opened or offered, by **duration first, then name**.
  **Match my files…** offers a whole folder, and an item's menu has **Choose my file…** to pick one by hand.
  Each item shows ✓ (match), ✓ *different name*, ⚠ *length differs* or *Pick your file*. A mismatch only warns; it never blocks.
- When an item ends, the theatre moves everyone to the next one (or stops, if *When an item ends* is set to *Stop* in
  Invite & settings). If your copy is shorter, you wait at its end. ⏮ / ⏭ skip with `playback.skip`.

## Player

- **Picture & sound** (transport bar): zoom 50–400% with pan, aspect override, fit (fit, fit width, fit height, stretch,
  fill), audio track, and a **personal sync offset** (±2000 ms, e.g. +200 for Bluetooth headphones). These settings stay
  on your PC: global defaults, or **Remember for this file only**.
- **Volume** goes up to 200% after a one-time warning. 🔊 mutes.
- **Fullscreen:** ⛶, F or F11; Esc exits.
- **Shortcuts:** Space/K play-pause · ←/→ 5 s · J/L 10 s · ↑/↓ volume · M mute · N/P next/previous · Home restart.
  In a screen, seeking and play/pause still need the matching permission.
- **Resume:** each file's position is saved locally every few seconds. Opening it again outside a screen resumes it.
- If a file can't be played, Kino says why, and names the codec if a decoder is missing.
- Crashes are written to `%LOCALAPPDATA%\Kino\crash.log`.

## Subtitles

**Subtitles** (transport bar) lets you choose, for each video: **off**, a track **in the file**, any file someone
**shared** with the screen, or **your own** file (`.srt`, `.vtt`, `.ass`, `.ssa`). A same-named subtitle file next to the
video is picked up automatically.

- **Also share with viewers** (with `subtitles.share`) uploads the file, compressed (1 MB at most). Everyone can then
  pick it, and chat announces it. It belongs to that playlist item only.
- **Timing offset** (per file) and the **look** (font, size, colour, outline, background box, position) stay on your PC.

## Chat

The **Chat** tab has text chat (500 characters), GIFs and reactions. Reactions float briefly over the video. System lines
show joins, leaves, role and host changes, and what's now playing. People who join later see the last 100 messages.

- **GIFs** are searched on GIPHY straight from your PC; only the chosen GIF's link is sent to the screen. GIPHY needs a
  free API key: in the GIF picker, paste a key from [developers.giphy.com](https://developers.giphy.com) (Create an app → API).
  It is saved in `%LOCALAPPDATA%\Kino\settings.json`, or you can set `KINO_GIPHY_KEY`. Without a key, paste a GIF link into
  the message box.
- Sending needs `chat.send`, `chat.gif` or `chat.react` (everyone has them by default). Muted viewers can't send anything.

## Sync status, buffering and ready checks

- The viewers panel shows everyone's **sync status** (🟢 in sync, 🟡 catching up, 🔵 buffering, ⚠ file differs,
  ⏳ waiting, ⛔ no file) and **latency** (🟢 < 100 ms, 🟡 100–250 ms, 🔴 > 250 ms). Each app reports its own status, only
  when it changes.
- **When someone is buffering** (Invite & settings): *Keep playing* (default), or *Wait for everyone*. With *Wait*, anyone
  buffering for more than 2 s pauses the screen ("Waiting for Alex…"). It resumes for everyone when they're ready. Anyone who
  can play/pause can **Resume anyway**.
- **Ready check** (viewers panel, `screen.ready_check`): everyone gets a ✅ Ready / ❌ Not yet prompt over the video, with a live
  tally. It closes after 30 s, and the result goes to chat.

## Invites, settings and reconnecting

- **Invite & settings…** (in the viewers panel) shows the screen code and its invite link, for example
  `https://kino-directory.srie.workers.dev/join#aurora/7K3QXM`. The web page opens the app through `kino://join/aurora/7K3QXM`.
  With `screen.invite` you can make a **new code**; the old code and links stop working. With `screen.settings` you can change
  the name, visibility, password, hide-from-lobby, max viewers and whether devices are shown.
- The app registers the `kino://` link scheme for the current user on startup (`HKCU\Software\Classes\kino`, no admin rights
  needed). Kino runs **one window per profile**. Opening a link while it's running hands the link to that window.
- If the connection drops, playback carries on from the last sync point and the app reconnects by itself for up to 60 s.
  Others see you as *reconnecting…* and your seat is kept. **Leave screen** frees the seat at once.

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

## Portable release build

```powershell
.\tools\Publish-Portable.ps1     # → dist\Kino-<version>-win-x64.zip (self-contained, about 135 MB)
```

Unzip anywhere and run `Kino.exe`. No installer or admin rights are needed: .NET, the Windows App SDK and libmpv are
included. The version comes from `<Version>` in `src/Kino.App/Kino.App.csproj`.

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
