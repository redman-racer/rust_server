WebsiteMapBridge skybox publish + Raidlands terrain viewer skybox support - 2026-07-11

Summary
- Added a generated Raidlands daytime skybox for the website 3D terrain viewer.
- The website viewer now loads `data-skybox-url` as an equirectangular Three.js environment/background, while keeping the procedural sky as fallback.
- The website map upload/status contract now accepts optional `skybox_image_base64` and exposes `mapImage.skyboxUrl`.
- `WebsiteMapBridge` can include a local equirectangular skybox file during `rl_map_publish` without trying to render from the headless Rust server.

Website files to upload
- `assets/media/skyboxes/raidlands-current-skybox.png`
- `assets/ts/shared/three-environment.ts`
- `assets/ts/server-map-viewer/app.ts`
- `assets/build/airstrike-animation-editor/server-map-viewer.js`
- `assets/build/airstrike-animation-editor/server-map-viewer.js.map`
- `assets/build/airstrike-animation-editor/chunks/three-environment-DlIms8Kr.js`
- `assets/build/airstrike-animation-editor/chunks/three-environment-DlIms8Kr.js.map`
- remove old generated `assets/build/airstrike-animation-editor/chunks/three-environment-B1duu_Ka.js`
- remove old generated `assets/build/airstrike-animation-editor/chunks/three-environment-B1duu_Ka.js.map`
- `includes/server-status.php`
- `pages/home.php`
- `pages/server.php`
- `database/migrations/055_server_map_skybox.sql`
- `server-plugins/WebsiteMapBridge.cs`

Rust server files to upload
- `oxide/plugins/WebsiteMapBridge.cs`
- optional seed skybox: `oxide/data/WebsiteMapBridge/current-skybox.png`

Deploy notes
- Run `database/migrations/055_server_map_skybox.sql` on the website database before expecting `mapImage.skyboxUrl` to persist.
- Upload/reload `oxide/plugins/WebsiteMapBridge.cs`.
- If using the generated skybox, upload `oxide/data/WebsiteMapBridge/current-skybox.png`.
- Run `oxide.reload WebsiteMapBridge`.
- Run `rl_map_status` and confirm the bridge reloads with the expected config.
- Run `rl_map_publish` to publish the current map/terrain/skybox bundle.

Verification performed locally
- `npm run build` passed in `C:\wamp64\www\raidlands`.
- `php -l includes\server-status.php`, `php -l pages\server.php`, `php -l pages\home.php`, and `php -l api\server\map-upload.php` passed.
- `node --check assets\build\airstrike-animation-editor\server-map-viewer.js` passed.
- Playwright/Chromium smoke render loaded `/assets/media/skyboxes/raidlands-current-skybox.png` with HTTP 200 and rendered the viewer without browser console errors.
- Rust-side live Oxide reload was not run locally; use `oxide.reload WebsiteMapBridge` as the runtime compile gate.
