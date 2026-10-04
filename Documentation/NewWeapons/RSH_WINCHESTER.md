# RSH-12 and Winchester Model 1897

Both weapons are installed in `Player.prefab` and `Bot.prefab`. Buy and equip them in the existing main-menu or deployment shop. RSH-12 uses the secondary slot (2); Winchester uses the primary slot (1). Ownership and loadout use the existing persistent save system.

| Setting | RSH-12 | Winchester 1897 |
|---|---:|---:|
| Catalog ID | `rsh12` | `winchester1897` |
| Price | 850 | 700 |
| Capacity | 5 | 5 |
| Fire mode | Single shot | Pump, single shot |
| Rate, rounds/min | 150 | 80 |
| Projectiles per round | 1 | 8 |
| Base damage per projectile | 65 | 19 |
| Pellet cone half-angle | 0° | 2.4° |
| Initial speed, m/s | 300 | 380 |
| Full damage / maximum distance, m | 30 / 150 | 12 / 70 |
| Minimum damage fraction | 0.5 | 0.2 |
| Reload, seconds | 3.4 | 4.2 |

These are game-balance settings, not a claim of measured real-world ballistics. Both weapons use gravity and the existing swept collision simulation. Shotgun pellets independently hit cover and players and receive distance and hit-zone damage modifiers. Ammo, recoil and shot audio trigger once per round.

Winchester's center-mass pattern is tuned for a one-shot burst at 10 m on a 0.55 m wide standing target, with damage falling below 100 by 20 m. The first-person weapon sits lower and closer to the camera, with 0.22 m iron-sight eye relief. The left-hand target is on the pump fore-end, below the barrel, rather than at the receiver. Both first- and third-person IK preserve elbow bend and remove the previous shoulder adjustment before each solve. The third-person presentation brings the weapon another 0.28 m toward the body. Pose validation checks both FPS grips in hip, ADS and reload and verifies repeated solves do not drift; screenshots bake the current skinned pose so they include the solved hands.

The supplied FBXs have no gameplay animation clips. `WeaponManualAction` supplies staged cylinder opening, ejection and loading gestures for RSH-12, and pump cycles plus repeated loading-hand motion for Winchester. The synchronized action clock also drives the weapon pose and a subtle camera sway while the weapon stays in view. Each reload action inserts one cartridge; actions repeat until the magazine is full. Interrupting a reload preserves cartridges already inserted. Shot/reload sounds are variations made from the project's existing audio.

`ConfirmShot` now carries the weapon and damage snapshot. Pellet trajectories use deterministic shot seeds and separate projectile IDs. The master reads ballistics from its weapon catalog. `LobbyManager` uses protocol version `CounterMine-0.3-pellets`, separating this implementation from older incompatible clients.

## Maintenance and checks

- Reinstall: **Tools → CounterMine → Install RSH-12 and Winchester** (`InstallRshWinchester`). This replaces only these two entries and their generated roots; other weapon entries remain intact.
- Editor checks: **Tools → CounterMine → Validate RSH-12 and Winchester** (`ValidateRshWinchester`). Reports and first-person screenshots are in this directory.
- A `Temp/validate-rsh-winchester-menu.request` file runs an opt-in Play Mode menu preview check. It makes no purchases and does not connect to multiplayer.
- Validation covers equip/ADS, mechanisms, ammo, interrupted/completed reloads, network action binding, projectile count/speed/cone, duplicate confirmations, partial cover, damage falloff, and an isolated purchase/save round trip.

No player/WebGL build or live two-client multiplayer test was performed.
