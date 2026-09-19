# Vertical arena

`Assets/Scenes/SampleScene.unity` contains one instance of
`Assets/maps/map1/VerticalArena/VerticalArena.prefab`, in world coordinates.
The existing map is retained; two low entrance walls are moved to the flanks to
open the ramps. Spawns and safe zones are preserved.

- 6 m upper deck; 5.65 m clearance beneath the bridge.
- Two 26 m approach ramps at 12.99 degrees, within the existing central lane.
- 8 x 8 m central platform with segmented cover, plus exits onto both central roofs.
- Two cargo courts with four tall containers, ten supply crates, L-shaped flank cover and underpass barricades.
- Both 60 m outer sightlines at z=+/-11.8 stay open; cargo aisles offer close encounters with multiple exits.
- Cyan/orange approach markings and six staggered ground barricades.
- 153 mesh pieces sharing seven materials; 71 box colliders. Cosmetic markings
  and underside details have no collision. No runtime builder is required.

Edit the prefab directly in Unity. `Tools/build_vertical_map.py` is an optional
authoring script; rerunning it replaces edits to that prefab and its materials.
It preserves asset GUIDs and does not duplicate the scene instance.

The explicit Editor check is `Tools > CounterMine > Validate UI and Vertical Map`.
It checks Unity imports and 791 collider ray samples across both ramps and the
deck, both sloped roof exits, cargo-aisle clearance and long sightlines, as well as repeated HUD binding. Results are in `unity-validation.txt`.
The PNG named `geometry-preview` shows the initial 4.5 m design and is historical, not the current layout.
`unity-overview` and `unity-approach`, when present, are renders of the saved scene.

Bots retain their existing ray-based avoidance, not NavMesh routing. The main
ramps are shallow enough for the short forward probe; tactical use of roofs and
the new route still needs a live match check. Player-build visual parity also
requires comparison at the same viewport size.

Offline geometry regression checks: `python Tools/validate_map_geometry.py`.
These include the saved scene building/wall box colliders and are analytical,
not a substitute for Unity physics or a live bot match.
