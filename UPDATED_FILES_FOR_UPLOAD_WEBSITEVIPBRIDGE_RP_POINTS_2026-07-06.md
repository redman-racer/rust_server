WebsiteVipBridge RP point request queue - 2026-07-06

Why this is needed:
- The website Vote Rewards and RP Games implementation adds signed RP point request endpoints.
- WebsiteVipBridge now needs to poll those requests and apply exact-once ServerRewards debits/credits.
- Existing RP purchase polling remains separate and unchanged.

Website prerequisite:
- Deploy the website Vote Rewards/RP Games changes first, including:
  - api/server/rp-point-requests.php
  - api/server/rp-point-result.php
  - includes/rewards.php
  - database/migrations/034_vote_rewards_rp_games.sql
  - server-plugins/WebsiteVipBridge.cs
  - server-plugins/WebsiteVipBridge.config.example.json

Rust server files to upload:
- oxide/plugins/WebsiteVipBridge.cs
- oxide/config/WebsiteVipBridge.json

Reload after upload:
- oxide.reload WebsiteVipBridge

Useful live checks:
- Reload should show WebsiteVipBridge v1.5.8.
- Run websitevip.rp.points.status to confirm RP point polling is enabled.
- Run websitevip.rp.points.sync after queuing a test reward/game request.
- Watch for requests hitting /api/server/rp-point-requests.php and results posting to /api/server/rp-point-result.php.
- Confirm ServerRewards balances change once per request_id, including duplicate request replay.

Local verification:
- Runtime plugin copy matches C:\wamp64\www\raidlands\server-plugins\WebsiteVipBridge.cs.
- oxide/config/WebsiteVipBridge.json parses successfully.
- Lightweight bridge source checks passed locally.
- Live Oxide reload is still required for compile/runtime proof.
