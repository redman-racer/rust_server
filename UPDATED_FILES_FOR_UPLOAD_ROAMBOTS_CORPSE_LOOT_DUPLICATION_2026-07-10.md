Raidlands RoamBots corpse loot duplication fix - 2026-07-10

What changed:
- RaidlandsRoamBots is now v0.3.74.
- Fixed the loot duplication caused by rebuilding a RoamBot corpse from its saved death snapshot during repeated `CanLootEntity` access checks.
- `OnCorpsePopulate` and the access fallback now share a per-corpse populate-once guard.
- The guard is removed when the corpse entity is destroyed.

Rust server files to upload:
- oxide/plugins/RaidlandsRoamBots.cs

Docs/reference files updated:
- Docs/RaidlandsRoamBots_Tactical_Rewrite_Plan_v2_LLM.md
- UPDATED_FILES_FOR_UPLOAD.txt
- UPDATED_FILES_FOR_UPLOAD_ROAMBOTS_CORPSE_LOOT_DUPLICATION_2026-07-10.md

Reload after upload:
- oxide.reload RaidlandsRoamBots

Immediate live smoke:
- Kill a RoamBot and note every item in its corpse.
- Remove one item, close the loot panel, and reopen the same corpse.
- Expected: the removed item remains gone and no replacement copy appears.
- Move several more items while keeping the panel open.
- Expected: removed items do not reappear during item moves or access rechecks.
- Let a second player open the same corpse.
- Expected: the second player sees only the items that remain.
- Kill a second RoamBot.
- Expected: the new corpse receives its own normal loot exactly once.

Local verification:
- Roslyn compile check passed for `oxide/plugins/RaidlandsRoamBots.cs` v0.3.74 against `RustDedicated_Data/Managed`, excluding `Oxide.References.dll` and the duplicate Newtonsoft provider.
- Remaining compile warnings are expected Oxide plugin-reference/future-field warnings.
- Live Oxide reload and the corpse-looting steps above are still required to prove runtime behavior.

Local source hash:
- oxide/plugins/RaidlandsRoamBots.cs SHA256: 4A96D9942CA2B1505A075DB83A412F6A1070105BC0336B5109FEB93BC723C270
