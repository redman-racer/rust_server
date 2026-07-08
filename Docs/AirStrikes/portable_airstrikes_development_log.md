# Portable Airstrikes Development Log

Last updated: 2026-07-08

## Purpose

This document is the working development log for the `PortableAirstrikes` Rust/uMod plugin. It records what has actually been implemented, what has been smoke-tested, and what the next implementation pass should pick up.

The original planning documents remain:

- `Docs/AirStrikes/portable_airstrikes_codex_implementation_plan.md`
- `Docs/AirStrikes/portable_airstrikes_options_tables_v2.md`

This log should be updated after each implementation pass so future work starts from the real plugin state instead of re-reading old plans as if nothing has shipped.

## Current Implementation Snapshot

Plugin file:

- `oxide/plugins/PortableAirstrikes.cs`

Current plugin version:

- `PortableAirstrikes` v0.1.30

Current upload note:

- `UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_STACKABLE_CID_2026-07-08.md`

Current state:

- The plugin now has the low-tier direct-command drone-drop slice for bee, grenade, utility, fire, and 40mm payloads, the first heavy-drop slice for heavy bee, firebomb, and propane payloads, the off-map mortar slice, the first rocket-run slice for HV, standard, and incendiary rockets, the first A-10 strafe slice, the scrollable CUI picker/confirmation flow, optional loot injection, the first disabled-by-default MLRS rocket slice, opt-in monument blocking for configured monuments, the first disabled-by-default vehicle homing missile slice, player status/cancel commands for pre-impact calls, heavy-strike warning map markers, in-game warning fanout, warning recipient diagnostics, optional audit Discord webhook forwarding, configurable visual delivery flyovers/artillery sources, the airstrike targeting binocular/default-strike flow, the CustomItemDefinitions item-definition layer, the v0.1.23 stacked-binocular/automatic raycast targeting pass, the v0.1.24 inventory-safe stack-cap fix, the v0.1.25 charge-backed binocular fix, the v0.1.28 client-safe visual prefab fix, the v0.1.29 stack/icon/rotor-wash repair, and the v0.1.30 stackable CID item pass.
- It can store a target, list strike definitions, validate a selected strike, check item/RP/permission/cooldown constraints, charge RP, consume the configured airstrike binocular item, start cooldowns, and dispatch bounded drone-drop, heavy-drop, rocket-run, off-map mortar, A-10 strafe, MLRS, and homing missile executors.
- Default live item is now the CID shortname `raidlands.airstrike.designator` named `Airstrike Targeting Binoculars`, parented to `tool.binoculars`; the legacy named `tool.binoculars` fallback still works when CID is unavailable and fallback is enabled.
- Actual Rust item stack size is now `65535` for the CID shortname `raidlands.airstrike.designator`. Vanilla `tool.binoculars` remains stack size `1`; old charge-backed airstrike items migrate their stored charge count into the physical stack amount.
- Holding the configured item gives player instructions, and a ping while holding it creates a short-lived cyan/amber targeting marker.
- Tool pings now first try the same raycast/entity capture that `/strike debugping` used, so vehicle-target pings can carry entity tracking without requiring the admin debug command.
- If the player has no saved default, the tool ping opens `/strike`; the confirmed selection is saved in `PortableAirstrikes_Data.DefaultStrikeByUser`.
- If the player has a saved default, the tool ping attempts that strike immediately. Players can view/change it with `/strike default show`, `/strike default <strikeId>`, and `/strike default clear`.
- `bee_swarm_drone`, `beancan_drop`, `f1_cluster`, `smoke_screen`, `flash_breach`, `molotov_drop`, and `he_40mm_micro` use the configured `BaseCount`, `MaxCount`, and `SpreadRadius`, then spawn payloads above the target with staggered timers.
- `bee_swarm_heavy`, `firebomb_run`, and `propane_bomb_drop` use the configured delivery multiplier, `BaseCount`, `MaxCount`, and `SpreadRadius`, then spawn catapult-style heavy projectiles above the target with staggered timers.
- `mortar_he` and `mortar_frag` now use an off-map mortar executor with configured count/spread and staggered mortar shell impacts.
- `hv_rocket_run`, `rocket_run`, and `incendiary_rocket_run` now use an attack-heli-style rocket-run executor with configured `RocketCount`, `MaxCount`, and `SpreadRadius`, then fire staggered rockets across a narrow target line.
- `a10_strafe` now uses a simulated Bradley-longbarrel cannon strafe with configured `BurstCount`, `LineLength`, `Width`, `ImpactRadius`, `PulseDelaySeconds`, and per-entity damage scales.
- `mini_mlrs` and `full_mlrs` now have an opt-in MLRS executor that uses configured `RocketCount`, `MaxCount`, and `SpreadRadius`, then launches staggered `rocket_mlrs.prefab` projectiles from an off-map approach toward randomized impact points.
- `homing_heli` and `homing_jet` now have an opt-in homing missile executor that requires a tracked vehicle entity ID, launches staggered HV-rocket visuals, steers them toward the live target entity for bounded tracking time/distance, and applies proximity-detonation vehicle/splash damage.
- Monument blocking is implemented in central validation but is disabled by default through `General.BlockMonuments=false`; when enabled it can block heavy strikes only or all strikes against configured monument names/radii.
- Call audit logging is implemented with server-console `Audit:` lines, bounded recent history in `PortableAirstrikes_Data`, and disabled-by-default Discord webhook mirroring.
- Heavy-strike warning markers use native `MapMarkerGenericRadius` entities while `General.UseMapMarkersForHeavyStrikes=true`, clean up on completion/failure/cancel/unload, and are intended as public counterplay markers.
- Accepted strikes can now notify the caller's online Rust team by default, and heavy strikes can optionally notify nearby online players through config without using Discord.
- Accepted strikes now print and audit warning fanout recipient counts, and admins can preview warning recipients with `/strike debug warnings <strikeId>` against the current stored target.
- `DeliveryVisuals` now controls scripted drone/aircraft flyovers, MLRS/A-10 aircraft passes, repeated autoload-safe flyover sound cues, high-rate movement intervals, and mortar artillery source visuals with optional temporary NPC crew. Rotor-wash effect sends are intentionally disabled because the prefabs are not in an autoloaded asset scene.
- All configured strike IDs now have an executor route, but `homing_heli`, `homing_jet`, `mini_mlrs`, and `full_mlrs` remain disabled by default pending deliberate live smoke/balance tuning.
- Loot injection live tuning is deferred until the end. MLRS and homing missiles remain disabled by default pending broader balance and safety tuning.

## Implemented So Far

### Scaffold, Config, Data, Commands

Implemented in `oxide/plugins/PortableAirstrikes.cs`:

- Plugin metadata and description.
- Config model sections:
  - `General`
  - `AirstrikeItem`
  - `Currency`
  - `Selection`
  - `DeliveryScaling`
  - `DeliveryVisuals`
  - `DamageScales`
  - `LootDistribution`
  - `AuditWebhooks`
  - `StrikeDefinitions`
  - `ChatPrefix`
- Data model:
  - `LastStrikeByUser`
  - `PlayerCooldownUntil`
  - `ClanCooldownUntil`
  - `GlobalCooldownUntil`
  - `Stats`
  - `RecentCalls`
- Config normalization and safe defaults.
- Data load/save for `PortableAirstrikes_Data`.
- Permission registration.
- Dynamic chat command registration from `Selection.OpenMenuCommand`, default `/strike`.

Implemented commands:

- `/strike`
- `/strike list`
- `/strike balance`
- `/strike reload`
- `/strike debug target`
- `/strike debug item`
- `/strike debug cooldowns`
- `/strike debug strikes`
- `/strike debug monument`
- `/strike debug history [count]`
- `/strike debug stats`
- `/strike debug active`
- `/strike debug warnings <strikeId>`
- `/strike debugping`
- `/strike giveitem <playerNameOrSteamId> [amount]`
- `/strike last`
- `/strike status`
- `/strike cancel`
- `/strike <strikeId>`
- `portableairstrikes.giveitem <playerNameOrSteamId> [amount]`

### Generic Airstrike Item

Implemented:

- Single generic item model.
- Default CID item:
  - custom shortname: `raidlands.airstrike.designator`
  - parent shortname: `tool.binoculars`
  - custom item id: `-395118447`
  - icon path: `oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png`
  - display name: `Airstrike Targeting Binoculars`
- Legacy fallback item:
  - shortname: `tool.binoculars`
  - display name: `Airstrike Targeting Binoculars`
  - skin: `0`
- CID matching by custom `ItemDefinition` or custom shortname.
- Legacy fallback matching by shortname plus optional custom-name or skin via `RequireCustomNameOrSkin`.
- `AirstrikeItem.MaxStackSize` controls the actual CID `maxStackSize` registration and defaults to `65535`.
- `AirstrikeItem.MaxChargesPerItem` remains `65535` for compatibility with old charge-backed items, but current inventory counts are physical Rust stack amounts.
- Inventory item count across main, belt, and wear containers.
- Give-item helper, admin/console give commands, Kits-created items, and loot injection create one movable binocular item carrying the requested charge amount.
- `/strike debug item` reports CID load/registration state, effective create shortname, parent item, icon source, and fallback mode.
- Item consume helper is used by the charge/execute state machine after validation and RP charge.
- Admin item bypass via `AirstrikeItem.AllowAdminsWithoutItem`.
- `AirstrikeItem.TreatAsTargetingTool=true` makes the configured item drive the ping-to-menu/default behavior while still preserving `/strike` direct/manual paths.
- `OnRaidlandsCreateKitItem(...)` delegates matching kit rows back to the plugin's configured item creator.

Plugin APIs:

- `API_GiveAirstrikeItem(ulong playerId, int amount)`
- `API_HasAirstrikeItem(ulong playerId, int amount)`

### Currency Adapter

Implemented:

- `ICurrencyAdapter` interface.
- `ServerRewardsCurrencyAdapter`, default provider.
- `EconomicsCurrencyAdapter`, available if config provider is changed to `Economics`.
- `NullCurrencyAdapter` for disabled/free mode.
- `/strike balance`.
- RP affordability checks in central validation.
- VIP discount calculation by permission.

Active:

- RP withdrawal on successful direct-command calls.
- RP refund if execution fails before impact.
- CUI confirmation starts the same centralized charge/consume/cooldown/executor path as direct commands.

### Strike Registry

Implemented:

- `StrikeDefinition` config model.
- Default catalog loaded into config.
- Configured strike permissions registered.
- Enabled strike filtering.
- Target-type filtering for `/strike` overview.
- Clean unknown-strike failure.

Default catalog currently includes:

- `bee_swarm_drone`
- `bee_swarm_heavy`
- `beancan_drop`
- `f1_cluster`
- `smoke_screen`
- `flash_breach`
- `he_40mm_micro`
- `molotov_drop`
- `firebomb_run`
- `propane_bomb_drop`
- `hv_rocket_run`
- `rocket_run`
- `incendiary_rocket_run`
- `mortar_he`
- `mortar_frag`
- `a10_strafe`
- `homing_heli` disabled by default
- `homing_jet` disabled by default
- `mini_mlrs` disabled by default
- `full_mlrs` disabled by default

### Target Capture

Implemented:

- Latest target dictionary keyed by player ID.
- `OnMapMarkerAdded(BasePlayer, ProtoBuf.MapNote)` stores map/ping positions as ground targets.
- `/strike debugping` raycasts from the admin player's view and stores a test target.
- Debug raycast classification:
  - ground
  - vehicle
  - player
  - NPC
- Target age checks via `General.MaxPingAgeSeconds`, default `20`.

Important verification point:

- Live binocular/team ping behavior still needs to be confirmed against the server build. The current hook may catch map/team markers, but `debugping` is the verified test path so far.

### Call State Machine And Drone Drop Executor

Implemented in v0.1.1:

- `AirstrikeCallContext` and `StrikeExecutionState`.
- Active call tracking by caller ID.
- Max simultaneous strike rejection using `General.MaxSimultaneousStrikes`.
- Per-player pending strike spam prevention.
- Charge path using the existing currency adapter `Withdraw`.
- Token consume path using `ConsumeAirstrikeTokens`.
- Cooldown start path using `StartCooldowns`.
- Success path that saves `LastStrikeByUser` only after the executor completes.
- Failure path that refunds RP and restores a token if execution fails before impact.
- Failure path that clears cooldowns if execution fails before impact.
- Timer cleanup and active payload cleanup on unload.
- Executor registry with the initial `drone:beancan` route.
- `DroneDropExecutor` for `beancan_drop`.
- Real deployed beancan prefab payloads:
  - prefab: `assets/prefabs/weapons/beancan grenade/grenade.beancan.deployed.prefab`
  - spawn height: `35`
  - stagger delay: `0.35s`
  - fuse: `2.5s`
  - downward velocity: `18`

Implemented in v0.1.2:

- Expanded the same `DroneDropExecutor` to use payload specs instead of a beancan-only spawn path.
- Added executor routes:
  - `drone:f1_grenade`
  - `drone:smoke`
- Added `f1_cluster` execution with deployed F1 grenade payloads:
  - prefab: `assets/prefabs/weapons/f1 grenade/grenade.f1.deployed.prefab`
  - fuse: `2.5s`
- Added `smoke_screen` execution with deployed smoke grenade payloads:
  - prefab: `assets/prefabs/tools/smoke grenade/grenade.smoke.deployed.prefab`
  - completion delay: `2s`
- Successful calls now leave completed payload entities to their native Rust lifecycle, while active/unload cleanup still kills tracked payloads if the plugin unloads mid-strike.

Implemented in v0.1.3:

- Added executor routes:
  - `drone:flashbang`
  - `drone:molotov`
  - `drone:he_40mm`
- Added `flash_breach` execution with deployed flashbang payloads:
  - prefab: `assets/prefabs/weapons/flashbang/grenade.flashbang.deployed.prefab`
  - fuse: `2s`
- Added `molotov_drop` execution with deployed molotov payloads:
  - prefab: `assets/prefabs/weapons/molotov cocktail/grenade.molotov.deployed.prefab`
  - fuse: `1.5s`
- Added `he_40mm_micro` execution with 40mm HE projectile payloads:
  - prefab: `assets/prefabs/ammo/40mmgrenade/40mm_grenade_he.prefab`
  - completion delay: `2s`
- Verified the new prefab paths against `Bundles/AssetSceneManifest.json`.

Implemented in v0.1.4:

- Added executor route:
  - `drone:bee_grenade`
- Added `bee_swarm_drone` execution with deployed bee grenade payloads:
  - prefab: `assets/prefabs/weapons/bee grenade/grenade.bee.deployed.prefab`
  - fuse: `2.5s`
- Added `MortarExecutor` for off-map mortar missions.
- Added executor routes:
  - `off_map_mortar:mortar_he_payload`
  - `off_map_mortar:mortar_frag_payload`
- Added `mortar_he` execution with basic mortar shell payloads:
  - prefab: `assets/prefabs/deployable/mortar/mortar_shell_basic.prefab`
  - shell spawn height: `90`
  - stagger delay: `0.55s`
  - downward velocity: `42`
- Added `mortar_frag` execution with fragment mortar shell payloads:
  - prefab: `assets/prefabs/deployable/mortar/mortar_shell_fragment.prefab`
  - shell spawn height: `90`
  - stagger delay: `0.55s`
  - downward velocity: `42`
- Verified the new bee and mortar prefab paths against `Bundles/AssetSceneManifest.json`.

Implemented in v0.1.5:

- Added `HeavyDropExecutor` for attack-heli and cargo-plane/jet scaled drops.
- Added executor routes:
  - `attack_heli:bee_catapult_bomb`
  - `attack_heli:firebomb`
  - `attack_heli:propane_bomb`
  - `cargo_plane_jet:bee_catapult_bomb`
  - `cargo_plane_jet:firebomb`
  - `cargo_plane_jet:propane_bomb`
- Added `bee_swarm_heavy` execution with catapult bee boulder payloads:
  - prefab: `assets/content/vehicles/siegeweapons/catapult/ammo/projectiles/boulder_bee.prefab`
  - drop spawn height: `60`
  - stagger delay: `0.65s`
  - downward velocity: `28`
- Added `firebomb_run` execution with catapult incendiary boulder payloads:
  - prefab: `assets/content/vehicles/siegeweapons/catapult/ammo/projectiles/boulder_incendiary.prefab`
  - drop spawn height: `60`
  - stagger delay: `0.65s`
  - downward velocity: `28`
- Added `propane_bomb_drop` execution with catapult explosive boulder payloads:
  - prefab: `assets/content/vehicles/siegeweapons/catapult/ammo/projectiles/boulder_explosive.prefab`
  - drop spawn height: `60`
  - stagger delay: `0.65s`
  - downward velocity: `28`
- `General.MaxSimultaneousHeavyStrikes` is now enforced for supported heavy strike calls.
- Verified the new catapult projectile prefab paths against `Bundles/AssetSceneManifest.json`.

Implemented in v0.1.6:

- Added `RocketRunExecutor` for attack-heli and cargo-plane/jet rocket volleys.
- Added executor routes:
  - `attack_heli:hv_rocket`
  - `attack_heli:rocket`
  - `attack_heli:incendiary_rocket`
  - `cargo_plane_jet:hv_rocket`
  - `cargo_plane_jet:rocket`
  - `cargo_plane_jet:incendiary_rocket`
- Added `hv_rocket_run` execution with HV rocket projectiles:
  - prefab: `assets/prefabs/ammo/rocket/rocket_hv.prefab`
  - rocket count: configured `RocketCount`, capped by `MaxCount`
  - stagger delay: `0.45s`
  - projectile speed: `70`
- Added `rocket_run` execution with standard rocket projectiles:
  - prefab: `assets/prefabs/ammo/rocket/rocket_basic.prefab`
  - rocket count: configured `RocketCount`, capped by `MaxCount`
  - stagger delay: `0.45s`
  - projectile speed: `55`
- Added `incendiary_rocket_run` execution with incendiary rocket projectiles:
  - prefab: `assets/prefabs/ammo/rocket/rocket_fire.prefab`
  - rocket count: configured `RocketCount`, capped by `MaxCount`
  - stagger delay: `0.45s`
  - projectile speed: `50`
- Rocket impacts are distributed across a narrow target line using `SpreadRadius` instead of a broad circular drop pattern.
- Verified the new rocket projectile prefab paths against `Bundles/AssetSceneManifest.json`.

Implemented in v0.1.7:

- Added `A10StrafeExecutor` for direct-command ground-ping cannon strafes.
- Added executor route:
  - `a10_gun_run:bradley_longbarrel_burst`
- Added `a10_strafe` execution with simulated Bradley longbarrel impact pulses:
  - effect: `assets/prefabs/npc/m2bradley/effects/maincannonattack.prefab`
  - effect: `assets/prefabs/npc/m2bradley/effects/maincannonshell_explosion.prefab`
  - effect: `assets/bundled/prefabs/fx/impacts/bullet/generic/generic1.prefab`
  - burst count: configured `BurstCount`, capped at `80`
  - line length: configured `LineLength`
  - lateral width: configured `Width`
  - damage radius: configured `ImpactRadius`
  - pulse delay: configured `PulseDelaySeconds`, with a safe fallback for older configs
- A-10 pulses raycast down to the hit surface before playing effects and applying damage.
- A-10 damage uses the existing global `DamageScales` plus the strike's `DamageScales` dictionary for `Players`, `Buildings`, `Vehicles`, `Deployables`, and `Turrets`.
- The default `a10_strafe` definition exposes the `Turrets` damage scale entry.
- Verified the Bradley/impact effect paths against `Bundles/AssetSceneManifest.json`.

Implemented in v0.1.8:

- Added the first CUI picker for `/strike`.
- Added CUI console routes:
  - `portableairstrikes.ui.select`
  - `portableairstrikes.ui.confirm`
  - `portableairstrikes.ui.close`
- The picker filters enabled strikes by the latest fresh target type.
- The picker shows token count, configured token requirement, and RP balance summary.
- Picker rows respect `Selection.ShowLockedStrikes`.
- Picker rows only show a selectable button when the strike passes central validation.
- Added a confirmation panel when `Selection.RequireConfirmation` is enabled.
- Confirmation shows the selected strike, target, RP cost, token requirement, warning delay, and player cooldown.
- Confirming a CUI strike calls the same `TryPrepareStrike` pipeline as direct commands, so RP charge, token consume, cooldown start, executor dispatch, refunds, and `/strike last` state remain centralized.
- Plugin unload now destroys open PortableAirstrikes CUI panels.

Implemented in v0.1.9:

- Added optional loot injection for the generic Airstrike Authorization item.
- Added `OnLootSpawn(LootContainer)` and `OnLootSpawn(LootFill)` hooks.
- Loot rules match container short prefab names, full prefab names, object names, and normalized prefab file names.
- Injection is additive and returns no hook result, so it does not replace existing Rust/default/BetterLoot population.
- Injection uses the existing configured token shortname/display name/skin through the same `CreateAirstrikeToken` helper as admin give commands.
- `LootDistribution.Enabled` remains `false` by default; enabling it is a live config choice.
- Injection stats are recorded in `PortableAirstrikes_Data` as `loot_tokens_injected` and per matched container key.

Implemented in v0.1.10:

- Reviewed the item/token spawn assumption: the generic Airstrike Authorization item is a normal Rust item created with `ItemManager.CreateByName`, default shortname `targeting.computer`, configured display name `Airstrike Authorization Key`, and configured skin ID. No custom item prefab is required for admin grants or loot injection.
- Changed default `homing_heli`, `homing_jet`, and `mini_mlrs` definitions to `Enabled=false` so unsupported/deferred strikes do not show as normal player-facing choices before their executor or safety controls are proven.
- Added root `ConfigVersion=10` and a one-time old-config safety reset that disables `homing_heli`, `homing_jet`, `mini_mlrs`, and `full_mlrs` when loading a pre-v0.1.10/generated config. After the config saves at version 10, deliberate manual opt-in can persist.
- Added `MlrsExecutor` for the existing `mini_mlrs` and `full_mlrs` config definitions.
- Added executor route:
  - `cargo_plane_jet:mlrs_rocket`
- Added `mini_mlrs`/`full_mlrs` execution with real MLRS rocket projectiles:
  - prefab: `assets/content/vehicles/mlrs/rocket_mlrs.prefab`
  - rocket count: configured `RocketCount`, capped by `MaxCount` and an executor hard cap of `24`
  - spread: configured `SpreadRadius`
  - launch delay: `0.7s`
  - projectile speed: `85`
  - launch shape: off-map angled approach with randomized impact points inside the target spread
- `mini_mlrs` and `full_mlrs` remain disabled by default; enabling either is a live config choice after deliberate smoke testing.
- Verified the MLRS rocket prefab path against `Bundles/AssetSceneManifest.json`.

Implemented in v0.1.11:

- Added root `ConfigVersion=11`.
- Added opt-in monument blocking settings under `General`:
  - `BlockMonuments`
  - `BlockMonumentsForHeavyStrikesOnly`
  - `MonumentBlockRadiusPadding`
  - `DefaultMonumentBlockRadius`
  - `BlockedMonumentNames`
- `BlockMonuments` defaults to `false`, so uploading v0.1.11 does not unexpectedly change current strike behavior.
- When enabled, monument blocking runs in `ValidateStrikeCall` after safe-zone validation and before token/RP/cooldown work, so blocked targets fail without consuming an Airstrike Authorization item or charging RP.
- `BlockMonumentsForHeavyStrikesOnly` defaults to `true`, so configured monument blocks apply only to heavy strikes unless deliberately changed.
- The monument matcher uses cached `TerrainMeta.Path.Monuments` zones and accepts prefab short names, full prefab names, and display-name style aliases such as `launch_site`, `launch_site_1`, or `Launch Site`.
- Added `/strike debug monument` and `/strike debug safety` so admins can test the current target against configured monument zones before turning the rule on broadly.

Implemented in v0.1.12:

- Added root `ConfigVersion=12`.
- Added `HomingMissileExecutor` for the existing default-disabled `homing_heli` and `homing_jet` config definitions.
- Added executor routes:
  - `attack_heli:homing_missile`
  - `cargo_plane_jet:homing_missile`
- Added `homing_heli`/`homing_jet` execution with HV rocket projectile visuals:
  - prefab: `assets/prefabs/ammo/rocket/rocket_hv.prefab`
  - missile count: configured `MissileCount`, capped by `MaxCount` and an executor hard cap of `8`
  - tracking time: configured `MaxTrackingSeconds`
  - tracking distance: configured `MaxTrackingDistance`
  - proximity radius: max of `3.5m` or 40% of configured `SplashRadius`, capped at `8m`
  - launch delay: `0.65s`
  - projectile speed: `82`
- Homing missiles re-resolve the target vehicle entity by stored network ID at launch and on every tracking tick.
- If the target vehicle is gone before the first missile launches, RP/token/cooldowns follow the existing refund/restore path.
- Vehicle-only strikes now fail validation before RP/token/cooldown work if the stored target lacks an entity ID.
- `homing_heli` and `homing_jet` remain disabled by default; enabling either is a live config choice after deliberate vehicle-target smoke testing.

Implemented in v0.1.13:

- Added root `ConfigVersion=13`.
- Added `General.RecentCallHistoryLimit`, default `50`, clamped to `0-200`.
- Added bounded `RecentCalls` audit history to `PortableAirstrikes_Data`.
- Added server-console `Audit:` lines for validation failures, charge failures, accepted calls, completed calls, failed/refunded calls, and unload cancellations.
- Audit records include player ID/name, team ID, strike ID/name, target type/position/entity ID, RP cost, RP/token/refund flags, impact-started state, result, and message.
- Added admin debug commands:
  - `/strike debug history [count]`
  - `/strike debug stats`
  - `/strike debug active`
- Existing damage attribution behavior is now documented in the audit pass:
  - spawned native payload/projectile entities set `OwnerID` and creator entity to the caller where available
  - simulated A-10 and homing proximity damage calls pass the calling player into `Hurt`
  - native projectile/grenade/shell explosion attribution still needs live confirmation per Rust/uMod behavior

Implemented in v0.1.14:

- Added root `ConfigVersion=14`.
- Added player command `/strike status` to show the caller's active airstrike state, target, active age or warning countdown, public marker state, and cancel hint.
- Added player command `/strike cancel` for pre-impact active calls.
- Added config keys under `General`:
  - `AllowPlayerCancelBeforeImpact`, default `true`
  - `RefundPlayerCancelledCallsBeforeImpact`, default `true`
  - `HeavyStrikeMapMarkerSize`, default `18`
  - `HeavyStrikeMapMarkerAlpha`, default `0.35`
- Player cancellation is only allowed before `ImpactStarted=true`; once payload/projectile/pulse work begins, cancellation is rejected.
- Cancelled pre-impact calls destroy pending timers/entities, remove warning markers, restore RP/tokens when configured, clear cooldowns when refunded, write audit records, and update stats.
- Heavy strikes now create a native public `MapMarkerGenericRadius` warning marker when `General.UseMapMarkersForHeavyStrikes=true`.
- Warning markers use prefab `assets/prefabs/tools/map/genericradiusmarker.prefab`, use non-saving globally broadcast entities, and are removed on completion, failure, cancellation, and plugin unload.
- Existing direct/CUI strike validation, charge, token consume, refund, audit history, default-disabled homing/MLRS gates, loot behavior, and monument blocking behavior were left unchanged.

Implemented in v0.1.15:

- Added root `ConfigVersion=15`.
- Added disabled-by-default `AuditWebhooks` config section:
  - `Enabled`, default `false`
  - `DiscordWebhookUrl`, default empty
  - `Username`, default `Portable Airstrikes`
  - `AvatarUrl`, default empty
  - `MentionText`, default empty
  - `SendStartedCalls`, default `true`
  - `SendCompletedCalls`, default `true`
  - `SendFailuresAndRefunds`, default `true`
  - `SendPlayerCancels`, default `true`
  - `SendValidationFailures`, default `false`
- Existing `StrikeCallAuditRecord` entries can now be mirrored to a Discord webhook when enabled.
- Webhook filtering is result-based, so validation failures can stay local-only while started/completed/failed/cancelled calls are sent.
- Discord posts include player, strike, target, cost/refund, state/impact, optional target entity, and audit message fields.
- Webhook delivery failures print warnings only; they do not block strike validation, charging, execution, cleanup, or local audit history.
- Existing direct/CUI strike validation, RP/token/cooldown/refund behavior, default-disabled homing/MLRS gates, loot behavior, monument blocking, cancel/status, and heavy warning markers were left unchanged.

Implemented in v0.1.16:

- Added root `ConfigVersion=16`.
- Added config keys under `General`:
  - `NotifyCallerTeamOnAcceptedStrike`, default `true`
  - `NotifyNearbyPlayersOnHeavyStrikes`, default `false`
  - `NearbyHeavyStrikeWarningRadius`, default `120`
- Accepted calls now fan out in-game warnings after the call enters the warning state and after any heavy-strike map marker is created.
- Caller-team warnings go to online Rust team members other than the caller.
- Nearby heavy-strike warnings are opt-in and only send to online players inside `NearbyHeavyStrikeWarningRadius`; teammates already warned by the team path are deduplicated.
- Discord webhook behavior was left unchanged and remains optional/disabled by default.
- Existing direct/CUI validation, RP/token/cooldown/refund behavior, default-disabled homing/MLRS gates, loot behavior, monument blocking, cancel/status, heavy warning markers, and local audit history were left unchanged.

Implemented in v0.1.17:

- Added root `ConfigVersion=17`.
- Added admin command `/strike debug warnings <strikeId>` to preview the warning fanout recipients for the caller's current stored target without sending messages.
- The warning preview reports team-warning mode, nearby-heavy-warning mode/radius, team member/skipped counts, nearby candidate/deduped counts, and up to eight online recipients with source and distance.
- Accepted strike calls now use the same shared recipient-selection path as the debug preview, then send the existing team/nearby chat messages.
- Accepted strike audit messages now include warning fanout counts such as total/team/nearby recipients and whether team/nearby warning modes were enabled.
- The server console now prints one `Warning fanout:` line per accepted strike with call ID, strike ID, caller, recipient counts, config mode, nearby eligibility, radius, and marker state.
- Stored stats now include `warning_fanout_calls`, `warning_team_recipients`, `warning_nearby_recipients`, and `warning_no_recipients` counters.
- Existing direct/CUI validation, RP/token/cooldown/refund behavior, default-disabled homing/MLRS gates, loot behavior, monument blocking, cancel/status, heavy warning markers, webhook behavior, and warning message text were left unchanged.

Implemented in v0.1.18:

- Added root `ConfigVersion=18`.
- Added `DeliveryVisuals` config:
  - `Enabled`
  - `SpawnDroneVisuals`
  - `SpawnAircraftVisuals`
  - `SpawnMortarArtilleryVisuals`
  - `SpawnMortarCrewNpc`
  - `DroneFlyoverDistance`
  - `DroneFlyoverHeight`
  - `AircraftFlyoverDistance`
  - `AttackHeliFlyoverHeight`
  - `CargoPlaneFlyoverHeight`
  - `A10FlyoverHeight`
  - `MortarSourceDistance`
  - `MortarCrewOffset`
  - `VisualMoveIntervalSeconds`
- Added tracked visual entity cleanup separate from payload/projectile cleanup, so native grenade/rocket/shell lifecycle behavior remains unchanged.
- Drone-drop strikes now spawn a temporary scripted drone flyover while staggered payloads drop.
- Heavy drops, rocket runs, MLRS, homing strikes, and A-10 strafes now spawn temporary scripted aircraft flyovers using attack-heli or cargo-plane visuals based on delivery type.
- Mortar strikes now spawn a temporary mortar source near the fire mission origin, play mortar deploy/muzzle effects, and can spawn an optional temporary `Raidlands Artillery` NPC crew visual.
- Artillery crew NPC visuals have player-target sensing suppressed before being removed with the strike cleanup path.
- Existing validation, RP/token/cooldown/refund behavior, actual payload timing/damage, warning fanout, heavy warning markers, audit/webhook behavior, default-disabled homing/MLRS gates, loot behavior, and monument blocking were left unchanged.

Implemented in v0.1.19:

- Added root `ConfigVersion=19`.
- Added `DeliveryVisuals.SpawnFlyoverSoundEffects`, default `true`, so scripted drone/aircraft flyovers can emit positional sound-effect cues.
- Added `DeliveryVisuals.MlrsAircraftFlyoverHeight`, default `58`, to make the MLRS aircraft pass lower and easier to see than the generic cargo-plane flyover.
- Changed the default `DeliveryVisuals.VisualMoveIntervalSeconds` from `0.2` to `0.1` and migrated the old v0.1.18 default value on reload.
- Changed scripted flyover movement updates to use queued network updates instead of immediate network updates on every movement tick.
- MLRS now starts its aircraft visual with the same approach direction already chosen for the rocket salvo, instead of resolving a separate approach inside the visual helper.
- Rocket-run projectiles now trigger a rocket launch effect at the spawn point.
- MLRS rockets now trigger MLRS backfire/thrust effects at launch.
- Existing validation, RP/token/cooldown/refund behavior, actual payload timing/damage, warning fanout, heavy warning markers, audit/webhook behavior, default-disabled homing/MLRS gates, loot behavior, and monument blocking were left unchanged.

Implemented in v0.1.20:

- Added root `ConfigVersion=20`.
- Added `DeliveryVisuals.SpawnRotorWashEffects`, default `true`.
- Added `DeliveryVisuals.FlyoverSoundIntervalSeconds`, default `0.75`.
- Changed the default `DeliveryVisuals.VisualMoveIntervalSeconds` from `0.1` to `0.04` and migrated old v0.1.18/v0.1.19 defaults on reload.
- Changed scripted flyover movement updates back to immediate network updates at the tighter interval, because queued 0.1-second updates still looked like low-FPS position snaps live.
- Visual vehicle entities now have their `On` flag set locally, and `PlayerHelicopter` visuals attempt to enable fuel state and finish engine start so client-side vehicle audio/animation has a better chance to activate.
- Flyover sound cues are now repeated along the flight path instead of firing only at start/midpoint.
- Flyover sound bursts now combine vehicle-engine, projectile-flight, large fast-falloff, bullet-flyby, and optional rotor wash effects depending on the visual type.
- Existing validation, RP/token/cooldown/refund behavior, actual payload timing/damage, warning fanout, heavy warning markers, audit/webhook behavior, default-disabled homing/MLRS gates, loot behavior, and monument blocking were left unchanged.

Implemented in v0.1.21:

- Added root `ConfigVersion=21`.
- Changed the default configured item from `targeting.computer` / `Airstrike Authorization Key` to `tool.binoculars` / `Airstrike Targeting Binoculars`.
- Added conservative config migration: only the old stock default item shape migrates to binoculars; deliberate custom `AirstrikeItem` settings are preserved.
- Added `AirstrikeItem.TreatAsTargetingTool`, `ShowEquipInstructions`, `ToolTargetMarkerEnabled`, `ToolTargetMarkerDurationSeconds`, `ToolTargetMarkerSize`, and `ToolTargetMarkerAlpha`.
- Added `PortableAirstrikes_Data.DefaultStrikeByUser` so each player's binocular default persists across relogs/restarts.
- Added `/strike default show`, `/strike default <strikeId>`, and `/strike default clear`.
- Added equip guidance when a player switches to the configured airstrike binocular item.
- Added airstrike-tool ping handling: while holding the configured item, a stored ping either opens the selection menu for first-time/default repair or calls the player's saved default strike.
- Added first-use default capture: when the tool opens the menu because no default exists, the confirmed selection is saved as that player's default.
- Added a short-lived cyan/amber `MapMarkerGenericRadius` targeting marker for airstrike-tool pings.
- Added `OnRaidlandsCreateKitItem(...)` support so Kits can create the configured airstrike item through the plugin-owned item contract.
- Generated a project reference icon at `Docs/AirStrikes/assets/airstrike-targeting-binoculars.png`.
- Confirmed local Rust item data says `tool.binoculars` has `HasSkins=false`; this generated PNG cannot become the live inventory item icon through a normal Rust skin ID and is kept as a docs/website/UI reference asset.
- Existing strike executors, RP charge/refund, item restore on failure/cancel, cooldowns, warnings, webhooks, visual delivery, and disabled homing/MLRS gates were left unchanged.

Implemented in v0.1.22:

- Added root `ConfigVersion=22`.
- Patched the downloaded `CustomItemDefinitions.cs` compatibility issue by replacing direct `Translate.allServerTranslations` access with reflection; this keeps CID compiling on the current Rust/Oxide managed assemblies.
- Added CID-backed airstrike item registration through `CustomItemDefinitions.Register`.
- Added `AirstrikeItem.UseCustomItemDefinition`, `AllowVanillaFallbackIfCIDMissing`, `CustomShortname`, `CustomItemId`, `ParentShortname`, `DefaultDescription`, `IconFileId`, `IconPngDataPath`, and `ImportParentItemMods`.
- Registered `raidlands.airstrike.designator` as a custom item parented to `tool.binoculars`, with parent item mods imported so held binocular behavior is preserved.
- Added live icon loading from `oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png`; when `IconFileId` is not configured, the plugin stores the PNG with Rust `FileStorage` and passes that ID to CID.
- Refactored airstrike item creation, inventory matching, loot injection, and kit hook matching so new items use the CID shortname while old named binoculars continue to work as a transition fallback.
- Added `/strike debug item` for admin verification of CID loaded/registered state, effective create shortname, parent item, icon source, and fallback mode.
- Existing ping/default selection, RP charge/refund, item consume/restore, cooldowns, warnings, webhooks, and strike executors were left unchanged.

Implemented in v0.1.23:

- Added root `ConfigVersion=23`.
- Added `AirstrikeItem.MaxStackSize`, default `2147483647`, and pass it to CID registration as the custom airstrike item `maxStackSize`.
- Existing CID definitions are corrected to the configured max stack size when reused after reload.
- `GiveAirstrikeTokensDetailed`, `CreateAirstrikeToken(int amount)`, kit-created items, and loot injection now create stack amounts instead of looping one single item per amount.
- Tool pings while holding `Airstrike Targeting Binoculars` now try a forward raycast first and store the hit entity/position as the airstrike target; if no raycast hit exists, the old map-note target fallback remains.
- Normal player-facing missing-target guidance now points players to aim and ping with the binoculars instead of using `/strike debugping`; the debug command remains available to admins.
- The `/strike` picker now uses a vertical CUI scroll view and renders every enabled target-compatible strike allowed by `Selection.ShowLockedStrikes`, instead of capping the display at seven rows.
- Updated `oxide/config/StackSizeController.json` so fallback `tool.binoculars` and the custom shortname `raidlands.airstrike.designator` have the same practical max stack size.
- Existing RP charge/refund, one-item consume on successful call, default strike persistence, direct commands, confirmation, cooldowns, warnings, webhooks, and strike executors were left unchanged.

Implemented in v0.1.24:

- Added root `ConfigVersion=24`.
- Changed `AirstrikeItem.MaxStackSize` default from `2147483647` to `65535` after live testing showed `int.MaxValue` could disconnect players with `AssertionException: split_Amount <= 0` / `RPC Error in MoveItem`.
- Added an explicit maximum clamp so existing live configs that already saved `2147483647` are normalized down to `65535` on reload.
- Existing CID definitions are corrected to the clamped stack size when reused after reload.
- Updated `oxide/config/StackSizeController.json` so fallback `tool.binoculars` and `raidlands.airstrike.designator` also use `65535`.
- Existing stacked item creation, one-item consume, automatic raycast targeting, scrollable picker, RP/cooldown behavior, warnings, webhooks, and strike executors were left unchanged.

Implemented in v0.1.25:

- Added root `ConfigVersion=25`.
- Moved away from physical Rust item stacks for the binocular item because live v0.1.24 testing still disconnected players when moving a 25-count binocular-derived custom item.
- Set actual CID/fallback item stack size back to `1` and changed StackSizeController entries for `tool.binoculars` and `raidlands.airstrike.designator` back to `1`.
- Added `AirstrikeItem.MaxChargesPerItem`, default `65535`, and store airstrike charge count in `item.instanceData.dataInt`.
- `portableairstrikes.giveitem <player> 25`, Kits, loot injection, and API gives now create one physical binocular item named like `Airstrike Targeting Binoculars x25`.
- Count/consume logic now sums and decrements stored charges; successful strikes consume one charge, and the item is removed only when its charges reach zero.
- On reload and player reconnect, existing airstrike binocular stacks are normalized from `amount > 1` into `amount=1` plus stored charges, reducing the chance that old bad stacks remain move-crashy.
- Existing automatic raycast targeting, scrollable picker, RP/cooldown behavior, warnings, webhooks, and strike executors were left unchanged.

Implemented in v0.1.28:

- Kept root `ConfigVersion=25`; no config migration is required for this runtime-only visual fix.
- Replaced flyover air-movement sound cue `assets/content/sound/templates/projectile-flight-large.prefab` with autoloaded `assets/content/sound/templates/projectile-flight.prefab`.
- Stopped sending direct `assets/content/vehicles/mlrs/effects/pfx_mlrs_rocket_thrust.prefab` effect calls during MLRS launches.
- Verified those removed prefabs live in non-autoloaded `AssetScene-props.other`, matching the client overlay errors seen while airstrike items were falling.
- Existing heavy-drop payload spawning, MLRS physical rockets, damage, RP/token/cooldown behavior, warnings, webhooks, and item-charge handling were left unchanged.

Implemented in v0.1.29:

- Added root `ConfigVersion=26`.
- Restored charge-backed item capacity by setting `AirstrikeItem.MaxChargesPerItem` default/clamp back to `65535`.
- Migrates existing pre-v26 configs to `MaxChargesPerItem=65535` and `DeliveryVisuals.SpawnRotorWashEffects=false`.
- Old physical airstrike binocular stacks are normalized into one physical item with charges stored in `item.instanceData.dataInt`.
- If the normalized item was actively held, the plugin clears the active item and sends a player network update so the client held-viewmodel can refresh.
- Give/API/loot creation paths create charged single items up to the configured max instead of looping one physical binocular per charge.
- Existing CID airstrike definitions are unregistered/re-registered on PortableAirstrikes reload so the current PNG `iconFileId` and stack metadata are rebuilt.
- Removed direct rotor-wash effect sends because `rotorwashparticles_small` and `rotorwashparticles_large` live in non-autoloaded `AssetScene-props.other`.
- Existing payload spawning, RP/token/cooldown behavior, warnings, webhooks, and strike damage were left unchanged.

Implemented in v0.1.30:

- Added root `ConfigVersion=27`.
- Restored physical Rust stacking for `raidlands.airstrike.designator` by setting `AirstrikeItem.MaxStackSize` default/clamp to `65535`.
- Existing charge-backed items migrate forward by converting the larger of `item.amount` and `item.instanceData.dataInt` into the physical stack amount.
- Current give/API/kit/loot paths create real physical stacks, split only when requested amount exceeds `AirstrikeItem.MaxStackSize`.
- Item display name is kept as `Airstrike Targeting Binoculars`; Rust's native stack overlay now shows the count.
- `oxide/config/StackSizeController.json` sets `raidlands.airstrike.designator=65535` while leaving vanilla `tool.binoculars=1`.
- `StackSizeController.cs` now applies configured stack sizes in `OnItemDefinitionRegistered`, so CID items registered after StackSizeController initialization receive their configured stack size immediately.
- Existing targeting, icon, warnings, RP/cooldowns, webhooks, and strike payload behavior were left unchanged.

### Central Validation

Implemented:

- Player connected/alive checks.
- Base permission check: `portableairstrikes.use`.
- Admin bypass checks.
- Strike exists/enabled checks.
- Strike permission checks.
- Target exists and is fresh.
- Target type matches strike type.
- Vehicle-only strikes require a tracked entity ID.
- Vehicle target still exists for vehicle strikes.
- Max call range.
- Minimum caller distance.
- Line of sight to target.
- Safe zone block.
- Configured monument block.
- Token possession.
- RP balance after discounts.
- Cooldown data checks.

Still deferred outside the current direct-command execution slice:

- Cooldowns are checked for all strikes, and are started by active low-tier drone execution paths.
- `General.MaxSimultaneousStrikes` is enforced for all active calls.
- `General.MaxSimultaneousHeavyStrikes` is enforced for supported heavy strike calls.
- Clan cooldowns currently use Rust team IDs; a richer clan adapter is deferred.

## v0.1.10 Live Smoke Evidence

Reported by the user on 2026-07-08 after upload/reload:

- `mini_mlrs` worked live.
- `full_mlrs` also worked live.
- Additional `mini_mlrs` testing confirmed the MLRS rockets were visible on the map during the strike.
- This means the default-disabled MLRS executor path is clean enough to treat as implemented, while keeping public availability gated by config, permissions, cooldowns, and future safety tuning.

## v0.1.6 Live Smoke Evidence

Reported by the user on 2026-07-08 after upload/reload:

- The rocket smoke test worked for the v0.1.6 rocket-run slice.
- This means the HV, standard, and incendiary rocket executor path was clean enough to continue with the next ground-strike combat family.
- v0.1.7 therefore implements `a10_strafe` instead of moving into vehicle-ping homing missiles or MLRS.

## v0.1.7 Live Smoke Evidence

Reported by the user on 2026-07-08 after upload/reload:

- The A-10 strike smoke test worked.
- This means the direct-command A-10 executor path was clean enough to continue with the player-facing selection/confirmation UI before adding homing missiles or MLRS.

## v0.1.5 Live Smoke Evidence

Reported by the user on 2026-07-08 after upload/reload:

- `bee_swarm_heavy`, `firebomb_run`, and `propane_bomb_drop` all worked live.
- This means the heavy-drop executor slice was clean enough to continue with rocket-run execution.

## v0.1.4 Live Smoke Evidence

Reported by the user on 2026-07-08 after upload/reload:

- The v0.1.4 direct-command low-tier and mortar smoke pass succeeded live.
- This means the bee, grenade, utility, fire, 40mm, and mortar executor slice was clean enough to continue with the heavy-drop handoff.

## v0.1.1 Live Smoke Evidence

Reported by the user on 2026-07-08 after upload/reload:

- `beancan_drop` seems to be working as expected so far.
- This means the v0.1.1 charge/consume/cooldown/direct beancan executor path is ready to build on.

## v0.1.0 Live Smoke Evidence

Observed on 2026-07-07 after upload/reload:

- `/strike debugping` stored a target successfully.
- The target was reported as a `ground ping`.
- Debug raycast captured entity information, for example `wall#4814146`.
- `/strike` displayed matching ground strikes with RP costs.
- `/strike beancan_drop` passed validation.
- The plugin correctly reported that executors are not enabled in v0.1.0 and that no RP/token was charged or fired.

This meant the v0.1.0 foundation was doing what it was supposed to do before the v0.1.1 executor pass.

## Local Verification

Completed:

- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`.
- The compile excluded `Oxide.References.dll` and `glTFast.Newtonsoft.dll`, matching the local Rust plugin compile sanity-check pattern.
- Compile completed without warnings after suppressing expected JSON config default-field warnings.
- Direct trailing-whitespace scan passed for the plugin and portable-airstrikes upload note.
- Roslyn compile check passed again after the v0.1.1 beancan state-machine/executor pass.
- Roslyn compile check passed again after the v0.1.2 F1/smoke drone-drop executor pass.
- Roslyn compile check passed again after the v0.1.3 flash/molotov/40mm drone-drop executor pass.
- Direct trailing-whitespace scan passed for the plugin, development log, and portable-airstrikes upload note after the v0.1.3 pass.
- Roslyn compile check passed again after the v0.1.4 bee/mortar executor pass.
- Roslyn compile check passed again after the v0.1.5 heavy-drop executor pass.
- Direct trailing-whitespace scan passed for the plugin, development log, global upload note, and portable-airstrikes upload note after the v0.1.5 pass.
- Roslyn compile check passed again after the v0.1.6 rocket-run executor pass.
- Direct trailing-whitespace scan passed for the plugin, development log, global upload note, and portable-airstrikes upload note after the v0.1.6 pass.
- Roslyn compile check passed again after the v0.1.7 A-10 strafe executor pass.
- Roslyn compile check passed again after the v0.1.8 CUI picker/confirmation pass.
- Roslyn compile check passed again after the v0.1.9 loot injection pass.
- Roslyn compile check passed again after the v0.1.10 default-disabled MLRS executor pass.
- Roslyn compile check passed again after the v0.1.11 opt-in monument blocking pass.
- Roslyn compile check passed again after the v0.1.12 default-disabled homing missile executor pass.
- Roslyn compile check passed again after the v0.1.13 audit logging/admin-debug pass.
- Confirmed `assets/prefabs/tools/map/genericradiusmarker.prefab` exists in `Bundles/AssetSceneManifest.json`.
- Roslyn compile check passed again after the v0.1.14 player cancel/status and heavy warning marker pass.
- Direct trailing-whitespace scan passed for the plugin after the v0.1.14 pass.
- Roslyn compile check passed again after the v0.1.15 disabled-by-default audit webhook pass.
- Direct trailing-whitespace scan passed for the plugin, development log, global upload note, and portable-airstrikes webhook upload note after the v0.1.15 pass.
- Roslyn compile check passed again after the v0.1.16 in-game warning fanout pass.
- Direct trailing-whitespace scan passed for the plugin, development log, global upload note, and portable-airstrikes warning upload note after the v0.1.16 pass.
- Roslyn compile check passed again after the v0.1.17 warning fanout diagnostics pass.
- Direct trailing-whitespace scan passed for the plugin, development log, global upload note, and portable-airstrikes warning-debug upload note after the v0.1.17 pass.
- Confirmed visual prefabs exist in `Bundles/AssetSceneManifest.json` for drone, attack helicopter, cargo plane, mortar, mortar muzzle/deploy effects, and scientist NPC crew.
- Roslyn compile check passed again after the v0.1.18 visual delivery/artillery source pass.
- Confirmed v0.1.19 sound/effect prefabs exist in `Bundles/AssetSceneManifest.json` for drone deploy, dangerous vehicle engine, bullet flyby, rocket launch, MLRS backfire, and MLRS rocket thrust.
- Roslyn compile check passed again after the v0.1.19 visual smoothing/sound/MLRS-plane pass.
- Direct trailing-whitespace scan passed for the plugin, development log, global upload note, and visual-smoothing upload note after the v0.1.19 pass.
- Confirmed v0.1.20 sound/effect prefabs exist in `Bundles/AssetSceneManifest.json` for projectile-flight-large, large-sound-fast-falloff, and large/small rotor wash effects.
- Roslyn compile check passed again after the v0.1.20 high-rate visual movement/repeated-sound pass.
- Direct trailing-whitespace scan passed for the plugin, development log, global upload note, and visual-high-rate upload note after the v0.1.20 pass.
- Confirmed local item data includes `Bundles/items/tool.binoculars.json` and that it is holdable/usable but has `HasSkins=false`.
- Generated and saved `Docs/AirStrikes/assets/airstrike-targeting-binoculars.png` as the project reference icon for the airstrike binocular item.
- Roslyn compile check passed again after the v0.1.21 binocular/default-strike pass.
- Direct trailing-whitespace scan passed for the plugin and Airstrikes docs after the v0.1.21 pass.
- Roslyn compile check passed again after the v0.1.22 CID custom item pass.
- Roslyn compile check passed again after the v0.1.23 stacked-binocular/scrollable-picker pass.
- JSON parse check passed for `oxide/config/StackSizeController.json` after setting `tool.binoculars` and `raidlands.airstrike.designator` to `2147483647`.
- Direct trailing-whitespace scan passed for the plugin, StackSizeController config, development log, global upload note, and stacked-binocular upload note after the v0.1.23 pass.
- Roslyn compile check passed again after the v0.1.24 inventory-safe stack-cap fix.
- JSON parse check passed for `oxide/config/StackSizeController.json` after lowering `tool.binoculars` and `raidlands.airstrike.designator` to `65535`.
- Direct trailing-whitespace scan passed for the plugin, StackSizeController config, development log, global upload note, superseded stacked-binocular note, and v0.1.24 stack-cap upload note after the v0.1.24 pass.
- Roslyn compile check passed again after the v0.1.25 charge-backed binocular fix.
- JSON parse check passed for `oxide/config/StackSizeController.json` after returning `tool.binoculars` and `raidlands.airstrike.designator` to actual stack size `1`.
- Confirmed `projectile-flight-large.prefab` and `pfx_mlrs_rocket_thrust.prefab` live in non-autoloaded `AssetScene-props.other`, matching the client overlay errors reported during falling payload visuals.
- Confirmed the replacement `projectile-flight.prefab` cue and remaining flyover/MLRS cosmetic cues used by v0.1.28 live in autoloaded asset scenes.
- Roslyn compile check passed again after the v0.1.28 client-safe visual prefab fix.
- Confirmed `rotorwashparticles_small.prefab` and `rotorwashparticles_large.prefab` live in non-autoloaded `AssetScene-props.other`, matching the later rotor-wash client overlay error.
- Roslyn compile check passed again after the v0.1.29 binocular stack/icon and rotor-wash repair.
- Roslyn compile check passed again after the v0.1.30 stackable CID item pass.
- Roslyn compile check passed for `StackSizeController.cs` after adding the late CID registration stack-size hook; remaining warnings were expected unassigned plugin-reference/config-field warnings.
- JSON parse checks passed for `oxide/config/PortableAirstrikes.json` and `oxide/config/StackSizeController.json`; airstrike CID stack is `65535`, vanilla `tool.binoculars` remains `1`.

Still required:

- Live reload verification after every plugin edit.
- In-game smoke for the `/strike` CUI picker and confirmation flow after the v0.1.8 upload/reload.
- Confirmation that live binocular/team pings fire `OnMapMarkerAdded` while the configured `Airstrike Targeting Binoculars` item is active.
- In-game smoke for first-use binocular behavior: hold item, place ping, menu opens, confirm strike, `DefaultStrikeByUser` persists, next ping calls that default.
- Live confirmation that `portableairstrikes.giveitem <player> 25` creates a stack, that one successful strike consumes one item from that stack, and that the scroll wheel moves the strike picker content.
- In-game smoke for v0.1.20 high-rate visual movement/repeated sound cleanup on drone, attack-heli/aircraft, MLRS if enabled, and mortar strikes.
- Live confirmation that optional mortar crew NPC visuals remain non-disruptive with player-target sensing suppressed; set `DeliveryVisuals.SpawnMortarCrewNpc=false` if they behave noisily.

## v0.1.18 Visual Live Smoke Evidence

Reported by the user on 2026-07-08 after the v0.1.18 visual upload/reload:

- Drone animation appeared.
- Mortar animations appeared.
- One attack-heli strike completed successfully.
- The MLRS strike payload worked, but the user did not see the plane.
- Drone and attack-heli animations were soundless and very laggy/skipping/low-FPS-looking.
- This meant the payload/executor path still looked healthy, but the temporary visual presentation layer needed the v0.1.19 smoothing, sound, and MLRS aircraft-visibility pass.

## v0.1.19 Visual Live Smoke Evidence

Reported by the user on 2026-07-08 after the v0.1.19 visual upload/reload:

- Drone, heli, and aircraft visuals still looked like frame-by-frame animation with many missing frames.
- The visual appeared to hover, jump farther along the path, then hover again instead of moving continuously.
- Heli, plane, and drone flyover audio still was not heard.
- This meant the v0.1.19 queued `0.1`-second movement interval and sparse start/midpoint sound cues were still too coarse for live-client presentation, so v0.1.20 moved to high-rate immediate network updates and repeated sound bursts along the flight path.

## Current Known Boundaries

The plugin is no longer validation-only for `bee_swarm_drone`, `bee_swarm_heavy`, `beancan_drop`, `f1_cluster`, `smoke_screen`, `flash_breach`, `molotov_drop`, `he_40mm_micro`, `firebomb_run`, `propane_bomb_drop`, `hv_rocket_run`, `rocket_run`, `incendiary_rocket_run`, `mortar_he`, `mortar_frag`, `a10_strafe`, `mini_mlrs`, `full_mlrs`, `homing_heli`, and `homing_jet`.

Current boundaries:

- All current default strike IDs have executor payload support, but default-disabled strikes must be deliberately enabled in config before they appear or can be called.
- The first CUI picker and confirmation UI exist for already-supported strikes.
- The configured airstrike item is now a named `tool.binoculars` item; because Rust reports `HasSkins=false` for binoculars, live inventory icon replacement requires a different delivery path than normal item skin ID config.
- Tool-driven default selection is implemented, but live smoke still needs to confirm the server's current Rust/uMod build raises `OnMapMarkerAdded` for the specific binocular/team ping action players use.
- Optional loot container injection exists, but is disabled by default until `LootDistribution.Enabled` is turned on in config.
- `mini_mlrs` and `full_mlrs` are supported by an executor and have passed initial live smoke, but remain disabled by default until balance and safety tuning are complete.
- `homing_heli` and `homing_jet` are supported by an executor, but remain disabled by default until vehicle-targeting and damage/tracking behavior are smoke-tested live.
- Monument blocking exists but is disabled by default until `General.BlockMonuments=true` is set in config; default behavior is heavy-strike-only blocking against configured monument names.
- Heavy warning markers exist and default to `General.UseMapMarkersForHeavyStrikes=true`; because they are globally broadcast native map markers, live smoke should confirm the marker size and visibility are acceptable before treating this as final public balance.
- In-game caller-team warnings default on through `General.NotifyCallerTeamOnAcceptedStrike=true`; nearby heavy-strike warnings are available but default off through `General.NotifyNearbyPlayersOnHeavyStrikes=false` until live message radius/noise can be tuned.
- Warning fanout recipient selection can be previewed/admin-audited without a second client through `/strike debug warnings <strikeId>` and the accepted-call `Warning fanout:` console line; actual recipient-side chat visibility still needs at least one other online player to confirm.
- Drone/aircraft/artillery source visuals now exist with high-rate immediate movement and repeated sound cues, but rotor-wash effect sends are disabled because those prefabs are not autoload-safe. Live smoke still needs to confirm client-side movement, sound audibility, cleanup timing, and acceptable visual scale/noise.
- Custom damage scaling is implemented for the simulated A-10 pulse executor and homing missile proximity damage so far; native projectile/grenade/shell strikes still use their Rust-native damage behavior and still need live attribution confirmation.
- Audit history is stored locally in `PortableAirstrikes_Data`; optional Discord webhook mirroring exists but is disabled until `AuditWebhooks.Enabled=true` and `AuditWebhooks.DiscordWebhookUrl` are configured. Discord delivery remains unverified if no real webhook smoke is available.

## Recommended Next Implementation Pass

Do not implement the full strike catalog at once.

Recommended next slice:

1. Upload/reload v0.1.21 and confirm the plugin banner reports v0.1.21:
   - `oxide.reload PortableAirstrikes`
2. Give the configured binocular item and confirm first-use/default behavior:
   - `portableairstrikes.giveitem <playerNameOrSteamId> 2`
   - Equip `Airstrike Targeting Binoculars`.
   - Place a ping while holding it.
   - Expected: a cyan/amber airstrike target marker appears and `/strike` opens if no default is saved.
   - Confirm a strike in the menu.
   - Place another ping while holding the item.
   - Expected: the saved default strike is attempted automatically.
3. Confirm manual default controls:
   - `/strike default show`
   - `/strike list`
   - `/strike default beancan_drop`
   - `/strike default clear`
4. Confirm normal direct strikes, visual flyover spawn/cleanup, warning fanout diagnostics, and local audit history still work with webhooks disabled:
   - `/strike debugping`
   - `/strike debug warnings beancan_drop`
   - `/strike`
   - `/strike beancan_drop`
   - `/strike debug history 5`
   - `/strike debug stats`
5. Smoke one aircraft visual in a safe area:
   - `/strike debugping`
   - `/strike a10_strafe`
   - Confirm the cargo-plane/A-10 stand-in crosses the target with less visible stepping, has repeated audible flyover cues, and disappears after completion.
6. Smoke mortar artillery source visuals in a safe area:
   - `/strike debugping`
   - `/strike mortar_he`
   - Confirm the mortar source, muzzle effects, and optional `Raidlands Artillery` NPC crew appear near the off-map source and clean up after completion.
7. If MLRS is deliberately enabled for safe testing, smoke one `mini_mlrs` in an open area and confirm the lower MLRS aircraft pass is visible near the rocket approach and has repeated sound cues.
8. If two online team members are available, confirm the teammate sees an in-game warning when the caller starts a strike, then compare the observed recipient with the `Warning fanout:` console line.
9. Optional nearby warning preview and smoke without Discord:
   - Set `General.NotifyNearbyPlayersOnHeavyStrikes=true`.
   - Reload.
   - Use `/strike debug warnings a10_strafe` with the current target to preview online nearby recipients.
   - Run a heavy strike in a safe open area with another online player inside `General.NearbyHeavyStrikeWarningRadius`.
   - Confirm the nearby player gets one warning message, not duplicates.
10. Continue the v0.1.14 player-facing smoke if it has not already been finished:
   - `/strike status`
   - `/strike cancel`
   - a heavy strike marker cleanup test such as `/strike a10_strafe` or an enabled MLRS test
11. Keep loot injection deferred until the end; do not spend the next pass on `LootDistribution.Enabled=true` smoke unless the user explicitly reopens it.
12. Keep vehicle-target homing smoke on hold until a safe live vehicle test is available.
13. Skip Discord webhook smoke unless a real private webhook URL is available.

## Next Pass Acceptance Criteria

For the v0.1.20 high-rate visual movement/repeated-sound pass:

- Reloading v0.1.20 saves `ConfigVersion=20` while preserving the existing `General`, warning, marker, audit, homing/MLRS, loot, and strike-definition keys.
- `DeliveryVisuals` writes `SpawnFlyoverSoundEffects`, `SpawnRotorWashEffects=false`, `MlrsAircraftFlyoverHeight`, `VisualMoveIntervalSeconds=0.04`, and `FlyoverSoundIntervalSeconds=0.75` defaults.
- Old v0.1.18/v0.1.19 configs still at `DeliveryVisuals.VisualMoveIntervalSeconds=0.2` or `0.1` migrate to `0.04`; custom non-default intervals are preserved within the allowed clamp.
- Drone-drop strikes spawn a temporary drone visual that moves across the target area with high-rate immediate updates, repeated sound cues, and cleanup after completion/failure/cancel/unload.
- Aircraft-delivered heavy drops, rocket runs, homing missiles, and A-10 strafes spawn a temporary attack-heli or cargo-plane visual that moves across the target area with high-rate immediate updates, repeated sound cues, and cleanup after completion/failure/cancel/unload.
- MLRS strikes use the same approach direction for the rocket salvo and the lower aircraft visual, so the plane should cross the watched impact area with repeated sound cues.
- Mortar strikes spawn a temporary mortar source, mortar muzzle effects, and optional `Raidlands Artillery` NPC crew visual; all mortar visuals clean up after completion/failure/cancel/unload.
- Visual spawn failures log warnings and increment stats, but do not block strike validation, charging, payload dispatch, refunds, cooldowns, or cleanup.
- `/strike debug warnings <strikeId>` previews warning recipients for the caller's current stored target without sending messages.
- Accepted strikes write `Warning fanout:` console lines and started audit records include warning recipient counts.
- With `General.NotifyCallerTeamOnAcceptedStrike=true`, online Rust teammates other than the caller receive one accepted-strike warning.
- With `General.NotifyNearbyPlayersOnHeavyStrikes=false`, nearby-player warning behavior stays off by default.
- With `General.NotifyNearbyPlayersOnHeavyStrikes=true`, online nearby players inside `General.NearbyHeavyStrikeWarningRadius` receive one heavy-strike warning and already-notified teammates are deduplicated.
- Existing direct/CUI strike behavior, RP charge, token consume, refunds, cooldowns, cancel/status, heavy warning markers, local audit history, webhooks-disabled behavior, and disabled homing/MLRS gating remain unchanged.

## Implementation Notes For The Next Agent

The v0.1.1-v0.1.8 execution and UI passes already use these existing methods:

- `ValidateStrikeCall`
- `GetFinalRPCost`
- `ConsumeAirstrikeTokens`
- `StartCooldowns`
- currency adapter `Withdraw`
- currency adapter `Deposit`
- `GetLatestTarget`
- `DescribeTarget`
- `SaveData`

New surface added in v0.1.1-v0.1.16:

- `AirstrikeCallContext`
- `StrikeExecutionState`
- active call tracking
- `IStrikeExecutor`
- `DroneDropExecutor`
- `DronePayloadSpec`
- `HeavyDropExecutor`
- `HeavyDropPayloadSpec`
- `RocketRunExecutor`
- `RocketRunPayloadSpec`
- `MortarExecutor`
- `MortarPayloadSpec`
- `A10StrafeExecutor`
- `A10StrafeSpec`
- CUI picker and confirmation helpers
- `portableairstrikes.ui.select`
- `portableairstrikes.ui.confirm`
- `portableairstrikes.ui.close`
- loot injection helpers:
  - `TryInjectLootToken`
  - `TryGetLootRule`
  - `GetLootContainerNameCandidates`
- default-disabled MLRS helpers:
  - `MlrsExecutor`
  - `MlrsPayloadSpec`
  - `TryGetMlrsPayloadSpec`
  - `TrySpawnMlrsRocket`
  - `CalculateMlrsRocketCount`
- monument blocking helpers:
  - `IsBlockedMonumentPosition`
  - `EnsureMonumentBlockZones`
  - `IsConfiguredBlockedMonument`
  - `GetMonumentBlockRadius`
  - `ShowMonumentDebug`
- default-disabled homing helpers:
  - `HomingMissileExecutor`
  - `HomingMissileSpec`
  - `TryGetHomingMissileSpec`
  - `TrySpawnHomingMissile`
- `ScheduleHomingMissileTrack`
- `TryResolveHomingTarget`
- `ApplyHomingMissileDamage`
- `CalculateHomingMissileCount`
- v0.1.13 audit helpers:
  - `RecordValidationAudit`
  - `RecordStrikeAudit`
  - `CreateAuditRecord`
  - `TrimRecentCallHistory`
  - `ShowDebugHistory`
  - `ShowDebugStats`
  - `ShowDebugActiveCalls`
- v0.1.14 cancel/status/marker helpers:
  - `ShowPlayerStrikeStatus`
  - `CmdCancelActiveStrike`
  - `CancelStrikeCall`
  - `CanPlayerCancelCall`
  - `CreateWarningMapMarker`
  - `DestroyWarningMapMarker`
  - `IsWarningMapMarkerActive`
  - `HeavyStrikeMapMarkerNativeRadius`
- v0.1.16-v0.1.17 in-game warning helpers:
  - `NotifyStrikeAccepted`
  - `BuildWarningFanoutPreview`
  - `AddCallerTeamWarningRecipients`
  - `AddNearbyWarningRecipients`
  - `ShowWarningFanoutDebug`
  - `FormatWarningFanoutSummary`
- v0.1.18-v0.1.20 visual helpers:
  - `StartDroneDeliveryVisual`
  - `StartAircraftDeliveryVisual`
  - `StartMlrsDeliveryVisual`
  - `StartA10DeliveryVisual`
  - `StartMortarArtilleryVisual`
  - `StartVisualFlyover`
  - `SpawnTrackedVisualEntity`
  - `ScheduleVisualFlyoverStep`
  - `PrepareVisualVehicleEntity`
  - `RunFlyoverSoundCues`
  - `RunFlyoverSoundBurst`
  - `GetFlyoverSoundIntervalSeconds`
  - `CleanupContextVisuals`
  - `PrepareVisualCrewNpc`
  - `SuppressVisualNpcTargeting`
  - `RunMortarLaunchVisual`

Suggested live smoke flow:

```text
portableairstrikes.giveitem <playerNameOrSteamId> 25
Equip Airstrike Targeting Binoculars.
Aim at a ground or vehicle target and place a ping.
Select or confirm a strike from the scrollable picker.
Ping again after a default is saved.
```

Expected after v0.1.30 upload/reload:

```text
The give command creates one movable physical binocular stack named `Airstrike Targeting Binoculars` with Rust stack-count overlay, for example `x25`.
Existing old charge-backed items normalize into a real physical stack amount.
`/strike debug item` reports `actualStack=65535`, `maxCharges=65535`, and a PNG-backed icon source when CID and the data PNG are loaded.
The first binocular ping opens the picker if no default is set.
The picker can scroll through all enabled target-compatible rows.
Clicking a row opens a confirmation panel.
CONFIRM starts the same RP/token/cooldown/executor path as /strike <id>.
The confirmed first-use selection is saved as the player's default.
The next binocular ping attempts the saved default without /strike debugping.
`/strike status` reports active calls during warning/inbound execution.
`/strike cancel` can cancel and refund/restore before impact begins.
Heavy strikes create a native public warning map marker while configured on.
`AuditWebhooks` config exists, remains disabled by default, and does not change local audit behavior.
`General.NotifyCallerTeamOnAcceptedStrike=true` by default sends accepted-strike notices to online Rust teammates.
`General.NotifyNearbyPlayersOnHeavyStrikes=false` by default keeps nearby heavy-strike warnings off until deliberately enabled.
`/strike debug warnings beancan_drop` previews the online team recipients that would receive a warning for the current target.
Accepted calls print a `Warning fanout:` console line and include warning counts in `/strike debug history`.
Drone, aircraft, and mortar source visuals appear for supported strike families while `DeliveryVisuals.Enabled=true`.
Drone/aircraft flyovers have repeated positional sound cues while `DeliveryVisuals.SpawnFlyoverSoundEffects=true`, using autoload-safe cue prefabs.
Heavy-drop and MLRS strikes no longer spam client overlays requiring `AssetScene-props.other` for `projectile-flight-large` or `pfx_mlrs_rocket_thrust`.
Rotor-wash effect sends are disabled; drone/attack-heli flyovers should not add `rotorwashparticles_*` client overlay errors.
Old v0.1.18/v0.1.19 configs with `DeliveryVisuals.VisualMoveIntervalSeconds=0.2` or `0.1` migrate to `0.04`.
MLRS aircraft flyovers use `DeliveryVisuals.MlrsAircraftFlyoverHeight` and the same approach direction as the MLRS rocket salvo.
Mortar strikes can show a temporary `Raidlands Artillery` NPC crew visual while `DeliveryVisuals.SpawnMortarCrewNpc=true`.
All visual entities are removed after completion/failure/cancel/unload without killing native payload projectiles early.
When `AuditWebhooks.Enabled=true` and a valid Discord webhook URL is configured, selected audit results should send Discord embeds, but this requires separate real-webhook verification.
Default-disabled `mini_mlrs`, `full_mlrs`, `homing_heli`, and `homing_jet` do not appear unless enabled in config.
When `homing_heli` is deliberately enabled, it requires a fresh tracked vehicle target.
Monument blocking remains off until `General.BlockMonuments=true`.
```

## Deferred Work

Defer these until after one direct-command strike works:

- Kit/VIP documentation polish beyond current give commands and permissions.
- Loot injection live tuning/smoke, per user direction to defer loot injection until the end.
- Clan adapter beyond Rust team cooldown keys.

## Upload And Reload Handoff

Current live-server upload for v0.1.30:

- `oxide/plugins/PortableAirstrikes.cs`
- `oxide/plugins/StackSizeController.cs`
- `oxide/config/PortableAirstrikes.json`
- `oxide/config/StackSizeController.json`

Runtime dependencies to keep from earlier passes:

- `oxide/config/StackSizeController.json`
- `oxide/plugins/CustomItemDefinitions.cs`
- `oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png`

Current reload:

```text
oxide.reload CustomItemDefinitions
oxide.reload StackSizeController
oxide.reload PortableAirstrikes
```

Docs-only changes, including this file, do not need to be uploaded to the live Rust server for runtime behavior.
The v0.1.30 runtime change requires PortableAirstrikes plus StackSizeController plugin/config updates. Keep `CustomItemDefinitions.cs` and the Oxide data PNG from earlier passes on the live server because PortableAirstrikes still registers the CID item and stores that PNG in Rust FileStorage for the item icon.

When the next code pass changes the plugin, create or update a dedicated upload note instead of mixing it into unrelated manifests.
