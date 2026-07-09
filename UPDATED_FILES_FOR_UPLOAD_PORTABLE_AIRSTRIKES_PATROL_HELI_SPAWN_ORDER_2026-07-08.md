Portable Airstrikes patrol-heli spawn-order fix - 2026-07-08

What changed:
- PortableAirstrikes is now v0.1.35.
- Fixed the native patrol-heli visual spawn order for rocket-run delivery visuals.
- The patrol-heli prefab now keeps its prefab GUID references intact until after `Spawn()`, which should stop the live `GUIDToPath: guid is empty` message followed by `spawn patrol helicopter failed: Object reference not set to an instance of an object`.
- After spawn, the visual still disables the native patrol-heli brain/AI and clears optional gib/fireball/map-marker references so the scripted flyover owns the route.
- `ConfigVersion` remains `30`; no config migration was added.
- Existing payload firing, warning fanout, RP/token/cooldown, warning marker, audit history, cargo-plane/F-15/drone/mortar visuals, homing gates, and destroyable-carrier behavior were left unchanged.

Rust server files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/config/PortableAirstrikes.json only if the v0.1.32+ vehicle-alignment config has not already been uploaded

Local source/config hashes:
- oxide/plugins/PortableAirstrikes.cs SHA256: 14C3E77CA32A07722DD1786A0BB4A9A2D68697F177FCC7CBA664032230199E5E
- oxide/config/PortableAirstrikes.json SHA256: 917F3170D61FC0EEF1809CD0F9F3133392D4B153ED1954091D5E7184F7CE669B

Runtime dependencies to keep uploaded:
- oxide/plugins/CustomItemDefinitions.cs
- oxide/plugins/StackSizeController.cs
- oxide/config/StackSizeController.json
- oxide/data/PortableAirstrikes/airstrike-targeting-binoculars.png

Reload after upload:
- oxide.reload PortableAirstrikes

Quick live smoke:
- Confirm the PortableAirstrikes reload banner reports v0.1.35.
- `/strike debugping`, then `/strike rocket_run`; confirm a native patrol-heli approach before rockets release and no `GUIDToPath: guid is empty` or `visual rocket run could not spawn` warning.
- `/strike debugping`, then `/strike hv_rocket_run`; confirm the same patrol-heli approach.
- `/strike debugping`, then `/strike incendiary_rocket_run`; confirm the same patrol-heli approach.
- Check `/strike debug history 10` and `/strike debug stats`.
- If the patrol-heli visual still does not appear, set `General.DebugMode=true`, reload, repeat `/strike rocket_run`, and capture the new phase-specific warning.

Validation performed locally:
- `oxide/config/PortableAirstrikes.json` parsed successfully.
- Roslyn compile check passed for `oxide/plugins/PortableAirstrikes.cs` v0.1.35 against `RustDedicated_Data/Managed`; remaining warnings are the existing Unity `Rigidbody.velocity` deprecation warnings in visual movement helpers.
- Live Oxide reload/smoke is still required because the local compile cannot prove client rendering.
