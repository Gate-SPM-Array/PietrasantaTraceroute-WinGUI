# Pietrasanta Traceroute GUI for Windows v1.2.1

**English** · [Italiano](README.it.md)

A Windows GUI for **[Pietrasanta traceroute](https://github.com/catchpoint/Networking.traceroute)** (Catchpoint), the enhanced Linux traceroute with **TCP InSession**, UDP InSession, QUIC and DSCP/ECN reporting.

Pietrasanta traceroute is a Linux-only program. It relies on Linux kernel features (the ICMP error queue, raw TCP sockets) that Windows does not provide. So instead of porting it, this GUI runs the **official, unmodified Linux binary inside WSL2**. Every mode works, including TCP InSession.

> **Language:** the program speaks **English** and **Italian**. Choose it from the **🌐 Language** menu; it applies at the next start (the program offers to restart). On first start it follows the Windows language. Translations are plain JSON files anyone can fix or add: see [Translations](#7-translations).

---

## 1. Requirements

- Windows 11 (or Windows 10 21H2 or later), x64
- **WSL2** with a real Linux distribution, Ubuntu 24.04 recommended. The `docker-desktop` distribution created by Docker Desktop is **not** usable and is ignored.
- .NET Framework 4.8: already part of Windows 11, nothing to install
- No administrator rights needed to use the GUI

## 2. Setup (once)

1. **Install Ubuntu** from a **normal** PowerShell (not "Run as administrator"):
   ```
   wsl --install -d Ubuntu-24.04
   ```
   You can also use the menu *Tools › Install Ubuntu 24.04 in WSL* (*Strumenti › Installa Ubuntu…*). When Ubuntu asks for it, create the Linux user.
   WSL distributions are registered **per Windows user**. If Ubuntu is installed with a different account, for example a separate admin account, it will not be visible to yours.
2. Extract the zip into any folder and run `PietrasantaTracerouteGUI_v1.2.1.exe`. Choose the distribution and press **Install / update tool in WSL** (*Installa / aggiorna tool in WSL*). It takes a few seconds. The status becomes *Ready – version 0.1.36*.
3. Recommended: *Tools › WSL networking in "mirrored" mode* (*Strumenti › Rete WSL in modalità "mirrored"*). With WSL's default NAT networking, an extra hop shows up: WSL's internal NAT, usually 172.x.x.1. Mirrored mode lets WSL use the PC's network adapters directly. `%USERPROFILE%\.wslconfig` is edited, with a backup copy.

The exe is not digitally signed. Windows SmartScreen may show "Windows protected your PC": choose *More info › Run anyway*, or right-click the file › Properties › *Unblock*.

## 3. Usage

- Enter the **Destination**, choose the **Method** (UDP, ICMP, TCP SYN, TCP InSession, TCP connect, UDP InSession, QUIC, DNS, DCCP) and press **▶ Start** or Enter.
- The *Command* field shows the exact command line that is run.
- **Path tab**: hops updated live, with RTT min/avg/max, loss, AS, and the TOS/DSCP/ECN received from each hop. Rows are colour-coded:
  - yellow: partial loss;
  - grey: no reply;
  - bold: the destination.
- **Tool output tab**: the full original output.
- **Exports:**
  - *Export CSV*: the hop table. Separator and decimals follow the Windows regional settings, so it opens straight in Excel.
  - *Export TXT report*: local and UTC time, host, WSL distribution, tool version and SHA-256, the command and the complete output. Useful as evidence.
- **More options:** *? › Tool options help* shows the tool's full `--help`. Options not on screen go in *Extra options*.

## 4. Portable program

Everything the program writes stays in its folder:

```
PietrasantaTracerouteGUI_v1.2.1\
  PietrasantaTracerouteGUI_v1.2.1.exe
  PietrasantaTracerouteGUI.settings.ini   last distribution, method, target history, language
  lang\en.json                            English translation (add more languages here)
```

- **Reading order:** first the exe folder, then `%APPDATA%\PietrasantaTracerouteGUI\settings.ini`. Settings found there are copied next to the exe on the first save.
- **Read-only folder:** if the exe folder is read-only (e.g. `C:\Program Files`), settings are saved in `%APPDATA%`. *? › About* shows the file in use.
- **Updates:** the settings file name has no version number, so replacing the exe keeps your settings.
- **What stays outside the folder:** only what has to, by its nature. That is the WSL distribution (with `/usr/local/bin/pietrasanta-traceroute` and `/usr/local/bin/pstr-run`) and, if mirrored networking is enabled, `%USERPROFILE%\.wslconfig`.

To remove the tool from WSL:
```
wsl -d Ubuntu-24.04 -u root rm /usr/local/bin/pietrasanta-traceroute /usr/local/bin/pstr-run
```

## 5. How it works

- The tool runs as root inside WSL (`wsl -d <distro> -u root --exec …`), so raw-socket modes work without extra configuration.
- Arguments are passed with `--exec`, without a shell. The destination is validated, and user input is never interpreted by a shell.
- `pstr-run` prints the Linux PID before starting the tool, so *■ Stop* can send it `SIGINT`.
- The embedded binary is the upstream `binaries/ubuntu24/x86_64/traceroute`, unmodified.
  SHA-256 `a998f88c2c9a2de1f71e5d6507ad9d6998dd1d2866361bcd93831689f1511902`.
  The GUI compares the installed copy with it and offers an update when they differ.

## 6. Building from source

Use the .NET SDK 8 (Windows, or Linux with Microsoft's official SDK build):

```
dotnet build -c Release
dotnet test -c Release
```

The exe is created in `src/PietrasantaTraceroute.Gui/bin/Release/net48/`. Release packages are named with the version, e.g. `PietrasantaTracerouteGUI_v1.2.1.zip`.

```
src/PietrasantaTraceroute.Gui/     WinForms GUI (net48)
  MainForm.cs                      window, run/stop, export
  Wsl.cs                           wsl.exe calls, tool install, .wslconfig
  TracerouteParser.cs              parser of the traceroute output
  Settings.cs                      portable settings
  L.cs                             translations (Italian text is the key)
lang/en.json                       English translation (embedded in the exe and copied next to it)
  res/pietrasanta-traceroute       embedded Linux binary (GPL, unmodified)
tests/PietrasantaTraceroute.Tests/ xunit tests (net8.0, run on Linux too)
third_party/pietrasanta-traceroute-0.1.36/   corresponding upstream source (GPL)
```

## 7. Translations

Same scheme as AdbSync. The Italian text in the code is the key: `L.T("Avvia")`. A language file maps each Italian text to its translation; anything missing stays in Italian.

```json
{ "code": "en", "language": "English", "translators": "...", "app_version": "1.2.1",
  "strings": { "Avvia": "Start", "hop: {0}": "hops: {0}" } }
```

- To fix a translation, edit `lang\en.json` next to the exe. It takes precedence over the copy embedded in the exe.
- To add a language, copy `en.json` to `lang\<code>.json` (e.g. `de.json`), change `code` and `language` and translate the values. It appears in the **🌐 Language** menu at the next start.
- Placeholders `{0}`, `{1}` must stay the same. A translation that changes them is ignored and the Italian text is shown instead.
- CSV column headers are not translated, so scripts that read the CSV keep working.

## 8. License

- The GUI (`src/`, `tests/`, documentation) is released under the **MIT** license (see [LICENSE](LICENSE)). The MIT license does not cover the Pietrasanta traceroute binary or `third_party/`.
- Pietrasanta traceroute is © Catchpoint Systems, Inc. and Dmitry Butskoy, licensed under the **GNU GPL v2 or later**. Its binary is included unmodified as a separate program. The corresponding source is in [`third_party/`](third_party/pietrasanta-traceroute-0.1.36).
- See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
