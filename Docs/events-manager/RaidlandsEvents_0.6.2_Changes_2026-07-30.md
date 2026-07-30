# RaidlandsEvents 0.6.2

Date: 2026-07-30

## Scope

This release consolidates the RaidlandsEvents work completed after the latest
server pull. It preserves the existing event, scoring, reward, marker, history,
and cleanup systems while adding the Kastro raid-base content, managed RoamBot
guards, stronger event-base initialization, and a complete administrator UI
rework.

## Raidable-base content

- Added portable Easy, Medium, Hard, and Nightmare profiles, base lists, and
  loot tables under `oxide/data/RaidlandsEvents`.
- Added the Fortify-derived `raidlands_kastro_mini_hard` and
  `raidlands_kastro_nightmare` CopyPaste buildings.
- Assigned Kastro Mini to Hard and Kastro to Nightmare.
- Registered both layouts in automatic rotation with difficulty-balanced
  weights.
- Made the CopyPaste scanner accept Fortify exports that legitimately omit the
  optional legacy `protocol` section.
- Added detailed per-layout scan diagnostics when a profile has no usable
  building.

## Placement and stability

- Disabled Rust stability simulation for authored event pastes so unsupported
  decorative Fortify pieces do not collapse during initialization.
- Preserved the adaptive generated-foundation survival audit.
- Increased the configurable runtime cached-candidate search budget from 50 to
  500 checks for large layouts.
- Added `Runtime Candidate Checks Per Spawn` to the spawn-grid configuration.

## Kastro initialization

For both Kastro layouts, RaidlandsEvents now:

- Upgrades all building blocks to HQM and restores newly upgraded blocks to
  full HQM health.
- Closes and locks all doors and engages attached locks.
- Equips every AutoTurret with an M249.
- Fills remaining turret inventory slots with incendiary 5.56 ammunition.
- Powers and activates AutoTurrets and SAM sites.
- Clears inherited owners and authorization while preserving managed guard
  authorization.
- Reapplies critical setup after CopyPaste/IO initialization.
- Applies the setup to existing active Kastro events when the plugin reloads.

## Managed RoamBot guards

- Added guard counts by difficulty: Easy 3, Medium 5, Hard 7, Nightmare 10.
- Added difficulty-specific health multipliers and leash radii.
- Spawns guards inside the event footprint and binds their lifecycle to the
  raid instance.
- Authorizes guard IDs on the event tool cupboard and pasted AutoTurrets.
- Reconciles guard groups after reload and removes them during event cleanup.
- Updated RaidlandsSentryTurrets authorization checks so authorized guards are
  not selected by its special targeting path.

## Administrator UI

- Rebuilt the manager using the LiveAdmin visual language without adding its
  refresh/polling feature.
- Added dedicated navigation for Overview, Automation, Bases & Loot, and
  Rewards.
- Added persistent automation, spawn-grid, and configuration health cards.
- Replaced cramped automation steppers with readable setting cards containing
  labels, large values, help text, and compact minus/plus controls.
- Added semantic enabled, warning, success, disabled, and danger states.
- Redesigned the per-layout loot editor and added inline save/validation
  feedback.
- Added confirmation dialogs before stopping or cleaning events.
- Disabled destructive controls when no active events exist.
- Removed every visible Refresh button while retaining event-driven redraws.
- Added a `Bot Debug Panel` toggle.

## RoamBot debug-panel integration

RaidlandsRoamBots now exposes:

- `REBOT_GetDebugSidePanelEnabled`
- `REBOT_SetDebugSidePanelEnabled`

The Events UI uses these hooks to persistently show or hide only the diagnostic
side panel. Toggling it does not change bot spawning, AI, combat, nameplates, or
other debug surfaces.

## Configuration

`oxide/config/RaidlandsEvents.json` now includes:

- Raidable Bases compatibility and difficulty selection.
- Managed guard counts, health multipliers, leash radii, and authorization.
- Kastro automatic-layout rotation.
- Batched cleanup size.
- Runtime cached-candidate search budget.
- CopyPaste stability set to `false` for authored event bases.

The runtime state file `oxide/data/RaidlandsEvents.json` is intentionally not
part of this release and must not be replaced during deployment.

## Validation performed

- All included profile, base-list, loot, config, and CopyPaste JSON files parse.
- Kastro CopyPaste entity counts: Mini 1,422; Nightmare 2,371.
- C# structural brace checks pass for RaidlandsEvents and RaidlandsRoamBots.
- `git diff --check` passes for the intended commit files.
- Live server compilation and in-game interaction testing remain required after
  deployment.

## Deployment

Upload the files listed in
`UPDATED_FILES_FOR_UPLOAD_RAIDLANDS_EVENTS_0.6.2_2026-07-30.md`, preserving
their relative paths. Reload in this order:

```text
oxide.reload CopyPaste
oxide.reload RaidlandsRoamBots
oxide.reload RaidlandsSentryTurrets
oxide.reload RaidlandsEvents
```

Then verify:

```text
rlevent validate
revents.layouts scan
revents.layouts list
```
