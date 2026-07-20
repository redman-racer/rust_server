# Raidlands Leaderboards v2.0.0 deployment

## Upload

- `oxide/plugins/RaidlandsLeaderboards.cs`

The plugin creates these files automatically after its first load:

- `oxide/config/Raidlands Leaderboards.json`
- `oxide/data/Raidlands Leaderboards.json`

## Commands

- `/leaderboard`
- `/lb`
- `/top`
- `/stats`
- `/stats <player name or Steam ID>`
- `/leaderboard lifetime`
- `/leaderboard wipe`

Leaderboards are public by default. If `Require use permission to open leaderboards` is enabled, grant:

```text
oxide.grant group default raidlandsleaderboards.use
```

Administrative permission:

```text
oxide.grant group admin raidlandsleaderboards.admin
```

## Verification

1. Reload with `oxide.reload RaidlandsLeaderboards`.
2. Confirm `Raidlands Leaderboards v2.0.0` loads without compiler errors and reports the cumulative legacy import when source files exist.
3. Open `/lb` and inspect Global, My Stats, Combat, Raiding, Activity, and Clans.
4. Use the Global and Clans pagers and confirm active players and clans are reachable with continuous absolute ranks.
5. Kill a test player, damage an enemy-owned structure, fire a rocket, and wait one sampling interval.
6. Reopen `/lb`; verify PvP, raid damage, and playtime changed, and test the sortable Global column headers.
7. Confirm the Clans plugin is loaded; every clan and its full member count should populate from `RaidlandsClanSnapshot`.
8. Restart once and verify the statistics persist.

After the next map wipe, use the scope button to cycle through This Wipe, each archived wipe, and Lifetime. Existing wipes from before v1.2.0 cannot be reconstructed because their reset data was not previously retained.

## Integration APIs

- `GetPlayerStats(ulong playerId, bool lifetime = false)`
- `GetLeaderboard(string metric = "kills", bool lifetime = false, int limit = 100)`
- `GetClanLeaderboard(bool lifetime = false, int limit = 100)`
- `GetPreviousWipes()`
- `GetPreviousWipeLeaderboard(int archiveIndex, string metric = "kills", int limit = 100)`

Supported player metrics are `kills`, `deaths`, `kdr`, `headshots`, `streak`, `raiddamage`, and `playtime`.

To hide staff or test accounts from every ranking, grant:

```text
oxide.grant user <steamid> raidlandsleaderboards.exclude
```
