# Portable Airstrikes editor-parity playback repair - 2026-07-10

## What changed

- `PortableAirstrikes` is now v0.1.48.
- Live scripted delivery vehicles now play the same per-waypoint XYZ rotation offsets shown by `/airanim preview`.
- Rotation offsets interpolate with the same `StopAtWaypoints` mode and `RotationSmoothTimeSeconds` value as the editor.
- Authored profiles now keep their exact duration, waypoint timestamps, first-payload time, and terrain-clearance settings at runtime instead of being silently stretched to executor timing.
- The strike call remains active for at least the authored profile duration so executor completion cannot cut the animation off early.
- This fixes attack-heli rolls/rollovers being flattened into straight, travel-direction-only flight during the real strike.
- Native cargo-plane routing remains native in both editor and runtime.

## Rust server file to upload

- `oxide/plugins/PortableAirstrikes.cs`

Do not replace `oxide/data/PortableAirstrikes/VisualProfiles.json` for this code-only repair. Keep the live profile that contains the authored attack-heli rollover.

## Reload after upload

```text
oxide.reload PortableAirstrikes
```

## Quick live smoke

- Confirm the reload banner reports `PortableAirstrikes v0.1.48`.
- Preview the attack-heli profile in `/airanim` and note the rollover waypoint and rocket-release timing.
- Trigger a strike assigned to that same profile.
- Confirm the live helicopter follows the same path, performs the same roll/rollover, and launches the rockets at the same authored point in the animation.
- Test once with `StopAtWaypoints=true` and once with it disabled if that profile uses both modes; position and rotation should match the corresponding editor preview.
- Confirm the carrier remains present for all authored release events and is cleaned up when the profile duration ends.

## Local verification

- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.48 against `RustDedicated_Data/Managed`.
- Remaining warnings are the existing Unity `Rigidbody.velocity` deprecation warnings in visual movement/carrier velocity helpers.
- Targeted JSON parse and `git diff --check` checks passed.

## Local source hash

- `oxide/plugins/PortableAirstrikes.cs` SHA256: `FF7C7D6D5D0AA5EAF35CD917B3F21413B23D8CF065F634E7F1DEE6640576C63F`
