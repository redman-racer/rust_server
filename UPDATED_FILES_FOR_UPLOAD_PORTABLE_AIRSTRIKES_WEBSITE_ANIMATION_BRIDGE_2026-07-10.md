# Portable Airstrikes website animation bridge - 2026-07-10

## What changed

- Added `WebsiteAirstrikeAnimationBridge` v1.0.3.
- The bridge can pull signed published bundles from `/api/server/airstrike-animation-bundle.php` and install them into `oxide/data/PortableAirstrikes/VisualProfiles.json`.
- It uses the existing Raidlands HMAC header contract: `X-Raidlands-Server`, `X-Raidlands-Timestamp`, and `X-Raidlands-Signature`.
- Config prefers `${RAIDLANDS_BRIDGE_SHARED_SECRET}` and falls back to the existing WebsiteVipBridge shared-secret path when that alias is not present yet.
- Startup performs one delayed recovery check by default; recurring sync exists but defaults off.
- `/airanimsync` opens an admin CUI with CHECK, SYNC NOW, UPLOAD LOCAL, FORCE PULL, and ROLLBACK.
- Console/RCON commands are available as `airanimsync`, `airanimsync.check`, `airanimsync.sync <revision>`, `airanimsync.force [revision]`, `airanimsync.upload`, and `airanimsync.rollback [revision]`.
- Normal pulls block when the local `VisualProfiles.json` changed after the last installed bundle, upload a conflict snapshot, and report `blocked_local_changes`.
- Forced pulls create a filesystem backup and attempt a pre-overwrite snapshot before installing.
- Local `/airanim save` events upload `local_save` snapshots through the hook added in the previous compatibility pass.
- The bridge keeps state in `oxide/data/WebsiteAirstrikeAnimationBridge/State.json` and local backups under `oxide/data/WebsiteAirstrikeAnimationBridge/backups/`.
- The website runtime-profile importer now mirrors Rust generated-release timing: `ReleaseTemplate.Time <= 0` falls back to `FirstPayloadDelaySeconds`, fixing bootstrap snapshot validation for `a10_strafe_run`.
- Bridge installs now preflight the loaded `PortableAirstrikes` visual-profile reload API before replacing `VisualProfiles.json`, so an older runtime plugin fails early with a clear upload/reload message instead of rolling back after replacement.
- Added the missing `airanimsync.status` console alias and an immediate started response for async check/sync requests.

## Rust server files to upload

- `oxide/plugins/PortableAirstrikes.cs`
- `oxide/plugins/PortableAirstrikesAnimationEditor.cs`
- `oxide/plugins/WebsiteAirstrikeAnimationBridge.cs`
- `oxide/config/WebsiteAirstrikeAnimationBridge.json`

## Website file to upload

- `C:\wamp64\www\raidlands\includes\airstrike-animations.php`

## Reference/config files updated

- `oxide/config/Secrets.example.json`
- `Docs/AirStrikes/portable_airstrikes_development_log.md`
- `UPDATED_FILES_FOR_UPLOAD.txt`
- `UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_WEBSITE_ANIMATION_BRIDGE_2026-07-10.md`

## Secret/config note

- Preferred live secret alias: `RAIDLANDS_BRIDGE_SHARED_SECRET` in `oxide/config/Secrets.local.json`.
- For first upload compatibility, leaving the new bridge config on `${RAIDLANDS_BRIDGE_SHARED_SECRET}` is okay if the existing WebsiteVipBridge shared secret is already configured; the plugin falls back to that path.
- Do not upload `Secrets.example.json` over `Secrets.local.json`.

## Reload after upload

```text
oxide.reload PortableAirstrikes
oxide.reload PortableAirstrikesAnimationEditor
oxide.reload WebsiteAirstrikeAnimationBridge
```

If `airanimsync.sync` says `PortableAirstrikes is loaded but does not expose the visual-profile reload API`, the live server is still running an older `PortableAirstrikes` build. Upload the current `oxide/plugins/PortableAirstrikes.cs`, reload it, then retry the bridge sync.

The minimum expected banners are:

```text
PortableAirstrikes v0.1.50
PortableAirstrikesAnimationEditor v0.2.3
WebsiteAirstrikeAnimationBridge v1.0.3
```

Admin permission, if needed for non-owner admins:

```text
oxide.grant group admin websiteairstrikeanimationbridge.admin
```

## Quick live smoke

- Run `airanimsync status` from console/RCON.
- Open `/airanimsync` as an admin and confirm the panel renders.
- Run `airanimsync.check`; if the website has no bundle and the server has local profiles, confirm a bootstrap snapshot is uploaded.
- After publishing a website bundle, run `airanimsync.sync <publishedRevision>` and confirm `VisualProfiles.json` installs, both consuming plugins reload, and the website records an `installed` receipt.
- Edit and save one profile through `/airanim`; confirm the website receives a `local_save` snapshot and does not auto-publish it.
- Test `airanimsync.force` and `airanimsync.rollback` only on a non-production profile set first.

## Local verification

- Roslyn compile check passed for `oxide/plugins/WebsiteAirstrikeAnimationBridge.cs` v1.0.3 against `RustDedicated_Data/Managed`.
- Remaining warnings were expected unassigned `[PluginReference]` fields for `PortableAirstrikes` and `PortableAirstrikesAnimationEditor`.
- PHP lint passed for `C:\wamp64\www\raidlands\includes\airstrike-animations.php`.
- Website importer validation passed for all 5 live `VisualProfiles.json` profiles, including `a10_strafe_run`.
- Targeted JSON parse checks passed for the new bridge config and `Secrets.example.json`.
- Targeted `git diff --check` passed for the bridge, config, docs, and upload notes.

## Still remaining from the larger website-editor plan

- Website WebRCON publish trigger still needs to call `airanimsync.sync <publishedRevision>` automatically after `Publish & Sync`.
- The full browser 3D editor/gizmo workflow remains separate from this game-side bridge slice.
