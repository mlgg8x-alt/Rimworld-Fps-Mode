# Current fork controls

Overview uses the rendered 3D cursor position for selection, native actions, targeting and Architect designation. Right-click orders; Shift captures queue intent while choosing a context action or placing waypoints. MMB drag rotates the camera. Clicked selection persists until another click changes it. A small cell outline shows the ground destination before clicking.

B opens native Architect. Colony tabs opens a searchable, scrollable grid built from the active MainButtonDefs, including modded tabs. Native building placement and drag designation use the same projected cell as commands. Native gizmos show pawn/thing actions and Immersive Warfare's own controls.

V enters the clicked colonist, or the closest controllable colonist to the overview camera when none is selected. In first person, press E while peaceful to perform the primary interaction. Tap E to pick up a stack; hold E to choose an amount. A nearby weapon equips directly; RMB provides native alternatives such as harvest versus chop. E uses linked stairs/ladders/lifts; approaching a paired landing also uses native AASB transit. Space toggles overview planning/execution. Pause and actual speed remain visible below Health/Pain.

Equipped guns remain holstered in peaceful mode. With Immersive Warfare active, its native WeaponsGizmo is shown at the lower left; hold Alt to release the cursor. Ammo reads magazine/charge components, shows a dash for an unrecognized ammo component, and says Unlimited for ordinary vanilla weapons. The existing shot path consumes supported reloadable charges.

RimVision settings have a Weapon and hand editor button at the top. Tactics and compatibility contains selected/hover/path colors and opacity, menu scaling, and automatic fallback generation. Changing status text below the compass is disabled by default.

Authored XML models keep priority. Unmodeled mod buildings can use cached generated volumes derived from their existing sprite, footprint and building properties; items already use sprite contour extrusion. This needs no user-authored XML, but produces approximate fallback geometry rather than reconstructing unseen surfaces. Floor spots and specialized objects retain their existing rendering.

Visible AASB levels are picked in rendered space and translated back to their real source cells. Solid upper terrain blocks clicks into the lower floor; genuine openings reveal targets below. World outlines and target boxes use the same stacked coordinates. Manual first-person bullets follow the physical stacked trajectory, testing translated pawn/object bounds, floor openings and roofs before applying the existing impact logic. Cross-floor combat still needs a live colony check.

Builds and isolated regression probes are useful checks; they do not establish the appearance or behavior of a loaded colony. Live verification remains required for Architect, queued work, native weapon slots, E transit and cross-level picks.

## RC3 fixes merged into this fork

Hold E on a bare growing-zone cell to sow available cells in that zone; release to stop. Plants keep harvest/chop as their primary action. Stamina recovers while resting or performing work. HUD need choices now save with each colony, inheriting the previous global choices for older saves; mood is available in the need selector. The Interface settings include a resource-counter toggle. Construction previews remain visible for wall replacements and wall-mounted objects; conduit designation uses the projected floor cell.

The supplied RC3 Multiplayer add-on targets RimWorld 1.6 and references six bridge members absent from this fork. It has been reviewed but is not installed as a working 1.7 integration.

Item count labels use the item renderer's true center and shelf/table surface height, with centered text and no inherited GUI padding.
