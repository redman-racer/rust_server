# PortableAirstrikes F15 rotation runtime fix - 2026-07-11

Upload this file:

- `oxide/plugins/PortableAirstrikes.cs`

What changed:

- Compiled visual tracks for the F15 now opt into authoritative runtime rotation.
- The scripted visual movement path now pushes F15 rotation directly into the entity transform and rigidbody every movement tick.
- Other aircraft keep the existing scripted rotation behavior unless they explicitly opt into the same compiled-track flag later.

Validation:

- `git diff --check -- oxide/plugins/PortableAirstrikes.cs`
- Roslyn compile passed for `oxide/plugins/PortableAirstrikes.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- Compile warnings were the existing Unity `Rigidbody.velocity` deprecation warnings in visual movement/carrier helpers.

After upload:

```text
oxide.reload PortableAirstrikes
```

Recommended live smoke test:

- Run an F15 profile with obvious authored roll/pitch/yaw changes from the animation editor.
- Confirm the in-game F15 attitude follows the website-previewed compiled track rather than drifting back to the prefab/controller facing.
