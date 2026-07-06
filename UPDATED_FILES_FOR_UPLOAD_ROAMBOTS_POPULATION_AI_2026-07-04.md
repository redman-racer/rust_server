Raidlands roam bots killfeed, quiet-console, and clan-war addendum - 2026-07-05

Latest addendum:
- RaidlandsRoamBots is now v0.3.34.
- Kill chat now includes weapon/method/distance using the configured template tokens `{weapon}`, `{method}`, and `{distance}`.
- Bot kills against real players now broadcast to chat, not only real-player kills against bots.
- Different-clan bot-vs-bot kills are allowed and broadcast through the same killfeed; same-clan bot damage remains blocked.
- DeathNotes suppression now applies to any death involving a tracked roam bot to avoid duplicate generic NPC/scientist messages.
- Debug UI and debug console output are separated. Nameplates/side panel can be enabled without console flooding; detailed console debug requires `Debug -> Debug Console Logs`.
- Repeated spawn/kit warnings are throttled by `Debug -> Console Warning Cooldown Seconds`.
- Local Roslyn compile check against `RustDedicated_Data/Managed` passed for `oxide/plugins/RaidlandsRoamBots.cs` v0.3.34. Remaining warnings are expected plugin-reference/future-field warnings.
- Confirm the live console loads RaidlandsRoamBots v0.3.34 after upload/reload.

Previous admin panel and high-cap live defaults update - 2026-07-05

Current update:
- RaidlandsRoamBots is now v0.3.33.
- The checked-in config is now staged for live release: disabled on reload, target=50, min=0, max=200.
- Added the in-game `/raidbots admin` CUI, backed by `raidbots.ui`, with tabs for overview, population, spawn, AI, utility, rewards, advisor, debug, and danger controls.
- The panel can manage enable/disable, population caps, manual spawn batches, spawn mode/fallback/anchor, tactical toggles, utility caps/cooldowns, bot reward/RP behavior, advisor modes, debug surfaces, config reloads, and emergency cleanup.
- Code defaults now match the staged high-cap live config for target/max population, near-player spawn tuning, random fallback, nav sample distance, retry timing, and solo/duo/trio weights.
- The tester-only near-player anchor is cleared, so live spawning uses any active non-safe-zone player as the anchor source.
- Random land fallback remains disabled so failed/no player-adjacent spawning does not silently become arbitrary map spawning.
- Team weights are back to a normal live mix: solo=55, duo=35, trio=10.
- Debug spawn details, tactical/perception logs, overhead nameplates, and side panel are off by default.
- Decision advisor is off/fallback-only by default for release play. Deterministic local tactics run without any external advisor or API key unless the owner explicitly opts in later.
- Foliage LOS release defaults now use a narrower `0.65m` cast, require `2` foliage blockers, and allow `24m` clear-through distance. This supersedes the older `0.9m` / `1 blocker` test tuning.
- Added player-like roam bot kill chat: when a real player kills a tracked roam bot, chat now says `{killer} killed [TAG] BotName` instead of letting the bot read like a generic scientist.
- Added DeathNotes suppression for tracked roam bot deaths so the server does not double-post both the new player-like bot kill line and the old NPC/scientist death line.
- Added ServerRewards RP payout for real-player roam bot kills. The live config currently awards `5 RP` per bot kill and sends the killer a short RP confirmation message.
- Added `Bot Kill Integration` config knobs for chat format, kill message text, DeathNotes suppression, ServerRewards RP enablement, RP amount, and the killer-only RP reward notice.
- Added non-blocking HTTP decision advisor adapters for `openai_compatible` and `website_proxy` providers using Oxide `webrequest.Enqueue`.
- Added compact advisor request payloads containing bot state, tactical context, and precomputed legal candidate actions.
- Added OpenAI-compatible chat-completions request bodies with configurable structured schema output, plus direct website-proxy request bodies for a future Raidlands-side advisor endpoint.
- Added response parsing and validation for direct proxy decisions and OpenAI-compatible `choices[0].message.content` JSON.
- Advisor responses are rejected on invalid JSON, empty/oversized responses, unknown action ids, low confidence, excessive TTL, late candidate expiry, invalid/dead/sleeping/disconnected targets, or movement destinations inside base-restricted areas.
- Remote advisor responses are trace/validation only in this pass. The deterministic heuristic action still executes in `fallback_only`, `shadow`, and `canary` modes.
- Added pending-request caps, timeout pruning, advisor result stats, richer JSONL trace fields, and `raidbots.advisor status|off|fallback|shadow|canary|stats|last [bot]`.
- RoamBots can resolve an OpenAI advisor key from ignored `oxide/config/Secrets.local.json` using `${OPENAI_ROAM_BOT_API_KEY}` if advisor plumbing is intentionally configured later.
- The checked-in advisor config now uses `Enabled=false`, `Provider=none`, and `Mode=fallback_only`.
- Added config knobs:
  - `Bot Kill Integration -> Broadcast Player-Like Kill Messages`
  - `Bot Kill Integration -> Suppress DeathNotes For Roam Bot Kills`
  - `Bot Kill Integration -> Chat Format`
  - `Bot Kill Integration -> Kill Message`
  - `Bot Kill Integration -> Award ServerRewards RP`
  - `Bot Kill Integration -> RP Reward Per Bot Kill`
  - `Bot Kill Integration -> Tell Killer About RP Reward`
  - `Bot Kill Integration -> RP Reward Message`
  - `Decision Advisor -> Use Structured Response Schema`
  - `Decision Advisor -> Max Advisor Response Bytes`
- Local Roslyn compile check against `RustDedicated_Data/Managed` passed for `oxide/plugins/RaidlandsRoamBots.cs` v0.3.33. Remaining warnings are expected plugin-reference/future-field warnings.

Live Oxide files to upload for this update:
- oxide/plugins/RaidlandsRoamBots.cs
- oxide/config/RaidlandsRoamBots.json

Local handoff/docs files updated:
- oxide/config/Secrets.example.json
- Docs/RaidlandsRoamBots_Tactical_Rewrite_Plan_v2_LLM.md
- UPDATED_FILES_FOR_UPLOAD.txt
- UPDATED_FILES_FOR_UPLOAD_ROAMBOTS_POPULATION_AI_2026-07-04.md

Optional live ignored secret file for later advisor testing:
- oxide/config/Secrets.local.json
- Only add `OPENAI_ROAM_BOT_API_KEY` if intentionally testing advisor shadow mode. Do not put the real key in tracked configs, upload manifests, docs, chat, or Git.

Recommended live RCON order for this update:
- Upload `oxide/plugins/RaidlandsRoamBots.cs`
- Upload `oxide/config/RaidlandsRoamBots.json`
- `oxide.reload RaidlandsRoamBots`
- Confirm the live console loads RaidlandsRoamBots v0.3.33.
- In game, run `/raidbots admin`.
- Expected: the admin panel opens and shows disabled, target=50, max=200, mode=near_players, anchor=all.
- `raidbots.diag`
- Expected before enable: `enabled=False`, `target=50`, `anchor=all`, and `advisor=none/fallback_only`.
- `raidbots.enable 3`
- `raidbots.list`
- If the first three bots look healthy, ramp with `raidbots.target 25`, then `raidbots.target 50`.
- Kill a roam bot with a real player account.
- Expected: chat shows a DeathNotes-styled line like `PlayerName killed [NR] LaunchLoot.` and does not also print the generic scientist/NPC death line.
- Expected: the killer receives `You earned 5 RP for killing [TAG] BotName.` and their ServerRewards balance increases by 5.
- `raidbots.advisor status`
- Expected release default: provider is `none`, mode is `fallback_only`, enabled is `False`, apiKey is `not_used`, pending is `0/2`, and no plaintext key is printed.
- Optional later advisor smoke: configure provider/endpoint/model/API key, run `raidbots.reload`, then `raidbots.advisor shadow`, fight until a hard decision trigger fires, and inspect `raidbots.decisions last 5` / `raidbots.advisor stats`.

Previous v0.3.26 stuck-memory resilience update:
- RaidlandsRoamBots is now v0.3.26.
- Bots now remember recently failed movement destinations instead of repeatedly selecting the same blocked or no-progress point.
- Failed blocked destinations, base-blocked paths, failed navigator commands, and repeated stuck/no-progress destinations are added to a per-bot bad-spot memory.
- Recovery, cover, peek, retreat, investigate, flank, regroup, push, and roam destination sampling now avoid remembered bad spots until the memory expires.
- `raidbots.list` now includes `badspots=...`, and the right-side debug panel now shows `Bad spots: <count> (<last reason>)`.
- Bot skill health now stays in the player-like `100-120` HP band: casual `100`, average `110`, dangerous `120`.
- Old high-health skill configs are normalized on plugin load, so stale `125/150/190` HP values should not recreate the endless hide-to-heal loop.
- Bot healing fractions now use the effective Rust entity max health, so a body capped around `120` HP will not keep trying to heal toward an impossible `190`.
- Bot outgoing and incoming damage are no longer scaled by skill tier or poor-range damage multipliers. Skill still affects aim, reaction, courage, and tactics; landed hits use normal Rust damage.
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
  - `AI -> Stuck Memory Seconds`
  - `AI -> Stuck Memory Radius`
  - `AI -> Maximum Stuck Memory Points`

Rust server files to upload for this update:
- oxide/plugins/RaidlandsRoamBots.cs
- oxide/config/RaidlandsRoamBots.json

Recommended live RCON order for this update:
- Upload `oxide/plugins/RaidlandsRoamBots.cs`
- Upload `oxide/config/RaidlandsRoamBots.json`
- `oxide.reload RaidlandsRoamBots`
- `raidbots.list ababmxking`
- Confirm spawned bots report HP caps in the `100-120` range in the side panel / `raidbots.list` instead of `125/150/190`.
- Recheck the jungle/forest sight line from the screenshot at 70m-120m.
- Shoot a bot after it has placed its first barricade; once the `12s` cooldown opens, it should be eligible to place another barricade if it is still exposed or its current wall is not effective cover.
- Test a 40m-60m fight while the bot is taking worse trades. If hard cover is within the skill-scaled 3m-8m nearby window, it should move there and heal; if cover is farther, it should prefer barricade first.
- Put a cliff, large rock, wall, or base edge between the bot and a likely retreat/search/cover route. Once it fails to make progress, `badspots` should increment and the next destination should avoid that remembered area for `AI -> Stuck Memory Seconds`.
- Break LOS around 12m-42m and hold near cover/last-known position. A bot may choose `throw_grenade`; squadmates should move out of the danger zone instead of standing in the blast lane.
- Hurt a bot while it is exposed around 10m-55m. A bot may choose `throw_smoke`, then retreat through the screen instead of pushing.
- `raidbots.list ababmxking`

Expected verification for this update:
- Through dense jungle/forest cover, the side panel should stop showing `LOS: Y` with `Exposure: 1.00 (6/6)`.
- Expected good outcomes are either `LOS: N` with `Sight: foliage ...`, or a reduced exposure count below the shoot threshold.
- In a cleaner lane with only light brush or a short gap, bots can still reacquire once enough target probes are genuinely visible.
- A repeatedly pressured bot should show a shorter barricade cooldown and should not lose the second wall opportunity just because the original damage reaction window expired.
- A healing bot should avoid pushing/flanking until close to full health, but should still shoot if the player closes distance during the heal process.
- A dangerous bot that reaches about `120` HP should leave `cover_heal` / hide-to-heal behavior instead of waiting for an impossible high-health target.
- Player-vs-bot and bot-vs-player hits should feel like normal Rust damage, with no casual damage penalty, dangerous damage bonus, incoming-damage handicap, or poor-range damage debuff from this plugin.
- Utility playtests should show concrete `Utility:` reasons in the panel. Expected blockers include `utility_cd`, `team_cd`, `grenade_range`, `grenade_ally_close`, `grenade_bystander_close`, `utility_base_blocked`, or `utility_cap`.
- Stuck/pathing playtests should show `badspots=...` in `raidbots.list` and `Bad spots:` in the side panel after a failed destination. The count should decay after the configured memory window.
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
