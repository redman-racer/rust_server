# Portable Airstrikes admin panel polish - 2026-07-10

What changed:
- PortableAirstrikes is now v0.1.50.
- The `/strike admin` Give tab now has a paginated, sortable, and filterable online/sleeping player table.
- Give search now uses the same single-line keyboard input setup as the animation editor, which avoids the live search-bar submit issue when typing player names.
- Admin numeric fields now open an exact-value keypad with digits, dot, minus, DEL, CLR, CUR, APPLY, and CANCEL instead of relying on direct CUI number input.
- The Visuals tab now keeps runtime visual toggles and a scrollable compatible-profile selector only; editor-owned visual timing/height/distance controls were removed from this panel.
- Visual profile assignment now highlights the selected profile, includes Auto as a selectable row, and changes assignments only through explicit Select buttons.

Rust server files to upload:
- oxide/plugins/PortableAirstrikes.cs

Reference files updated:
- Docs/AirStrikes/portable_airstrikes_development_log.md
- UPDATED_FILES_FOR_UPLOAD.txt
- UPDATED_FILES_FOR_UPLOAD_PORTABLE_AIRSTRIKES_ADMIN_PANEL_POLISH_2026-07-10.md

Reload after upload:
```text
oxide.reload PortableAirstrikes
```

Quick live smoke:
- Confirm the reload banner reports PortableAirstrikes v0.1.50.
- Open `/strike admin`, type a partial display name with spaces in Give search, and confirm the table updates without an error.
- Change Give filter, sort, and pages; grant a listed player `Airstrike Targeting Binoculars`.
- Open the keypad for Give amount plus at least one Strike, Balance, Safety, and Loot numeric field; test digits, dot, minus, DEL, CLR, CUR, APPLY, and CANCEL.
- On Visuals, select a strike, choose Auto, choose a compatible profile from the scroll list, and confirm the selected row highlighting and config persistence after `oxide.reload PortableAirstrikes`.

Local verification:
- Roslyn compile check passed for oxide/plugins/PortableAirstrikes.cs v0.1.50 against RustDedicated_Data/Managed.
- Remaining warnings were the existing Unity Rigidbody.velocity deprecation warnings in visual movement/carrier velocity helpers.
- oxide/config/PortableAirstrikes.json parsed successfully and still reports ConfigVersion 35.
- Targeted `git diff --check` passed for oxide/plugins/PortableAirstrikes.cs; Git only warned that LF will be normalized to CRLF when it next touches the plugin file.

Local source hashes:
- oxide/plugins/PortableAirstrikes.cs SHA256: 408EBD5C0ED240BD0D3EC1C2922526947BBFE46645221843DA1856AE9BDF5850
- oxide/config/PortableAirstrikes.json SHA256: 8863A510F89FD2CFAD5A6F85E87799CE6A0A83D1E6C7C77B46CEFBC4AB2B5768
