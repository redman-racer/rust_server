# Changelog

## 2026-07-18 — Server upgrade recovery and performance fixes

### Performance

- Updated `LiveAdmin` to v0.15.1 and limited plugin/backup discovery to targeted top-level directories, removing the recurring recursive filesystem scan that caused periodic stalls.
- Updated `WebsiteMapBridge` to v1.0.25.
  - The complete server-entity discovery scan now runs once during initialization; spawn and kill hooks maintain the indexes afterward.
  - World-event publication now runs every 15 seconds.
  - Unchanged entities are suppressed until they move at least 2 metres or rotate at least 3 degrees.
  - Route samples remain available across failed requests, then compact to a single anchor after the website acknowledges the revision.
  - Discrete excavator and quarry state sampling uses the maintained entity index instead of rescanning the world.
- Increased the `WebsiteVipBridge` permission snapshot interval from 180 to 600 seconds.

### Recovered current plugins

- Restored `WebsiteVipBridge` v1.8.1 from the developer copy, replacing the incomplete local v1.6.2 copy.
- Restored `RaidlandsRoamBots` v0.4.4 from the developer copy, replacing the incomplete local v0.3.81 copy.
- Retained bounded RaidlandsRoamBots logging and trace persistence behavior from v0.4.4.

### Latest host-backup synchronization

- Synchronized current plugin sources for CopyPaste, DroppedItemDespawn, MonumentsRecycler, PortableAirstrikes, PortableAirstrikesAnimationEditor, RaidlandsEvents, RaidlandsPortaforts, RaidlandsSentryTurrets, RaidlandsVehicleTokens, ServerRewards, ToolCupboardTurrets, and WebsiteAirstrikeAnimationBridge.
- Synchronized their associated production configuration updates along with current Kits, NTeleportation, QuickSmelt, RemoverTool, ServerArmour, SmartChatBot, StackSizeController, and UI bridge settings.
- Kept shared secrets and bot tokens as environment-backed placeholders; `Secrets.local.json` remains untracked.

### Deployment notes

- Upload the changed plugin and configuration files, then allow Oxide to compile/reload them.
- Confirm the console reports `LiveAdmin` v0.15.1, `WebsiteMapBridge` v1.0.25, `WebsiteVipBridge` v1.8.1, and `RaidlandsRoamBots` v0.4.4.
- Monitor server frame time and network traffic through several 15-second world-event cycles after deployment.
