Portable Airstrikes warning fanout diagnostics - 2026-07-08

What changed:
- Updated `PortableAirstrikes` to v0.1.17.
- Added root config key `ConfigVersion=17`.
- Added admin diagnostic command:
  - `/strike debug warnings <strikeId>`
- The warning debug command previews who would receive accepted-strike warnings for the caller's current stored target without sending messages.
- The preview reports team-warning mode, nearby-heavy-warning mode/radius, team member/skipped counts, nearby candidate/deduped counts, and up to eight online recipients with warning source and distance.
- Accepted strike calls now use the same shared recipient-selection path as the preview command.
- Accepted strike audit messages now include warning fanout counts.
- Server console now prints a `Warning fanout:` line for each accepted strike with call ID, strike ID, caller, recipient counts, config mode, nearby eligibility, radius, and marker state.
- Stored stats now include `warning_fanout_calls`, `warning_team_recipients`, `warning_nearby_recipients`, and `warning_no_recipients`.
- Existing warning message text, direct/CUI validation, RP/token/cooldown/refund behavior, cancel/status, heavy warning markers, local audit history, default-disabled homing/MLRS gates, loot behavior, monument blocking behavior, and Discord webhook behavior were left unchanged.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: 4D193DAE60A6D82F241F2E43B28A4AAD5929CF92AD41F08B70F0174186713D3B

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_WARNING_DEBUG_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Config upload:
- No config upload is required for safe/default behavior.
- Reloading will save `ConfigVersion=17`.
- Existing warning settings are preserved:
  - `General.NotifyCallerTeamOnAcceptedStrike`
  - `General.NotifyNearbyPlayersOnHeavyStrikes`
  - `General.NearbyHeavyStrikeWarningRadius`

Reload after upload:
- oxide.reload PortableAirstrikes

Solo diagnostic smoke:
- `oxide.reload PortableAirstrikes`
- `/strike debugping`
- `/strike debug warnings beancan_drop`
- `/strike beancan_drop`
- `/strike debug history 5`
- `/strike debug stats`

Expected solo result:
- `/strike debug warnings beancan_drop` reports zero recipients if you are solo, or lists online team recipients if any exist.
- The accepted strike still works normally.
- Server console prints a `Warning fanout:` line for the accepted call.
- `/strike debug history 5` includes warning fanout counts in the started audit row.
- `/strike debug stats` can show the new warning fanout counters after at least one accepted call.

Optional nearby heavy-warning smoke:
- Reload once first so the new config version is written.
- Set `General.NotifyNearbyPlayersOnHeavyStrikes=true`.
- Reload again with `oxide.reload PortableAirstrikes`.
- Run `/strike debugping` at the test impact area.
- Run `/strike debug warnings a10_strafe` to preview nearby online recipients.
- Run a heavy strike in a safe open area, such as `/strike a10_strafe`, with another online player inside the configured radius.
- Expected: the nearby player receives one warning message. If they are also on the caller's team, they should not receive a duplicate.

Known unresolved verification points:
- The new preview/logging proves recipient selection and send attempts, but true recipient-side chat visibility still needs at least one other online player to confirm.
- Nearby heavy-warning radius/noise should be tuned live before enabling it publicly.
- Heavy warning marker size/visibility should still be tuned from live Rust map behavior.
- Live Rust/uMod still needs confirmation that binocular/team pings include vehicle entity IDs on this server build.
- Homing missile tracking/damage remains locally compiled but not live-smoked; keep homing strikes gated until a safe vehicle test is available.
- Native projectile/grenade/shell damage attribution still depends on Rust/uMod behavior, though spawned payload entities set `OwnerID` and creator entity.
- Loot injection live tuning/smoke is intentionally deferred until the end.

Local verification:
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- Direct trailing-whitespace scan passed for `oxide/plugins/PortableAirstrikes.cs`, `Docs/AirStrikes/portable_airstrikes_development_log.md`, `UPDATED_FILES_FOR_UPLOAD.txt`, and this upload note.
