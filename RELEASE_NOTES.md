### 💡 Highlights
Read-only save browsing with mod-list import, Steam integration, the log reader, macOS support, .NET 10

### 🚀 What's Changed (since v2.4.4)

#### Save management: read-only save browsing with load-order-aware mod comparison (#43)
The new Saves dialog reads save headers without loading full saves and never writes, moves or deletes a save.
- 513055b Read save-file headers and details without loading full saves
- 4d55c64 Resolve the saves folder beside the config folder
- 58671a7 Add a read-only saves dialog
- 65674f0 Extract save details correctly and size the details window by marker
- 7838256 Extract per-colonist data and the real game calendar from saves
- c958f54 List only living colony members in the colonist scan
- aa54c0d Reopen the mods card and show the save's own list by default
- 81daaad Judge the save's load order, not just its mod set
- b6adb79 Import a save's mod list into the active list (with confirmation; missing mods offered for download)
- 1f04825 Reset the differences toggle when another save is selected
- Browse saves newest first, compare a save's mods against your active list in load order, and import that order on request
- Only living colonists and slaves are listed — guests, traders, prisoners, corpses and world pawns are excluded

#### Implement Steam integration and improve mod management features (#38)
- ac508c7 Implement Steam copy detection and workshop support
- 414b888 Implement Steam integration and settings refactor
- 06b1a20 Steamworks.NET integration complete (init retry, callbacks)
- 8640141 Add workshop ACF manifest parser
- 526471b Apply workshop ACF timestamps to scanned mods
- 080cf33 Add workshop folder override and validated Steam claims
- ea378ad Harden SteamWorkshopApiClient with retry, timeouts, and batch tests
- 4c2a43b Consolidate workshop update checking into one decision path
- 9c91ae8 Rename SteamCMD-downloaded and manually-placed mod types
- cfe7226 Add mods directory setting with smart auto-sync in settings dialog
- d220748 path plumbing + mods_folder override slot + PersistedKeys fix
- 3d8b5bc refactor: centralize GameInstance.LocalFolder writes behind IPathService
- 8264a50 fix: resolve nullable logger warning in DialogService (CS8604)
- de56a51 Add GameDistribution model
- 4754446 Remove GameDistribution from previous commit
- 2e14794 Linux Steam support: Proton prefix config, Flatpak/Snap roots, case-tolerant About.xml, SteamCMD depotcache auto-clear
- 76fd431 Steam integration: Linux protocol launch wiring, Steamworks init retry, plus test/warning cleanup
- 8ac7ac9 Honor the depot cache auto-clear setting in the SteamCMD downloader
- 79e525e Remove dead ModSource enum and DetermineModSource
- 0f329e7 feat(filter): tri-state tag filter (Any/Include/Exclude) with empty-state hint
- 061e613 feat(filter): tri-state version filter (Any/Include/Exclude) replacing checkboxes
- 7a4c124 fix(moddetails): reset description scroll to top when switching mods
- cd08cd6 Stabilize StatusMessage test against constructor's background init

#### Add the log reader: Player.log viewer, App Logs window, and their redesign (#39)
- f4dfe2e Add the Player.log viewer: reading, severity display, and its Utilities entry
- 59ad643 Add filters, live stats, and multi-line grouping to the Player.log viewer
- b467b45 Log Viewer: source switcher, open any log, export, repeat collapse
- 83b7d2f Add log troubleshooting and summary tabs to the Player.log viewer (known-issue signatures)
- ada0d48 Read shared HugsLib-style log exports in the Player.log viewer
- bc90605 Add an App Logs window for the logs RimSharp writes about itself
- e8b90ea Open multi-gigabyte logs in bounded memory with exact line numbers
- 258d3b3 Judge what the Player.log viewer opens: two families, two states
- 1cd9b7b Redesign the two log windows around a shared sheet layout
- 42fa814 Retune Fluent item-state brushes to the tan palette

#### Support macOS, harden multi-OS game identification and paths (#40)
- 3cf0606 Detect RimWorld installations on macOS
- 393817b Launch the game and locate Player.log on macOS
- b84057d Build and release macOS artifacts in CI
- e8fe18f Harden game detection: score config folders, scan all Proton prefixes, fall back to drive search
- e1f5ef8 Fix multi-OS support gaps in path resolution and game identity
- e03d6b9 Adapt CI-flagged behavior for macOS runners
- a8c4db3 Split Steamworks interop out of subscription services
- 70746a3 Keep Steamworks callback helper compiling against both packages
- 8ae87db Clear build and CI deprecation warnings
- 990c573 Make manifest cache-invalidation tests independent of write timing

#### Target .NET 10 in projects and CI (#41)
- 8bf9282 Target .NET 10 in projects and CI
- 8fd41c2 Assert Steamworks load errors only when init throws

##### Other changes
- 81e07b81 Fix mods misclassified as Core and tighten mod type detection
- 78461fb0 adjust workflow to ignore non-code changes and tag pushes
