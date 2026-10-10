# ExaltHUD Standalone

[![Release](https://img.shields.io/github/v/release/dach6/ExaltHud?color=blue)](https://github.com/dach6/ExaltHud/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/dach6/ExaltHud/total)](https://github.com/dach6/ExaltHud/releases/latest)

**Version 1.0.0**

ExaltHUD is a standalone Windows combat dashboard for Realm of the Mad God Exalt, with an optional overlay. It uses Npcap to observe game traffic and runs independently of ExaltHelper.

## Features

- DPS, total damage, encounter history, and weapon, ability, and summon attribution.
- Player and equipment inspection, ability logs, party tracking, and loot history.
- Moonlight Village phase tracking and manual phase/flame controls.
- A desktop dashboard and optional movable, resizable overlay with independent tabs and scroll positions.
- Clickable scrollbars, thumb dragging, and mouse-wheel navigation.
- Bundled monster names, equipment icons, forge rarities, and enchantment definitions.

## Getting started

1. Use 64-bit Windows 10 or 11 with .NET Framework 4.8.
2. Install [Npcap](https://npcap.com/#download). Npcap is installed separately and is not bundled with ExaltHUD.
3. Download and extract the latest [ExaltHUD-Standalone-Release.zip](https://github.com/dach6/ExaltHud/releases/latest/download/ExaltHUD-Standalone-Release.zip) from the [Releases](https://github.com/dach6/ExaltHud/releases) page to a writable folder.
4. Open `ExaltHUD.exe` and select the adapter carrying the game's traffic, usually Ethernet or Wi-Fi. For a VPN, select the appropriate adapter.
5. Click **Start listening**, then connect to the game. If already playing, reconnect or change servers so ExaltHUD sees a fresh connection.

Keep `assets/`, `ExaltHUD_Data/`, and `ExaltHUD.exe.config` beside the executable. If Npcap access is restricted to administrators, open ExaltHUD with **Run as administrator**.

## Dashboard and overlay

The dashboard remains available when the overlay is hidden or locked. Enable the overlay when you want the HUD above the game.

| Control | Action |
| --- | --- |
| Enable overlay / **F8** | Show or hide the overlay |
| Click through / **F9** | Lock or unlock the overlay |
| Overlay drag grips | Move the unlocked overlay |
| Overlay bottom edge | Resize the unlocked overlay vertically |
| Dashboard border | Resize the dashboard vertically |
| Scrollbars | Click the track, drag the thumb, or use the mouse wheel |
| Stop listening | Pause capture and keep the displayed results |

Use windowed or borderless game mode. Unlock the overlay to use its controls and scrollbars; locking it leaves the dashboard interactive. Closing the dashboard exits ExaltHUD.

The app saves the adapter, overlay position, and click-through setting in `ExaltHUD.settings.xml`. The overlay starts hidden each time.

## Capture and damage tracking

Start listening before connecting. If capture loses synchronization, follow the status area's reconnect instructions. Restart listening and reconnect after changing networks or adapters. ExaltHUD supports one game client at a time.

Capture is passive and requires no DLL in the game folder. Damage is reconstructed from combat packets and corrected when matching server reports arrive. Unconfirmed hits remain estimates; visibility, missing packets, unavailable buff definitions, and game updates can affect totals. Party and loot information is limited to data received by your client.

Crucible and Blood Ritual bonuses use definitions received from the game. If the HUD flags missing definitions, open the relevant event panels while listening.

Combat diagnostics are saved to `Diagnostics/dps-latest.csv`, with up to 30 traces retained in `Diagnostics/History/`. Each trace contains up to 20,000 recent combat events and excludes account details, player names, chat, and raw packets. Include the relevant trace, equipment, and active buffs when reporting a damage issue.

## Build from source

Use a current .NET SDK on Windows and .NET Framework 4.8 reference assemblies, available through the Developer Pack or an initial NuGet restore. The project builds with .NET SDK 10.0.401 and targets x64.

```powershell
dotnet build ExaltHUD.csproj -c Release
```

Run `bin/Release/net48/ExaltHUD.exe`. The build copies the required game data and assets. At runtime, ExaltHUD loads the installed 64-bit Npcap library from Windows.

## Tests

See [tests/README.md](tests/README.md) for packet, tracking, capture, metadata, and UI checks, including optional smoke tests for an installed Npcap driver.

## Source layout

| Folder | Contents |
| --- | --- |
| `Capture/` | Npcap binding, frame decoding, TCP reassembly, and packet framing |
| `Protocol/` | Packet readers and definitions |
| `Tracking/` | Game state, DPS, and Moonlight Village tracking |
| `Models/`, `Data/` | HUD snapshots, game definitions, and damage helpers |
| `UI/` | Dashboard, overlay, scrolling, and item rendering |
| `assets/`, `ExaltHUD_Data/` | Bundled sprites and game metadata |
| `tests/` | Capture, tracking, and UI tests with combat/dialogue fixtures |

The capture layer supports the game's TCP port 2050 protocol, TCP retransmission/reassembly, IPv4 fragments, Ethernet, loopback, raw IP, and basic IPv6 TCP. Fragmented IPv6 and unusual IPv6 extension chains are unsupported. Game updates may require changes to packet definitions and bundled data.

## References

- [RealmShark / Tomato](https://github.com/X-com/RealmShark): reference implementation for combat tracking and game packets.
- [RealmEye Wiki](https://www.realmeye.com/wiki/items): item and ability mechanics.
