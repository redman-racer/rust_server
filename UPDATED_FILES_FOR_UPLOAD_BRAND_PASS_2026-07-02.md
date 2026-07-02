# Raidlands Brand Pass Upload Manifest

Generated: 2026-07-02

Upload these files to the same relative paths under the live Rust server root.

| Local file | Game server destination |
| --- | --- |
| `C:\wamp64\www\rust_server\oxide\plugins\WebsiteVipBridge.cs` | `oxide/plugins/WebsiteVipBridge.cs` |
| `C:\wamp64\www\rust_server\oxide\config\SimpleLogo.json` | `oxide/config/SimpleLogo.json` |
| `C:\wamp64\www\rust_server\oxide\config\ServerInfo.json` | `oxide/config/ServerInfo.json` |
| `C:\wamp64\www\rust_server\oxide\config\ServerPop.json` | `oxide/config/ServerPop.json` |
| `C:\wamp64\www\rust_server\oxide\config\SmartChatBot.json` | `oxide/config/SmartChatBot.json` |
| `C:\wamp64\www\rust_server\oxide\config\Kits.json` | `oxide/config/Kits.json` |
| `C:\wamp64\www\rust_server\oxide\config\DiscordWipe.json` | `oxide/config/DiscordWipe.json` |
| `C:\wamp64\www\rust_server\oxide\config\Scoreboards.json` | `oxide/config/Scoreboards.json` |

Notes:

- This manifest file is a local handoff artifact. It does not need to be uploaded unless you want a copy on the server.
- The website images do not need to be uploaded to the game server. The plugin configs point to hosted `https://raidlands.net/...` asset URLs.
- After upload, reload `WebsiteVipBridge` and the listed config-driven plugins, or restart the server so every UI picks up the new config values.
