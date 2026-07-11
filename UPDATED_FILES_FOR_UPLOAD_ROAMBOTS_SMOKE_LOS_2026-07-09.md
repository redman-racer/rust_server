Raidlands RoamBots smoke LOS blocking - 2026-07-09

What changed:
- RaidlandsRoamBots is now v0.3.73.
- Active smoke sources now block RoamBot vision, including player-thrown smoke, bot-thrown smoke, 40mm smoke, smoke rockets, smoke-cover effects, and smoke-signal style cover volumes.
- Target visibility probes count smoke-blocked points as no LOS, so bots may remember/search/reposition/hear through smoke but should not keep shooting through it.
- Diagnostics now surface smoke blockers as `Sight: smoke ...`; the fire gate remains `Fire: no_los` when smoke blocks the current target.
- `/raidbots admin` now includes `Smoke LOS` and `Smoke rad` controls in the AI tab.
- Added config defaults: `AI -> Smoke Blocks Vision = true`, `Smoke Vision Radius = 7.5`, `Smoke Vision Height = 5.0`, and `Smoke Vision Lifetime Seconds = 30.0`.

Rust server files to upload:
- oxide/plugins/RaidlandsRoamBots.cs
- oxide/config/RaidlandsRoamBots.json

Docs/reference files updated:
- Docs/RaidlandsRoamBots_Tactical_Rewrite_Plan_v2_LLM.md
- UPDATED_FILES_FOR_UPLOAD.txt
- UPDATED_FILES_FOR_UPLOAD_ROAMBOTS_SMOKE_LOS_2026-07-09.md

Reload after upload:
- oxide.reload RaidlandsRoamBots

Immediate live smoke:
- raidbots.diag
- raidbots.enable 1
- raidbots.list
- Get clean LOS to a test bot and confirm normal shooting still works.
- Throw a smoke grenade between the player and bot while it has or recently had target memory.
- Expected: the bot stops firing within the next perception/fire check, `Sight: smoke ...` appears, and `Fire: no_los` appears if the bot still remembers the target.
- Expected: the bot may search, flank, retreat, or reposition, but should not keep shooting through the smoke volume.
- Step out of the smoke into a clean lane.
- Expected: the bot reacquires after reaction delay and can shoot normally.
- Hurt a bot until it throws smoke around 10m-55m.
- Expected: bot-thrown smoke also blocks RoamBot shooting through that screen.

Local verification:
- `oxide/config/RaidlandsRoamBots.json` parsed successfully.
- Roslyn compile check passed for `oxide/plugins/RaidlandsRoamBots.cs` v0.3.73 against `RustDedicated_Data/Managed` with `Oxide.References.dll` and `glTFast.Newtonsoft.dll` excluded.
- Remaining compile warnings are expected Oxide plugin-reference/future-field warnings.
- `git diff --check` passed for the RoamBots smoke LOS files.
- Live Oxide reload and the smoke steps above are still required to prove in-game smoke behavior.

Local source hashes:
- oxide/plugins/RaidlandsRoamBots.cs SHA256: 1A5869ECE76C151B523648C01DE02CA936B28124483552F34E647ACAB13A03C6
- oxide/config/RaidlandsRoamBots.json SHA256: 5195F6FF4D6E3D60E90B05B4A0DE5ACF780BEFB266FBE33C631ED1851D9FFDB8
