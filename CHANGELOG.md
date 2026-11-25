# Changelog

All notable changes to Kino for Windows. Versions follow [Semantic Versioning](https://semver.org): releases are
tagged `v<version>`, and each `## <version>` section here becomes that release's notes.

## Unreleased

- **Web viewers:** invite links from [Kino for the web](https://kino-web.srie.workers.dev) (`https://…/#<theatre>/<code>`)
  work in **Join a screen**. Web and Windows viewers can share a screen.

## 0.1.0 — 2026-11-25

The first release: watch videos together, in sync, with everyone playing their own copy.

- **Screens on free theatres:** one lobby across every theatre (Aurora, Nova); create public or private screens, join by
  code or `kino://` invite link; automatic reconnect.
- **Smooth sync:** clock sync, scheduled starts and gentle speed correction (no jumps for small drift); late joiners and
  reconnects catch up on their own.
- **Playlists:** local files, folders, direct links and YouTube; your copies are matched by length and name, with a
  warning if a file differs; the screen moves on to the next item by itself.
- **Hosts, co-hosts and roles:** Discord-style roles and permissions, co-hosts take over while the host is away,
  moderation (mute, kick, ban), screen settings, ready checks, "wait for everyone" when someone is buffering.
- **Chat:** messages, GIFs (GIPHY), reactions over the video, history for late joiners.
- **Subtitles:** embedded, your own, or shared with the screen; timing and look stay on your PC.
- **Player:** libmpv (practically every format, hardware decoding), fullscreen, keyboard shortcuts, audio tracks, zoom,
  aspect, fit, volume boost, personal sync offset, resume position.
- **Windows:** installer (per user, no admin) and portable editions for x64, x86 and ARM64; update notice in the lobby.
