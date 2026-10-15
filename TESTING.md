# RimWorld FPS Mode: install and test

## Install

1. Install **Harmony** (https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).
2. Unzip the build into `RimWorld/Mods` so you have `Mods/RimWorldFPS/About/About.xml`, enable
   it **below Harmony**, and restart RimWorld. Disable the original "RimWorld MW2" mod if you
   have it.
3. Nothing else: the client (Windows and Linux) comes with the mod and starts by itself. The
   indicator in the top right corner shows it (green online, yellow starting, red offline);
   its button force starts it. Hover it for details and the client log's path.

## Controls

WASD move, mouse look, Shift sprint, Space jump, Ctrl/C crouch, Alt walk.
Left click fire / swing, right click aim, R reload, V melee bash, G grenade,
E use (doors, switches), F loot, M map, Tab back to RimWorld, Esc menu,
F2 edit HUD, F3 FPS counter.

## What to test (tell me what breaks, with `Player.log` lines starting `[RimWorldFPS]`)

1. **Connection**: the indicator is green ("FPS ready") on the main menu and green ("FPS online") once a save is loaded. If not, hover it, press Start/Restart, and send the hover text plus the client log.
2. **Map**: walls, floors, doors, trees, furniture, lamps all show with your real textures. Note anything missing or invisible, and anything from mods.
3. **FPS mode gizmo**: select any colonist; the **FPS mode** toggle drafts them and the client comes to the front playing them. Turning it off hands them back. RimWorld's own UI stays up in FPS mode for debugging (Mod settings > RimWorld FPS Mode to hide it).
4. **Movement**: walk around; the colonist moves in RimWorld too. Walls and closed doors block you; E opens doors.
5. **Guns**: try a vanilla rifle, pistol and **at least one modded gun**. The gun is RimWorld's own flat picture turned into the view. Check aiming, recoil, reload, ammo/burst count.
5b. **Weapon editor (F2)**: with a weapon in hand, drag the gun to move it, right-drag to turn, wheel to size; drag the hands and the orange muzzle dot; use the panel's sliders, Hip/Aiming tabs, hands count, "Use this pose for all guns". Restart: poses stick.
6. **Damage**: shoot a raider; they get real RimWorld damage on real body parts (check their health tab).
7. **Melee**: equip a knife, longsword, club, and a modded melee weapon. Left click swings (only hits when adjacent). V bashes with a gun.
8. **Grenades**: G throws with frag grenades equipped.
9. **Getting shot**: enemies' bullets hit you, and you can dodge them by moving.
10. **Health widget**: shows the colonist's real Health tab (select the colonist so their own tab, e.g. Nice Health Tab's, is used); it updates within a second when they are hurt.
11. **Minimap**: the round RimWorld view in the corner follows you and matches the real map. Try Edit HUD (F2): move/resize the minimap and other widgets, toggle them, check they stay put after restart.
12. **Loot (F)**: take a weapon off a corpse, drop one, wear apparel.
13. **Tab**: back to RimWorld and return with the gizmo. Pausing RimWorld freezes you.
14. **Night**: sky and lighting get dark; lamps light rooms.
15. **Options (Esc)**: switch Style to Retro and back, change FOV and mouse speed.
16. **Performance**: FPS on a big colony (F3).
