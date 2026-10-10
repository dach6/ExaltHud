# Tests

Build `ExaltHUD.csproj -c Release` first. Run each script in a separate Windows PowerShell process. All scripts accept `-AssemblyPath` for a different application build; scrolling tests require `-STA`.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-AbilityPackets.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-DpsTracking.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-DpsAccounting.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-DpsModifiers.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-CombatStats.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-CombatDiagnostics.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-LethalStrikeReplay.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-PackagedAssets.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-MvDetection.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tests/Test-OverlayScrolling.ps1
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File tests/Test-OverlayScrolling.ps1 -Embedded

dotnet build tests/ExaltHUD.Tests.csproj -c Release
./tests/bin/Release/net48/ExaltHUD.Tests.exe
```

The C# project accepts `-p:ApplicationAssemblyPath=C:\path\to\ExaltHUD.exe`. Its tests use synthetic Ethernet/IP/TCP frames, RC4 test vectors, encrypted game packets, and bundled XML to cover stream reconstruction, cipher state, connection recovery, passive tracking, encounter history, and independent dashboard/overlay controls.

| PowerShell suite | Coverage |
| --- | --- |
| Ability packets | Packet layouts, optional fields, shot counts, and trailing data |
| DPS tracking/accounting | Attribution, scaling, server corrections, duplicate suppression, pellets, summons, and RNG seeds |
| DPS modifiers | Enchantments, party/event bonuses, late monster metadata, completed fight timing, and background trace saving |
| Combat stats | Numeric stat packets, temporary vitality boosts, skin independence, stat displays, and server reconciliation |
| Combat diagnostics | Combat snapshots, boost payloads, trace retention, and bounded output |
| Lethal Strike replay | Darkened Sun combat fixture, target defense, vitality snapshots, duplicate reports, and confirmations |
| Packaged assets | Monster names, Fractal projectiles, forge data, equipment icons, and concurrent initialization |
| Moonlight Village | Encounter detection and the bundled NPC dialogue fixture |
| Overlay scrolling | Mouse wheel, track clicks, dragging, resizing, filtering, and lock mode across six lists in each view |

Run the packaged-assets suite against a clean extracted release. Use `-ScreenshotPath <png>` for an equipment image or `-NamesOnly` with an executable copied without assets to check embedded names. The scrolling suite accepts `-ScreenshotDirectory <path>` for rendered examples.

Fixtures contain numeric combat inputs or NPC dialogue and session markers, excluding personal chat, account details, and raw packet captures. The Lethal Strike replay includes an independent Tomato formula comparison; the reference totals do not establish server-confirmed damage for every hit.

## Optional Npcap smoke tests

These checks require Npcap and a usable Windows desktop:

```powershell
./tests/bin/Release/net48/ExaltHUD.Tests.exe --npcap
./tests/bin/Release/net48/ExaltHUD.Tests.exe --npcap --ui-smoke
```

They briefly open the selected/default adapter, check start/stop/restart, and exercise dashboard controls. They do not launch the game or save captured traffic. Add an image filename after `--ui-smoke` to save a dashboard picture.

Offline tests cover defined packet and UI behavior. Live verification exercises fresh game connections, map transitions, abilities, party/loot updates, and overlay behavior in windowed or borderless mode.
