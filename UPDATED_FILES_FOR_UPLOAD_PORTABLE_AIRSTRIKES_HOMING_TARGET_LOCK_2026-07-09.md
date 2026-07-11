# Portable Airstrikes Homing Target Lock Fix - 2026-07-09

## What changed

- `PortableAirstrikes` is now v0.1.41.
- Binocular tool pings now prefer an associated ping entity before accepting the fallback raycast result.
- Raycast and stored ping entities now normalize child/seat hits back to their parent vehicle when possible.
- If the immediate ping data/raycast looks like ground, the tool ping does a small nearby-vehicle search before falling back to a ground target.
- Homing validation/tracking now resolves stored entity IDs through the same vehicle normalizer.

## Rust server files to upload

- `oxide/plugins/PortableAirstrikes.cs`

No config upload is required for this v0.1.41 fix.

## Runtime dependencies to keep from earlier passes

- `oxide/plugins/CustomItemDefinitions.cs`
- `oxide/plugins/StackSizeController.cs`
- `oxide/config/StackSizeController.json`
- `oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png`

## Reload after upload

```text
oxide.reload PortableAirstrikes
```

## Quick live smoke

1. Confirm the reload banner reports `PortableAirstrikes v0.1.41`.
2. Confirm `homing_heli` or `homing_jet` is enabled in `oxide/config/PortableAirstrikes.json` and the tester has the matching permission.
3. Hold `Airstrike Targeting Binoculars`, aim directly at a minicopter, and place a ping.
4. Run `/strike`.
5. Expected: the stored target is a `vehicle ping`, and enabled homing rows appear instead of only ground strike rows.
6. Run `/strike homing_heli` or `/strike homing_jet`.
7. Expected: the command starts the homing strike instead of failing with a ground-target mismatch.

## Local verification

- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.41 against `RustDedicated_Data/Managed`; remaining warnings were the existing Unity `Rigidbody.velocity` deprecation warnings in visual movement helpers.
- JSON parse check passed for `oxide/config/PortableAirstrikes.json`.
- Targeted `git diff --check` passed for `oxide/plugins/PortableAirstrikes.cs`.

## Local source hash

- `oxide/plugins/PortableAirstrikes.cs` SHA256: `DF897724DCE94D0AC47B858B2BBB047D34646407D681F5B7F9CD290493F5C78E`

## Notes

- This is a target-capture fix, not a homing damage/balance change.
- Live smoke is still required because local compile cannot prove Rust client ping association or vehicle marker behavior.
