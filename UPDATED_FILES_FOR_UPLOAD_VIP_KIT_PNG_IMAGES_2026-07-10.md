# VIP Kit PNG Images - 2026-07-10

## What changed

- The six VIP-family kit cards now point at PNG artwork instead of WebP:
  - `vip`
  - `vip_plus`
  - `mvp`
  - `golden`
  - `ultimate`
  - `titan`
- `WebsiteVipBridge` canonical image repair/sync now resolves those permissions to PNG, so `websitevip.kits.repairimages` will not put them back to WebP.
- The website kit helper/store aliases now use PNG for the same VIP-family artwork.
- A new website migration updates published kit image URLs and queues a kit sync revision without editing old applied migrations.

## Website upload

Upload these into the Raidlands website root:

- `includes/kits.php`
- `pages/store.php`
- `server-plugins/WebsiteVipBridge.cs`
- `database/migrations/048_fix_vip_kit_png_image_urls.sql`

The PNG assets already exist under `assets/media/kits/` on the website host:

- `vip-kit.png`
- `vip-plus-kit.png`
- `mvp-kit.png`
- `golden-vip-kit.png`
- `ultimate-vip-kit.png`
- `titan-vip-kit.png`

## Rust server upload

Upload these into the Rust server root:

- `oxide/plugins/WebsiteVipBridge.cs`
- `oxide/data/Kits/kits_data.json`

## Reload and repair path

After uploading the Rust files:

```text
oxide.reload WebsiteVipBridge
oxide.reload Kits
```

If the live data may still have stale WebP or failed ImageLibrary entries:

```text
websitevip.kits.repairimages
```

That command rewrites canonical Kits/ServerRewards image URLs and reloads `Kits` plus `ServerRewards`.

## Verification

- The local website asset folder contains all six PNG files.
- `oxide/data/Kits/kits_data.json` has no remaining WebP URLs for the six VIP-family kits.
- Live Oxide reload/ImageLibrary import still needs to be run on the server.
