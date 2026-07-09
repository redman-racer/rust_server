Portable Airstrikes vehicle asset alignment - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.32.
- Added `ConfigVersion=30`.
- Swapped rocket/helicopter delivery visuals from the player attack-heli prefab to the native patrol helicopter prefab.
- Added the native F-15 prefab for jet-style visuals, enabled MLRS flyovers, and the A-10 BRRRRT visual carrier.
- Heavy-drop visuals now consistently use the native cargo plane profile.
- Migrated default `bee_swarm_heavy` and `firebomb_run` deliveries from `attack_heli` to `cargo_plane_jet`; `propane_bomb_drop` already used `cargo_plane_jet`.
- Heavy-drop timing and carrier health use cargo-plane settings even if an older config still has an old heavy-drop delivery label before migration.
- Confirmed all planned non-homing strike IDs are present in the plugin/config. Homing strike definitions remain present but disabled/gated by default.
- Existing payload behavior, RP/token/cooldowns, warning markers, audit history, and destroyable-carrier intercept semantics were left unchanged.

Rust server files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/config/PortableAirstrikes.json

Local source/config hashes:
- oxide/plugins/PortableAirstrikes.cs SHA256: BEF308794E023CEA53F8DDFDB37A90EEFBF456647A303C0EC0092F67A8755BEA
- oxide/config/PortableAirstrikes.json SHA256: C2A30F4BD8BB257C8021388869C449FBCFD537A6FF80B93F8772841C5DD10CA0

Runtime dependencies to keep uploaded from earlier passes:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/plugins/StackSizeController.cs
- oxide/config/StackSizeController.json
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png

Reload after upload:
- oxide.reload PortableAirstrikes

Live smoke:
- Confirm the PortableAirstrikes reload banner reports v0.1.32.
- Run `/strike debugping`, then `/strike rocket_run`.
  Expected: native patrol-helicopter approach appears before rockets release, then cleans up.
- Run `/strike debugping`, then `/strike bee_swarm_heavy`.
  Expected: native cargo-plane approach appears before payload release, then cleans up.
- Run `/strike debugping`, then `/strike firebomb_run`.
  Expected: native cargo-plane approach appears before payload release, then cleans up.
- Run `/strike debugping`, then `/strike propane_bomb_drop`.
  Expected: native cargo-plane approach appears before payload release, then cleans up.
- Run `/strike debugping`, then `/strike a10_strafe`.
  Expected: native F-15 visual approaches the strafe-line start before BRRRRT pulses begin.
- If MLRS remains deliberately enabled, run `mini_mlrs` in a safe open area.
  Expected: native F-15 visual reaches the MLRS standoff before rockets launch.
- Check `/strike debug history 10` and `/strike debug stats`.
- Keep `homing_heli` and `homing_jet` gated until a separate safe vehicle-target test is explicitly opened.

Local verification:
- `oxide/config/PortableAirstrikes.json` parsed successfully with `ConfigVersion=30`.
- Config audit confirmed `bee_swarm_heavy`, `firebomb_run`, and `propane_bomb_drop` use `cargo_plane_jet`; rocket runs use `attack_heli`; `a10_strafe` uses `a10_gun_run`; `mini_mlrs` and `full_mlrs` use `cargo_plane_jet`; homing remains disabled.
- Asset manifest check confirmed native drone, cargo plane, patrol helicopter, F-15, and mortar visual prefabs exist locally.
- Planned non-homing catalog audit found all 18 expected IDs present and no missing IDs.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.32 against `RustDedicated_Data/Managed` with `Oxide.References.dll` and `glTFast.Newtonsoft.dll` excluded.
- Remaining compile warnings are the existing Unity `Rigidbody.velocity` deprecation warnings in the visual vehicle helper.
- Direct trailing-whitespace scan passed for the changed plugin/config/docs/upload files.

Known live-verification boundary:
- Local compile verifies code shape only. The live server still needs the visual smoke tests above to confirm the patrol-heli and F-15 entities render, move, sound, and clean up acceptably for connected clients.
