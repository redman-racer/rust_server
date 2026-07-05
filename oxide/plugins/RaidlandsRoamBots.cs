using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using UnityEngine.AI;

namespace Oxide.Plugins
{
    [Info("RaidlandsRoamBots", "Raidlands", "0.1.19")]
    [Description("Spawns player-like roaming NPCs with Raidlands kits, separate NPC stats, and admin controls.")]
    public class RaidlandsRoamBots : RustPlugin
    {
        private const string AdminPermission = "raidlandsroambots.admin";
        private const string SpawnModeNearPlayers = "near_players";
        private const string SpawnModeRandom = "random";
        private const string StatsDataFile = "RaidlandsRoamBots/stats";
        private const string KitsDataFile = "Kits/kits_data";
        private const string ScoreboardNpcKills = "NPC Kills";
        private const string ScoreboardDeathsByNpc = "Killed by NPCs";
        private const string ScoreboardBotKd = "Bot K/D";

        [PluginReference]
        private Plugin Kits;

        [PluginReference]
        private Plugin Scoreboards;

        private Configuration config;
        private StoredData data;
        private readonly System.Random random = new System.Random();
        private readonly Dictionary<BasePlayer, BotRuntime> activeBots = new Dictionary<BasePlayer, BotRuntime>();
        private readonly HashSet<BasePlayer> despawningBots = new HashSet<BasePlayer>();
        private readonly Dictionary<string, KitEligibility> eligibleKits = new Dictionary<string, KitEligibility>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SpawnGroup> npcSpawnGroups = new List<SpawnGroup>();
        private int nativeBasePlayerPrefabGroups;
        private Timer maintainTimer;
        private Timer scoreboardTimer;
        private Timer saveTimer;
        private int teamSequence;
        private float spawnRetryBlockedUntil;

        private class Configuration
        {
            public bool Enabled = false;

            [JsonProperty("Target Population")]
            public int TargetPopulation = 15;

            [JsonProperty("Minimum Allowed Population")]
            public int MinAllowedPopulation = 10;

            [JsonProperty("Maximum Allowed Population")]
            public int MaxAllowedPopulation = 25;

            [JsonProperty("Team Size Weights")]
            public Dictionary<string, int> TeamSizeWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["solo"] = 70,
                ["duo"] = 25,
                ["trio"] = 5
            };

            [JsonProperty("Skill Weights")]
            public Dictionary<string, int> SkillWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["casual"] = 25,
                ["average"] = 60,
                ["dangerous"] = 15
            };

            [JsonProperty("Skill Definitions")]
            public Dictionary<string, SkillDefinition> SkillDefinitions = new Dictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["casual"] = new SkillDefinition { Health = 125f, DamageScale = 0.78f, IncomingDamageScale = 1.15f, LeashDistance = 95f, LeashSeconds = 35f },
                ["average"] = new SkillDefinition { Health = 150f, DamageScale = 1f, IncomingDamageScale = 1f, LeashDistance = 120f, LeashSeconds = 45f },
                ["dangerous"] = new SkillDefinition { Health = 190f, DamageScale = 1.22f, IncomingDamageScale = 0.9f, LeashDistance = 150f, LeashSeconds = 55f }
            };

            [JsonProperty("High Tier Kit Weight")]
            public int HighTierKitWeight = 5;

            [JsonProperty("Kit Selection")]
            public KitSelectionConfig Kits = new KitSelectionConfig();

            [JsonProperty("Spawn Settings")]
            public SpawnConfig Spawn = new SpawnConfig();

            [JsonProperty("Prefab Candidates In Order")]
            public List<string> PrefabCandidates = new List<string>
            {
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/gen2/scientist2.prefab"
            };

            [JsonProperty("Use Gen2 Scientist Fallback")]
            public bool UseGen2ScientistFallback = false;

            [JsonProperty("Require NPC Navigator Placement")]
            public bool RequireNpcNavigatorPlacement = true;

            [JsonProperty("Debug Spawn Details")]
            public bool DebugSpawnDetails = false;

            [JsonProperty("Spawn Failure Retry Seconds")]
            public float SpawnFailureRetrySeconds = 120f;

            [JsonProperty("Bot Profiles")]
            public List<string> BotProfiles = new List<string>
            {
                "LaunchLoot",
                "BlueCarded",
                "ColdFurnace",
                "RoadsignMain",
                "HarborEcho",
                "StoneCeiling",
                "OxideSeven",
                "NorthOil",
                "DepotMains",
                "TwigLaw",
                "CratePilot",
                "FuseBox",
                "RoofMuted",
                "BanditQueue",
                "CargoLeft",
                "RedKeycard",
                "BenchTier3",
                "WipeClock",
                "RecyclerOne",
                "OilDock",
                "HazzyStep",
                "GarageDoor",
                "MonumentRun",
                "MetalNode",
                "WorkbenchTwo",
                "TrainYard",
                "BradleyLane",
                "OutpostEdge",
                "LaunchStairs",
                "FurnaceBase"
            };

            [JsonProperty("Maintain Interval Seconds")]
            public float MaintainIntervalSeconds = 20f;

            [JsonProperty("Scoreboard Interval Seconds")]
            public float ScoreboardIntervalSeconds = 60f;

            [JsonProperty("Respawn Delay Seconds")]
            public float RespawnDelaySeconds = 20f;
        }

        private class KitSelectionConfig
        {
            [JsonProperty("Default Group")]
            public string DefaultGroup = "default";

            [JsonProperty("Eligible Kit Names")]
            public List<string> EligibleKitNames = new List<string> { "ak", "lr300", "m16", "mp5" };

            [JsonProperty("Rare High Tier Kit Names")]
            public List<string> RareHighTierKitNames = new List<string> { "raid" };

            [JsonProperty("Weapon Shortnames")]
            public List<string> WeaponShortnames = new List<string>
            {
                "rifle.ak",
                "rifle.lr300",
                "m16a2",
                "smg.mp5",
                "lmg.m249",
                "rifle.m39",
                "rifle.semiauto",
                "rifle.bolt",
                "rifle.l96",
                "smg.thompson",
                "smg.2",
                "pistol.python",
                "pistol.semiauto",
                "pistol.m92"
            };
        }

        private class SpawnConfig
        {
            [JsonProperty("Spawn Mode")]
            public string SpawnMode = SpawnModeNearPlayers;

            [JsonProperty("Prefer Native NPC Spawn Groups")]
            public bool PreferNativeNpcSpawnGroups = false;

            [JsonProperty("Only Use Native BasePlayer Spawn Groups")]
            public bool OnlyUseNativeBasePlayerSpawnGroups = true;

            [JsonProperty("Prefer Native Spawn Group Prefabs")]
            public bool PreferNativeSpawnGroupPrefabs = false;

            [JsonProperty("Use Random Land Fallback")]
            public bool UseRandomLandFallback = false;

            [JsonProperty("Native NPC Spawn Group Attempts")]
            public int NativeNpcSpawnGroupAttempts = 40;

            [JsonProperty("Max Position Attempts")]
            public int MaxPositionAttempts = 80;

            [JsonProperty("Navmesh Sample Distance")]
            public float NavmeshSampleDistance = 12f;

            [JsonProperty("Minimum Above Water")]
            public float MinimumAboveWater = 1.5f;

            [JsonProperty("Require Land Spawns")]
            public bool RequireLandSpawns = true;

            [JsonProperty("Minimum Land Height")]
            public float MinimumLandHeight = 0f;

            [JsonProperty("Group Spawn Radius")]
            public float GroupSpawnRadius = 12f;

            [JsonProperty("Prefer Native NPC Spawn Points Near Players")]
            public bool PreferNativeNpcSpawnPointsNearPlayers = true;

            [JsonProperty("Use Generated Positions Near Players")]
            public bool UseGeneratedPositionsNearPlayers = false;

            [JsonProperty("Near Player Minimum Distance")]
            public float NearPlayerMinDistance = 80f;

            [JsonProperty("Near Player Maximum Distance")]
            public float NearPlayerMaxDistance = 300f;

            [JsonProperty("Near Player Native Spawn Point Attempts")]
            public int NearPlayerNativeSpawnPointAttempts = 300;

            [JsonProperty("Near Player Attempts Per Bot")]
            public int NearPlayerAttempts = 48;

            [JsonProperty("Near Player Anchor Name Or SteamID")]
            public string NearPlayerAnchorNameOrSteamId = "";

            [JsonProperty("Avoid Safe Zone Spawns")]
            public bool AvoidSafeZoneSpawns = true;

            [JsonProperty("Ignore Players In Safe Zones")]
            public bool IgnorePlayersInSafeZones = true;

            [JsonProperty("Safe Zone Spawn Buffer Distance")]
            public float SafeZoneSpawnBufferDistance = 75f;
        }

        private class SkillDefinition
        {
            public float Health = 150f;
            public float DamageScale = 1f;
            public float IncomingDamageScale = 1f;
            public float LeashDistance = 120f;
            public float LeashSeconds = 45f;
        }

        private class StoredData
        {
            public Dictionary<string, PlayerNpcStats> players = new Dictionary<string, PlayerNpcStats>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, BotStats> bots = new Dictionary<string, BotStats>(StringComparer.OrdinalIgnoreCase);
        }

        private class PlayerNpcStats
        {
            public string steam_id64 = "";
            public string display_name = "";
            public int npc_kills;
            public int deaths_by_npc;
        }

        private class BotStats
        {
            public string bot_key = "";
            public string display_name = "";
            public string kit_name = "";
            public string skill_tier = "";
            public int kills;
            public int deaths;
        }

        private class BotRuntime
        {
            public string BotKey;
            public string DisplayName;
            public string KitName;
            public string SkillTier;
            public SkillDefinition Skill;
            public int TeamId;
            public Vector3 SpawnPosition;
            public Vector3 HomePosition;
            public float LastCombatAt;
            public BasePlayer LastEnemy;
        }

        private class SpawnCandidate
        {
            public Vector3 Position;
            public string PreferredPrefab;
        }

        private class KitEligibility
        {
            public string Name;
            public string RequiredPermission;
            public bool HighTier;
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>() ?? new Configuration();
            }
            catch (Exception ex)
            {
                PrintWarning($"Configuration was invalid; writing defaults. {ex.Message}");
                config = new Configuration();
            }

            NormalizeConfig();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            RefreshEligibleKits();
            RefreshNpcSpawnGroups();
            CreateScoreboards();
            UpdateScoreboards();

            if (!config.Enabled)
            {
                Puts("Raidlands roam bots are disabled. Config and data are ready; no bots will spawn until raidbots.enable is run.");
                return;
            }

            StartRuntime();
        }

        private void Unload()
        {
            StopRuntime();
            KillAllBots(false);
            SaveData();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin?.Name == "Scoreboards")
            {
                timer.Once(2f, () =>
                {
                    CreateScoreboards();
                    UpdateScoreboards();
                });
            }

            if (plugin?.Name == "Kits")
            {
                timer.Once(2f, RefreshEligibleKits);
            }
        }

        private void StartRuntime()
        {
            StopRuntime();
            spawnRetryBlockedUntil = 0f;
            maintainTimer = timer.Every(Math.Max(5f, config.MaintainIntervalSeconds), MaintainPopulation);
            scoreboardTimer = timer.Every(Math.Max(15f, config.ScoreboardIntervalSeconds), UpdateScoreboards);
            MaintainPopulation();
            Puts($"Raidlands roam bots enabled. Target population: {TargetPopulation()}.");
        }

        private void StopRuntime()
        {
            maintainTimer?.Destroy();
            maintainTimer = null;
            scoreboardTimer?.Destroy();
            scoreboardTimer = null;
        }

        private void NormalizeConfig()
        {
            var defaults = new Configuration();
            config.MinAllowedPopulation = Math.Max(0, config.MinAllowedPopulation);
            config.MaxAllowedPopulation = Math.Max(config.MinAllowedPopulation, config.MaxAllowedPopulation);
            config.TargetPopulation = Clamp(config.TargetPopulation, config.MinAllowedPopulation, config.MaxAllowedPopulation);
            config.HighTierKitWeight = Clamp(config.HighTierKitWeight, 0, 100);

            if (config.TeamSizeWeights == null || config.TeamSizeWeights.Count == 0)
            {
                config.TeamSizeWeights = defaults.TeamSizeWeights;
            }

            if (config.SkillWeights == null || config.SkillWeights.Count == 0)
            {
                config.SkillWeights = defaults.SkillWeights;
            }

            if (config.SkillDefinitions == null)
            {
                config.SkillDefinitions = defaults.SkillDefinitions;
            }
            else
            {
                foreach (var entry in defaults.SkillDefinitions)
                {
                    if (!config.SkillDefinitions.ContainsKey(entry.Key))
                    {
                        config.SkillDefinitions[entry.Key] = entry.Value;
                    }
                }
            }

            if (config.Kits == null)
            {
                config.Kits = defaults.Kits;
            }

            if (config.Kits.EligibleKitNames == null || config.Kits.EligibleKitNames.Count == 0)
            {
                config.Kits.EligibleKitNames = defaults.Kits.EligibleKitNames;
            }

            if (config.Kits.RareHighTierKitNames == null)
            {
                config.Kits.RareHighTierKitNames = defaults.Kits.RareHighTierKitNames;
            }

            if (config.Kits.WeaponShortnames == null || config.Kits.WeaponShortnames.Count == 0)
            {
                config.Kits.WeaponShortnames = defaults.Kits.WeaponShortnames;
            }

            if (string.IsNullOrWhiteSpace(config.Kits.DefaultGroup))
            {
                config.Kits.DefaultGroup = defaults.Kits.DefaultGroup;
            }

            if (config.Spawn == null)
            {
                config.Spawn = defaults.Spawn;
            }

            config.Spawn.SpawnMode = NormalizeSpawnMode(config.Spawn.SpawnMode);
            config.Spawn.MaxPositionAttempts = Math.Max(10, config.Spawn.MaxPositionAttempts);
            config.Spawn.NavmeshSampleDistance = Math.Max(2f, config.Spawn.NavmeshSampleDistance);
            config.Spawn.MinimumAboveWater = Math.Max(0f, config.Spawn.MinimumAboveWater);
            config.Spawn.MinimumLandHeight = Math.Max(-100f, config.Spawn.MinimumLandHeight);
            config.Spawn.GroupSpawnRadius = Math.Max(1f, config.Spawn.GroupSpawnRadius);
            config.Spawn.NativeNpcSpawnGroupAttempts = Math.Max(1, config.Spawn.NativeNpcSpawnGroupAttempts);
            config.Spawn.NearPlayerMinDistance = Math.Max(25f, config.Spawn.NearPlayerMinDistance);
            config.Spawn.NearPlayerMaxDistance = Math.Max(config.Spawn.NearPlayerMinDistance + 10f, config.Spawn.NearPlayerMaxDistance);
            config.Spawn.NearPlayerNativeSpawnPointAttempts = Math.Max(8, config.Spawn.NearPlayerNativeSpawnPointAttempts);
            config.Spawn.NearPlayerAttempts = Math.Max(8, config.Spawn.NearPlayerAttempts);
            config.Spawn.NearPlayerAnchorNameOrSteamId = (config.Spawn.NearPlayerAnchorNameOrSteamId ?? "").Trim();
            config.Spawn.SafeZoneSpawnBufferDistance = Math.Max(0f, config.Spawn.SafeZoneSpawnBufferDistance);

            if (config.PrefabCandidates == null || config.PrefabCandidates.Count == 0)
            {
                config.PrefabCandidates = defaults.PrefabCandidates;
            }

            config.PrefabCandidates = config.PrefabCandidates
                .Where(prefab => !string.IsNullOrWhiteSpace(prefab))
                .Select(prefab => prefab.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            config.SpawnFailureRetrySeconds = Math.Max(15f, config.SpawnFailureRetrySeconds);

            if (config.BotProfiles == null || config.BotProfiles.Count == 0)
            {
                config.BotProfiles = defaults.BotProfiles;
            }

            config.MaintainIntervalSeconds = Math.Max(5f, config.MaintainIntervalSeconds);
            config.ScoreboardIntervalSeconds = Math.Max(15f, config.ScoreboardIntervalSeconds);
            config.RespawnDelaySeconds = Math.Max(5f, config.RespawnDelaySeconds);
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(StatsDataFile) ?? new StoredData();
            }
            catch
            {
                data = new StoredData();
            }

            if (data.players == null)
            {
                data.players = new Dictionary<string, PlayerNpcStats>(StringComparer.OrdinalIgnoreCase);
            }

            if (data.bots == null)
            {
                data.bots = new Dictionary<string, BotStats>(StringComparer.OrdinalIgnoreCase);
            }

            SaveData();
        }

        private void SaveData()
        {
            saveTimer?.Destroy();
            saveTimer = null;
            Interface.Oxide.DataFileSystem.WriteObject(StatsDataFile, data);
        }

        private void QueueSaveData()
        {
            saveTimer?.Destroy();
            saveTimer = timer.Once(5f, SaveData);
        }

        private void MaintainPopulation()
        {
            CleanupInactiveBots();
            HandleLeashes();

            if (!config.Enabled)
            {
                return;
            }

            RefreshEligibleKits();

            var target = TargetPopulation();

            if (TrimExcessBots(target, "target cap") > 0)
            {
                return;
            }

            if (NormalizeSpawnMode(config.Spawn.SpawnMode) == SpawnModeNearPlayers && !config.Spawn.UseRandomLandFallback && SpawnAnchorPlayers().Count == 0)
            {
                return;
            }

            var missing = target - activeBots.Count;

            if (missing <= 0)
            {
                return;
            }

            if (Time.realtimeSinceStartup < spawnRetryBlockedUntil)
            {
                return;
            }

            var spawned = SpawnBots(missing, false);

            if (spawned <= 0)
            {
                spawnRetryBlockedUntil = Time.realtimeSinceStartup + config.SpawnFailureRetrySeconds;
                PrintWarning($"Roam bot spawning is paused for {config.SpawnFailureRetrySeconds:0} seconds because no configured prefab could be placed on navmesh.");
            }
        }

        private void CleanupInactiveBots()
        {
            foreach (var entry in activeBots.ToList())
            {
                if (!IsLiveBot(entry.Key))
                {
                    activeBots.Remove(entry.Key);
                    despawningBots.Remove(entry.Key);
                }
            }
        }

        private int TrimExcessBots(int target, string reason)
        {
            target = Math.Max(0, target);
            var excess = activeBots.Count - target;

            if (excess <= 0)
            {
                return 0;
            }

            var bots = activeBots.Keys
                .Where(IsLiveBot)
                .Take(excess)
                .ToList();

            foreach (var bot in bots)
            {
                DespawnBot(bot, "");
            }

            if (bots.Count > 0)
            {
                Puts($"Trimmed {bots.Count} roam bot{(bots.Count == 1 ? "" : "s")} above target population {target} after {reason}.");
            }

            return bots.Count;
        }

        private void HandleLeashes()
        {
            var now = Time.realtimeSinceStartup;

            foreach (var entry in activeBots.ToList())
            {
                var bot = entry.Key;
                var runtime = entry.Value;

                if (!IsLiveBot(bot) || runtime.LastCombatAt <= 0f)
                {
                    continue;
                }

                var combatAge = now - runtime.LastCombatAt;
                var distance = Vector3.Distance(bot.transform.position, runtime.HomePosition);
                var enemyGone = runtime.LastEnemy == null || !runtime.LastEnemy.IsConnected || runtime.LastEnemy.IsDead() || ShouldIgnoreSafeZonePlayer(runtime.LastEnemy);

                if (combatAge >= runtime.Skill.LeashSeconds || distance >= runtime.Skill.LeashDistance || enemyGone && combatAge >= 15f)
                {
                    DespawnBot(bot, "leash");
                    timer.Once(config.RespawnDelaySeconds, MaintainPopulation);
                }
            }
        }

        private int SpawnBots(int requested, bool manual)
        {
            if (requested <= 0)
            {
                return 0;
            }

            RefreshEligibleKits();

            if (eligibleKits.Count == 0)
            {
                PrintWarning("No eligible default-access weapon kits found for roam bots.");
                return 0;
            }

            var spawned = 0;
            var remaining = requested;

            while (remaining > 0)
            {
                var teamSize = Math.Min(remaining, WeightedTeamSize());
                var teamId = ++teamSequence;

                if (!TryFindSpawnPosition(out var leaderPosition, out var preferredPrefab))
                {
                    PrintWarning("Could not find a valid land/navmesh spawn position for roam bots.");
                    break;
                }

                for (var index = 0; index < teamSize; index++)
                {
                    var position = leaderPosition;

                    if (index > 0)
                    {
                        TryFindNearbyPosition(leaderPosition, out position);
                    }

                    if (TrySpawnBot(position, teamId, preferredPrefab) != null)
                    {
                        spawned++;
                        remaining--;
                    }
                    else
                    {
                        remaining = 0;
                        break;
                    }
                }
            }

            if (manual && spawned > 0)
            {
                Puts($"Manually spawned {spawned} roam bot{(spawned == 1 ? "" : "s")}.");
            }

            return spawned;
        }

        private BasePlayer TrySpawnBot(Vector3 position, int teamId, string preferredPrefab)
        {
            foreach (var prefab in ActivePrefabCandidates(preferredPrefab))
            {
                if (config.DebugSpawnDetails)
                {
                    Puts($"Trying prefab {prefab} at {FormatVector(position)} ({PositionDiagnostics(position)}), require navigator placement={config.RequireNpcNavigatorPlacement}.");
                }

                var entity = GameManager.server.CreateEntity(prefab, position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), true);

                if (entity == null)
                {
                    if (config.DebugSpawnDetails)
                    {
                        Puts($"Prefab {prefab} could not be created at {FormatVector(position)}.");
                    }

                    continue;
                }

                var bot = entity as BasePlayer;

                if (bot == null)
                {
                    if (config.DebugSpawnDetails)
                    {
                        Puts($"Prefab {prefab} created {entity.GetType().Name}, not BasePlayer; rejecting spawn attempt.");
                    }

                    SafeKillSpawnAttempt(entity);
                    continue;
                }

                entity.Spawn();

                if (config.RequireNpcNavigatorPlacement && !TryPlaceBotOnOwnNavmesh(bot, ref position))
                {
                    PrintWarning($"Prefab {prefab} spawned but its navigator could not be placed on navmesh; trying the next candidate.");
                    SafeKillSpawnAttempt(bot);
                    continue;
                }

                if (IsUnderWater(position))
                {
                    PrintWarning($"Prefab {prefab} spawned at {FormatVector(position)}, but that position is water or below the land spawn threshold; trying the next candidate.");
                    SafeKillSpawnAttempt(bot);
                    continue;
                }

                ConfigureBot(bot, position, teamId);

                if (config.DebugSpawnDetails)
                {
                    Puts($"Accepted roam bot {bot.displayName} from prefab {prefab} at {FormatVector(position)} ({PositionDiagnostics(position)}).");
                }

                return bot;
            }

            PrintWarning("None of the configured NPC prefabs could be created.");
            return null;
        }

        private IEnumerable<string> ActivePrefabCandidates(string preferredPrefab = "")
        {
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (CanUseRoamPrefab(preferredPrefab) && yielded.Add(preferredPrefab.Trim()))
            {
                yield return preferredPrefab.Trim();
            }

            foreach (var prefab in config.PrefabCandidates ?? new List<string>())
            {
                if (!CanUseRoamPrefab(prefab))
                {
                    continue;
                }

                if (yielded.Add(prefab.Trim()))
                {
                    yield return prefab.Trim();
                }
            }
        }

        private void SafeKillSpawnAttempt(BaseNetworkable entity)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return;
            }

            try
            {
                entity.Kill(BaseNetworkable.DestroyMode.None);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not clean up a failed roam bot spawn attempt: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void ConfigureBot(BasePlayer bot, Vector3 position, int teamId)
        {
            var skillTier = WeightedKey(config.SkillWeights, "average");
            var skill = SkillFor(skillTier);
            var kit = ChooseKit();
            var displayName = ChooseProfileName();
            var botKey = BotKey(displayName);

            bot.displayName = displayName;
            bot.InitializeHealth(skill.Health, skill.Health);
            bot.health = skill.Health;

            var runtime = new BotRuntime
            {
                BotKey = botKey,
                DisplayName = displayName,
                KitName = kit?.Name ?? "",
                SkillTier = skillTier,
                Skill = skill,
                TeamId = teamId,
                SpawnPosition = position,
                HomePosition = position
            };

            activeBots[bot] = runtime;
            EnsureBotStats(runtime);

            if (kit != null)
            {
                ApplyKit(bot, kit.Name);
            }

            bot.SendNetworkUpdateImmediate();
        }

        private void ApplyKit(BasePlayer bot, string kitName)
        {
            if (Kits == null || string.IsNullOrWhiteSpace(kitName))
            {
                return;
            }

            bot.inventory?.Strip();
            var result = Kits.Call("GiveKit", bot, kitName);

            if (result is string message && !string.IsNullOrWhiteSpace(message))
            {
                PrintWarning($"Kits plugin could not give {kitName} to {bot.displayName}: {message}");
            }
        }

        private bool TryPlaceBotOnOwnNavmesh(BasePlayer bot, ref Vector3 position)
        {
            if (bot == null)
            {
                return false;
            }

            var navigator = bot.GetComponent<BaseNavigator>() ?? bot.GetComponentInChildren<BaseNavigator>();

            if (navigator == null)
            {
                position = bot.transform.position;
                return true;
            }

            Vector3 nearest;

            if (navigator.GetNearestNavmeshPosition(position, out nearest, Math.Max(24f, config.Spawn.NavmeshSampleDistance * 3f)))
            {
                bot.transform.position = nearest;
                position = nearest;
                bot.SendNetworkUpdateImmediate();
            }

            if (navigator.PlaceOnNavMesh(0f))
            {
                position = bot.transform.position;
                return true;
            }

            return false;
        }

        private void DespawnBot(BasePlayer bot, string reason)
        {
            if (bot == null || !activeBots.ContainsKey(bot))
            {
                return;
            }

            despawningBots.Add(bot);
            activeBots.Remove(bot);

            if (!bot.IsDestroyed)
            {
                bot.Kill(BaseNetworkable.DestroyMode.None);
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                Puts($"Despawned roam bot after {reason}.");
            }
        }

        private void KillAllBots(bool dropBodies)
        {
            foreach (var bot in activeBots.Keys.ToList())
            {
                if (bot == null)
                {
                    continue;
                }

                if (!dropBodies)
                {
                    despawningBots.Add(bot);
                }

                if (!bot.IsDestroyed)
                {
                    bot.Kill(BaseNetworkable.DestroyMode.None);
                }
            }

            activeBots.Clear();
            despawningBots.Clear();
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null)
            {
                return null;
            }

            var victim = entity as BasePlayer;
            var attacker = info.Initiator as BasePlayer;

            if (victim == null && attacker == null)
            {
                return null;
            }

            var victimRuntime = victim != null ? RuntimeFor(victim) : null;
            var attackerRuntime = attacker != null ? RuntimeFor(attacker) : null;

            if (victimRuntime != null && attackerRuntime != null)
            {
                return true;
            }

            if (attackerRuntime != null && IsRealPlayer(victim) && ShouldIgnoreSafeZonePlayer(victim))
            {
                return true;
            }

            if (victimRuntime != null && IsRealPlayer(attacker) && ShouldIgnoreSafeZonePlayer(attacker))
            {
                return null;
            }

            var now = Time.realtimeSinceStartup;

            if (victimRuntime != null && IsRealPlayer(attacker))
            {
                info.damageTypes.ScaleAll(victimRuntime.Skill.IncomingDamageScale);
                victimRuntime.LastCombatAt = now;
                victimRuntime.LastEnemy = attacker;
                return null;
            }

            if (attackerRuntime != null && IsRealPlayer(victim))
            {
                info.damageTypes.ScaleAll(attackerRuntime.Skill.DamageScale);
                attackerRuntime.LastCombatAt = now;
                attackerRuntime.LastEnemy = victim;
            }

            return null;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;

            if (victim == null)
            {
                return;
            }

            var victimRuntime = RuntimeFor(victim);
            var attacker = info?.Initiator as BasePlayer;
            var attackerRuntime = attacker != null ? RuntimeFor(attacker) : null;

            if (victimRuntime != null)
            {
                activeBots.Remove(victim);

                if (!despawningBots.Remove(victim))
                {
                    var botStats = EnsureBotStats(victimRuntime);
                    botStats.deaths++;

                    if (IsRealPlayer(attacker) && attackerRuntime == null)
                    {
                        var playerStats = EnsurePlayerStats(attacker);
                        playerStats.npc_kills++;
                    }

                    QueueSaveData();
                    UpdateScoreboards();
                }

                if (config.Enabled)
                {
                    timer.Once(config.RespawnDelaySeconds, MaintainPopulation);
                }

                return;
            }

            if (attackerRuntime != null && IsRealPlayer(victim) && !ShouldIgnoreSafeZonePlayer(victim))
            {
                var playerStats = EnsurePlayerStats(victim);
                playerStats.deaths_by_npc++;
                var botStats = EnsureBotStats(attackerRuntime);
                botStats.kills++;
                QueueSaveData();
                UpdateScoreboards();
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            var bot = entity as BasePlayer;

            if (bot == null)
            {
                return;
            }

            activeBots.Remove(bot);
            despawningBots.Remove(bot);
        }

        [ConsoleCommand("raidbots.status")]
        private void CmdStatus(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            RefreshEligibleKits();
            RefreshNpcSpawnGroups();
            var retrySeconds = Math.Max(0f, spawnRetryBlockedUntil - Time.realtimeSinceStartup);
            var retryMessage = retrySeconds > 0f ? $", spawn retry in {retrySeconds:0}s" : "";
            Reply(arg, $"Raidlands roam bots: enabled={config.Enabled}, mode={config.Spawn.SpawnMode}, anchor={SpawnAnchorLabel()}, target={TargetPopulation()}, active={activeBots.Count}{retryMessage}, navcheck={config.RequireNpcNavigatorPlacement}, nativePrefabs={config.Spawn.PreferNativeSpawnGroupPrefabs}, near-player anchors={SpawnAnchorPlayers().Count}, npc spawn groups={npcSpawnGroups.Count}, native baseplayer prefabs={nativeBasePlayerPrefabGroups}, eligible kits={string.Join(", ", eligibleKits.Keys.OrderBy(name => name))}, tracked players={data.players.Count}, tracked bots={data.bots.Count}.");
        }

        [ConsoleCommand("raidbots.enable")]
        private void CmdEnable(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            if (TryReadIntArg(arg, 0, out var population))
            {
                config.TargetPopulation = Clamp(population, config.MinAllowedPopulation, config.MaxAllowedPopulation);
            }

            config.Enabled = true;
            SaveConfig();
            StartRuntime();
            Reply(arg, $"Raidlands roam bots enabled with target population {TargetPopulation()}.");
        }

        [ConsoleCommand("raidbots.disable")]
        private void CmdDisable(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            config.Enabled = false;
            SaveConfig();
            StopRuntime();
            KillAllBots(false);
            Reply(arg, "Raidlands roam bots disabled and active bots despawned.");
        }

        [ConsoleCommand("raidbots.reload")]
        private void CmdReload(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            LoadConfig();
            LoadData();
            RefreshEligibleKits();

            if (config.Enabled)
            {
                StartRuntime();
            }
            else
            {
                StopRuntime();
                KillAllBots(false);
            }

            CreateScoreboards();
            UpdateScoreboards();
            Reply(arg, "Raidlands roam bot config and data reloaded.");
        }

        [ConsoleCommand("raidbots.spawn")]
        private void CmdSpawn(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var count = 1;

            if (!config.Enabled)
            {
                Reply(arg, "Raidlands roam bots are disabled; run raidbots.enable [target] before spawning bots.");
                return;
            }

            if (TryReadIntArg(arg, 0, out var requested))
            {
                count = Clamp(requested, 1, config.MaxAllowedPopulation);
            }

            CleanupInactiveBots();

            var available = TargetPopulation() - activeBots.Count;

            if (available <= 0)
            {
                Reply(arg, $"Raidlands roam bots are already at target population {TargetPopulation()}.");
                return;
            }

            count = Math.Min(count, available);
            var spawned = SpawnBots(count, true);
            Reply(arg, $"Spawned {spawned} roam bot{(spawned == 1 ? "" : "s")}.");
        }

        [ConsoleCommand("raidbots.diag")]
        private void CmdDiag(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            RefreshEligibleKits();
            RefreshNpcSpawnGroups();

            var anchors = SpawnAnchorPlayers();
            Reply(arg, $"Raidlands roam bot diag: enabled={config.Enabled}, mode={config.Spawn.SpawnMode}, anchor={SpawnAnchorLabel()}, anchors={anchors.Count}, target={TargetPopulation()}, active={activeBots.Count}, requireNavigator={config.RequireNpcNavigatorPlacement}, requireLand={config.Spawn.RequireLandSpawns}.");

            foreach (var anchor in anchors.Take(5))
            {
                Reply(arg, $"Anchor {PlayerName(anchor)} at {FormatVector(anchor.transform.position)} ({PositionDiagnostics(anchor.transform.position)}).");
            }

            if (TryFindSpawnPosition(out var position, out var preferredPrefab))
            {
                var prefabs = string.Join(", ", ActivePrefabCandidates(preferredPrefab).Take(5));
                Reply(arg, $"Next spawn candidate: {FormatVector(position)} ({PositionDiagnostics(position)}), preferredPrefab={(string.IsNullOrWhiteSpace(preferredPrefab) ? "none" : preferredPrefab)}, activePrefabs={prefabs}.");
            }
            else
            {
                Reply(arg, "No spawn candidate found with the current mode, anchor, safe-zone, water, and distance filters.");
            }
        }

        [ConsoleCommand("raidbots.testsetup")]
        private void CmdTestSetup(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var anchor = ArgStringFrom(arg, 0);

            config.Enabled = false;
            config.TargetPopulation = 1;
            config.MinAllowedPopulation = 1;
            config.MaxAllowedPopulation = 3;
            config.TeamSizeWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["solo"] = 100,
                ["duo"] = 0,
                ["trio"] = 0
            };
            config.Spawn.SpawnMode = SpawnModeNearPlayers;
            config.Spawn.NearPlayerAnchorNameOrSteamId = string.IsNullOrWhiteSpace(anchor) ? config.Spawn.NearPlayerAnchorNameOrSteamId : anchor.Trim();
            config.Spawn.PreferNativeNpcSpawnPointsNearPlayers = true;
            config.Spawn.PreferNativeSpawnGroupPrefabs = true;
            config.Spawn.UseGeneratedPositionsNearPlayers = false;
            config.Spawn.UseRandomLandFallback = false;
            config.Spawn.RequireLandSpawns = true;
            config.Spawn.AvoidSafeZoneSpawns = true;
            config.Spawn.IgnorePlayersInSafeZones = true;
            config.RequireNpcNavigatorPlacement = true;
            config.DebugSpawnDetails = true;
            config.SpawnFailureRetrySeconds = 30f;
            spawnRetryBlockedUntil = 0f;
            NormalizeConfig();
            SaveConfig();
            StopRuntime();
            KillAllBots(false);

            Reply(arg, $"Raidlands roam bot moving test setup applied: anchor={SpawnAnchorLabel()}, target={TargetPopulation()}, max={config.MaxAllowedPopulation}, navcheck={config.RequireNpcNavigatorPlacement}, nativePrefabs={config.Spawn.PreferNativeSpawnGroupPrefabs}, debug={config.DebugSpawnDetails}, requireLand={config.Spawn.RequireLandSpawns}. Run raidbots.diag, then raidbots.enable 1.");
        }

        [ConsoleCommand("raidbots.navcheck")]
        private void CmdNavCheck(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            if (!TryReadBoolArg(arg, 0, out var enabled))
            {
                Reply(arg, $"Raidlands roam bot navigator placement requirement is {config.RequireNpcNavigatorPlacement}. Use raidbots.navcheck on|off.");
                return;
            }

            config.RequireNpcNavigatorPlacement = enabled;
            SaveConfig();
            spawnRetryBlockedUntil = 0f;
            Reply(arg, $"Raidlands roam bot navigator placement requirement set to {config.RequireNpcNavigatorPlacement}.");
        }

        [ConsoleCommand("raidbots.debug")]
        private void CmdDebug(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            if (!TryReadBoolArg(arg, 0, out var enabled))
            {
                Reply(arg, $"Raidlands roam bot debug spawn details is {config.DebugSpawnDetails}. Use raidbots.debug on|off.");
                return;
            }

            config.DebugSpawnDetails = enabled;
            SaveConfig();
            Reply(arg, $"Raidlands roam bot debug spawn details set to {config.DebugSpawnDetails}.");
        }

        [ConsoleCommand("raidbots.land")]
        private void CmdLand(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            if (!TryReadBoolArg(arg, 0, out var enabled))
            {
                Reply(arg, $"Raidlands roam bot land-spawn requirement is {config.Spawn.RequireLandSpawns}. Use raidbots.land on|off.");
                return;
            }

            config.Spawn.RequireLandSpawns = enabled;
            SaveConfig();
            spawnRetryBlockedUntil = 0f;
            Reply(arg, $"Raidlands roam bot land-spawn requirement set to {config.Spawn.RequireLandSpawns}.");
        }

        [ConsoleCommand("raidbots.nativeprefabs")]
        private void CmdNativePrefabs(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            if (!TryReadBoolArg(arg, 0, out var enabled))
            {
                Reply(arg, $"Raidlands roam bot native spawn-group prefab preference is {config.Spawn.PreferNativeSpawnGroupPrefabs}. Use raidbots.nativeprefabs on|off.");
                return;
            }

            config.Spawn.PreferNativeSpawnGroupPrefabs = enabled;
            SaveConfig();
            spawnRetryBlockedUntil = 0f;
            Reply(arg, $"Raidlands roam bot native spawn-group prefab preference set to {config.Spawn.PreferNativeSpawnGroupPrefabs}.");
        }

        [ConsoleCommand("raidbots.target")]
        private void CmdTarget(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            if (!TryReadIntArg(arg, 0, out var population))
            {
                Reply(arg, $"Raidlands roam bot target population is {TargetPopulation()}.");
                return;
            }

            config.TargetPopulation = Clamp(population, config.MinAllowedPopulation, config.MaxAllowedPopulation);
            SaveConfig();
            spawnRetryBlockedUntil = 0f;

            if (config.Enabled)
            {
                MaintainPopulation();
            }

            Reply(arg, $"Raidlands roam bot target population set to {TargetPopulation()}.");
        }

        [ConsoleCommand("raidbots.mode")]
        private void CmdMode(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var requested = ArgString(arg, 0);

            if (string.IsNullOrWhiteSpace(requested))
            {
                Reply(arg, $"Raidlands roam bot spawn mode is {config.Spawn.SpawnMode}. Use {SpawnModeNearPlayers} or {SpawnModeRandom}.");
                return;
            }

            if (!TryNormalizeSpawnMode(requested, out var mode))
            {
                Reply(arg, $"Unknown spawn mode '{requested}'. Use {SpawnModeNearPlayers} or {SpawnModeRandom}.");
                return;
            }

            config.Spawn.SpawnMode = mode;
            SaveConfig();
            spawnRetryBlockedUntil = 0f;

            if (config.Enabled)
            {
                MaintainPopulation();
            }

            Reply(arg, $"Raidlands roam bot spawn mode set to {mode}.");
        }

        [ConsoleCommand("raidbots.anchor")]
        private void CmdAnchor(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var requested = ArgStringFrom(arg, 0);

            if (string.IsNullOrWhiteSpace(requested))
            {
                Reply(arg, $"Raidlands roam bot near-player anchor is {SpawnAnchorLabel()}. Use raidbots.anchor <name or steam id>, or raidbots.anchor clear.");
                return;
            }

            if (requested.Equals("clear", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("off", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("all", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                config.Spawn.NearPlayerAnchorNameOrSteamId = "";
            }
            else
            {
                config.Spawn.NearPlayerAnchorNameOrSteamId = requested.Trim();
            }

            SaveConfig();
            spawnRetryBlockedUntil = 0f;

            if (config.Enabled)
            {
                MaintainPopulation();
            }

            Reply(arg, $"Raidlands roam bot near-player anchor set to {SpawnAnchorLabel()}.");
        }

        [ConsoleCommand("raidbots.list")]
        private void CmdList(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            CleanupInactiveBots();

            var target = ArgString(arg, 0);
            var targetPlayer = string.IsNullOrWhiteSpace(target) ? null : FindActivePlayer(target);
            var bots = ActiveBotEntries();

            if (bots.Count == 0)
            {
                Reply(arg, "No active Raidlands roam bots.");
                return;
            }

            Reply(arg, $"Active Raidlands roam bots ({bots.Count}):");

            for (var index = 0; index < bots.Count; index++)
            {
                var bot = bots[index].Key;
                var runtime = bots[index].Value;
                var position = bot.transform.position;
                var distance = targetPlayer == null ? "" : $", {Vector3.Distance(targetPlayer.transform.position, position):0}m from {PlayerName(targetPlayer)}";
                Reply(arg, $"{index + 1}. {runtime.DisplayName} [{runtime.KitName}/{runtime.SkillTier}] at {FormatVector(position)}{distance}");
            }
        }

        [ConsoleCommand("raidbots.goto")]
        private void CmdGoto(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var playerQuery = ArgString(arg, 0);

            if (string.IsNullOrWhiteSpace(playerQuery))
            {
                Reply(arg, "Usage: raidbots.goto <player name or steam id> [bot number]");
                return;
            }

            var player = FindActivePlayer(playerQuery);

            if (player == null)
            {
                Reply(arg, $"No connected player matched '{playerQuery}'.");
                return;
            }

            var bots = ActiveBotEntries();

            if (bots.Count == 0)
            {
                Reply(arg, "No active Raidlands roam bots to visit.");
                return;
            }

            var botIndex = 1;

            if (TryReadIntArg(arg, 1, out var requestedIndex))
            {
                botIndex = Clamp(requestedIndex, 1, bots.Count);
            }

            var selected = bots[botIndex - 1];
            var botPosition = selected.Key.transform.position;
            var destination = botPosition + selected.Key.transform.right * 8f + Vector3.up;

            if (TryFindNearbyPosition(botPosition, out var nearbyPosition))
            {
                destination = nearbyPosition;
            }

            destination.y = Math.Max(destination.y, TerrainHeight(destination) + 1f);
            player.Teleport(destination);
            Reply(arg, $"Moved {PlayerName(player)} near bot #{botIndex} {selected.Value.DisplayName} at {FormatVector(botPosition)}.");
        }

        [ConsoleCommand("raidbots.killall")]
        private void CmdKillAll(ConsoleSystem.Arg arg)
        {
            if (!CanAdmin(arg))
            {
                Reply(arg, "You do not have permission to manage Raidlands roam bots.");
                return;
            }

            var count = activeBots.Count;
            KillAllBots(false);
            Reply(arg, $"Despawned {count} roam bot{(count == 1 ? "" : "s")}.");
        }

        [HookMethod("GetRaidlandsRoamBotStats")]
        public JObject GetRaidlandsRoamBotStats()
        {
            return JObject.FromObject(data ?? new StoredData());
        }

        private void RefreshEligibleKits()
        {
            eligibleKits.Clear();

            var kitData = ReadKitsData();

            if (kitData == null)
            {
                return;
            }

            var kits = kitData["_kits"] as JObject;

            if (kits == null)
            {
                return;
            }

            var allowed = new HashSet<string>(config.Kits.EligibleKitNames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var highTier = new HashSet<string>(config.Kits.RareHighTierKitNames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var property in kits.Properties())
            {
                var name = property.Name;
                var kit = property.Value as JObject;

                if (kit == null)
                {
                    continue;
                }

                var isHighTier = highTier.Contains(name);

                if (!allowed.Contains(name) && !isHighTier)
                {
                    continue;
                }

                if (kit.Value<bool?>("IsHidden") == true)
                {
                    continue;
                }

                var requiredPermission = (string) kit["RequiredPermission"] ?? "";

                if (!DefaultGroupHasKitPermission(requiredPermission))
                {
                    continue;
                }

                if (!KitContainsWeapon(kit))
                {
                    continue;
                }

                eligibleKits[name] = new KitEligibility
                {
                    Name = name,
                    RequiredPermission = requiredPermission,
                    HighTier = isHighTier
                };
            }
        }

        private void RefreshNpcSpawnGroups()
        {
            npcSpawnGroups.Clear();
            nativeBasePlayerPrefabGroups = 0;

            foreach (var group in UnityEngine.Object.FindObjectsOfType<SpawnGroup>() ?? new SpawnGroup[0])
            {
                if (group == null || !group.isActiveAndEnabled)
                {
                    continue;
                }

                try
                {
                    if (group.DoesGroupContainNPCs() && group.SpawnPointCount > 0)
                    {
                        var basePlayerPrefab = FirstBasePlayerPrefabPath(group);

                        if (config.Spawn.OnlyUseNativeBasePlayerSpawnGroups && string.IsNullOrWhiteSpace(basePlayerPrefab))
                        {
                            continue;
                        }

                        npcSpawnGroups.Add(group);

                        if (!string.IsNullOrWhiteSpace(basePlayerPrefab))
                        {
                            nativeBasePlayerPrefabGroups++;
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private JObject ReadKitsData()
        {
            try
            {
                var dataPath = Path.Combine(Interface.Oxide.DataFileSystem.Directory, $"{KitsDataFile}.json");

                if (!File.Exists(dataPath))
                {
                    return null;
                }

                return JObject.Parse(File.ReadAllText(dataPath));
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read Kits data for roam bot eligibility: {ex.Message}");
                return null;
            }
        }

        private bool DefaultGroupHasKitPermission(string requiredPermission)
        {
            if (string.IsNullOrWhiteSpace(requiredPermission))
            {
                return true;
            }

            return permission.GroupHasPermission(config.Kits.DefaultGroup, requiredPermission);
        }

        private bool KitContainsWeapon(JObject kit)
        {
            var weaponShortnames = new HashSet<string>(config.Kits.WeaponShortnames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var container in new[] { "BeltItems", "MainItems", "WearItems" })
            {
                var items = kit[container] as JArray;

                if (items == null)
                {
                    continue;
                }

                foreach (var item in items.OfType<JObject>())
                {
                    var shortname = ((string) item["Shortname"] ?? "").Trim();

                    if (weaponShortnames.Contains(shortname))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private KitEligibility ChooseKit()
        {
            if (eligibleKits.Count == 0)
            {
                return null;
            }

            var highTier = eligibleKits.Values.Where(kit => kit.HighTier).ToList();

            if (highTier.Count > 0 && random.Next(100) < config.HighTierKitWeight)
            {
                return highTier[random.Next(highTier.Count)];
            }

            var normal = eligibleKits.Values.Where(kit => !kit.HighTier).ToList();

            if (normal.Count == 0)
            {
                normal = eligibleKits.Values.ToList();
            }

            return normal[random.Next(normal.Count)];
        }

        private int WeightedTeamSize()
        {
            var value = WeightedKey(config.TeamSizeWeights, "solo");

            if (value.Equals("trio", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (value.Equals("duo", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 1;
        }

        private string WeightedKey(Dictionary<string, int> weights, string fallback)
        {
            if (weights == null || weights.Count == 0)
            {
                return fallback;
            }

            var entries = weights
                .Where(entry => entry.Value > 0 && !string.IsNullOrWhiteSpace(entry.Key))
                .ToList();

            var total = entries.Sum(entry => Math.Max(0, entry.Value));

            if (total <= 0)
            {
                return fallback;
            }

            var roll = random.Next(total);
            var running = 0;

            foreach (var entry in entries)
            {
                running += Math.Max(0, entry.Value);

                if (roll < running)
                {
                    return entry.Key;
                }
            }

            return fallback;
        }

        private SkillDefinition SkillFor(string tier)
        {
            if (config.SkillDefinitions != null && config.SkillDefinitions.TryGetValue(tier, out var definition) && definition != null)
            {
                definition.Health = Math.Max(1f, definition.Health);
                definition.DamageScale = Math.Max(0.1f, definition.DamageScale);
                definition.IncomingDamageScale = Math.Max(0.1f, definition.IncomingDamageScale);
                definition.LeashDistance = Math.Max(25f, definition.LeashDistance);
                definition.LeashSeconds = Math.Max(10f, definition.LeashSeconds);
                return definition;
            }

            return new SkillDefinition();
        }

        private string ChooseProfileName()
        {
            var activeKeys = new HashSet<string>(activeBots.Values.Select(runtime => runtime.BotKey), StringComparer.OrdinalIgnoreCase);
            var candidates = (config.BotProfiles ?? new List<string>())
                .Select(name => CleanName(name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(_ => random.Next())
                .ToList();

            foreach (var candidate in candidates)
            {
                if (!activeKeys.Contains(BotKey(candidate)))
                {
                    return candidate;
                }
            }

            return $"Roamer{random.Next(1000, 9999).ToString(CultureInfo.InvariantCulture)}";
        }

        private string CleanName(string value)
        {
            var text = (value ?? "").Trim();

            if (text.Length > 32)
            {
                text = text.Substring(0, 32);
            }

            return string.Concat(text.Where(character => char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == ' ')).Trim();
        }

        private string BotKey(string displayName)
        {
            var builder = new System.Text.StringBuilder();

            foreach (var character in (displayName ?? "").Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                    continue;
                }

                if ((character == '_' || character == '-' || char.IsWhiteSpace(character)) && builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            var key = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(key) ? "roamer" : key;
        }

        private bool TryFindSpawnPosition(out Vector3 position, out string preferredPrefab)
        {
            var mode = NormalizeSpawnMode(config.Spawn.SpawnMode);

            if (mode == SpawnModeNearPlayers)
            {
                if (TryFindNearPlayerSpawnPosition(out position, out preferredPrefab))
                {
                    return true;
                }

                if (!config.Spawn.UseRandomLandFallback)
                {
                    position = Vector3.zero;
                    preferredPrefab = "";
                    return false;
                }
            }

            if (config.Spawn.PreferNativeNpcSpawnGroups && TryFindNativeNpcSpawnPosition(out position, out preferredPrefab))
            {
                return true;
            }

            return TryFindRandomLandSpawnPosition(out position, out preferredPrefab);
        }

        private bool TryFindNearPlayerSpawnPosition(out Vector3 position, out string preferredPrefab)
        {
            var players = SpawnAnchorPlayers();

            if (players.Count == 0)
            {
                position = Vector3.zero;
                preferredPrefab = "";
                return false;
            }

            if (config.Spawn.PreferNativeNpcSpawnPointsNearPlayers && TryFindNativeNpcSpawnPositionNearPlayers(players, out position, out preferredPrefab))
            {
                return true;
            }

            if (!config.Spawn.UseGeneratedPositionsNearPlayers)
            {
                position = Vector3.zero;
                preferredPrefab = "";
                return false;
            }

            var minDistance = Math.Max(25f, config.Spawn.NearPlayerMinDistance);
            var maxDistance = Math.Max(minDistance + 10f, config.Spawn.NearPlayerMaxDistance);
            var attempts = Math.Max(8, config.Spawn.NearPlayerAttempts);

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var player = players[UnityEngine.Random.Range(0, players.Count)];
                var distance = UnityEngine.Random.Range(minDistance, maxDistance);
                var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                var offset = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                var candidate = player.transform.position + offset;

                candidate.y = TerrainHeight(candidate) + 0.25f;

                if (IsUnderWater(candidate))
                {
                    continue;
                }

                if (IsBlockedSafeZoneSpawn(candidate))
                {
                    continue;
                }

                if (!NavMesh.SamplePosition(candidate, out var hit, config.Spawn.NavmeshSampleDistance, NavMesh.AllAreas))
                {
                    continue;
                }

                if (IsUnderWater(hit.position) || IsBlockedSafeZoneSpawn(hit.position) || Vector3.Distance(player.transform.position, hit.position) < minDistance * 0.8f)
                {
                    continue;
                }

                position = hit.position;
                preferredPrefab = "";
                return true;
            }

            position = Vector3.zero;
            preferredPrefab = "";
            return false;
        }

        private bool TryFindNativeNpcSpawnPositionNearPlayers(List<BasePlayer> players, out Vector3 position, out string preferredPrefab)
        {
            if (npcSpawnGroups.Count == 0)
            {
                RefreshNpcSpawnGroups();
            }

            if (npcSpawnGroups.Count == 0 || players == null || players.Count == 0)
            {
                position = Vector3.zero;
                preferredPrefab = "";
                return false;
            }

            var minDistance = Math.Max(25f, config.Spawn.NearPlayerMinDistance);
            var maxDistance = Math.Max(minDistance + 10f, config.Spawn.NearPlayerMaxDistance);
            var attempts = Math.Max(8, config.Spawn.NearPlayerNativeSpawnPointAttempts);
            var candidates = new List<SpawnCandidate>();

            foreach (var group in npcSpawnGroups.OrderBy(_ => random.Next()).Take(Math.Min(npcSpawnGroups.Count, attempts)))
            {
                if (group == null)
                {
                    continue;
                }

                var groupPreferredPrefab = config.Spawn.PreferNativeSpawnGroupPrefabs ? FirstBasePlayerPrefabPath(group) : "";

                if (config.Spawn.PreferNativeSpawnGroupPrefabs && string.IsNullOrWhiteSpace(groupPreferredPrefab))
                {
                    continue;
                }

                var spawnPoints = group.spawnPoints;

                if (spawnPoints == null || spawnPoints.Length == 0)
                {
                    continue;
                }

                foreach (var spawnPoint in spawnPoints.OrderBy(_ => random.Next()))
                {
                    if (spawnPoint == null)
                    {
                        continue;
                    }

                    spawnPoint.GetLocation(out var candidate, out var rotation);

                    if (IsUnderWater(candidate))
                    {
                        continue;
                    }

                    if (IsBlockedSafeZoneSpawn(candidate))
                    {
                        continue;
                    }

                    var distance = DistanceToClosestAnchor(players, candidate);

                    if (distance < minDistance || distance > maxDistance)
                    {
                        continue;
                    }

                    candidates.Add(new SpawnCandidate
                    {
                        Position = candidate,
                        PreferredPrefab = groupPreferredPrefab
                    });
                }
            }

            if (candidates.Count == 0)
            {
                position = Vector3.zero;
                preferredPrefab = "";
                return false;
            }

            var selected = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            position = selected.Position;
            preferredPrefab = selected.PreferredPrefab ?? "";
            return true;
        }

        private float DistanceToClosestAnchor(List<BasePlayer> players, Vector3 position)
        {
            var closest = float.MaxValue;

            foreach (var player in players)
            {
                if (player == null)
                {
                    continue;
                }

                closest = Math.Min(closest, Vector3.Distance(player.transform.position, position));
            }

            return closest;
        }

        private bool TryFindRandomLandSpawnPosition(out Vector3 position, out string preferredPrefab)
        {
            var mapSize = TerrainMeta.Size.x > 0f ? TerrainMeta.Size.x : 4500f;
            var half = mapSize * 0.5f;

            for (var attempt = 0; attempt < config.Spawn.MaxPositionAttempts; attempt++)
            {
                var candidate = new Vector3(
                    UnityEngine.Random.Range(-half, half),
                    0f,
                    UnityEngine.Random.Range(-half, half)
                );

                candidate.y = TerrainHeight(candidate) + 0.25f;

                if (IsUnderWater(candidate))
                {
                    continue;
                }

                if (IsBlockedSafeZoneSpawn(candidate))
                {
                    continue;
                }

                if (!NavMesh.SamplePosition(candidate, out var hit, config.Spawn.NavmeshSampleDistance, NavMesh.AllAreas))
                {
                    continue;
                }

                if (IsUnderWater(hit.position) || IsBlockedSafeZoneSpawn(hit.position))
                {
                    continue;
                }

                position = hit.position;
                preferredPrefab = "";
                return true;
            }

            position = Vector3.zero;
            preferredPrefab = "";
            return false;
        }

        private bool TryFindNativeNpcSpawnPosition(out Vector3 position, out string preferredPrefab)
        {
            if (npcSpawnGroups.Count == 0)
            {
                RefreshNpcSpawnGroups();
            }

            if (npcSpawnGroups.Count == 0)
            {
                position = Vector3.zero;
                preferredPrefab = "";
                return false;
            }

            for (var attempt = 0; attempt < config.Spawn.NativeNpcSpawnGroupAttempts; attempt++)
            {
                var group = npcSpawnGroups[UnityEngine.Random.Range(0, npcSpawnGroups.Count)];

                if (group == null)
                {
                    continue;
                }

                BaseSpawnPoint spawnPoint;
                Vector3 candidate;
                Quaternion rotation;

                try
                {
                    var prefabRef = ChooseNativePrefabRef(group);
                    var basePlayerPrefab = BasePlayerPrefabPath(prefabRef);

                    if (config.Spawn.OnlyUseNativeBasePlayerSpawnGroups && string.IsNullOrWhiteSpace(basePlayerPrefab))
                    {
                        continue;
                    }

                    if (config.Spawn.PreferNativeSpawnGroupPrefabs && string.IsNullOrWhiteSpace(basePlayerPrefab))
                    {
                        continue;
                    }

                    if (!TryGetNativeSpawnPoint(group, out spawnPoint, out candidate, out rotation))
                    {
                        continue;
                    }

                    preferredPrefab = config.Spawn.PreferNativeSpawnGroupPrefabs ? basePlayerPrefab : "";
                }
                catch
                {
                    continue;
                }

                if (spawnPoint == null)
                {
                    continue;
                }

                if (IsUnderWater(candidate))
                {
                    continue;
                }

                if (IsBlockedSafeZoneSpawn(candidate))
                {
                    continue;
                }

                position = candidate;
                return true;
            }

            position = Vector3.zero;
            preferredPrefab = "";
            return false;
        }

        private string FirstBasePlayerPrefabPath(SpawnGroup group)
        {
            if (group?.prefabs == null)
            {
                return "";
            }

            foreach (var entry in group.prefabs)
            {
                var path = BasePlayerPrefabPath(entry?.prefab);

                if (CanUseRoamPrefab(path))
                {
                    return path;
                }
            }

            return "";
        }

        private GameObjectRef ChooseNativePrefabRef(SpawnGroup group)
        {
            if (group?.prefabs == null || group.prefabs.Count == 0)
            {
                return null;
            }

            var entries = group.prefabs
                .Where(entry => entry?.prefab != null && entry.prefab.isValid)
                .Where(entry =>
                {
                    var path = BasePlayerPrefabPath(entry.prefab);
                    return (!config.Spawn.OnlyUseNativeBasePlayerSpawnGroups || !string.IsNullOrWhiteSpace(path)) && CanUseRoamPrefab(path);
                })
                .ToList();

            if (entries.Count == 0)
            {
                return null;
            }

            var totalWeight = entries.Sum(entry => Math.Max(0, entry.weight));

            if (totalWeight <= 0)
            {
                return entries[UnityEngine.Random.Range(0, entries.Count)].prefab;
            }

            var roll = UnityEngine.Random.Range(0, totalWeight);
            var running = 0;

            foreach (var entry in entries)
            {
                running += Math.Max(0, entry.weight);

                if (roll < running)
                {
                    return entry.prefab;
                }
            }

            return entries[entries.Count - 1].prefab;
        }

        private bool TryGetNativeSpawnPoint(SpawnGroup group, out BaseSpawnPoint spawnPoint, out Vector3 position, out Quaternion rotation)
        {
            spawnPoint = null;
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var spawnPoints = group?.spawnPoints;

            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return false;
            }

            for (var attempt = 0; attempt < spawnPoints.Length; attempt++)
            {
                spawnPoint = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)];

                if (spawnPoint == null)
                {
                    continue;
                }

                spawnPoint.GetLocation(out position, out rotation);
                return true;
            }

            return false;
        }

        private string BasePlayerPrefabPath(GameObjectRef prefabRef)
        {
            if (prefabRef == null || !prefabRef.isValid)
            {
                return "";
            }

            try
            {
                var prefabEntity = prefabRef.GetEntity();

                if (prefabEntity is BasePlayer)
                {
                    return prefabRef.resourcePath ?? "";
                }
            }
            catch
            {
            }

            return "";
        }

        private bool TryFindNearbyPosition(Vector3 origin, out Vector3 position)
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var offset = UnityEngine.Random.insideUnitCircle * config.Spawn.GroupSpawnRadius;
                var candidate = origin + new Vector3(offset.x, 0f, offset.y);
                candidate.y = TerrainHeight(candidate) + 0.25f;

                if (IsUnderWater(candidate))
                {
                    continue;
                }

                if (IsBlockedSafeZoneSpawn(candidate))
                {
                    continue;
                }

                if (NavMesh.SamplePosition(candidate, out var hit, config.Spawn.NavmeshSampleDistance, NavMesh.AllAreas))
                {
                    if (!IsUnderWater(hit.position) && !IsBlockedSafeZoneSpawn(hit.position))
                    {
                        position = hit.position;
                        return true;
                    }
                }
            }

            position = origin;
            return false;
        }

        private float TerrainHeight(Vector3 position)
        {
            return TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(position) : position.y;
        }

        private string PositionDiagnostics(Vector3 position)
        {
            var terrain = TerrainHeight(position);
            var waterMap = TerrainMeta.WaterMap != null ? TerrainMeta.WaterMap.GetHeight(position) : float.NaN;
            var waterSurface = float.NaN;

            try
            {
                waterSurface = WaterLevel.GetWaterSurface(position, true, true, null);
            }
            catch
            {
            }

            var sampled = NavMesh.SamplePosition(position, out var hit, config.Spawn.NavmeshSampleDistance, NavMesh.AllAreas);
            var sample = sampled ? FormatVector(hit.position) : "none";
            return $"terrain={terrain:0.0}, waterMap={(float.IsNaN(waterMap) ? "n/a" : waterMap.ToString("0.0", CultureInfo.InvariantCulture))}, waterSurface={(float.IsNaN(waterSurface) ? "n/a" : waterSurface.ToString("0.0", CultureInfo.InvariantCulture))}, underwater={IsUnderWater(position)}, safeZone={IsBlockedSafeZoneSpawn(position)}, unityNavSample={sample}";
        }

        private bool IsUnderWater(Vector3 position)
        {
            if (config?.Spawn?.RequireLandSpawns != true)
            {
                return false;
            }

            if (position.y < config.Spawn.MinimumLandHeight)
            {
                return true;
            }

            if (TerrainMeta.WaterMap != null && TerrainMeta.WaterMap.GetHeight(position) + config.Spawn.MinimumAboveWater > position.y)
            {
                return true;
            }

            try
            {
                if (WaterLevel.Test(position, true, true))
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                var waterSurface = WaterLevel.GetWaterSurface(position, true, true, null);

                if (!float.IsNaN(waterSurface) && position.y < waterSurface + config.Spawn.MinimumAboveWater)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsNonLandRoamPrefab(string prefab)
        {
            var value = prefab ?? "";
            return value.IndexOf("underwaterdweller", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("tunneldweller", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool CanUseRoamPrefab(string prefab)
        {
            if (string.IsNullOrWhiteSpace(prefab))
            {
                return false;
            }

            if (!config.UseGen2ScientistFallback && prefab.IndexOf("/gen2/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (config.Spawn.RequireLandSpawns && IsNonLandRoamPrefab(prefab))
            {
                return false;
            }

            return true;
        }

        private bool IsBlockedSafeZoneSpawn(Vector3 position)
        {
            return config?.Spawn?.AvoidSafeZoneSpawns == true && IsSafeZonePosition(position, config.Spawn.SafeZoneSpawnBufferDistance);
        }

        private bool ShouldIgnoreSafeZonePlayer(BasePlayer player)
        {
            return config?.Spawn?.IgnorePlayersInSafeZones == true && PlayerInSafeZone(player);
        }

        private bool PlayerInSafeZone(BasePlayer player)
        {
            if (player == null)
            {
                return false;
            }

            if (player.InSafeZone())
            {
                return true;
            }

            return IsSafeZonePosition(player.transform.position, 0f);
        }

        private bool IsSafeZonePosition(Vector3 position, float extraDistance)
        {
            var zones = TriggerSafeZone.allSafeZones;

            if (zones == null)
            {
                return false;
            }

            foreach (var zone in zones)
            {
                if (zone == null)
                {
                    continue;
                }

                var collider = zone.triggerCollider;
                var center = collider != null ? collider.bounds.center : zone.transform.position;
                var radius = 25f;

                if (collider != null)
                {
                    var extents = collider.bounds.extents;
                    radius = Mathf.Max(extents.x, extents.z);
                }

                var distance = Distance2D(position, center);

                if (distance <= radius + extraDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private float Distance2D(Vector3 a, Vector3 b)
        {
            var x = a.x - b.x;
            var z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        private bool IsLiveBot(BasePlayer bot)
        {
            return bot != null && !bot.IsDestroyed && !bot.IsDead();
        }

        private BotRuntime RuntimeFor(BasePlayer player)
        {
            if (player == null)
            {
                return null;
            }

            activeBots.TryGetValue(player, out var runtime);
            return runtime;
        }

        private bool IsRealPlayer(BasePlayer player)
        {
            if (player == null)
            {
                return false;
            }

            return IsSteamId64(player.UserIDString) && RuntimeFor(player) == null;
        }

        private List<BasePlayer> SpawnAnchorPlayers()
        {
            var players = BasePlayer.activePlayerList
                .Where(player => IsRealPlayer(player) && player.IsConnected && !player.IsDead() && !player.IsSleeping() && !ShouldIgnoreSafeZonePlayer(player))
                .ToList();

            var anchor = config?.Spawn?.NearPlayerAnchorNameOrSteamId;

            if (string.IsNullOrWhiteSpace(anchor))
            {
                return players;
            }

            return players
                .Where(player => PlayerMatchesQuery(player, anchor))
                .ToList();
        }

        private bool IsSteamId64(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length == 17
                && value.StartsWith("7656119", StringComparison.Ordinal)
                && value.All(char.IsDigit);
        }

        private PlayerNpcStats EnsurePlayerStats(BasePlayer player)
        {
            var steamId = player.UserIDString;

            if (!data.players.TryGetValue(steamId, out var stats) || stats == null)
            {
                stats = new PlayerNpcStats { steam_id64 = steamId };
                data.players[steamId] = stats;
            }

            stats.display_name = CleanName(PlayerName(player));
            return stats;
        }

        private BotStats EnsureBotStats(BotRuntime runtime)
        {
            if (!data.bots.TryGetValue(runtime.BotKey, out var stats) || stats == null)
            {
                stats = new BotStats { bot_key = runtime.BotKey };
                data.bots[runtime.BotKey] = stats;
            }

            stats.display_name = runtime.DisplayName;
            stats.kit_name = runtime.KitName;
            stats.skill_tier = runtime.SkillTier;
            return stats;
        }

        private void CreateScoreboards()
        {
            if (Scoreboards == null)
            {
                return;
            }

            Scoreboards.Call("CreateScoreboard", ScoreboardNpcKills, "Players with the most roaming bot kills", TopPlayerNpcKills().ToArray());
            Scoreboards.Call("CreateScoreboard", ScoreboardDeathsByNpc, "Players killed most often by roaming bots", TopPlayerDeathsByNpc().ToArray());
            Scoreboards.Call("CreateScoreboard", ScoreboardBotKd, "Roaming bot standings by K/D", TopBotKd().ToArray());
        }

        private void UpdateScoreboards()
        {
            if (Scoreboards == null)
            {
                return;
            }

            Scoreboards.Call("UpdateScoreboard", ScoreboardNpcKills, TopPlayerNpcKills().ToArray());
            Scoreboards.Call("UpdateScoreboard", ScoreboardDeathsByNpc, TopPlayerDeathsByNpc().ToArray());
            Scoreboards.Call("UpdateScoreboard", ScoreboardBotKd, TopBotKd().ToArray());
        }

        private IEnumerable<KeyValuePair<string, string>> TopPlayerNpcKills()
        {
            return data.players.Values
                .Where(player => player != null && player.npc_kills > 0)
                .OrderByDescending(player => player.npc_kills)
                .ThenBy(player => player.display_name)
                .Take(15)
                .Select(player => new KeyValuePair<string, string>(ScoreboardName(player.display_name, player.steam_id64), player.npc_kills.ToString(CultureInfo.InvariantCulture)));
        }

        private IEnumerable<KeyValuePair<string, string>> TopPlayerDeathsByNpc()
        {
            return data.players.Values
                .Where(player => player != null && player.deaths_by_npc > 0)
                .OrderByDescending(player => player.deaths_by_npc)
                .ThenBy(player => player.display_name)
                .Take(15)
                .Select(player => new KeyValuePair<string, string>(ScoreboardName(player.display_name, player.steam_id64), player.deaths_by_npc.ToString(CultureInfo.InvariantCulture)));
        }

        private IEnumerable<KeyValuePair<string, string>> TopBotKd()
        {
            return data.bots.Values
                .Where(bot => bot != null && (bot.kills > 0 || bot.deaths > 0))
                .OrderByDescending(bot => BotKdr(bot))
                .ThenByDescending(bot => bot.kills)
                .Take(15)
                .Select(bot => new KeyValuePair<string, string>($"{bot.display_name} ({bot.kit_name}/{bot.skill_tier})", BotKdr(bot).ToString("0.00", CultureInfo.InvariantCulture)));
        }

        private string ScoreboardName(string displayName, string fallback)
        {
            var name = string.IsNullOrWhiteSpace(displayName) ? fallback : displayName;
            return name.Length <= 32 ? name : name.Substring(0, 32);
        }

        private float BotKdr(BotStats bot)
        {
            return bot.deaths <= 0 ? bot.kills : (float) bot.kills / bot.deaths;
        }

        private bool CanAdmin(ConsoleSystem.Arg arg)
        {
            var player = arg?.Connection?.player as BasePlayer;

            if (player == null)
            {
                return true;
            }

            return player.IsAdmin || permission.UserHasPermission(player.UserIDString, AdminPermission);
        }

        private void Reply(ConsoleSystem.Arg arg, string message)
        {
            var player = arg?.Connection?.player as BasePlayer;

            if (player != null)
            {
                player.ChatMessage(message);
                return;
            }

            Puts(message);
        }

        private string ArgString(ConsoleSystem.Arg arg, int index)
        {
            if (arg?.Args == null || arg.Args.Length <= index)
            {
                return "";
            }

            return arg.Args[index].ToString().Trim();
        }

        private string ArgStringFrom(ConsoleSystem.Arg arg, int index)
        {
            if (arg?.Args == null || arg.Args.Length <= index)
            {
                return "";
            }

            return string.Join(" ", arg.Args.Skip(index).Select(value => value.ToString()).Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        }

        private List<KeyValuePair<BasePlayer, BotRuntime>> ActiveBotEntries()
        {
            CleanupInactiveBots();

            return activeBots
                .Where(entry => IsLiveBot(entry.Key) && entry.Value != null)
                .OrderBy(entry => entry.Value.DisplayName)
                .ThenBy(entry => entry.Key.net?.ID.Value ?? 0UL)
                .ToList();
        }

        private BasePlayer FindActivePlayer(string partialNameOrId)
        {
            var query = (partialNameOrId ?? "").Trim();

            if (query == "")
            {
                return null;
            }

            return BasePlayer.activePlayerList.FirstOrDefault(player =>
                player != null
                    && !player.IsNpc
                    && PlayerMatchesQuery(player, query));
        }

        private bool PlayerMatchesQuery(BasePlayer player, string query)
        {
            if (player == null || string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            var name = PlayerName(player);
            var text = query.Trim();

            return player.UserIDString.Equals(text, StringComparison.OrdinalIgnoreCase)
                || name.Equals(text, StringComparison.OrdinalIgnoreCase)
                || name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string SpawnAnchorLabel()
        {
            var anchor = config?.Spawn?.NearPlayerAnchorNameOrSteamId;
            return string.IsNullOrWhiteSpace(anchor) ? "all" : anchor.Trim();
        }

        private string PlayerName(BasePlayer player)
        {
            return player == null ? "" : player.displayName.ToString();
        }

        private string FormatVector(Vector3 position)
        {
            return $"{position.x:0.0}, {position.y:0.0}, {position.z:0.0}";
        }

        private bool TryReadIntArg(ConsoleSystem.Arg arg, int index, out int value)
        {
            value = 0;

            if (arg?.Args == null || arg.Args.Length <= index)
            {
                return false;
            }

            return int.TryParse(arg.Args[index].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryReadBoolArg(ConsoleSystem.Arg arg, int index, out bool value)
        {
            value = false;
            var text = ArgString(arg, index).ToLowerInvariant();

            if (text == "1" || text == "true" || text == "yes" || text == "on" || text == "enable" || text == "enabled")
            {
                value = true;
                return true;
            }

            if (text == "0" || text == "false" || text == "no" || text == "off" || text == "disable" || text == "disabled")
            {
                value = false;
                return true;
            }

            return false;
        }

        private int TargetPopulation()
        {
            return Clamp(config.TargetPopulation, config.MinAllowedPopulation, config.MaxAllowedPopulation);
        }

        private string NormalizeSpawnMode(string value)
        {
            return TryNormalizeSpawnMode(value, out var mode) ? mode : SpawnModeNearPlayers;
        }

        private bool TryNormalizeSpawnMode(string value, out string mode)
        {
            var normalized = (value ?? "")
                .Trim()
                .ToLowerInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");

            if (normalized == "near" || normalized == "near_player" || normalized == "near_players" || normalized == "players")
            {
                mode = SpawnModeNearPlayers;
                return true;
            }

            if (normalized == SpawnModeRandom)
            {
                mode = SpawnModeRandom;
                return true;
            }

            mode = SpawnModeNearPlayers;
            return false;
        }

        private int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
