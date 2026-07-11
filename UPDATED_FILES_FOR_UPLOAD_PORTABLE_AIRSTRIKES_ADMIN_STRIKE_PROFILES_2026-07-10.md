# Portable Airstrikes admin strike profiles - 2026-07-10

What changed:
- PortableAirstrikes is now v0.1.51.
- `/strike admin` now treats Strikes as economy/eligibility wrappers and Strike Profiles as the authored payload/timing/spread source.
- The Strikes tab no longer edits delivery or payload fields. It edits wrapper name, RP, permission, cooldowns, warning delay, accepted target types, profile count, and wrapper deletion.
- The old Visuals assignment wording is now Strike Profiles where the admin is including/removing runtime profiles from a strike wrapper.
- Strike wrappers now support `AcceptedTargetTypes`, `StrikeProfiles`, per-profile start delay, per-profile payload count limit, and positive wrapper multipliers for spread, line/width, impact radius, pulse delay, tracking, damage, vehicle damage, and splash radius.
- Runtime bundled execution starts every enabled included profile that can run for the target. Homing payload profiles require a `vehicle_ping`; non-homing profiles can still run on other accepted target types.
- Profile payload events and max counts stay authoritative. Wrapper payload count limits can only reduce the profile count.
- Admin strike deletion removes only the strike wrapper and clears saved player defaults that pointed at it. It does not delete `oxide/data/PortableAirstrikes/VisualProfiles.json` profile definitions.
- `oxide/config/PortableAirstrikes.json` is now ConfigVersion 36 and carries the new wrapper/profile fields while keeping legacy `TargetType` and `VisualProfileId` readable.

Rust server files to upload:
- oxide/plugins/PortableAirstrikes.cs
- oxide/config/PortableAirstrikes.json

Reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD.txt
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_ADMIN_STRIKE_PROFILES_2026-07-10.md

Do not overwrite as part of this upload:
- oxide/data/PortableAirstrikes/VisualProfiles.json

Reload after upload:
```text
oxide.reload PortableAirstrikes
```

Quick live smoke:
- Confirm the reload banner reports PortableAirstrikes v0.1.51.
- Open `/strike admin`, verify the tab label says Strike Profiles, and verify Strikes no longer exposes delivery/payload editing controls.
- In Strikes, toggle accepted target types for a temporary/test wrapper and confirm at least one accepted target is always preserved.
- In Strike Profiles, include and remove a loaded profile from a test wrapper; verify profile definitions remain in `VisualProfiles.json`.
- In Balance, verify profile delay/limit rows appear for included profiles and multiplier rows only appear for relevant payload capabilities.
- Call one legacy strike with no included profile and one profile-backed strike; verify both follow normal charge/cooldown/completion paths.
- For any homing profile, verify it is skipped or rejected without a vehicle ping and runs only against a tracked vehicle target.
- Delete a temporary/test wrapper from the admin panel and confirm saved defaults pointing at it are cleared without deleting profile data.

Local verification:
- Roslyn compile check passed for oxide/plugins/PortableAirstrikes.cs v0.1.51 against RustDedicated_Data/Managed.
- Remaining warnings were the existing Unity Rigidbody.velocity deprecation warnings in visual movement/carrier velocity helpers.
- oxide/config/PortableAirstrikes.json parsed successfully and reports ConfigVersion 36.
- Targeted `git diff --check` passed for the tracked PortableAirstrikes plugin/config/doc/upload-note files; the new detailed upload note passed a separate trailing-whitespace check.
- Live Oxide reload and in-game admin panel smoke testing are still required.

Local source hashes:
- oxide/plugins/PortableAirstrikes.cs SHA256: 33F0529AD55B89D3E50CCEF38F6BC435F5B52DF7AC6BC24BD7F127BABBD002DE
- oxide/config/PortableAirstrikes.json SHA256: 6629A1AE7DE48386A1257C18A038B846EFCCB2F010F86D9BD419871A07E2BAFE
