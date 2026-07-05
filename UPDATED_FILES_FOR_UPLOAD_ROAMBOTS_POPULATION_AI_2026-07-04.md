Raidlands Gen2 native land roam bots rollout - 2026-07-04

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
