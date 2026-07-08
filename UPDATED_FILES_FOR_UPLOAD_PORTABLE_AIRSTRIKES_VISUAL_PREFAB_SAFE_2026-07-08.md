Portable Airstrikes client-safe visual prefab fix - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.28.
- Flyover air-movement sound cues now use `assets/content/sound/templates/projectile-flight.prefab` instead of `assets/content/sound/templates/projectile-flight-large.prefab`.
- MLRS launch visuals no longer send the direct `assets/content/vehicles/mlrs/effects/pfx_mlrs_rocket_thrust.prefab` effect.
- The physical heavy-drop payloads, MLRS rocket entities, RP/token/cooldown flow, warnings, webhooks, and item charges were left unchanged.
- No config upload or migration is required; `ConfigVersion` remains 25.

Why:
- The client error overlay showed `projectile-flight-large.prefab` and `pfx_mlrs_rocket_thrust.prefab` requiring `AssetScene-props.other` while airstrike payloads were falling.
- The server console stays clean because these are client-side asset-scene dependency errors from cosmetic effect dispatch, not server exceptions.
- `projectile-flight.prefab`, `dangerous-vehicle-engine.prefab`, `bullet-flyby.prefab`, `large-sound-fast-falloff.prefab`, and `pfx_mlrs_backfire.prefab` are in autoloaded asset scenes in the local manifest.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: BD32EA2D20B32799191ED6076B24AB45121F1E4DC9176B1F22F9FE1F37082884

Runtime dependencies to keep from earlier airstrike passes:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/config/StackSizeController.json
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png

Reload after upload:
- oxide.reload PortableAirstrikes

Quick live smoke:
- Confirm the reload banner reports v0.1.28.
- Run a ground heavy strike such as `/strike propane_bomb_drop`.
- Expected: payloads still deliver, and the client error overlay no longer adds `projectile-flight-large.prefab requires AssetScene-props.other`.
- If `mini_mlrs` or `full_mlrs` is deliberately enabled, run one MLRS strike in a safe area.
- Expected: MLRS rockets still launch, and the client error overlay no longer adds `pfx_mlrs_rocket_thrust.prefab requires AssetScene-props.other`.

Local verification:
- Confirmed `projectile-flight-large.prefab` and `pfx_mlrs_rocket_thrust.prefab` live in non-autoloaded `AssetScene-props.other` in `Bundles/AssetSceneManifest.json`.
- Confirmed replacement/remaining cosmetic cue prefabs used by this path are in autoloaded asset scenes in `Bundles/AssetSceneManifest.json`.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.28 against `RustDedicated_Data/Managed`.

Known live-verification boundary:
- Local compile verifies code shape only. The live server should confirm the client overlay stops receiving those two prefab dependency errors during falling-payload visual delivery.
