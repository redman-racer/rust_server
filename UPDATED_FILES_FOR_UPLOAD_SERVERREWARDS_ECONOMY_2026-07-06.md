# ServerRewards Economy Update - 2026-07-06

## Upload these files

- `oxide/config/PlaytimeTracker.json`
- `oxide/data/ServerRewards/products.json`

## What changed

- Baseline playtime RP is now exactly `100 RP/hour`: `25 RP` every `900` seconds.
- ServerRewards item shop now includes missing core PvP rows such as M249, HMLMG, minigun, SKS, M4 shotgun, M16A2, MGL, C4, rocket ammo, combat attachments, SAM sites, and barricade cover.
- Core PvP prices were recalibrated around the `100 RP/hour` baseline.
- Direct ServerRewards kit rows are no longer free and high-tier direct kit purchases were reduced from unreachable old values.

## Reload after upload

```text
oxide.reload PlaytimeTracker
oxide.reload ServerRewards
```

## Quick live checks

- Open `/s` or `/shop` and confirm guns, ammo, attachments, meds, armor, and raid gear appear in the Items tab.
- Confirm baseline players earn `25 RP` every 15 minutes.
- Buy one low-cost item on a test account and confirm the balance debit matches the listed RP price.
