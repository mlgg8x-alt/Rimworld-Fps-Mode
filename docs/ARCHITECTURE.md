# Architecture

RimWorld raids play out in first person in MW2. RimWorld stays the game (vanilla rules, AI and
items). IW4L, an open-source MW2 runtime in Rust/Bevy, is a 3D window onto it plus the player's
first-person controls. Both run at once on one PC and talk over localhost TCP.

```
RimWorld (C#, Mono)                        IW4L engine (Rust, Bevy)
  rimworld-mod/                              engine/ + engine-patches/
  ├ BridgeGameComponent  main thread          ├ bridge plugin (planned)  Bevy systems
  │   drains inbound, sends state             │   read a channel resource
  └ BridgeClient  background thread  ──TCP──  └ listener thread
        newline JSON, protocol/PROTOCOL.md
```

## Who owns what

RimWorld owns: map, pawns, raider AI, health and body parts, inventory, mortars, time, and every
NPC shot (timing, aim, hit or miss).

IW4L owns: the player's camera and input, rendering, animation, sound, and the player's own hit
detection (MW2 hitscan, spread, recoil). RimWorld applies the damage from those hits.

## Threading

RimWorld game state is not thread-safe. The socket runs on a background thread and hands lines
over through concurrent queues; `BridgeGameComponent` applies them during the game tick.
Even `Log.Message` is only called from the main thread.

On the IW4L side the same pattern applies: socket thread, channel, Bevy systems drain it.

## Engine base

We build on chasm's mashup rather than bare IW4L because it already has a block world
(MinecraftOSS, registered as the `minecraft:overworld` zone in `crates/assets/src/minecraft_map.rs`),
outside entities taking MW2 damage, and an MW2-styled inventory. A RimWorld map is a grid of
walls, rock and floor, which converts naturally to block columns.

## Folder layout

```
<workspace>/                  general modding folder shared by several projects; Ryan's is game_modding/
├ reference/                  generated, never shared; reused by every project
│ ├ repos/                    pinned reference repos (read only)
│ └ rimworld/                 one folder per game
│   ├ decompiled/             from your own RimWorld install
│   └ defs/                   Defs XML from your own install
└ rimworld-mw2/               this project
  ├ rimworld-mod/             RimWorld mod (About/, Source/, 1.6/Assemblies/)
  ├ bridge/                   Rust: protocol crate + bridge-echo test server
  ├ protocol/                 wire spec and example messages
  ├ engine-patches/           our changes to the engine, as patches
  ├ engine/                   pinned engine clone + patches (generated, gitignored)
  ├ scripts/setup.py          workspace setup
  └ versions.json             every pinned version
```

`engine/` lives inside the project, not in `reference/repos`, because it is a working copy:
this project patches it, builds it and runs it. Reference repos stay pristine and read only.
