Portable Airstrikes patrol-heli visual fix - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.33.
- Added a dedicated native `PatrolHelicopter` flyover path for `attack_heli` delivery visuals.
- Patrol-heli visuals now clear server gib, fireball, map-marker, and flee-marker prefab references, then disable the native patrol-heli brain/AI after spawn so the carrier follows the scripted rocket-run path.
- Generic visual movement updates now fail soft with a debug warning instead of letting a native entity transform/network quirk break the scheduled flyover.
- `ConfigVersion` remains `30`; this pass has no config migration.
- Existing cargo-plane, F-15, drone, mortar, payload, RP/token/cooldown, warning marker, audit history, homing gate, and destroyable-carrier behavior were left unchanged.

Rust server files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/config/PortableAirstrikes.json only if the v0.1.32 vehicle-alignment config has not already been uploaded

Local source/config hashes:
- oxide/plugins/PortableAirstrikes.cs SHA256: 1DB7280B8DD613B07259EC8EDB82919DB140E6DC31F5771E141C3A6C0F783246
- oxide/config/PortableAirstrikes.json SHA256: C2A30F4BD8BB257C8021388869C449FBCFD537A6FF80B93F8772841C5DD10CA0

Runtime dependencies to keep uploaded:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/plugins/StackSizeController.cs
- oxide/config/StackSizeController.json
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png

Reload after upload:
- oxide.reload PortableAirstrikes

Quick live smoke:
- Confirm the PortableAirstrikes reload banner reports v0.1.33.
- `/strike debugping`, then `/strike rocket_run`; confirm a native patrol-heli approach before rockets release and no `visual rocket run could not spawn` warning.
- `/strike debugping`, then `/strike hv_rocket_run`; confirm the same patrol-heli approach.
- `/strike debugping`, then `/strike incendiary_rocket_run`; confirm the same patrol-heli approach.
- Check `/strike debug history 10` and `/strike debug stats`.
- If the patrol-heli visual still does not appear, turn on `General.DebugMode=true`, reload, repeat `/strike rocket_run`, and capture the new phase-specific warning.

Validation performed locally:
- Reviewed `oxide/logs/oxide_2026-07-08.txt` and found the prior live warning: `rocket_run visual rocket run could not spawn: Object reference not set to an instance of an object`.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.33 against `RustDedicated_Data/Managed` with `Oxide.References.dll` and `glTFast.Newtonsoft.dll` excluded.
- Remaining compile warnings are Unity `Rigidbody.velocity` deprecation warnings in the visual movement helpers.
