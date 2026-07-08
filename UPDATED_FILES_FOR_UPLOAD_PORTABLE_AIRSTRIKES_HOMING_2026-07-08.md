Portable Airstrikes default-off homing missile executor - 2026-07-08

What changed:
- Updated `PortableAirstrikes` to v0.1.12.
- Added root config key `ConfigVersion=12`.
- Added a default-disabled homing missile executor for `homing_heli` and `homing_jet`.
- Added executor routes:
  - `attack_heli:homing_missile`
  - `cargo_plane_jet:homing_missile`
- Homing missiles use HV rocket projectile visuals:
  - prefab: `assets/prefabs/ammo/rocket/rocket_hv.prefab`
  - missile count from `MissileCount`, capped by `MaxCount` and an executor hard cap of `8`
  - tracking time from `MaxTrackingSeconds`
  - tracking distance from `MaxTrackingDistance`
  - proximity damage using `VehicleDamageScale`, `SplashRadius`, global damage scales, and strike damage scales
- Vehicle-only strikes now require a tracked target entity ID during validation, so invalid vehicle targets fail before RP charge, token consume, or cooldown start.
- `homing_heli` and `homing_jet` remain disabled by default; uploading/reloading this build does not make them public.
- Loot injection was left untouched and remains deferred until the end.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: E0F58FA0614A1E1F3698156AB855A905A2111852CD116E83EEDEB9C5F780718F

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_HOMING_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Config upload:
- No config upload is required for the safe/default behavior.
- First reload will save `ConfigVersion=12` while keeping `homing_heli`, `homing_jet`, `mini_mlrs`, and `full_mlrs` disabled unless the live config already deliberately enabled them after the v0.1.10 safety reset.
- To test `homing_heli`, edit `oxide/config/PortableAirstrikes.json` after reload:
  - set `StrikeDefinitions.homing_heli.Enabled=true`
  - keep `homing_jet` disabled for the first smoke pass
  - reload `PortableAirstrikes` again

Reload after upload:
- oxide.reload PortableAirstrikes

Safe regression smoke:
- `oxide.reload PortableAirstrikes`
- `/strike debugping`
- `/strike`
- `/strike beancan_drop`
- `/strike debugping`
- `/strike a10_strafe`

Homing opt-in smoke:
- Enable `StrikeDefinitions.homing_heli.Enabled=true`, then reload.
- Grant the test player:
  - `portableairstrikes.use`
  - `portableairstrikes.use.homing.heli`
- Give the test player Airstrike Authorization items with `/strike giveitem <playerNameOrSteamId> [amount]`.
- Aim directly at a live vehicle in a safe open area and run `/strike debugping`.
- Expected target debug: vehicle ping with an entity short prefab/name and network ID.
- Run `/strike`.
- Expected picker behavior: vehicle-compatible `homing_heli` appears if permission/RP/token/cooldowns pass.
- Run `/strike homing_heli`.
- Expected strike behavior: configured missile count launches, tracks the vehicle within configured time/distance, and detonates near the live vehicle target.

Expected default behavior:
- The plugin compiles and loads as v0.1.12.
- Existing direct/CUI strike flow still charges RP and consumes one token exactly once.
- Loot behavior is unchanged.
- Monument blocking remains disabled until `General.BlockMonuments=true`.
- Default-disabled `homing_heli`, `homing_jet`, `mini_mlrs`, and `full_mlrs` stay gated by config.

Known unresolved verification points:
- Live Rust/uMod still needs confirmation that binocular/team pings include vehicle entity IDs on this server build.
- The admin `/strike debugping` path remains the verified vehicle-target smoke path until live ping behavior is confirmed.
- Homing missile tracking and damage are locally compiled but still need live vehicle smoke before public use.
- Loot injection live tuning/smoke is intentionally deferred until the end.
- MLRS has passed initial live smoke but still needs final public-balance tuning.

Local verification:
- Confirmed the HV rocket projectile prefab exists in `Bundles/AssetSceneManifest.json`.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
