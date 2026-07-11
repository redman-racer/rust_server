Website map terrain viewer, clean texture render, and monument primitives - 2026-07-11

What changed:
- `WebsiteMapBridge` is now v1.0.6.
- Map publishes still send the RustMapApi top-down render configured by `RenderName` for the Latest wipe map.
- Map publishes now also send a clean `TextureRenderName` render, defaulting to RustMapApi's `Default`, for the 3D terrain viewer texture without monument icons.
- v1.0.5 fixes the failed `TextureRenderName=Map` publish path by trying the configured render and then RustMapApi `Default` before falling back to the icon render.
- The website stores the clean texture as `assets/media/maps/{server}/current-texture.jpg|png` beside the public map image.
- The terrain export samples `TerrainMeta.HeightMap`, `WaterMap`, `SplatMap`, and `BiomeMap` into a compact JSON grid for the website's 3D stats-page viewer.
- Terrain exports now include sanitized `TerrainMeta.Path.Monuments` metadata: display name, prefab key, kind, world position, approximate radius, and yaw.
- The website 3D viewer no longer lays a semi-transparent full-map water plane over the terrain.
- The website 3D viewer uses `mapImage.textureUrl` as its terrain texture, falling back to the normal map URL only for older publishes that do not include a clean texture yet.
- The website stores `current-terrain.json` beside `current.jpg` and exposes the terrain URL through `/api/server-status.php`.
- The server stats 3D map now has heatmap playback controls that load delayed history frames from `/api/server/heatmap.php?playback=1`.
- Heatmap metric selection now includes `All activity`, which aggregates every stored heatmap metric into the same bucket/frame query.
- The playback timeline now renders as a full scrubber with a latest-frame default, frame label, and 16-frame range even before meaningful heatmap data exists.
- Playback query fix: frame assignment now happens in PHP using UTC timestamps after fetching the bucket rows, avoiding the prepared SQL/HAVING frame-index path that returned zero rows against the live export.
- Local proof with `raiduonz_website (4).sql`: the dump contained 19 heatmap rows from `2026-07-11 19:57:29` through `2026-07-11 20:29:35`; playback now resolves `npc_fights` to frame 15 with 17 buckets, `all` to frame 15 with 18 buckets, and `deaths` to frame 15 with 1 bucket.
- The server stats 3D map now has an opt-in clan location layer. Anonymous users receive no player locations; signed-in players receive only their own online marker plus markers for online players with the same clan tag.
- `WebsiteMapBridge` now posts connected player coordinates to `/api/server/player-locations-snapshot.php` every `PlayerLocationIntervalSeconds` seconds when `PublishPlayerLocations` is true, and `rl_map_locations_sync` can force a one-shot sync from console/RCON.

Rust server files to upload:
- oxide/plugins/WebsiteMapBridge.cs
- oxide/config/WebsiteMapBridge.json

Website files to upload:
- includes/server-status.php
- api/server/map-upload.php
- api/server/heatmap.php
- api/server/player-locations.php
- api/server/player-locations-snapshot.php
- pages/server.php
- assets/css/styles.css
- assets/ts/server-map-viewer/app.ts
- assets/build/airstrike-animation-editor/server-map-viewer.js
- assets/build/airstrike-animation-editor/server-map-viewer.js.map
- assets/build/airstrike-animation-editor/chunks/three-environment-XIt9HrO7.js
- assets/build/airstrike-animation-editor/chunks/three-environment-XIt9HrO7.js.map
- assets/build/airstrike-animation-editor/chunks/three.module-CYP28Qcw.js
- assets/build/airstrike-animation-editor/chunks/three.module-CYP28Qcw.js.map
- database/migrations/050_server_map_terrain.sql
- database/migrations/051_server_map_heatmap.sql
- database/migrations/053_server_player_locations.sql
- vite.config.ts
- tsconfig.json
- DEPLOYMENT.md
- README.md
- docs/vip-store-setup.md

Website database step:
- Run `database/migrations/050_server_map_terrain.sql` after the existing migrations.
- Run `database/migrations/051_server_map_heatmap.sql` if heatmap buckets are not already installed.
- Run `database/migrations/053_server_player_locations.sql` before enabling clan location markers.
- The upload endpoint remains image-compatible before this migration, but the public stats page needs the new columns to rediscover the latest terrain URL after publish.

Reload / publish after upload:
- `oxide.reload WebsiteMapBridge`
- `rl_map_status`
- To force a publish after reload: `rl_map_publish`
- To force a player-location post after reload: `rl_map_locations_sync`

Expected success signal:
- `rl_map_status` should report `WebsiteMapBridge v1.0.6`, `render=Icons`, `textureRender=Default`, `terrainEnabled=True`, `monumentsEnabled=True`, and `heightMap=available`.
- `rl_map_status` should also report `playerLocations=True/15s` unless location publishing is disabled in config.
- `rl_map_locations_sync` should report `Website player locations synced: X connected players.`
- `rl_map_publish` should immediately log `Website map publish requested...` before rendering/uploading.
- Console should log `Website map published: https://raidlands.net/assets/media/maps/raidlands-main/current.jpg; texture: https://raidlands.net/assets/media/maps/raidlands-main/current-texture.jpg; terrain ...`.
- If the console says terrain was sent but no terrain URL came back, the website code or `database/migrations/050_server_map_terrain.sql` is not live yet.
- `/api/server-status.php` should include `mapImage.textureUrl` and `mapImage.terrainUrl` after the texture/terrain-enabled publish is ingested.
- `/api/server/player-locations.php` returns an empty `players` array when not signed in; signed-in clan members should only see self/same-clan online markers.
- The saved `assets/media/maps/raidlands-main/current-terrain.json` should include a `monuments` array after a v1.0.3 publish.
- For the attached live export, playback is expected to show a single populated latest frame rather than a long animation because all exported heatmap rows are within a 32-minute window.

Local verification:
- PHP lint passed for `includes/server-status.php`, `api/server/map-upload.php`, and `pages/server.php`.
- `npm run typecheck` passed.
- `npm run build` passed and emitted `server-map-viewer.js` plus shared Three/Orbit chunks.
- `node --check assets/build/airstrike-animation-editor/server-map-viewer.js` passed.
- `git diff --check` passed for the website checkout.
- `git diff --check -- oxide/plugins/WebsiteMapBridge.cs oxide/config/WebsiteMapBridge.json` passed in the Rust checkout.
- Rust-side Roslyn compile was not rerun for this location addendum because this checkout no longer has a local `csc.dll`/Roslyn bundle available; live Oxide reload remains the runtime compile gate.
- Live Oxide reload still needs to happen on the server.
