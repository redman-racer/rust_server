# Portable Airstrikes waypoint stop/rotation toggle - 2026-07-10

## What changed

- `PortableAirstrikesAnimationEditor` is now v0.1.26.
- `PortableAirstrikes` is now v0.1.46.
- Visual profiles now include `StopAtWaypoints`.
- Existing profiles default to `StopAtWaypoints=true`, preserving the current ease-to-stop behavior.
- `/airanim stopwaypoints <on|off|toggle>` and the `STOP WP ON/OFF` editor button toggle the setting per profile.
- When `StopAtWaypoints=false`, editor preview and live runtime waypoint visuals use blended Hermite-style motion through waypoint timestamps instead of slowing to zero at each waypoint.
- Marker arrows, silent waypoint object outlines, editor previews, and live visuals now derive heading from blended velocity in this mode, so the plane eases rotation through turns instead of pointing perfectly at the next node.

## Rust server files to upload

- `oxide/plugins/PortableAirstrikesAnimationEditor.cs`
- `oxide/plugins/PortableAirstrikes.cs`
- `oxide/data/PortableAirstrikes/VisualProfiles.json`

## Reload after upload

```text
oxide.reload PortableAirstrikesAnimationEditor
oxide.reload PortableAirstrikes
```

## Quick live smoke

- Confirm reload banners report `PortableAirstrikesAnimationEditor v0.1.26` and `PortableAirstrikes v0.1.46`.
- Open `/airanim edit <profileId>` and confirm the profile details panel shows `STOP WP ON`.
- Run `/airanim stopwaypoints off` or click `STOP WP ON/OFF`, then save.
- Start `/airanim preview`; expected result: the vehicle passes through waypoints smoothly instead of easing to a stop at each one, and its nose rotates through the turn instead of snapping to the next segment direction.
- Confirm waypoint bubble arrows and silent object outlines show the blended through-turn direction while `StopAtWaypoints=false`.
- Toggle back on and preview again; expected result: the old waypoint stop/ease behavior returns.
- Trigger a matching live strike profile and confirm live visual speed and rotation match the editor preview mode.

## Local verification

- Roslyn compile check passed for `oxide/plugins/PortableAirstrikesAnimationEditor.cs` v0.1.26 against `RustDedicated_Data/Managed`.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.46 against `RustDedicated_Data/Managed`.
- Remaining warnings were the existing Unity `Rigidbody.velocity` deprecation warnings in visual movement helpers.
- `oxide/data/PortableAirstrikes/VisualProfiles.json` parsed successfully.
- Targeted `git diff --check` passed for the two plugins and `VisualProfiles.json`; Git only warned that LF will be normalized to CRLF when it next touches the plugin files.

## Local source hashes

- `oxide/plugins/PortableAirstrikesAnimationEditor.cs` SHA256: `655A36C9A89571DA12541B4094A828ABA81DEA4637D4759690AB8EDB3BF82D53`
- `oxide/plugins/PortableAirstrikes.cs` SHA256: `68A3AFFA2CB9AB15C59881223660C25EFE199BAB35E435FE4D5190EEC4B0C170`
- `oxide/data/PortableAirstrikes/VisualProfiles.json` SHA256: `38C41DCBE8F7EF823E5835F8766E81D1123B953E2E67FCF02B29F8DD7506CC20`
