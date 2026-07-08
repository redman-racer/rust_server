Portable Airstrikes loot token injection - 2026-07-08

What changed:
- Updated `PortableAirstrikes` to v0.1.9.
- Added optional loot injection for the single generic Airstrike Authorization item.
- Added loot spawn hooks for:
  - `OnLootSpawn(LootContainer)`
  - `OnLootSpawn(LootFill)`
- Loot injection is additive:
  - it does not replace default loot
  - it does not return a hook result that should block BetterLoot/default population
  - it uses the existing configured token shortname/display name/skin
- Container rules can match:
  - short prefab name, such as `crate_normal`
  - full prefab path/name
  - object name
  - normalized prefab file name without `.prefab`
- `LootDistribution.Enabled` remains disabled by default.
- Existing CUI/direct-command strike behavior remains supported.
- Homing missiles, MLRS, aircraft movement, and broader zone controls remain deferred.

Rust server plugin files to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: FEC7A71C9A056EDEBCCB6234648F7B00DB577F53EF12B24DF2FBC25464F1C145

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Config upload:
- No config upload is required unless you want loot injection enabled immediately.
- To enable live loot injection, set `LootDistribution.Enabled` to `true` in `oxide/config/PortableAirstrikes.json`.
- Start with one low-risk rule such as `crate_normal` or `crate_elite`, then tune chance/amount after smoke testing.

Reload after upload:
- oxide.reload PortableAirstrikes

Minimum setup for strike smoke:
- Grant basic use: `oxide.grant group default portableairstrikes.use`
- Grant A-10 strike family: `oxide.grant group default portableairstrikes.use.a10`
- Grant admin access: `oxide.grant user <steamId> portableairstrikes.admin`
- Give test items from server console: `portableairstrikes.giveitem <playerNameOrSteamId> 10`

Loot injection smoke:
- Upload `oxide/plugins/PortableAirstrikes.cs`.
- Run `oxide.reload PortableAirstrikes`.
- Leave `LootDistribution.Enabled=false` first and confirm normal loot behavior is unchanged.
- Set `LootDistribution.Enabled=true` for one configured container rule.
- Spawn/find the matching container.
- Expected: existing loot remains, and the configured Airstrike Authorization item can appear at the configured chance and amount.
- Pick up a loot-generated token and confirm it is accepted by `/strike` and direct strike calls.

CUI/direct regression smoke:
- `/strike debugping`
- `/strike`
- select a known working strike such as `A-10 BRRRRT Run`
- confirm
- `/strike debugping`
- `/strike hv_rocket_run`
- `/strike debugping`
- `/strike beancan_drop`

Expected success behavior:
- CUI still opens on a fresh target.
- Confirmation still charges RP and consumes one token exactly once unless admin/free bypass applies.
- Direct commands still dispatch the same executors.
- Loot-generated tokens match the same item validation path as admin-granted tokens.

Known unresolved verification points:
- Live Rust/uMod still needs confirmation that binocular/team pings fire the expected hook on this server build.
- The admin `/strike debugping` path remains the verified test path until live ping behavior is confirmed.
- Loot injection is locally compiled but still needs live container smoke testing.
- Homing missiles, MLRS, map markers, aircraft movement, and custom damage scaling for native projectile strikes are still deferred.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
