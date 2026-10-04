# Procedural weapon actions based on AK74

The AK74 equip and reload clips were sampled at their original 60 FPS, including weapon-body movement, both hands and the camera bone. The source assets remain unchanged. The equip reference contains 52 frame intervals (0.8667 seconds), and reload contains 173 (2.8833 seconds). The CSV files `ak74-equip-frames.csv` and `ak74-reload-frames.csv` retain the measured transforms; `ak74-motion-analysis.txt` records their ranges.

The equip starts below the view, accelerates upward, overshoots and settles. Camera pitch and roll trail the weapon. Reload alternates extraction, insertion and charging movements; its camera response follows those events instead of repeating a sinusoid. `WeaponMotionReference` stores the samples as an asset and interpolates them continuously. `WeaponProceduralMotion` adapts amplitude and duration for each weapon. All poses use the existing synchronized action clock. Weapon-root rotations pivot around the saved trigger-hand position, so the model origin does not pull the wrist in a circle.

| Weapon | Actions and mechanisms |
|---|---|
| UCP | Procedural equip/reload; magazine extraction, retrieval arc, insertion, seating and slide reach. Original authored assets are retained. |
| HK416 | Faster equip; magazine exchange with separate extraction and seating beats. |
| L115A3 | Slower equip and reload; the supporting hand returns to the fore-end while the right hand operates the bolt. Its post-shot bolt cycle is delayed until after recoil. |
| RSH-12 | Cylinder opens once; each cartridge follows a retrieval arc into the cylinder, followed by smooth chamber indexing. The cylinder closes once at the end. |
| Winchester 1897 | The gun turns to expose the loading port. The hand fetches and seats red shells individually, then returns to the fore-end for a single closing pump stroke. |
| Milkor | Heavier procedural equip and camera response. Existing finite-ammo rules and cylinder indexing remain in place; reload is unavailable. |

The player's FPS arms and the bot/world rig are checked independently. Procedural IK removes its previous shoulder corrections before solving and fits both wrists within the rig's reach. Pistol and long-gun world offsets keep the weapons closer to the torso. The bot catalog now includes every weapon in the player's catalog, with local component references.

Winchester keeps both upper-arm attachment positions unchanged. Its first-person arms fit their independent camera origin to the gun rather than pulling the gun into the camera. The world rig and standalone menu rigs can bring the gun towards the body when needed for reach. Corrections reset each frame. ADS uses 32 cm eye relief and a sight axis through the actual imported front bead, with clearance above the receiver. The installer reads native mesh rest geometry rather than the altered prefab BakeMesh pose. Validation checks the physical bead against the aim axis, eye relief throughout the pump stroke, shoulder attachment positions, repeated solves, and equip/reload grip reach.

## Regeneration and validation

After rebuilding weapon roots with an individual weapon installer, run **Tools → CounterMine → Install Procedural Weapon Motion**. This resamples AK74, updates `Assets/Resources/WeaponMotion/AK74 Motion Reference.asset`, binds mechanisms and cartridge props, and adds missing bot weapon roots. It does not change damage, prices, capacities or purchases.

Run **Tools → CounterMine → Validate Procedural Weapon Motion** to sample every equip/reload action at 60 FPS for both prefabs. `Motion/validation.txt` reports grip gaps, camera peaks, frame rotation steps and return-to-idle checks. The contact sheets show eight evenly spaced poses, read left to right across the top row, then the bottom row. Character skins are baked for the previews so they include the current IK pose; weapon skins retain their native renderer.

RSH/Winchester use one continuous action clock for the missing-round count, shared in the network presentation state. Ammo increases at 62% of each insertion stroke, rather than when the entire action restarts. Opening and closing happen once; the weapon stays in its loading pose between cartridges. Fire cancels the reload when a seated round is available, retains all credited rounds, and consumes one round for the shot. Firing the only loaded round suppresses the immediate automatic reload restart; a subsequent reload/dry-fire request can reload again. Magazine-fed weapons retain their original reload gate.

Run **Tools → CounterMine → Validate Individual Reload** for full 60 FPS cycle checks, exact insertion credit, interruption/resumption, weapon switching, network round counts, and real bot firing through the cancellation path. `individual-reload-validation.txt` records results; the `*-individual-reload.png` contact sheets show opening, fetch, approach, insertion, repeat and closing poses.

Additional HK416/L115A3 and RSH/Winchester validators verify ammo, interrupted/completed reloads, ADS and application of synchronized network action state. Editor validation does not replace a live two-client match or a WebGL build.
