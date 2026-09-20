# Bot navigation and decisions

Only the Photon master runs decisions and movement. On authority transfer the
new master clears old targets/paths and builds navigation if necessary.

`BotNavigation` builds one asynchronous NavMesh per loaded gameplay scene from
enabled static box/mesh colliders. Characters and safe-zone collider geometry are
excluded. Safe zones become area volumes (3 = team 1, 4 = team 2); each team query
excludes the enemy's area. Settings match the 1.8 m character, 0.28 m navigation
radius, 0.3 m step height and 40 degree slope. The bake is bounded to the arena.

Bots follow complete paths using their existing CharacterController, gravity
and network transforms. There is no competing NavMeshAgent transform writer.
Spawning waits for navigation and accepts only sampled walkable positions.

Twelve patrol destinations cover flanks, cargo courts, bridge and roofs, with
different starting offsets per bot. Targets require line of sight and broad
forward vision; unseen opponents are pursued at their last observed location
for up to six seconds. Shooting includes a reaction delay, spread and short
bursts. Close combat uses short sideways routes; reload/low health triggers a
search for cover. Stuck bots abandon a route and select another sector.

Patrol positions and the bake bounds currently match SampleScene. Update these
when replacing or substantially resizing the arena.

Validation menu: Tools > CounterMine > Validate Bot Routes. This builds navigation
from the saved scene and checks routes from both spawns to both roofs, the bridge,
cargo courts and flanks, plus enemy-sanctuary exclusion. A live Photon match and
WebGL performance are separate checks, not covered by these route assertions.
