# Code map — where to edit what

Task routing for agents and humans. Architecture lives in `PLAN.md`; wire format in
`protocol/PROTOCOL.md`. Step-by-step workflows in `docs/AGENT-RECIPES.md`.

## Entry points

| Component | Boot / wiring |
|-----------|----------------|
| BepInEx plugin | `src/WinterMP.Core/WinterMPPlugin.cs` — creates persistent `WinterMP` GameObject and subsystems |
| FastBoot (save gate) | `src/WinterMP.FastBoot/SessionGate.cs` |
| Launcher | `src/WinterMP.Launcher/MainWindow.xaml.cs` |
| Protocol (engine-independent) | `src/WinterMP.Net/` — **unit-tested, builds without the game** |

## Session & transport

| Concern | Files |
|---------|--------|
| Host/join, handshake, chat, relay | `Session/SessionManager.cs` |
| Remote player bookkeeping | `Session/RemotePlayer.cs`, `Session/DevLoopbackClient.cs` |
| Launch modes / CLI | `LaunchOptions.cs` |
| Guest sidecar (pose + needs) | `Session/GuestProfileStore.cs` |
| Host main-menu gate | `Session/HostLaunchPolicy.cs`, `UI/MainMenuHostGate.cs` |
| Steam lobby / P2P | `Steam/SteamBootstrap.cs`, `SteamLobbyManager.cs`, `SteamP2PTransport.cs` |
| Loopback / UDP dev transport | `Session/SessionManager.cs` (`StartDevLoopback`, `StartHostLocal`) |

## Player presence (M2)

| Concern | Files |
|---------|--------|
| Pose stream in/out | `Sync/PlayerSyncManager.cs` |
| Remote bodies | `Sync/RemoteAvatar.cs`, `Sync/NpcCharacterFactory.cs`, `Sync/RemoteCharacterAnimator.cs` |
| Move-state bitfield | `Sync/PlayerMoveStateReader.cs`, `Sync/PlayerMoveState.cs` |
| Guest spawn picker | `UI/GuestSpawnPrompt.cs`, `Sync/GuestSpawnRelocator.cs` |
| Guest needs → host sidecar | `Sync/PlayerNeedsSync.cs` |
| Vehicle seats | `Sync/PassengerController.cs` |

## World sync (M3–M5)

| Concern | Files |
|---------|--------|
| Orchestrator | `Sync/WorldSyncManager.cs` (+ partials under same prefix) |
| Generic FSM sync engine | `Sync/FsmWorldSync.cs`, `.Registry.cs`, `.Local.cs`, `.Remote.cs`, `.Snapshots.cs` |
| FSM hook helpers | `Sync/FsmHook.cs` |
| Curated rules loader | `Catalog/SyncCatalog.cs`, `catalog/sync-catalog.json` |
| Doors, shops, bolts, parts | Registered via `FsmWorldSync.Registry.cs` from catalog |
| Items / pickables | `Sync/ItemWorldSync.*` |
| Jerrycan / liquid contents | `Sync/FluidContainerSync.cs` |
| Vehicles | `Sync/VehicleWorldSync.*`, `Sync/ItemWorldSync.Vehicle.cs` |
| Classifieds / factory / Marketti progress | `Sync/WorldProgressSync.cs` |
| Sewage / firewood delivery sites | `Sync/JobSiteSync.cs` |
| Time / weather | `Sync/TimeWeatherSync.cs` |
| Shared wallet | `Sync/WalletSync.cs` |
| Scene paths / net IDs | `Sync/ScenePath.cs` |

## Long-tail player systems (M6)

| Concern | Files |
|---------|--------|
| Sleep consent | `Sync/PlayerSleepHook.cs`, `Sync/SleepConsentManager.cs`, `UI/SleepConsentPrompt.cs` — FSM paths in `catalog/README.md` |
| Death / respawn | `Sync/PlayerDeathHook.cs`, `Sync/DeathSyncManager.cs` |
| Permadeath settings | `Sync/PermadeathSettings.cs` |

## Protocol layer (change here first when possible)

| Concern | Files |
|---------|--------|
| Version | `WinterMP.Net/Protocol.cs` |
| Message IDs | `WinterMP.Net/Messages/IMessage.cs` |
| Message types | `WinterMP.Net/Messages/*.cs` |
| Registry | `WinterMP.Net/Messages/MessageRegistry.cs` |
| Spec (must match code) | `protocol/PROTOCOL.md` |
| Tests | `WinterMP.Net.Tests/` — especially `WorldMessagesTests.cs`, `PacketCodecTests.cs` |

## UI & dev tooling

| Concern | Files |
|---------|--------|
| In-game debug overlay (TAB) | `UI/DebugOverlay.cs` |
| Main menu join browser | `UI/MainMenuJoinBrowser.cs`, `UI/MainMenuUiFactory.cs`, `UI/MainMenuUiButton.cs` |
| F9 catalog dump | `WinterMP.Tools/FsmDumperPlugin.cs` → `<game>/WinterMP/dumps/` |
| Catalog curation scripts | `tools/extract_fsm_details.py`, `tools/analyze_catalog.py`, `tools/catalog_diff.py` |

## Partial-class convention

Large types split by concern, e.g. `FsmWorldSync.cs` + `FsmWorldSync.Remote.cs`.
When a file approaches ~1000 lines, add a new partial — don't grow a monolith.
