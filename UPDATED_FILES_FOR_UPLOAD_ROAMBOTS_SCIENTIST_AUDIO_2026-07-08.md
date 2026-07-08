Raidlands RoamBots native scientist audio mute - 2026-07-08

What changed:
- RaidlandsRoamBots is now v0.3.71.
- Legacy scientist bodies are still used as the physical RoamBot actor.
- Native `ScientistNPC` radio chatter is muted during body preparation by clearing `RadioChatterEffects`.
- Native scientist death chatter/effects are muted by clearing `DeathEffects`.
- Any already queued `PlayRadioChatter` action is cancelled and cleared during the same initial/follow-up body prep path.
- The idle chatter repeat window is stretched out as a backup guard.
- RoamBots hearing/sound investigation, bot chat, weapon fire, tactical AI, killfeed, stats, loot, and config tuning are unchanged.
- No `oxide/config/RaidlandsRoamBots.json` upload is required for this specific fix unless you also want to deploy other staged config changes already present in the working tree.

Rust server plugin file to upload:
- oxide/plugins/RaidlandsRoamBots.cs

Docs/reference files updated:
- Docs/RaidlandsRoamBots_Tactical_Rewrite_Plan_v2_LLM.md
- UPDATED_FILES_FOR_UPLOAD.txt
- UPDATED_FILES_FOR_UPLOAD_ROAMBOTS_SCIENTIST_AUDIO_2026-07-08.md

Reload after upload:
- oxide.reload RaidlandsRoamBots

Immediate live smoke:
- raidbots.diag
- raidbots.enable 1
- raidbots.list
- Stand near the spawned bot through idle/alert/combat moments.
- Damage and kill one test bot.
- Expected: no native scientist radio chatter, scientist alert voice, or scientist death chatter.
- Expected: normal weapon fire, RoamBots chat/killfeed behavior, bot targeting, and sound investigation still work.

Local verification:
- `oxide/config/RaidlandsRoamBots.json` parsed successfully.
- Roslyn compile check passed for `oxide/plugins/RaidlandsRoamBots.cs` v0.3.71 against `RustDedicated_Data/Managed` with `Oxide.References.dll` and `glTFast.Newtonsoft.dll` excluded.
- Remaining compile warnings are expected Oxide plugin-reference/future-field warnings.
- Live Oxide reload and the smoke steps above are still required to prove in-game audio behavior.
