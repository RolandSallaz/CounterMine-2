# Grenade

Stylized low-poly visual game prop. Approximately 10 cm tall, authored in meters.
1,924 triangles, flat shading, UVs, seven palette materials. No texture dependency.

- Editable source: `SourceArt/Grenade/grenade.blend`
- Unity model: `Assets/Resources/Grenade/grenade.fbx` (in Resources so gameplay code can load it at runtime)
- Render: `Documentation/Grenade/grenade_preview.png`
- Reproducible authoring script: `SourceArt/Grenade/build_grenade.py`

Separate objects: Body_Core, Body_Segments, Neck, Marking_Band, Safety_Lever,
Safety_Pin, Pull_Ring. Grip_R is an empty reference for future hand placement.
The root origin is at the body center. Blender uses Z up; FBX exports Y up for Unity.
Studio lights, camera and ground are only in the Blender source, excluded from FBX.
The scoped GrenadeModelImporter assigns URP/Lit to the imported materials.

Throwing is wired into the player: `GrenadeThrower` shows the model in the left
hand during the `GrenadeThrowIK` animation and launches `GrenadeProjectile` on
release. The master simulates the authoritative flight and radial damage; every
client renders the same ballistic arc and procedural explosion. Stock is 3
grenades per player with +1 every 30 seconds; the throw animation alone gates
the throw rate, there is no extra cooldown. The stock counter lives on the
weapon panel of `PlayerHUD` (`G xN`). Live grenades register in
`GrenadeProjectile.Active`; the local HUD tracks the nearest one within 25 m
with an orange arrow around the crosshair (`ThreatArrow`). Incoming damage
direction uses the same wheel in red (`HitDirectionIndicator`, fed from the
local health drop via `PlayerHealth.LastDamage`).

- Explosion sound: `Assets/Resources/Audio/Explosion/explosion.wav`,
  "Mechanical Explosion" by Spring Spring, CC0 via OpenGameArt
  (https://opengameart.org/content/mechanical-explosion). No attribution required.
