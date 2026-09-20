# HK UCP

Controls: **1** AK-74, **2** UCP, **R** reload. UCP fires one shot per click.
Prototype balance: 20-round magazine, 400 RPM limit, 28 base damage. Each weapon
keeps its magazine when switching. Switching cancels reload without refilling.

Six animation clips are exported from `Assets/Anims/UCP_Actions.blend` at 60 FPS:
Idle (static pose), Equip (frames 0–70), Reload (frames 0–136). Character and weapon
are baked with both corresponding actions active so Child Of constraints resolve.
The Blender source is not changed by export.

To re-export after animation edits, run Blender in background with
`--python Tools/export_ucp.py`, then Unity menu **Tools > CounterMine > Install UCP**.
The installer refreshes clips and UCP bindings on Player/Bot prefabs. This resets
UCP grip/sight positions and prototype stats to the values in the installer.
**Tools > CounterMine > Validate UCP** checks prefabs without saving them.

UCP currently reuses existing weapon audio and the shared recoil animation profile.
The model materials and authored animation poses are preserved.
See `validation.txt` for verification scope and `first-person.png` for the preview.
