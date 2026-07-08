# VIP Kit Screenshot Sync - 2026-07-07

## Website upload

Upload these into the Raidlands website root:

- `includes/kits.php`
- `database/migrations/043_sync_vip_kit_items_from_screenshots.sql`
- `database/migrations/044_restore_vip_kit_artwork_urls.sql`
- `database/migrations/045_correct_vip_kit_items_from_verified_icons.sql`

Do not upload the screenshot PNGs as kit artwork. They were reference images only.
Do not use the website `server-plugins` directory for plugin deployment; the Rust server plugin source of truth is `oxide/plugins/WebsiteVipBridge.cs` under the Rust server root.

Migration order:

- Use `043_sync_vip_kit_items_from_screenshots.sql` as the corrected baseline if that migration has not been applied yet.
- If an earlier/bad `043` was already applied, apply `044_restore_vip_kit_artwork_urls.sql`, then `045_correct_vip_kit_items_from_verified_icons.sql`.
- `044_restore_vip_kit_artwork_urls.sql` restores the kit artwork URLs to the existing WEBP assets.
- `045_correct_vip_kit_items_from_verified_icons.sql` rebuilds the item rows from the verified icon comparison.

The item correction updates only the active, non-deleted kit rows for:

- `kits.vip`
- `kits.vipplus`
- `kits.mvp`
- `kits.goldenvip`
- `kits.ultimatevip`
- `kits.titanvip`

The item correction deletes and recreates only `game_kit_items` for those six kit permissions, publishes a new kit revision, and queues a kit sync row. It does not overwrite deleted alias rows or unrelated kit, group, store, or entitlement records.

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
