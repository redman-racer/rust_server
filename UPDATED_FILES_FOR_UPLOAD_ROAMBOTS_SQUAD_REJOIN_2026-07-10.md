Raidlands RoamBots ambient squad respawn rejoin - 2026-07-10

What changed:
- RaidlandsRoamBots is now v0.3.76.
- Ambient RoamBots that die now try to rejoin their internal RoamBot squad after the normal respawn delay.
- The replacement keeps the dead bot's internal TeamId and spawns directly beside a living teammate from that team.
- Teammates are skipped when they are below the configured health fraction or were damaged within the configured recent-damage window.
- Rejoined bots hold perception and tactical decisions during the wake/equip delay, with `wake=squad_rejoin:Ns` visible in `raidbots.list`.
- Managed/event RoamBots are unchanged and remain owner-driven through the `REBOT_*` lifecycle.
- If no healthy teammate or direct navmesh spot is available, normal population maintenance runs and follows the current random-land spawn mode.
- The checked-in JSON was refreshed from the attached live config before adding the new block, preserving live smoke LOS settings, kit/profile lists, observed SteamIDs, and learned profile weights such as `fireclan=25`.

Rust server files to upload:
- oxide/plugins/RaidlandsRoamBots.cs
- oxide/config/RaidlandsRoamBots.json

Docs/reference files updated:
- Docs/RaidlandsRoamBots_Tactical_Rewrite_Plan_v2_LLM.md
- UPDATED_FILES_FOR_UPLOAD.txt
- UPDATED_FILES_FOR_UPLOAD_ROAMBOTS_SQUAD_REJOIN_2026-07-10.md

Reload after upload:
- oxide.reload RaidlandsRoamBots

Immediate live smoke:
- Spawn or find a same-team ambient pair with `raidbots.list`.
- Kill one bot while a same-team teammate is alive, healthy, and not recently damaged.
- After `Respawn Delay Seconds`, confirm the replacement appears directly beside that teammate and `raidbots.list` briefly shows `wake=squad_rejoin:Ns`.
- Confirm the new bot does not move, acquire, or fire during the wake/equip delay.
- Damage the surviving teammate and kill another same-team bot before the damage window expires.
- Expected: the damaged/recently damaged teammate is skipped; if no other healthy teammate is available, the replacement uses normal random-land population maintenance.
- Kill the whole squad.
- Expected: no teammate rejoin is attempted, and normal random-land population maintenance restores the missing ambient count.

Local verification:
- oxide/config/RaidlandsRoamBots.json parsed successfully.
- oxide/config/RaidlandsRoamBots.json was structurally compared against the attached live file and matches it except for the added `Ambient Squad Respawn Rejoin` block.
- Roslyn compile check passed for oxide/plugins/RaidlandsRoamBots.cs v0.3.76 against RustDedicated_Data/Managed, excluding Oxide.References.dll and glTFast.Newtonsoft.dll.
- Remaining warnings were expected Oxide plugin-reference/future-field warnings.
- Live Oxide reload and in-game death/respawn smoke testing are still required.

Local source hashes:
- oxide/plugins/RaidlandsRoamBots.cs SHA256: C5E28DB782D6A253A6E31736A040D87AF0255BA8178694FE0DA258F859F34FF4
- oxide/config/RaidlandsRoamBots.json SHA256: 129928F24479124037DBC0D27E339A76EF44866BAB4037008810E0A559FE03F0
