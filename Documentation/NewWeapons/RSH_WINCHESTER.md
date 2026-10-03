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
| Base damage per projectile | 65 | 13 |
| Pellet cone half-angle | 0° | 3.2° |
| Initial speed, m/s | 300 | 380 |
| Full damage / maximum distance, m | 30 / 150 | 10 / 70 |
| Minimum damage fraction | 0.5 | 0.15 |
| Reload, seconds | 3.4 | 4.2 |

These are game-balance settings, not a claim of measured real-world ballistics. Both weapons use gravity and the existing swept collision simulation. Shotgun pellets independently hit cover and players and receive distance and hit-zone damage modifiers. Ammo, recoil and shot audio trigger once per round.

The supplied FBXs have no gameplay animation clips. `WeaponManualAction` supplies staged cylinder opening, ejection and loading gestures for RSH-12, and pump cycles plus repeated loading-hand motion for Winchester. The synchronized action clock also drives the weapon pose and a subtle camera sway while the weapon stays in view. Reload completes the entire magazine at the end of the action. Interrupting it preserves the previous ammo count; individual shell insertion is not implemented. Shot/reload sounds are variations made from the project's existing audio.

`ConfirmShot` now carries the weapon and damage snapshot. Pellet trajectories use deterministic shot seeds and separate projectile IDs. The master reads ballistics from its weapon catalog. `LobbyManager` uses protocol version `CounterMine-0.3-pellets`, separating this implementation from older incompatible clients.

## Maintenance and checks

- Reinstall: **Tools → CounterMine → Install RSH-12 and Winchester** (`InstallRshWinchester`). This replaces only these two entries and their generated roots; other weapon entries remain intact.
- Editor checks: **Tools → CounterMine → Validate RSH-12 and Winchester** (`ValidateRshWinchester`). Reports and first-person screenshots are in this directory.
- A `Temp/validate-rsh-winchester-menu.request` file runs an opt-in Play Mode menu preview check. It makes no purchases and does not connect to multiplayer.
- Validation covers equip/ADS, mechanisms, ammo, interrupted/completed reloads, network action binding, projectile count/speed/cone, duplicate confirmations, partial cover, damage falloff, and an isolated purchase/save round trip.

No player/WebGL build or live two-client multiplayer test was performed.
