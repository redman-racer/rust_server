Portable Airstrikes default-off MLRS executor - 2026-07-08

Review result:
- The generic Airstrike Authorization item is sound.
- It is not a custom prefab that still needs to be made.
- The plugin creates a normal Rust item with:
  - shortname: `targeting.computer`
  - display name: `Airstrike Authorization Key`
  - skin: configured `AirstrikeItem.SkinId`
- Admin grants and loot injection both use the same `CreateAirstrikeToken` path, so loot-generated tokens should validate the same way as `/strike giveitem` tokens.

What changed:
- Updated `PortableAirstrikes` to v0.1.10.
- Added root config key `ConfigVersion`.
- Added a one-time safety reset for old/generated configs:
  - if `ConfigVersion < 10`, the plugin disables `homing_heli`, `homing_jet`, `mini_mlrs`, and `full_mlrs`
  - after save, `ConfigVersion` becomes `10`
  - later deliberate manual opt-in can persist
- Changed default `homing_heli`, `homing_jet`, and `mini_mlrs` definitions to disabled by default.
- Added a disabled-by-default MLRS executor for existing `mini_mlrs` and `full_mlrs` definitions.
- Added executor route:
  - `cargo_plane_jet:mlrs_rocket`
- MLRS execution uses the real Rust prefab:
  - `assets/content/vehicles/mlrs/rocket_mlrs.prefab`
- MLRS count uses configured `RocketCount`, capped by `MaxCount` and an executor hard cap of `24`.
- MLRS spread uses configured `SpreadRadius`.
- Homing missiles remain deferred.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: F683681B993689BDFFD2C355B99DE542E0DB47C00553A15A22FB0B52B7A0AE18

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_MLRS_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Config upload:
- No config upload is required for the safe/default behavior.
- First reload will save `ConfigVersion=10` and disable old generated experimental entries.
- To test `mini_mlrs`, reload once first, then edit `oxide/config/PortableAirstrikes.json`:
  - set `StrikeDefinitions.mini_mlrs.Enabled=true`
  - keep `full_mlrs.Enabled=false` until mini is proven
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

Expected default behavior:
- The plugin compiles and loads as v0.1.10.
- `homing_heli`, `homing_jet`, `mini_mlrs`, and `full_mlrs` are disabled after the first reload unless deliberately re-enabled.
- The CUI picker and direct commands still use the same RP/token/cooldown pipeline.
- The Airstrike Authorization item remains `targeting.computer` with the configured display name/skin.

Optional MLRS smoke after deliberate config opt-in:
- Grant basic use: `oxide.grant group default portableairstrikes.use`
- Grant mini MLRS: `oxide.grant user <steamId> portableairstrikes.use.mlrs.mini`
- Give test tokens: `portableairstrikes.giveitem <playerNameOrSteamId> 5`
- `/strike debugping` at a safe open test area
- `/strike mini_mlrs`

Expected MLRS behavior:
- RP/token/cooldown validation happens before launch.
- Configured `RocketCount` MLRS rockets launch with staggered timing.
- Impact points are randomized inside the configured `SpreadRadius`.
- If execution fails before the first impact starts, the existing refund/token restore/cooldown clear path applies.

Known unresolved verification points:
- Live Rust/uMod still needs confirmation that binocular/team pings fire the expected hook on this server build.
- The admin `/strike debugping` path remains the verified test path until live ping behavior is confirmed.
- Loot injection is locally compiled but still needs live container smoke testing.
- MLRS is locally compiled and prefab-backed, but still needs live smoke testing before public use.
- Homing missiles, aircraft movement, map markers, blocked monument rules, and broader zone controls remain deferred.

Local verification:
- Confirmed `assets/content/vehicles/mlrs/rocket_mlrs.prefab` exists in `Bundles/AssetSceneManifest.json`.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
