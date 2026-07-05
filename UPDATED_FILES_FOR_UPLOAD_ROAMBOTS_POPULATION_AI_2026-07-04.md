Raidlands roam bots population, spawn-mode, safe-zone, test-anchor, land-spawn, nav-diagnostic, runtime-toggle, and native-prefab fix - 2026-07-04

Context:
- Live status showed RaidlandsRoamBots enabled=False, target=15, active=130.
- That means disabled state was not equivalent to no active bots, and the active bot count was far above the intended target.
- The old config also preferred native NPC spawn groups and native spawn-group prefabs, which can cluster bots at road/monument NPC points and inherit unsuitable native NPC prefab behavior.
- The current testing direction is to use Rust's newer underwater/deep-sea NPC prefab and spawn bots near connected players.
- v0.1.12 live testing showed generated near-player positions were not valid for the NPC navigator: "Agent still not on navmesh after a warp."
- v0.1.13 live testing showed native near-player spawn points could cluster bots at Outpost/safe zones.
- v0.1.14 live testing still failed navigator placement and needs smaller, targeted tests around one player.
- v0.1.15 live testing spawned one active bot, but raidbots.goto moved the tester to deep water and no visible land NPC was present.
- v0.1.16 live testing found valid land scientist prefabs still fail the synchronous navigator placement gate before becoming active.
- v0.1.17 live testing showed the live config still had Require NPC Navigator Placement=True, so the relaxed navigator test was not actually running.
- v0.1.18 live testing proved navcheck off allows stationary bots, while navcheck on prevents the current generic scientist prefab from becoming active.

Rust server files to upload:
- oxide/plugins/RaidlandsRoamBots.cs
- oxide/config/RaidlandsRoamBots.json

Important behavior changes:
- RaidlandsRoamBots is now v0.1.19.
- The test prefab list now uses land scientist prefabs first and removes the underwater dweller from the active testing config.
- Spawn mode is now configurable with two saved values: near_players and random.
- The default/test config uses near_players with native NPC spawn points 80m-300m from connected players.
- Generated near-player positions are disabled by default because they caused navigator placement failures.
- raidbots.enable, raidbots.target, and raidbots.mode clear the spawn retry timer so failed-test cooldowns do not block the next test.
- Disabled config reload now despawns active roam bots.
- Enabled maintenance trims active bots above the configured target population.
- The target population is treated as the total active bot cap, and deaths/leashes schedule maintain so bots refill back to target.
- raidbots.spawn no longer spawns bots while the plugin is disabled.
- Manual raidbots.spawn is capped by remaining target-population room.
- Native NPC spawn groups and native spawn-group prefabs are disabled in config by default.
- Native spawn-group fallback now uses public SpawnGroup fields instead of protected Rust methods.
- Safe-zone spawn filtering is enabled by default and rejects spawn candidates inside or near safe zones.
- Players in safe zones are ignored as near-player spawn anchors and ignored as roam-bot combat targets.
- Bot damage against a real player currently in a safe zone is blocked.
- Near-player mode can now be anchored to one player by name or SteamID with raidbots.anchor.
- The testing config is anchored to ababmxking, target population 1, minimum 1, maximum 3, and solo-only bot teams.
- Spawn failure retry is reduced to 30 seconds for faster test loops.
- The missing assets/prefabs/npc/scientist/underwaterlabscientist.prefab candidate was removed to keep live test logs focused on real prefabs.
- Land-spawn filtering is stricter: WaterLevel checks, ocean/deep-water checks, and a final post-navigator position check prevent active bots from being accepted underwater.
- When land spawns are required, underwaterdweller and tunneldweller prefabs are skipped even if an older live config still contains them.
- The moving test config now sets Require NPC Navigator Placement=true and Prefer Native Spawn Group Prefabs=true.
- raidbots.testsetup now applies the moving-NPC profile: one anchored bot, native spawn-group prefabs, land filtering, navcheck on, and debug on.
- Native preferred prefabs are filtered so tunnel and underwater NPC prefabs are skipped while land spawns are required.
- Debug Spawn Details is enabled for this test pass.
- New admin command: raidbots.diag
- New admin command: raidbots.testsetup [player name or steam id]
- New admin command: raidbots.navcheck on|off
- New admin command: raidbots.debug on|off
- New admin command: raidbots.land on|off
- New admin command: raidbots.nativeprefabs on|off
- New admin command: raidbots.target <count>
- New admin command: raidbots.mode near_players
- New admin command: raidbots.mode random
- New admin command: raidbots.anchor <player name or steam id>
- New admin command: raidbots.anchor clear

Recommended live RCON order:
- raidbots.disable
- raidbots.killall
- Upload oxide/plugins/RaidlandsRoamBots.cs
- Upload oxide/config/RaidlandsRoamBots.json
- oxide.reload RaidlandsRoamBots
- raidbots.status
- raidbots.testsetup ababmxking
- raidbots.diag
- raidbots.enable 1
- Wait 30 seconds, then run raidbots.status again.
- If the first bot spawns and behaves, raise slowly with raidbots.target 2, then raidbots.target 3.

Expected verification:
- After reload while disabled: enabled=False, active=0.
- Status should show mode=near_players, anchor=ababmxking, target=1, and near-player anchors=1 if ababmxking is connected, awake, and outside safe zones.
- Near-player anchors should not include players who are standing in Outpost, Bandit Camp, or another safe zone.
- raidbots.diag should show requireNavigator=True, requireLand=True, activePrefabs beginning with the native preferred prefab when a matching spawn group is found, then print anchor/candidate terrain-water-safe-zone details.
- After raidbots.enable 1, active should settle at or below 1 while the anchor player is eligible.
- raidbots.list ababmxking should show the bot at a land/road/monument position, not a negative-Y deep-water position.
- If no real players are connected, near_players mode should wait without spawning instead of falling back to random.
- If active remains 0, check whether ababmxking is within 300m of any native NPC spawn point; for testing, move near roads/monuments or temporarily raise Near Player Maximum Distance.
- If active remains 0 while everyone is at Outpost/Bandit, move one tester out of the safe zone and run raidbots.status again.
- If active is higher than 15 after enable, maintenance should log a trim message and bring it down on the next maintain tick.
- Watch console for "Failed to compile: RaidlandsRoamBots" after reload; the patched file passed a local compile check against RustDedicated_Data/Managed assemblies.
