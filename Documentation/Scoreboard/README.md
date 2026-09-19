# Match score and reward HUD

Hold Tab for standings. Sorted by points descending, then kills descending, deaths ascending and participant key for stable ties. Players and persistent bot slots are included. Scroll or use PgUp/PgDn if the list exceeds the panel. The local row is highlighted.

Enemy kill: +10 points and +10 money. Assist: +5 points and +5 money. Suicide, world death and friendly kills do not grant rewards. Three animated reward cards can appear at the bottom; successive rewards remain readable. Team, kills, deaths, assists, points, local wallet and local score are shown in the scoreboard.

MatchScore runs independently of player lives and writes master-calculated totals to room properties under score/p<actor> and score/b<slot>. Late join reads existing standings; new masters load the room totals. KillRewards credits wallet/notifications from positive changes in local totals, rejects duplicate or older payout baselines, and waits for profile loading before saving earned money. Entering a new room resets match counters, not profile money. No 10-minute timer yet.

Validation: validation.txt. Pure logic checks ran against compiled assemblies; Unity rendering and live multiplayer remain to be checked.

Assist rules: every hostile contributor in the last 10 seconds receives one assist, except the finisher. Repeated hits refresh eligibility without adding duplicate assists. Self/friendly damage is excluded; full healing and death clear contributions. Human and bot IDs are tracked separately; kill snapshots carry the complete contributor list. See assist-validation.txt.
