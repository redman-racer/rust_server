# RaidlandsEvents 0.6.3 upload manifest

Upload these files while preserving their paths:

- `oxide/plugins/RaidlandsRoamBots.cs`
- `oxide/plugins/RaidlandsEvents.cs`
- `oxide/config/RaidlandsEvents.json`

Operator documentation:

- `Docs/events-manager/RaidlandsEvents_0.6.3_Changes_2026-07-30.md`
- `UPDATED_FILES_FOR_UPLOAD_RAIDLANDS_EVENTS_0.6.3_2026-07-30.md`

Reload order:

```text
oxide.reload RaidlandsRoamBots
oxide.reload RaidlandsEvents
```

Do not replace:

- `oxide/data/RaidlandsEvents.json`
- `oxide/data/RaidlandsRoamBots/*`

Those locations contain live state and the persisted per-administrator debug
window preference.
