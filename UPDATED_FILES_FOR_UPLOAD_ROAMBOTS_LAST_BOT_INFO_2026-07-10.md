# Raidlands RoamBots last-bot info upload

## Upload

- `oxide/plugins/RaidlandsRoamBots.cs`

## Reload

- `oxide.reload RaidlandsRoamBots`

## Player smoke test

1. Before fighting a bot, run `/lastbotinfo`; expect the no-recent-fight message.
2. Kill a Raidlands roam bot, then run `/lastbotinfo` as a non-admin player.
3. Confirm the reply shows the bot name, `you won`, profile, training source, difficulty, kit, clan, and lifetime K/D.
4. Let a different Raidlands bot kill the player, respawn, and run `/lastbotinfo` again.
5. Confirm the reply now shows the newer bot and `bot won`.
6. Optionally confirm `/raidbots showlastinfo` returns the same information.

## Local verification

- Roslyn compile check passed for `oxide/plugins/RaidlandsRoamBots.cs` v0.3.75 against `RustDedicated_Data/Managed` with `Oxide.References.dll` and `glTFast.Newtonsoft.dll` excluded.
- Remaining warnings are the existing unused/unassigned Oxide plugin-reference and internal state warnings.
- Live Oxide reload and in-game smoke testing are still required.
