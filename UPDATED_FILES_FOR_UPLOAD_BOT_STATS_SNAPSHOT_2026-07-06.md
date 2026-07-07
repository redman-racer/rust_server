WebsiteVipBridge dynamic bot stats snapshot fix - 2026-07-06

Why this is needed:
- The website endpoint previously rejected signed stats snapshots when the payload contained more than 1000 bot rows.
- RaidlandsRoamBots can track many bot keys over a wipe as names, generated profiles, kits, clans, and squad roles change.

Website files to deploy:
- includes/config.php
- includes/stats.php
- .env.example
- server-plugins/WebsiteVipBridge.cs
- server-plugins/WebsiteVipBridge.config.example.json

Rust server files to upload:
- oxide/plugins/WebsiteVipBridge.cs

Recommended live order:
- Deploy the website files first.
- Upload oxide/plugins/WebsiteVipBridge.cs.
- Run `oxide.reload WebsiteVipBridge`.

Expected verification:
- Reload should show WebsiteVipBridge v1.5.8.
- The next stats sync should no longer fail with `Stats snapshot has too many bots`.
- The live config will save `StatsBotSnapshotLimit` automatically; `0` means unlimited bot rows.
