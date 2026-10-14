# OrbMet

A small Windows panel that shows how much of your **Claude Code** and **Codex**
usage limits you have used, so you can see it at a glance while you work in
the terminal.

<p align="center"><img src="docs/screenshot.png" width="378" alt="The OrbMet strip with the Claude Code card open"></p>

OrbMet sits on the edge of your screen as a black strip with one ring per
provider. The ring fills as you use your limit and changes colour only when it
starts to matter: green below 50%, yellow below 75%, red above.

## Features

- One ring per provider, with the percentage the provider reports.
- Hover a ring to see every limit it reports (5-hour, weekly, per model),
  how much of each is used and when it resets.
- Click a ring to refresh it.
- Drag the strip to any edge or corner and it docks there, or leave it
  anywhere on screen. It remembers where.
- Never takes focus from your terminal.
- English and Turkish, switchable from the menu.

<p align="center"><img src="docs/docking.gif" width="600" alt="Dragging the strip round the screen: it docks to each edge, turns into an L in the corners and floats in the middle, then hovering each ring opens its card"></p>

## Requirements

- Windows 10 or 11, x64
- [Claude Code](https://docs.claude.com/en/docs/claude-code), [Codex CLI](https://github.com/openai/codex), or both

## Install

Download a zip from [Releases](../../releases), unpack it into any folder and
run `OrbMet.exe`. There is no installer.

| Download | Size | Needs |
| --- | --- | --- |
| `OrbMet-<version>-win-x64.zip` | ~70 MB | Nothing, runs as is |
| `OrbMet-<version>-win-x64-runtime-required.zip` | <1 MB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

OrbMet lives in the system tray. Right-click the strip or the tray icon for
the menu; **Exit** is in the tray menu.

## Connecting

### Codex

Nothing to set up. OrbMet finds `codex` on your `PATH` and asks it for your
rate limits through `codex app-server`, using the sign-in you already have.
It checks every two minutes and when you click the ring.

### Claude Code

Claude Code reports usage to its status line, so OrbMet needs to be connected
once: right-click the strip, open **Connections** and press **Connect**.

This changes one setting, `statusLine` in `~/.claude/settings.json`, so that
Claude Code runs OrbMet's small helper (`OrbMet.Capture.exe`) after each
response. Good to know:

- If you already had a status line, it keeps showing: the helper runs your
  command and prints its output.
- Your original settings file is backed up once to
  `%LOCALAPPDATA%\OrbMet\backups` before the first change.
- **Disconnect** puts your previous status line back exactly as it was.
- Usage updates when Claude Code finishes a response. OrbMet never sends
  prompts to make data appear.
- If you move the OrbMet folder, the Connections window says so and offers
  **Reconnect**.

## Privacy

OrbMet makes no network requests of its own and sends nothing anywhere.

- After each response, Claude Code hands the helper a summary of the session.
  The helper keeps only the rate-limit figures (percentages and reset times)
  and a one-way hash of the session id, in `%LOCALAPPDATA%\OrbMet`, and
  discards the rest.
- Codex usage comes from `codex app-server` and is kept in memory.
- Prompts, transcripts and credentials are never read or stored.

## How the numbers are shown

- Percentages are exactly what the provider reports; OrbMet does not estimate
  usage from tokens or cost.
- Missing data shows as `—`, never as 0%.
- When a limit's reset time passes, its old value is hidden until new data
  arrives, rather than shown as the new period's.
- Data that is not fresh carries its age, such as "As of 12 min ago".

## Uninstall

Press **Disconnect** in Connections if Claude Code is connected, exit OrbMet
from the tray, then delete its folder and `%LOCALAPPDATA%\OrbMet`.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build -c Debug
dotnet test -c Debug --no-build
```

Release packages:

```powershell
powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -Version 0.1.0
```

This writes both zips to `artifacts\`.

## Credits

Provider marks come from [Lobe Icons](https://github.com/lobehub/lobe-icons)
(MIT). Third-party licences are listed in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Claude and OpenAI names and marks belong to their owners. OrbMet is not
affiliated with or endorsed by Anthropic or OpenAI.

## License

[MIT](LICENSE)
