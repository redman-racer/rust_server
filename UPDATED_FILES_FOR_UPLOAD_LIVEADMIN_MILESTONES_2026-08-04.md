# LiveAdmin Milestones and Raid Guard Balance — 2026-08-04

## Release summary

This release begins the next production stage for LiveAdmin and adjusts RaidlandsEvents guard combat at range.

## LiveAdmin 0.17.3

### Durable administration history

- Stores audit, console, chat, player-session, and server-metric records in daily JSON files under `oxide/data/LiveAdmin/history/`.
- Uses separate streams so records can be retained and administered independently.
- Migrates the existing in-plugin audit list into the archive once without deleting the legacy list.
- Buffers writes and flushes them periodically to reduce unnecessary disk activity.
- Adds configurable retention periods and hourly removal of expired archive files.
- Supports optional removal of player IP addresses from newly persisted connection records.

### Historical browsing and exports

- Replaces the former audit-only Logs page with a dated History browser.
- Supports previous/next day navigation, stream selection, searching, and paging.
- Limits the streams and records shown according to the active Admin, Staff, or Chat Moderator interface.
- Adds `/lahistoryexport` for exporting the selected date, stream, and filter to JSON.
- Extends Live Console with Live and Archive modes.
- Archive mode combines historical console, audit-command, and chat records while preserving the existing console category filters.
- Adds archive day navigation, paging, searching, filtering, and date-specific text exports.

### Role-focused interfaces

- `/admin` opens the full administration interface.
- `/staff` opens an operations-focused interface containing only staff-relevant features.
- `/chatmod` opens a reduced chat-moderation interface.
- Server-side tab checks prevent hidden interface pages from being opened through crafted UI commands.
- Navigation now lays out only visible items, eliminating empty gaps and overlapping section headings.

### Managed permission roles

- Adds managed `liveadmin_chatmod`, `liveadmin_staff`, and `liveadmin_admin` groups.
- Synchronizes each group with its configured permission bundle at startup.
- Adds a Role Access page for assigning, replacing, or removing managed roles from offline or online players.
- Restricts administrator-role assignment and removal to owners or auth-level-2 administrators.
- Records managed-role changes in the audit archive.

### Sensitive information protection

- Adds `liveadmin.players.sensitive.view`.
- Grants sensitive-data access to the managed Staff and Admin roles, but not Chat Moderators.
- The `/chatmod` interface always renders player IP addresses as `hidden`, even when the user has another permission that would normally expose them.

## RaidlandsRoamBots 0.8.0

### RaidlandsEvents guard accuracy

- Applies the new accuracy rules only to bots managed by RaidlandsEvents; ambient RoamBots remain unchanged.
- Adds increasing aim error as engagement distance increases.
- Supports separate error multipliers for easy, medium, hard, and nightmare guards.
- Disables sustained bursts beyond the configured long-range threshold.
- Caps long-range bursts at three rounds by default.
- Prevents event guards from firing beyond the configured maximum engagement range of 125 metres.

### New configuration

The `Managed Bot API` section now contains `RaidlandsEvents Guard Accuracy`, including:

- enable/disable toggle;
- maximum engagement range;
- long-range threshold;
- maximum long-range burst size;
- sustained-burst control;
- distance-to-error curve;
- difficulty error multipliers.

## Files in this release

- `oxide/plugins/LiveAdmin.cs`
- `oxide/config/LiveAdmin.json`
- `oxide/plugins/RaidlandsRoamBots.cs`
- `oxide/config/RaidlandsRoamBots.json`

## Validation completed

- Both JSON configuration files parse successfully.
- Edited C# files have balanced structural braces.
- Git whitespace validation passes.
- LiveAdmin compiler errors reported during testing were corrected, including the misplaced audit archive call and the archive timestamp helper name.

## Deployment notes

1. Upload the four plugin/config files listed above.
2. Reload `RaidlandsRoamBots`.
3. Reload `LiveAdmin`.
4. Confirm LiveAdmin creates daily files below `oxide/data/LiveAdmin/history/`.
5. Test `/admin`, `/staff`, and `/chatmod` with representative permission groups.
6. Confirm the Chat Moderator player profile displays `IP hidden`.
7. Open Live Console, select Archive, and browse the current and previous day.
