Raidlands RoamBots invisible admin targeting guard - 2026-07-07

What changed:
- RaidlandsRoamBots is now v0.3.60.
- RoamBots now treats invisible real players as non-targets.
- Native Rust/admin invis is detected through `BasePlayer.isInvisible`.
- Standard Vanish state is tracked through `OnVanishDisappear(BasePlayer)` and `OnVanishReappear(BasePlayer)`.
- When a targeted player vanishes, active bot target memory is cleared immediately, bot firing stops, aim warmup resets, last-seen/heard/damage memory for that player is cleared, and the bot returns to roam.
- Invisible players are excluded from visible target selection, shooting checks, damage memory, hearing/sound investigation, advisor real-player gates, utility bystander checks, player observation learning, and near-player spawn anchors.
- Bot-vs-bot targeting, safe-zone behavior, advisor config, kill/RP stats, and public config shape are unchanged.
- No `oxide/config/RaidlandsRoamBots.json` upload is required for this specific fix unless you also want to deploy other staged config changes already present in the working tree.

Rust server plugin files to upload:
- oxide/plugins/RaidlandsRoamBots.cs

Docs/reference files to keep with the repo handoff:
- Docs/RaidlandsRoamBots_Tactical_Rewrite_Plan_v2_LLM.md
- UPDATED_FILES_FOR_UPLOAD.txt
- UPDATED_FILES_FOR_UPLOAD_ROAMBOTS_INVISIBLE_ADMINS_2026-07-07.md

Reload after upload:
- oxide.reload RaidlandsRoamBots

Invisible admin smoke:
- Run `raidbots.enable 1` or `raidbots.enable 3`.
- Stand visible in line of sight and confirm bots can normally target/shoot you.
- Toggle the normal admin invis/Vanish feature while a bot has or recently had line of sight.
- Expected: the bot stops shooting within the next tick or Vanish hook, `raidbots.list` no longer shows you as the active target, and the side panel stops showing you as target.
- While still invisible, fire a gun, rocket, thrown explosive, or melee/tool hit near a bot.
- Expected: bots do not investigate that sound and do not set the invisible admin as target.
- Toggle invis off and step back into line of sight.
- Expected: normal visible-player targeting resumes.
- Confirm normal visible players and visible admins remain targetable.

Suggested live commands:
- oxide.reload RaidlandsRoamBots
- raidbots.diag
- raidbots.enable 1
- raidbots.list
- raidbots.decisions last 5
- raidbots.killall

Local verification:
- `oxide/config/RaidlandsRoamBots.json` parsed successfully.
- Roslyn compile check passed for `oxide/plugins/RaidlandsRoamBots.cs` v0.3.60 against `RustDedicated_Data/Managed` with `Oxide.References.dll` and `glTFast.Newtonsoft.dll` excluded.
- Remaining compile warnings are expected Oxide plugin-reference/future-field warnings.
- Live Oxide reload and the smoke steps above are still required to prove in-game runtime behavior.
