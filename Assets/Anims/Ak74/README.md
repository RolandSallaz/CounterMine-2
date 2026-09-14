# AK-74 first-person idle

`Assets/Resources/Player.prefab` contains `WeaponIdleSynchronizer` on `Camera`.
It controls separate Animancer components on `Camera/WeaponAimPivot/WeaponSwayPivot/FpsChar`
and `Camera/WeaponAimPivot/WeaponSwayPivot/WeaponRecoilPivot/AK-74`.
The original root Animancer remains responsible for the separate third-person model.

The two graphs use manual evaluation. One clock supplies the same normalized phase
to both clips every frame, so they start and loop together even with different clip
lengths. The arms clip defines the cycle duration; Playback Speed changes both.
Disabling the camera pauses the pair; enabling it restarts both from zero. Photon
ownership already disables the first-person camera for remote players.

Clips:
- `AK74_IDLE_HANDS.anim`: curves for the `Arms_Rig` hierarchy.
- `AK74_IDLE.anim`: curves for the `Armature` hierarchy.

The synchronizer continues working when the assigned clips are edited or replaced.
Do not assign the old `Skeleton/...` body clips
to the first-person arms; their bone paths differ.

Assign arms/weapon Animancers and idle clips directly on `WeaponIdleSynchronizer`
in the camera Inspector. The current player prefab already contains these bindings.

This component currently owns idle playback only. Future fire/reload pairs should
be switched together through the same clock, rather than played independently on
either of its Animancer components.

## Mouse sway

`WeaponSway` on `Camera/WeaponAimPivot/WeaponSwayPivot` offsets the arms and weapon together after
animation sampling. Its input is the actual look delta from `PlayerCameraLook`,
converted to degrees per second. It respects look sensitivity, pitch limits and
cursor unlock. Sway returns to rest when looking stops and freezes when time is paused.

Tune Maximum Position Offset (metres), Maximum Rotation (degrees), Smoothness and
Full Sway Turn Speed (degrees/second) on the pivot. The initial offset limit is 1.5 cm.

## Right-mouse aiming and ergonomics

Hold RMB to aim; release it to return to hip fire. `WeaponAimController` on
`Camera/WeaponAimPivot` moves both arms and weapon, reduces FOV from the original
camera value to 60 degrees, and blends sensitivity to 70% and movement speed to 65%.
Sprinting is blocked during ADS and its exit transition. Unlocking the cursor,
losing focus or dying releases ADS. Disabling the component/camera resets its pose
and FOV. Game pause freezes the transition.

Ergonomics is a tunable weapon stat from 0 to 100, default 55:

| Ergonomics | Aim in | Aim out | ADS sway multiplier |
| --- | --- | --- | --- |
| 0 | 0.500 s | 0.400 s | 0.400 |
| 55 | 0.313 s | 0.250 s | 0.268 |
| 100 | 0.160 s | 0.128 s | 0.160 |

ADS alignment is universal: it is computed from the active sight every frame,
after animation sampling and before sway, recoil and hand IK. There are no baked
AK-74 position/rotation offsets. Procedural offsets are excluded from the alignment
calculation so aiming does not cancel recoil or introduce feedback drift.

For each weapon, add `WeaponAimRig` to its root under `WeaponAimPivot` and assign
a `WeaponHandlingProfile` (Create > CounterMine > Weapon Handling). The current
AK-74 uses `AK74_Handling.asset` here. This profile stores base ergonomics, transition
times, ADS sensitivity and movement speed. Enabling a mounted weapon binds its
rig automatically; an inventory may also call `aimController.Equip(rig)` explicitly.

Add `WeaponSight` to each iron sight or optic. Assign its Aim Point, or leave that
field empty to use the component's transform. Position the point on the sight axis
at the rear aperture/eyepiece: local +Z points towards the target and +Y points up.
Eye Relief sets the camera-to-point distance in metres, clamped outside the near
clip plane. Aimed Field Of View belongs to the sight. The current AK-74 already
has its iron sight configured on `Armature/body/AimSight`.

Mounted, enabled sights with higher Selection Priority override the default iron
sight automatically (e.g. irons 0, scope 10). Call `rig.SelectSight(sight)` to choose
between a scope and backup irons; `rig.SelectSight(null)` restores automatic
selection. Switching blends position, orientation and FOV even while holding RMB.
Removing/disabling the optic falls back to the remaining sight; no sight releases ADS.

Add `WeaponAttachmentStats` to each optic, grip, suppressor or other attachment.
Ergonomics Modifier adds to the weapon's base value; the total is clamped to 0-100
and feeds aiming, sway and recoil. Only enabled attachments mounted under this
weapon count. After an inventory reparents an already-active attachment container
between nested sockets, call `rig.RefreshAttachments()` on the receiving weapon.
Instantiation/enabling of sight/stat components refreshes their owner automatically.
Weapon animation, firing and inventory bindings remain separate from this ADS API.

Configure ADS through the weapon rig, handling profile and sight components in the
Inspector. These control local first-person presentation.

## Dynamic recoil and hand IK

Hold LMB for automatic recoil at 600 RPM (configurable). `WeaponRecoilController`
on `WeaponRecoilPivot` owns the shot cadence and drives the installed Kinemation
Recoilly `RecoilAnimation`. `AK74_Recoil.asset` stores the random pitch/yaw/roll,
kickback and recovery curves. The controller gradually builds sustained-fire
intensity and recovers it while not shooting. Higher ergonomics slightly reduces
intensity; ADS smoothly reduces the visual kick and camera impulse. Camera recoil
changes the actual view direction and can be countered with mouse movement.

The existing `PlayerAnimancerController.PlayWeaponFire` plays/notifies the body
fire animation once per accepted shot; its old independent mouse trigger is disabled
when this controller is assigned. The same shot path launches the ballistic bullet
described below; ammunition and firing audio are not implemented.
Firing is blocked during sprint, death, cursor unlock,
focus loss and pause. Recoilly's Init now clears its previous output/timeline so
disabling and enabling a weapon cannot resume an old recoil impulse.

`WeaponHandIK` on `FpsChar` uses two Final IK limb solvers. The order is: idle
animation, ADS, weapon recoil and sway, then hand IK. The weapon moves independently
on `WeaponRecoilPivot`; the arm roots stay on `WeaponSwayPivot`. Grip targets
`LeftHandGrip` and `RightHandGrip` live under `AK-74/Armature/body` and preserve the
authored reference grip. Fingers continue using their animation curves. Adjust
the grip targets in the prefab for other weapon poses; reduce the corresponding
Hand Weight when an animation needs a hand to leave the gun (e.g. reload).

Configure the recoil profile on `WeaponRecoilController`, and assign hand bones,
grip targets and weights on `WeaponHandIK` in the Inspector.

## Network ballistic bullets

`NetworkWeapon` on the root of `Assets/Resources/Player.prefab` uses actual flight,
not instant hitscan damage. Default speed is 180 m/s (`Bullet Speed (m/s)` in the
Inspector), gravity 9.81 m/s^2 and maximum flight range 200 m. `Bullet Size` is
0.012 m and `Tracer Length` is 0.45 m. Each pooled visual has a bullet mesh plus
a short line behind it; no long TrailRenderer ribbon is emitted. Muzzle position
is `AK-74/Armature/body/Muzzle`. No additional projectile prefab is required.

The owning player fires immediately for visual responsiveness. The master checks
ownership, sequence, rate and origin, then simulates gravity in swept segments of
at most 1/120 second. Every segment checks cover and player capsules; damage (34
by default) is applied only on impact. Late shot requests are caught up by at most
0.25 seconds against recorded capsules. Same-team damage is disabled by default.
Clients receive launch and impact events, not per-frame bullet positions. Clients
correct their predicted bullet instead of spawning a second one. Master shots
use the direct authority path and send confirmation only to other players.
Duplicate same-frame local calls and repeated RPC sequence numbers are ignored.
Health is stored in room properties for late joiners and master changes.

A new master resumes known in-flight bullets at their extrapolated position;
collisions during the handover gap are not reconstructed. New joiners do not
receive bullets launched before they joined. Geometry rewind covers player
capsules, not moving level geometry. No penetration, ammunition or firing audio
is included yet.

Configure speed, gravity, range, damage and bullet appearance on `NetworkWeapon`
in the player prefab Inspector. Assign its camera, muzzle, recoil and health references.
Bullet objects are pooled: they move while active, then deactivate after impact/range
and a short tracer fade. At 180 m/s, 10 m of flight takes about 0.056 seconds.
The spawn frame is rendered before advancing the local visual. Camera input is
applied before the shot direction is sampled; a locally rejected shot does not
produce recoil or a master-side flash.

## Muzzle flash

`NetworkWeapon` automatically creates one reusable `MuzzleFlashEffect` on first fire.
The owner sees it immediately at the muzzle; other players display it from the
existing shot confirmation. Confirmation does not replay the owner's flash, and
repeated sequence numbers are ignored. No extra RPC or projectile object is needed.

The effect uses low-poly solids: a faceted axial flame, five angular side tongues,
a pale core and five polygonal sparks, plus a brief warm point light without shadows.
Each triangle has a flat yellow/orange color. The URP shader in `Assets/Resources/VFX`
uses depth-tested solid surfaces, shrinking the geometry before clipping it at the
end of its lifetime. Geometry is reused. The muzzle flash follows the local weapon
after recoil and hand IK.

Use the Muzzle Flash section of `NetworkWeapon`: Enabled, Scale, Brightness and
Duration (default 0.055 seconds). The current player uses Scale 1.8.
Sparks fade within 0.14 seconds. For a suppressed
weapon, reduce Scale/Brightness or disable the effect. HDR bloom can enhance the
glow; the effect also renders without post-processing.
