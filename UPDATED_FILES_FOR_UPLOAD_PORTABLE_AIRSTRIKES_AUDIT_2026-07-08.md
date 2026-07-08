Portable Airstrikes audit logging and admin debug - 2026-07-08

What changed:
- Updated `PortableAirstrikes` to v0.1.13.
- Added root config key `ConfigVersion=13`.
- Added `General.RecentCallHistoryLimit`, default `50`, clamped to `0-200`.
- Added bounded `RecentCalls` audit history to `PortableAirstrikes_Data`.
- Added server-console `Audit:` lines for:
  - validation failures
  - charge failures
  - accepted/started calls
  - completed calls
  - failed/refunded calls
  - plugin-unload cancellations
- Audit records include player, strike ID/name, target type/position/entity ID, RP cost, RP/token/refund flags, impact-started state, result, and message.
- Added admin debug commands:
  - `/strike debug history [count]`
  - `/strike debug stats`
  - `/strike debug active`
- Homing missiles, MLRS, loot injection, monument blocking behavior, RP charge, token consume, refunds, cooldowns, and disabled-by-default gates were left unchanged.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: F593363136EC57C1B0D7C94406699B9AFEE1508078A037B3C23B8272B3358298

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_AUDIT_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Config upload:
- No config upload is required for safe/default behavior.
- Reloading will save `ConfigVersion=13` and `General.RecentCallHistoryLimit=50` into the live config.
- Existing `PortableAirstrikes_Data` can stay live; reload/load will add `RecentCalls` automatically.

Reload after upload:
- oxide.reload PortableAirstrikes

Safe regression smoke:
- `oxide.reload PortableAirstrikes`
- `/strike debugping`
- `/strike`
- `/strike beancan_drop`
- `/strike debug history 5`
- `/strike debug stats`
- `/strike debug active`
- `/strike debugping`
- `/strike a10_strafe`

Expected default behavior:
- Existing direct/CUI strike flow still charges RP and consumes one token exactly once.
- Default-disabled `homing_heli`, `homing_jet`, `mini_mlrs`, and `full_mlrs` stay gated by config.
- Loot injection remains disabled unless `LootDistribution.Enabled=true`.
- Monument blocking remains disabled unless `General.BlockMonuments=true`.
- Server console prints clear `Audit:` lines for accepted/completed/failed/validation-failed calls.
- `/strike debug history [count]` shows newest bounded audit records to admins.
- `/strike debug stats` shows existing stat counters.
- `/strike debug active` shows current active calls or reports none.

Known unresolved verification points:
- Live reload and in-game smoke are still required.
- Live Rust/uMod still needs confirmation that binocular/team pings include vehicle entity IDs on this server build.
- Homing missile tracking/damage remains locally compiled but not live-smoked; keep homing strikes gated until a safe vehicle test is available.
- Native projectile/grenade/shell damage attribution still depends on Rust/uMod behavior, though spawned payload entities set `OwnerID` and creator entity.
- Loot injection live tuning/smoke is intentionally deferred until the end.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
