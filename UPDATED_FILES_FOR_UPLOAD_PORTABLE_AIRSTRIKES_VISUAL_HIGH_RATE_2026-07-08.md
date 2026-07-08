Portable Airstrikes high-rate visual movement and repeated flyover sound cues - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.20.
- Added `ConfigVersion=20`.
- Added `DeliveryVisuals.SpawnRotorWashEffects`, default `true`.
- Added `DeliveryVisuals.FlyoverSoundIntervalSeconds`, default `0.75`.
- Changed the default `DeliveryVisuals.VisualMoveIntervalSeconds` from `0.1` to `0.04`.
- Old v0.1.18/v0.1.19 configs still using `VisualMoveIntervalSeconds=0.2` or `0.1` migrate to `0.04` on reload.
- Scripted flyover movement now sends immediate network updates at the tighter interval instead of queued `0.1`-second updates.
- Visual entities set their local `On` flag when spawned.
- Player-helicopter visuals attempt to keep fuel state active and finish engine startup so client-side vehicle audio/animation has a better chance to activate.
- Drone, attack-heli, cargo-plane, and MLRS aircraft flyovers now schedule repeated positional sound bursts along the flight path instead of only start/midpoint cues.
- Sound bursts now combine vehicle-engine, projectile-flight, large fast-falloff, bullet-flyby, and optional rotor wash effects depending on the visual type.
- Existing direct/CUI validation, RP/token/cooldown/refund behavior, payload timing/damage, warning fanout, heavy warning markers, audit/webhook behavior, default-disabled homing/MLRS gates, loot behavior, and monument blocking behavior were left unchanged.

Rust server plugin file to upload:
- oxide/plugins/PortableAirstrikes.cs

Local source hash:
- SHA256: E0B6881A61357AA0A9D5E66FB1B39649B22D977075D6A30BDB11E9CE309567D1

Reload after upload:
- oxide.reload PortableAirstrikes

Safe visual smoke:
- /strike debugping
- /strike beancan_drop
- /strike debugping
- /strike a10_strafe
- /strike debugping
- /strike mortar_he
- /strike debug history 5
- /strike debug stats

Optional enabled-strike smoke:
- /strike debugping
- /strike mini_mlrs

Expected:
- Plugin banner reports v0.1.20.
- Config saves `ConfigVersion=20`.
- `DeliveryVisuals.VisualMoveIntervalSeconds` is `0.04` unless you deliberately customized it.
- `DeliveryVisuals.SpawnFlyoverSoundEffects=true`.
- `DeliveryVisuals.SpawnRotorWashEffects=true`.
- `DeliveryVisuals.FlyoverSoundIntervalSeconds=0.75`.
- Drone and attack-heli/cargo-plane flyovers should show less visible stepping than v0.1.19.
- Drone and aircraft flyovers should emit repeated positional sound cues while `DeliveryVisuals.SpawnFlyoverSoundEffects=true`.
- Drone and attack-heli flyovers may show rotor wash effects while `DeliveryVisuals.SpawnRotorWashEffects=true`.
- Visuals clean up after completion/failure/cancel/unload without killing native payload projectiles early.

Known caveat:
- The local compile and asset checks can confirm the code path and prefab names, but the real proof is live-client smoke. If v0.1.20 still looks like jumpy manual teleporting or remains silent, the next implementation should stop pushing the scripted-entity approach and switch to a different visual strategy, such as effect-only/tracer paths or native moving event/vehicle entities that the Rust client already interpolates and sounds correctly.

Local verification:
- Confirmed v0.1.20 sound/effect prefabs exist in Bundles/AssetSceneManifest.json for projectile-flight-large, large-sound-fast-falloff, and large/small rotor wash effects.
- Roslyn compile check passed for oxide/plugins/PortableAirstrikes.cs against RustDedicated_Data/Managed with Oxide.References.dll and glTFast.Newtonsoft.dll excluded.
- Direct trailing-whitespace scan passed for oxide/plugins/PortableAirstrikes.cs, Docs/AirStrikes/portable_airstrikes_development_log.md, UPDATED_FILES_FOR_UPLOAD.txt, and UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_VISUAL_HIGH_RATE_2026-07-08.md.
