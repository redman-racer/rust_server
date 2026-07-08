Turret/SAM/sentry switch placement and world-entity cleanup - 2026-07-08

What changed:
- Updated `TurretSwitches` to v1.8.
- Player-placed auto turrets now position their switch on the side closest to the placing player.
- Player-placed SAM sites now position their switch on the side closest to the placing player.
- Player-owned Raidlands Outpost Sentry Turrets still use the existing `RaidlandsSentryTurrets` compatibility call, but `TurretSwitches` now honors the same player-side positioning.
- Switch creation is now limited to player-owned entities with a Steam owner ID, except during the immediate successful player placement hook.
- Ownerless monument/outpost sentries and SAM sites are skipped.
- Ownerless turret/SAM entities have stale simple-switch child entities removed during plugin load/spawn repair.
- Existing player-owned switch children are adopted/repositioned instead of duplicated.

Rust server plugin files to upload:
- oxide/plugins/TurretSwitches.cs

No config upload is required:
- Existing `oxide/config/TurretSwitches.json` can stay live.
- Current live config has `RequiresPermission=false`; this patch removes the world-entity switch attachments that made that dangerous at monuments/outpost.

Reload after upload:
- oxide.reload TurretSwitches

Primary live smoke:
- Reload `TurretSwitches`.
- Confirm Outpost/monument sentries no longer have visible simple switches.
- Confirm monument/static SAM sites no longer have visible simple switches.
- Place a normal auto turret while standing on one side of it; expected switch appears on that same side.
- Place a SAM site while standing on one side of it; expected switch appears on that same side.
- Place a Raidlands Outpost Sentry Turret item; after the replacement succeeds, expected switch appears on the side where the placing player stood.

Useful sentry check:
- Run `raidlands.sentry.scan all`.
- Expected: native ownerless Outpost sentries should not report `copySwitch=True` after the reload/cleanup.

Local verification:
- Roslyn compile check passed for `oxide/plugins/TurretSwitches.cs` against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and `glTFast.Newtonsoft.dll`.
- Only obsolete `SetFlag` warnings remained; no compile errors.
- Live Oxide reload and in-game placement/outpost visual smoke are still required to prove runtime behavior.
