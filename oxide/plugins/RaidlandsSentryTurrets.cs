using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Rust;
using UnityEngine;
using RaycastHit = UnityEngine.RaycastHit;

namespace Oxide.Plugins
{
    [Info("RaidlandsSentryTurrets", "Raidlands", "1.0.12")]
    [Description("Converts specially named auto turret items into player-deployed Outpost sentry turrets.")]
    public class RaidlandsSentryTurrets : RustPlugin
    {
        private const string AdminPermission = "raidlandssentryturrets.admin";
        private const string LegacyAdminPermission = "raidlands.sentryturrets.admin";
        private const string RaidlandsEventsDataFileName = "RaidlandsEvents";
        private const string RemoverToolRefundKey = "raidlands.sentryturret";
        private const string VanillaAutoTurretPrefab = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        private const string VanillaAutoTurretShortPrefab = "autoturret_deployed";
        private const string SimpleSwitchPrefab = "assets/prefabs/io/electric/switches/simpleswitch/simpleswitch.prefab";
        private const string SimpleSwitchShortPrefab = "simpleswitch";
        private const string OwnerlessManageConfirmation = "confirm";

        [PluginReference]
        private Plugin Clans, Friends, RemoverTool, TurretSwitches;

        private Configuration config;
        private readonly HashSet<ulong> handledTurretIds = new HashSet<ulong>();
        private readonly HashSet<ulong> eventManagedSentryIds = new HashSet<ulong>();
        private readonly Dictionary<ulong, ulong> sentrySupportEntityIds = new Dictionary<ulong, ulong>();
        private readonly HashSet<ulong> pendingSupportDropSentryIds = new HashSet<ulong>();
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

            [JsonProperty("Allow Automatic Ownerless CopyPaste Sentry Fallback")]
            public bool AllowAutomaticOwnerlessCopyPasteSentryFallback = false;

            [JsonProperty("Repair Unmanaged Ownerless Sentries On Load")]
            public bool RepairUnmanagedOwnerlessSentriesOnLoad = true;

            [JsonProperty("Post Spawn Attack All Reapply Delays Seconds")]
            public float[] AttackAllReapplyDelaysSeconds = { 0.05f, 0.25f, 1f, 3f };

            [JsonProperty("Sentry Tuning")]
            public SentryTuning SentryTuning = new SentryTuning();

            [JsonProperty("Remove TurretSwitches Child Entities From Spawned NPC Sentries")]
            public bool RemoveTurretSwitchChildEntities = false;

            [JsonProperty("Enable TurretSwitches Compatibility")]
            public bool EnableTurretSwitchesCompatibility = true;

            [JsonProperty("Default New Sentries Online")]
            public bool DefaultNewSentriesOnline = false;

            [JsonProperty("Enable Hammer Pickup")]
            public bool EnableHammerPickup = true;

            [JsonProperty("Enable RemoverTool Integration")]
            public bool EnableRemoverToolIntegration = true;

            [JsonProperty("Return Item On Hammer Pickup")]
            public bool ReturnItemOnHammerPickup = true;

            [JsonProperty("Return Item When RemoverTool Refunds")]
            public bool ReturnItemWhenRemoverToolRefunds = true;

            [JsonProperty("Support Loss Drop")]
            public SupportLossDropSettings SupportLossDrop = new SupportLossDropSettings();

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

        private class SentryTuning
        {
            [JsonProperty("Enable Range Tuning")]
            public bool EnableRangeTuning = true;

            [JsonProperty("Use Vanilla Auto Turret Prefab Range As Base")]
            public bool UseVanillaAutoTurretPrefabRangeAsBase = true;

            [JsonProperty("Fallback Normal Auto Turret Range Metres")]
            public float FallbackNormalAutoTurretRangeMetres = 30f;

            [JsonProperty("Range Multiplier")]
            public float RangeMultiplier = 1.3f;

            [JsonProperty("Enable Durability Tuning")]
            public bool EnableDurabilityTuning = true;

            [JsonProperty("Effective Max Health")]
            public float EffectiveMaxHealth = 500f;

            [JsonProperty("Incoming Damage Multiplier")]
            public float IncomingDamageMultiplier = 1f;

            [JsonProperty("Use Manual Damage Handling")]
            public bool UseManualDamageHandling = true;

            [JsonProperty("Clear Native Base Protection")]
            public bool ClearNativeBaseProtection = true;

            [JsonProperty("Ignore Pure Decay Damage")]
            public bool IgnorePureDecayDamage = true;

            [JsonProperty("Log Damage Events")]
            public bool LogDamageEvents = false;
        }

        private class SupportLossDropSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Drop Delay Seconds")]
            public float DropDelaySeconds = 300f;

            [JsonProperty("Check Interval Seconds")]
            public float CheckIntervalSeconds = 10f;

            [JsonProperty("Support Raycast Distance Metres")]
            public float SupportRaycastDistanceMetres = 2f;

            [JsonProperty("Drop Item On Support Loss")]
            public bool DropItemOnSupportLoss = true;

            [JsonProperty("Log Support Loss Drops")]
            public bool LogSupportLossDrops = true;
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

            if (config.SentryTuning == null)
            {
                config.SentryTuning = new SentryTuning();
            }

            ValidateSentryTuning(config.SentryTuning);

            if (config.SupportLossDrop == null)
            {
                config.SupportLossDrop = new SupportLossDropSettings();
            }

            ValidateSupportLossDropSettings(config.SupportLossDrop);

            if (config.UseNormalTurretAuthorization)
            {
                config.ClearAuthorizedPlayersOnSpawnedNpcSentries = false;
            }

            if (config.EnableTurretSwitchesCompatibility)
            {
                config.RemoveTurretSwitchChildEntities = false;
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
                if (!IsPlayerOwnedManagedNpcSentry(entity))
                {
                    continue;
                }

                var authIds = BuildAuthorizationSet(entity as AutoTurret, entity.OwnerID);
                ApplySentryRuntimeState(entity, authIds);
                ScheduleSentryRuntimeStateReapply(entity, authIds);
                ConfigurePickup(entity);
                TrackSentrySupport(entity);
            }

            var restoredEventSentries = ManagePersistedRaidlandsEventSentries();
            if (restoredEventSentries > 0)
            {
                Puts($"Restored durability tuning for {restoredEventSentries} active RaidlandsEvents sentry turret(s).");
            }

            if (config.AllowAutomaticOwnerlessCopyPasteSentryFallback)
            {
                var restoredCopyPasteSentries = ManageUntrackedCopyPasteSentries();
                if (restoredCopyPasteSentries > 0)
                {
                    Puts($"Restored durability tuning for {restoredCopyPasteSentries} ownerless CopyPaste sentry turret(s).");
                }
            }

            if (config.RepairUnmanagedOwnerlessSentriesOnLoad)
            {
                var repairedOwnerlessSentries = RepairUnmanagedOwnerlessSentries();
                if (repairedOwnerlessSentries > 0)
                {
                    Puts($"Repaired {repairedOwnerlessSentries} unmanaged ownerless sentry turret(s) back to native peacekeeper behavior.");
                }
            }

            StartSupportLossMonitor();
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

        [ConsoleCommand("raidlands.sentry.scan")]
        private void CCmdScanSentries(ConsoleSystem.Arg arg)
        {
            if (!CanUseAdminCommand(arg))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            var includeAll = arg.Args != null && arg.Args.Length > 0 && string.Equals(arg.GetString(0), "all", StringComparison.OrdinalIgnoreCase);
            var lines = new List<string>
            {
                $"RaidlandsSentryTurrets scan v1.0.12 hp={config.SentryTuning?.EffectiveMaxHealth:0.##} rangeMultiplier={config.SentryTuning?.RangeMultiplier:0.##} autoOwnerlessFallback={config.AllowAutomaticOwnerlessCopyPasteSentryFallback}"
            };

            var total = 0;
            var managed = 0;
            var copied = 0;
            foreach (var serverEntity in BaseNetworkable.serverEntities)
            {
                var entity = serverEntity as BaseEntity;
                if (!LooksLikeConfiguredSentryPrefab(entity))
                {
                    continue;
                }

                total++;
                if (IsManagedNpcSentry(entity))
                {
                    managed++;
                }

                if (ShouldManageUntrackedCopyPasteSentry(entity))
                {
                    copied++;
                }

                if (includeAll || lines.Count < 30)
                {
                    lines.Add(DescribeSentryForScan(entity));
                }
            }

            lines.Insert(1, $"total={total} managed={managed} ownerlessSwitchCandidatesManualOnly={copied}");
            arg.ReplyWith(string.Join("\n", lines));
        }

        [ConsoleCommand("raidlands.sentry.manage")]
        private void CCmdManageSentry(ConsoleSystem.Arg arg)
        {
            if (!CanUseAdminCommand(arg))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                arg.ReplyWith("Usage: raidlands.sentry.manage <entityId|ownerless-switch|ownerless-all> [confirm]");
                return;
            }

            var mode = arg.GetString(0);
            if (string.Equals(mode, "ownerless-switch", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasOwnerlessManageConfirmation(arg))
                {
                    ReplyOwnerlessConfirmationRequired(arg, "ownerless-switch");
                    return;
                }

                arg.ReplyWith($"Managed {ManageUntrackedCopyPasteSentries()} ownerless CopyPaste sentry turret(s).");
                return;
            }

            if (string.Equals(mode, "ownerless-all", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasOwnerlessManageConfirmation(arg))
                {
                    ReplyOwnerlessConfirmationRequired(arg, "ownerless-all");
                    return;
                }

                arg.ReplyWith($"Managed {ManageOwnerlessSentries()} ownerless configured sentry turret(s).");
                return;
            }

            ulong entityId;
            if (!ulong.TryParse(mode, out entityId))
            {
                arg.ReplyWith("Usage: raidlands.sentry.manage <entityId|ownerless-switch|ownerless-all> [confirm]");
                return;
            }

            var allowOwnerless = HasOwnerlessManageConfirmation(arg);
            if (ManageEventSentry(entityId, allowOwnerless))
            {
                arg.ReplyWith($"Managed sentry entity {entityId}.");
                return;
            }

            var entity = FindServerEntity(entityId);
            arg.ReplyWith(!allowOwnerless && IsOwnerlessConfiguredSentry(entity)
                ? $"Sentry entity {entityId} is ownerless. Native Outpost sentries are ownerless too, so only RaidlandsEvents tracked IDs are managed automatically. If this is a verified pasted event sentry, run: raidlands.sentry.manage {entityId} confirm"
                : $"Could not manage sentry entity {entityId}. Run raidlands.sentry.scan all and verify the entity id/prefab.");
        }

        [ConsoleCommand("raidlands.sentry.repairnative")]
        private void CCmdRepairNativeSentries(ConsoleSystem.Arg arg)
        {
            if (!CanUseAdminCommand(arg))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            arg.ReplyWith($"Repaired {RepairUnmanagedOwnerlessSentries()} unmanaged ownerless sentry turret(s) back to native peacekeeper behavior.");
        }

        private bool HasOwnerlessManageConfirmation(ConsoleSystem.Arg arg)
        {
            return arg?.Args != null
                && arg.Args.Length > 1
                && string.Equals(arg.GetString(1), OwnerlessManageConfirmation, StringComparison.OrdinalIgnoreCase);
        }

        private void ReplyOwnerlessConfirmationRequired(ConsoleSystem.Arg arg, string mode)
        {
            arg.ReplyWith($"Refusing broad ownerless sentry management without '{OwnerlessManageConfirmation}'. Native Outpost sentries are ownerless too. If you verified these are pasted event sentries, run: raidlands.sentry.manage {mode} {OwnerlessManageConfirmation}");
        }

        [HookMethod(nameof(API_RaidlandsManageEventSentries))]
        public int API_RaidlandsManageEventSentries(object rawEntityIds)
        {
            if (config == null || !config.Enabled)
            {
                return 0;
            }

            var managed = 0;
            foreach (var entityId in NormalizeEntityIds(rawEntityIds))
            {
                if (ManageEventSentry(entityId, true))
                {
                    managed++;
                }
            }

            return managed;
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

        private object OnRaidlandsCreateKitItem(string shortname, int amount, ulong skin, string displayName)
        {
            if (!IsSentryKitItem(shortname, skin, displayName))
            {
                return null;
            }

            return CreateSentryItem(Math.Max(1, amount));
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

        private object OnRemovableEntityInfo(BaseEntity entity, BasePlayer player)
        {
            if (!config.EnableRemoverToolIntegration || !IsPlayerOwnedManagedNpcSentry(entity))
            {
                return null;
            }

            var info = new Dictionary<string, object>
            {
                ["DisplayName"] = config.SentryItem.DisplayName,
                ["ImageId"] = config.SentryItem.Shortname,
                ["Price"] = new Dictionary<string, object>()
            };

            if (config.ReturnItemWhenRemoverToolRefunds)
            {
                info["Refund"] = CreateRemoverToolItemMap(1);
            }

            return info;
        }

        private object CanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            if (!config.EnableHammerPickup || !IsPlayerOwnedManagedNpcSentry(entity))
            {
                return null;
            }

            TryPickupSentry(player, entity, true);
            return false;
        }

        private object OnHammerHit(BasePlayer player, HitInfo info)
        {
            if (!config.EnableHammerPickup || player == null || info?.HitEntity == null)
            {
                return null;
            }

            var entity = info.HitEntity as BaseEntity;
            if (!IsPlayerOwnedManagedNpcSentry(entity) || !IsHammer(player.GetActiveItem()) || IsRemoverToolActive(player))
            {
                return null;
            }

            TryPickupSentry(player, entity, true);
            return false;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (!config.Enabled || entity == null || info?.damageTypes == null || !IsManagedNpcSentry(entity))
            {
                return null;
            }

            var tuning = config.SentryTuning;
            if (tuning == null || !tuning.EnableDurabilityTuning || !tuning.UseManualDamageHandling)
            {
                return null;
            }

            var incomingDamage = info.damageTypes.Total();
            if (incomingDamage <= 0f)
            {
                return null;
            }

            if (tuning.IgnorePureDecayDamage && IsPureDecayDamage(info))
            {
                info.damageTypes.ScaleAll(0f);
                return true;
            }

            ConfigureSentryDurability(entity, false);

            var damage = incomingDamage * tuning.IncomingDamageMultiplier;
            if (damage <= 0f)
            {
                info.damageTypes.ScaleAll(0f);
                return true;
            }

            var oldHealth = entity.Health();
            var newHealth = oldHealth - damage;
            if (tuning.LogDamageEvents)
            {
                Puts($"Sentry damage: weapon={DescribeDamageSource(info)} incoming={incomingDamage:0.##} applied={damage:0.##} health={Mathf.Max(0f, newHealth):0.##}/{entity.MaxHealth():0.##}");
            }

            if (newHealth <= 0f)
            {
                entity.Die(info);
                return true;
            }

            entity.SetHealth(newHealth);
            entity.SendNetworkUpdate();
            return true;
        }

        private object OnRemovableEntityGiveRefund(BaseEntity targetEntity, BasePlayer player, string itemName, int amount, long skinId)
        {
            if (!string.Equals(itemName, RemoverToolRefundKey, StringComparison.OrdinalIgnoreCase) || !IsPlayerOwnedManagedNpcSentry(targetEntity) || player == null)
            {
                return null;
            }

            GiveSentryTurretsDetailed(player, Math.Max(1, amount));
            return null;
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

            var placerPosition = player.transform.position;
            var ownerId = player.userID;
            timer.Once(config.ReplacementDelaySeconds, () => ReplaceTurret(ownerId, placerPosition, entity));
        }

        private void ReplaceTurret(ulong ownerId, Vector3 placerPosition, BaseEntity original)
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

            InitializeNewSentryDurability(replacement as BaseCombatEntity);
            ApplySentryRuntimeState(replacement, authIds);
            ScheduleSentryRuntimeStateReapply(replacement, authIds);
            ConfigurePickup(replacement);
            SetTurretPowerState(replacement as AutoTurret, config.DefaultNewSentriesOnline);
            RepositionTurretSwitch(replacement, placerPosition);
            timer.Once(0.1f, () => RepositionTurretSwitch(replacement, placerPosition));

            if (config.RemoveTurretSwitchChildEntities)
            {
                timer.Once(0.05f, () => RemoveSwitchChildren(replacement));
            }

            original.Kill();
            TrackSentrySupport(replacement);
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
            eventManagedSentryIds.Remove(entity.net.ID.Value);
            sentrySupportEntityIds.Remove(entity.net.ID.Value);
            pendingSupportDropSentryIds.Remove(entity.net.ID.Value);
        }

        private void OnRaidlandsTrackedPasteFinished(string trackingId, string filename, List<ulong> pastedEntityIds, object player, Vector3 startPos)
        {
            API_RaidlandsManageEventSentries(pastedEntityIds);
        }

        private void OnEntitySpawned(BaseNetworkable networkable)
        {
            if (!config.Enabled || !config.AllowAutomaticOwnerlessCopyPasteSentryFallback)
            {
                return;
            }

            var entity = networkable as BaseEntity;
            if (!LooksLikeConfiguredSentryPrefab(entity))
            {
                return;
            }

            timer.Once(0.25f, () => TryManageUntrackedCopyPasteSentry(entity));
            timer.Once(1.5f, () => TryManageUntrackedCopyPasteSentry(entity));
            timer.Once(3f, () => TryManageUntrackedCopyPasteSentry(entity));
        }

        private bool ManageEventSentry(ulong entityId, bool allowOwnerlessSentry)
        {
            if (entityId == 0)
            {
                return false;
            }

            var entity = FindServerEntity(entityId);
            if (!IsNpcSentryPrefab(entity))
            {
                return false;
            }

            if (!entity.OwnerID.IsSteamId() && !allowOwnerlessSentry)
            {
                return false;
            }

            eventManagedSentryIds.Add(entityId);
            var authIds = BuildAuthorizationSet(entity as AutoTurret, entity.OwnerID);
            ApplySentryRuntimeState(entity, authIds);
            ScheduleSentryRuntimeStateReapply(entity, authIds);
            TrackSentrySupport(entity);
            return true;
        }

        private int ManagePersistedRaidlandsEventSentries()
        {
            try
            {
                var storedData = Interface.Oxide.DataFileSystem.ReadObject<JObject>(RaidlandsEventsDataFileName);
                var activeRaidBases = storedData?["ActiveRaidBases"] as JObject;
                if (activeRaidBases == null)
                {
                    return 0;
                }

                var ids = new List<ulong>();
                var seen = new HashSet<ulong>();
                foreach (var activeProperty in activeRaidBases.Properties())
                {
                    var active = activeProperty.Value as JObject;
                    if (active == null)
                    {
                        continue;
                    }

                    var status = active["Status"]?.ToString();
                    if (string.Equals(status, "cleaning", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var entityIds = active["EntityIds"] as JArray;
                    if (entityIds == null)
                    {
                        continue;
                    }

                    foreach (var token in entityIds)
                    {
                        TryAddEntityId(ids, seen, token?.ToString());
                    }
                }

                return API_RaidlandsManageEventSentries(ids);
            }
            catch
            {
                return 0;
            }
        }

        private int ManageUntrackedCopyPasteSentries()
        {
            var managed = 0;
            foreach (var serverEntity in BaseNetworkable.serverEntities)
            {
                if (TryManageUntrackedCopyPasteSentry(serverEntity as BaseEntity))
                {
                    managed++;
                }
            }

            return managed;
        }

        private int ManageOwnerlessSentries()
        {
            var managed = 0;
            foreach (var serverEntity in BaseNetworkable.serverEntities)
            {
                var entity = serverEntity as BaseEntity;
                if (entity?.net == null || !IsNpcSentryPrefab(entity) || entity.OwnerID.IsSteamId())
                {
                    continue;
                }

                if (ManageEventSentry(entity.net.ID.Value, true))
                {
                    managed++;
                }
            }

            return managed;
        }

        private bool TryManageUntrackedCopyPasteSentry(BaseEntity entity)
        {
            if (!ShouldManageUntrackedCopyPasteSentry(entity) || entity.net == null)
            {
                return false;
            }

            return ManageEventSentry(entity.net.ID.Value, true);
        }

        private bool ShouldManageUntrackedCopyPasteSentry(BaseEntity entity)
        {
            return IsOwnerlessConfiguredSentry(entity)
                && !IsEventManagedSentry(entity)
                && HasSimpleSwitchChild(entity);
        }

        private int RepairUnmanagedOwnerlessSentries()
        {
            var repaired = 0;
            foreach (var serverEntity in BaseNetworkable.serverEntities)
            {
                var entity = serverEntity as BaseEntity;
                if (!ShouldRepairUnmanagedOwnerlessSentry(entity))
                {
                    continue;
                }

                if (RepairUnmanagedOwnerlessSentry(entity))
                {
                    repaired++;
                }
            }

            return repaired;
        }

        private bool ShouldRepairUnmanagedOwnerlessSentry(BaseEntity entity)
        {
            return IsOwnerlessConfiguredSentry(entity) && !IsEventManagedSentry(entity);
        }

        private bool RepairUnmanagedOwnerlessSentry(BaseEntity entity)
        {
            var turret = entity as AutoTurret;
            var combat = entity as BaseCombatEntity;
            if (turret == null && combat == null)
            {
                return false;
            }

            if (turret != null)
            {
                EnablePeacekeeperMode(turret);
                turret.target = null;
                RestoreSentryRangeFromPrefab(entity, turret);
            }

            if (combat != null)
            {
                RestoreSentryDurabilityFromPrefab(entity, combat);
            }

            entity.SendNetworkUpdate();
            return true;
        }

        private void StartSupportLossMonitor()
        {
            var settings = config.SupportLossDrop;
            if (settings == null || !settings.Enabled)
            {
                return;
            }

            timer.Every(settings.CheckIntervalSeconds, CheckTrackedSentrySupports);
        }

        private void CheckTrackedSentrySupports()
        {
            if (config == null || !config.Enabled || config.SupportLossDrop == null || !config.SupportLossDrop.Enabled)
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

                TrackSentrySupport(entity);
                if (HasTrackedSupport(entity) && !IsTrackedSupportPresent(entity))
                {
                    ScheduleSupportLossDrop(entity);
                }
            }
        }

        private void TrackSentrySupport(BaseEntity entity)
        {
            if (entity?.net == null || config.SupportLossDrop == null || !config.SupportLossDrop.Enabled)
            {
                return;
            }

            var sentryId = entity.net.ID.Value;
            if (sentrySupportEntityIds.ContainsKey(sentryId))
            {
                return;
            }

            var support = FindSupportEntityBelow(entity);
            if (support?.net == null)
            {
                return;
            }

            sentrySupportEntityIds[sentryId] = support.net.ID.Value;
        }

        private bool HasTrackedSupport(BaseEntity entity)
        {
            return entity?.net != null && sentrySupportEntityIds.ContainsKey(entity.net.ID.Value);
        }

        private bool IsTrackedSupportPresent(BaseEntity entity)
        {
            if (entity?.net == null)
            {
                return false;
            }

            ulong supportId;
            if (!sentrySupportEntityIds.TryGetValue(entity.net.ID.Value, out supportId) || supportId == 0)
            {
                return false;
            }

            var currentSupport = FindSupportEntityBelow(entity);
            return currentSupport?.net != null && currentSupport.net.ID.Value == supportId;
        }

        private BaseEntity FindSupportEntityBelow(BaseEntity entity)
        {
            if (entity == null || entity.transform == null || config.SupportLossDrop == null)
            {
                return null;
            }

            var origin = entity.transform.position + Vector3.up * 0.35f;
            var hits = Physics.RaycastAll(origin, Vector3.down, config.SupportLossDrop.SupportRaycastDistanceMetres);
            if (hits == null || hits.Length == 0)
            {
                return null;
            }

            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                var hitEntity = GetEntityFromHit(hit);
                if (hitEntity == null || hitEntity == entity || hitEntity.IsDestroyed || hitEntity.net == null)
                {
                    continue;
                }

                if (hitEntity.GetParentEntity() == entity || entity.children != null && entity.children.Contains(hitEntity))
                {
                    continue;
                }

                return hitEntity;
            }

            return null;
        }

        private BaseEntity GetEntityFromHit(RaycastHit hit)
        {
            var hitEntity = hit.GetEntity();
            if (hitEntity != null)
            {
                return hitEntity;
            }

            return hit.collider == null ? null : hit.collider.GetComponentInParent<BaseEntity>();
        }

        private void ScheduleSupportLossDrop(BaseEntity entity)
        {
            if (entity?.net == null)
            {
                return;
            }

            var sentryId = entity.net.ID.Value;
            if (!pendingSupportDropSentryIds.Add(sentryId))
            {
                return;
            }

            var delay = config.SupportLossDrop.DropDelaySeconds;
            timer.Once(delay, () => DropSentryIfSupportStillMissing(sentryId));
        }

        private void DropSentryIfSupportStillMissing(ulong sentryId)
        {
            pendingSupportDropSentryIds.Remove(sentryId);

            var entity = FindServerEntity(sentryId);
            if (!IsManagedNpcSentry(entity) || !HasTrackedSupport(entity) || IsTrackedSupportPresent(entity))
            {
                return;
            }

            if (config.SupportLossDrop.DropItemOnSupportLoss)
            {
                var item = CreateSentryItem();
                if (item != null)
                {
                    item.Drop(entity.transform.position + Vector3.up * 0.5f, Vector3.up * 0.25f);
                }
            }

            if (config.SupportLossDrop.LogSupportLossDrops)
            {
                Puts($"Dropped unsupported Outpost Sentry Turret entity {sentryId} after {config.SupportLossDrop.DropDelaySeconds:0.##} second support-loss delay.");
            }

            entity.Kill(BaseNetworkable.DestroyMode.None);
        }

        private List<ulong> NormalizeEntityIds(object rawEntityIds)
        {
            var ids = new List<ulong>();
            var seen = new HashSet<ulong>();
            if (rawEntityIds == null)
            {
                return ids;
            }

            var ulongIds = rawEntityIds as IEnumerable<ulong>;
            if (ulongIds != null)
            {
                foreach (var entityId in ulongIds)
                {
                    AddEntityId(ids, seen, entityId);
                }

                return ids;
            }

            var objectIds = rawEntityIds as IEnumerable<object>;
            if (objectIds != null)
            {
                foreach (var value in objectIds)
                {
                    TryAddEntityId(ids, seen, value);
                }

                return ids;
            }

            TryAddEntityId(ids, seen, rawEntityIds);
            return ids;
        }

        private bool TryAddEntityId(List<ulong> ids, HashSet<ulong> seen, object value)
        {
            ulong entityId;
            if (!TryGetEntityId(value, out entityId))
            {
                return false;
            }

            return AddEntityId(ids, seen, entityId);
        }

        private bool AddEntityId(List<ulong> ids, HashSet<ulong> seen, ulong entityId)
        {
            if (entityId == 0 || !seen.Add(entityId))
            {
                return false;
            }

            ids.Add(entityId);
            return true;
        }

        private bool TryGetEntityId(object value, out ulong entityId)
        {
            entityId = 0;
            if (value == null)
            {
                return false;
            }

            if (value is ulong)
            {
                entityId = (ulong)value;
                return entityId != 0;
            }

            if (value is NetworkableId)
            {
                entityId = ((NetworkableId)value).Value;
                return entityId != 0;
            }

            try
            {
                entityId = Convert.ToUInt64(value);
                return entityId != 0;
            }
            catch
            {
                return false;
            }
        }

        private BaseEntity FindServerEntity(ulong entityId)
        {
            return entityId == 0 ? null : BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as BaseEntity;
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
            if (!IsNpcSentryPrefab(entity))
            {
                return false;
            }

            return entity.OwnerID.IsSteamId() || IsEventManagedSentry(entity);
        }

        private bool IsPlayerOwnedManagedNpcSentry(BaseEntity entity)
        {
            return IsNpcSentryPrefab(entity) && entity.OwnerID.IsSteamId();
        }

        private bool IsOwnerlessConfiguredSentry(BaseEntity entity)
        {
            return IsNpcSentryPrefab(entity) && entity.net != null && !entity.OwnerID.IsSteamId();
        }

        private bool IsNpcSentryPrefab(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed || !(entity is BaseCombatEntity))
            {
                return false;
            }

            return LooksLikeConfiguredSentryPrefab(entity);
        }

        private bool LooksLikeConfiguredSentryPrefab(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return false;
            }

            var requiredShortName = NormalizeShortPrefabName(config.RequiredSpawnedShortPrefabName);
            return string.IsNullOrWhiteSpace(requiredShortName)
                || string.Equals(NormalizeShortPrefabName(entity.ShortPrefabName), requiredShortName, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsEventManagedSentry(BaseEntity entity)
        {
            return entity?.net != null && eventManagedSentryIds.Contains(entity.net.ID.Value);
        }

        private bool HasSimpleSwitchChild(BaseEntity entity)
        {
            if (entity?.children == null || entity.children.Count == 0)
            {
                return false;
            }

            foreach (var child in entity.children)
            {
                if (IsSimpleSwitch(child))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSimpleSwitch(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return false;
            }

            return string.Equals(entity.ShortPrefabName, SimpleSwitchShortPrefab, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entity.PrefabName, SimpleSwitchPrefab, StringComparison.OrdinalIgnoreCase);
        }

        private string DescribeSentryForScan(BaseEntity entity)
        {
            if (entity == null)
            {
                return "null sentry";
            }

            var combat = entity as BaseCombatEntity;
            var turret = entity as AutoTurret;
            var entityId = entity.net == null ? 0UL : entity.net.ID.Value;
            var health = combat == null ? "n/a" : $"{combat.Health():0.##}/{combat.MaxHealth():0.##}";
            var range = turret == null ? "n/a" : turret.sightRange.ToString("0.##");
            var pos = entity.transform == null ? "n/a" : $"{entity.transform.position.x:0.#},{entity.transform.position.y:0.#},{entity.transform.position.z:0.#}";
            return $"id={entityId} type={entity.GetType().Name} short={entity.ShortPrefabName} owner={entity.OwnerID} managed={IsManagedNpcSentry(entity)} event={IsEventManagedSentry(entity)} copySwitch={HasSimpleSwitchChild(entity)} hp={health} range={range} pos={pos} prefab={entity.PrefabName}";
        }

        private bool API_IsRaidlandsManagedSentry(BaseEntity entity)
        {
            return IsManagedNpcSentry(entity);
        }

        private ulong API_GetRaidlandsSentryOwner(BaseEntity entity)
        {
            return IsManagedNpcSentry(entity) ? entity.OwnerID : 0UL;
        }

        private bool API_ShouldBypassNativeSentryAuth(BaseEntity entity)
        {
            return IsPlayerOwnedManagedNpcSentry(entity);
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

        private bool IsSentryKitItem(string shortname, ulong skin, string displayName)
        {
            if (config.SentryItem == null || string.IsNullOrWhiteSpace(shortname))
            {
                return false;
            }

            if (!string.Equals(shortname, config.SentryItem.Shortname, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (config.SentryItem.Skin != 0 && skin != config.SentryItem.Skin)
            {
                return false;
            }

            return !config.SentryItem.RequireDisplayNameMatch
                || string.Equals(displayName ?? "", config.SentryItem.DisplayName ?? "", StringComparison.OrdinalIgnoreCase);
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

        private void ConfigurePickup(BaseEntity entity)
        {
            if (!config.EnableHammerPickup)
            {
                return;
            }

            var combatEntity = entity as BaseCombatEntity;
            if (combatEntity == null)
            {
                return;
            }

            var itemDefinition = ItemManager.FindItemDefinition(config.SentryItem.Shortname);
            if (itemDefinition == null)
            {
                return;
            }

            combatEntity.pickup.enabled = true;
            combatEntity.pickup.itemTarget = itemDefinition;
            combatEntity.SendNetworkUpdate();
        }

        private bool TryPickupSentry(BasePlayer player, BaseEntity entity, bool sendMessages)
        {
            if (player == null || !IsPlayerOwnedManagedNpcSentry(entity))
            {
                return false;
            }

            if (!CanManageSentry(player, entity))
            {
                if (sendMessages)
                {
                    player.ChatMessage($"{config.ChatPrefix} You are not authorized to pick up this Outpost Sentry Turret.");
                }

                return false;
            }

            if (config.ReturnItemOnHammerPickup)
            {
                var item = CreateSentryItem();
                if (item != null)
                {
                    if (!player.inventory.GiveItem(item))
                    {
                        item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                    }
                }
            }

            entity.Kill(BaseNetworkable.DestroyMode.None);
            if (sendMessages)
            {
                player.ChatMessage($"{config.ChatPrefix} Outpost Sentry Turret picked up.");
            }

            return true;
        }

        private bool CanManageSentry(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null)
            {
                return false;
            }

            if (player.IsAdmin || HasAdminPermission(player.UserIDString))
            {
                return true;
            }

            if (entity.OwnerID == player.userID)
            {
                return true;
            }

            var turret = entity as AutoTurret;
            return turret != null && turret.authorizedPlayers.Contains(player.userID);
        }

        private bool IsHammer(Item item)
        {
            return string.Equals(item?.info?.shortname, "hammer", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsRemoverToolActive(BasePlayer player)
        {
            if (player == null || RemoverTool == null)
            {
                return false;
            }

            var removeType = RemoverTool.Call("GetPlayerRemoveType", player) as string;
            if (!string.IsNullOrWhiteSpace(removeType))
            {
                return true;
            }

            foreach (var component in player.GetComponents<Component>())
            {
                if (component != null && string.Equals(component.GetType().Name, "ToolRemover", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private Dictionary<string, object> CreateRemoverToolItemMap(int amount)
        {
            return new Dictionary<string, object>
            {
                [RemoverToolRefundKey] = new Dictionary<string, object>
                {
                    ["Amount"] = amount,
                    ["SkinId"] = ToRemoverToolSkinId(config.SentryItem.Skin),
                    ["ImageId"] = config.SentryItem.Shortname,
                    ["DisplayName"] = config.SentryItem.DisplayName
                }
            };
        }

        private long ToRemoverToolSkinId(ulong skinId)
        {
            return skinId > long.MaxValue ? -1 : (long)skinId;
        }

        private void SetTurretPowerState(AutoTurret turret, bool online)
        {
            if (turret == null || turret.IsDestroyed)
            {
                return;
            }

            try
            {
                turret.SetFlag(IOEntity.Flag_HasPower, online);
                turret.SetFlag(BaseEntity.Flags.Reserved8, online);

                if ((turret.inputs?.Length ?? 0) > 0)
                {
                    turret.UpdateFromInput(online ? 11 : 0, 0);
                }

                if (online)
                {
                    turret.InitiateStartup();
                    turret.SetIsOnline(true);
                }
                else
                {
                    turret.InitiateShutdown();
                    turret.SetIsOnline(false);
                    turret.target = null;
                }

                turret.SendNetworkUpdate();
            }
            catch (Exception exception)
            {
                PrintWarning($"Could not set Outpost Sentry Turret power state: {exception.Message}");
            }
        }

        private void RepositionTurretSwitch(BaseEntity entity, Vector3 preferredWorldPosition)
        {
            if (!config.EnableTurretSwitchesCompatibility || TurretSwitches == null || entity == null || entity.IsDestroyed)
            {
                return;
            }

            TurretSwitches.Call("API_RepositionTurretSwitch", entity, preferredWorldPosition);
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

            ConfigureSentryTuning(turret);
            turret.SendNetworkUpdate();
        }

        private void ValidateSentryTuning(SentryTuning tuning)
        {
            if (tuning.FallbackNormalAutoTurretRangeMetres <= 0f)
            {
                tuning.FallbackNormalAutoTurretRangeMetres = 30f;
            }

            if (tuning.RangeMultiplier <= 0f)
            {
                tuning.RangeMultiplier = 1.3f;
            }

            if (tuning.EffectiveMaxHealth <= 0f)
            {
                tuning.EffectiveMaxHealth = 500f;
            }

            if (tuning.IncomingDamageMultiplier < 0f)
            {
                tuning.IncomingDamageMultiplier = 0f;
            }
        }

        private void ValidateSupportLossDropSettings(SupportLossDropSettings settings)
        {
            if (settings.DropDelaySeconds < 0f)
            {
                settings.DropDelaySeconds = 0f;
            }

            if (settings.CheckIntervalSeconds <= 0f)
            {
                settings.CheckIntervalSeconds = 10f;
            }

            if (settings.SupportRaycastDistanceMetres <= 0f)
            {
                settings.SupportRaycastDistanceMetres = 2f;
            }
        }

        private void ConfigureSentryTuning(AutoTurret turret)
        {
            if (turret == null || turret.IsDestroyed || config.SentryTuning == null)
            {
                return;
            }

            ConfigureSentryRange(turret);
            ConfigureSentryDurability(turret, true);
        }

        private void ConfigureSentryRange(AutoTurret turret)
        {
            var tuning = config.SentryTuning;
            if (tuning == null || !tuning.EnableRangeTuning)
            {
                return;
            }

            var range = GetConfiguredSentryRange();
            turret.sightRange = range;

            var collider = turret.targetTrigger == null ? null : turret.targetTrigger.GetComponent<SphereCollider>();
            if (collider != null)
            {
                collider.radius = range;
            }
        }

        private void RestoreSentryRangeFromPrefab(BaseEntity entity, AutoTurret turret)
        {
            var prefab = FindPrefabForEntity(entity);
            var prefabTurret = prefab == null ? null : prefab.GetComponent<AutoTurret>();
            if (turret == null || prefabTurret == null || prefabTurret.sightRange <= 0f)
            {
                return;
            }

            turret.sightRange = prefabTurret.sightRange;

            var collider = turret.targetTrigger == null ? null : turret.targetTrigger.GetComponent<SphereCollider>();
            if (collider != null)
            {
                collider.radius = prefabTurret.sightRange;
            }
        }

        private void RestoreSentryDurabilityFromPrefab(BaseEntity entity, BaseCombatEntity combat)
        {
            var prefab = FindPrefabForEntity(entity);
            var prefabCombat = prefab == null ? null : prefab.GetComponent<BaseCombatEntity>();
            if (combat == null || prefabCombat == null)
            {
                return;
            }

            combat.baseProtection = prefabCombat.baseProtection;

            var maxHealth = prefabCombat.MaxHealth();
            if (maxHealth <= 0f)
            {
                return;
            }

            combat.SetMaxHealth(maxHealth);
            combat.SetHealth(maxHealth);
        }

        private GameObject FindPrefabForEntity(BaseEntity entity)
        {
            if (!string.IsNullOrWhiteSpace(entity?.PrefabName))
            {
                var prefab = GameManager.server.FindPrefab(entity.PrefabName);
                if (prefab != null)
                {
                    return prefab;
                }
            }

            foreach (var prefabPath in config.OutpostSentryPrefabCandidates ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(prefabPath))
                {
                    continue;
                }

                var prefab = GameManager.server.FindPrefab(prefabPath);
                if (prefab != null && PrefabMatchesRequiredShortName(prefab, prefabPath))
                {
                    return prefab;
                }
            }

            return null;
        }

        private float GetConfiguredSentryRange()
        {
            var tuning = config.SentryTuning;
            var baseRange = tuning.FallbackNormalAutoTurretRangeMetres;
            if (tuning.UseVanillaAutoTurretPrefabRangeAsBase)
            {
                var prefab = GameManager.server.FindPrefab(VanillaAutoTurretPrefab);
                var prefabTurret = prefab == null ? null : prefab.GetComponent<AutoTurret>();
                if (prefabTurret != null && prefabTurret.sightRange > 0f)
                {
                    baseRange = prefabTurret.sightRange;
                }
            }

            return Mathf.Max(1f, baseRange * tuning.RangeMultiplier);
        }

        private void InitializeNewSentryDurability(BaseCombatEntity entity)
        {
            var tuning = config.SentryTuning;
            if (tuning == null || !tuning.EnableDurabilityTuning || entity == null || entity.IsDestroyed)
            {
                return;
            }

            if (tuning.ClearNativeBaseProtection)
            {
                entity.baseProtection = null;
            }

            entity.SetMaxHealth(tuning.EffectiveMaxHealth);
            entity.SetHealth(tuning.EffectiveMaxHealth);
            entity.SendNetworkUpdate();
        }

        private void ConfigureSentryDurability(BaseCombatEntity entity, bool clampOnly)
        {
            var tuning = config.SentryTuning;
            if (tuning == null || !tuning.EnableDurabilityTuning || entity == null || entity.IsDestroyed)
            {
                return;
            }

            if (tuning.ClearNativeBaseProtection)
            {
                entity.baseProtection = null;
            }

            var targetHealth = tuning.EffectiveMaxHealth;
            var currentMaxHealth = entity.MaxHealth();
            var currentHealth = entity.Health();
            if (currentMaxHealth <= 0f || Math.Abs(currentMaxHealth - targetHealth) > 0.01f)
            {
                entity.SetMaxHealth(targetHealth);
            }

            if (currentHealth <= 0f)
            {
                return;
            }

            if (!clampOnly && currentHealth < targetHealth)
            {
                return;
            }

            if (currentHealth > targetHealth || currentMaxHealth > targetHealth)
            {
                entity.SetHealth(targetHealth);
            }
        }

        private bool IsPureDecayDamage(HitInfo info)
        {
            if (info?.damageTypes == null || !info.damageTypes.Has(DamageType.Decay))
            {
                return false;
            }

            var total = info.damageTypes.Total();
            return total > 0f && info.damageTypes.Get(DamageType.Decay) >= total - 0.01f;
        }

        private string DescribeDamageSource(HitInfo info)
        {
            if (info == null)
            {
                return "unknown";
            }

            return info.WeaponPrefab?.ShortPrefabName
                ?? info.Weapon?.ShortPrefabName
                ?? info.WeaponPrefab?.GetItem()?.info?.shortname
                ?? info.Weapon?.GetItem()?.info?.shortname
                ?? info.Initiator?.ShortPrefabName
                ?? "unknown";
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

        private void EnablePeacekeeperMode(AutoTurret turret)
        {
            InvokeBooleanMethod(turret, "SetPeacekeepermode", true);
            InvokeBooleanMethod(turret, "SetPeacekeeperMode", true);
            SetBooleanMember(turret, "peacekeepermode", true);
            SetBooleanMember(turret, "peacekeeperMode", true);
            SetBooleanMember(turret, "Peacekeepermode", true);
            SetBooleanMember(turret, "PeacekeeperMode", true);
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
            return CreateSentryItem(1);
        }

        private Item CreateSentryItem(int amount)
        {
            var item = ItemManager.CreateByName(config.SentryItem.Shortname, Math.Max(1, amount), config.SentryItem.Skin);
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
            return player != null && HasAdminPermission(player.UserIDString);
        }

        private bool HasAdminPermission(string playerId)
        {
            return permission.UserHasPermission(playerId, AdminPermission) || permission.UserHasPermission(playerId, LegacyAdminPermission);
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
