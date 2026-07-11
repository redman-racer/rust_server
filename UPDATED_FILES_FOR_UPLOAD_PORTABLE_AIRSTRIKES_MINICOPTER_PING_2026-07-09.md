# Portable Airstrikes Minicopter Ping Recovery - 2026-07-09

## What changed

- `PortableAirstrikes` is now v0.1.42.
- Airstrike binocular pings now include Rust's `Vehicle Detailed` layer when resolving the aimed target.
- If the ping's associated entity is a mounted pilot/passenger, target capture now normalizes that player back to the mounted vehicle before storing the target.
- Tool pings now try a small vehicle-only sphere cast along the player's aim before accepting ground fallback.
- The nearby vehicle recovery radius was widened from 8m to 16m for cases where Rust places the ping position slightly off the minicopter body.
- Line-of-sight validation now normalizes child/seat collider hits back to the stored vehicle before rejecting the target.

## Rust server files to upload

- `oxide/plugins/PortableAirstrikes.cs`

No config upload is required for this v0.1.42 fix.

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

1. Confirm the reload banner reports `PortableAirstrikes v0.1.42`.
2. Confirm `homing_heli` or `homing_jet` is enabled in `oxide/config/PortableAirstrikes.json` and the tester has the matching permission.
3. Hold `Airstrike Targeting Binoculars`, aim directly at a minicopter, and place a ping.
4. Run `/strike`.
5. Expected: the stored target is a `vehicle ping`, and enabled homing rows appear instead of only ground strike rows.
6. Run `/strike homing_heli` or `/strike homing_jet`.
7. Expected: the command starts the homing strike instead of failing with a ground-target mismatch.

## Local verification

- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.42 against `RustDedicated_Data/Managed`; remaining warnings were the existing Unity `Rigidbody.velocity` deprecation warnings in visual movement helpers.

## Local source hash

- `oxide/plugins/PortableAirstrikes.cs` SHA256: `79730B40D41EAD560D9AD8625B2CAEF7527A2DFD6F1F480910DAD8E66A73A41C`

## Notes

- This is a target-capture fix only; homing damage, RP costs, permissions, cooldowns, and strike balance were not changed.
- Live smoke is still required because local compile cannot prove Rust client ping association or live vehicle collider behavior.
