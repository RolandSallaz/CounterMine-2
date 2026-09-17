# Equip, reload and animated camera

The updated AK74_Equip.blend supplies four actions: AK74_Equip_Character, AK74_Equip_Weapon, AK74_Reload_Character and AK74_Reload_Weapon. Exported at 60 FPS with constraints baked every frame, no simplification and Unity animation compression Off. Equip: frames 0–52 (0.8667 s); reload: frames 0–173 (2.8833 s). The main character and weapon model files and authored blend remain unchanged. Idle .anim clips are regenerated from the final equip frame.

NetworkWeaponPresentation plays registered action `reload` on R in Editor and Development Builds, when the owner is alive/idle and the game has mouse focus. This is animation debugging: it does not implement ammunition or magazine inventory. The action uses the existing network weapon state and action timestamps. Fire and arm IK are blocked during the action and restored at idle.

PlayerCameraLook applies mouse pitch/lean and then WeaponIdleSynchronizer.CameraRotationOffset. The offset is measured from the current character camera bone relative to the character skeleton, against the neutral orientation of the main model. This avoids inheriting the FPS parent's camera rotation a second time. Camera movement from the animation bone is not applied; only its rotation is added.

Verification: equip_reload_installed.txt and equip_reload_geometry.txt. Actual Unity skinned geometry was compared with Blender at start/middle/end of both actions (60 mesh samples). Maximum error below one micrometre. Repeated camera rotation application stays stable; reload fire/IK gates and idle recovery pass. Live two-client reload input was not tested.
