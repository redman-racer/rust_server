Portable Airstrikes visual delivery flyovers and artillery source - 2026-07-08

What changed:
- Updated `PortableAirstrikes` to v0.1.18.
- Added root config key `ConfigVersion=18`.
- Added `DeliveryVisuals` config:
  - `Enabled`
  - `SpawnDroneVisuals`
  - `SpawnAircraftVisuals`
  - `SpawnMortarArtilleryVisuals`
  - `SpawnMortarCrewNpc`
  - `DroneFlyoverDistance`
  - `DroneFlyoverHeight`
  - `AircraftFlyoverDistance`
  - `AttackHeliFlyoverHeight`
  - `CargoPlaneFlyoverHeight`
  - `A10FlyoverHeight`
  - `MortarSourceDistance`
  - `MortarCrewOffset`
  - `VisualMoveIntervalSeconds`
- Drone-drop strikes now spawn a temporary scripted drone flyover while staggered payloads drop.
- Heavy drops, rocket runs, MLRS, homing strikes, and A-10 strafes now spawn temporary scripted aircraft flyovers using attack-heli or cargo-plane visuals based on delivery type.
- Mortar strikes now spawn a temporary mortar source, play mortar deploy/muzzle effects, and can spawn an optional temporary `Raidlands Artillery` NPC crew visual.
- Visual entities are tracked separately from payload entities and are removed on completion, failure, cancel, or plugin unload.
- Visual spawn failures log warnings and increment stats, but they do not block strike validation, charging, payload execution, refunds, cooldowns, or cleanup.
- Existing direct/CUI validation, RP/token/cooldown/refund behavior, payload timing/damage, warning fanout, heavy warning markers, audit/webhook behavior, default-disabled homing/MLRS gates, loot behavior, and monument blocking behavior were left unchanged.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: 5EAC0F32D97966D8C434D24E8FE4DCD19B7ED2D0BEC40A16050ADD1397DA1586

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_VISUALS_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Config upload:
- No config upload is required for safe/default behavior.
- Reloading will save `ConfigVersion=18` and add the `DeliveryVisuals` section.
- To disable all visuals quickly, set:
  - `DeliveryVisuals.Enabled=false`
- If the temporary mortar crew NPC visual behaves noisily live, set:
  - `DeliveryVisuals.SpawnMortarCrewNpc=false`

Reload after upload:
- oxide.reload PortableAirstrikes

Safe visual smoke:
- `oxide.reload PortableAirstrikes`
- `/strike debugping`
- `/strike beancan_drop`
- `/strike debugping`
- `/strike a10_strafe`
- `/strike debugping`
- `/strike mortar_he`
- `/strike debug history 5`
- `/strike debug stats`

Expected:
- Plugin banner reports v0.1.18.
- `beancan_drop` shows a temporary drone moving across the target area while payloads drop.
- `a10_strafe` shows a temporary cargo-plane/A-10 stand-in crossing the target area and then disappearing.
- `mortar_he` shows a temporary mortar source, muzzle effects, and optionally a `Raidlands Artillery` NPC crew visual near the source.
- Visuals clean up after completion/failure/cancel/unload.
- Payloads still complete normally and are not killed early by visual cleanup.
- `/strike debug stats` may show `visual_spawned*` counters after visual strikes.

Optional MLRS/homing visual smoke:
- Keep MLRS/homing default-disabled unless deliberately testing them.
- If `mini_mlrs` is already enabled for smoke, run `/strike mini_mlrs` in a safe open area and confirm the cargo-plane visual plus existing MLRS rockets.
- If `homing_heli` is deliberately enabled for a safe vehicle test, confirm the attack-heli visual plus existing homing missile launch/tracking.

Known unresolved verification points:
- Live client-side movement/visibility of the scripted visual entities still needs in-game smoke.
- Temporary aircraft entities are plugin-moved visual props, not autonomous aircraft combat logic.
- The optional mortar crew NPC has player-target sensing suppressed, but live Rust AI behavior should still be watched during the first reload/smoke.
- Heavy warning marker size/visibility, nearby warning radius/noise, and recipient-side chat visibility still need live tuning with other players online.
- Live Rust/uMod still needs confirmation that binocular/team pings include vehicle entity IDs on this server build.
- Homing missile tracking/damage remains locally compiled but should stay gated until a safe vehicle test is available.
- Native projectile/grenade/shell damage attribution still depends on Rust/uMod behavior, though spawned payload entities set `OwnerID` and creator entity.
- Loot injection live tuning/smoke is intentionally deferred until the end.

Local verification:
- Confirmed visual prefabs exist in `Bundles/AssetSceneManifest.json` for drone, attack helicopter, cargo plane, mortar source, mortar deploy/muzzle effects, and scientist NPC crew.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- Direct trailing-whitespace scan passed for `oxide/plugins/PortableAirstrikes.cs`, `Docs/AirStrikes/portable_airstrikes_development_log.md`, `UPDATED_FILES_FOR_UPLOAD.txt`, and this upload note.
