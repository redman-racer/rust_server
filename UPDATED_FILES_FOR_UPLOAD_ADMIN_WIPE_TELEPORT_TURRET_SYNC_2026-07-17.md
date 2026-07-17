# Admin wipe, teleport, turret, and repository sync

Date: 2026-07-17

## Files to upload

- `oxide/plugins/LiveAdmin.cs`
- `oxide/plugins/NTeleportation.cs`
- `oxide/plugins/ToolCupboardTurrets.cs`
- `oxide/config/DiscordRoles.json`
- `oxide/config/LiveAdmin.json`
- `runds.sh`
- `.gitattributes`

`oxide/config/LiveAdmin.json` now enables offline first-Thursday force wipes while normal weekly wipes continue to preserve blueprints.

## LiveAdmin 0.8.6

- Automatic map wipes now resolve a concrete numeric seed and retain the configured 3500 world size.
- The next seed, world size, and custom map URL are applied as server convars and persisted to the active identity's `cfg/server.cfg` before restart.
- Normal wipes continue to preserve blueprints and do not depend on unsafe live save-file deletion.
- Wipes are recorded as pending until the restarted server verifies the expected seed, world size, and level URL.
- `LastWipeUtc` is updated only after successful post-restart verification; mismatches are written as failed audit entries.

## LiveAdmin 0.9.0 monthly force wipes

- The first configured Thursday of each month uses the existing `ForceWipe` schedule mode.
- Force wipes write a validated offline cleanup marker before the restart and enable blueprint wiping independently of normal weekly wipes.
- `runds.sh` moves the marker into a processing state, validates every field, deletes only top-level map saves/maps and `player.blueprints*.db*` files beneath the configured identity root, and writes a cleanup receipt.
- The force-wipe launch explicitly passes the generated seed and configured 3500 world size to `RustDedicated`.
- LiveAdmin requires a matching cleanup receipt plus the expected running seed, world size, and level URL before recording force-wipe success.
- If marker creation fails, the scheduled wipe is not advanced and shutdown is cancelled so it can safely retry.
- The server must be launched through the updated `runds.sh`; deployments that bypass it cannot perform or verify offline force-wipe cleanup.

## NTeleportation 1.9.9

- Adds `Raidlands: Use Native Teleport Without Loading Screen`, enabled by default.
- Connected-player teleports use Rust's native teleport flow instead of forcing the legacy loading screen and complete snapshot sequence.
- Removes manual snapshot flag clearing that could race the client snapshot stream and cause black screens or `Unresponsive` disconnects.
- Keeps the legacy loading-screen path available when explicitly configured, including a fallback wake attempt.

## Tool Cupboard Turrets 1.3.10

- Removes the redundant 150-metre `Vis.Entities` minicopter scan from every SAM target-list hook.
- Filters Rust's supplied SAM candidate list in place, retaining occupant and authorization rules while avoiding repeated physics/world scans.

## Discord token handling

- Replaces the committed Discord bot token with `${DISCORD_BOT_TOKEN}`.
- Keep the real token only in the ignored `oxide/config/Secrets.local.json` server file.
- Tokens previously committed to repository history must be revoked and regenerated in Discord; replacing the current file does not erase historical exposure.

## Repository synchronization

- Fetched `origin/main` and rebased the local branch onto commit `bb65d8a` before preparing this update.
- Git recognized the earlier live-backup/category-despawn commit as already present upstream and replayed the remaining local TimeOfDay/permission commit.
