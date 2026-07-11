Raidlands RoamBots bush/foliage LOS tightening - 2026-07-09

What changed:
- RaidlandsRoamBots is now v0.3.72.
- Dense foliage is stricter for shooting fairness without changing damage, hearing, population, advisor behavior, or weapon ranges.
- `AI -> Minimum Exposed Target Fraction To Shoot` is now `0.34`.
- `AI -> Foliage Vision Check Radius` is now `0.9`.
- `AI -> Maximum Clear Vision Through Foliage` is now `14.0`.
- `AI -> Foliage Hits To Block Vision` is now `1`.
- `AI -> Foliage Blocks Vision`, `AI -> Foliage Terrain Sampling`, and `AI -> Require Line Of Sight To Shoot` stay enabled.
- Config validation now clamps invalid foliage/exposure values without resetting stricter operator tuning on reload.

Rust server files to upload:
- oxide/plugins/RaidlandsRoamBots.cs
- oxide/config/RaidlandsRoamBots.json

Docs/reference files updated:
- Docs/RaidlandsRoamBots_Tactical_Rewrite_Plan_v2_LLM.md
- UPDATED_FILES_FOR_UPLOAD_ROAMBOTS_FOLIAGE_LOS_2026-07-09.md

Reload after upload:
- oxide.reload RaidlandsRoamBots

Immediate live smoke:
- raidbots.diag
- raidbots.enable 1
- raidbots.list
- Put multiple bushes/trees between the player and bot at 40m-120m.
- Expected: dense foliage shows `Sight: foliage ...` or exposure below `0.34`, and `Fire: no_los` if the bot has a remembered target.
- Expected: the bot may investigate, search, or reposition, but should not keep firing through dense bushes without clean LOS.
- Step into a clean lane or light brush at similar distance.
- Expected: the bot reacquires after reaction delay and can shoot normally.

Local verification:
- `oxide/config/RaidlandsRoamBots.json` parsed successfully.
- Roslyn compile check passed for `oxide/plugins/RaidlandsRoamBots.cs` v0.3.72 against `RustDedicated_Data/Managed` with `Oxide.References.dll` and `glTFast.Newtonsoft.dll` excluded.
- Remaining compile warnings are expected Oxide plugin-reference/future-field warnings.
- Live Oxide reload and the smoke steps above are still required to prove in-game foliage behavior.
