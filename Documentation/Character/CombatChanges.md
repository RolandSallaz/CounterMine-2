# Hit zones and impacts

PlayerHitboxes is attached to Player.prefab and samples only the TPS ragdoll collider shapes. The shapes are queried analytically; the animation-time ragdoll colliders remain disabled. Head uses the head box, torso uses pelvis/spine/chest, and arms/legs use their limb capsules. Forty pose samples retain roughly half a second of hit history for the master's swept projectile queries.

Defaults: head x3, torso x1, arms x0.65, legs x0.75. For the current 34 damage weapon these give 102, 34, 22, 26. Tune on PlayerHitboxes. Draw Hitboxes shows the zones when the player is selected in Scene view.

Environment hits are confirmed by the master with point and normal. Each client plays seven angular sparks and a black surface mark. Player hits and range/time expiry do not spawn this effect. Lifetime defaults to 15 seconds (NetworkWeapon / Impact Lifetime). A pool caps effects at 64; oldest effects can be reused earlier. Marks follow locally detected moving cover. Cosmetics are not persisted for late joiners.

Late-join pose fixes: remote characters start with idle while waiting for their snapshot; static one-frame character poses are restored before each evaluation to avoid accumulating IK; physical interpolation is disabled on animated kinematic bones and enabled only in ragdoll; FPS-to-TPS pose copying includes bone scale.

The exact reported late-join problem requires confirmation with two clients on the same updated build. Editor diagnostics cover direct idle from a delayed snapshot, repeated IK, moving the root far from origin, hit zones and historical poses.
