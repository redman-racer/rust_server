WebsiteMapBridge force publish command visibility fix - 2026-07-06

Why this is needed:
- `rl_map_publish Icons 0.5` can silently return on some remote console/RCON surfaces because the command only checked `arg.IsAdmin`.
- The manual command already force-publishes once it reaches the publish path, but the previous guard could block it before any logging.

Rust server files to upload:
- oxide/plugins/WebsiteMapBridge.cs

Recommended live RCON order:
- oxide.reload WebsiteMapBridge
- rl_map_publish Icons 0.5

Expected verification:
- The reload should show `WebsiteMapBridge` v1.0.1.
- The manual publish should log `Publishing Icons map to website (...) after manual command.`
- The command should finish with `Website map published: https://raidlands.net/assets/media/maps/raidlands-main/current.jpg`
