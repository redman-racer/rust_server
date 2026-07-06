using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RaidlandsSentryTurrets", "Raidlands", "1.0.3")]
    [Description("Converts specially named auto turret items into player-deployed Outpost sentry turrets.")]
    public class RaidlandsSentryTurrets : RustPlugin
    {
        private const string AdminPermission = "raidlands.sentryturrets.admin";
        private const string VanillaAutoTurretPrefab = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        private const string VanillaAutoTurretShortPrefab = "autoturret_deployed";

        [PluginReference]
        private Plugin Clans, Friends;

        private Configuration config;
        private readonly HashSet<ulong> handledTurretIds = new HashSet<ulong>();
        private string resolvedSentryPrefab;

        private class Configuration
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Sentry Item")]
            public SentryItem SentryItem = new SentryItem();

            [JsonProperty("Outpost Sentry Prefab Candidates")]
            public string[] OutpostSentryPrefabCandidates =
            {
                "assets/prefabs/npc/autoturret/sentry.scientist.static.prefab"
            };

            [JsonProperty("Outpost Sentry Short Prefab Name Candidates")]
            public string[] OutpostSentryShortPrefabNameCandidates =
            {
                "sentry.scientist.static"
            };

            [JsonProperty("Required Spawned Short Prefab Name")]
            public string RequiredSpawnedShortPrefabName = "sentry.scientist.static";

            [JsonProperty("Force Attack All")]
            public bool ForceAttackAll = true;

            [JsonProperty("Use Normal Turret Authorization")]
            public bool UseNormalTurretAuthorization = true;

            [JsonProperty("Clear Authorized Players On Spawned NPC Sentries")]
            public bool ClearAuthorizedPlayersOnSpawnedNpcSentries = false;

            [JsonProperty("Post Spawn Attack All Reapply Delays Seconds")]
            public float[] AttackAllReapplyDelaysSeconds = { 0.05f, 0.25f, 1f, 3f };

            [JsonProperty("Remove TurretSwitches Child Entities From Spawned NPC Sentries")]
            public bool RemoveTurretSwitchChildEntities = true;

            [JsonProperty("Replacement Delay Seconds")]
            public float ReplacementDelaySeconds = 0.1f;

            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "<color=#ce422b>[Raidlands]</color>";
        }

        private class SentryItem
        {
            [JsonProperty("Shortname")]
            public string Shortname = "autoturret";

            [JsonProperty("Display Name")]
            public string DisplayName = "Outpost Sentry Turret";

            [JsonProperty("Skin")]
            public ulong Skin;

            [JsonProperty("Require Display Name Match")]
            public bool RequireDisplayNameMatch = true;
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>() ?? new Configuration();
            }
            catch
            {
                PrintWarning("Could not read config; creating a new default config.");
                config = new Configuration();
            }

            if (config.SentryItem == null)
            {
                config.SentryItem = new SentryItem();
            }

            if (string.IsNullOrWhiteSpace(config.SentryItem.Shortname))
            {
                config.SentryItem.Shortname = "autoturret";
            }

            if (string.IsNullOrWhiteSpace(config.SentryItem.DisplayName))
            {
                config.SentryItem.DisplayName = "Outpost Sentry Turret";
            }

            if (config.OutpostSentryPrefabCandidates == null || config.OutpostSentryPrefabCandidates.Length == 0)
            {
                config.OutpostSentryPrefabCandidates = new Configuration().OutpostSentryPrefabCandidates;
            }

            if (config.OutpostSentryShortPrefabNameCandidates == null || config.OutpostSentryShortPrefabNameCandidates.Length == 0)
            {
                config.OutpostSentryShortPrefabNameCandidates = new Configuration().OutpostSentryShortPrefabNameCandidates;
            }

            if (string.IsNullOrWhiteSpace(config.RequiredSpawnedShortPrefabName))
            {
                config.RequiredSpawnedShortPrefabName = "sentry.scientist.static";
            }

            if (config.AttackAllReapplyDelaysSeconds == null || config.AttackAllReapplyDelaysSeconds.Length == 0)
            {
                config.AttackAllReapplyDelaysSeconds = new Configuration().AttackAllReapplyDelaysSeconds;
            }

            if (config.UseNormalTurretAuthorization)
            {
                config.ClearAuthorizedPlayersOnSpawnedNpcSentries = false;
            }

            if (config.ReplacementDelaySeconds < 0f)
            {
                config.ReplacementDelaySeconds = 0.1f;
            }

            if (string.IsNullOrWhiteSpace(config.ChatPrefix))
            {
                config.ChatPrefix = new Configuration().ChatPrefix;
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
        }

        private void OnServerInitialized()
        {
            if (!config.Enabled)
            {
                return;
            }

            foreach (var serverEntity in BaseNetworkable.serverEntities)
            {
                var entity = serverEntity as BaseEntity;
                if (!IsManagedNpcSentry(entity))
                {
                    continue;
                }

                var authIds = BuildAuthorizationSet(entity as AutoTurret, entity.OwnerID);
                ApplySentryRuntimeState(entity, authIds);
                ScheduleSentryRuntimeStateReapply(entity, authIds);
            }
        }

        [ConsoleCommand("raidlands.sentry.give")]
        private void CCmdGiveSentry(ConsoleSystem.Arg arg)
        {
            if (!CanUseAdminCommand(arg))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                arg.ReplyWith("Usage: raidlands.sentry.give <playerNameOrSteamId> [amount]");
                return;
            }

            var target = FindPlayer(arg.GetString(0));
            if (target == null)
            {
                arg.ReplyWith("Player not found.");
                return;
            }

            var amount = 1;
            if (arg.Args.Length >= 2)
            {
                int.TryParse(arg.GetString(1), out amount);
            }

            var result = GiveSentryTurretsDetailed(target, Math.Max(1, amount));
            var dropped = result.Dropped > 0 ? $" {result.Dropped} dropped at their feet because inventory was full." : "";
            arg.ReplyWith($"Gave {result.Given} Outpost Sentry Turret item(s) to {target.displayName}.{dropped}");
        }

        [ConsoleCommand("raidlands.sentry.prefabs")]
        private void CCmdListSentryPrefabs(ConsoleSystem.Arg arg)
        {
            if (!CanUseAdminCommand(arg))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            var lines = new List<string>();
            foreach (var prefab in config.OutpostSentryPrefabCandidates ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(prefab))
                {
                    continue;
                }

                lines.Add(DescribePrefab(prefab));
            }

            var matches = FindSentryPrefabMatches();
            foreach (var prefab in matches)
            {
                if (!lines.Contains(DescribePrefab(prefab)))
                {
                    lines.Add(DescribePrefab(prefab));
                }
            }

            if (lines.Count == 0)
            {
                arg.ReplyWith("No sentry prefab candidates found.");
                return;
            }

            arg.ReplyWith($"Required short prefab name: {config.RequiredSpawnedShortPrefabName}\nSentry prefab candidates:\n" + string.Join("\n", lines));
        }

        public int GiveSentryTurrets(ulong playerId, int amount)
        {
            var player = BasePlayer.FindAwakeOrSleeping(playerId.ToString());
            if (player == null)
            {
                return 0;
            }

            return GiveSentryTurretsDetailed(player, Math.Max(1, amount)).Given;
        }

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            if (!config.Enabled)
            {
                return;
            }

            var player = planner?.GetOwnerPlayer();
            var entity = gameObject?.ToBaseEntity();
            var sourceItem = ResolveSourceItem(player, planner);
            if ((!IsSentryItem(sourceItem) && !HasSentryMarkerSkin(entity)) || !IsVanillaAutoTurret(entity))
            {
                return;
            }

            QueueReplacement(player, entity);
        }

        private void OnItemDeployed(Deployer deployer, BaseEntity entity)
        {
            HandleItemDeployed(deployer, entity);
        }

        private void OnItemDeployed(Deployer deployer, ItemModDeployable modDeployable, BaseEntity entity)
        {
            HandleItemDeployed(deployer, entity);
        }

        private void OnItemDeployed(Deployer deployer, BaseEntity slotEntity, BaseEntity entity)
        {
            HandleItemDeployed(deployer, entity);
        }

        private void HandleItemDeployed(Deployer deployer, BaseEntity entity)
        {
            if (!config.Enabled)
            {
                return;
            }

            var player = deployer?.GetOwnerPlayer();
            var sourceItem = ResolveSourceItem(player, deployer);
            if ((!IsSentryItem(sourceItem) && !HasSentryMarkerSkin(entity)) || !IsVanillaAutoTurret(entity))
            {
                return;
            }

            QueueReplacement(player, entity);
        }

        private void QueueReplacement(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null || entity.IsDestroyed || entity.net == null)
            {
                return;
            }

            if (!handledTurretIds.Add(entity.net.ID.Value))
            {
                return;
            }

            timer.Once(config.ReplacementDelaySeconds, () => ReplaceTurret(player.userID, entity));
        }

        private void ReplaceTurret(ulong ownerId, BaseEntity original)
        {
            if (original == null || original.IsDestroyed || !IsVanillaAutoTurret(original))
            {
                return;
            }

            var prefab = ResolvePrefab();
            if (string.IsNullOrWhiteSpace(prefab))
            {
                WarnOwner(ownerId, "Outpost sentry prefab is not available; the placed turret was left as a normal auto turret.");
                return;
            }

            var authIds = BuildAuthorizationSet(original as AutoTurret, ownerId);
            var position = original.transform.position;
            var rotation = original.transform.rotation;
            var replacement = GameManager.server.CreateEntity(prefab, position, rotation);
            if (replacement == null)
            {
                WarnOwner(ownerId, $"Could not create Outpost sentry prefab '{prefab}'; the placed turret was left as a normal auto turret.");
                return;
            }

            replacement.OwnerID = ownerId;
            replacement.skinID = original.skinID;
            replacement.Spawn();

            ApplySentryRuntimeState(replacement, authIds);
            ScheduleSentryRuntimeStateReapply(replacement, authIds);

            if (config.RemoveTurretSwitchChildEntities)
            {
                timer.Once(0.05f, () => RemoveSwitchChildren(replacement));
            }

            original.Kill();
            WarnOwner(ownerId, "Outpost Sentry Turret deployed.");
        }

        private void OnEntityKill(BaseNetworkable networkable)
        {
            var entity = networkable as BaseEntity;
            if (entity?.net == null)
            {
                return;
            }

            handledTurretIds.Remove(entity.net.ID.Value);
        }

        private string ResolvePrefab()
        {
            if (!string.IsNullOrWhiteSpace(resolvedSentryPrefab))
            {
                var cachedPrefab = GameManager.server.FindPrefab(resolvedSentryPrefab);
                if (cachedPrefab != null && PrefabMatchesRequiredShortName(cachedPrefab, resolvedSentryPrefab))
                {
                    return resolvedSentryPrefab;
                }

                resolvedSentryPrefab = null;
            }

            foreach (var prefab in config.OutpostSentryPrefabCandidates)
            {
                if (string.IsNullOrWhiteSpace(prefab))
                {
                    continue;
                }

                var prefabObject = GameManager.server.FindPrefab(prefab) ?? GameManager.server.FindPrefab(prefab.ToLowerInvariant());
                if (prefabObject == null || !PrefabMatchesRequiredShortName(prefabObject, prefab))
                {
                    continue;
                }

                resolvedSentryPrefab = prefab;
                return resolvedSentryPrefab;
            }

            var matches = FindSentryPrefabMatches();
            foreach (var prefab in matches)
            {
                var prefabObject = GameManager.server.FindPrefab(prefab);
                if (prefabObject == null || !PrefabMatchesRequiredShortName(prefabObject, prefab))
                {
                    continue;
                }

                resolvedSentryPrefab = prefab;
                PrintWarning($"Resolved Outpost sentry prefab from manifest: {DescribePrefab(resolvedSentryPrefab)}");
                return resolvedSentryPrefab;
            }

            return string.Empty;
        }

        private List<string> FindSentryPrefabMatches()
        {
            var matches = new List<string>();
            var candidates = config.OutpostSentryShortPrefabNameCandidates ?? new string[0];
            if (GameManifest.Current == null || GameManifest.Current.entities == null)
            {
                return matches;
            }

            foreach (var prefab in GameManifest.Current.entities)
            {
                if (string.IsNullOrWhiteSpace(prefab))
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    var shortName = NormalizeShortPrefabName(candidate);
                    if (string.IsNullOrWhiteSpace(shortName))
                    {
                        continue;
                    }

                    if (!PrefabMatchesShortName(prefab, shortName))
                    {
                        continue;
                    }

                    var prefabObject = GameManager.server.FindPrefab(prefab);
                    if (prefabObject == null || !PrefabMatchesRequiredShortName(prefabObject, prefab))
                    {
                        continue;
                    }

                    if (!matches.Contains(prefab))
                    {
                        matches.Add(prefab);
                    }

                    break;
                }
            }

            return matches;
        }

        private string DescribePrefab(string prefab)
        {
            var prefabObject = GameManager.server.FindPrefab(prefab);
            if (prefabObject == null)
            {
                return $"{prefab} | missing";
            }

            var baseEntity = prefabObject.GetComponent<BaseEntity>();
            var component = baseEntity == null ? "no BaseEntity" : baseEntity.GetType().Name;
            var shortName = GetPrefabShortName(prefabObject, prefab);
            return $"{prefab} | component={component} | short={shortName}";
        }

        private bool PrefabMatchesRequiredShortName(GameObject prefabObject, string prefabPath)
        {
            var requiredShortName = NormalizeShortPrefabName(config.RequiredSpawnedShortPrefabName);
            if (string.IsNullOrWhiteSpace(requiredShortName))
            {
                return true;
            }

            return string.Equals(GetPrefabShortName(prefabObject, prefabPath), requiredShortName, StringComparison.OrdinalIgnoreCase);
        }

        private string GetPrefabShortName(GameObject prefabObject, string prefabPath)
        {
            var baseEntity = prefabObject == null ? null : prefabObject.GetComponent<BaseEntity>();
            if (baseEntity != null && !string.IsNullOrWhiteSpace(baseEntity.ShortPrefabName))
            {
                return NormalizeShortPrefabName(baseEntity.ShortPrefabName);
            }

            return NormalizeShortPrefabName(prefabPath);
        }

        private string NormalizeShortPrefabName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var result = value.Trim().ToLowerInvariant();
            if (result.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - ".prefab".Length);
            }

            var lastSlash = result.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < result.Length - 1)
            {
                result = result.Substring(lastSlash + 1);
            }

            return result;
        }

        private bool PrefabMatchesShortName(string prefab, string shortName)
        {
            return prefab.EndsWith("/" + shortName + ".prefab", StringComparison.OrdinalIgnoreCase)
                || prefab.EndsWith("\\" + shortName + ".prefab", StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeShortPrefabName(prefab), shortName, StringComparison.OrdinalIgnoreCase);
        }

        private Item ResolveSourceItem(BasePlayer player, BaseEntity heldEntity)
        {
            var item = TryGetHeldItem(heldEntity);
            if (IsSentryItem(item))
            {
                return item;
            }

            item = player?.GetActiveItem();
            return IsSentryItem(item) ? item : null;
        }

        private Item TryGetHeldItem(BaseEntity heldEntity)
        {
            if (heldEntity == null)
            {
                return null;
            }

            try
            {
                var method = heldEntity.GetType().GetMethod("GetItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return method?.Invoke(heldEntity, null) as Item;
            }
            catch
            {
                return null;
            }
        }

        private bool IsVanillaAutoTurret(BaseEntity entity)
        {
            if (entity == null || entity is NPCAutoTurret || !(entity is AutoTurret))
            {
                return false;
            }

            return string.Equals(entity.PrefabName, VanillaAutoTurretPrefab, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entity.ShortPrefabName, VanillaAutoTurretShortPrefab, StringComparison.OrdinalIgnoreCase);
        }

        private bool HasSentryMarkerSkin(BaseEntity entity)
        {
            return entity != null && config.SentryItem != null && config.SentryItem.Skin != 0 && entity.skinID == config.SentryItem.Skin;
        }

        private bool IsManagedNpcSentry(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed || !(entity is NPCAutoTurret) || !entity.OwnerID.IsSteamId())
            {
                return false;
            }

            var requiredShortName = NormalizeShortPrefabName(config.RequiredSpawnedShortPrefabName);
            return string.IsNullOrWhiteSpace(requiredShortName)
                || string.Equals(NormalizeShortPrefabName(entity.ShortPrefabName), requiredShortName, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSentryItem(Item item)
        {
            if (item?.info == null || config.SentryItem == null)
            {
                return false;
            }

            if (!string.Equals(item.info.shortname, config.SentryItem.Shortname, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (config.SentryItem.Skin != 0 && item.skin != config.SentryItem.Skin)
            {
                return false;
            }

            return !config.SentryItem.RequireDisplayNameMatch
                || string.Equals(item.name ?? "", config.SentryItem.DisplayName ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private void RemoveSwitchChildren(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed || entity.children == null || entity.children.Count == 0)
            {
                return;
            }

            var children = new List<BaseEntity>(entity.children);
            foreach (var child in children)
            {
                if (child == null || child.IsDestroyed)
                {
                    continue;
                }

                if (string.Equals(child.ShortPrefabName, "simpleswitch", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(child.PrefabName, "assets/prefabs/io/electric/switches/simpleswitch/simpleswitch.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    child.Kill();
                }
            }
        }

        private void ScheduleSentryRuntimeStateReapply(BaseEntity entity, HashSet<ulong> authIds)
        {
            var delays = config.AttackAllReapplyDelaysSeconds ?? new float[0];
            foreach (var delay in delays)
            {
                if (delay < 0f)
                {
                    continue;
                }

                timer.Once(delay, () => ApplySentryRuntimeState(entity, authIds));
            }
        }

        private void ApplySentryRuntimeState(BaseEntity entity, HashSet<ulong> authIds)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return;
            }

            var turret = entity as AutoTurret;
            if (turret == null)
            {
                return;
            }

            if (config.ClearAuthorizedPlayersOnSpawnedNpcSentries)
            {
                turret.authorizedPlayers.Clear();
                turret.target = null;
            }

            if (config.UseNormalTurretAuthorization)
            {
                ApplyAuthorization(turret, authIds);
                turret.target = null;
            }

            if (config.ForceAttackAll)
            {
                DisablePeacekeeperMode(turret);
            }

            turret.SendNetworkUpdate();
        }

        private HashSet<ulong> BuildAuthorizationSet(AutoTurret turret, ulong ownerId)
        {
            var authIds = new HashSet<ulong>();
            AddAuthorization(authIds, ownerId);

            if (turret != null)
            {
                foreach (var playerId in turret.authorizedPlayers)
                {
                    AddAuthorization(authIds, playerId);
                }

                var buildingPrivilege = turret.GetBuildingPrivilege();
                if (buildingPrivilege != null)
                {
                    foreach (var playerId in buildingPrivilege.authorizedPlayers)
                    {
                        AddAuthorization(authIds, playerId);
                    }
                }
            }

            AddTeamMembers(authIds, ownerId);
            AddFriends(authIds, ownerId);
            AddClanMembers(authIds, ownerId);
            return authIds;
        }

        private void ApplyAuthorization(AutoTurret turret, HashSet<ulong> authIds)
        {
            if (authIds == null)
            {
                return;
            }

            foreach (var playerId in authIds)
            {
                AddAuthorization(turret.authorizedPlayers, playerId);
            }
        }

        private void AddAuthorization(HashSet<ulong> authIds, ulong playerId)
        {
            if (playerId == 0)
            {
                return;
            }

            authIds.Add(playerId);
        }

        private void AddTeamMembers(HashSet<ulong> authIds, ulong ownerId)
        {
            var owner = BasePlayer.FindAwakeOrSleeping(ownerId.ToString());
            if (owner == null || owner.currentTeam == 0)
            {
                return;
            }

            var team = RelationshipManager.ServerInstance.FindTeam(owner.currentTeam);
            if (team == null || team.members == null)
            {
                return;
            }

            foreach (var memberId in team.members)
            {
                AddAuthorization(authIds, memberId);
            }
        }

        private void AddFriends(HashSet<ulong> authIds, ulong ownerId)
        {
            if (Friends == null)
            {
                return;
            }

            var friends = Friends.Call("GetFriends", ownerId);
            var ulongFriends = friends as ulong[];
            if (ulongFriends != null)
            {
                foreach (var friendId in ulongFriends)
                {
                    AddAuthorization(authIds, friendId);
                }

                return;
            }

            var stringFriends = friends as string[];
            if (stringFriends == null)
            {
                return;
            }

            foreach (var friendId in stringFriends)
            {
                ulong parsed;
                if (ulong.TryParse(friendId, out parsed))
                {
                    AddAuthorization(authIds, parsed);
                }
            }
        }

        private void AddClanMembers(HashSet<ulong> authIds, ulong ownerId)
        {
            if (Clans == null)
            {
                return;
            }

            var rebornMembers = Clans.Call("GetClanMembers", ownerId) as List<string>;
            if (rebornMembers != null)
            {
                AddStringIds(authIds, rebornMembers);
                return;
            }

            var clanName = Clans.Call("GetClanOf", ownerId) as string;
            if (string.IsNullOrWhiteSpace(clanName))
            {
                return;
            }

            var clan = Clans.Call("GetClan", clanName) as JObject;
            var members = clan?.GetValue("members") as JArray;
            if (members == null)
            {
                return;
            }

            foreach (var member in members)
            {
                ulong memberId;
                if (ulong.TryParse(member.ToString(), out memberId))
                {
                    AddAuthorization(authIds, memberId);
                }
            }
        }

        private void AddStringIds(HashSet<ulong> authIds, IEnumerable<string> playerIds)
        {
            foreach (var playerId in playerIds)
            {
                ulong parsed;
                if (ulong.TryParse(playerId, out parsed))
                {
                    AddAuthorization(authIds, parsed);
                }
            }
        }

        private void DisablePeacekeeperMode(AutoTurret turret)
        {
            InvokeBooleanMethod(turret, "SetPeacekeepermode", false);
            InvokeBooleanMethod(turret, "SetPeacekeeperMode", false);
            SetBooleanMember(turret, "peacekeepermode", false);
            SetBooleanMember(turret, "peacekeeperMode", false);
            SetBooleanMember(turret, "Peacekeepermode", false);
            SetBooleanMember(turret, "PeacekeeperMode", false);
        }

        private void InvokeBooleanMethod(object instance, string methodName, bool value)
        {
            var type = instance.GetType();
            while (type != null)
            {
                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                    {
                        method.Invoke(instance, new object[] { value });
                        return;
                    }
                }

                type = type.BaseType;
            }
        }

        private void SetBooleanMember(object instance, string memberName, bool value)
        {
            var type = instance.GetType();
            while (type != null)
            {
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == typeof(bool))
                {
                    field.SetValue(instance, value);
                    return;
                }

                var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
                {
                    property.SetValue(instance, value, null);
                    return;
                }

                type = type.BaseType;
            }
        }

        private GiveResult GiveSentryTurretsDetailed(BasePlayer player, int amount)
        {
            var result = new GiveResult();
            for (var i = 0; i < amount; i++)
            {
                var item = CreateSentryItem();
                if (item == null)
                {
                    return result;
                }

                if (player.inventory.GiveItem(item))
                {
                    result.Given++;
                    continue;
                }

                item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                result.Given++;
                result.Dropped++;
            }

            return result;
        }

        private Item CreateSentryItem()
        {
            var item = ItemManager.CreateByName(config.SentryItem.Shortname, 1, config.SentryItem.Skin);
            if (item == null)
            {
                PrintWarning($"Could not create sentry item '{config.SentryItem.Shortname}'.");
                return null;
            }

            item.name = config.SentryItem.DisplayName;
            item.MarkDirty();
            return item;
        }

        private bool CanUseAdminCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null || arg.Connection.authLevel > 0 || arg.IsAdmin)
            {
                return true;
            }

            var player = arg.Connection.player as BasePlayer;
            return player != null && permission.UserHasPermission(player.UserIDString, AdminPermission);
        }

        private BasePlayer FindPlayer(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            ulong id;
            if (ulong.TryParse(value, out id))
            {
                return BasePlayer.FindAwakeOrSleeping(id.ToString());
            }

            value = value.ToLowerInvariant();
            foreach (var player in BasePlayer.allPlayerList)
            {
                if (player.displayName != null && player.displayName.ToLowerInvariant().Contains(value))
                {
                    return player;
                }
            }

            return null;
        }

        private void WarnOwner(ulong ownerId, string message)
        {
            var player = BasePlayer.FindByID(ownerId);
            if (player != null && player.IsConnected)
            {
                player.ChatMessage($"{config.ChatPrefix} {message}");
                return;
            }

            Puts(message);
        }

        private class GiveResult
        {
            public int Given;
            public int Dropped;
        }
    }
}
