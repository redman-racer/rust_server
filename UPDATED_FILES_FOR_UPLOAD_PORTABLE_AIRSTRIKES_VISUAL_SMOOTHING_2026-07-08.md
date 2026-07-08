Portable Airstrikes visual smoothing, sound cues, and MLRS plane visibility - 2026-07-08

Live test report addressed:
- Drone animation appeared.
- Mortar animations appeared.
- One attack-heli strike completed successfully.
- The MLRS payload worked, but the plane was not visible to the tester.
- Drone and attack-heli visuals were soundless and very laggy/skipping/low-FPS-looking.

What changed:
- Updated `PortableAirstrikes` to v0.1.19.
- Added root config key `ConfigVersion=19`.
- Added `DeliveryVisuals.SpawnFlyoverSoundEffects`, default `true`.
- Added `DeliveryVisuals.MlrsAircraftFlyoverHeight`, default `58`.
- Changed default `DeliveryVisuals.VisualMoveIntervalSeconds` from `0.2` to `0.1`.
- A v0.1.18 config that still has the old default `VisualMoveIntervalSeconds=0.2` is migrated to `0.1` on reload.
- Scripted flyover movement now uses queued network updates instead of immediate network updates on every movement tick.
- Drone and aircraft flyovers now play positional sound cues while `DeliveryVisuals.SpawnFlyoverSoundEffects=true`.
- Rocket-run projectiles now trigger a rocket launch effect at the spawn point.
- MLRS rockets now trigger MLRS backfire/thrust effects at launch.
- MLRS aircraft visuals now use the same approach direction as the MLRS rocket salvo and a lower MLRS-specific flyover height.
- Existing validation, RP/token/cooldown/refund behavior, payload timing/damage, warning fanout, heavy warning markers, audit/webhook behavior, default-disabled homing/MLRS gates, loot behavior, and monument blocking behavior were left unchanged.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: 87B5AFB7E1BC757EAD7D6A315EF29F1E8865535A58957E6C185474B784871D1F

Docs/reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_VISUAL_SMOOTHING_2026-07-08.md
- UPDATED_FILES_FOR_UPLOAD.txt

Config upload:
- No config upload is required for safe/default behavior.
- Reloading v0.1.19 will save `ConfigVersion=19` and add:
  - `DeliveryVisuals.SpawnFlyoverSoundEffects`
  - `DeliveryVisuals.MlrsAircraftFlyoverHeight`
- If flyover sounds are too noisy, set:
  - `DeliveryVisuals.SpawnFlyoverSoundEffects=false`
- If visuals still look too choppy, try:
  - `DeliveryVisuals.VisualMoveIntervalSeconds=0.08`
- If visuals add too much network/server load, try:
  - `DeliveryVisuals.VisualMoveIntervalSeconds=0.15`
- To disable all temporary visuals quickly, set:
  - `DeliveryVisuals.Enabled=false`

Reload after upload:
- oxide.reload PortableAirstrikes

Safe visual smoke:
- `oxide.reload PortableAirstrikes`
- `/strike debugping`
- `/strike beancan_drop`
- `/strike debugping`
- `/strike a10_strafe`
- `/strike debugping`
- `/strike mortar_he`
- `/strike debug history 5`
- `/strike debug stats`

Optional MLRS visual smoke:
- Keep MLRS default-disabled unless deliberately testing it.
- If `mini_mlrs` is already enabled for smoke, run:
  - `/strike debugping`
  - `/strike mini_mlrs`
- Confirm the lower cargo-plane/jet stand-in crosses the watched impact area while the MLRS rockets launch.

Expected:
- Plugin banner reports v0.1.19.
- Drone and attack-heli/cargo-plane flyovers should look smoother than v0.1.18.
- Drone and aircraft flyovers should have positional sound cues while `DeliveryVisuals.SpawnFlyoverSoundEffects=true`.
- MLRS should show a lower aircraft pass aligned with the same approach direction as the rocket salvo.
- Mortar visuals and payload behavior should remain unchanged except for the existing mortar muzzle/deploy effects.
- Visuals clean up after completion/failure/cancel/unload.
- Payloads still complete normally and are not killed early by visual cleanup.

Known unresolved verification points:
- Live client-side smoothness and sound audibility need in-game smoke after upload/reload.
- Rust sound-template prefabs are asset-verified locally, but audible range/volume still need live confirmation.
- Temporary aircraft entities are plugin-moved visual props, not autonomous aircraft combat logic.
- The optional mortar crew NPC has player-target sensing suppressed, but live Rust AI behavior should still be watched during the first reload/smoke.
- Heavy warning marker size/visibility, nearby warning radius/noise, and recipient-side chat visibility still need live tuning with other players online.
- Live Rust/uMod still needs confirmation that binocular/team pings include vehicle entity IDs on this server build.
- Homing missile tracking/damage remains locally compiled but should stay gated until a safe vehicle test is available.
- Native projectile/grenade/shell damage attribution still depends on Rust/uMod behavior, though spawned payload entities set `OwnerID` and creator entity.
- Loot injection live tuning/smoke is intentionally deferred until the end.

Local verification:
- Confirmed the sound/effect prefabs exist in `Bundles/AssetSceneManifest.json` for drone deploy, dangerous vehicle engine, bullet flyby, rocket launch, MLRS backfire, and MLRS rocket thrust.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- Direct trailing-whitespace scan passed for `oxide/plugins/PortableAirstrikes.cs`, `Docs/AirStrikes/portable_airstrikes_development_log.md`, `UPDATED_FILES_FOR_UPLOAD.txt`, and this upload note.
