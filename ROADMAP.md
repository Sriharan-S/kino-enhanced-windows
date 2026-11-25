# Kino for Windows roadmap

The Windows checklist for [Kino Enhanced](https://github.com/Sriharan-S/kino-enhanced). It follows the
**[master roadmap](https://github.com/Sriharan-S/kino-enhanced/blob/main/ROADMAP.md)**, which holds every common task's description and acceptance criteria; this file adds where
each one lives in this repo, what tests cover it, and the Windows-only work.

- Spec: [wiki docs](https://github.com/Sriharan-S/kino-enhanced/wiki/docs) (cited as §n).
- Released: [v0.1.0](https://github.com/Sriharan-S/kino-enhanced-windows/releases/latest). Code signing is waiting
  for SignPath approval (WIN-20.3).

## How to use this roadmap

- **IDs.** `WIN-x.y` is the Windows side of common task `C-x.y` (same numbers). `WIN-x.Pn` is **platform-only**:
  Windows work with no common task (the Common column says "Platform-only", or names the common task it serves).
  IDs are never renumbered or reused.
- **Status.** ✅ done · 🟨 partial · ⬜ not started · ⏸ on hold. A row is ✅ only when it meets the master roadmap's
  [definition of done](https://github.com/Sriharan-S/kino-enhanced/blob/main/ROADMAP.md#definition-of-done): implemented, covered by automated tests (or "UI: manual" for purely
  presentational criteria until WIN-21.5 adds UI tests), and verified by hand on a real Windows PC.
- **Updating.** Change a status only after looking at the code and tests. Change the same row's Windows column in the
  master roadmap in the same piece of work; for common tasks the master is the authority. The tables below were
  generated from the master on the audit date, so they match it.
- **Where.** Paths are shortened: `App/` = `src/Kino.App/`, `Core/` = `src/Kino.Core/`, `Player/` =
  `src/Kino.Player/`, `Tests/` = `tests/Kino.Core.Tests/`; `file.cs:12` is a line at the audit commit.
- **Tests.** "unit" and "live" are `tests/Kino.Core.Tests` (live tests need `KINO_LIVE_THEATRE`); "Th smoke" is the
  theatre's smoke test in the main repo; "UI: manual" means checked by hand only.

Rows: ✅ 180 · 🟨 36 · ⬜ 66 · ⏸ 3 (every row below, platform-only items and the design phase included).

Audited on 2026-11-25 at commit `0e86b29` (v0.1.0 plus web-link parsing).

## Fix first

Problems the audit found in the released app, in the order to fix them:

1. **Messages vanish in a screen (WIN-19.4).** Action errors, rate limits, codec errors and YouTube errors are written to
   `OverlayText`, which `OnSyncStatus` rewrites every 250 ms (`App/MainWindow.xaml.cs:300`), so they disappear almost
   at once; the overlay is also hidden when toggled off.
2. **Settings errors are invisible (WIN-5.11.1, WIN-5.11.2, WIN-19.6).** "The screen needs a name" and "Set a password"
   go to the overlay, which is hidden behind the settings page; `SettingsMessage` is never filled.
3. **Giving up on a reconnect, or being replaced, leaves you on the video page (WIN-19.2, WIN-3.7)** with "Disconnected
   (…)" instead of returning to the lobby.
4. **The display name isn't saved (WIN-3.1)**: it resets to the Windows user name at every launch.
5. **Keyboard shortcuts (WIN-12.7):** a focused slider takes the arrow keys, nothing works while the YouTube view has
   focus, and ←/→ during YouTube items read libmpv's position.
6. **Unavailable theatres can be picked in Create (WIN-4.4).**
7. **No "Stop sharing" for subtitles (WIN-17.7)**, although `ScreenSession.UnshareSubtitleAsync` exists.
8. **Live tests never run in CI and pass silently when skipped (WIN-21.3, WIN-2.3).**
9. **CHANGELOG "Unreleased" is inaccurate:** it says web-app invite links work in "Join a screen", but only launch
   arguments are parsed (`App/LaunchOptions.cs:79`); the code box (`MaxLength 12`) doesn't accept links (WIN-6.2).


## WIN-0. Foundation and governance

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-0.1 | Repositories set up | C-0.1 | ✅ | repo, `.gitignore` |  |
| WIN-0.2 | READMEs | C-0.2 | ✅ | `README.md` |  |
| WIN-0.6 | Licence: GPL-3.0-or-later in every repo | C-0.6 | ✅ | `LICENSE` |  |
| WIN-0.7 | Lint, format and test tooling | C-0.7 | ✅ | `.editorconfig`, `Directory.Build.props` | `dotnet format whitespace` only; no analyzers enforced |
| WIN-0.8 | CI on every push and pull request | C-0.8 | ✅ | `.github/workflows/ci.yml` | windows-2025 runner; builds and tests x64 only (WIN-20.P7) |
| WIN-0.9 | Roadmaps: this file and one per platform repo | C-0.9 | ✅ | this file |  |
| WIN-0.11 | Changelog per released client | C-0.11 | ✅ | `CHANGELOG.md`, `.github/workflows/release.yml` | `CHANGELOG.md` feeds the release notes |

## WIN-2. Protocol and contract tests

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-2.2 | Compatibility policy | C-2.2 | ✅ | `Core/Net/TheatreNetwork.cs:10`, `Core/ScreenSession.cs:195` | unit (`ProtocolTests`: old theatre, unknown types) |
| WIN-2.3 | Contract tests against `protocol/examples` | C-2.3 | 🟨 | `Tests/ProtocolTests.cs` | Parses `protocol/examples/server` only, and passes silently when the protocol folder is missing; client messages aren't compared with `examples/client` |
| WIN-2.5 | Error envelope and fatal closes | C-2.5 | ✅ | `Core/ScreenSession.cs:234-282`, `App/MainWindow.xaml.cs:165-185` | Close 4001 carries the error code as the reason · 4000 = replaced |

## WIN-3. Identity and connection

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-3.1 | Display name, 1–32 characters, saved locally | C-3.1 | 🟨 | `App/MainWindow.xaml:26` (`NameBox`), `App/MainWindow.xaml.cs:58,131` | Not saved: resets to the Windows user name (or `--name`) each launch; an empty box becomes "Viewer" |
| WIN-3.2 | Change the display name at any time | C-3.2 | 🟨 | `Core/ScreenSession.cs:33,152,197` | Editing `NameBox` in a screen does nothing until the next join; needs the protocol's `rename` (C-3.2) |
| WIN-3.3 | Device secret | C-3.3 | ✅ | `Core/Identity/DeviceIdentity.cs:17-32`, `Core/KinoPaths.cs` | UI: manual. `%LOCALAPPDATA%\Kino\<profile>\device.key` (portable: `Data\<profile>\`), raw bytes, not DPAPI-protected |
| WIN-3.4 | Per-screen token | C-3.4 | ✅ | `Core/Identity/DeviceIdentity.cs:38-39` | unit (`Tokens_are_stable_per_screen…`, `Token_matches_theatre_side_hmac`) |
| WIN-3.5 | Reconnect restores everything | C-3.5 | ✅ | `Core/ScreenSession.cs` | live (`Network_drop_reconnects_silently_and_kick_does_not`) |
| WIN-3.6 | Host ownership by owner token (one-time claim) | C-3.6 | ✅ | `Core/ScreenSession.cs` (create, `hello.claim`) | live |
| WIN-3.7 | Automatic reconnect after a network drop | C-3.7 | 🟨 | `Core/ScreenSession.cs:16-19,234-282`, `Core/Net/ScreenConnection.cs` | live. Delays 300 ms, 1 s, 2 s, 3 s, then 5 s for 60 s. Re-syncs with 3 samples, not 8; a give-up leaves you on the video page |
| WIN-3.8 | One live session per viewer | C-3.8 | ✅ | `Core/ScreenSession.cs` (close 4000) | Stops on 4000, but shows "Disconnected (…)" on the video page (WIN-19.2) |
| WIN-3.9 | Profiles: separate identities on one device | C-3.9 | ✅ | `App/LaunchOptions.cs:55`, `Core/Media/LocalPrefs.cs:63`, `App/SingleInstance.cs:21-27` | live (profiles `test-live-host`, `test-live-viewer`). Shared by all profiles: `settings.json` (GIPHY key), `servers.json`, WebView2 data |
| WIN-3.P1 | Command-line options and test hooks | Platform-only | ✅ | `App/LaunchOptions.cs` | `--profile`, `--name`, `--theatre`, `--directory`, `--create`, `--join`, `--window`, `--panel`, `--synclog`, `--perturb`, `--seeks`, `--sub*`, `--youtube`; used by the live sync tests in the README |

## WIN-4. Theatre discovery and lobby

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-4.1 | Theatre list: live, then cached, then bundled | C-4.1 | ✅ | `Core/Net/TheatreDirectory.cs`, `App/MainWindow.Lobby.cs:80-106` | unit (`TheatreDirectoryTests`, 5). Cache `%LOCALAPPDATA%\Kino\servers.json`; directory fetch uses the 10 s HTTP timeout |
| WIN-4.2 | Parallel `/info` probes and theatre latency | C-4.2 | ✅ | `Core/Net/TheatreNetwork.cs:43-63` | unit (`Fastest_skips_unhealthy_and_incompatible_theatres`). Probes reused for 60 s when creating |
| WIN-4.3 | Incompatible theatres are unusable, and say why | C-4.3 | 🟨 | `Core/Net/TheatreNetwork.cs:10` | An incompatible theatre just looks "unavailable" |
| WIN-4.4 | Unavailable theatres greyed out and not usable for create | C-4.4 | 🟨 | `App/MainWindow.Lobby.cs:128-158,203-221` | Picker items get " · unavailable" but stay selectable; the lobby strip greys a theatre only when `/lobby` fails |
| WIN-4.5 | Create suggests the fastest usable theatre | C-4.5 | ✅ | `App/MainWindow.Lobby.cs:131-134,160` | unit |
| WIN-4.6 | Unified lobby across theatres | C-4.6 | ✅ | `Core/Lobby/LobbyView.cs`, `App/MainWindow.Lobby.cs:195-201` | unit (`LobbyViewTests`, `Lobby_fetch_survives_a_down_theatre`) |
| WIN-4.7 | Listing fields | C-4.7 | ✅ | `App/LobbyRow.cs`, `App/MainWindow.xaml:260-281` | UI: manual |
| WIN-4.8 | "Now playing" in the listing (host setting) | C-4.8 | ⬜ |  | Needs the protocol field first |
| WIN-4.10 | Refresh and ping cadence | C-4.10 | 🟨 | `App/MainWindow.Lobby.cs:43-73,165-185` | Opening the lobby doesn't re-ping; auto-refresh re-probes when no theatre is usable |
| WIN-4.11 | Partial results with a 3 s per-theatre timeout | C-4.11 | ✅ | `Core/Net/TheatreNetwork.cs:31` | unit (`Slow_theatre_times_out_without_blocking`) |
| WIN-4.12 | Sort, filter and search | C-4.12 | ✅ | `Core/Lobby/LobbyView.cs:45-67` | unit (4 tests) |
| WIN-4.13 | Loading and empty states | C-4.13 | ✅ | `App/MainWindow.Lobby.cs:223-226` | UI: manual |
| WIN-4.14 | Full screens shown as full and not joinable | C-4.14 | ✅ | `App/MainWindow.Lobby.cs:229-257` | unit (full screens last); full rows at 50% opacity and blocked |

## WIN-5. Screens: create, join, leave, settings, lifecycle

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-5.1 | Terminology in the UI | C-5.1 | ✅ | UI text | Theatre, Screen, Host, Co-host, Viewer |
| WIN-5.2 | Create a screen | C-5.2 | ✅ | `App/MainWindow.Lobby.cs:370-404`, `App/MainWindow.xaml:311-334` | live (`Private_screen_rejects_wrong_password…`) |
| WIN-5.2.1 | Name required, 1–64 | C-5.2.1 | ✅ | `ScreenNameBox` | UI: manual |
| WIN-5.2.2 | Public or private, password required for private | C-5.2.2 | ✅ | `PrivateSwitch`, `CreatePasswordBox` | Live |
| WIN-5.2.3 | "Hide from lobby" shown only when private | C-5.2.3 | ✅ | `HideFromLobbyBox` | UI: manual |
| WIN-5.2.4 | Max viewers 2–25, default 10 | C-5.2.4 | ✅ | `MaxViewersBox` | UI: manual |
| WIN-5.3 | Internal screen ID never shown | C-5.3 | ✅ |  | UI: manual |
| WIN-5.5 | Join from the lobby | C-5.5 | ✅ | `App/MainWindow.Lobby.cs:229-257` | UI: manual |
| WIN-5.7 | Snapshot on join | C-5.7 | ✅ | `Core/ScreenSession.cs:216` | Unit (welcome round-trip) |
| WIN-5.8 | Leave frees the seat at once | C-5.8 | ✅ | `Core/ScreenSession.cs:552-560` | Live |
| WIN-5.9 | 60 s reconnect grace, shown to others | C-5.9 | ✅ | `App/ViewerRow.cs:50,79` | Clients UI: manual |
| WIN-5.11 | Screen settings | C-5.11 | 🟨 | `App/MainWindow.Screen.cs:29-91`, `App/MainWindow.xaml:417-444` | UI: manual; theatre side Th smoke |
| WIN-5.11.1 | Rename the screen | C-5.11.1 | 🟨 | `SettingsNameBox` | The "needs a name" error goes to the overlay, hidden behind the settings page |
| WIN-5.11.2 | Visibility and password | C-5.11.2 | 🟨 | `SettingsPrivateSwitch`, `SettingsPasswordBox` | The "Set a password" error goes to the hidden overlay |
| WIN-5.11.3 | Hide from lobby | C-5.11.3 | ✅ | `SettingsHiddenBox` | UI: manual |
| WIN-5.11.4 | Max viewers | C-5.11.4 | ✅ | `SettingsMaxViewersBox` | UI: manual |
| WIN-5.11.5 | Show devices | C-5.11.5 | ✅ | `SettingsDevicesSwitch` | UI: manual |
| WIN-5.11.6 | Auto-advance | C-5.11.6 | ✅ | `SettingsAutoAdvanceSwitch` | UI: manual |
| WIN-5.11.7 | Buffering policy | C-5.11.7 | ✅ | `SettingsBufferingSwitch` | UI: manual |
| WIN-5.13 | Close the screen | C-5.13 | ✅ | `App/MainWindow.Viewers.cs:154-162` | UI: manual |
| WIN-5.13.1 | Host closes, with confirmation | C-5.13.1 | ✅ | `CloseScreenButton` | Confirmation flyout "Close the screen for everyone" |
| WIN-5.13.2 | Everyone else returns to the lobby with the reason | C-5.13.2 | ✅ | `App/MainWindow.xaml.cs:168-185` | UI: manual |

## WIN-6. Invites and private screens

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-6.2 | Code entry: normalised, and accepts a pasted link | C-6.2 | 🟨 | `App/MainWindow.Lobby.cs:282-332`, `Core/InviteLink.cs:28-29` | Typed codes aren't normalised client-side (the theatre does it); a link pasted into the code box isn't parsed |
| WIN-6.3 | Invite link formats | C-6.3 | ✅ | `Core/InviteLink.cs:18-35` | unit (`InviteLinkTests`) |
| WIN-6.5 | Join by code across every theatre | C-6.5 | ✅ | `Core/Net/TheatreNetwork.cs:98-130` | unit (`Resolve_finds_the_code…`, `Resolve_reports_unreachable…`) |
| WIN-6.6 | Private join: password prompt in place | C-6.6 | ✅ | `App/MainWindow.Lobby.cs:282-332` (`JoinPasswordBox`) | live |
| WIN-6.8 | Members rejoin without the password | C-6.8 | 🟨 |  | Reconnects skip the password; not checked whether a later join from the lobby still asks |
| WIN-6.9 | Invite panel: code and links with copy | C-6.9 | ✅ | `App/MainWindow.Screen.cs:29-41,93-113` | Copy puts "Join my screen on Kino: <link> (code X)" on the clipboard; "Copy code" too; no `kino://` link offered (the web link opens the app) |
| WIN-6.10 | Regenerate the code | C-6.10 | ✅ | `App/MainWindow.Screen.cs:115-123` | Confirmation flyout |
| WIN-6.11 | `screen.invite` controls who sees the code | C-6.11 | ⬜ |  | Spec and code disagree (Appendix B 8) · decide first |
| WIN-6.12 | Opening invite links on each platform | C-6.12 | ✅ | `App/SingleInstance.cs`, `installer/Kino.iss:83-99` | `kino://` written to `HKCU\Software\Classes\kino` at every launch (a portable copy won't take over an installed one); https invite links only as launch arguments |

## WIN-7. Host, co-hosts and handover

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-7.1 | Appoint and remove co-hosts | C-7.1 | ✅ | `App/MainWindow.Viewers.cs:93-105`, `Core/ScreenSession.cs:410-431` | live (`Co_host_takes_over_the_timeline…`) |
| WIN-7.2 | Co-host priority order | C-7.2 | ✅ | `App/MainWindow.Viewers.cs:93-105` | UI: manual |
| WIN-7.3 | Away co-hosts listed | C-7.3 | ✅ | `App/MainWindow.Viewers.cs:33-36` | UI: manual |
| WIN-7.4 | Acting host takes over the timeline | C-7.4 | ✅ | `App/ViewerRow.cs:39`, `Core/ScreenSession.cs:441` | live |
| WIN-7.5 | Acting host limits | C-7.5 | ✅ | `App/MainWindow.Viewers.cs:39,93` | UI: manual |
| WIN-7.8 | Transfer ownership | C-7.8 | ✅ | `App/MainWindow.Viewers.cs:106` | Confirmation "Yes, make X the host (you become co-host #1)" |

## WIN-8. Roles and permissions

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-8.1 | Permission catalogue (19 permissions) | C-8.1 | ✅ | `Core/Protocol/Permissions.cs` | Unit (`Rank_matches_the_theatre`) |
| WIN-8.2 | Role model and limits | C-8.2 | 🟨 | `App/MainWindow.Roles.cs` | The 20-custom-role cap isn't checked before sending; the theatre's error lands in the overlay |
| WIN-8.4 | Resolution order; effective permissions from the theatre | C-8.4 | ✅ | `Core/Protocol/Messages.cs` | Unit (parse) · clients never compute permissions |
| WIN-8.5 | Roles page | C-8.5 | ✅ | `App/MainWindow.Roles.cs:26-215`, `App/MainWindow.xaml:341-399` | UI: manual; theatre side Th smoke |
| WIN-8.5.1 | List roles by rank, with colour | C-8.5.1 | ✅ | `RolesList` | UI: manual |
| WIN-8.5.2 | Create a role | C-8.5.2 | ✅ | `NewRoleButton`, `RoleFields` | UI: manual |
| WIN-8.5.3 | Edit a role | C-8.5.3 | ✅ | `SaveRoleButton` | UI: manual |
| WIN-8.5.4 | Delete a role | C-8.5.4 | ✅ | `DeleteRoleButton` | UI: manual |
| WIN-8.5.5 | Built-in roles: Viewer permissions only; Co-host read-only | C-8.5.5 | ✅ | `App/MainWindow.Roles.cs:110` | UI: manual |
| WIN-8.6 | Assign roles to viewers | C-8.6 | ✅ | `App/MainWindow.Viewers.cs:110-122` | UI: manual |
| WIN-8.7 | Per-viewer overrides: Inherit, Allow, Deny | C-8.7 | ✅ | `App/MainWindow.Roles.cs:219-261` | UI: manual |
| WIN-8.9 | Permission-aware UI | C-8.9 | 🟨 | `App/MainWindow.xaml.cs:214-223` and each page | Everything follows permissions except `screen.invite` (only Regenerate is gated; C-6.11) |
| WIN-8.10 | Roles shown in the viewer list | C-8.10 | ✅ | `App/ViewerRow.cs:11-42` | Role names as plain text; one colour dot (the highest coloured role) |

## WIN-9. Moderation

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-9.1 | Chat mute and unmute | C-9.1 | ✅ | `App/MainWindow.Viewers.cs:133-144` | Acts at once |
| WIN-9.2 | Kick | C-9.2 | ✅ | `App/MainWindow.Viewers.cs:133-144` | live. "Kick (can rejoin)" acts at once |
| WIN-9.3 | Ban | C-9.3 | ✅ | `App/MainWindow.Viewers.cs:83-90` | Confirmation submenu |
| WIN-9.4 | Viewer actions menu, filtered by rank and permission | C-9.4 | ✅ | `App/MainWindow.Viewers.cs:68-162` | "No actions available" when none apply |
| WIN-9.5 | Being kicked or banned | C-9.5 | ✅ | `App/MainWindow.xaml.cs:168-185` | Clients UI: manual |

## WIN-10. Viewers panel: devices, latency and sync status

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-10.1 | Viewer rows: name, you, host, acting host, co-host #n | C-10.1 | ✅ | `App/ViewerRow.cs`, `App/MainWindow.Viewers.cs:26-29` | unit (parse); UI: manual |
| WIN-10.2 | Device reported in `hello` | C-10.2 | ✅ | `Core/ScreenSession.cs` (`hello.device`) | `windows`, `web` (`linux`, `android` later) |
| WIN-10.3 | Device icon per viewer | C-10.3 | 🟨 | `App/ViewerRow.cs:63` | Text ("Windows"), no icon |
| WIN-10.4 | Show devices off: no devices sent or shown | C-10.4 | ✅ | `App/ViewerRow.cs:63` | UI: manual |
| WIN-10.5 | Own latency, updated live | C-10.5 | ✅ | `App/MainWindow.xaml.cs:292-300` | In the sync overlay (can be toggled off) |
| WIN-10.6 | Others' latency with colour bands | C-10.6 | ✅ | `App/ViewerRow.cs:64-68` | 🟢 < 100, 🟡 100–250, 🔴 > 250, "– ms" when unknown |
| WIN-10.7 | RTT reported after every clock sync | C-10.7 | ✅ | `Core/ScreenSession.cs:510-519` | live (RTT reported after each burst) |
| WIN-10.8 | Sync status per viewer (six states) | C-10.8 | ✅ | `App/ViewerRow.cs:53-61` | Emoji per status |
| WIN-10.9 | Status reporter: on change only, buffering after 2 s | C-10.9 | ✅ | `Core/Sync/StatusReporter.cs` | unit (`StatusReporterTests`) |
| WIN-10.10 | Reconnecting viewers dimmed | C-10.10 | ✅ | `App/ViewerRow.cs:50,79` | UI: manual |
| WIN-10.11 | Chat-mute icon | C-10.11 | ✅ | `App/MainWindow.xaml:75-76` | UI: manual |

## WIN-11. Sync engine

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-11.1 | Sync only, never stream | C-11.1 | ✅ |  | By design: no media is ever sent |
| WIN-11.2 | Clock sync burst on join | C-11.2 | ✅ | `Core/Sync/ClockSync.cs`, `Core/ScreenSession.cs:510-519` | unit (`ClockSyncTests`) |
| WIN-11.3 | Periodic re-sync and smoothing | C-11.3 | ✅ | `Core/Sync/ClockSync.cs:47-49`, `Core/ScreenSession.cs:538-542` | unit (`Blends_small_changes_and_replaces_large_jumps`) |
| WIN-11.4 | Immediate re-sync when RTT jumps by more than 50% | C-11.4 | ⬜ |  | No such code |
| WIN-11.5 | Anchors and `seq` ordering | C-11.5 | ✅ | `Core/Sync/SyncEngine.cs:119-137` | unit (`Stale_anchors_are_ignored`) |
| WIN-11.6 | Expected position (latency compensation) | C-11.6 | ✅ | `Core/Sync/SyncEngine.cs` | unit (`SyncEngineTests`) |
| WIN-11.7 | Scheduled start | C-11.7 | ✅ | `Core/Sync/SyncEngine.cs:192-197` | unit (scheduled start on time) |
| WIN-11.8 | Anchors applied on arrival | C-11.8 | ✅ | `Core/Sync/SyncEngine.cs:119-137` | unit (`New_anchor_is_applied_on_arrival…`) |
| WIN-11.9 | Drift measurement | C-11.9 | ✅ | `Core/Sync/SyncEngine.cs:244-251` | unit |
| WIN-11.10 | Green zone with hysteresis | C-11.10 | ✅ | `Core/Sync/RateController.cs:21-22` | unit (`Hysteresis_keeps_correcting_until_exit_threshold`) |
| WIN-11.11 | Exact seek while paused | C-11.11 | ✅ | `Core/Sync/SyncEngine.cs:169-179` | unit. Ignores the personal offset while paused (master Appendix B 16) |
| WIN-11.12 | Smooth rate controller with pitch correction | C-11.12 | ✅ | `Core/Sync/RateController.cs`, `Player/MpvPlayer.cs:52,93` | unit (`Converges_smoothly_without_overshoot`: bounds 25 s, 40 s, 85 s for 0.3, 1.0, 3.0 s) |
| WIN-11.13 | Hard seek with learned seek latency | C-11.13 | ✅ | `Core/Sync/SyncEngine.cs:253-260,275-306` | unit (`Viewer_far_off_hard_seeks_then_converges`, throttle tests) |
| WIN-11.14 | Late anchors | C-11.14 | ✅ | `Core/Sync/SyncEngine.cs:128-132,235-242` | unit (`Host_obeys_a_seek_that_arrives_after_its_start_time`) |
| WIN-11.15 | Host re-anchor | C-11.15 | ✅ | `Core/Sync/SyncEngine.cs:263-272` | unit (`Host_reanchors_instead_of_bending_playback`); at most every 2 s |
| WIN-11.16 | Late join and reconnect catch-up | C-11.16 | ✅ | `Core/Sync/SyncEngine.cs:218-229` | live |
| WIN-11.17 | Buffering policy "Wait for everyone" | C-11.17 | ✅ | `App/MainWindow.Notices.cs:44-48,90-93` | live (`Buffering_viewer_holds_the_screen…`). "Resume anyway" shown to everyone with `playback.toggle` |
| WIN-11.18 | Playlist transitions | C-11.18 | ✅ | `Core/Sync/SyncEngine.cs:203-215` | unit (`PlaylistEngineTests`) |
| WIN-11.19 | Ready check | C-11.19 | ✅ | `App/MainWindow.Notices.cs:18-88` | live (`…ready_checks_tally`). Shown only on the video page |
| WIN-11.19.1 | Start a ready check | C-11.19.1 | ✅ | `ReadyCheckButton` (`App/MainWindow.Viewers.cs:40`) | unit (`SyncEngineTests`) |
| WIN-11.19.2 | Prompt with Ready and Not yet | C-11.19.2 | ✅ | `ReadyYesButton`, `ReadyNoButton` | unit (`SyncEngineTests`) |
| WIN-11.19.3 | Live tally | C-11.19.3 | ✅ | `NoticeDetail` | unit (`SyncEngineTests`) |
| WIN-11.19.4 | 30 s countdown | C-11.19.4 | ✅ | `NoticeDetail` | unit (`SyncEngineTests`) |
| WIN-11.19.5 | Result shown | C-11.19.5 | ✅ | `App/MainWindow.Notices.cs` | Shown for 5 s over the video and posted to chat |
| WIN-11.20 | Personal sync offset | C-11.20 | ✅ | `Core/Sync/SyncEngine.cs:103`, `App/MainWindow.Player.cs:68` | unit (`Personal_offset_shifts_target`). Enabled only in a screen |
| WIN-11.21 | Sync readout for the viewer | C-11.21 | ✅ | `App/MainWindow.xaml.cs:292-300` | Debug-style overlay: drift, rate, RTT, zone |
| WIN-11.22 | Shared sync test vectors across engines | C-11.22 | ⬜ |  |  |
| WIN-11.23 | Sleep, lock and background behaviour | C-11.23 | 🟨 | `Core/ScreenSession.cs` (reconnect) | Sleep drops the socket and the reconnect recovers; no test |

## WIN-12. Media player

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-12.1 | Playback engine integration | C-12.1 | 🟨 | `Player/MpvPlayer.cs`, `Player/VideoHost.cs` | No automated test drives libmpv (`Kino.Player` isn't in the test project). `vo=gpu-next`, `hwdec=auto-safe`, `hr-seek=yes`, `keep-open=yes` |
| WIN-12.2 | Core playback controls | C-12.2 | ✅ | `App/MainWindow.xaml.cs:386-410` | UI: manual. Seek is sent on pointer release or any key-up on the slider (a stray Tab also commits) |
| WIN-12.3 | Fine playback rate with pitch correction | C-12.3 | ✅ | `Player/MpvPlayer.cs:52` | UI: manual |
| WIN-12.4 | Seek bar with hover preview time | C-12.4 | ⬜ | `App/MainWindow.xaml:498-500` | `IsThumbToolTipEnabled="False"`; only `TimeText` |
| WIN-12.5 | Audio track selection | C-12.5 | ✅ | `Player/MpvPlayer.cs:134-149`, `App/MainWindow.Player.cs:95-100` | "title · LANG · codec · N ch" |
| WIN-12.6 | Fullscreen | C-12.6 | ✅ | `App/MainWindow.Player.cs:192-212` | F, F11, button; Esc leaves; the transport bar stays |
| WIN-12.7 | Keyboard shortcuts | C-12.7 | 🟨 | `App/MainWindow.Player.cs:216-255` | All keys exist; a focused slider eats ←/→; nothing while the WebView2 has focus; `SeekBy` reads libmpv during YouTube items |
| WIN-12.9 | Buffering and end-of-file detection | C-12.9 | 🟨 | `Player/MpvPlayer.cs:248` | Only `paused-for-cache` |
| WIN-12.10 | System media controls go through the screen | C-12.10 | ⬜ |  | No System Media Transport Controls; media keys do nothing |
| WIN-12.11 | Keep the display awake while playing | C-12.11 | 🟨 | `Player/MpvPlayer.cs` (libmpv defaults) | libmpv's default `stop-screensaver` is assumed to work with `wid`; not verified |
| WIN-12.P1 | Video in a child window; overlays as windowed popups (airspace) | Platform-only (C-12.1) | ✅ | `Player/VideoHost.cs`, `App/MainWindow.xaml:450-478` | The native window is hidden on non-video pages; WIN-23.P3 revisits this |
| WIN-12.P2 | Minimum window size | Platform-only | ⬜ |  | None set; at small sizes the transport bar and side panel overlap (720×480 proposed in C-23.6.1) |
| WIN-12.P3 | DPI changes move the video window correctly | Platform-only | 🟨 | `App/MainWindow.xaml.cs:323-331` | Uses `RasterizationScale` on `SizeChanged` only; moving to a monitor with another scale doesn't reposition until a resize. Initial 1280×800 isn't DPI-scaled |

## WIN-13. Local media, playlists and file matching

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-13.1 | Pick a single local file | C-13.1 | ✅ | `App/MainWindow.xaml.cs:354-362` | UI: manual (pickers use the `*` filter) |
| WIN-13.2 | Add a folder | C-13.2 | ✅ | `App/MainWindow.Playlist.cs:187-226` | Top level only |
| WIN-13.3 | Multi-select files | C-13.3 | ✅ | `App/MainWindow.Playlist.cs:187-226` | UI: manual |
| WIN-13.4 | Natural A–Z order, ignoring case | C-13.4 | ✅ | `Core/Media/MediaMatch.cs:30-34` | Unit |
| WIN-13.5 | The same media file types on every platform | C-13.5 | ✅ | `Core/Media/MediaMatch.cs` | Identical 26-extension lists |
| WIN-13.6 | Drag files in from the system | C-13.6 | ⬜ |  | No drag and drop from Explorer |
| WIN-13.7 | Host-defined order: drag and drop, Move up and Move down | C-13.7 | ✅ | `App/MainWindow.Playlist.cs:278-279,297-309` | UI: manual |
| WIN-13.8 | Remove an item | C-13.8 | ✅ | `App/MainWindow.Playlist.cs:343` | UI: manual |
| WIN-13.9 | Play an item now | C-13.9 | ✅ | `App/MainWindow.Playlist.cs:332` | UI: manual |
| WIN-13.10 | "Open now" for a file or a link | C-13.10 | 🟨 | `App/MainWindow.xaml.cs:368-376` | One new file sends `load`; links are only appended (`LoadUrlAsync` unused) |
| WIN-13.11 | Playlist limits and chunked adds | C-13.11 | ✅ | `App/MainWindow.Playlist.cs` | Th unit (limits enforced by the theatre); chunks of 100 |
| WIN-13.12 | Automatic matching: duration first, then name | C-13.12 | ✅ | `Core/Media/MediaMatch.cs:77-121` | unit (`AutoMap_prefers_duration_then_name…`, `AutoMap_keeps_manual_mappings`) |
| WIN-13.13 | "Choose my file…" per item | C-13.13 | ✅ | `App/MainWindow.Playlist.cs:335-336` | UI: manual |
| WIN-13.14 | "Match my files…" (folder or several files) | C-13.14 | ✅ | `App/MainWindow.Playlist.cs:202-211,353-365` | "Matched n of m items" |
| WIN-13.15 | "Pick your file" when an unmapped item comes up | C-13.15 | ✅ | `App/PlaylistRow.cs:31-35`, `App/MainWindow.Playlist.cs:381-382` | unit (`Holds_while_the_current_item_has_no_file_loaded`) |
| WIN-13.16 | Duration tolerance max(2 s, 0.5%) | C-13.16 | ✅ | `Core/Media/MediaMatch.cs:37-40` | unit |
| WIN-13.17 | Name comparison | C-13.17 | ✅ | `Core/Media/MediaMatch.cs:43-62` | unit (`Names_ignore_case_extension_and_separators`) |
| WIN-13.18 | Mismatch indicator and "different name" note | C-13.18 | ✅ | `App/PlaylistRow.cs:42-49` | unit (`Compare_checks_duration_first_then_name`); UI: manual |
| WIN-13.19 | Only name and duration leave the device | C-13.19 | ✅ | `Core/ScreenSession.cs:290-307` | UI: manual |
| WIN-13.20 | Mixed playlists | C-13.20 | ✅ | `App/PlaylistRow.cs` | live (`Playlist_reaches_everyone_and_advances_on_the_theatre`) |
| WIN-13.21 | Resume position | C-13.21 | 🟨 | `App/MainWindow.Player.cs:37-60,259-266`, `Core/Media/LocalPrefs.cs` | unit (`LocalPrefsTests`). Used only when watching alone |
| WIN-13.22 | Watch alone, outside a screen | C-13.22 | ✅ | `App/MainWindow.xaml.cs:114-127` | Also: a file opened before joining becomes the first item of an empty screen |

## WIN-14. External media: links and YouTube

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-14.1 | Add a link: direct URL or YouTube, detected | C-14.1 | ✅ | `App/MainWindow.Playlist.cs:230-264`, `Core/Media/YouTube.cs` | unit (`YouTubeTests`) |
| WIN-14.2 | Direct URL items play on every viewer | C-14.2 | ✅ | `Player/MpvPlayer.cs` | UI: manual |
| WIN-14.3 | URL items get a duration, so they end and auto-advance | C-14.3 | ⬜ | `App/MainWindow.Playlist.cs:263` | Links are added with `duration: null`, so the theatre never ends them |
| WIN-14.5 | YouTube player | C-14.5 | ✅ | `App/YouTubePlayer.cs`, `App/MainWindow.YouTube.cs` | WebView2 on `https://kino-directory.srie.workers.dev/yt`; messages accepted only from that origin |
| WIN-14.6 | YouTube sync rules | C-14.6 | ✅ | `Core/Sync/SyncSettings.cs:12-21` | unit (`YouTube_settings_have_no_rate_correction`) |
| WIN-14.7 | Ads and YouTube buffering count as buffering | C-14.7 | ✅ | `App/YouTubePlayer.cs:115` | UI: manual |
| WIN-14.8 | End of a YouTube item: wait for everyone, host can skip | C-14.8 | ✅ | `App/YouTubePlayer.cs:176`, `App/MainWindow.YouTube.cs:94-98` | UI: manual |
| WIN-14.9 | YouTube titles | C-14.9 | ✅ | `App/MainWindow.YouTube.cs:106` | oEmbed, else "YouTube video {id}" |
| WIN-14.10 | YouTube errors explained | C-14.10 | ✅ | `App/YouTubePlayer.cs` | 101/150 embedding disabled, 100 private |
| WIN-14.P1 | YouTube page served from the directory site (an https origin) | Platform-only (C-14.5) | ✅ | `App/YouTubePlayer.cs:53-66`, main repo `directory/public/yt.html` | Devtools and context menu off, pop-ups blocked |
| WIN-14.P2 | Missing WebView2 runtime is explained | Platform-only | ⬜ |  | No runtime check found in the code; Windows 10 PCs without the Evergreen runtime would get no YouTube and no explanation |

## WIN-15. Codecs and formats

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-15.1 | Full codec support through libmpv / FFmpeg | C-15.1 | ✅ | `Player/MpvPlayer.cs:50`, `tools/Fetch-LibMpv.ps1` | `hwdec=auto-safe` |
| WIN-15.2 | Unsupported formats: a message that names the codec | C-15.2 | 🟨 | `Player/MpvPlayer.cs:225-261` | "This file uses the {codec} codec…", but in the overlay that is rewritten every 250 ms |

## WIN-16. Viewer customisation (local only)

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-16.1 | Zoom 50–400% with pan | C-16.1 | ✅ | `App/MainWindow.Player.cs:104-117`, `Core/Media/ViewSettings.cs` | unit (`Zoom_is_log2_scaled_and_clamped`) |
| WIN-16.2 | Aspect ratio override | C-16.2 | ✅ | `Core/Media/ViewSettings.cs:26` | UI: manual |
| WIN-16.3 | Fit modes | C-16.3 | ✅ | `Core/Media/ViewSettings.cs:49-60` | unit (`Fit_width_and_height_become_letterbox_or_crop`) |
| WIN-16.4 | Volume 0–100% and mute | C-16.4 | ✅ | `App/MainWindow.Player.cs:153-188` | UI: manual |
| WIN-16.5 | Volume boost to 200% with a one-time disclaimer | C-16.5 | ✅ | `BoostFlyout`, `App/MainWindow.Player.cs:153-188` | Going above 100 opens `BoostFlyout` once; acceptance saved in prefs |
| WIN-16.6 | Remember preferences: defaults and per file | C-16.6 | ✅ | `Core/Media/LocalPrefs.cs` | unit (`Per_file_overrides_win_over_defaults_and_round_trip`). `%LOCALAPPDATA%\Kino\<profile>\prefs.json` |
| WIN-16.7 | Local-only guarantee | C-16.7 | ✅ |  | No protocol message carries them |

## WIN-17. Subtitles

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-17.1 | Load an external subtitle file | C-17.1 | ✅ | `App/MainWindow.Subtitles.cs:138-149` | UI: manual |
| WIN-17.2 | A same-named file next to the video | C-17.2 | 🟨 | `Player/MpvPlayer.cs:42-58` | Left to libmpv's default; such a track is excluded from the choices |
| WIN-17.3 | Formats: SRT, WebVTT, ASS/SSA | C-17.3 | ✅ | `Core/ScreenSession.cs:330-334` | unit (`Subtitle_formats_come_from_the_extension`) |
| WIN-17.4 | Embedded tracks ("In the file") | C-17.4 | ✅ | `App/MainWindow.Subtitles.cs:96-97` | UI: manual |
| WIN-17.6 | "Also share with viewers" (upload) | C-17.6 | ✅ | `Core/Net/TheatreApi.cs:35-60`, `App/MainWindow.Subtitles.cs:151-163` | live (`Shared_subtitles_reach_viewers_intact`). Tick "Also share" before loading; 1 MiB checked client-side |
| WIN-17.7 | Stop sharing (unshare) | C-17.7 | ⬜ | `Core/ScreenSession.cs:347` (no caller) | No UI calls it |
| WIN-17.8 | Download shared subtitles | C-17.8 | ✅ | `Core/Net/TheatreApi.cs` | live. Cached in `%TEMP%\Kino\subs\<code>\` |
| WIN-17.9 | Viewer's choice per item | C-17.9 | ✅ | `App/MainWindow.Subtitles.cs:83-136` | UI: manual |
| WIN-17.10 | Timing offset per file, ±10 s | C-17.10 | ✅ | `App/MainWindow.Subtitles.cs:167-174` | unit (`Style_maps_to_libmpv_properties`) |
| WIN-17.11 | Style | C-17.11 | 🟨 | `Core/Media/SubtitleStyle.cs`, `App/MainWindow.Subtitles.cs:176-191` | No outline-colour control (fixed `#000000`); 7 fonts, size 20–90, 5 colours |
| WIN-17.12 | "{name} shared subtitles" announcement | C-17.12 | ✅ | `App/ChatRow.cs` | UI: manual |
| WIN-17.13 | No subtitles for YouTube items | C-17.13 | 🟨 | `App/MainWindow.Subtitles.cs:83` | The flyout still opens on YouTube items |

## WIN-18. Chat, reactions and GIFs

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-18.1 | Text chat with an unread count | C-18.1 | ✅ | `App/MainWindow.Chat.cs:52-63` | unit (`Chat_and_reaction_messages_parse`); groups messages within 2 min |
| WIN-18.2 | Chat history for late joiners | C-18.2 | ✅ | `Core/ScreenSession.cs:459-466` | UI: manual |
| WIN-18.3 | Emoji reactions | C-18.3 | ✅ | `App/MainWindow.Chat.cs:105-136` | UI: manual |
| WIN-18.3.1 | Send a reaction | C-18.3.1 | ✅ | `ReactionBar` | UI: manual |
| WIN-18.3.2 | Reactions float over the video for 3 s | C-18.3.2 | ✅ | `ReactionPopup` | UI: manual |
| WIN-18.4 | GIF search (GIPHY, key kept on the device) | C-18.4 | ✅ | `Core/Chat/GifSearch.cs`, `App/MainWindow.Chat.cs:140-195` | unit (`Giphy_results_give_a_preview…`). Key in `%LOCALAPPDATA%\Kino\settings.json` (plain text, all profiles) or `KINO_GIPHY_KEY` |
| WIN-18.5 | Pasted GIF links | C-18.5 | ✅ | `Core/Chat/GifSearch.cs:53-56` | unit (`Gif_links_match_the_theatre_allow_list`) |
| WIN-18.6 | System messages | C-18.6 | ✅ | `App/ChatRow.cs:20-25` | UI: manual |
| WIN-18.7 | Chat permissions and muted state | C-18.7 | ✅ | `App/MainWindow.Chat.cs:75-83` | Input disabled, "You are muted in this screen" |

## WIN-19. Notices, errors and resilience

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-19.1 | Plain-language text for every error code | C-19.1 | 🟨 | `App/MainWindow.Lobby.cs:434-444` | `rate_limited` and `forbidden` show the theatre's raw text |
| WIN-19.2 | Kicked, banned, closed or replaced: back to the lobby with the reason | C-19.2 | 🟨 | `App/MainWindow.xaml.cs:165-185` | Kicked, banned, closed: lobby with the reason. Replaced and give-ups: stay on the video page |
| WIN-19.3 | Reconnecting indicator, visible everywhere | C-19.3 | 🟨 | `App/MainWindow.xaml.cs:156-162` | `SessionText` only; hidden in fullscreen; the overlay copy is overwritten on the next tick |
| WIN-19.4 | Errors and notices stay long enough to read | C-19.4 | 🟨 | `App/MainWindow.xaml.cs:300`, `App/MainWindow.Viewers.cs:44-55` | No toasts; errors go to `OverlayText`, rewritten every 250 ms |
| WIN-19.5 | `update_required` tells people to update | C-19.5 | ✅ | `App/MainWindow.Lobby.cs:440` | "This version of Kino is too old for that theatre. Please update." (no link) |
| WIN-19.6 | Form errors shown where the form is | C-19.6 | 🟨 | `App/MainWindow.Screen.cs:77-78` | Settings-page errors go to the hidden overlay; `SettingsMessage` is never filled |
| WIN-19.7 | Progress for slow actions | C-19.7 | ✅ | `SessionText`, `JoinMessage` | Connecting, joining, creating, reading files |
| WIN-19.P1 | Local crash log | Platform-only | ✅ | `App/App.xaml.cs:13-28` | `crash.log` in the data folder; unobserved task exceptions are swallowed |

## WIN-20. Builds, distribution and updates

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-20.1 | Release packages | C-20.1 | ✅ | `installer/Kino.iss`, `tools/Build-Release.ps1` | Inno Setup per-user installer and portable zip for x64, x86, ARM64 |
| WIN-20.2 | Release pipeline in each repo's CI | C-20.2 | ✅ | `.github/workflows/release.yml` | Tag `v<version>` must equal `Directory.Build.props`; version-less names; `SHA256SUMS.txt` |
| WIN-20.3 | Signed releases | C-20.3 | 🟨 | `.signpath/`, `.github/workflows/release.yml:54-121` | Signs exe and dlls, then the installer, once `SIGNPATH_ORGANIZATION_ID` and `SIGNPATH_API_TOKEN` are set; libmpv passes through unsigned |
| WIN-20.4 | Update notice | C-20.4 | ✅ | `Core/Updates.cs`, `App/MainWindow.Updates.cs` | unit (`UpdatesTests`). Once per launch, 5 s after start; no UI to turn it off |
| WIN-20.6 | Bundled theatre list matches `servers.json` | C-20.6 | ✅ | `Core/Net/TheatreDirectory.cs` (`Bundled`) | Aurora and Nova |
| WIN-20.7 | About: version, licence, credits, privacy | C-20.7 | 🟨 | `App/MainWindow.Updates.cs:13,21` (`VersionText`) | `VersionText` "Kino 0.1.0" in the lobby only; no About page |
| WIN-20.P1 | Installer: per user, no admin, uninstaller | Platform-only (C-20.1) | ✅ | `installer/Kino.iss` | `PrivilegesRequired=lowest`; admins may install for all users; uninstall offers to delete `%LOCALAPPDATA%\Kino`; Windows 10 19041+ |
| WIN-20.P2 | Portable edition | Platform-only (C-20.1) | ✅ | `Core/KinoPaths.cs:10-16`, `tools/Build-Release.ps1:63-88` | A `portable` file next to `Kino.exe` puts data in `Data\`; own single-instance pipe |
| WIN-20.P3 | x64, x86 and ARM64 builds | Platform-only (C-20.1) | ✅ | `App/Kino.App.csproj:13-17`, `.github/workflows/release.yml:51-53` |  |
| WIN-20.P4 | Single instance: a second launch forwards to the first | Platform-only | ✅ | `App/SingleInstance.cs:13-65` | Named pipe per user and profile; forwards media and invite links only (`--join`, `--create` are ignored) |
| WIN-20.P5 | libmpv pinned and checked | Platform-only | ✅ | `tools/Fetch-LibMpv.ps1` | Build `20261108-git-55d3feb45c`, SHA-256 pinned |
| WIN-20.P6 | Release guard: tag must match the project version | Platform-only | ✅ | `.github/workflows/release.yml:21-31` | Versions with `-` publish as pre-releases |
| WIN-20.P7 | CI builds and tests every architecture | Platform-only | 🟨 | `.github/workflows/ci.yml` | CI builds x64 only; x86 and ARM64 are built only when releasing, and the release workflow has no format check |
| WIN-20.P8 | SmartScreen guidance until releases are signed | Platform-only | ✅ | `.github/release-notes.md` | Remove once WIN-20.3 is done |
| WIN-20.P9 | winget package (optional; not in the spec) | Platform-only | ⬜ |  | After signing, so installs don't trip SmartScreen |

## WIN-21. Quality and the cross-platform test

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-21.2 | Client core unit tests in CI | C-21.2 | ✅ | `Tests/` | About 140 test cases (85 methods) |
| WIN-21.3 | Live tests against a real theatre in CI | C-21.3 | 🟨 | `Tests/LiveTheatreTests.cs` | CI never sets `KINO_LIVE_THEATRE`; without it the live tests return early and count as passed |
| WIN-21.5 | UI test suite per client | C-21.5 | ⬜ |  | None |
| WIN-21.6 | Release test checklist per platform | C-21.6 | ⬜ |  | The manual part of the definition of done, written down |
| WIN-21.7 | Cross-platform sync test | C-21.7 | ⬜ |  | Sub-tasks below |
| WIN-21.7.1 | Test plan and matrix | C-21.7.1 | ⬜ |  |  |
| WIN-21.7.2 | Windows and Web in one screen: sync | C-21.7.2 | ⬜ |  |  |
| WIN-21.7.3 | Roles, permissions and moderation across platforms | C-21.7.3 | ⬜ |  |  |
| WIN-21.7.4 | Chat, GIFs and reactions across platforms | C-21.7.4 | ⬜ |  |  |
| WIN-21.7.5 | Shared subtitles across platforms | C-21.7.5 | ⬜ |  |  |
| WIN-21.7.6 | YouTube across platforms | C-21.7.6 | ⬜ |  | Needs C-14.5 on the web |
| WIN-21.7.7 | Handover across platforms | C-21.7.7 | ⬜ |  |  |
| WIN-21.7.8 | Network faults across platforms | C-21.7.8 | ⬜ |  |  |
| WIN-21.7.9 | Every platform in the same screen | C-21.7.9 | ⬜ |  | The gate for Phase 23 |
| WIN-21.7.10 | Automated Windows + Web live test | C-21.7.10 | ⬜ |  |  |
| WIN-21.8 | Soak and scale | C-21.8 | ⬜ |  | 25 viewers, 3 hours |

## WIN-22. Later / to be planned

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-22.1 | User-added theatres | C-22.1 | ⏸ |  |  |
| WIN-22.3 | Compact binary encoding | C-22.3 | ⏸ |  | Only if budgets require it |
| WIN-22.4 | Own YouTube player | C-22.4 | ⏸ |  | **Not yet planned.** The plan stays the official IFrame player in WebView2 (WIN-14.5); open questions are in the master's C-22.4 |

## WIN-23. Layout and Design Language rework (gated)

**Gated.** Nothing here starts until the master roadmap's Phase 23 gate passes (every feature complete and tested on Windows, Web, Android and Linux). The common tasks are specified in the master; the platform-only tasks are specified below the table.

| ID | Task | Common | Status | Where | Tests / notes |
|---|---|---|---|---|---|
| WIN-23.0 | Gate check | C-23.0 | ⬜ |  | Record the date and the commit of every repo when the gate passes |
| WIN-23.1 | UI/UX audit of every current screen | C-23.1 | ⬜ |  |  |
| WIN-23.1.1 | Screen inventory with screenshots | C-23.1.1 | ⬜ |  |  |
| WIN-23.1.2 | Heuristic review with severities | C-23.1.2 | ⬜ |  |  |
| WIN-23.1.3 | Accessibility audit (automated and manual) | C-23.1.3 | ⬜ |  |  |
| WIN-23.1.4 | Cross-platform consistency audit | C-23.1.4 | ⬜ |  |  |
| WIN-23.1.5 | Task-time baseline for the core tasks | C-23.1.5 | ⬜ |  |  |
| WIN-23.3 | Design language and tokens | C-23.3 | ⬜ |  | Columns = tokens wired into that platform |
| WIN-23.3.1 | Colour, "Sky & stars", light and dark | C-23.3.1 | ⬜ |  |  |
| WIN-23.3.2 | Typography | C-23.3.2 | ⬜ |  |  |
| WIN-23.3.3 | Spacing and sizing | C-23.3.3 | ⬜ |  |  |
| WIN-23.3.4 | Corner radius | C-23.3.4 | ⬜ |  |  |
| WIN-23.3.5 | Elevation | C-23.3.5 | ⬜ |  |  |
| WIN-23.3.6 | Motion | C-23.3.6 | ⬜ |  |  |
| WIN-23.3.7 | Token pipeline (one source, generated per platform) | C-23.3.7 | ⬜ |  |  |
| WIN-23.4 | Iconography and app icon | C-23.4 | ⬜ |  |  |
| WIN-23.5 | Component library specification | C-23.5 | ⬜ |  | Sub-tasks C-23.5.1 to C-23.5.18 below |
| WIN-23.6 | Layouts per form factor | C-23.6 | ⬜ |  |  |
| WIN-23.6.1 | Desktop window | C-23.6.1 | ⬜ |  |  |
| WIN-23.7 | Screen-by-screen redesign | C-23.7 | ⬜ |  | Sub-tasks C-23.7.1 to C-23.7.22 below |
| WIN-23.8 | Accessibility, WCAG 2.2 AA | C-23.8 | ⬜ |  |  |
| WIN-23.9 | Localisation readiness | C-23.9 | ⬜ |  |  |
| WIN-23.10 | Per-platform implementation | C-23.10 | ⬜ |  | Detail in each platform roadmap (WIN-23, WEB-23) |
| WIN-23.11 | Visual regression testing | C-23.11 | ⬜ |  |  |
| WIN-23.12 | Usability testing | C-23.12 | ⬜ |  |  |
| WIN-23.13 | Rollout | C-23.13 | ⬜ |  |  |
| WIN-23.P1 | Theme resources from the tokens; remove hard-coded colours | C-23.10.1 | ⬜ | `App/App.xaml`, `App/MainWindow.xaml`, `App/ViewerRow.cs:11-25`, `App/MainWindow.Chat.cs:121-124` |  |
| WIN-23.P2 | Window chrome: custom title bar, Mica, theme choice | C-23.10.1 | ⬜ | `App/MainWindow.xaml.cs` |  |
| WIN-23.P3 | Video composition that allows XAML over the video | C-23.10.1 | ⬜ | `Player/VideoHost.cs`, `Player/MpvPlayer.cs` | Spike first |
| WIN-23.P4 | Page structure: settings page, About page, first run | C-23.10.1 | ⬜ | `App/MainWindow.xaml` |  |
| WIN-23.P5 | Fluent controls for every component | C-23.10.1 | ⬜ |  |  |
| WIN-23.P6 | Icons: Fluent System Icons instead of emoji | C-23.4 | ⬜ |  |  |
| WIN-23.P7 | Accessibility: names, live regions, accelerators, Narrator | C-23.8 | ⬜ |  | No `AutomationProperties.Name` anywhere today |
| WIN-23.P8 | Localisation: `x:Uid` and `.resw` | C-23.9 | ⬜ |  | Every string is inline today |
| WIN-23.P9 | Responsive layout: compact, regular, wide; resizable side panel | C-23.6.1 | ⬜ | `SideColumn` (290 px fixed) |  |
| WIN-23.P10 | DPI and multi-monitor | C-23.6.1 | ⬜ |  | Builds on WIN-12.P3 |
| WIN-23.P11 | Installer pages in the new language | C-23.7.19 | ⬜ | `installer/Kino.iss` |  |
| WIN-23.P12 | Component gallery and UI screenshot tests | C-23.11.2 | ⬜ |  |  |
| WIN-23.P13 | UI work per playback tick | C-23.10.1 | ⬜ | `App/MainWindow.xaml.cs:292-300` |  |
| WIN-23.P14 | Preview channel and rollout | C-23.13 | ⬜ | `.github/workflows/release.yml` |  |

### WIN-23 platform tasks in detail

- **WIN-23.P1 Theme resources.** `ThemeDictionaries` (Light, Dark, HighContrast) generated from `design/tokens.json`
  (C-23.3.7); every `#E0202020`, `#B0202020`, `#CCFFFFFF`, `#DDFFFFFF`, `White`, `Black` and the host colour
  `#d64545` replaced by tokens. AC: a search of `src/Kino.App` for `#[0-9A-Fa-f]{6,8}` finds only the generated file;
  every surface follows Windows light, dark and the four high contrast themes.
- **WIN-23.P2 Window chrome.** `ExtendsContentIntoTitleBar` with a Kino title bar (screen name and connection state,
  drag regions that leave room for the caption buttons); Mica on Windows 11, a solid surface on Windows 10; a theme
  setting (Follow system, Light, Dark). AC: snap layouts, double-click to maximise and keyboard `Alt+Space` still work.
- **WIN-23.P3 Video composition.** Today libmpv draws into a child window, so nothing in XAML can be drawn over the
  video. Spike libmpv's render API (OpenGL via ANGLE or D3D11) into a `SwapChainPanel`, so controls, reactions and
  banners become ordinary XAML overlays. AC: no dropped frames on 4K60 HDR10 and 1080p on a mid-range GPU and on an ARM64
  laptop; `hwdec` still used; colour and HDR output unchanged; seek latency (C-11.13) not worse; if the spike fails,
  keep the child window and host all overlays in one transparent owned window instead of several popups.
- **WIN-23.P4 Page structure.** Add an app Settings page (theme, update check on/off, GIPHY key, data folder, profile),
  an About page (C-20.7), and a first-run page (C-23.7.17); keep Lobby, Screen, Roles and Invite & settings as pages.
- **WIN-23.P5 Fluent controls.** Transport bar as a `CommandBar`-style bar with the new seek bar (C-23.5.4); side panel
  tabs with `SelectorBar` and badges; `InfoBar` for notices (C-23.5.10); `ContentDialog` for destructive
  confirmations; `MenuFlyout` for viewer and playlist menus; `TeachingTip` for first-run hints.
- **WIN-23.P6 Icons.** Fluent System Icons (or Segoe Fluent Icons where identical) for every button now showing ⏮ ⏭ ▶
  ⏸ 🔊 🔇 ⛶ ✕ or other emoji. AC: C-23.4 AC 1–3.
- **WIN-23.P7 Accessibility.** `AutomationProperties.Name` (and `HelpText` where useful) on every icon-only control;
  `AutomationProperties.LiveSetting` on notices, the hold banner and the ready check; `KeyboardAccelerator` for every
  shortcut, shown in tooltips; access keys on the main commands; focus visuals; focus returned after dialogs and flyouts;
  shortcuts that work whatever has focus, including the YouTube view (C-12.7). AC: C-23.8 with Narrator and NVDA;
  Accessibility Insights FastPass clean.
- **WIN-23.P8 Localisation.** Every XAML string through `x:Uid` and `Strings/en-GB/Resources.resw`; code strings through
  `ResourceLoader`; plural rules; a pseudo-locale for testing. AC: C-23.9 AC 1–3.
- **WIN-23.P9 Responsive layout.** The width classes of C-23.6.1; the side panel resizable from 280 to 480 px (today a
  fixed 290 px column) and collapsible, width remembered; the transport bar hides after 3 s while playing.
- **WIN-23.P10 DPI and multi-monitor.** First window size scaled for the monitor's DPI; the video window (or panel)
  follows DPI changes without a resize; tested at 100%, 125%, 150%, 200% and 300%, and across two monitors with
  different scales.
- **WIN-23.P11 Installer.** Modern wizard style; wizard images supplied at 100% to 250% scale; wording in the new tone;
  the new app icon (C-23.4).
- **WIN-23.P12 Gallery and screenshot tests.** A debug-only component gallery window; FlaUI (or WinAppDriver) tests
  capture it and the key screens at 100%, 150% and 200%, light, dark and high contrast, on a Windows CI runner
  (C-23.11.2).
- **WIN-23.P13 UI work per tick.** The sync overlay is rewritten every 250 ms today; after the rework, per-tick work on
  the UI thread is limited to values that changed, with no allocation per tick (checked with a profiler during a 30
  minute session).
- **WIN-23.P14 Rollout.** A pre-release (`v0.x.0-preview.n`) with the new UI (C-23.13.1); the stable switch and the
  removal of the old XAML follow C-23.13.


## Changelog of this roadmap

- 2026-11-25: first version, generated from the master roadmap after a full audit of the code and tests at `0e86b29`.
