# KSF Companion

A dashboard for surfing on **KSF** servers in **Counter-Strike: Source**. Put it on your second monitor while you play.

- **Your times, live.** Your stage and bonus times appear the moment you finish, along with the gap to the record and your rank on every stage.
- **The map you're on.** See its picture, tier, stages, the top 10, and the time left on the map (including extends).
- **Your level.** See your KSF title, rank and points on 66 and 100 tick, and how far it is to the next title.
- **Nominate.** Browse every KSF map, see which ones you've done, nominate one or rock the vote in one click. Search tolerates typos, and you don't need to type `surf_`.
- **Binds.** Put restart, restart stage, save/load location, turn binds and any KSF command on a key. Your existing binds show up on the page. Nothing is ever typed in chat.
- **Play-later list.** Press F5 in game to save a map for later.
- **Live servers.** See every KSF server, who's on, what map, how long is left, and join in one click.

## Install (one click)

1. **[Download KSFCompanion.exe](../../releases/latest/download/KSFCompanion.exe)**
2. Double-click it. That's it.

It installs itself, adds a Start menu and desktop shortcut, starts with Windows (quietly, in the tray), and opens the dashboard.
- It finds your Counter-Strike: Source and your Steam account on its own.
- You don't sign in to anything.
- Start (or restart) CS:S once and it connects.

> **"Windows protected your PC"?** The exe isn't code-signed, so Windows SmartScreen warns about new downloads.
> Click **More info → Run anyway**.

To update, download the new version and double-click it. Your settings stay.
To remove it, go to **Settings → Apps → KSF Companion → Uninstall**. That also takes it out of CS:S and gives your keys back.

**Needs:** Windows 10 or 11 and Counter-Strike: Source from Steam. Nothing else to install (.NET Framework 4.8 comes with Windows).

## Is it safe? (VAC)

KSF Companion **never touches the game's memory** and doesn't inject anything. It only uses what the game offers anyone:

- It adds a few small `cfg` files (`ksf_*.cfg`) and a short block in `autoexec.cfg`. Removing the app takes them out again.
- It reads the game's console log and has the game record a demo, so it can read the timer's text as you play.
- It sends console commands the same way Steam does (for example `sm_restart`, `sm_nominate`). They run from the console, so nothing appears in chat.
- It reads public data from [ksf.surf](https://ksf.surf).

## Where things are

| | |
|---|---|
| App | `%LOCALAPPDATA%\Programs\KSF Companion` |
| Settings, play-later list | `Documents\KSF Companion` (`settings.ini` can be edited with Notepad) |
| Cache (map pictures, records) | `%LOCALAPPDATA%\KSF Companion` |

Want it to stay where you put it (e.g. on a USB stick)? Put an empty `portable.txt` next to the exe.

## Build it yourself

You need the .NET SDK (8 or newer) on Windows; the .NET Framework 4.8 targeting pack comes with Visual Studio or the "Developer Pack".

```
dotnet build KSFCompanion/KSFCompanion.csproj -c Release
```

The exe is `KSFCompanion/bin/Release/net48/KSFCompanion.exe`. Self-tests: `KSFCompanion.exe --clock-test` and `--binds-test`.

Releases are built by GitHub Actions: push a tag like `v3.2.0` and the release with `KSFCompanion.exe` appears under Releases.

---

Not affiliated with KSF. Map data and pictures come from [ksf.surf](https://ksf.surf).
