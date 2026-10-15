# RimWorld FPS Mode: handoff

Paste this whole file into another AI tool to pick the project up. It says what the project is,
what the owner asked for, how the code is laid out, how to build and package it, what has been
tested and what has not, and what to do next.

- Repository: https://github.com/mlgg8x-alt/Rimworld-Fps-Mode
- Working branch (all current code): `ccr-6e26a519-k6f9ln`. The default branch still holds the
  original upstream project.
- Latest test build: `dist/` on that branch (Windows only). **Every build carries this handoff and the whole
  source inside the mod folder** (`RimWorldFPS/HANDOFF.md`, `RimWorldFPS/Source/`, with the commit
  in `Source/COMMIT`), so the project can be picked up from the installed mod alone.
- Android work (dropped) is on branch `android-client`, for reference only.

## What it is

A fork of [RimWorld x MW2](https://github.com/rkwitz/rimworld-mw2) (rkwitz), which let you play
RimWorld colonists in first person inside a patched Call of Duty MW2 engine. This fork replaces
the MW2 engine with its own first-person client, drawn with RimWorld's own art.

RimWorld stays the source of truth (map, pawns, health, AI, combat results). A RimWorld mod (C#,
Harmony) reports the game over a local TCP connection to the client, and the client sends back the
player's movement, shots, melee, use/loot actions. The client listens on 127.0.0.1:47820 and
RimWorld connects; one JSON object per line ("NDJSON"), tagged by `"t"`.

## What the owner asked for (in order, newest decisions win)

1. Fork the MW2 mod, but drop the MW2 graphics. (A "boomer shooter" look was tried, then rejected.)
2. Modern FPS gameplay with **RimWorld-style graphics**.
3. Built-in support for **any modded weapon**: turn the weapon's 2D RimWorld texture into the held
   weapon automatically. 3D extrusion was tried and rejected ("3d assets require too much user side
   work"); the current version draws weapons as **flat 2D pictures on screen**, placed
   automatically, with **2D circle hands**.
4. **Integrated melee** with every RimWorld melee weapon (not just a knife).
5. A **minimal HUD** of **floating widgets with an edit mode** (no status bar). The edit mode must
   also let you graphically move/rotate/scale the weapon and drag the hands and muzzle.
6. A **minimap showing the normal RimWorld view** around you.
7. Health display: **use RimWorld's own Health tab** (and stay compatible with mods that change it,
   e.g. Nice Health Tab), not a custom pawn preview.
8. **No manual setup**: the mod must start the client **automatically, in the background**, when
   RimWorld launches; a **status indicator** (green/red) and a **force start** button.
9. Use the game's and mods' own assets rather than generating new ones; no cache files.
10. Work in **both the Windows and Linux** versions of RimWorld.
11. Keep RimWorld's in-game UI visible in FPS mode for now (debugging).
12. An **"FPS mode" toggle gizmo** on colonists to switch in and out.

## Layout

| Path | What |
| --- | --- |
| `rimworld-mod/About/About.xml` | Mod metadata (packageId `rimworldfps.mode`, RimWorld 1.6, depends on Harmony `brrainz.harmony`). |
| `rimworld-mod/1.6/Assemblies/` | Build output of the mod DLL (not committed). |
| `rimworld-mod/Source/` | The mod's C# (`RimWorldFPS.csproj`, net472, references `Krafs.Rimworld.Ref` 1.6.4871 and `Lib.Harmony` 2.4.2 with runtime excluded). |
| `client/` | The client: `RimFpsClient.csproj`, .NET 10, Raylib-cs 8.1.0 (raylib 6), C#. |
| `protocol/PROTOCOL.md`, `protocol/examples.ndjson` | The wire protocol and one example line per message. |
| `bridge/crates/protocol` | Rust types for every message; `cargo test` round-trips every example line. |
| `TESTING.md` | Player install steps, controls, test checklist. |
| `dist/` | The latest packaged test build. |

### Mod (rimworld-mod/Source), key files

- `BridgeMod.cs`: entry point. Starts the client launcher first, then applies each Harmony patch
  class on its own (one failing patch only costs itself). Mod settings page: client status, "Force
  start the FPS client", "Open the client's log", address/port, auto-start, auto-connect, hide UI.
- `ClientLauncher.cs`: finds `Client/win-x64/RimFpsClient.exe` or `Client/linux-x64/RimFpsClient`
  in the mod folder, starts it with `--background --parent <RimWorld pid> --address host:port`
  (hidden, no console), restarts it up to 5 times, kills it when RimWorld quits. "Is it running" is
  decided by trying to bind the port (not by listing processes, which can throw under RimWorld's
  Mono). `ForceStart()` kills any running/left-over client and starts a fresh one. `chmod +x` on
  Linux. Problems go to `Log.Error` and the indicator tooltip.
- `StatusDot.cs`: the status indicator in the top right corner (menus and game): dot + word
  (online/ready green, starting yellow, offline red), tooltip with details and log path, and a
  Start/Restart (force start) button. Also drives `HealthCard.Update()` each GUI frame.
- `BridgeGameComponent.cs`: owns the TCP client (`BridgeClient.cs`, reconnects on its own), sends
  hello, polls all reports each frame, handles incoming player actions. Main thread only.
- Reports (RimWorld → client): `TerrainReport`, `WorldReport` (buildings, items, roofs, textures),
  `PawnSnapshot` (pawns, the played pawn's health/weapon), `LookReport`, `PawnArt`, `FloraReport`,
  `DoorReport`, `EffectsReport`, `FleckReport`, `ShotReport`, `ExplosionReport`, `WeatherReport`,
  `LootReport`, `StatusReport` (health numbers), `HealthCard` (Health tab picture), `HeldReport`.
- Player actions: `PlayerPawn` (control/release, movement), `PlayerShots` (real RimWorld damage via
  the bullet def), `PlayerMelee` (`meleeVerbs.TryMeleeAttack`), `Throws`, `Activate` (doors,
  switches, carry), loot take/drop.
- `TextureExport.cs`: reads any material's texture through the GPU (RimWorld textures are not
  readable), tints it as RimWorld does, encodes PNG **in memory**, and sends it as `texture` lines.
  No files are written.
- `ControlGizmo.cs`: the **"FPS mode"** toggle on every humanlike player pawn (drafts them and hands
  them to the client; off hands them back).
- `RimView.cs`: Windows only. Shrinks RimWorld's own window into the client's minimap corner
  (round via `SetWindowRgn`), follows the player, brings windows to front on Tab. Skipped on Linux.
- `HealthCard.cs`: draws the played colonist's Health tab (their own inspector tab instance when
  they are selected, so mods that patch/replace it show; else `HealthCardUtility.DrawPawnHealthCard`)
  into a RenderTexture in tiles no larger than the window, reads it back (checks orientation with a
  marker pixel), sends a PNG about once a second when it changes. **Untested in real RimWorld.**

### Client (client/src), key files

- `Program.cs`: flags (`--demo`, `--address`, `--background`, `--parent`, screenshot/test flags).
  `--background` logs to `<AppData or ~/.config>/RimWorldFPS/client.log`.
- `App.cs`: main loop; background mode keeps the window hidden until a colonist is played and quits
  when the parent process dies.
- `Net/Bridge.cs` (TCP listen, retries bind), `Net/Link.cs` (applies messages), `Net/Proto.cs`.
- `World/*`: grid, atlas, pictures (from `texture` lines in memory, or files in demo), world mesh.
- `Render/*`: shaders, renderer (RimWorld style; optional "retro" style), sprites.
- `Game/Hands.cs` + `Game/WeaponModel.cs`: **2D weapons**. The weapon's RimWorld picture is drawn
  flat on screen with a rotation/scale/mirror, a dark offset copy for an edge, round hands. Guns
  are assumed to point right in their picture (turned by the def's equipped angle offset); melee
  weapons' direction comes from the picture's long axis (PCA). Grip/fore/muzzle found from the
  silhouette. Poses (hip/aim screen position and angle, size, edge, hand size, hand count, mirror,
  hand/muzzle points) per weapon picture or for all guns/melee in `Settings.Weapons`.
- `UI/Hud.cs`: floating widgets (minimap, health, weapon, compass, messages, prompt) with edit mode
  (F2). Health widget shows the `health_card` picture, else a fallback laid out like the tab.
- `UI/WeaponEditor.cs`: weapon part of edit mode (drag move, right-drag turn, wheel size, drag
  hands and muzzle, slider panel, hip/aim tabs, use-for-all, reset).
- `Demo/*`: a fake RimWorld speaking the real protocol, for `--demo` and screenshots.
- Settings: `client.json` in `%AppData%/RimWorldFPS` (Windows) or `~/.config/RimWorldFPS` (Linux).

## Build and package

Requirements: .NET SDK 10, Rust (for `bridge/`). Nothing needs a RimWorld install to compile (the
mod uses the `Krafs.Rimworld.Ref` NuGet reference assemblies).

```
dotnet build rimworld-mod/Source/RimWorldFPS.csproj -c Release      # -> rimworld-mod/1.6/Assemblies/RimWorldFPS.dll
dotnet run --project client -- --demo                               # client against a fake colony
cd bridge && cargo test                                             # protocol round-trip tests
```

Client release builds (both are needed in the package):

```
dotnet publish client/RimFpsClient.csproj -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=true -p:TrimMode=partial -p:JsonSerializerIsReflectionEnabledByDefault=true -p:EnableCompressionInSingleFile=true -o out/win-x64
dotnet publish client/RimFpsClient.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=true -p:TrimMode=partial -p:JsonSerializerIsReflectionEnabledByDefault=true -p:EnableCompressionInSingleFile=true -o out/linux-x64
```

`scripts/package.sh [name]` does all of it (build, publish both clients, assemble, zip into
`dist/<name>.zip`, including this handoff and the source). Package layout (players unzip into
`RimWorld/Mods`):

```
RimWorldFPS/
  About/About.xml
  1.6/Assemblies/RimWorldFPS.dll
  Client/win-x64/RimFpsClient.exe
  Client/linux-x64/RimFpsClient      (chmod +x)
  HANDOFF.md, TESTING.md, README.md  (RimWorld ignores them)
  Source/                            (the repository; RimWorld ignores it)
```

Keep the zip under 30 MB if it is sent as a chat attachment (v5 is about 15 MB).

## Status

Tested (headless Linux, demo mode and scripted runs):
- Both projects compile; `cargo test` passes with every example line.
- Client renders the world, 2D weapons, HUD widgets and edit mode against the fake colony.
- Linux client started exactly as the mod starts it (`--background --parent`): stays hidden,
  listens, answers a connection with its hello, and exits when the parent process dies.
- Trimmed build loads saved settings (weapon poses) correctly.

**Not tested in real RimWorld at all** (the owner has a RimWorld 1.6 install; no tool session
has had one):
- The mod loading, patches applying, auto-start, status indicator, force start, FPS mode gizmo.
- Everything that needs the real game: textures over the bridge, shooting/melee damage, loot,
  doors, minimap overlay (Windows), health tab capture (`HealthCard.cs`, riskiest piece:
  IMGUI drawn into a RenderTexture).
- The Windows client binary (only built, never run).

The owner's last report (before v4) was: the client did not start automatically or connect.
v4 fixed the likely causes (no Linux client shipped; launcher ran after Harmony patching, so one
failing patch stopped it; process listing can throw under RimWorld's Mono). v5 added the status
indicator and force start. Ask the owner for: the indicator's hover text,
`Player.log` lines starting `[RimWorldFPS]`, and `client.log`.

## Note on another tool's attempt

**ChatGPT was stumped** on this: it could not compile anything and left the owner with zips of an
uncompiled "integration kit" (`FpsClientControls.cs`, a settings-page Force Start/Restart button and
status label) plus repackaged copies of the v4 build. Those are superseded: v5 has the same
features compiled in (`ClientLauncher.ForceStart`, `StatusDot`, settings page). Do not merge
`FpsClientControls.cs`; it starts the client without `--background`/`--parent`/`--address` and
would run a second, visible client.

## RimWorld versions

The mod builds for **1.6 and 1.7** from the same source (`-p:RimWorldVersion=1.6|1.7`; the
package carries `1.6/Assemblies` and `1.7/Assemblies`, About.xml lists both). RimWorld 1.7's
reference assemblies (Krafs.Rimworld.Ref 1.7.4979-alpha) target **.NET 10**, so the 1.7 build
targets net10.0; it compiles with no errors but is **untested in 1.7** (it is an alpha; watch
for runtime differences from Mono, and a Harmony build for 1.7).

**First 1.7 test (1.7.4979 rev109):** 1.7 runs on a new engine, "Waylith", that stands in for
Unity, with some Unity calls as stubs that do nothing. The log flags `Graphics.Blit` as one.
`TextureExport` used Blit to read RimWorld's textures, and `HealthCard` and the minimap window
trick (`RimView`) use RenderTexture/ReadPixels and Unity's window, which are likely stubs too.

The client connected, then stopped right away, and RimWorld restarted it 5 times, so "FPS mode"
stayed disabled. v10 changes:
- The client skips any line it cannot use (`Link.Apply` catches everything and logs it).
- Crashes go to `client.log`, and the previous run's log is kept as `client.previous.log`.
- The parent-process check can no longer throw.
- `TextureExport` reads readable textures straight from memory and refuses empty PNGs.

Still to do on 1.7: read the client logs from a 1.7 run to confirm the cause. If textures come
out blank there, read them another way: the mods' PNG files on disk, or Waylith's own API.

## Platforms

**Windows only** (the owner's decision). The package ships only `Client/win-x64`. Linux and Android
were tried and dropped: an Android app build (NativeAOT + raylib for Android, touch controls)
remains on branch `android-client` for reference only.

## Performance notes

- While no colonist is played the client is hidden and draws nothing: it polls window events
  and sleeps (about 2% of a core here, from about 49% with empty frames, and full 3D frames at up
  to 165 fps before). Its clock is real time, so it still notices RimWorld closing.
- The client is published ReadyToRun (precompiled), so play starts without JIT stutter.
- The mod redraws the Health tab picture (a GPU read-back) only when the colonist's health report
  changed, or every 8 seconds, instead of every second.

## Agreed next architecture (owner's decisions, latest)

Goal: the FPS view is drawn **inside RimWorld's own window** (no second window, no tab
switching), and a **separate headless backend process** takes the heavy work off RimWorld (RAM,
CPU), so RimWorld stays fast and loads stay short. Windows only. Hardware: ASUS ROG Flow Z13
(integrated AMD GPU with unified memory: "VRAM" is carved out of system RAM; recommend 8-12 GB, not
32, since RimWorld and mods need the RAM; 16 CPU cores, RimWorld uses about one).

- **Backend** (`backend/`, C#, headless, local): map processing into 3D chunk meshes, map caches,
  and later collision/movement, bullets, line of sight, weapon analysis, GPU compute (DirectX
  compute shaders; no CUDA on AMD) for whole-map lighting/visibility/fog if worth it. Low priority
  on idle cores. Starts with RimWorld (mod launches it), exits with it (`--parent`). A crash must
  not affect RimWorld (mod falls back to the normal view).
- **Mod**: draws with Unity in RimWorld (mesh chunks uploaded once to the GPU, then the CPU copy
  dropped: `Mesh.UploadMeshData(true)`), uses RimWorld's already-loaded textures directly (the
  PNG export goes away), instanced drawing for repeated things, culling. The Health tab is drawn
  natively (the capture trick goes away). The minimap becomes RimWorld's own camera drawn into a
  corner (the window juggling goes away). Mouse locked in FPS mode, returned to the UI on exit.
  Keep per-frame work and allocations tiny (RimWorld's old Mono GC stutters on garbage).
- **Transport**: small binary frames on TCP 127.0.0.1:47821 for control; big data (cells, meshes)
  in a shared memory arena (`Local\RimWorldFPS.Arena`) read in place. No JSON.
- **Instant FPS mode**: the backend builds the map in the background while the player plays
  normally; if FPS mode is entered early, chunks around the colonist come first (progressive
  streaming, brief edge pop-in, no loading screen). Only changed chunks rebuild.
- **Caching**: the **home map is always cached** on disk (instant reload); caravan/quest maps
  keep only the last few and get a **short boot-up time** when new (owner accepted).
- **Deferred weapon assets**: nothing weapon-related during RimWorld's load (mods' defs, patches
  and textures are not final until it finishes). After a save loads, a low-priority queue
  analyzes weapons a few per frame: first the played colonist's weapon, then weapons on the map,
  then the rest; results cached by texture and mod version; a default pose until ready.
- Optional later: baked lighting, higher-detail geometry, distant LOD; DirectX shared GPU
  resources (needs a small native DLL) only if shared memory is not fast enough.

### Status of that plan

Done (`backend/`, builds; `dotnet run -- --selftest` passes):
- `Protocol.cs`: binary frames and message types (Hello, MapBegin, MapCells, CellsChanged,
  ChunkReady, MapReady, Ping), the 8-byte `Cell`.
- `Arena.cs`: named shared memory with a bump allocator (file-backed off Windows, for tests).
- `Mesher.cs`: 16x16-cell chunk meshes on all cores (floors, wall faces only where they meet open
  ground, ceilings under roofs), and which chunks a changed cell touches.
- `MapCache.cs`: per save/map/mod-list cache on disk; home map always kept, others last 3.
- `Program.cs`: the server loop (MapBegin -> cache hit sends chunks at once; MapCells -> cache,
  build, send), `--parent`, `--selftest`. Self-test: 250x250 map, 256 chunks, 460k vertices built
  in ~90 ms, cache save/load ~1.5 ms each, ~24 ms into shared memory.

To do, in order:
1. Mod: pack cells (from `Map.edificeGrid`, terrain, roofs) into the arena, send MapBegin/MapCells,
   CellsChanged on map changes; launch the backend like the client (ClientLauncher).
2. Mod: in-game view: a Unity camera at the colonist's eyes, chunk meshes from the arena drawn with
   RimWorld's materials (texture table: index -> material), pawns/things as upright billboards
   (their own materials/PortraitsCache), the 2D weapon and hands on the IMGUI layer, HUD widgets,
   native Health tab, minimap from RimWorld's camera. Reuse the mod's existing actions
   (PlayerPawn.Move, PlayerShots.Fired, PlayerMelee.Strike, Activate.Use, Throws.Throw).
3. Move movement/collision/ballistics from `client/src/Game` into the backend.
4. Deferred weapon analysis queue (port `client/src/Game/WeaponModel.cs`'s grip/axis logic).
5. Progressive streaming order (chunks nearest the colonist first), background prebuild.
6. Retire the separate client window once the in-game view covers it.

## Next steps (suggested order)

1. Get the owner's logs from a real run of v5 and fix what they show (auto-start, connection).
2. Verify `HealthCard` capture in real RimWorld; if it fails, it logs one warning and the client
   falls back to the text layout of the tab.
3. Real-game pass over the TESTING.md checklist.
4. Replace the client's procedural effect art (fire, smoke, muzzle flash, blood, sparks) with
   RimWorld's own textures sent by the mod (from the fleck/filth/fire defs' graphics).
5. Decide the Android route if the owner plays on RimDroid.
6. Open a PR from `ccr-6e26a519-k6f9ln` to the default branch when the owner wants it.

## Conventions (from CLAUDE.md)

- RimWorld is the source of truth; the client never decides outcomes.
- Never commit game files or decompiled RimWorld code.
- RimWorld state only on the main thread (`BridgeGameComponent`), never the socket thread.
- Protocol changes update together: `bridge/crates/protocol`, `protocol/PROTOCOL.md`,
  `protocol/examples.ndjson`, and the C# sides; `cargo test` must pass.
- Offline only: never connect to online services.
