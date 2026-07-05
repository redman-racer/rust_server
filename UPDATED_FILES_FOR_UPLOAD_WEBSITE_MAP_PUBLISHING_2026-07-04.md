Raidlands website-hosted map publishing rollout - 2026-07-04

Website files to upload:
- api/server/map-upload.php
- assets/css/styles.css
- assets/media/maps/.gitkeep
- database/migrations/024_server_map_images.sql
- includes/server-status.php
- pages/server.php
- .gitignore

Website database migration:
- Apply database/migrations/024_server_map_images.sql after the existing migrations.
- Ensure the live web user can write to assets/media/maps/ so the signed upload endpoint can publish current.jpg.

Rust server files to upload:
- oxide/plugins/WebsiteMapBridge.cs
- oxide/config/WebsiteMapBridge.json

Recommended live RCON order:
- Upload the website files and run the new migration first.
- Upload the Rust files.
- oxide.reload RustMapApi
- oxide.load WebsiteMapBridge
- If WebsiteMapBridge was already attempted and failed to compile, upload the fixed oxide/plugins/WebsiteMapBridge.cs and run oxide.reload WebsiteMapBridge
- rl_map_publish

Expected verification:
- WebsiteMapBridge logs the resolved shared-secret source/fingerprint without printing the secret.
- rl_map_publish returns a raidlands.net map URL.
- https://raidlands.net/api/server-status.php includes mapImageUrl and mapImage.
- The Server Status page shows the latest wipe map.
- Future RustMapApi ready events auto-publish the map after the configured delay.
