# Team safe zones

`Assets/Resources/TeamSafeZone.prefab` is a reusable zone. `SampleScene` contains a box at each team spawn (X = -43 and +43). The base collider size is 8 x 4 x 8 m; Transform scale can widen the protected area.

Set **Team** (1 or 2), edit the **BoxCollider Center / Size**, or rotate/scale the object. **Reveal Distance** controls the camera proximity fade (default 5 m). Cyan is team 1; orange is team 2. The collider is solid, with collision ignored for friendly CharacterControllers. Layer 2 keeps ground and spawn probes from treating the zone roof as terrain; weapon queries include that layer.

Inside any zone, firing and grenade throws are blocked locally and on the master. Enemy bullets stop at the barrier, enemy grenades bounce off it, and protected team members reject enemy damage on the master. Grenade broadcasts include the throwing team so client-side bounces match. HUD displays SAFE ZONE. Disabling the component disables its barrier and protection.

Validation menu: **Tools > CounterMine > Validate Safe Zones** (outside Play Mode). Checks containment, rotated boxes, team projectile filtering, grenade cover, CharacterController passage, cleanup and shader compilation. Produces `validation.txt` after success. Requires Unity; a dotnet build alone does not run these physics/rendering checks. Live two-client play still needs verification.
