using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Raidlands Leaderboards", "Raidlands", "2.0.0")]
    [Description("Branded lifetime and wipe player/clan statistics, rankings, UI, and integration APIs.")]
    public class RaidlandsLeaderboards : RustPlugin
    {
        [PluginReference] private Plugin Clans;
        [PluginReference] private Plugin RaidlandsUiEscapeBridge;

        private const string PermUse = "raidlandsleaderboards.use";
        private const string PermAdmin = "raidlandsleaderboards.admin";
        private const string PermExclude = "raidlandsleaderboards.exclude";
        private const string UiRoot = "RaidlandsLeaderboards.UI";
        private const string Accent = "0.12 0.58 0.55 1";
        private const string AccentAlt = "0.48 0.28 0.92 1";
        private const string Warning = "0.96 0.62 0.18 1";
        private const string Danger = "0.82 0.20 0.18 1";
        private const string Success = "0.24 0.68 0.36 1";
        private const string Background = "0.025 0.030 0.038 0.98";
        private const string Panel = "0.065 0.075 0.088 0.98";
        private const string PanelAlt = "0.09 0.105 0.12 0.98";
        private const string Text = "0.94 0.95 0.97 1";
        private const string Muted = "0.58 0.64 0.70 1";

        private ConfigData config;
        private StoredData data;
        private readonly Dictionary<ulong, UiState> uiStates = new Dictionary<ulong, UiState>();
        private readonly Dictionary<ulong, Vector3> lastPositions = new Dictionary<ulong, Vector3>();
        private readonly Dictionary<ulong, DateTime> lastSamples = new Dictionary<ulong, DateTime>();
        private readonly Dictionary<ulong, string> lastWeapons = new Dictionary<ulong, string>();
        private readonly Dictionary<string, DateTime> recentPvpKills = new Dictionary<string, DateTime>();
        private Timer pendingSaveTimer;

        private class ConfigData
        {
            [JsonProperty("Leaderboard title")] public string Title = "RAIDLANDS // LEADERBOARDS";
            [JsonProperty("Rows per leaderboard")] public int Rows = 10;
            [JsonProperty("Minimum clan members for clan leaderboard")] public int MinimumClanMembers = 1;
            [JsonProperty("Reset wipe statistics on new save")] public bool ResetWipeOnNewSave = true;
            [JsonProperty("Track damage dealt (higher hook volume)")] public bool TrackDamage = true;
            [JsonProperty("Hide inactive players from rankings")] public bool HideInactivePlayers = true;
            [JsonProperty("Minimum playtime seconds to count as active")] public long MinimumRankedPlaytimeSeconds = 300;
            [JsonProperty("Minimum kills required for K/D and headshot-rate rankings")] public int MinimumKillsForKdrRanking = 10;
            [JsonProperty("Ignore same-clan PvP kills")] public bool IgnoreSameClanKills = true;
            [JsonProperty("Ignore same-team PvP kills")] public bool IgnoreSameTeamKills = true;
            [JsonProperty("Count kills against sleeping players")] public bool CountSleeperKills = false;
            [JsonProperty("Repeat victim kill credit cooldown seconds")] public int RepeatVictimCooldownSeconds = 120;
            [JsonProperty("Require use permission to open leaderboards")] public bool RequireUsePermission = false;
            [JsonProperty("Playtime sample interval seconds")] public float SampleIntervalSeconds = 60f;
            [JsonProperty("Maximum movement counted per sample metres")] public float MaximumMovementPerSample = 1000f;
            [JsonProperty("Previous wipes retained")] public int PreviousWipesRetained = 12;
            [JsonProperty("Import cumulative KDRScoreboard and PlaytimeTracker data")] public bool ImportLegacyLifetimeStats = true;
            [JsonProperty("Chat command aliases")] public List<string> Commands = new List<string> { "leaderboard", "lb", "top", "stats" };
        }

        private class StoredData
        {
            public int SchemaVersion;
            public string WipeId;
            public string WorldIdentity;
            public string WipeStartedUtc;
            public Dictionary<ulong, PlayerRecord> Players = new Dictionary<ulong, PlayerRecord>();
            public List<WipeArchive> PreviousWipes = new List<WipeArchive>();
        }

        private class WipeArchive
        {
            public string WipeId;
            public string StartedUtc;
            public string EndedUtc;
            public Dictionary<ulong, ArchivedPlayer> Players = new Dictionary<ulong, ArchivedPlayer>();
        }

        private class ArchivedPlayer
        {
            public ulong Id;
            public string Name;
            public string Clan;
            public StatBlock Stats = new StatBlock();
        }

        private class LegacyKdrData
        {
            public ulong id;
            public string name;
            public int kills;
            public int deaths;
        }

        private class LegacyPlaytimeData
        {
            public Dictionary<string, LegacyPlaytimeUser> _userData = new Dictionary<string, LegacyPlaytimeUser>();
        }

        private class LegacyPlaytimeUser
        {
            public double playtime;
            public double PlayTime;
            public string displayName;
        }

        private class PlayerRecord
        {
            public ulong Id;
            public string Name;
            public string FirstSeenUtc;
            public string LastSeenUtc;
            public string CurrentClan;
            public StatBlock Lifetime = new StatBlock();
            public StatBlock Wipe = new StatBlock();
        }

        private class StatBlock
        {
            public long SecondsPlayed;
            public int Connections;
            public int PlayerKills;
            public int NpcKills;
            public int AnimalKills;
            public int Deaths;
            public int PvpDeaths;
            public int Suicides;
            public int Headshots;
            public int MeleeKills;
            public int RangedKills;
            public int ExplosiveKills;
            public double PlayerDamage;
            public double NpcDamage;
            public double RaidDamage;
            public long ItemsCrafted;
            public int StructuresBuilt;
            public int EntitiesDestroyed;
            public int ExplosivesThrown;
            public int ShotsFired;
            public int RocketsFired;
            public int HealingUsed;
            public double DistanceTravelled;
            public int LongestKillMetres;
            public int CurrentKillStreak;
            public int BestKillStreak;
        }

        private class UiState
        {
            public string Tab = "global";
            public bool Lifetime;
            public ulong SelectedPlayer;
            public int PlayerPage;
            public int ClanPage;
            public int ArchiveIndex = -1;
            public string SortMetric = "kills";
        }

        private class ClanStanding
        {
            public string Tag;
            public int Members;
            public StatBlock Stats = new StatBlock();
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { config = Config.ReadObject<ConfigData>() ?? new ConfigData(); }
            catch { PrintWarning("Invalid config; loading defaults."); config = new ConfigData(); }
            config.Rows = Mathf.Clamp(config.Rows, 5, 20);
            config.SampleIntervalSeconds = Mathf.Clamp(config.SampleIntervalSeconds, 15f, 300f);
            config.MaximumMovementPerSample = Mathf.Clamp(config.MaximumMovementPerSample, 100f, 5000f);
            config.MinimumRankedPlaytimeSeconds = Math.Max(0L, config.MinimumRankedPlaytimeSeconds);
            config.MinimumKillsForKdrRanking = Math.Max(0, config.MinimumKillsForKdrRanking);
            config.RepeatVictimCooldownSeconds = Mathf.Clamp(config.RepeatVictimCooldownSeconds, 0, 3600);
            if (config.Commands == null || config.Commands.Count == 0) config.Commands = new List<string> { "leaderboard", "lb", "top", "stats" };
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermExclude, this);
            data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
            EnsureData();
            foreach (var command in config.Commands.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
                cmd.AddChatCommand(command.Trim(), this, nameof(LeaderboardCommand));
        }

        private void OnServerInitialized()
        {
            DetectMissedWipe();
            EnsureWipeIdentity(null);
            if (config.ImportLegacyLifetimeStats) ImportLegacyLifetimeStats();
            foreach (var player in BasePlayer.activePlayerList) TrackConnection(player, false);
            timer.Every(config.SampleIntervalSeconds, SampleOnlinePlayers);
            timer.Every(300f, SaveData);
        }

        private void Unload()
        {
            SampleOnlinePlayers();
            SaveData();
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UiRoot);
                RaidlandsUiEscapeBridge?.Call("UnregisterUi", player, this, UiRoot);
            }
        }

        private void OnServerSave() => SaveData();

        private void OnNewSave(string filename)
        {
            if (!config.ResetWipeOnNewSave) { EnsureWipeIdentity(filename); return; }
            ArchiveCurrentWipe();
            EnsureWipeIdentity(filename);
            recentPvpKills.Clear();
            foreach (var record in data.Players.Values) record.Wipe = new StatBlock();
            data.WipeStartedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            SaveData();
            Puts("New save detected; the previous wipe was archived and current wipe statistics were reset.");
        }

        private void OnPlayerConnected(BasePlayer player) => TrackConnection(player, true);

        private void OnClanMemberJoined(string tag, string joining, List<string> members)
        {
            SetClanCache(joining, tag);
            foreach (var member in members ?? new List<string>()) SetClanCache(member, tag);
        }

        private void OnClanMemberGone(string tag, string leaving, List<string> members)
        {
            SetClanCache(leaving, null);
            foreach (var member in members ?? new List<string>()) SetClanCache(member, tag);
        }

        private void OnClanDisbanded(string tag, List<string> members)
        {
            foreach (var member in members ?? new List<string>()) SetClanCache(member, null);
            foreach (var record in data.Players.Values.Where(value => value != null && string.Equals(value.CurrentClan, tag, StringComparison.OrdinalIgnoreCase))) record.CurrentClan = null;
        }

        private void SetClanCache(string playerId, string tag)
        {
            ulong id;
            if (!ulong.TryParse(playerId, out id) || id == 0) return;
            EnsurePlayer(id, null).CurrentClan = tag;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            SamplePlayer(player);
            if (player != null) lastPositions.Remove(player.userID);
            if (player != null) lastSamples.Remove(player.userID);
            QueueSaveData();
        }

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            if (victim == null) return;
            if (victim.IsNpc)
            {
                var npcAttacker = info?.InitiatorPlayer;
                if (npcAttacker != null && npcAttacker.userID != 0 && !npcAttacker.IsNpc) Increment(npcAttacker.userID, block => block.NpcKills++);
                return;
            }
            if (victim.userID == 0) return;
            Increment(victim.userID, block => block.Deaths++);
            var attacker = info?.InitiatorPlayer;
            if (attacker == null || attacker.userID == 0 || attacker.IsNpc || attacker.userID == victim.userID)
            {
                if (attacker == null || attacker?.userID == victim.userID) Increment(victim.userID, block => block.Suicides++);
                ResetStreak(victim.userID);
                return;
            }
            if (!IsCreditedPvpKill(attacker, victim))
            {
                ResetStreak(victim.userID);
                return;
            }
            Increment(victim.userID, block => block.PvpDeaths++);
            Increment(attacker.userID, block =>
            {
                block.PlayerKills++;
                block.CurrentKillStreak++;
                block.BestKillStreak = Math.Max(block.BestKillStreak, block.CurrentKillStreak);
                if (info.isHeadshot) block.Headshots++;
                var weapon = WeaponName(info, attacker);
                if (IsExplosiveWeapon(weapon)) block.ExplosiveKills++;
                else if (IsMeleeWeapon(weapon)) block.MeleeKills++;
                else block.RangedKills++;
                var distance = Vector3.Distance(attacker.transform.position, victim.transform.position);
                block.LongestKillMetres = Math.Max(block.LongestKillMetres, Mathf.RoundToInt(distance));
            });
            ResetStreak(victim.userID);
        }

        private bool IsCreditedPvpKill(BasePlayer attacker, BasePlayer victim)
        {
            if (attacker == null || victim == null) return false;
            if (!config.CountSleeperKills && victim.IsSleeping()) return false;
            if (config.IgnoreSameTeamKills && attacker.currentTeam != 0 && attacker.currentTeam == victim.currentTeam) return false;
            if (config.IgnoreSameClanKills)
            {
                var attackerClan = GetClanTag(attacker.userID);
                var victimClan = GetClanTag(victim.userID);
                if (!string.IsNullOrEmpty(attackerClan) && string.Equals(attackerClan, victimClan, StringComparison.OrdinalIgnoreCase)) return false;
            }
            if (config.RepeatVictimCooldownSeconds <= 0) return true;
            var key = attacker.userID + ":" + victim.userID;
            DateTime lastKill;
            var now = DateTime.UtcNow;
            if (recentPvpKills.TryGetValue(key, out lastKill) && (now - lastKill).TotalSeconds < config.RepeatVictimCooldownSeconds) return false;
            recentPvpKills[key] = now;
            if (recentPvpKills.Count > 4096)
            {
                var cutoff = now.AddSeconds(-Math.Max(60, config.RepeatVictimCooldownSeconds));
                foreach (var expired in recentPvpKills.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToList()) recentPvpKills.Remove(expired);
            }
            return true;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || entity is BasePlayer) return;
            var attacker = info?.InitiatorPlayer;
            if (attacker == null || attacker.userID == 0 || attacker.IsNpc) return;
            if (entity is BaseNpc || entity.ShortPrefabName.IndexOf("scientist", StringComparison.OrdinalIgnoreCase) >= 0 || entity.ShortPrefabName.IndexOf("murderer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var npc = entity.ShortPrefabName.IndexOf("scientist", StringComparison.OrdinalIgnoreCase) >= 0 || entity.ShortPrefabName.IndexOf("murderer", StringComparison.OrdinalIgnoreCase) >= 0;
                Increment(attacker.userID, block => { if (npc) block.NpcKills++; else block.AnimalKills++; });
            }
            if (entity.OwnerID != 0 && entity.OwnerID != attacker.userID) Increment(attacker.userID, block => block.EntitiesDestroyed++);
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (!config.TrackDamage || entity == null || info == null) return;
            var attacker = info.InitiatorPlayer;
            if (attacker == null || attacker.userID == 0 || attacker.IsNpc || attacker == entity) return;
            var damage = Math.Max(0d, info.damageTypes.Total());
            if (damage <= 0d) return;
            Increment(attacker.userID, block =>
            {
                if (entity is BasePlayer && !((BasePlayer)entity).IsNpc) block.PlayerDamage += damage;
                else if (entity.OwnerID != 0 && entity.OwnerID != attacker.userID) block.RaidDamage += damage;
                else block.NpcDamage += damage;
            });
        }

        private void OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter itemCrafter)
        {
            var player = itemCrafter?.owner;
            if (player == null || item == null) return;
            Increment(player.userID, block => block.ItemsCrafted += Math.Max(1, item.amount));
        }

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            var player = planner?.GetOwnerPlayer();
            if (player != null) Increment(player.userID, block => block.StructuresBuilt++);
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
        {
            if (player != null) Increment(player.userID, block => block.ExplosivesThrown++);
        }

        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (player == null) return;
            var weapon = projectile?.GetItem()?.info?.shortname ?? projectile?.ShortPrefabName ?? string.Empty;
            lastWeapons[player.userID] = weapon;
            Increment(player.userID, block =>
            {
                block.ShotsFired++;
                if (weapon.IndexOf("rocket", StringComparison.OrdinalIgnoreCase) >= 0) block.RocketsFired++;
            });
        }

        private void OnHealingItemUse(MedicalTool item, BasePlayer player, IMedicalToolTarget target)
        {
            if (player != null) Increment(player.userID, block => block.HealingUsed++);
        }

        private void LeaderboardCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null || !CanUse(player)) return;
            var state = GetState(player);
            if (args != null && args.Length > 0)
            {
                var query = string.Join(" ", args).Trim();
                var target = FindRecord(query);
                if (target != null) state.SelectedPlayer = target.Id;
                else if (query.Equals("lifetime", StringComparison.OrdinalIgnoreCase)) { state.Lifetime = true; state.ArchiveIndex = -1; }
                else if (query.Equals("wipe", StringComparison.OrdinalIgnoreCase)) { state.Lifetime = false; state.ArchiveIndex = -1; }
            }
            Draw(player);
        }

        [ConsoleCommand("raidlandsleaderboards.ui")]
        private void UiCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !CanUse(player)) return;
            var args = arg.Args == null ? new string[0] : arg.Args.Select(value => value.ToString()).ToArray();
            if (args.Length == 0) return;
            var state = GetState(player);
            if (args[0] == "close") { Close(player); return; }
            if (args[0] == "tab" && args.Length > 1)
            {
                var tab = args[1].ToLowerInvariant();
                state.Tab = tab == "global" || tab == "combat" || tab == "raiding" || tab == "activity" || tab == "clans" ? tab : "overview";
            }
            else if (args[0] == "scope") CycleScope(state);
            else if (args[0] == "player" && args.Length > 1 && ulong.TryParse(args[1], out var id)) { state.SelectedPlayer = id; state.Tab = "overview"; }
            else if (args[0] == "page" && args.Length > 2 && int.TryParse(args[2], out var page))
            {
                if (args[1] == "players") state.PlayerPage = Math.Max(0, page);
                else if (args[1] == "clans") state.ClanPage = Math.Max(0, page);
            }
            else if (args[0] == "sort" && args.Length > 1)
            {
                var metric = args[1].ToLowerInvariant();
                state.SortMetric = metric == "kdr" || metric == "headshots" || metric == "streak" || metric == "playtime" || metric == "deaths" || metric == "raiddamage" ? metric : "kills";
                state.PlayerPage = 0;
            }
            Draw(player);
        }

        private void Draw(BasePlayer player)
        {
            EnsurePlayer(player);
            var state = GetState(player);
            if (state.SelectedPlayer == 0) state.SelectedPlayer = player.userID;
            CuiHelper.DestroyUi(player, UiRoot);
            var c = new CuiElementContainer();
            AddPanel(c, UiRoot, "Overlay", Background, "0.08 0.07", "0.92 0.93", true);
            AddPanel(c, UiRoot + ".Top", UiRoot, "0.045 0.052 0.065 1", "0 0.89", "1 1");
            AddPanel(c, UiRoot + ".TopAccent", UiRoot + ".Top", Accent, "0 0", "0.72 0.025");
            AddPanel(c, UiRoot + ".TopAccent2", UiRoot + ".Top", AccentAlt, "0.72 0", "1 0.025");
            AddLabel(c, UiRoot + ".Brand", UiRoot + ".Top", "RL", 19, "0.025 0.20", "0.085 0.80", TextAnchor.MiddleCenter, Accent);
            AddLabel(c, UiRoot + ".Title", UiRoot + ".Top", config.Title, 18, "0.10 0.47", "0.62 0.86", TextAnchor.MiddleLeft, Text);
            AddLabel(c, UiRoot + ".Subtitle", UiRoot + ".Top", "SEASON INTELLIGENCE  //  LIVE PLAYER + CLAN RANKINGS", 8, "0.10 0.16", "0.62 0.48", TextAnchor.MiddleLeft, Muted);
            AddButton(c, UiRoot + ".Scope", UiRoot + ".Top", ScopeLabel(state), "raidlandsleaderboards.ui scope", state.Lifetime ? AccentAlt : Accent, "0.70 0.25", "0.89 0.75", 9);
            AddButton(c, UiRoot + ".Close", UiRoot + ".Top", "X", "raidlandsleaderboards.ui close", Danger, "0.93 0.25", "0.975 0.75", 12);

            var tabs = new[] { "global", "overview", "combat", "raiding", "activity", "clans" };
            var x = 0.025;
            foreach (var tab in tabs)
            {
                var tabLabel = tab == "overview" ? "MY STATS" : tab.ToUpperInvariant();
                AddButton(c, UiRoot + ".Tab." + tab, UiRoot, tabLabel, "raidlandsleaderboards.ui tab " + tab, state.Tab == tab ? Accent : "0.075 0.085 0.10 1", $"{x:0.###} 0.825", $"{x + 0.14:0.###} 0.875", 9);
                x += 0.15;
            }

            if (state.Tab == "global") DrawGlobalPage(c, state, player);
            else if (state.Tab == "clans") DrawClanPage(c, state);
            else DrawPlayerPage(c, player, state);
            RaidlandsUiEscapeBridge?.Call("RegisterUi", player, this, UiRoot, nameof(OnUiBridgeClosed));
            CuiHelper.AddUi(player, c);
        }

        private void DrawGlobalPage(CuiElementContainer c, UiState state, BasePlayer viewer)
        {
            const int pageSize = 7;
            var players = RankedPlayers(state.SortMetric, state).ToList();
            state.PlayerPage = ClampPage(state.PlayerPage, players.Count, pageSize);

            AddPanel(c, UiRoot + ".GlobalPlayers", UiRoot, Panel, "0.025 0.08", "0.975 0.80");
            AddLabel(c, UiRoot + ".GlobalPlayersTitle", UiRoot + ".GlobalPlayers", "GLOBAL PLAYER RANKINGS", 17, "0.025 0.91", "0.55 0.985", TextAnchor.MiddleLeft, Text);
            var viewerIndex = players.FindIndex(record => record.Id == viewer.userID);
            var viewerRank = viewerIndex >= 0 ? "  //  YOUR RANK #" + (viewerIndex + 1) : string.Empty;
            AddLabel(c, UiRoot + ".GlobalPlayersScope", UiRoot + ".GlobalPlayers", ScopeLabel(state) + "  //  SORTED BY " + MetricLabel(state.SortMetric) + viewerRank, 9, "0.48 0.91", "0.975 0.985", TextAnchor.MiddleRight, Accent);
            AddPanel(c, UiRoot + ".GlobalPlayersHeader", UiRoot + ".GlobalPlayers", "0.035 0.042 0.052 1", "0.025 0.825", "0.975 0.895");
            AddPlayerTableHeader(c, UiRoot + ".GlobalPlayersHeader", state);
            if (players.Count == 0)
                AddLabel(c, UiRoot + ".GlobalPlayersEmpty", UiRoot + ".GlobalPlayers", "NO ELIGIBLE PLAYER STATS YET  //  RANKINGS APPEAR AFTER COMBAT OR 5 MINUTES PLAYED", 11, "0.10 0.35", "0.90 0.70", TextAnchor.MiddleCenter, Muted);

            var y = 0.715;
            var playerOffset = state.PlayerPage * pageSize;
            foreach (var entry in players.Skip(playerOffset).Take(pageSize).Select((record, index) => new { record, index }))
            {
                var rank = playerOffset + entry.index + 1;
                var block = StatsFor(entry.record.Id, state);
                var row = UiRoot + ".GlobalPlayer." + rank;
                var rowColor = entry.record.Id == viewer.userID ? "0.09 0.30 0.29 1" : entry.index % 2 == 0 ? PanelAlt : "0.075 0.085 0.098 1";
                AddButton(c, row, UiRoot + ".GlobalPlayers", "", "raidlandsleaderboards.ui player " + entry.record.Id, rowColor, $"0.025 {y:0.###}", $"0.975 {y + 0.09:0.###}", 1);
                AddPlayerTableCells(c, row, "#" + rank, Short(PlayerNameFor(entry.record.Id, state), 22), ClanFor(entry.record.Id, state) ?? "-", block.PlayerKills.ToString("N0"), block.PvpDeaths.ToString("N0"), Kdr(block).ToString("0.00"), HeadshotRate(block).ToString("0") + "%", block.BestKillStreak.ToString("N0"), FormatDuration(block.SecondsPlayed), 11, rank <= 3 ? Warning : Text);
                y -= 0.098;
            }

            DrawPager(c, UiRoot + ".GlobalPlayers", "players", state.PlayerPage, players.Count, pageSize);
        }

        private void AddPlayerTableHeader(CuiElementContainer c, string parent, UiState state)
        {
            AddLabel(c, parent + ".Rank", parent, "RANK", 9, "0.015 0", "0.07 1", TextAnchor.MiddleCenter, Muted);
            AddLabel(c, parent + ".Player", parent, "PLAYER", 9, "0.075 0", "0.28 1", TextAnchor.MiddleLeft, Muted);
            AddLabel(c, parent + ".Clan", parent, "CLAN", 9, "0.285 0", "0.38 1", TextAnchor.MiddleLeft, Muted);
            AddSortHeader(c, parent, state, "kills", "KILLS", "0.385 0", "0.46 1");
            AddSortHeader(c, parent, state, "deaths", "PVP D", "0.465 0", "0.54 1");
            AddSortHeader(c, parent, state, "kdr", "K/D", "0.545 0", "0.62 1");
            AddSortHeader(c, parent, state, "headshots", "HS%", "0.625 0", "0.70 1");
            AddSortHeader(c, parent, state, "streak", "STREAK", "0.705 0", "0.79 1");
            AddSortHeader(c, parent, state, "playtime", "PLAYTIME", "0.795 0", "0.985 1");
        }

        private void AddSortHeader(CuiElementContainer c, string parent, UiState state, string metric, string label, string min, string max)
        {
            AddButton(c, parent + ".Sort." + metric, parent, label, "raidlandsleaderboards.ui sort " + metric, state.SortMetric == metric ? Accent : "0 0 0 0", min, max, 9);
        }

        private void AddPlayerTableCells(CuiElementContainer c, string parent, string rank, string player, string clan, string kills, string deaths, string kdr, string headshots, string streak, string playtime, int size, string color)
        {
            AddLabel(c, parent + ".Rank", parent, rank, size, "0.015 0", "0.07 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".Player", parent, player, size, "0.075 0", "0.28 1", TextAnchor.MiddleLeft, color);
            AddLabel(c, parent + ".Clan", parent, clan, size, "0.285 0", "0.38 1", TextAnchor.MiddleLeft, Accent);
            AddLabel(c, parent + ".Kills", parent, kills, size, "0.385 0", "0.46 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".Deaths", parent, deaths, size, "0.465 0", "0.54 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".Kdr", parent, kdr, size, "0.545 0", "0.62 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".Headshots", parent, headshots, size, "0.625 0", "0.70 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".Streak", parent, streak, size, "0.705 0", "0.79 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".Playtime", parent, playtime, size, "0.795 0", "0.985 1", TextAnchor.MiddleCenter, color);
        }

        private int ClampPage(int page, int count, int pageSize) => Mathf.Clamp(page, 0, Math.Max(0, (count - 1) / pageSize));

        private void DrawPager(CuiElementContainer c, string parent, string type, int page, int count, int pageSize)
        {
            var pages = Math.Max(1, (count + pageSize - 1) / pageSize);
            AddButton(c, parent + ".Prev", parent, "<", "raidlandsleaderboards.ui page " + type + " " + (page - 1), PanelAlt, "0.04 0.025", "0.15 0.09", 10);
            AddLabel(c, parent + ".Page", parent, $"PAGE {page + 1}/{pages}  //  {count} TOTAL", 8, "0.18 0.025", "0.82 0.09", TextAnchor.MiddleCenter, Muted);
            AddButton(c, parent + ".Next", parent, ">", "raidlandsleaderboards.ui page " + type + " " + (page + 1), PanelAlt, "0.85 0.025", "0.96 0.09", 10);
        }

        private void DrawPlayerPage(CuiElementContainer c, BasePlayer viewer, UiState state)
        {
            var metric = MetricForTab(state.Tab);
            var standings = RankedPlayers(metric, state).Take(config.Rows).ToList();
            AddPanel(c, UiRoot + ".Ranks", UiRoot, Panel, "0.025 0.08", "0.60 0.80");
            AddLabel(c, UiRoot + ".RanksHeader", UiRoot + ".Ranks", "RANK   PLAYER                         " + MetricLabel(metric), 9, "0.035 0.91", "0.965 0.98", TextAnchor.MiddleLeft, Muted);
            var y = 0.82;
            for (var i = 0; i < standings.Count; i++)
            {
                var record = standings[i];
                var block = StatsFor(record.Id, state);
                var color = i == 0 ? Warning : i == 1 ? "0.66 0.70 0.76 1" : i == 2 ? "0.70 0.43 0.24 1" : PanelAlt;
                AddButton(c, UiRoot + ".Rank." + record.Id, UiRoot + ".Ranks", $"#{i + 1,-3}  {Short(PlayerNameFor(record.Id, state), 27),-27}  {FormatMetric(metric, block)}", "raidlandsleaderboards.ui player " + record.Id, color, $"0.035 {y:0.###}", $"0.965 {y + 0.075:0.###}", 9, TextAnchor.MiddleLeft);
                y -= 0.085;
            }
            var selected = data.Players.TryGetValue(state.SelectedPlayer, out var found) ? found : EnsurePlayer(viewer);
            DrawPlayerCard(c, selected, state, metric);
        }

        private void DrawPlayerCard(CuiElementContainer c, PlayerRecord record, UiState state, string metric)
        {
            var block = StatsFor(record.Id, state);
            AddPanel(c, UiRoot + ".Card", UiRoot, Panel, "0.625 0.08", "0.975 0.80");
            AddPanel(c, UiRoot + ".CardBand", UiRoot + ".Card", AccentAlt, "0 0.965", "1 1");
            AddLabel(c, UiRoot + ".CardName", UiRoot + ".Card", Short(PlayerNameFor(record.Id, state), 28), 17, "0.06 0.88", "0.94 0.96", TextAnchor.MiddleLeft, Text);
            AddLabel(c, UiRoot + ".CardClan", UiRoot + ".Card", "CLAN  " + (ClanFor(record.Id, state) ?? "NO CLAN"), 8, "0.06 0.82", "0.94 0.88", TextAnchor.MiddleLeft, Accent);
            if (metric == "raiddamage")
            {
                AddStat(c, "RAID DAMAGE", Compact((long)block.RaidDamage), 0, 0);
                AddStat(c, "ROCKETS FIRED", block.RocketsFired.ToString("N0"), 1, 0);
                AddStat(c, "EXPLOSIVES USED", block.ExplosivesThrown.ToString("N0"), 0, 1);
                AddStat(c, "ENTITIES DESTROYED", block.EntitiesDestroyed.ToString("N0"), 1, 1);
                AddStat(c, "PVP KILLS", block.PlayerKills.ToString("N0"), 0, 2);
                AddStat(c, "K/D", Kdr(block).ToString("0.00"), 1, 2);
                AddStat(c, "SHOTS FIRED", block.ShotsFired.ToString("N0"), 0, 3);
                AddStat(c, "PLAYTIME", FormatDuration(block.SecondsPlayed), 1, 3);
            }
            else if (metric == "playtime")
            {
                AddStat(c, "PLAYTIME", FormatDuration(block.SecondsPlayed), 0, 0);
                AddStat(c, "SESSIONS", block.Connections.ToString("N0"), 1, 0);
                AddStat(c, "DISTANCE", Compact((long)block.DistanceTravelled) + "m", 0, 1);
                AddStat(c, "HEALING USED", block.HealingUsed.ToString("N0"), 1, 1);
                AddStat(c, "SHOTS FIRED", block.ShotsFired.ToString("N0"), 0, 2);
                AddStat(c, "NPC KILLS", block.NpcKills.ToString("N0"), 1, 2);
                AddStat(c, "PVP KILLS", block.PlayerKills.ToString("N0"), 0, 3);
                AddStat(c, "K/D", Kdr(block).ToString("0.00"), 1, 3);
            }
            else
            {
                AddStat(c, "KILLS", block.PlayerKills.ToString("N0"), 0, 0);
                AddStat(c, "K/D", Kdr(block).ToString("0.00"), 1, 0);
                AddStat(c, "HEADSHOTS", block.Headshots.ToString("N0"), 0, 1);
                AddStat(c, "HEADSHOT RATE", HeadshotRate(block).ToString("0") + "%", 1, 1);
                AddStat(c, "PVP DAMAGE", Compact((long)block.PlayerDamage), 0, 2);
                AddStat(c, "PVP DEATHS", block.PvpDeaths.ToString("N0"), 1, 2);
                AddStat(c, "BEST STREAK", block.BestKillStreak.ToString("N0"), 0, 3);
                AddStat(c, "LONGEST KILL", block.LongestKillMetres + "m", 1, 3);
            }
            var rank = PlayerRank(record.Id, metric, state);
            var rankText = rank > 0 ? "GLOBAL RANK  #" + rank : "UNRANKED";
            AddLabel(c, UiRoot + ".CardRank", UiRoot + ".Card", rankText + "   //   " + FormatMetric(metric, block), 9, "0.06 0.05", "0.94 0.12", TextAnchor.MiddleCenter, Warning);
        }

        private void AddStat(CuiElementContainer c, string label, string value, int column, int row)
        {
            var x1 = 0.06 + column * 0.47;
            var x2 = x1 + 0.41;
            var y2 = 0.78 - row * 0.16;
            var y1 = y2 - 0.125;
            var name = UiRoot + ".Card.Stat." + column + "." + row;
            AddPanel(c, name, UiRoot + ".Card", PanelAlt, $"{x1:0.###} {y1:0.###}", $"{x2:0.###} {y2:0.###}");
            AddLabel(c, name + ".Label", name, label, 7, "0.06 0.52", "0.94 0.90", TextAnchor.MiddleLeft, Muted);
            AddLabel(c, name + ".Value", name, value, 13, "0.06 0.06", "0.94 0.56", TextAnchor.MiddleLeft, Text);
        }

        private void DrawClanPage(CuiElementContainer c, UiState state)
        {
            const int pageSize = 7;
            var clans = BuildClanStandings(state);
            state.ClanPage = ClampPage(state.ClanPage, clans.Count, pageSize);
            AddPanel(c, UiRoot + ".ClanRanks", UiRoot, Panel, "0.025 0.08", "0.975 0.80");
            AddLabel(c, UiRoot + ".ClanTitle", UiRoot + ".ClanRanks", "GLOBAL CLAN RANKINGS", 17, "0.025 0.91", "0.55 0.985", TextAnchor.MiddleLeft, Text);
            AddLabel(c, UiRoot + ".ClanFormula", UiRoot + ".ClanRanks", ScopeLabel(state) + "  //  RANKED BY KILLS, K/D, PLAYTIME", 9, "0.55 0.91", "0.975 0.985", TextAnchor.MiddleRight, AccentAlt);
            AddPanel(c, UiRoot + ".ClanHeader", UiRoot + ".ClanRanks", "0.035 0.042 0.052 1", "0.025 0.825", "0.975 0.895");
            AddClanTableCells(c, UiRoot + ".ClanHeader", "RANK", "CLAN", "MEMBERS", "KILLS", "PVP DEATHS", "K/D", "PLAYTIME", "RAID DAMAGE", 9, Muted);
            if (clans.Count == 0)
                AddLabel(c, UiRoot + ".ClanEmpty", UiRoot + ".ClanRanks", "NO CLAN COMBAT STATS ARE AVAILABLE FOR THIS WIPE", 11, "0.10 0.35", "0.90 0.70", TextAnchor.MiddleCenter, Muted);
            var y = 0.715;
            var offset = state.ClanPage * pageSize;
            foreach (var entry in clans.Skip(offset).Take(pageSize).Select((clan, index) => new { clan, index }))
            {
                var rank = offset + entry.index + 1;
                var row = UiRoot + ".Clan." + rank;
                AddPanel(c, row, UiRoot + ".ClanRanks", entry.index % 2 == 0 ? PanelAlt : "0.075 0.085 0.098 1", $"0.025 {y:0.###}", $"0.975 {y + 0.09:0.###}");
                AddClanTableCells(c, row, "#" + rank, "[" + Short(entry.clan.Tag, 20) + "]", entry.clan.Members.ToString("N0"), entry.clan.Stats.PlayerKills.ToString("N0"), entry.clan.Stats.PvpDeaths.ToString("N0"), Kdr(entry.clan.Stats).ToString("0.00"), FormatDuration(entry.clan.Stats.SecondsPlayed), Compact((long)entry.clan.Stats.RaidDamage), 11, rank <= 3 ? Warning : Text);
                y -= 0.098;
            }
            DrawPager(c, UiRoot + ".ClanRanks", "clans", state.ClanPage, clans.Count, pageSize);
        }

        private void AddClanTableCells(CuiElementContainer c, string parent, string rank, string clan, string members, string kills, string deaths, string kdr, string playtime, string raidDamage, int size, string color)
        {
            AddLabel(c, parent + ".Rank", parent, rank, size, "0.015 0", "0.075 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".Clan", parent, clan, size, "0.085 0", "0.34 1", TextAnchor.MiddleLeft, color);
            AddLabel(c, parent + ".Members", parent, members, size, "0.345 0", "0.46 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".Kills", parent, kills, size, "0.465 0", "0.56 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".Deaths", parent, deaths, size, "0.565 0", "0.66 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".Kdr", parent, kdr, size, "0.665 0", "0.75 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".Playtime", parent, playtime, size, "0.755 0", "0.87 1", TextAnchor.MiddleCenter, color);
            AddLabel(c, parent + ".RaidDamage", parent, raidDamage, size, "0.875 0", "0.985 1", TextAnchor.MiddleCenter, color);
        }

        private void SampleOnlinePlayers()
        {
            foreach (var player in BasePlayer.activePlayerList) SamplePlayer(player);
        }

        private void SamplePlayer(BasePlayer player)
        {
            if (player == null || player.userID == 0 || player.IsNpc) return;
            var now = DateTime.UtcNow;
            var seconds = lastSamples.TryGetValue(player.userID, out var lastSample)
                ? Math.Max(1, Math.Min(Mathf.RoundToInt(config.SampleIntervalSeconds * 2f), (int)(now - lastSample).TotalSeconds))
                : Math.Max(1, Mathf.RoundToInt(config.SampleIntervalSeconds));
            Increment(player.userID, block => block.SecondsPlayed += seconds);
            if (lastPositions.TryGetValue(player.userID, out var previous))
            {
                var distance = Vector3.Distance(previous, player.transform.position);
                if (distance >= 0f && distance <= config.MaximumMovementPerSample) Increment(player.userID, block => block.DistanceTravelled += distance);
            }
            lastPositions[player.userID] = player.transform.position;
            lastSamples[player.userID] = now;
            Touch(player);
        }

        private void TrackConnection(BasePlayer player, bool count)
        {
            if (player == null || player.userID == 0 || player.IsNpc) return;
            var record = EnsurePlayer(player);
            record.Name = player.displayName;
            record.CurrentClan = GetClanTag(player.userID);
            record.LastSeenUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            if (count) { record.Lifetime.Connections++; record.Wipe.Connections++; }
            lastPositions[player.userID] = player.transform.position;
            lastSamples[player.userID] = DateTime.UtcNow;
        }

        private void Increment(ulong id, Action<StatBlock> action)
        {
            if (id == 0 || action == null) return;
            var record = EnsurePlayer(id, null);
            action(record.Lifetime);
            action(record.Wipe);
        }

        private void ResetStreak(ulong id)
        {
            var record = EnsurePlayer(id, null);
            record.Lifetime.CurrentKillStreak = 0;
            record.Wipe.CurrentKillStreak = 0;
        }

        private PlayerRecord EnsurePlayer(BasePlayer player) => EnsurePlayer(player.userID, player.displayName);

        private PlayerRecord EnsurePlayer(ulong id, string name)
        {
            if (data == null || data.Players == null) EnsureData();
            if (!data.Players.TryGetValue(id, out var record) || record == null)
            {
                record = new PlayerRecord { Id = id, Name = string.IsNullOrEmpty(name) ? id.ToString() : name, FirstSeenUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") };
                data.Players[id] = record;
            }
            if (!string.IsNullOrWhiteSpace(name)) record.Name = name;
            if (record.Lifetime == null) record.Lifetime = new StatBlock();
            if (record.Wipe == null) record.Wipe = new StatBlock();
            return record;
        }

        private void Touch(BasePlayer player)
        {
            var record = EnsurePlayer(player);
            record.Name = player.displayName;
            record.LastSeenUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void EnsureData()
        {
            if (data == null) data = new StoredData();
            if (data.Players == null) data.Players = new Dictionary<ulong, PlayerRecord>();
            if (data.PreviousWipes == null) data.PreviousWipes = new List<WipeArchive>();
            else data.PreviousWipes = data.PreviousWipes.Where(value => value != null).ToList();
            foreach (var record in data.Players.Values.Where(value => value != null))
            {
                if (record.Lifetime == null) record.Lifetime = new StatBlock();
                if (record.Wipe == null) record.Wipe = new StatBlock();
            }
            foreach (var wipe in data.PreviousWipes.Where(value => value != null))
            {
                if (wipe.Players == null) wipe.Players = new Dictionary<ulong, ArchivedPlayer>();
                foreach (var player in wipe.Players.Values.Where(value => value != null))
                    if (player.Stats == null) player.Stats = new StatBlock();
            }
            if (data.SchemaVersion < 2)
            {
                data.SchemaVersion = 2;
            }
        }

        private void EnsureWipeIdentity(string filename)
        {
            string identity;
            if (string.IsNullOrWhiteSpace(filename))
                identity = CurrentWorldIdentity();
            else
                identity = filename;
            data.WorldIdentity = CurrentWorldIdentity();
            if (string.IsNullOrEmpty(data.WipeId)) data.WipeId = identity;
            if (string.IsNullOrEmpty(data.WipeStartedUtc)) data.WipeStartedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            if (!string.IsNullOrWhiteSpace(filename)) data.WipeId = identity;
        }

        private string CurrentWorldIdentity() => Convert.ToString(ConVar.Server.level) + ":" + ConVar.Server.seed + ":" + ConVar.Server.worldsize;

        private void DetectMissedWipe()
        {
            var current = CurrentWorldIdentity();
            if (string.IsNullOrEmpty(data.WorldIdentity)) { data.WorldIdentity = current; return; }
            if (string.Equals(data.WorldIdentity, current, StringComparison.Ordinal)) return;
            if (config.ResetWipeOnNewSave)
            {
                ArchiveCurrentWipe();
                recentPvpKills.Clear();
                foreach (var record in data.Players.Values.Where(value => value != null)) record.Wipe = new StatBlock();
                data.WipeStartedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                Puts("Changed world identity detected during startup; the previous wipe was archived automatically.");
            }
            data.WorldIdentity = current;
            data.WipeId = current;
            SaveData();
        }

        private void ImportLegacyLifetimeStats()
        {
            var imported = 0;
            try
            {
                var directory = Path.Combine(Interface.Oxide.DataFileSystem.Directory, "KDRScoreboard");
                if (Directory.Exists(directory))
                {
                    foreach (var path in Directory.GetFiles(directory, "*.json"))
                    {
                        var source = JsonConvert.DeserializeObject<LegacyKdrData>(File.ReadAllText(path));
                        if (source == null || source.id == 0) continue;
                        var record = EnsurePlayer(source.id, source.name);
                        record.Lifetime.PlayerKills = Math.Max(record.Lifetime.PlayerKills, Math.Max(0, source.kills));
                        record.Lifetime.Deaths = Math.Max(record.Lifetime.Deaths, Math.Max(0, source.deaths));
                        record.Lifetime.PvpDeaths = Math.Max(record.Lifetime.PvpDeaths, Math.Max(0, source.deaths));
                        imported++;
                    }
                }

                if (Interface.Oxide.DataFileSystem.ExistsDatafile("PlaytimeTracker/user_data"))
                {
                    var playtime = Interface.Oxide.DataFileSystem.ReadObject<LegacyPlaytimeData>("PlaytimeTracker/user_data");
                    foreach (var entry in playtime?._userData ?? new Dictionary<string, LegacyPlaytimeUser>())
                    {
                        ulong id;
                        if (!ulong.TryParse(entry.Key, out id) || entry.Value == null) continue;
                        var record = EnsurePlayer(id, entry.Value.displayName);
                        record.Lifetime.SecondsPlayed = Math.Max(record.Lifetime.SecondsPlayed, (long)Math.Max(entry.Value.playtime, entry.Value.PlayTime));
                        imported++;
                    }
                }
                if (imported > 0) { SaveData(); Puts($"Imported cumulative legacy lifetime statistics for {imported} source records."); }
            }
            catch (Exception ex) { PrintWarning("Legacy lifetime statistics import failed: " + ex.Message); }
        }

        private void ArchiveCurrentWipe()
        {
            EnsureData();
            if (string.IsNullOrEmpty(data.WipeId) || data.PreviousWipes.Any(wipe => wipe != null && wipe.WipeId == data.WipeId)) return;
            var archive = new WipeArchive
            {
                WipeId = data.WipeId,
                StartedUtc = data.WipeStartedUtc,
                EndedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
            foreach (var record in data.Players.Values.Where(value => value != null))
            {
                archive.Players[record.Id] = new ArchivedPlayer
                {
                    Id = record.Id,
                    Name = record.Name,
                    Clan = !string.IsNullOrWhiteSpace(record.CurrentClan) ? record.CurrentClan : GetClanTag(record.Id),
                    Stats = JObject.FromObject(record.Wipe ?? new StatBlock()).ToObject<StatBlock>()
                };
            }
            data.PreviousWipes.Insert(0, archive);
            var retained = Mathf.Clamp(config.PreviousWipesRetained, 1, 50);
            if (data.PreviousWipes.Count > retained) data.PreviousWipes.RemoveRange(retained, data.PreviousWipes.Count - retained);
        }

        private void CycleScope(UiState state)
        {
            if (!state.Lifetime && state.ArchiveIndex < 0)
            {
                if (data.PreviousWipes.Count > 0) state.ArchiveIndex = 0;
                else state.Lifetime = true;
            }
            else if (!state.Lifetime && state.ArchiveIndex + 1 < data.PreviousWipes.Count) state.ArchiveIndex++;
            else if (!state.Lifetime) { state.ArchiveIndex = -1; state.Lifetime = true; }
            else { state.Lifetime = false; state.ArchiveIndex = -1; }
            state.PlayerPage = 0;
            state.ClanPage = 0;
        }

        private string ScopeLabel(UiState state)
        {
            if (state.Lifetime) return "LIFETIME";
            if (state.ArchiveIndex < 0 || state.ArchiveIndex >= data.PreviousWipes.Count) return "THIS WIPE";
            var wipe = data.PreviousWipes[state.ArchiveIndex];
            if (wipe == null) return "PREVIOUS WIPE";
            DateTime ended;
            return DateTime.TryParse(wipe.EndedUtc, out ended) ? "WIPE " + ended.ToString("dd MMM yy") : "PREVIOUS WIPE " + (state.ArchiveIndex + 1);
        }

        private StatBlock StatsFor(ulong id, UiState state)
        {
            var archive = ArchiveFor(state);
            if (archive != null)
            {
                ArchivedPlayer archived;
                return archive.Players.TryGetValue(id, out archived) && archived != null ? archived.Stats ?? new StatBlock() : new StatBlock();
            }
            PlayerRecord record;
            if (!data.Players.TryGetValue(id, out record) || record == null) return new StatBlock();
            return state.Lifetime ? record.Lifetime : record.Wipe;
        }

        private WipeArchive ArchiveFor(UiState state)
        {
            if (state == null || state.ArchiveIndex < 0 || state.ArchiveIndex >= data.PreviousWipes.Count) return null;
            var archive = data.PreviousWipes[state.ArchiveIndex];
            return archive != null && archive.Players != null ? archive : null;
        }

        private void QueueSaveData()
        {
            if (pendingSaveTimer != null) return;
            pendingSaveTimer = timer.Once(10f, SaveData);
        }

        private void SaveData()
        {
            if (pendingSaveTimer != null) pendingSaveTimer.Destroy();
            pendingSaveTimer = null;
            Interface.Oxide.DataFileSystem.WriteObject(Name, data);
        }
        private bool CanUse(BasePlayer player) => player != null && (!config.RequireUsePermission || player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermUse) || permission.UserHasPermission(player.UserIDString, PermAdmin));
        private UiState GetState(BasePlayer player) { if (!uiStates.TryGetValue(player.userID, out var state)) uiStates[player.userID] = state = new UiState(); return state; }

        private PlayerRecord FindRecord(string query)
        {
            if (ulong.TryParse(query, out var id) && data.Players.TryGetValue(id, out var exact)) return exact;
            return data.Players.Values.Where(value => value != null && !string.IsNullOrEmpty(value.Name)).OrderBy(value => value.Name.Length).FirstOrDefault(value => value.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private IEnumerable<PlayerRecord> RankedPlayers(string metric, bool lifetime) => data.Players.Values.Where(value => value != null && !IsExcluded(value.Id) && IsRanked(lifetime ? value.Lifetime : value.Wipe) && IsMetricEligible(metric, lifetime ? value.Lifetime : value.Wipe)).OrderByDescending(value => Metric(metric, lifetime ? value.Lifetime : value.Wipe)).ThenByDescending(value => (lifetime ? value.Lifetime : value.Wipe).PlayerKills).ThenBy(value => value.Name);
        private IEnumerable<PlayerRecord> RankedPlayers(string metric, UiState state)
        {
            var archive = ArchiveFor(state);
            return data.Players.Values.Where(value => value != null && !IsExcluded(value.Id) && (archive == null || archive.Players.ContainsKey(value.Id)) && IsRanked(StatsFor(value.Id, state)) && IsMetricEligible(metric, StatsFor(value.Id, state))).OrderByDescending(value => Metric(metric, StatsFor(value.Id, state))).ThenByDescending(value => StatsFor(value.Id, state).PlayerKills).ThenBy(value => PlayerNameFor(value.Id, state));
        }
        private int PlayerRank(ulong id, string metric, bool lifetime) { var index = RankedPlayers(metric, lifetime).Select(value => value.Id).ToList().IndexOf(id); return index < 0 ? 0 : index + 1; }
        private int PlayerRank(ulong id, string metric, UiState state) { var index = RankedPlayers(metric, state).Select(value => value.Id).ToList().IndexOf(id); return index < 0 ? 0 : index + 1; }
        private string MetricForTab(string tab) => tab == "combat" ? "kills" : tab == "raiding" ? "raiddamage" : tab == "activity" ? "playtime" : "kdr";
        private double Metric(string metric, StatBlock block) => metric == "kills" ? block.PlayerKills : metric == "deaths" ? block.PvpDeaths : metric == "headshots" ? HeadshotRate(block) : metric == "streak" ? block.BestKillStreak : metric == "playtime" ? block.SecondsPlayed : metric == "raiddamage" ? block.RaidDamage : metric == "kdr" ? Kdr(block) : block.PlayerKills;
        private string FormatMetric(string metric, StatBlock block) => metric == "kills" ? block.PlayerKills.ToString("N0") + " KILLS" : metric == "deaths" ? block.PvpDeaths.ToString("N0") + " PVP DEATHS" : metric == "headshots" ? HeadshotRate(block).ToString("0") + "% HEADSHOTS" : metric == "streak" ? block.BestKillStreak.ToString("N0") + " STREAK" : metric == "playtime" ? FormatDuration(block.SecondsPlayed) : metric == "raiddamage" ? Compact((long)block.RaidDamage) + " RAID DAMAGE" : Kdr(block).ToString("0.00") + " K/D";
        private string MetricLabel(string metric) => metric == "kdr" ? "K/D" : metric == "headshots" ? "HEADSHOTS" : metric == "streak" ? "BEST STREAK" : metric == "playtime" ? "PLAYTIME" : metric == "deaths" ? "DEATHS" : metric == "raiddamage" ? "RAID DAMAGE" : "KILLS";
        private bool IsRanked(StatBlock block) => block != null && (!config.HideInactivePlayers || block.PlayerKills > 0 || block.PvpDeaths > 0 || block.SecondsPlayed >= config.MinimumRankedPlaytimeSeconds);
        private bool IsMetricEligible(string metric, StatBlock block) => (metric != "kdr" && metric != "headshots") || block.PlayerKills >= config.MinimumKillsForKdrRanking;
        private bool IsExcluded(ulong playerId) => permission.UserHasPermission(playerId.ToString(), PermExclude);
        private double Kdr(StatBlock block) => block.PvpDeaths <= 0 ? block.PlayerKills : (double)block.PlayerKills / block.PvpDeaths;
        private double HeadshotRate(StatBlock block) => block.PlayerKills <= 0 ? 0d : (double)block.Headshots * 100d / block.PlayerKills;

        private string ClanFor(ulong playerId, UiState state)
        {
            var archive = ArchiveFor(state);
            if (archive != null)
            {
                ArchivedPlayer player;
                return archive.Players.TryGetValue(playerId, out player) && player != null ? player.Clan : null;
            }
            var tag = GetClanTag(playerId);
            PlayerRecord record;
            return !string.IsNullOrEmpty(tag) ? tag : data.Players.TryGetValue(playerId, out record) && record != null ? record.CurrentClan : null;
        }

        private string PlayerNameFor(ulong playerId, UiState state)
        {
            var archive = ArchiveFor(state);
            ArchivedPlayer archived;
            if (archive != null && archive.Players.TryGetValue(playerId, out archived) && archived != null && !string.IsNullOrWhiteSpace(archived.Name)) return archived.Name;
            PlayerRecord record;
            return data.Players.TryGetValue(playerId, out record) && record != null ? record.Name : playerId.ToString();
        }

        private List<ClanStanding> BuildClanStandings(UiState state)
        {
            var archive = ArchiveFor(state);
            if (archive == null) return BuildClanStandings(state.Lifetime);
            var standings = new Dictionary<string, ClanStanding>(StringComparer.OrdinalIgnoreCase);
            foreach (var player in archive.Players.Values.Where(value => value != null && !string.IsNullOrWhiteSpace(value.Clan) && !IsExcluded(value.Id)))
            {
                ClanStanding clan;
                if (!standings.TryGetValue(player.Clan, out clan)) standings[player.Clan] = clan = new ClanStanding { Tag = player.Clan };
                clan.Members++;
                AddStats(clan.Stats, player.Stats);
            }
            return RankClans(standings.Values);
        }

        private List<ClanStanding> BuildClanStandings(bool lifetime)
        {
            var standings = new Dictionary<string, ClanStanding>(StringComparer.OrdinalIgnoreCase);
            var snapshotLoaded = false;
            if (Clans != null && Clans.IsLoaded)
            {
                try
                {
                    var snapshot = Clans.Call("RaidlandsClanSnapshot") as JArray;
                    if (snapshot != null)
                    {
                        snapshotLoaded = true;
                        foreach (var clanToken in snapshot.OfType<JObject>())
                        {
                            var tag = clanToken.Value<string>("tag");
                            if (string.IsNullOrWhiteSpace(tag)) continue;
                            var clan = new ClanStanding { Tag = tag };
                            standings[tag] = clan;
                            var members = clanToken["members"] as JArray;
                            if (members == null) continue;
                            foreach (var member in members.OfType<JObject>())
                            {
                                if (!ulong.TryParse(member.Value<string>("steam_id64"), out var memberId)) continue;
                                var record = EnsurePlayer(memberId, member.Value<string>("display_name"));
                                record.CurrentClan = tag;
                                clan.Members++;
                                if (!IsExcluded(memberId)) AddStats(clan.Stats, lifetime ? record.Lifetime : record.Wipe);
                            }
                        }
                    }
                }
                catch (Exception ex) { PrintWarning("Could not read Raidlands clan snapshot: " + ex.Message); }
            }

            if (!snapshotLoaded)
            {
                foreach (var record in data.Players.Values.Where(value => value != null))
                {
                    var tag = GetClanTag(record.Id);
                    if (string.IsNullOrEmpty(tag)) continue;
                    if (!standings.TryGetValue(tag, out var clan)) standings[tag] = clan = new ClanStanding { Tag = tag };
                    clan.Members++;
                    if (!IsExcluded(record.Id)) AddStats(clan.Stats, lifetime ? record.Lifetime : record.Wipe);
                }
            }
            return RankClans(standings.Values);
        }

        private List<ClanStanding> RankClans(IEnumerable<ClanStanding> clans)
        {
            return clans.Where(value => value.Members >= config.MinimumClanMembers && (!config.HideInactivePlayers || IsRanked(value.Stats) || value.Stats.RaidDamage > 0d))
                .OrderByDescending(value => value.Stats.PlayerKills)
                .ThenByDescending(value => Kdr(value.Stats))
                .ThenByDescending(value => value.Stats.SecondsPlayed)
                .ThenBy(value => value.Tag).ToList();
        }

        private string GetClanTag(ulong playerId)
        {
            if (Clans == null || !Clans.IsLoaded) return null;
            try { return Clans.Call<string>("GetClanOf", playerId); } catch { return Clans.Call("GetClanOf", playerId.ToString()) as string; }
        }

        private void AddStats(StatBlock target, StatBlock source)
        {
            if (target == null || source == null) return;
            target.SecondsPlayed += source.SecondsPlayed; target.Connections += source.Connections; target.PlayerKills += source.PlayerKills; target.NpcKills += source.NpcKills; target.AnimalKills += source.AnimalKills;
            target.Deaths += source.Deaths; target.PvpDeaths += source.PvpDeaths; target.Suicides += source.Suicides; target.Headshots += source.Headshots; target.MeleeKills += source.MeleeKills; target.RangedKills += source.RangedKills; target.ExplosiveKills += source.ExplosiveKills;
            target.PlayerDamage += source.PlayerDamage; target.NpcDamage += source.NpcDamage; target.RaidDamage += source.RaidDamage;
            target.ItemsCrafted += source.ItemsCrafted; target.StructuresBuilt += source.StructuresBuilt; target.EntitiesDestroyed += source.EntitiesDestroyed; target.ExplosivesThrown += source.ExplosivesThrown; target.ShotsFired += source.ShotsFired;
            target.RocketsFired += source.RocketsFired; target.HealingUsed += source.HealingUsed; target.DistanceTravelled += source.DistanceTravelled; target.LongestKillMetres = Math.Max(target.LongestKillMetres, source.LongestKillMetres); target.BestKillStreak = Math.Max(target.BestKillStreak, source.BestKillStreak);
        }

        private string WeaponName(HitInfo info, BasePlayer attacker)
        {
            var name = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
            if (string.IsNullOrEmpty(name) && attacker != null) lastWeapons.TryGetValue(attacker.userID, out name);
            return name ?? string.Empty;
        }

        private bool IsExplosiveWeapon(string value) => value.IndexOf("rocket", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("explosive", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("grenade", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("satchel", StringComparison.OrdinalIgnoreCase) >= 0;
        private bool IsMeleeWeapon(string value) => new[] { "knife", "sword", "machete", "spear", "rock", "salvaged.cleaver", "bone.club", "longsword" }.Any(item => value.IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0);
        private string Compact(long value) => value >= 1000000 ? (value / 1000000d).ToString("0.0") + "M" : value >= 1000 ? (value / 1000d).ToString("0.0") + "K" : value.ToString("N0");
        private string FormatDuration(long seconds) { var span = TimeSpan.FromSeconds(Math.Max(0, seconds)); return span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h" : $"{(int)span.TotalHours}h {span.Minutes}m"; }
        private string Short(string value, int max) { value = string.IsNullOrEmpty(value) ? "Unknown" : value; return value.Length <= max ? value : value.Substring(0, Math.Max(1, max - 3)) + "..."; }

        private object GetPlayerStats(ulong playerId, bool lifetime = false)
        {
            if (!data.Players.TryGetValue(playerId, out var record)) return null;
            return JObject.FromObject(new { playerId, record.Name, clan = GetClanTag(playerId), scope = lifetime ? "lifetime" : "wipe", stats = lifetime ? record.Lifetime : record.Wipe });
        }

        private object GetLeaderboard(string metric = "kills", bool lifetime = false, int limit = 100)
        {
            return JArray.FromObject(RankedPlayers(metric, lifetime).Take(Mathf.Clamp(limit, 1, 500)).Select((record, index) => new { rank = index + 1, playerId = record.Id, record.Name, clan = GetClanTag(record.Id), value = Metric(metric, lifetime ? record.Lifetime : record.Wipe), stats = lifetime ? record.Lifetime : record.Wipe }));
        }

        private object GetClanLeaderboard(bool lifetime = false, int limit = 100)
        {
            return JArray.FromObject(BuildClanStandings(lifetime).Take(Mathf.Clamp(limit, 1, 500)).Select((clan, index) => new { rank = index + 1, clan.Tag, clan.Members, clan.Stats }));
        }

        private object GetPreviousWipes()
        {
            return JArray.FromObject(data.PreviousWipes.Select((wipe, index) => new { wipe, index }).Where(entry => entry.wipe != null).Select(entry => new { entry.index, entry.wipe.WipeId, entry.wipe.StartedUtc, entry.wipe.EndedUtc, players = entry.wipe.Players == null ? 0 : entry.wipe.Players.Count }));
        }

        private object GetPreviousWipeLeaderboard(int archiveIndex, string metric = "kills", int limit = 100)
        {
            if (archiveIndex < 0 || archiveIndex >= data.PreviousWipes.Count) return null;
            var state = new UiState { ArchiveIndex = archiveIndex };
            if (ArchiveFor(state) == null) return null;
            return JArray.FromObject(RankedPlayers(metric, state).Take(Mathf.Clamp(limit, 1, 500)).Select((record, index) => new { rank = index + 1, playerId = record.Id, record.Name, clan = ClanFor(record.Id, state), value = Metric(metric, StatsFor(record.Id, state)), stats = StatsFor(record.Id, state) }));
        }

        private void Close(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiRoot);
            RaidlandsUiEscapeBridge?.Call("UnregisterUi", player, this, UiRoot);
        }

        private void OnUiBridgeClosed(BasePlayer player, string reason) { if (player != null) CuiHelper.DestroyUi(player, UiRoot); }

        private void AddPanel(CuiElementContainer c, string name, string parent, string color, string min, string max, bool cursor = false)
        {
            c.Add(new CuiPanel { Image = { Color = color }, RectTransform = { AnchorMin = min, AnchorMax = max }, CursorEnabled = cursor }, parent, name);
        }

        private void AddLabel(CuiElementContainer c, string name, string parent, string text, int size, string min, string max, TextAnchor align, string color)
        {
            c.Add(new CuiLabel { Text = { Text = text, FontSize = size, Align = align, Color = color }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent, name);
        }

        private void AddButton(CuiElementContainer c, string name, string parent, string text, string command, string color, string min, string max, int size, TextAnchor align = TextAnchor.MiddleCenter)
        {
            c.Add(new CuiButton { Button = { Command = command, Color = color }, Text = { Text = text, FontSize = size, Align = align, Color = Text }, RectTransform = { AnchorMin = min, AnchorMax = max } }, parent, name);
        }
    }
}
