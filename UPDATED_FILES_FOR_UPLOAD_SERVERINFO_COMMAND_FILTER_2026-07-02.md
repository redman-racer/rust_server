Rust server files to update
Generated: 2026-07-02

ServerInfo manual command catalog hotfix:

Rust server files:

oxide/plugins/ServerInfo.cs
oxide/config/ServerInfo.json

Why:

Changes the /commands tab from a static command list into a manually curated
catalog. ServerInfo.json is now the source of truth for command rows. ServerInfo
filters that catalog by Enabled, RequireStaff, RequireAdmin, MinimumAuthLevel,
RequiredGroups, AnyGroup, and NTeleportation disabled-command keys. Empty command
categories are hidden, so normal players should not see the Staff tab or
staff/admin-only rows.

Permission and plugin-loaded checks are off by default for the manual catalog.
Turn on CommandCatalogUsesPermissionChecks, CommandCatalogUsesPluginLoadedChecks,
or per-row CheckPermissions/CheckPluginLoaded only for reviewed exceptions.

Hotfix note:

ServerInfo.cs is now version 0.6.2. It keeps the compile-stopping authLevel
fix that prevented /info, /help, and /commands from registering after reload,
and switches the command menu to manual curation with explicit visibility gates.
ServerInfo.json also keeps the corrected SpawnHeli scrap transport permissions
using the live `spawnheli.scraptransport.*` names shown by `oxide.show perms`.

After upload:

1. Upload `oxide/plugins/ServerInfo.cs` and `oxide/config/ServerInfo.json` to
   the same relative paths on the live Rust server.
2. Reload ServerInfo from the server console:

oxide.reload ServerInfo

3. QA `/commands` as a normal player and confirm staff/admin rows are gone.
4. QA `/commands` as an admin/staff account and confirm staff entries appear.
5. Curate future command rows manually in `settings.CommandCatalog`.
