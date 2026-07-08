Portable Airstrikes player cancel/status and heavy warning markers - 2026-07-08

What changed:
- Updated `PortableAirstrikes` to v0.1.14.
- Added root config key `ConfigVersion=14`.
- Added `/strike status` so players can check their own active call state, target, warning countdown, marker state, and cancel hint.
- Added `/strike cancel` for active calls before any payload/projectile/pulse starts.
- Added `General.AllowPlayerCancelBeforeImpact`, default `true`.
- Added `General.RefundPlayerCancelledCallsBeforeImpact`, default `true`.
- Added `General.HeavyStrikeMapMarkerSize`, default `18`.
- Added `General.HeavyStrikeMapMarkerAlpha`, default `0.35`.
- Heavy strikes now create a native public `MapMarkerGenericRadius` warning marker while `General.UseMapMarkersForHeavyStrikes=true`.
- Warning markers are non-saving globally broadcast entities and are removed on completion, failure, cancellation, and plugin unload.
- Player-cancelled pre-impact calls destroy pending timers/entities, remove warning markers, restore RP/tokens when configured, clear cooldowns when refunded, write audit records, and update stats.
- Existing direct/CUI strike validation, RP/token/cooldown/refund behavior, audit history, default-disabled homing/MLRS gates, loot behavior, and monument blocking behavior were left unchanged.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: 0BECFA4B88A3ADD37DEB5BCC3C814A9CDEC0C60561DE0160E6B7FB0B110FE891

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_CANCEL_MARKERS_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Config upload:
- No config upload is required for safe/default behavior.
- Reloading will save `ConfigVersion=14` and the new `General` keys into the live config.
- Note: `General.UseMapMarkersForHeavyStrikes` already defaults to `true`; heavy strikes now honor that setting with public native map markers.

Reload after upload:
- oxide.reload PortableAirstrikes

Safe regression smoke:
- `oxide.reload PortableAirstrikes`
- `/strike debugping`
- `/strike`
- `/strike beancan_drop`
- `/strike status`
- `/strike debug history 5`
- `/strike debug active`

Cancel smoke:
- Use a strike with enough warning delay to cancel before impact, for example `/strike a10_strafe`.
- Immediately run `/strike status`.
- Run `/strike cancel` before payloads begin.
- Expected: call cancels, pending timers/entities are cleaned, costs are restored if configured, cooldowns clear when refunded, and `/strike debug history 5` shows a `cancelled_refunded` row.

Heavy marker smoke:
- Run a heavy strike such as `/strike a10_strafe` in a safe open area.
- Expected: a public warning marker appears on the Rust map during warning/inbound time, is a reasonable size, and disappears after completion.

Expected default behavior:
- Existing direct/CUI strike flow still charges RP and consumes one token exactly once.
- Default-disabled `homing_heli`, `homing_jet`, `mini_mlrs`, and `full_mlrs` stay gated by config.
- Loot injection remains disabled unless `LootDistribution.Enabled=true`.
- Monument blocking remains disabled unless `General.BlockMonuments=true`.
- `/strike cancel` is rejected after impact starts and does not refund costs.
- Server console prints clear `Audit:` lines for started/completed/failed/validation-failed/cancelled calls.

Known unresolved verification points:
- Live reload and in-game smoke are still required.
- Heavy warning marker size/visibility should be tuned from live Rust map behavior.
- Live Rust/uMod still needs confirmation that binocular/team pings include vehicle entity IDs on this server build.
- Homing missile tracking/damage remains locally compiled but not live-smoked; keep homing strikes gated until a safe vehicle test is available.
- Native projectile/grenade/shell damage attribution still depends on Rust/uMod behavior, though spawned payload entities set `OwnerID` and creator entity.
- Loot injection live tuning/smoke is intentionally deferred until the end.

Local verification:
- Confirmed `assets/prefabs/tools/map/genericradiusmarker.prefab` exists in `Bundles/AssetSceneManifest.json`.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- Direct trailing-whitespace scan passed for `oxide/plugins/PortableAirstrikes.cs`.
