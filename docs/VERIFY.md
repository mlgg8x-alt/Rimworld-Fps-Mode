# Open questions

Facts to confirm in the code before relying on them. Move items to "Confirmed" with the file
and line that proves them.

## Confirmed
- These RimWorld 1.6 types exist (checked against Krafs.Rimworld.Ref 1.6.4871):
  `Verse.Verb_Shoot`, `Verse.Verb_LaunchProjectile`, `Verse.Projectile` (with `Launch(...)`,
  `ImpactSomething()`, `Impact(Thing, bool)`), `Verse.AI.Pawn_PathFollower`, `Verse.DamageInfo`,
  `Verse.MapComponent`, `Verse.GameComponent`, `Verse.ShotReport`.
- The engine registers the Minecraft world as zone `minecraft:overworld`
  (`crates/assets/src/minecraft_map.rs`).
- Engine bots accept `bot tp`, `bot give`, `bot fire`, `bot hold` (`docs/BOTS.md` in the engine).

## To check
- Where RimWorld decides hit or miss: at launch (`Verb_LaunchProjectile.TryCastShot`) or on
  impact (`Projectile.ImpactSomething`). Needs the full decompile from your own install.
- Which jobs move a drafted colonist besides `Pawn_PathFollower`, so player control is not
  overridden.
- IW4L world units (believed ~1 inch) and axis convention (Bevy is Y-up).
- Where a Bevy plugin is registered in the engine (`crates/bootstrap/src/plugins.rs` looks likely).
- Whether bots and multiplayer are stable at the pinned engine commit.
