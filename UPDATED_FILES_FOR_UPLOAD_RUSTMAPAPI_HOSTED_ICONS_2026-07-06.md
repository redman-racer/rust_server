RustMapApi hosted monument icon rollout - 2026-07-06

Why this is needed:
- RustMapApi was downloading monument overlay icons from Imgur on startup.
- Imgur is returning HTTP 429 Too Many Requests, so the icon cache stays empty and the console logs repeated `Failed to download icon` messages.
- Some newer monument entries were enabled with empty `ImageUrl` values, so RustMapApi also logged repeated `contains an invalid image` messages after the downloads were fixed.
- WebsiteMapBridge map image publishing is separate and can still work while RustMapApi icon downloads are noisy.

Website files to upload first:
- assets/media/map-icons/rustmapapi/abandoned-cabins.png
- assets/media/map-icons/rustmapapi/abandoned-supermarket.png
- assets/media/map-icons/rustmapapi/airfield.png
- assets/media/map-icons/rustmapapi/bandit-camp.png
- assets/media/map-icons/rustmapapi/cave.png
- assets/media/map-icons/rustmapapi/fishing-village.png
- assets/media/map-icons/rustmapapi/giant-excavator-pit.png
- assets/media/map-icons/rustmapapi/harbor.png
- assets/media/map-icons/rustmapapi/junkyard.png
- assets/media/map-icons/rustmapapi/launch-site.png
- assets/media/map-icons/rustmapapi/lighthouse.png
- assets/media/map-icons/rustmapapi/military-tunnel.png
- assets/media/map-icons/rustmapapi/mining-outpost.png
- assets/media/map-icons/rustmapapi/oil-rig.png
- assets/media/map-icons/rustmapapi/outpost.png
- assets/media/map-icons/rustmapapi/oxums-gas-station.png
- assets/media/map-icons/rustmapapi/power-plant.png
- assets/media/map-icons/rustmapapi/power-sub-station.png
- assets/media/map-icons/rustmapapi/quarry.png
- assets/media/map-icons/rustmapapi/ranch.png
- assets/media/map-icons/rustmapapi/satellite-dish.png
- assets/media/map-icons/rustmapapi/sewer-branch.png
- assets/media/map-icons/rustmapapi/the-dome.png
- assets/media/map-icons/rustmapapi/train-yard.png
- assets/media/map-icons/rustmapapi/water-treatment-plant.png
- assets/media/map-icons/rustmapapi/water-well.png
- assets/media/map-icons/rustmapapi/wild-swamp.png

Rust server files to upload after the website files are live:
- oxide/plugins/RustMapApi.cs
- oxide/config/RustMapApi.json

Recommended live RCON order:
- oxide.reload RustMapApi
- rl_map_publish

Expected verification:
- RustMapApi should no longer log repeated `Failed to download icon: HTTP/1.1 429 Too Many Requests`.
- RustMapApi should no longer log repeated `<monument> contains an invalid image` for entries without configured icon URLs.
- The configured icon URLs should point at `https://raidlands.net/assets/media/map-icons/rustmapapi/`.
- `rl_map_publish` should publish the Icons render to the website as before.
