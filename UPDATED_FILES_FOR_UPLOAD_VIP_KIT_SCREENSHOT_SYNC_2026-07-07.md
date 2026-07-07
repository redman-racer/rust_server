# VIP Kit Screenshot Sync - 2026-07-07

## Website upload

Upload these into the Raidlands website root:

- `includes/kits.php`
- `database/migrations/043_sync_vip_kit_items_from_screenshots.sql`
- `assets/media/kits/vip-kit.png`
- `assets/media/kits/vip-plus-kit.png`
- `assets/media/kits/mvp-kit.png`
- `assets/media/kits/golden-vip-kit.png`
- `assets/media/kits/ultimate-vip-kit.png`
- `assets/media/kits/titan-vip-kit.png`

Apply the SQL migration after upload. It updates only the active, non-deleted kit rows for:

- `kits.vip`
- `kits.vipplus`
- `kits.mvp`
- `kits.goldenvip`
- `kits.ultimatevip`
- `kits.titanvip`

The migration deletes and recreates only `game_kit_items` for those six kit permissions, updates their PNG image URLs, publishes a new kit revision, and queues a kit sync row. It does not overwrite deleted alias rows or unrelated kit, group, store, or entitlement records.

## Rust server upload

Upload these into the Rust server root:

- `oxide/plugins/WebsiteVipBridge.cs`
- `oxide/data/Kits/kits_data.json`

## Reload path

If the website migration is applied live and the bridge should pull from the website:

```text
oxide.reload WebsiteVipBridge
```

If `oxide/data/Kits/kits_data.json` is uploaded directly to the server:

```text
oxide.reload Kits
```

`oxide/data/ServerRewards/products.json` was checked and did not need a data upload for this kit-item change; its native kit products still point at `mvp`, `vip`, `vip_plus`, `golden`, `ultimate`, and `titan`.
