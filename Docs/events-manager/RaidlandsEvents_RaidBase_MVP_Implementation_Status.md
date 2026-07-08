# RaidlandsEvents Raid-Base MVP Implementation Status

Updated: 2026-07-08 01:49:36 -05:00

## Current Scope

This pass implements the first usable RaidlandsEvents slice for public random CopyPaste raid bases.

Included:
- Layout discovery for `oxide/data/copypaste/*.json`.
- Admin-enabled layout rotation.
- Server/admin start commands for enabled layouts.
- Random open-world location selection with map edge, water, safe-zone, monument, road, player-base, active-event, slope, flatness, and overlap checks.
- Tracked CopyPaste paste completion using pasted entity net IDs.
- Native public map radius markers.
- Public start/completion announcements using `Public Raid Base`.
- Active instance persistence and cleanup.
- Tool cupboard objective completion when the tracked TC is destroyed or removed.
- Auto-spawn loop with safety defaults off.
- In-game admin panel available with `/eventsmanager` and `/em`.
- In-game admin panel teleport buttons for active raid bases.
- Pasted auto/scientist turrets are forced to attack all after Events Manager tracked pastes.
- Player contribution scoring for raid-base PvP damage, player kills/deaths, event-entity damage, explosive event-entity damage, and TC destruction.
- Scoring radius and map marker now use the pasted layout center instead of the raw CopyPaste origin.
- Explosive/rocket scoring now tracks launched/thrown explosive owners and resolves attackers from `HitInfo`, weapon entities, creator entities, owner IDs, and recent explosive owner tracking.
- Scoreboards through `revents.score <instanceId|all>` and an in-game admin panel Score modal/table.
- Optional ServerRewards RP placement payouts on completion, disabled by default.
- Pending RP reward queue and retry command if ServerRewards is missing or rejects a payout.
- Player purchase triggers for public random raid-base events through `/eventbuy`, `/raidbase`, and `revents.purchase`, disabled by default.
- Purchase costs support ServerRewards RP and Rust inventory items, with global/player cooldowns and queued refund retry for failed starts.

Deferred:
- Player-owned `/raidme` base defense/bounty mode where the player's real base becomes the raid target.
- RoamBots base-raiding attackers for `/raidme` defense events.
- Full admin CUI definition editor.
- Website sync.
- Discord sync.
- RoamBots guard groups.
- Clan scoring/splitting.
- Percentage reward pools and non-RP reward types.
- Provider registry and import/export framework from the larger event-manager plan.

## Files Changed

- `oxide/plugins/RaidlandsEvents.cs`
  - New plugin, version `0.1.20`.
  - Registers permissions:
    - `raidlandsevents.admin`
    - `raidlandsevents.admin.layouts`
    - `raidlandsevents.admin.start`
    - `raidlandsevents.admin.stop`
    - `raidlandsevents.player.purchase`
  - Adds commands:
    - `revents.status`
    - `revents.layouts scan|list|enable <layoutId>|disable <layoutId>`
    - `revents.start <layoutId|random> here|random`
    - `revents.score <instanceId|all>`
    - `revents.rewards list|retry`
    - `revents.purchase <layoutId|random>`
    - `revents.purchases status|refunds|retry|balance [playerNameOrSteamId]`
    - `revents.auto on|off`
    - `revents.stop <instanceId|all>`
    - `revents.cleanup`
    - `/revents`
    - `/eventbuy`
    - `/raidbase`
    - `/raidme` reserved/future player-base defense notice
    - `/eventsmanager`
    - `/em`
    - `revents.ui` internal CUI action command
  - Adds CUI controls for scan/refresh, start random, auto toggle, stop all, cleanup, retry RP, per-layout enable/disable, per-layout start here/random, per-instance score modal, per-instance teleport, and per-instance stop.
  - The score modal displays a leaderboard table with rank, player, score, kills, deaths, PvP damage, base damage, explosive damage, TC credit, reward state, refresh, close, and retry RP controls.
  - Normalizes pasted turrets by clearing authorization, disabling peacekeeper/friendly mode, and reapplying the attack-all state after paste completion.
  - Tracks player score entries inside active raid-base radius:
    - player kills
    - player deaths
    - player damage
    - event entity damage
    - explosive event entity damage bonus
    - tool cupboard destruction credit
  - Fixes score registration reliability:
    - event center/radius now uses the actual pasted layout footprint center
    - map markers use the same layout center
    - C4/rocket/explosive damage can credit the owning player even when `HitInfo.InitiatorPlayer` is empty
  - Announces the completion winner and top leaderboard.
  - Can pay configured ServerRewards RP placement rewards when `Rewards.Enabled` and `Award ServerRewards RP` are both true.
  - Queues failed RP rewards in `PendingRewards` for `revents.rewards retry`.
  - Charges configured public raid-base purchase costs only after layout/cooldown preflight.
  - Reserves `/raidme` for future player-owned base defense/bounty events and moves the current public CopyPaste raid-base purchase command to `/eventbuy` and `/raidbase`.
  - Aggregates duplicate RP and duplicate item cost rows before preflight/charge so duplicate rows cannot partially debit and then fail mid-charge.
  - Config list fields now replace default lists during JSON load, preventing default purchase costs/rewards/layout lists from being appended to configured JSON rows.
  - Stores purchase trigger/purchaser/cost metadata on the active instance.
  - Refunds or queues purchase refunds when start/paste fails and `Refund On Start Failure` is enabled.
  - ServerRewards purchase balance checks now probe `BasePlayer`, `player.userID`, parsed SteamID ulong, and `UserIDString` call shapes.
  - ServerRewards purchase debit now reports insufficient RP immediately when the verified live ServerRewards balance is below the configured cost.
  - ServerRewards purchase debit failure messages include the purchaser display name and SteamID.
  - ServerRewards purchase debit now tries the same call shapes and stops on the first accepted debit, with per-attempt diagnostics if every call is rejected.
  - ServerRewards purchase debit falls back to the configured ServerRewards admin command from `oxide/config/ServerRewards.json`, then `xp`, then `rp` when API debit calls all return false but the verified preflight balance is sufficient, then confirms the exact before/after balance delta.
  - Adds `revents.purchases balance [playerNameOrSteamId]` to show the exact ServerRewards balance/check details RaidlandsEvents sees.
  - Adds `/eventbuy balance` and `/raidbase balance` to show the same purchaser identity/balance path used by player purchases.
  - `revents.purchases status` shows the effective aggregated cost and raw configured cost row count.
  - ServerRewards purchase refund/credit calls verify before/after balances and fall back to the configured ServerRewards admin add command if API credit calls do not restore the expected balance.

- `oxide/plugins/CopyPaste.cs`
  - Adds `API_RaidlandsTryTrackedPasteAtPosition`.
  - The tracked API starts a server-owned position paste and fires:
    - `OnRaidlandsTrackedPasteFinished(trackingId, filename, pastedEntityIds, player, startPos)`
    - `OnRaidlandsTrackedPasteFailed(trackingId, filename, result, startPos)`

- `oxide/config/RaidlandsEvents.json`
  - Adds safe defaults.
  - `AutoSpawn.Enabled` is `false`.
  - `Scoring.Enabled` is `true`.
  - `Scoring.Use Layout Center For Score Radius` is `true`.
  - `Rewards.Enabled` and `Award ServerRewards RP` are both `false` until live payout testing is intentional.
  - `Purchase.Enabled` is `false` until live purchase testing is intentional.
  - Default purchase cost is `12,000 RP` plus `50,000 scrap`.
  - Default purchase cooldowns are 180 minutes per player and 45 minutes global.
  - `LayoutRotation.EnabledLayouts` is empty.
  - Portafort layouts are ignored by default:
    - `raidlands_portafort`
    - `raidlands_portafort_test`

- `Docs/events-manager/RaidlandsEvents_RaidBase_MVP_Implementation_Status.md`
  - This implementation tracker.

## Current Layout Scan Expectations

Local JSON validation confirmed:

| Layout | Expected scan result |
|---|---|
| `justintoactive-1` | valid, 965 entities, has TC, no crate-like entity detected |
| `plasma-1` | valid, 1414 entities, has TC, crate-like entity detected |
| `raidlands_portafort` | valid data but ignored by RaidlandsEvents config |
| `raidlands_portafort_test` | valid data but ignored by RaidlandsEvents config |

Public chat and map-marker text uses `Public Raid Base`; internal layout IDs remain admin-only.

## Admin Smoke Commands

Recommended live reload order:

```text
oxide.reload CopyPaste
oxide.reload RaidlandsEvents
```

In-game admin panel:

```text
/eventsmanager
/em
```

Initial smoke:

```text
revents.status
revents.layouts scan
revents.layouts list
revents.layouts enable justintoactive-1
revents.start justintoactive-1 here
revents.status
revents.stop all
```

Random spawn smoke after at least one valid layout is enabled:

```text
revents.layouts enable plasma-1
revents.start random random
revents.status
revents.stop all
```

Auto-spawn smoke with a short temporary interval:

```text
revents.auto on
revents.status
revents.auto off
```

Do not leave auto-spawn on publicly until a live random-location test has passed on the current map.

Scoring smoke:

```text
revents.start justintoactive-1 here
revents.score all
```

Then have two non-admin test players fight inside the event radius and damage the pasted raid base. Expected:
- `revents.score all` shows player damage, player kill/death, event entity damage, explosive damage, and total score movement.
- The map marker should be centered over the pasted raid-base footprint, and that marker center is also the score-radius center.
- C4/rockets should credit the player who placed/fired them even if Rust does not populate `HitInfo.InitiatorPlayer`.
- Destroying the tracked TC completes the event and awards TC destruction score to the final attacker if the death hook reports a player.
- Completion chat announces the winner and top leaderboard.
- The Score button in `/eventsmanager` opens an in-panel scoreboard modal/table.

Optional RP payout smoke:

```text
revents.rewards list
revents.rewards retry
```

To test payout, intentionally enable both `Rewards.Enabled` and `Rewards.Award ServerRewards RP` in `oxide/config/RaidlandsEvents.json`, reload `RaidlandsEvents`, run a small scored event, and complete it. Expected:
- Qualified placement winners receive ServerRewards RP.
- If ServerRewards is missing or rejects a payout, `revents.rewards list` shows a pending reward.
- After fixing ServerRewards availability, `revents.rewards retry` pays queued rewards and removes them from the queue.

Player purchase smoke:

```text
revents.purchases status
oxide.grant user <steamId> raidlandsevents.player.purchase
```

To test purchase, intentionally set `Purchase.Enabled=true` in `oxide/config/RaidlandsEvents.json`, keep at least one valid layout enabled, reload `RaidlandsEvents`, then have a non-admin player with enough RP/scrap run:

```text
/eventbuy random
revents.purchases status
```

Expected:
- The player is charged the configured RP/item cost only if the event start is accepted.
- The event is public, map-marked, and announces that it is counterable and not reserved.
- Global/player cooldowns update after a successful purchase.
- If CopyPaste start/paste fails and `Refund On Start Failure=true`, the cost is refunded or appears in `revents.purchases refunds`.
- After fixing refund blockers such as ServerRewards availability or the player being offline for item return, `revents.purchases retry` pays queued refunds.

## Local Verification

Passed:
- `oxide/config/RaidlandsEvents.json` parses as JSON.
- Local CopyPaste layout scan script confirmed the expected entity counts, TC presence, crate-like detection, and protocol/default sections for the current four CopyPaste files.
- Roslyn compile check passed for `oxide/plugins/RaidlandsEvents.cs` against `RustDedicated_Data/Managed` with `Oxide.References.dll` and `glTFast.Newtonsoft.dll` excluded.
  - Only warning: `CopyPaste` plugin reference is populated at runtime by Oxide.
- Roslyn compile check also passed after adding the `/eventsmanager` and `/em` CUI panel.
- Roslyn compile check also passed after adding active-event `TP` buttons and admin teleport handling.
- Roslyn compile check also passed after adding pasted-turret attack-all normalization.
- Roslyn compile check passed after adding raid-base scoring, score UI/commands, optional ServerRewards RP payouts, and pending reward retry.
  - Only warnings: `CopyPaste` and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after replacing score chat output with the in-game scoreboard modal/table.
  - Only warnings: `CopyPaste` and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after scoring reliability fixes for layout-centered radius, marker center, and explosive/rocket owner resolution.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after adding disabled-by-default player purchase triggers, RP/item costs, cooldowns, and purchase refund queueing.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after fixing ServerRewards purchase debit/credit calls to use numeric SteamIDs.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after changing ServerRewards purchase debit to call `TakePoints` with the live `player.userID` value.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after adding multi-shape ServerRewards balance/debit fallback diagnostics for player purchases.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after adding verified ServerRewards `rp take` command fallback for player purchase debit.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after adding the explicit insufficient-RP guard before ServerRewards purchase debit attempts.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after updating the ServerRewards command fallback to try this server's `xp` admin command before `rp`.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after adding `revents.purchases balance` and making the ServerRewards command fallback read `Admin RP Command` from `ServerRewards.json`.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after making ServerRewards purchase refund/credit calls verify before/after balances and use the configured admin add command fallback.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after adding purchaser identity to RP failures and player-side purchase balance diagnostics.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after aggregating duplicate purchase cost rows before preflight/charge.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after changing config list fields to replace defaults during JSON load instead of appending configured rows.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.
- Roslyn compile check passed after reserving `/raidme` for future player-owned base defense/bounty events and adding `/raidbase` for current public raid-base purchases.
  - Only warnings: `CopyPaste`, `RaidlandsSentryTurrets`, and `ServerRewards` plugin references are populated at runtime by Oxide.

Boundary:
- Standalone Roslyn compile of the full existing `CopyPaste.cs` is currently blocked by unrelated pre-existing local reference/API errors elsewhere in that file.
- The new tracked API section did not appear in the CopyPaste error list, and the live log already shows this checkout's CopyPaste compiled before this patch.
- A live `oxide.reload CopyPaste` is still required before calling `RaidlandsEvents`.

## Known Gaps

- Random location validation is conservative but still needs live map testing. Rust topology, roads, safe zones, and monuments can vary by map seed and server build.
- Scoring is player-only for this MVP; no clan aggregation, allied-clan filtering, or contribution-weighted clan payout is implemented yet.
- Rewards are ServerRewards RP placement payouts only; item, command, percentage pool, and clan split rewards are still deferred.
- RP payouts are disabled by default and should stay disabled until a staged live payout test passes.
- Player purchases are implemented only for this public random raid-base MVP; website/store purchase sync, event tokens, clan purchase ownership, non-raid-base purchase packs, and player-owned `/raidme` base defense/bounty events are still deferred.
- Purchase item refunds require the purchaser to be online; otherwise they remain queued in `PendingPurchaseRefunds` for `revents.purchases retry`.
- TC destruction score uses the stronger attacker resolver, but removal/cleanup completion may still not have an attacker to credit.
- No loot accounting is performed yet.
- Completion cleanup currently waits `Cleanup.Completion Cleanup Delay Seconds` before despawning pasted entities.
- Plugin unload cleanup attempts to remove active pasted entities when `Cleanup.Cleanup On Unload` is true.
- No RoamBots guard groups are started or cleaned yet.
- No website or Discord event feed is emitted yet.

## Next-Step Checklist

- [x] Add tracked CopyPaste position paste API.
- [x] Add RaidlandsEvents plugin MVP.
- [x] Add safe default config.
- [x] Add `/eventsmanager` and `/em` admin CUI panel.
- [x] Add active-event TP buttons to the admin CUI panel.
- [x] Force pasted event-base turrets to attack all.
- [x] Add player raid-base scoring ledger.
- [x] Add score command and admin panel Score modal/table.
- [x] Fix score radius to use pasted layout center.
- [x] Add explosive/rocket owner tracking for scoring credit.
- [x] Add optional ServerRewards RP placement rewards and pending reward retry.
- [x] Add disabled-by-default player purchase trigger with RP/item costs, cooldowns, and refund queue.
- [x] Confirm current CopyPaste layout candidates by local JSON scan.
- [x] Compile-check RaidlandsEvents locally.
- [ ] Live reload `CopyPaste`.
- [ ] Live reload `RaidlandsEvents`.
- [ ] Run `revents.layouts scan` and confirm `justintoactive-1` / `plasma-1` valid in-game.
- [ ] Start `justintoactive-1` with `here`, verify tracked entities, marker, status, TC, and cleanup.
- [ ] Test player damage, player kill/death, event entity damage, explosive damage, and `revents.score`.
- [ ] Test `/eventsmanager` Score modal/table during an active scored event.
- [ ] Test optional ServerRewards RP payout on a staging event before leaving rewards enabled.
- [ ] Test `/eventbuy random` or `/raidbase random` with `Purchase.Enabled=true` and a player granted `raidlandsevents.player.purchase`.
- [ ] Test purchase start failure refund and `revents.purchases refunds|retry`.
- [ ] Start `random random`, verify random location rejection behaves correctly.
- [ ] Test TC destruction completion.
- [ ] Test plugin unload cleanup during an active event.
