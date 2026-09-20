# Construction battlefield

Scene: `Assets/Scenes/SampleScene.unity`.
Editable geometry: `Assets/maps/map1/BattlefieldArena/BattlefieldArena.prefab`.

- 132 × 92 m foundation; playable perimeter about 130 × 90 m.
- Central 22 × 20 m concrete frame with decks at 4, 8, 12, 16 and 20 m.
- Two independent external ramp routes. Each floor connects opposite landings;
  successive flights maintain full headroom.
- Four side building frames, garage canopies, containers, supply stacks,
  concrete barricades, partial walls and a construction crane.
- Bases/safe zones at x = ±56 m. Side streets support flanking; upper-level cover
  breaks exposed firing lines. Both teams have mirrored approaches.
- Old Map and Vertical Arena roots remain disabled in the scene for reference.
- BotPatrolRoute on the new prefab supplies actual floor destinations, avoiding
  scaling old bridge coordinates onto nonexistent floors.

`python Tools/build_battlefield_map.py` regenerates this prefab and its materials;
manual prefab edits will be replaced. Scene placement is already saved.
Unity menu: Tools → CounterMine → Validate Battlefield Map.

Validation: 30 complete native Unity NavMesh routes from both bases to all central
floors, side roofs, outer flanks and avenues. The isolated check uses the exact
saved geometry and production BotNavigation, with metadata-only team/health stubs.
Preview images from that check use matching palette colors with the Standard
shader; main-project lighting/URP can differ. Live PvP balance and frame rate have
not been measured.

`movement-validation.txt` records an additional isolated simulation using the
production BotController and a native CharacterController: the bot physically
reaches the central avenue, first and fifth central floors, and a side roof.
Only Photon/combat dependencies are stubbed; this does not validate multiplayer
transport, combat decisions or crowd interactions.
