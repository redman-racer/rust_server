# SmartChatBot automated messages and keyword replies - 2026-07-11

What changed:
- The existing `SmartChatBot` plugin is the automated chat-message plugin for this server.
- Timed auto messages now rotate ten Raidlands website/help/feature announcements.
- Keyword auto responses are enabled for:
  - Discord / website
  - store / VIP / donate / kits
  - wipe / map / BP
  - RP / rewards / points
  - airstrike / strike
  - admin / staff / help / support
  - leaderboards / stats / KDR
  - RP casino-style games
  - voting for RP
  - clan invites / promotion / demotion / kicks
  - 3D map / terrain / heatmap / activity tools
  - feature suggestions and feature voting
  - portaforts / sentries / vehicle tokens
- Response throttles are now `45s` per triggering player and `15s` globally.
- The public message/response groups are no longer permission-gated, so default players can trigger and receive them immediately.
- `SmartChatBot.cs` now fixes the random welcome/join/leave picker so the final configured message in each list can be selected.

Rust server files to upload:
- `oxide/plugins/SmartChatBot.cs`
- `oxide/config/SmartChatBot.json`

Reload after upload:
```text
oxide.reload SmartChatBot
```

Quick live smoke:
- In global chat, say `discord`, `vip`, `wipe`, `rp`, `casino`, `vote`, `clan`, `heatmap`, `leaderboard`, `airstrike`, and `help`.
- Expected: the bot chooses one configured answer for the matching group after the configured 1-3 second response delay.
- Say another keyword immediately from the same player.
- Expected: no response until the `45s` per-player cooldown has passed.
- Have another player trigger a keyword immediately after a response.
- Expected: no response until the `15s` global cooldown has passed.
- Wait for the configured `5m` auto-message interval.
- Expected: the rotating website/help/rewards announcement posts to global chat.

Local verification:
- `oxide/config/SmartChatBot.json` parses successfully.
- The config contains 13 keyword response sets and 10 timed auto messages.
- `git diff --check` passed for `oxide/plugins/SmartChatBot.cs` and `oxide/config/SmartChatBot.json`.
- A local Roslyn compile check was not available in this checkout; live Oxide reload remains the compile/runtime proof.
