Portable Airstrikes vehicle delivery pass - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.31.
- Added `ConfigVersion=29`.
- Kept destroyable delivery vehicles enabled, live-carrier payload gating enabled, and no-refund intercept behavior.
- Added per-delivery first-payload approach timing:
  - drones: `1.5s`
  - attack-heli heavy/rocket/homing carriers: `7s`
  - cargo-plane heavy carriers: `9s`
  - A-10 cargo-plane stand-in: `8s`
  - MLRS cargo-plane stand-in: `12s`
- Added delivery flight plans so carriers reach the correct visible release point when the first payload starts.
- Cargo-plane-backed heavy/A-10/MLRS visuals now use native `CargoPlane` route setup instead of the generic visual spawn/move path.
- Generic attack-heli visual spawn no longer uses risky pre-spawn `SetFlagLocal` or creator setup, and spawn warnings include the failed phase.
- Existing strike enabled/disabled choices were preserved. Homing remains gated/disabled in the live config.

Rust server files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/config/PortableAirstrikes.json

Local source/config hashes:
- oxide/plugins/PortableAirstrikes.cs SHA256: 2BEF4FCE9D0D5F3A0DEF5E5D4427FF598A6D23B1C07C214DD1869CC137DB2555
- oxide/config/PortableAirstrikes.json SHA256: 1C2C047ED4BB110860BD5C16668B948F8CB0C3AE36EC5E7718ACA48F6C5B61F4

Runtime dependencies to keep uploaded from earlier passes:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/plugins/StackSizeController.cs
- oxide/config/StackSizeController.json
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png

Reload after upload:
- oxide.reload PortableAirstrikes

Live smoke:
- Confirm the PortableAirstrikes reload banner reports v0.1.31.
- Run `/strike debugping`, then `/strike rocket_run`.
  Expected: no aircraft visual null-ref warning, and an attack-heli approach is visible/audible before rockets release.
- Run `/strike debugping`, then `/strike propane_bomb_drop`.
  Expected: cargo-plane approach, payload release near target, and visual cleanup.
- Run `/strike debugping`, then `/strike a10_strafe`.
  Expected: A-10 stand-in approach and strafe timing align with the visible line start.
- If MLRS is deliberately enabled, run `mini_mlrs` in a safe open area.
  Expected: the aircraft reaches the MLRS launch standoff before rockets release.
- Shoot down one inbound carrier before release.
  Expected: strike is audited as intercepted, unreleased payload timers are cancelled, and no refund/cooldown restore occurs by default.
- Shoot down one carrier mid-run.
  Expected: already released payloads continue while unreleased payload timers are cancelled.
- Let one carrier finish release or shoot it down after all payloads release.
  Expected: only the visual carrier is removed.
- Check `/strike debug history 10` and `/strike debug stats`.

Local verification:
- `oxide/config/PortableAirstrikes.json` parsed successfully with `ConfigVersion=29`.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.31 against `RustDedicated_Data/Managed` with `Oxide.References.dll` and `glTFast.Newtonsoft.dll` excluded.
- Remaining compile warnings are the existing Unity `Rigidbody.velocity` deprecation warnings in the visual vehicle helper.
- Direct trailing-whitespace scan passed for the changed plugin/config/docs/upload files.

Known live-verification boundary:
- Local compile verifies code shape only. The live server still needs the aircraft visual smoke and intercept tests above.
