Portable Airstrikes opt-in monument blocking - 2026-07-08

What changed:
- Updated `PortableAirstrikes` to v0.1.11.
- Added root config key `ConfigVersion=11`.
- Added opt-in monument blocking settings under `General`:
  - `BlockMonuments`
  - `BlockMonumentsForHeavyStrikesOnly`
  - `MonumentBlockRadiusPadding`
  - `DefaultMonumentBlockRadius`
  - `BlockedMonumentNames`
- `BlockMonuments` defaults to `false`, so upload/reload does not change current strike behavior until deliberately enabled.
- When enabled, monument blocking runs before RP charge, token consume, and cooldown start.
- `BlockMonumentsForHeavyStrikesOnly` defaults to `true`, so configured monument blocks affect heavy strikes unless changed.
- Added `/strike debug monument` and `/strike debug safety` to report whether the current target is inside a configured blocked monument zone.
- Loot injection was left untouched and remains deferred until the end.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: C26FC8E307D3617E5EFBEBFD93808C792C67407FE33F4936C009E14570B0D6D7

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_MONUMENTS_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Config upload:
- No config upload is required for the safe/default behavior.
- First reload will save the new `General` monument-blocking keys with `BlockMonuments=false`.
- To test monument blocking later, edit `oxide/config/PortableAirstrikes.json` after reload:
  - set `General.BlockMonuments=true`
  - keep `General.BlockMonumentsForHeavyStrikesOnly=true` for the first test pass
  - keep the default `BlockedMonumentNames` list or narrow it to one known test monument
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

Monument debug smoke:
- Use `/strike debugping` while aiming near a configured monument.
- Run `/strike debug monument`.
- Expected while `General.BlockMonuments=false`: the command can report a matched monument zone, but normal strike validation is unchanged.
- Optional enabled test: set `General.BlockMonuments=true`, reload, target a configured monument, and try a heavy strike such as `/strike a10_strafe`.
- Expected enabled test result: the heavy strike is rejected before RP/token/cooldown changes with a blocked-monument message.

Expected default behavior:
- The plugin compiles and loads as v0.1.11.
- Existing direct/CUI strike flow still charges RP and consumes one token exactly once.
- Loot behavior is unchanged because this pass does not modify or enable loot injection.
- Monument blocking remains disabled until `General.BlockMonuments=true`.
- Default-disabled `homing_heli`, `homing_jet`, `mini_mlrs`, and `full_mlrs` stay gated by config.

Known unresolved verification points:
- Live Rust/uMod still needs confirmation that binocular/team pings fire the expected hook on this server build.
- The admin `/strike debugping` path remains the verified test path until live ping behavior is confirmed.
- Loot injection live tuning/smoke is intentionally deferred until the end.
- MLRS has passed initial live smoke but still needs final public-balance tuning.
- Homing missiles, aircraft movement, map markers, and clan adapter work remain deferred.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
