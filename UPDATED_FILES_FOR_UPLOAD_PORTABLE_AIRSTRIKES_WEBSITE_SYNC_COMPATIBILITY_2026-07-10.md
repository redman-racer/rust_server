# Portable Airstrikes website-sync compatibility - 2026-07-10

## What changed

- `PortableAirstrikesAnimationEditor` is now v0.2.3.
- Loading and saving `VisualProfiles.json` preserves schema 2, compiler metadata, bundle revision metadata, compiled tracks, and compiled release events.
- Editing and explicitly saving one profile invalidates only that profile's compiled fields so Rust cannot continue playing stale website-compiled motion.
- A local edit clears published revision/hash metadata and emits `OnPortableAirstrikesVisualProfilesSaved` with the data-file name, exact serialized JSON, and changed profile IDs.
- Startup normalization and bridge-driven reloads do not emit false local-save notifications.

## Rust server file to upload

- `oxide/plugins/PortableAirstrikesAnimationEditor.cs`

Do not upload or replace `oxide/data/PortableAirstrikes/VisualProfiles.json` for this code-only compatibility update.

## Reload after upload

```text
oxide.reload PortableAirstrikesAnimationEditor
```

## Quick live smoke

- Confirm the reload banner reports v0.2.3.
- Open and save one profile through `/airanim`.
- Confirm the file keeps `SchemaVersion: 2` and preserves compiled data for profiles that were not edited.
- Confirm the edited profile no longer has `CompiledTrack` or `CompiledReleaseEvents` until the website compiles and publishes it again.

## Local verification

- Roslyn compile passed against `RustDedicated_Data/Managed`.
- The only compiler warnings were existing Unity `Rigidbody.velocity` deprecation warnings.
- `git diff --check` passed.
