# RimWorld FPS Mode: install and test

## Install

1. Install **Harmony** (Steam Workshop, or github.com/pardeike/HarmonyRimWorld).
2. Copy the `RimWorldFPS` folder into `RimWorld/Mods/`, enable it in the mod list
   **below Harmony**, and restart RimWorld. Disable the original "RimWorld MW2" mod if
   you have it.
3. Run `RimFpsClient.exe` (any folder). Leave it open; RimWorld connects to it by itself
   on 127.0.0.1:47820 (either can start first). `RimFpsClient.exe --demo` runs it without
   RimWorld, against a made-up colony.

## Controls

WASD move, mouse look, Shift sprint, Space jump, Ctrl/C crouch, Alt walk.
Left click fire / swing, right click aim, R reload, V melee bash, G grenade,
E use (doors, switches), F loot, M map, Tab back to RimWorld, Esc menu,
F2 edit HUD, F3 FPS counter.

## What to test (tell me what breaks, with `Player.log` lines starting `[RimWorldFPS]`)

1. **Connection**: load a save. The client window should say it's connected. RimWorld's log shows `[RimWorldFPS]` hello.
2. **Map**: walls, floors, doors, trees, furniture, lamps all show with your real textures. Note anything missing or invisible, and anything from mods.
3. **Taking control**: draft a colonist, use the **Play in first person** gizmo. The client comes to the front as that colonist.
4. **Movement**: walk around; the colonist moves in RimWorld too. Walls and closed doors block you; E opens doors.
5. **Guns**: try a vanilla rifle, pistol and **at least one modded gun**. Check the 3D held model looks right (not huge, not mirrored, hands on the grip), aiming, recoil, reload, ammo/burst count.
6. **Damage**: shoot a raider; they get real RimWorld damage on real body parts (check their health tab).
7. **Melee**: equip a knife, longsword, club, and a modded melee weapon. Left click swings (only hits when adjacent). V bashes with a gun.
8. **Grenades**: G throws with frag grenades equipped.
9. **Getting shot**: enemies' bullets hit you, and you can dodge them by moving.
10. **Pawn status widget**: the colonist preview shows health, pain, mood, and injured body parts change colour when hit; conditions list updates (bleeding etc).
11. **Minimap**: the round RimWorld view in the corner follows you and matches the real map. Try Edit HUD (F2): move/resize the minimap and other widgets, toggle them, check they stay put after restart.
12. **Loot (F)**: take a weapon off a corpse, drop one, wear apparel.
13. **Tab**: back to RimWorld and return with the gizmo. Pausing RimWorld freezes you.
14. **Night**: sky and lighting get dark; lamps light rooms.
15. **Options (Esc)**: switch Style to Retro and back, change FOV and mouse speed.
16. **Performance**: FPS on a big colony (F3).
