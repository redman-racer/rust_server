# RaidlandsEvents 0.6.3

Date: 2026-07-30

## Summary

This patch follows the 0.6.2 Kastro and administrator-UI release. It corrects
guard placement, adds a consistent large-layout terrain-support policy, and
makes the RoamBot diagnostic window easy for each administrator to control.

## Interior raid guards

Previously, event guard positions were generated in a terrain ring outside the
base at `footprint radius + 4m`.

RaidlandsEvents now:

- Builds guard spawn candidates from live pasted foundation and floor blocks.
- Uses the top surface of the actual spawned collider.
- Scores candidates for nearby walls and overhead construction.
- Prefers enclosed interior positions.
- Spreads guards across distinct building surfaces.
- Falls back to other base surfaces when there are not enough enclosed nodes.
- Uses the event center only when no usable building blocks exist.

Existing live guards are not teleported. The corrected placement applies when a
guard group is newly spawned or reconciled after its previous group is gone.

## Nightmare and large-layout placement

The Kastro Nightmare footprint could exhaust 500 cached candidates because the
normal 6 m adaptive support-depth limit was too shallow for a structure of that
size.

New configuration:

```json
"Large Layout Radius Threshold Meters": 45.0,
"Large Layout Maximum Lowering Meters": 15.0
```

Behavior:

- Layouts below the threshold retain the normal 6 m maximum lowering.
- Layouts at or above the threshold may generate supports down to 15 m.
- Runtime terrain validation and CopyPaste adaptive-foundation generation use
  the same calculated limit.
- The validator cannot approve a location that the paste operation is not
  configured to support.

## Per-administrator debug-window controls

The RoamBot diagnostic side panel is no longer controlled only by a global
Events UI switch.

Administrators can use:

```text
/botdebug
/botdebug on
/botdebug off
```

The Events UI now displays `My Bot Debug: VISIBLE` or
`My Bot Debug: HIDDEN`.

The preference:

- Affects only the administrator changing it.
- Immediately removes the panel when hidden.
- Persists in RaidlandsRoamBots data across plugin reloads and server restarts.
- Does not change bot AI, spawning, combat, nameplates, map markers, or another
  administrator's window.

New integration hooks:

- `REBOT_GetDebugSidePanelVisible(ulong userId)`
- `REBOT_SetDebugSidePanelVisible(ulong userId, bool visible)`

The existing global side-panel hooks remain available for compatibility.

## Files

- `oxide/plugins/RaidlandsEvents.cs`
- `oxide/plugins/RaidlandsRoamBots.cs`
- `oxide/config/RaidlandsEvents.json`
- `Docs/events-manager/RaidlandsEvents_0.6.3_Changes_2026-07-30.md`
- `UPDATED_FILES_FOR_UPLOAD_RAIDLANDS_EVENTS_0.6.3_2026-07-30.md`

Do not upload or replace runtime data files. RaidlandsRoamBots adds the
per-administrator preference to its existing data file automatically.

## Deployment

```text
oxide.reload RaidlandsRoamBots
oxide.reload RaidlandsEvents
```

Spawn a new Hard and Nightmare raid event to verify interior guard placement.
Existing living guards will remain where they were spawned.

## Validation

- RaidlandsEvents configuration parses successfully.
- C# structural brace checks pass for both changed plugins.
- `git diff --check` passes for the patch.
- Live server compilation and in-game testing remain required.
