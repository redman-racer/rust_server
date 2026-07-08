Portable Airstrikes in-game warning fanout - 2026-07-08

What changed:
- Updated `PortableAirstrikes` to v0.1.16.
- Added root config key `ConfigVersion=16`.
- Added in-game warning config under `General`:
  - `NotifyCallerTeamOnAcceptedStrike`, default `true`
  - `NotifyNearbyPlayersOnHeavyStrikes`, default `false`
  - `NearbyHeavyStrikeWarningRadius`, default `120`
- Accepted strikes now notify online Rust teammates other than the caller when `NotifyCallerTeamOnAcceptedStrike=true`.
- Heavy strikes can optionally notify online nearby players inside `NearbyHeavyStrikeWarningRadius` when `NotifyNearbyPlayersOnHeavyStrikes=true`.
- Team and nearby warning recipients are deduplicated so a nearby teammate should only receive one warning message.
- Discord webhook code and config were left unchanged; webhook delivery still requires a separate real-webhook smoke when available.
- Existing direct/CUI validation, RP/token/cooldown/refund behavior, cancel/status, heavy warning markers, local audit history, default-disabled homing/MLRS gates, loot behavior, and monument blocking behavior were left unchanged.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: 24AF07B47E4D25F99F76CB221463629870460CDE4331A3999C95E29ED0B84536

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_WARNINGS_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Config upload:
- No config upload is required for safe/default behavior.
- Reloading will save `ConfigVersion=16` and the new `General` warning keys into the live config.
- `NotifyCallerTeamOnAcceptedStrike` defaults to `true`.
- `NotifyNearbyPlayersOnHeavyStrikes` defaults to `false`, so nearby-player warning fanout stays off until deliberately configured.

Reload after upload:
- oxide.reload PortableAirstrikes

Safe default smoke:
- `oxide.reload PortableAirstrikes`
- `/strike debugping`
- `/strike`
- `/strike beancan_drop`
- `/strike debug history 5`
- Expected: normal strike behavior and local audit history still work with no Discord/webhook activity.
- If two online team members are available, expected: the non-calling teammate receives one in-game accepted-strike warning.

Optional nearby heavy-warning smoke:
- Reload once first so the new `General` keys are written to `oxide/config/PortableAirstrikes.json`.
- Set `General.NotifyNearbyPlayersOnHeavyStrikes=true`.
- Leave `General.NearbyHeavyStrikeWarningRadius=120` unless tuning is needed.
- Reload again with `oxide.reload PortableAirstrikes`.
- Run a heavy strike in a safe open area, such as `/strike a10_strafe`, with another online player inside the configured radius.
- Expected: the nearby player receives one warning message. If they are also on the caller's team, they should not receive a duplicate.

Expected default behavior:
- Default-disabled `mini_mlrs`, `full_mlrs`, `homing_heli`, and `homing_jet` stay gated by config.
- Loot injection remains disabled unless `LootDistribution.Enabled=true`.
- Monument blocking remains disabled unless `General.BlockMonuments=true`.
- Discord webhook forwarding remains disabled unless `AuditWebhooks.Enabled=true` and a valid webhook URL is configured.
- `/strike cancel` is rejected after impact starts and does not refund costs.
- Server console still prints clear `Audit:` lines for started/completed/failed/validation-failed/cancelled calls.

Known unresolved verification points:
- Live reload and in-game smoke are still required.
- Optional Discord webhook delivery cannot be verified without a real private webhook URL.
- Team warning fanout needs live confirmation with at least two online Rust team members.
- Nearby heavy-warning radius/noise should be tuned live before enabling it publicly.
- Heavy warning marker size/visibility should still be tuned from live Rust map behavior.
- Live Rust/uMod still needs confirmation that binocular/team pings include vehicle entity IDs on this server build.
- Homing missile tracking/damage remains locally compiled but not live-smoked; keep homing strikes gated until a safe vehicle test is available.
- Native projectile/grenade/shell damage attribution still depends on Rust/uMod behavior, though spawned payload entities set `OwnerID` and creator entity.
- Loot injection live tuning/smoke is intentionally deferred until the end.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- Direct trailing-whitespace scan passed for `oxide/plugins/PortableAirstrikes.cs`, `Docs/AirStrikes/portable_airstrikes_development_log.md`, `UPDATED_FILES_FOR_UPLOAD.txt`, and this upload note.
