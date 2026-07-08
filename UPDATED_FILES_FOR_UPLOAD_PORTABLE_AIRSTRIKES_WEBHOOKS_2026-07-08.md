Portable Airstrikes audit webhook forwarding - 2026-07-08

What changed:
- Updated `PortableAirstrikes` to v0.1.15.
- Added root config key `ConfigVersion=15`.
- Added disabled-by-default `AuditWebhooks` config.
- Existing local `Audit:` console lines and `PortableAirstrikes_Data.RecentCalls` history are unchanged.
- When enabled, selected audit results can be mirrored to a Discord webhook as embeds.
- Webhook result filters:
  - `SendStartedCalls`, default `true`
  - `SendCompletedCalls`, default `true`
  - `SendFailuresAndRefunds`, default `true`
  - `SendPlayerCancels`, default `true`
  - `SendValidationFailures`, default `false`
- Bad or missing webhook URLs print a warning only; they do not block validation, RP/token handling, cooldowns, execution, cleanup, or local audit history.
- Existing direct/CUI validation, RP/token/cooldown/refund behavior, cancel/status, heavy warning markers, default-disabled homing/MLRS gates, loot behavior, and monument blocking behavior were left unchanged.

Live smoke evidence added to the development log:
- Additional `mini_mlrs` testing confirmed MLRS rockets were visible on the map during the strike.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: 0E6767A63420A5E52E80C41473CBBA50B8FBC168051F6603A8B4A76BC94F430F

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_WEBHOOKS_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Config upload:
- No config upload is required for safe/default behavior.
- Reloading will save `ConfigVersion=15` and the new `AuditWebhooks` section into the live config.
- `AuditWebhooks.Enabled` defaults to `false`, so webhook forwarding stays off until deliberately configured.

Reload after upload:
- oxide.reload PortableAirstrikes

Safe default smoke:
- `oxide.reload PortableAirstrikes`
- `/strike debugping`
- `/strike`
- `/strike beancan_drop`
- `/strike debug history 5`
- Expected: normal strike behavior and local audit history still work with no Discord/webhook activity.

Optional Discord webhook smoke:
- Reload once first so `AuditWebhooks` is written to `oxide/config/PortableAirstrikes.json`.
- Set `AuditWebhooks.Enabled=true`.
- Set `AuditWebhooks.DiscordWebhookUrl` to the private Discord webhook URL.
- Leave `AuditWebhooks.SendValidationFailures=false` unless validation-failure noise is desired.
- Reload again with `oxide.reload PortableAirstrikes`.
- Run a cheap accepted/completed strike such as `/strike beancan_drop`.
- Expected: a Discord embed appears for the configured audit result types.

Expected default behavior:
- Default-disabled `mini_mlrs`, `full_mlrs`, `homing_heli`, and `homing_jet` stay gated by config.
- Loot injection remains disabled unless `LootDistribution.Enabled=true`.
- Monument blocking remains disabled unless `General.BlockMonuments=true`.
- `/strike cancel` is rejected after impact starts and does not refund costs.
- Server console still prints clear `Audit:` lines for started/completed/failed/validation-failed/cancelled calls.

Known unresolved verification points:
- Live reload and in-game smoke are still required.
- Optional Discord webhook delivery requires a real private webhook URL on the live server.
- Heavy warning marker size/visibility should still be tuned from live Rust map behavior.
- Live Rust/uMod still needs confirmation that binocular/team pings include vehicle entity IDs on this server build.
- Homing missile tracking/damage remains locally compiled but not live-smoked; keep homing strikes gated until a safe vehicle test is available.
- Native projectile/grenade/shell damage attribution still depends on Rust/uMod behavior, though spawned payload entities set `OwnerID` and creator entity.
- Loot injection live tuning/smoke is intentionally deferred until the end.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- Direct trailing-whitespace scan passed for `oxide/plugins/PortableAirstrikes.cs`, `Docs/AirStrikes/portable_airstrikes_development_log.md`, `UPDATED_FILES_FOR_UPLOAD.txt`, and this upload note.
