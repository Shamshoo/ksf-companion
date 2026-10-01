# KSF Companion

A second-monitor dashboard for **KSF surf** in **Counter-Strike: Source**.

## Download

### **[⬇ Download KSFCompanion.exe](../../releases/latest/download/KSFCompanion.exe)**

Double-click it. That's the whole install.

- No sign-in, no account, nothing else to install.
- It finds your CS:S and your Steam account by itself.
- Start (or restart) CS:S once and it connects.

> **Windows says "Windows protected your PC"?**
> That shows for any new app that isn't signed. Click **More info → Run anyway**.

## What it does

- **Live times:** your stage and bonus times appear as soon as you finish, with the gap to the record.
- **Map info:** see the map you're on, its tier, the top 10, and the time left (including extends).
- **Your rank:** see your KSF title, rank and points on 66 and 100 tick.
- **Nominate:** search every KSF map, see which ones you've done, and nominate one in one click.
- **Binds:** put restart, restart stage, save/load location and turn binds on any key. Nothing shows in chat.
- **Play later:** press F5 in game to save a map for later.
- **Servers:** see every KSF server, who's on, and the map, and join in one click.

## Is it VAC safe?

**Yes.** KSF Companion is not a cheat and works like a keybind, not a hack.

| ✅ What it does | ❌ What it never does |
|---|---|
| Adds a few config files (`ksf_*.cfg`) | Read or change the game's memory |
| Reads the game's console log | Inject anything into the game |
| Records a demo with the game's normal `record` command, and reads it | Change any game files |
| Sends normal console commands, like a bind does | Give you any advantage in game |
| Gets public data from [ksf.surf](https://ksf.surf) | |

VAC looks for programs that tamper with the game. This doesn't. Everything it does, you could do yourself by typing in the console.

## Update or remove

- **Update:** download the new version and double-click it. Your settings stay.
- **Remove:** go to Windows **Settings → Apps → KSF Companion → Uninstall**. It also removes its files from CS:S and puts your old key binds back.

**Needs:** Windows 10 or 11 and Counter-Strike: Source on Steam.

---

<sub>Not affiliated with KSF. Map data and pictures come from ksf.surf. ·
Build it yourself: `dotnet build KSFCompanion/KSFCompanion.csproj -c Release`</sub>
