Portable Airstrikes beancan execution slice - 2026-07-07

What changed:
- Updated `PortableAirstrikes` to v0.1.1.
- Added the first real direct-command execution path for `beancan_drop`.
- Added common call-state tracking:
  - validates before charge
  - blocks another pending strike by the same caller
  - honors `General.MaxSimultaneousStrikes`
  - charges RP once
  - consumes one configured Airstrike Authorization item once unless admin/item bypass applies
  - starts player/team/global cooldowns
  - saves `/strike last` only after executor completion
- Added executor routing with the first route:
  - `drone:beancan`
- Added `DroneDropExecutor` for `beancan_drop`.
- `beancan_drop` now spawns real deployed beancan grenade payloads above the target:
  - prefab: `assets/prefabs/weapons/beancan grenade/grenade.beancan.deployed.prefab`
  - payload count: bounded by `BaseCount` and `MaxCount`
  - spread: bounded by `SpreadRadius`
  - drop stagger: `0.35s`
  - fuse: `2.5s`
- Added pre-impact failure protection:
  - refunds RP if charged
  - restores the consumed token
  - clears cooldowns
- Added unload cleanup for active call timers and tracked active payload entities.
- Other configured strike IDs still fail cleanly before charge with an "executor not enabled yet" message.

Rust server plugin files to upload:
- oxide/plugins/PortableAirstrikes.cs

No config/data upload is required for this pass:
- Existing/generated `oxide/config/PortableAirstrikes.json` can stay live.
- Existing/generated `oxide/data/PortableAirstrikes_Data.json` can stay live.

Reload after upload:
- oxide.reload PortableAirstrikes

Minimum setup for smoke:
- Grant basic use: `oxide.grant group default portableairstrikes.use`
- Grant starter strike family: `oxide.grant group default portableairstrikes.use.grenade`
- Grant admin access: `oxide.grant user <steamId> portableairstrikes.admin`
- Give a test item from server console: `portableairstrikes.giveitem <playerNameOrSteamId> 1`

Primary live smoke:
- Run `oxide.reload PortableAirstrikes`.
- As an admin, run `/strike debugping` while looking at terrain or a wall/foundation.
- Run `/strike beancan_drop`.
- Expected:
  - chat says Beancan Drop inbound
  - RP is charged unless admin/free mode applies
  - one Airstrike Authorization Key is consumed unless admin/item bypass applies
  - cooldown starts
  - beancan payloads drop and detonate around the target
  - completion chat reports delivered payload count
- Run `/strike debug cooldowns`; expected cooldown data count increases.
- Create a fresh `/strike debugping`, then run `/strike last`; expected it repeats `beancan_drop`.

Negative smoke:
- Run `/strike f1_cluster` after a fresh ground target.
- Expected:
  - clean "executor not enabled yet" message
  - no RP charge
  - no token consume
  - no cooldown start

Known unresolved verification points:
- Live Rust/uMod must confirm whether binocular/team pings fire the same `OnMapMarkerAdded(BasePlayer, ProtoBuf.MapNote)` hook on this server build.
- The admin `/strike debugping` path remains the verified test path until live ping behavior is confirmed.
- CUI selection, confirmation UI, payment confirmation, loot injection, drone visual movement, and the rest of the strike catalog are still deferred.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed` with `Oxide.References.dll` and `glTFast.Newtonsoft.dll` excluded.
- Direct trailing-whitespace scan passed for the plugin, development log, and this upload note.
