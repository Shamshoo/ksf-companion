# KSF Companion on Linux

This branch is a native Linux build of KSF Companion: the same dashboard, Nominate and Binds pages,
in-game F5 / F6 / F7 keys, live stage times and play-later list, for Counter-Strike: Source running
natively on Linux (or through Proton).

## Install

1. Put `KSFCompanion` somewhere (e.g. Downloads), make it executable and run it:

   ```bash
   chmod +x KSFCompanion && ./KSFCompanion
   ```

   It copies itself to `~/.local/share/ksf-companion`, adds **KSF Companion** to your app menu,
   starts when you log in (tray icon > "Start when you log in" to turn that off) and opens the dashboard.
   Running a newer download updates it. A `portable.txt` next to the file keeps it where it is instead.

2. **Add `-usercon` to CS:S's launch options** — Steam > Counter-Strike: Source > Properties >
   Launch Options — and start (or restart) the game.

That's it. The dot on your avatar (top of the dashboard) turns green once the game is connected.

## Why `-usercon`?

On Windows, KSF Companion hands console commands to the game with a Windows window message. That doesn't
exist on Linux, so this build uses the game's own **remote console (RCON)**, the same channel tools like
TF2 Bot Detector use. `-usercon` makes the game listen for it; KSF Companion's block in `autoexec.cfg` sets a
random password (kept in `settings.ini`) and starts it (`net_start`). Commands still run from the console,
never in chat, and nothing touches the game's memory.

Without `-usercon`, the dashboard, map card, play-later list and F5 / F6 / F7 still work (they only use cfg
files and the console log). The things that need commands — Nominate / Rock the vote from the dashboard,
teleport arrows, live stage times (the timer demo), binds applied without a restart, `!m` / `!mrank` — wait
until it's there, and the dashboard says so.

The game listens on its usual port (27015/TCP) on all network interfaces while it runs, protected by that
password (and the engine bans an address after a few wrong guesses). Your firewall normally keeps it to
this computer anyway.

## Where things are

| What | Where |
| --- | --- |
| The app | `~/.local/share/ksf-companion/KSFCompanion` |
| Settings, play-later list | `~/Documents/KSF Companion` (`settings.ini`, `play-later.txt`) |
| Map pictures, map list, logs | `~/.cache/ksf-companion` |
| In the game | `cstrike/cfg/autoexec.cfg` (a marked block) and `cstrike/cfg/ksf_*.cfg` |

**Remove:** right-click KSF Companion in the app menu > *Uninstall KSF Companion* (it also takes it out of
CS:S and gives your F5/F6/F7 binds back), or tray icon > *Remove from CS:S...* for just the game part.

## Differences from the Windows build

- The game link is RCON (above) instead of `WM_COPYDATA` / `-hijack`.
- Fonts: [Barlow](https://github.com/jpt/barlow) and Inter stand in for Bahnschrift and Segoe UI; icons are
  Microsoft's open [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons).
- Desktop notifications go through `notify-send`; "Keep on top" is in the tray menu and on right-click of
  the title bar.
- The dashboard runs through XWayland on Wayland desktops, so it can still open itself on your second monitor.

## Build it yourself

Needs the .NET 10 SDK (`dotnet-install.sh --channel 10.0` puts one in `~/.dotnet` without root).

```bash
dotnet publish KSFCompanion/KSFCompanion.csproj -c Release -r linux-x64 -o dist
```

`dist/KSFCompanion` is a single self-contained file (no .NET needed to run it).

Handy checks: `KSFCompanion --link` (does the running game take commands?), `KSFCompanion --clock-test`,
`KSFCompanion --binds-test`, and `KSFCompanion --preview live out.png` (draws the dashboard with live KSF
data to a picture, no window needed).
