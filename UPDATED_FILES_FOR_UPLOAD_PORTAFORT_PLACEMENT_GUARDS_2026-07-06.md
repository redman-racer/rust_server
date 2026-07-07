Raidlands Portafort placement guards - 2026-07-06

What changed:
- RaidlandsPortaforts is now v1.2.7.
- Inventory item drops no longer deploy Portaforts.
- Portafort placement now checks the saved CopyPaste footprint before paste.
- Placement samples the footprint center, corners, and edge midpoints for basic world-surface restrictions.
- Placement is rejected on roads.
- Placement is rejected inside or too close to monuments.
- Placement is rejected in water.
- Placement is rejected if the footprint overlaps existing construction, deployables, or large vehicles.
- Placement is rejected if an active or sleeping player is inside the Portafort footprint.
- Failed guarded placement returns a chat message before the token is consumed.
- Existing CopyPaste args still keep blockcollision at 0, preserving the earlier fix for false "Something is blocking the paste" failures.

Rust server plugin/config files to upload:
- oxide/plugins/RaidlandsPortaforts.cs
- oxide/config/RaidlandsPortaforts.json

Docs/reference files to upload or keep with the repo handoff:
- UPDATED_FILES_FOR_UPLOAD_PORTAFORT_PLACEMENT_GUARDS_2026-07-06.md

Reload after upload:
- oxide.reload RaidlandsPortaforts

Smoke checks:
- Drop a Portafort Token from inventory. Expected: it remains a dropped item and does not deploy.
- Use /portafort while aiming at open ground or rocky open terrain. Expected: Portafort deploys normally and consumes one token.
- Use /portafort while aiming at a road. Expected: placement is rejected with a road message and the token is not consumed.
- Use /portafort near a named monument such as gas station, supermarket, launch site, harbor, trainyard, or outpost/bandit. Expected: placement is rejected with a monument message and the token is not consumed.
- Use /portafort while aiming at water/submerged terrain. Expected: placement is rejected with a water message and the token is not consumed.
- Use /portafort while aiming at or inside a player-built structure. Expected: placement is rejected with a building/deployable/vehicle message and the token is not consumed.
- Use /portafort or throw the throwable token at a spot occupied by a player or sleeper. Expected: placement is rejected with a player-clearance message; thrown-token failures refund the token.

Local verification:
- oxide/config/RaidlandsPortaforts.json parses successfully.
- Roslyn compile check passed for oxide/plugins/RaidlandsPortaforts.cs v1.2.7 against RustDedicated_Data/Managed with Oxide.References.dll and glTFast.Newtonsoft.dll excluded.
- Remaining compile warnings are expected Oxide plugin-reference/config-field warnings.
- Live Oxide reload and the smoke steps above are still required to prove in-game runtime behavior.
