WebsiteVipBridge heatmap aggregates - 2026-07-11

What changed:
- WebsiteVipBridge is now v1.6.1.
- Added disabled-by-default heatmap config: `HeatmapEnabled`, `HeatmapSyncIntervalSeconds`, `HeatmapBucketSize`, `HeatmapMetrics`, `HeatmapRetentionHours`, and `HeatmapIncludeOnlinePositions`.
- Death hooks now aggregate player deaths, PvP kills, NPC deaths, deaths by NPC, and coarse RoamBots activity into server-side x/z buckets.
- Connected-player position sampling is opt-in and remains bucket-count only; it is off by default.
- The bridge periodically posts aggregate buckets to `/api/server/heatmap-snapshot.php`; it does not send raw event lists.
- Added admin commands: `websitevip.heatmap.sync`, `websitevip.heatmap.status`, and `websitevip.heatmap.clearwipe`.
- Follow-up fix: if the website endpoint returns 404, the bridge now pauses heatmap posting and reports `endpoint_missing=true` in `websitevip.heatmap.status` until reload or a manual sync retry.
- Follow-up fix: web callbacks skip work during plugin unload to reduce noisy late-callback failures while Oxide is unloading the plugin.
- Website ingest now accepts the bridge's counter-style bucket payload and normalizes `player_deaths`, `pvp_kills`, `npc_deaths`, `deaths_by_npc`, `roambots_activity`, and `online_positions` into public heatmap metrics.

Rust server files to upload:
- oxide/plugins/WebsiteVipBridge.cs
- oxide/config/WebsiteVipBridge.json

Website source file to keep in sync:
- server-plugins/WebsiteVipBridge.cs

Website files that must be deployed before enabling heatmap:
- api/server/heatmap-snapshot.php
- api/server/heatmap.php
- includes/server-status.php
- database/migrations/051_server_map_heatmap.sql
- pages/server.php
- assets/build/airstrike-animation-editor/server-map-viewer.js
- assets/build/airstrike-animation-editor/server-map-viewer.js.map
- assets/css/styles.css

Reload after upload:
- oxide.reload WebsiteVipBridge

Useful live checks:
- websitevip.heatmap.status
- Set `HeatmapEnabled` true only after the website endpoint exists.
- If status shows `endpoint_missing=true`, deploy the website endpoint files and migration, then run `websitevip.heatmap.sync` or `oxide.reload WebsiteVipBridge`.
- Optional: include `online_positions` in `HeatmapMetrics` and set `HeatmapIncludeOnlinePositions` true only if coarse connected-player position sampling is acceptable.
- Trigger a few deaths, then run `websitevip.heatmap.sync` and watch for `Heatmap snapshot synced for X bucket(s).`

Verification completed:
- `oxide/config/WebsiteVipBridge.json` parses and contains the new heatmap keys.
- `git diff --check` passed for `oxide/plugins/WebsiteVipBridge.cs` and `oxide/config/WebsiteVipBridge.json`.
- Roslyn compile check passed for `oxide/plugins/WebsiteVipBridge.cs` against `RustDedicated_Data/Managed` with `Oxide.References.dll` excluded; warnings were existing DTO/plugin-reference assignment warnings.
- Follow-up Roslyn compile check passed for `oxide/plugins/WebsiteVipBridge.cs` after the endpoint-missing/unload guard; warnings were the same existing DTO/plugin-reference assignment warnings.
- `server-plugins/WebsiteVipBridge.cs` and `oxide/plugins/WebsiteVipBridge.cs` SHA256 match after the follow-up fix.
- Runtime reload and website endpoint ingest are still required on the live server.
