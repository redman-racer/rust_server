Raidlands Sentry Support-Loss Drop
==================================

Upload these files:

- `oxide/plugins/RaidlandsSentryTurrets.cs`
- `oxide/config/RaidlandsSentryTurrets.json`

Server reload:

- `oxide.reload RaidlandsSentryTurrets`

Notes:

- `RaidlandsSentryTurrets` is now v1.0.12.
- New config block: `Support Loss Drop`.
- Default behavior is enabled with a 300 second delay.
- When a managed Outpost Sentry Turret loses the entity it was placed on, the plugin waits the configured delay, confirms the support is still missing, drops an `Outpost Sentry Turret` loose item, then removes the world sentry.
- Loose item cleanup is left to normal Rust loose-loot despawn behavior.
