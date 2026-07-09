# PortableAirstrikesAnimationEditor waypoint marker update - 2026-07-09

## Upload

- `oxide/plugins/PortableAirstrikesAnimationEditor.cs`

## Reload

```text
oxide.reload PortableAirstrikesAnimationEditor
```

## In-game check

```text
/airanim edit <profileId>
/airanim markers
```

Expected result: waypoint markers render as client-side world bubbles with an arrow inside each bubble. The arrow follows the profile's 3D path tangent, and the small red/green attitude ticks show the marker's right/up orientation.

## Local verification

- Roslyn compile check passed for `oxide/plugins/PortableAirstrikesAnimationEditor.cs` against `RustDedicated_Data/Managed`.
- Remaining warnings were the existing Unity `Rigidbody.velocity` deprecation warnings in the preview movement helpers.
