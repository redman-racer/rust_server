# LiveAdmin production UI and operations update

Date: 2026-07-17  
LiveAdmin version: 0.15.1

## Files changed

- `oxide/plugins/LiveAdmin.cs`
- `oxide/config/LiveAdmin.json`
- `oxide/config/Guardian.json`
- `oxide/config/ServerArmour.json`

## LiveAdmin changes

### Interface

- Refreshed the complete interface with the existing dark, orange, and purple visual identity.
- Replaced letter-only navigation markers with matching Rust icon assets.
- Improved panel spacing, status cards, navigation, filters, paging, and confirmation flows.

### Dashboard and live console

- Expanded the dashboard with server health, population, performance, plugin, moderation, wipe, and recent-activity information.
- Added console categories, search, wrapping, pause controls, a movable mini console, and recent performance history.
- Set the live-console refresh interval to 30 seconds.
- Added plugin log capture alongside normal server console output.

### Players

- Added staff-focused player profiles, moderation history, warnings, notes, groups, inventory access, entity counts, and world-position information.
- Added owned/authorized tool-cupboard listings and guarded teleport actions.
- Added safer kick, ban, mute, teleport, inventory, and configured player-action controls.

### Chat

- Added searchable live chat monitoring, message details, staff replies, warnings, timeouts, hide/restore controls, and configurable quick replies.
- Added editable word-blacklist management and moderation audit records.

### Staff tools and reports

- Expanded staff actions, inventory inspection, notes, group access, ticket context, duty/activity information, and permission-aware controls.
- Added report scopes, priorities, claim/investigate/resolve/reopen states, staff notes, deletion confirmation, and linked player context.

### Manage

- Added loaded/unloaded plugin status and guarded load, unload, and reload controls.
- Added recursive editing for nested plugin configuration values.
- Added config path/value search for large configuration files.
- Added editing for booleans, strings, integers, decimals, null values, list entries, full lists, and empty objects.
- Lists accept JSON arrays or values separated with `||`.
- Added timestamped automatic config backups and confirmed restoration of the latest valid backup.
- Added group creation with name, title, and rank plus confirmed group removal.
- Protected groups cannot be removed.
- Expanded permission browsing, filtering, group selection, grant/revoke controls, and role presets.

### Wipe automation

- Reworked the wipe panel around editable wipe sequences and future cadence changes.
- Supports weekly, twice-weekly, and interval scheduling fields.
- Keeps map size at 3500 and generates a random seed when configured as `random`.
- Supports first-Thursday monthly force wipes and normal scheduled map wipes.
- Separates map deletion, blueprint deletion, Discord setup, normal commands, and force-wipe commands.
- Includes previews, dry-run support, offline cleanup readiness, pending-wipe verification, and force-wipe safeguards.

### Production operations and safety

- Added maintenance mode, maintenance bypass, scheduled restart controls, warnings, command safety, rate limiting, confirmation prompts, and expanded audit logging.
- Added configuration and role-preset entries for maintenance and restart permissions.
- Updated the LiveAdmin config schema to version 3.

## Temporary VPN/proxy policy

- Disabled Guardian VPN checking.
- Disabled ServerArmour VPN auto-kicking.
- Other ServerArmour account and ban protections remain enabled.
- These settings can be re-enabled later as server population grows.

## Deployment

Upload all four changed runtime files, then run:

```text
oxide.reload Guardian
oxide.reload ServerArmour
oxide.reload LiveAdmin
```

Review the LiveAdmin panel after reload and verify the server console reports version `0.15.1` without compilation errors.

