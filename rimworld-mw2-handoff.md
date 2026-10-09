# RimWorld × MW2 Mashup — Handoff Brief

Goal: quick-and-dirty prototype. Get something working end to end, then iterate.
Prefer the simplest thing that runs over the "right" architecture. Upgrade later.

## The idea

RimWorld raids play out in first person in MW2. RimWorld stays the game (vanilla
rules, vanilla AI, vanilla items); MW2 is a 3D window into it plus the player's
first-person controls.

Both games run at the same time on one PC and talk over a localhost socket,
like the other bridge-style mashups in this trend (GTA × Minecraft, ArkWeb).

## Stack

| Side | Tech | Notes |
|---|---|---|
| RimWorld | Unity, C#, Mono. Logic in `Assembly-CSharp.dll` | Not obfuscated, decompiles to readable C#. RimWorld's EULA allows decompiling for personal use. Mod = our own C# DLL + Harmony patches. |
| MW2 | IW4L — open-source MW2 runtime in **Rust + Bevy + wgpu** (Apache-2.0) | Loads assets from the user's own MW2 install; doesn't inject into MW2. Started Sept 13, 2026 — very young. APIs, config and wire protocol change between commits. **Fork and pin a commit.** |
| Bridge | No third program. C# endpoint inside the RimWorld mod, Rust endpoint as a Bevy plugin inside IW4L | Localhost TCP. |

Setup steps for RimWorld source:
`ilspycmd <RimWorld>/RimWorld*_Data/Managed/Assembly-CSharp.dll -p -o ./rimworld-decompiled`
Keep the decompiled output **out of git** (reference only, never redistribute).

## Ownership (the core rule)

**RimWorld is the single source of truth.** When in doubt, RimWorld decides.

- RimWorld owns: map, pawns, raider AI, health/body parts, inventory, mortars, time, NPC shooting (timing, aim, hit/miss).
- IW4L owns: player camera + input, rendering, animation, sound, and **the player's own hit detection** (MW2 hitscan/spread/recoil).

Data flow:
- RimWorld → IW4L: per-tick snapshots (pawn id, position, facing, stance, equipped weapon, downed/dead) + events (shot fired, pawn downed, mortar launched/impact).
- IW4L → RimWorld: player movement/position, player hit reports (target pawn, body part, weapon), interactions (loot request).

## Protocol v0 (quick and dirty)

Newline-delimited JSON over one localhost TCP connection. IW4L listens, RimWorld connects
(or the reverse — pick whichever is easier to get running).
Upgrade to a shared schema (Protobuf/FlatBuffers/MessagePack) and UDP for snapshots only if
JSON becomes a bottleneck.

Example messages:
```json
{"t":"snapshot","tick":12345,"pawns":[{"id":"Human123","x":42.5,"z":17.0,"rot":90,"stance":"crouch","weapon":"Gun_AssaultRifle","downed":false,"faction":"raider"}]}
{"t":"shot","shooter":"Human123","weapon":"Gun_AssaultRifle","from":[42.5,1.4,17.0],"to":[50.0,1.2,20.0],"result":"hit","target":"Human7","part":"Torso"}
{"t":"player_move","pawn":"Human7","x":50.1,"z":20.3,"yaw":185}
{"t":"player_hit","target":"Human123","part":"Head","weapon":"Gun_AssaultRifle"}
```

## Coordinates and scale

- RimWorld: 2D grid, cells are `IntVec3 (x, z)`, y unused. Smooth positions via `DrawPos`.
- Start with **1 tile ≈ 1.5 m**. IW engine units are ~1 inch (verify in IW4L); Bevy is Y-up — check which convention IW4L uses internally.
- RimWorld has no real height. Add it in IW4L: muzzle at chest height (lower when crouched behind cover), target height from the body part hit.

## Mapping decisions

- **Terrain:** generate the IW4L map from the RimWorld grid. Rock/mountain → tall wall blocks, built walls → wall geometry, trees/sandbags → cover props, floor → ground. **Reuse chasm's MinecraftOSS world integration** (loaded as a map type like `minecraft:overworld`) — add a `rimworld:colony` map type the same way.
- **Pawns:** raiders = IW4L bot/character models driven by snapshots, interpolated between tiles. Model on chasm's Minecraft mobs (outside entities living in IW4L taking MW2 damage).
- **Player colonist:** Harmony-patch so its position comes from IW4L instead of RimWorld pathing. Constrain IW4L collision to the tile grid so the games agree on position.
- **NPC shooting (v0):** RimWorld decides everything. RimWorld already computes warmup/burst/cooldown, hit chance, and a destination (intended target or a "wild" miss spot). Send origin + destination + result; IW4L only plays muzzle flash, sound, tracer, impact, flinch.
- **NPC shooting (later):** RimWorld picks timing + aim direction, IW4L raycasts and reports hits (lets the player dodge). Only after v0 works.
- **Player shooting:** IW4L hitscan decides hits; RimWorld applies damage using its own matching weapon's stats on the reported body part.
- **Balance:** MW2 fire rates are way higher than RimWorld bursts. v0: scale per-bullet damage by (RimWorld DPS / MW2 DPS). One tunable multiplier.
- **Weapons:** lookup table, e.g. assault rifle → ACR, sniper rifle → Intervention, shotgun → SPAS-12.
- **Mortars:** RimWorld fires and resolves; IW4L draws the arc, whistle, and explosion.
- **Downed pawns:** prone or MW2 Last Stand animation. **Looting:** interact key opens an MW2-styled inventory (reuse chasm's MW2-styled inventory pattern) showing RimWorld gear; transfers go through RimWorld's own logic.

## Threading gotchas

- RimWorld game state is **not thread-safe**. Socket on a background thread → concurrent queue → drain and apply inside a `MapComponent`/`GameComponent` tick on the main thread.
- IW4L: socket on its own thread → crossbeam channel → Bevy systems read it as a resource.
- RimWorld runs 60 ticks/sec at 1x. Lock to 1x during raids for v0.

## Milestones

1. **M0 — Hello socket.** RimWorld mod logs "connected" and sends a tick counter; IW4L prints it.
2. **M1 — Ghosts.** RimWorld pawn positions show up as placeholder models in an existing MW2 map (no terrain conversion yet).
3. **M2 — One colonist, one raider.** Player controls a colonist from IW4L, position syncs back; raider shots render as tracers; player hits apply RimWorld damage. Flat map.
4. **M3 — Terrain.** Generate the IW4L map from the RimWorld grid (walls, rock, cover).
5. **M4 — Downed, loot, mortars.** Then polish.

## Things to verify (unconfirmed — check the actual code)

- RimWorld class/method names to hook: likely `Verb_Shoot` / `Verb_LaunchProjectile`, `Projectile.Launch`, `Pawn_PathFollower`, `DamageInfo` + `TakeDamage`, `MapComponent`. Confirm in the decompiled source.
- Where RimWorld resolves projectile hits (at launch vs. on impact).
- IW4L: how bots and weapon fire are spawned, how map types are registered, units and axis convention, whether multiplayer/bots are stable at the pinned commit.
- How chasm's repo wires MinecraftOSS mobs and the inventory UI into IW4L.

## Constraints

- No game assets in the repo. Users supply their own RimWorld and MW2.
- Offline only. Never connect modded clients to online services.
- Respect licenses: IW4L and chasm's repo are Apache-2.0 (keep NOTICE/attribution).

## Resources

**Trend overview**
- How the mashups actually work (bridge vs. rewrite vs. decomp): https://vgtimes.com/articles/169886-how-ai-game-mashups-work.html
- Discord hub: chasm's server — get the invite from his X (@chasmmmmmmmmmmm) or YouTube, not third-party links.

**MW2 side**
- IW4L (Rust/Bevy MW2 runtime): https://github.com/vladtrc/iw4L
- chasm's mashup (IW4L + Skate 3 + Minecraft). Read `AGENT.md` and `CONTEXT.md` for how he drives Claude Code on this codebase: https://github.com/chasmlol/2010-rust-rewrite-mashup

**Bridge examples**
- Universal Modder (Claude Code plugin; GTA × Minecraft example with agent field notes): https://github.com/rehan-remade/universal-modder
- GTA × Minecraft passthrough source: https://github.com/rehan-remade/universal-modder/tree/main/examples/minecraft-gta5-passthrough
- ArkWeb (Spider-Man in Arkham Knight, bridge + puppet trick): https://github.com/luki-1/ArkWeb
- Minecraft crossover bridge (open-source, Mac): https://github.com/justbustin/minecraft-crossover-bridge
- Prior art for strategy ↔ action linking: Crusader Blade (Crusader Kings 3 ↔ Bannerlord).

**RimWorld side**
- Decompiling guide (ILSpy, ilspycmd): https://rimworldwiki.com/wiki/Modding_Tutorials/Decompiling_source_code
- Harmony patching docs: https://github.com/pardeike/Harmony/wiki/Patching
- RimWorld mod tutorials + template: https://github.com/Zeta-of-the-rim/RimWorld-Mod-Tutorials
- RimSearcher — MCP server for searching RimWorld C# source and XML from Claude: https://mcpmarket.com/server/rimsearcher
- RimWorldForge — MCP/skill workbench for RimWorld 1.6 modding with Claude Code: https://github.com/SenjuWoo/RimWorldForge
- RimMolt — mod that runs a server inside RimWorld for an external agent (proof the in-game server pattern works): https://steamcommunity.com/sharedfiles/filedetails/?id=3796006886
- RimWorld Multiplayer mod (rwmt) — worth skimming for how it hooks the tick loop and syncs state.
