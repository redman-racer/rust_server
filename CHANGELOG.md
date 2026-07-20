# Changelog

## 2026-07-19 — Raidlands Leaderboards v2.0.0

- Removed gathering collection, storage fields, hooks, tables, profile tiles, tabs, ranking metrics, and API support.
- Rebuilt the leaderboard for 1000x PvP with sortable kills, PvP deaths, K/D, headshot rate, best streak, playtime, and raid-damage rankings.
- Added dedicated raid-damage tracking, a Raiding tab, PvP-versus-total death separation, and a v2 data migration.
- Added competitive-integrity controls for same-clan/team kills, sleepers, repeat-victim cooldowns, minimum rate-ranking eligibility, inactive accounts, and staff exclusion.
- Added automatic missed-wipe detection, hardened archived-wipe data, historical names/clans, empty states, viewer-rank display, and viewer-row highlighting.
- Debounced disconnect saves to avoid repeatedly serializing the full leaderboard and wipe archive during high player churn.
- Rebuilt Global as a full-width, larger player-stat table and moved clan rankings exclusively to a matching full-width Clans table.
- Added aligned columns for kills, deaths, K/D, playtime, gathering, clan, and member counts with pagination on both tables.
- Added an idempotent lifetime-stat import from the same `KDRScoreboard` and `PlaytimeTracker` local files used by `WebsiteVipBridge`.
- Added automatic previous-wipe archives with UI browsing and historical clan membership snapshots.
- Removed synthetic points/power ranking from the UI; players and clans are now ranked by real statistics such as kills, K/D, playtime, and gathering.
- Added a paginated Global view containing every tracked player and every clan with absolute server-wide ranks.
- Changed clan aggregation to consume the complete `RaidlandsClanSnapshot`, ensuring clans and their full membership are tracked even before every member reconnects.
- Updated `RaidlandsLeaderboards` to v1.0.2 after live compilation testing: crafting ownership now uses `ItemCrafter.owner`, and console UI arguments explicitly convert the current `Facepunch.StringView[]` values to strings.
- Added the custom `RaidlandsLeaderboards` plugin with persistent lifetime and per-wipe player statistics.
- Added rankings for overall score, PvP kills, gathering, playtime, and dynamically aggregated clans.
- Added tracking for K/D, headshots, kill streaks, longest kill, PvE kills, damage, resource categories, crafting, building, destruction, explosives, shots, rockets, healing, connections, travel distance, and playtime.
- Added a branded teal, purple, orange, and dark in-game interface with player profiles and wipe/lifetime switching.
- Added APIs for player statistics and player/clan leaderboard publication to the Raidlands website and other plugins.

## 2026-07-18 — LiveAdmin moderation centers

- Updated `LiveAdmin` to v0.16.0.
- Added a Ban Management Center with native-ban import, offline Steam-ID bans, permanent and timed durations, automatic expiry, unban confirmations, searching, paging, granular permissions, and audit history.
- Added a Moderation Case Center with case ownership, status workflow, notes, report escalation, and linked evidence summaries from reports, player notes, flagged chat, warnings, bans, and punishment logs.
- Added additive stored-data migration for ban and case records and extended the built-in role presets with appropriate view/manage permissions.

## 2026-07-18 — July Rust RPC compatibility

- Updated `LiveAdmin` to v0.15.2.
  - Replaced the removed `BasePlayer.ClientRPCPlayer` reflection path with the current `ClientRPC(RpcTarget.Player(...))` API when opening the inventory loot panel.
  - Retained the existing console-command fallback if the RPC cannot be sent.
- Audited `CustomItemDefinitions` v2.5.2 and left its vendor code unchanged: the July removal affects `ClientRPCPlayer` and `ClientRPCEx`, while its Harmony patch targets the separate internal RPC writer path. Check the live load for Harmony patch errors after deployment.

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
