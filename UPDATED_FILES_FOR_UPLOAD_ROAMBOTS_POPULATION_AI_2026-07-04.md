Raidlands roam bots tactical utility, defensive healing, foliage, and barricade update - 2026-07-05

Current update:
- RaidlandsRoamBots is now v0.3.24.
- Bots can now choose real F1 grenade throws as a tactical `throw_grenade` action when a covered or last-known target is within the configured throw window.
- Bots can now choose real smoke grenade throws as a `throw_smoke` action when hurt or under pressure and needing a retreat/screen lane.
- Bot utility throws use shared bot/team cooldowns, cap active bot utility projectiles, avoid throwing F1 grenades onto squadmates or non-target bystander players, and refuse utility throws into base-restricted paths/positions.
- Fresh bot F1 throws create short-lived danger zones. Bots inside one should pick a high-priority retreat/escape move and movement commands avoid destinations inside active grenade danger zones.
- `raidbots.list` and the right-side debug panel now include `utility=...` / `Utility:` so playtests can distinguish `grenade_candidate`, `grenade_thrown`, `smoke_thrown`, cooldown, range, ally-close, bystander-close, or base-blocked utility decisions.
- Skill testing is now more even by default: `casual=34`, `average=33`, `dangerous=33`.
- Long-range defensive healing is now skill-scaled. Around `40m-60m`, bots that are losing the exchange or below their skill's defensive health threshold will prefer to get healthy before continuing the fight.
- Defensive cover selection now prefers nearby hard cover first. The nearby-cover cutoff scales from `3m` for lower-skill bots to `8m` for higher-skill bots; if cover is farther than that, bots prefer to place a barricade before continuing the heal-cover process.
- Bots now try to hold cover until close to full health (`AI -> Low Health Cover Heal Target Fraction` is `0.96`) and the cover-heal commitment is longer (`24s`).
- Bots can still fire while moving to cover, holding cover, or after placing a barricade. The only intentional no-fire window remains the syringe/fire-lock window, and close pushes can break the hold-cover preference so the bot shoots back.
- Bots now keep fresh damage-wall awareness through the active barricade cooldown when they are shot again at or after their current barricade placement, so a follow-up wall can happen once the cooldown opens instead of the damage reaction expiring early.
- The default barricade cooldown is now `12s` instead of `18s`, with a new `AI -> Barricade Followup Memory Seconds` value of `6s` to keep the follow-up window tight without making bots spam walls.
- Foliage LOS is stricter for long jungle/forest sight lines like the screenshot case where a bot 90m+ away reported `LOS: Y` and `Exposure: 1.00 (6/6)` through dense leaf cover.
- Foliage sphere casts now default to a wider `0.9m` radius, allow only `14m` of free clear-through-foliage visibility, and require just `1` foliage blocker hit to hide a probe.
- Tree/resource layer hits now count as foliage blockers even when the collider/prefab name does not include an obvious foliage word.
- The foliage classifier also recognizes more jungle-style names such as leaves, branches, canopy, palms, ferns, vines, and bramble.
- Long sight lines now sample terrain forest splat along the ray, so purely visual forest/jungle density can still conceal targets even when individual leaf meshes do not produce useful physics hits.
- This package still includes the v0.3.20 sound-investigation changes: bots listen for player gunfire, suppressed shots, rocket launches, explosive damage, thrown explosives, and melee/tool impacts.
- New config knobs:
  - `AI -> Foliage Terrain Sampling`
  - `AI -> Foliage Terrain Sample Step`
  - `AI -> Foliage Terrain Samples To Block Vision`
  - `AI -> Sound Investigation Commitment Seconds`
  - `AI -> Sound Investigation Command Cooldown Seconds`
  - `AI -> Barricade Followup Memory Seconds`
  - `AI -> Long Range Defensive Minimum Distance`
  - `AI -> Long Range Defensive Maximum Distance`
  - `AI -> Long Range Losing Fight Memory Seconds`
  - `AI -> Nearby Defensive Cover Minimum Distance`
  - `AI -> Nearby Defensive Cover Maximum Distance`
  - `AI -> Long Range Defensive Health Fraction Casual/Average/Dangerous`
  - `AI -> Full Health Cover Discipline Chance Casual/Average/Dangerous`
  - `AI -> Healing Return Fire Distance`
  - `AI -> Grenade Prefab`
  - `AI -> Smoke Grenade Prefab`
  - `AI -> Grenade Minimum/Maximum Throw Distance`
  - `AI -> Smoke Minimum/Maximum Throw Distance`
  - `AI -> Grenade Throw Velocity`
  - `AI -> Smoke Throw Velocity`
  - `AI -> Grenade Fuse Seconds`
  - `AI -> Grenade Danger Radius`
  - `AI -> Grenade Ally Avoid Radius`
  - `AI -> Grenade Avoidance Seconds`
  - `AI -> Smoke Screen Distance`
  - `AI -> Maximum Active Bot Utility Projectiles`

Rust server files to upload for this update:
- oxide/plugins/RaidlandsRoamBots.cs
- oxide/config/RaidlandsRoamBots.json

Recommended live RCON order for this update:
- Upload `oxide/plugins/RaidlandsRoamBots.cs`
- Upload `oxide/config/RaidlandsRoamBots.json`
- `oxide.reload RaidlandsRoamBots`
- `raidbots.list ababmxking`
- Recheck the jungle/forest sight line from the screenshot at 70m-120m.
- Shoot a bot after it has placed its first barricade; once the `12s` cooldown opens, it should be eligible to place another barricade if it is still exposed or its current wall is not effective cover.
- Test a 40m-60m fight while the bot is taking worse trades. If hard cover is within the skill-scaled 3m-8m nearby window, it should move there and heal; if cover is farther, it should prefer barricade first.
- Break LOS around 12m-42m and hold near cover/last-known position. A bot may choose `throw_grenade`; squadmates should move out of the danger zone instead of standing in the blast lane.
- Hurt a bot while it is exposed around 10m-55m. A bot may choose `throw_smoke`, then retreat through the screen instead of pushing.
- `raidbots.list ababmxking`

Expected verification for this update:
- Through dense jungle/forest cover, the side panel should stop showing `LOS: Y` with `Exposure: 1.00 (6/6)`.
- Expected good outcomes are either `LOS: N` with `Sight: foliage ...`, or a reduced exposure count below the shoot threshold.
- In a cleaner lane with only light brush or a short gap, bots can still reacquire once enough target probes are genuinely visible.
- A repeatedly pressured bot should show a shorter barricade cooldown and should not lose the second wall opportunity just because the original damage reaction window expired.
- A healing bot should avoid pushing/flanking until close to full health, but should still shoot if the player closes distance during the heal process.
- Utility playtests should show concrete `Utility:` reasons in the panel. Expected blockers include `utility_cd`, `team_cd`, `grenade_range`, `grenade_ally_close`, `grenade_bystander_close`, `utility_base_blocked`, or `utility_cap`.
- A successful F1 throw should leave the bot in `GrenadeFlush`, spawn a real grenade, then move away or back to cover. A successful smoke throw should leave the bot in `Retreat` and move it away from the threat.
- If the bot still gets perfect sight through foliage, paste the side panel `Sight:` line plus `raidbots.list ababmxking` from that same angle.

Previous Gen2/native land roam bots rollout - 2026-07-04

Context:
- Live testing proved the plugin can spawn and teleport to bots, but the accepted legacy scientists can remain stuck standing.
- The new direction is native AI first: use Rust's Gen2/naval scientist mechanics for land roamers, and keep legacy BasePlayer scientist/Kits behavior only as fallback.
- v0.2.0 live testing accepted ScientistNPC2 with gen2=True, target=True, and sample navmesh=True, but Unity still logged "ResetPath/DistanceToEdge can only be called on an active agent that has been placed on a NavMesh" and the NPC crouched/stood in place.
- v0.2.1 live testing proved the Gen2 prefab is not usable at the tested mainland spawn points because the internal Unity NavMeshAgent is not active on navmesh. Legacy fallback spawned, but still needed an explicit movement/target kick.
- v0.2.2 live testing proved the native spawn-group prefab is tried first and movement kick methods are called, but the bot still stood in place; the old moved=True log did not prove BaseNavigator produced a path.
- v0.2.3 live testing proved the server had navmesh globally disabled; after `aimanager.nav_disable false`, legacy fallback bots receive valid NavMesh paths and move, but still need an explicit combat/target nudge to shoot.
- v0.2.4 live testing proved the combat nudge works, but the bot still spawned as a legacy junkpile scientist because the native spawn-group preferred prefab jumped ahead of the Gen2 list; it also spawned too far away for fast MP5 engagement.
- v0.2.5 live testing proved Gen2 candidates are now first, but mainland placement still fails at Rust's agent layer with "No navmesh areas matching agent type"; the known working junkpile scientist fallback needs to remain in the list.
- This local repo is a staging tree; live Oxide reload is still the final runtime proof.

Rust server files to upload:
- oxide/plugins/RaidlandsRoamBots.cs
- oxide/config/RaidlandsRoamBots.json

Important behavior changes:
- RaidlandsRoamBots is now v0.2.6.
- Active roam bots are tracked as BaseCombatEntity, not only BasePlayer, so Gen2 ScientistNPC2 entities can be accepted and managed.
- Default AI runtime mode is gen2_native.
- Gen2/native prefab candidates are tried first:
  - assets/rust.ai/agents/npcplayer/humannpc/scientist/gen2/scientist2.prefab
  - assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_ptboat.prefab
  - assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_rhib.prefab
- Legacy land scientist prefabs remain as fallback when Allow Legacy Scientist Fallback is true.
- Native Gen2 bots do not strip inventory or call Kits.GiveKit; their stats use kit_name=native_gen2.
- Legacy BasePlayer fallback bots can still use Kits loadouts.
- Gen2 activation wakes NpcSleepingComponent before the final placement check, samples/warps the RustNavMeshAgent, verifies the underlying Unity NavMeshAgent is active on navmesh, seeds SenseComponent with a simulated sighting of the nearest real player, sets an initial destination to that target, and force-ticks FSMComponent.
- Gen2 diagnostics now distinguish the Rust navmesh sample from the internal Unity agent state, for example nav=gen2:unity=on,sample=on.
- When a native spawn group provides a Gen2/native preferred prefab for its spawn point, that paired native prefab is tried before the generic Gen2 candidate list.
- In Gen2 mode, legacy spawn-group preferred prefabs no longer jump ahead of the configured Gen2/native prefab candidates.
- The known working legacy fallback `scientistnpc_junkpile_pistol.prefab` is now explicitly included after the Gen2/native candidates.
- The moving test setup now uses a 45m-180m near-player window, more native spawn-point attempts, and generated near-player fallback positions so spawning is less brittle.
- Legacy NPCPlayer fallback bots now move toward shorter 18m-45m nav-sampled hops toward the anchor instead of one long direct destination.
- Legacy movement kicks now force BaseNavigator destination recalculation with updateInterval=0 and log npcCommand, navCommand, navPath, dormant, stuck, navType, destination, and AI convars.
- Legacy fallback combat kicks now seed the old scientist brain senses/memory with the nearest real player, call the generic IAIAttack interface, and log attackStarted plus combat diagnostics.
- Legacy fallback movement is driven every 2 seconds while enabled, and the final navigator command now runs after the combat nudge so attack setup does not clear the movement path.
- raidbots.testsetup is wrapped with a plugin warning so a future setup failure reports raidbots.testsetup failed: <exception>.
- raidbots.status now reports AI mode, native/legacy active counts, legacy fallback, native kit behavior, and native roam prefab group count.
- raidbots.diag and raidbots.list now include entity type, AI mode, Gen2 component presence, nav status, target status, and prefab.
- Stats file shape is unchanged: oxide/data/RaidlandsRoamBots/stats.json still has players and bots, so WebsiteVipBridge does not need a contract change.

Recommended live RCON order:
- raidbots.disable
- raidbots.killall
- Upload oxide/plugins/RaidlandsRoamBots.cs
- Upload oxide/config/RaidlandsRoamBots.json
- oxide.reload RaidlandsRoamBots
- raidbots.testsetup ababmxking
- raidbots.diag
- raidbots.enable 1
- Wait 1 second for the delayed Gen2 AI kick.
- raidbots.list ababmxking
- raidbots.goto ababmxking 1

Expected verification:
- After reload while disabled: enabled=False and active=0.
- raidbots.status should show ai=gen2_native, target=1, active=0 before enable, legacyFallback=True, and gen2Kits=False.
- raidbots.diag may show activePrefabs beginning with the native spawn-group prefab when the selected spawn point provides one, followed by the Gen2/naval scientist candidates.
- After `raidbots.testsetup`, `raidbots.diag` should show activePrefabs beginning with `scientist2.prefab`, not `scientistnpc_junkpile_pistol.prefab`.
- After raidbots.enable 1, raidbots.list should show a land position and diagnostics like ai=gen2_native, gen2=True, nav=gen2:unity=on,sample=on when the native Gen2 path succeeds.
- Current live evidence suggests mainland Gen2 may continue to fail with "No navmesh areas matching agent type"; in that case the expected fallback is `ai=legacy_scientist` using `scientistnpc_junkpile_pistol.prefab`.
- If Gen2 cannot attach to the mainland navmesh, fallback should show ai=legacy_scientist and target=legacy:path after the movement kick.
- The bot should move, acquire or react to players, and no longer stand inert.
- If it moves but does not shoot, paste the Legacy move command lines plus raidbots.list output; the important fields are attackStarted, combat, canAttack, inRange, ammo, held, threat, senseTarget, and best.
- If it still stands inert, paste the Legacy move command lines plus raidbots.list output; the important fields are navCommand, navPath, aiMove, aiNavthink, unityNav, and navDisabled.
- If diagnostics show sample=on but unity=off, the Rust-side sample is valid but the internal Unity NavMeshAgent is not actually placed; paste the delayed Gen2 AI kick line and any spawn warnings.
- Killing the bot should increment the bot death count and player npc_kills in oxide/data/RaidlandsRoamBots/stats.json.
- If Gen2 fails on the live map, legacy fallback may still spawn; raidbots.list will clearly show ai=legacy_scientist.
- If active remains 0, paste the raidbots.diag output plus the spawn log lines after raidbots.enable 1.

Relevant reloads:
- oxide.reload RaidlandsRoamBots
- Optional after behavior verification: oxide.reload WebsiteVipBridge to push the next scheduled stats snapshot sooner.
