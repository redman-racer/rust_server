using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

using Rust;
using UnityEngine;

using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Plugins.BGradeExt;

namespace Oxide.Plugins
{
    [Info("BGrade", "Ryan / Rustoria.co", "1.1.7")]
    [Description("Auto update building blocks when placed")]
    public class BGrade : RustPlugin
    {
        #region Declaration

        public static BGrade Instance;
        private static readonly MethodInfo UpdateSurroundingEntitiesMethod = typeof(BuildingBlock).GetMethod("UpdateSurroundingEntities", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        [PluginReference]
        private Plugin RaidlandsRoamBots;

        private ListHashSet<string> _registeredPermissions = new ListHashSet<string>();
        private Dictionary<Vector3, int> _lastAttacked = new Dictionary<Vector3, int>();

        #endregion

        #region Config

        private bool ConfigChanged;

        // Last attack settings
        private bool CheckLastAttack;
        private int UpgradeCooldown;

        // PvP lockout settings
        private bool PvpLockoutEnabled;
        private int PvpLockoutCooldownSeconds;
        private float PvpLockoutCancelDamagePercent;
        private bool CountRaidlandsRoamBotsAsPlayers;

        // Command settings
        private List<string> ChatCommands;
        private List<string> ConsoleCommands;

        // Refund settings
        private bool RefundOnBlock;

        // Placement / skin update safety settings
        private bool DelayUpgradeOneTick;
        private bool ResetSkinBeforeUpgrade;
        private bool StartRotatableAfterUpgrade;
        private bool UpdateSurroundingAfterUpgrade;

        protected override void LoadDefaultConfig() => PrintWarning("Generating default configuration file...");

        private void InitConfig()
        {
            ChatCommands = GetConfig(new List<string>
            {
                "bgrade",
                "grade"
            }, "Command Settings", "Chat Commands");
            ConsoleCommands = GetConfig(new List<string>
            {
                "bgrade.up"
            }, "Command Settings", "Console Commands");
            CheckLastAttack = GetConfig(true, "Building Attack Settings", "Enabled");
            UpgradeCooldown = GetConfig(30, "Building Attack Settings", "Cooldown Time");
            PvpLockoutEnabled = GetConfig(true, "PvP Lockout Settings", "Enabled");
            PvpLockoutCooldownSeconds = Math.Max(1, GetConfig(30, "PvP Lockout Settings", "Cooldown Seconds"));
            PvpLockoutCancelDamagePercent = Mathf.Clamp(GetConfig(30f, "PvP Lockout Settings", "Cancel Damage Percent"), 0f, 100f);
            CountRaidlandsRoamBotsAsPlayers = GetConfig(true, "PvP Lockout Settings", "Count Raidlands Roam Bots As Players");
            RefundOnBlock = GetConfig(true, "Refund Settings", "Refund on Block");

            // These defaults are intentionally conservative. They avoid mutating the block in the
            // same placement tick and prevent stale skin/mesh data from carrying into the upgrade.
            DelayUpgradeOneTick = GetConfig(true, "Placement Fix Settings", "Delay Upgrade One Tick");
            ResetSkinBeforeUpgrade = GetConfig(true, "Placement Fix Settings", "Reset Skin Before Upgrade");
            StartRotatableAfterUpgrade = GetConfig(false, "Placement Fix Settings", "Start Being Rotatable After Upgrade");
            UpdateSurroundingAfterUpgrade = GetConfig(true, "Placement Fix Settings", "Update Surrounding Entities After Upgrade");

            if (ConfigChanged)
            {
                PrintWarning("Updated configuration file with new/changed values.");
                SaveConfig();
            }
        }

        private T GetConfig<T>(T defaultVal, params string[] path)
        {
            var data = Config.Get(path);
            if (data != null)
            {
                return Config.ConvertValue<T>(data);
            }

            Config.Set(path.Concat(new object[] { defaultVal }).ToArray());
            ConfigChanged = true;
            return defaultVal;
        }

        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Permission"] = "You don't have permission to use that command",

                ["Error.InvalidArgs"] = "Invalid arguments, please use /{0} help",
                ["Error.Resources"] = "You don't have enough resources to upgrade.",

                ["Notice.SetGrade"] = "Automatic upgrading is now set to grade <color=orange>{0}</color>.",
                ["Notice.SetGrade.Locked"] = "Automatic upgrading is now set to grade <color=orange>{0}</color> and will resume after your PvP lockout.",
                ["Notice.Disabled"] = "Automatic upgrading is now disabled.",
                ["Notice.PvpLockout"] = "BGrade is paused for <color=orange>{0}</color> seconds because you were shot by {1}.",
                ["Notice.PvpRestored"] = "BGrade has resumed at grade <color=orange>{0}</color>.",
                ["Notice.PvpCancelled"] = "BGrade PvP lockout cancelled. Grade <color=orange>{0}</color> restored.",

                ["Command.Help"] = "<color=orange><size=16>BGrade Command Usages</size></color>",
                ["Command.Help.0"] = "/{0} 0 - Disables BGrade",
                ["Command.Help.1"] = "/{0} 1 - Upgrades to Wood upon placement",
                ["Command.Help.2"] = "/{0} 2 - Upgrades to Stone upon placement",
                ["Command.Help.3"] = "/{0} 3 - Upgrades to Metal upon placement",
                ["Command.Help.4"] = "/{0} 4 - Upgrades to Armoured upon placement",
                ["Command.Help.Persistent"] = "BGrade stays selected until logout, /{0} 0, or a temporary PvP lockout.",

                ["Command.Settings"] = "<color=orange><size=16>Your current settings</size></color>",
                ["Command.Settings.Grade"] = "Grade: <color=orange>{0}</color>",
                ["Command.Settings.Lockout"] = "PvP Lockout: <color=orange>{0}</color>",

                ["Words.Disabled"] = "disabled",
                ["Words.Inactive"] = "inactive",
                ["Words.SecondsRemaining"] = "{0}s remaining"
            }, this);
        }

        #endregion

        #region Methods

        private void RegisterPermissions()
        {
            _registeredPermissions = new ListHashSet<string>(  );

            for (var i = 1; i < 5; i++)
            {
                RegisterPermission( Name.ToLower() + "." + i );
            }

            RegisterPermission( Name.ToLower() + "." + "nores" );
            RegisterPermission( Name.ToLower() + "." + "all" );
        }

        private void RegisterPermission( string permissionName )
        {
            if ( !_registeredPermissions.Contains( permissionName ) )
            {
                _registeredPermissions.Add( permissionName );
            }

            permission.RegisterPermission( permissionName, this );
        }

        private void RegisterCommands()
        {
            foreach (var command in ChatCommands)
            {
                cmd.AddChatCommand(command, this, BGradeCommand);
            }

            foreach (var command in ConsoleCommands)
            {
                cmd.AddConsoleCommand(command, this, nameof(BGradeUpCommand));
            }
        }

        private void DestroyAll<T>() where T : MonoBehaviour
        {
            foreach (var type in UnityEngine.Object.FindObjectsOfType<T>())
            {
                UnityEngine.Object.Destroy(type);
            }
        }

        private void DealWithHookResult(BasePlayer player, BuildingBlock buildingBlock, int hookResult, GameObject gameObject)
        {
            if (hookResult <= 0)
            {
                return;
            }

            if (RefundOnBlock)
            {
                foreach (var itemToGive in buildingBlock.BuildCost())
                {
                    player.GiveItem(ItemManager.CreateByItemID(itemToGive.itemid, (int)itemToGive.amount));
                }
            }

            var entity = gameObject.GetComponent<BaseEntity>();
            entity?.Kill();
        }

        private string TakeResources(BasePlayer player, int playerGrade, BuildingBlock buildingBlock, out Dictionary<int, int> items)
        {
            var itemsToTake = new Dictionary<int, int>();

            List<ItemAmount> costToBuild = null;
            foreach (var grade in buildingBlock.blockDefinition.grades)
            {
                if (grade.gradeBase.type == (BuildingGrade.Enum) playerGrade)
                {
                    costToBuild = grade.CostToBuild();
                    break;
                }
            }

            if (costToBuild == null)
            {
                PrintError($"COULDN'T FIND COST TO BUILD WITH GRADE: {playerGrade} FOR {buildingBlock.PrefabName}");
                items = itemsToTake;
                return "Error.Resources".Lang(player.UserIDString);
            }

            foreach (var itemAmount in costToBuild)
            {
                if (!itemsToTake.ContainsKey(itemAmount.itemid))
                {
                    itemsToTake.Add(itemAmount.itemid, 0);
                }

                itemsToTake[itemAmount.itemid] += (int)itemAmount.amount;
            }

            var canAfford = true;
            foreach (var itemToTake in itemsToTake)
            {
                if (!player.HasItemAmount(itemToTake.Key, itemToTake.Value))
                {
                    canAfford = false;
                }
            }

            items = itemsToTake;
            return canAfford ? null : "Error.Resources".Lang(player.UserIDString);
        }

        private void CheckLastAttacked()
        {
            foreach (var lastAttackEntry in _lastAttacked.ToList())
            {
                if (!WasAttackedRecently(lastAttackEntry.Key))
                {
                    _lastAttacked.Remove(lastAttackEntry.Key);
                }
            }
        }

        private bool WasAttackedRecently(Vector3 position)
        {
            int cooldownExpiresAt;
            if (!_lastAttacked.TryGetValue(position, out cooldownExpiresAt))
            {
                return false;
            }

            return cooldownExpiresAt > Facepunch.Math.Epoch.Current;
        }

        private void ApplyGradeUpgrade(BuildingBlock buildingBlock, int playerGrade)
        {
            if (buildingBlock == null || buildingBlock.IsDestroyed)
            {
                return;
            }

            var targetGrade = (BuildingGrade.Enum) playerGrade;

            if (ResetSkinBeforeUpgrade)
            {
                buildingBlock.skinID = 0;
            }

            buildingBlock.SetGrade(targetGrade);
            buildingBlock.SetHealthToMax();

            if (StartRotatableAfterUpgrade)
            {
                buildingBlock.StartBeingRotatable();
            }

            buildingBlock.UpdateSkin();
            buildingBlock.ResetUpkeepTime();

            if (UpdateSurroundingAfterUpgrade)
            {
                TryUpdateSurroundingEntities(buildingBlock);
            }

            buildingBlock.SendNetworkUpdate();
            buildingBlock.GetBuilding()?.Dirty();
        }

        private void TryUpdateSurroundingEntities(BuildingBlock buildingBlock)
        {
            if (buildingBlock == null || UpdateSurroundingEntitiesMethod == null)
            {
                return;
            }

            try
            {
                UpdateSurroundingEntitiesMethod.Invoke(buildingBlock, null);
            }
            catch (Exception exception)
            {
                PrintWarning($"Failed to update surrounding entities after BGrade upgrade: {exception.GetBaseException().Message}");
            }
        }

        private bool TryResolveQualifyingPvpAttacker(BasePlayer victim, HitInfo info, out string attackerKey, out string attackerName)
        {
            attackerKey = null;
            attackerName = null;

            if (victim == null || info == null || !IsDirectRangedHit(info))
            {
                return false;
            }

            var attackerEntity = info.Initiator as BaseCombatEntity;
            var attackerPlayer = info.InitiatorPlayer ?? info.Initiator as BasePlayer;

            if (CountRaidlandsRoamBotsAsPlayers && TryGetRaidlandsRoamBotCombatKey(attackerEntity ?? attackerPlayer, out attackerKey, out attackerName))
            {
                return true;
            }

            if (attackerPlayer == null || attackerPlayer == victim || !ReferenceEquals(info.Initiator, attackerPlayer) || !IsSteamId64(attackerPlayer.UserIDString))
            {
                return false;
            }

            attackerKey = $"player:{attackerPlayer.userID}";
            attackerName = SafeName(attackerPlayer.displayName, "another player");
            return true;
        }

        private bool IsDirectRangedHit(HitInfo info)
        {
            if (info?.damageTypes == null || !info.IsProjectile())
            {
                return false;
            }

            return info.damageTypes.Has(DamageType.Bullet) || info.damageTypes.Has(DamageType.Arrow);
        }

        private float ActualHealthDamage(BasePlayer victim, HitInfo info)
        {
            if (victim == null || info?.damageTypes == null)
            {
                return 0f;
            }

            var total = Mathf.Max(0f, info.damageTypes.Total());
            return Mathf.Min(total, Mathf.Max(0f, victim.Health()));
        }

        private float PlayerMaxHealth(BasePlayer player)
        {
            return Mathf.Max(1f, player?.MaxHealth() ?? 100f);
        }

        private bool TryGetDeathCombatantKey(BaseCombatEntity entity, out string combatantKey)
        {
            combatantKey = null;

            if (entity == null)
            {
                return false;
            }

            string botName;
            if (CountRaidlandsRoamBotsAsPlayers && TryGetRaidlandsRoamBotCombatKey(entity, out combatantKey, out botName))
            {
                return true;
            }

            var player = entity as BasePlayer;
            if (player == null || !IsSteamId64(player.UserIDString))
            {
                return false;
            }

            combatantKey = $"player:{player.userID}";
            return true;
        }

        private bool TryGetRaidlandsRoamBotCombatKey(BaseCombatEntity entity, out string combatantKey, out string displayName)
        {
            combatantKey = null;
            displayName = null;

            if (RaidlandsRoamBots == null || entity == null)
            {
                return false;
            }

            var isBotResult = RaidlandsRoamBots.Call("API_IsRaidlandsRoamBot", entity);
            if (!(isBotResult is bool) || !(bool)isBotResult)
            {
                return false;
            }

            var keyResult = RaidlandsRoamBots.Call("API_GetRaidlandsRoamBotCombatKey", entity) as string;
            combatantKey = $"roambot:{(string.IsNullOrWhiteSpace(keyResult) ? EntityId(entity).ToString() : keyResult)}";

            var player = entity as BasePlayer;
            displayName = SafeName(player?.displayName, "a Raidlands RoamBot");
            return true;
        }

        private ulong EntityId(BaseNetworkable entity)
        {
            try
            {
                return entity?.net?.ID.Value ?? 0UL;
            }
            catch
            {
                return 0UL;
            }
        }

        private bool IsSteamId64(string id)
        {
            ulong value;
            return ulong.TryParse(id, out value) && value >= 76561197960265728UL;
        }

        private string SafeName(string name, string fallback)
        {
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }

        #endregion

        #region BGrade Player

        private class BGradePlayer : FacepunchBehaviour
        {
            public static Dictionary<BasePlayer, BGradePlayer> Players = new Dictionary<BasePlayer, BGradePlayer>();

            private BasePlayer _player;
            private Timer _pvpLockoutTimer;
            private int _selectedGrade;
            private bool _pvpLocked;
            private float _pvpLockoutExpiresAt;
            private string _pvpLockoutSingleAttackerKey;
            private bool _pvpLockoutMultipleAttackers;
            private float _pvpLockoutHealthLost;
            private float _pvpLockoutMaxHealth;

            public void Awake()
            {
                var attachedPlayer = GetComponent<BasePlayer>();
                if ( attachedPlayer == null || !attachedPlayer.IsConnected )
                {
                    return;
                }

                _player = attachedPlayer;
                Players[_player] = this;
            }

            public int GetGrade()
            {
                return IsPvpLocked ? 0 : _selectedGrade;
            }

            public int GetSelectedGrade()
            {
                return _selectedGrade;
            }

            public bool IsPvpLocked
            {
                get
                {
                    return _pvpLocked && UnityEngine.Time.realtimeSinceStartup < _pvpLockoutExpiresAt;
                }
            }

            public int PvpLockoutRemainingSeconds
            {
                get
                {
                    if (!IsPvpLocked)
                    {
                        return 0;
                    }

                    return Mathf.CeilToInt(Mathf.Max(0f, _pvpLockoutExpiresAt - UnityEngine.Time.realtimeSinceStartup));
                }
            }

            public void SetGrade(int newGrade)
            {
                _selectedGrade = Mathf.Clamp(newGrade, 0, 4);

                if (_selectedGrade == 0)
                {
                    ClearPvpLockout(false);
                }
            }

            public void ApplyPvpHit(string attackerKey, string attackerName, float damageAmount, float maxHealth)
            {
                if (_selectedGrade == 0 || string.IsNullOrWhiteSpace(attackerKey))
                {
                    return;
                }

                var wasLocked = IsPvpLocked;
                if (!wasLocked)
                {
                    ResetPvpLockoutTracking();
                }

                if (string.IsNullOrWhiteSpace(_pvpLockoutSingleAttackerKey))
                {
                    _pvpLockoutSingleAttackerKey = attackerKey;
                }
                else if (!string.Equals(_pvpLockoutSingleAttackerKey, attackerKey, StringComparison.Ordinal))
                {
                    _pvpLockoutMultipleAttackers = true;
                }

                _pvpLockoutHealthLost += Mathf.Max(0f, damageAmount);
                _pvpLockoutMaxHealth = Mathf.Max(_pvpLockoutMaxHealth, Mathf.Max(1f, maxHealth));
                _pvpLocked = true;
                _pvpLockoutExpiresAt = UnityEngine.Time.realtimeSinceStartup + Instance.PvpLockoutCooldownSeconds;

                DestroyPvpLockoutTimer();
                _pvpLockoutTimer = Instance.timer.Once(Instance.PvpLockoutCooldownSeconds, () =>
                {
                    ClearPvpLockout(true);
                });

                if (!wasLocked && _player != null && _player.IsConnected)
                {
                    _player.ChatMessage("Notice.PvpLockout".Lang(_player.UserIDString, Instance.PvpLockoutCooldownSeconds, Instance.SafeName(attackerName, "another player")));
                }
            }

            public bool TryCancelPvpLockout(string killedCombatantKey)
            {
                if (!CanCancelPvpLockout(killedCombatantKey))
                {
                    return false;
                }

                var restoredGrade = _selectedGrade;
                ClearPvpLockout(false);

                if (restoredGrade > 0)
                {
                    _player.ChatMessage("Notice.PvpCancelled".Lang(_player.UserIDString, restoredGrade));
                }

                return true;
            }

            public void ClearPvpLockout(bool notify)
            {
                if (!_pvpLocked && _pvpLockoutTimer == null)
                {
                    ResetPvpLockoutTracking();
                    return;
                }

                var restoredGrade = _selectedGrade;
                DestroyPvpLockoutTimer();
                ResetPvpLockoutTracking();

                if (notify && restoredGrade > 0 && _player != null && _player.IsConnected)
                {
                    _player.ChatMessage("Notice.PvpRestored".Lang(_player.UserIDString, restoredGrade));
                }
            }

            private bool CanCancelPvpLockout(string killedCombatantKey)
            {
                if (!IsPvpLocked || string.IsNullOrWhiteSpace(killedCombatantKey) || _pvpLockoutMultipleAttackers)
                {
                    return false;
                }

                if (!string.Equals(_pvpLockoutSingleAttackerKey, killedCombatantKey, StringComparison.Ordinal))
                {
                    return false;
                }

                var maxHealth = Mathf.Max(1f, _pvpLockoutMaxHealth);
                var lostPercent = (_pvpLockoutHealthLost / maxHealth) * 100f;
                return lostPercent <= Instance.PvpLockoutCancelDamagePercent;
            }

            private void ResetPvpLockoutTracking()
            {
                _pvpLocked = false;
                _pvpLockoutExpiresAt = 0f;
                _pvpLockoutSingleAttackerKey = null;
                _pvpLockoutMultipleAttackers = false;
                _pvpLockoutHealthLost = 0f;
                _pvpLockoutMaxHealth = 0f;
            }

            private void DestroyPvpLockoutTimer()
            {
                _pvpLockoutTimer?.Destroy();
                _pvpLockoutTimer = null;
            }

            public void Destroy()
            {
                DestroyPvpLockoutTimer();
                Destroy(this);
            }

            public void OnDestroy()
            {
                if ( Players.ContainsKey( _player ) )
                {
                    Players.Remove( _player );
                }
            }
        }

        #endregion

        #region Hooks

        private void Init()
        {
            Instance = this;

            InitConfig();
            RegisterCommands();
            RegisterPermissions();

            if (!CheckLastAttack)
            {
                Unsubscribe(nameof(OnServerSave));
            }

            if (!PvpLockoutEnabled)
            {
                Unsubscribe(nameof(OnEntityTakeDamage));
            }

            if (!CheckLastAttack && !PvpLockoutEnabled)
            {
                Unsubscribe(nameof(OnEntityDeath));
            }
        }

        private void OnServerSave()
        {
            CheckLastAttacked();
        }

        private void Unload()
        {
            Instance = null;
            DestroyAll<BGradePlayer>();
            BGradePlayer.Players.Clear();
        }

        private void OnEntityBuilt(Planner plan, GameObject gameObject)
        {
            var player = plan?.GetOwnerPlayer();
            if (player == null)
            {
                return;
            }

            if ( plan.isTypeDeployable )
            {
                return;
            }

            var buildingBlock = gameObject.GetComponent<BuildingBlock>();
            if ( buildingBlock == null )
            {
                return;
            }

            if (!player.CanBuild())
            {
                return;
            }

            if ( !player.HasAnyPermission( _registeredPermissions ) )
            {
                return;
            }

            BGradePlayer bgradePlayer;
            if ( !BGradePlayer.Players.TryGetValue( player, out bgradePlayer ) )
            {
                return;
            }

            var playerGrade = bgradePlayer.GetGrade();
            if (playerGrade == 0)
            {
                return;
            }

            if (!player.HasPluginPerm("all") && !player.HasPluginPerm(playerGrade.ToString()))
            {
                return;
            }

            var hookCall = Interface.Call("CanBGrade", player, playerGrade, buildingBlock, plan);

            if (hookCall is int)
            {
                DealWithHookResult(player, buildingBlock, (int) hookCall, gameObject);
                return;
            }

            if (playerGrade < (int) buildingBlock.grade || buildingBlock.blockDefinition.grades[playerGrade] == null)
            {
                return;
            }

            if (CheckLastAttack && WasAttackedRecently(buildingBlock.transform.position))
            {
                return;
            }

            if (Interface.Call("OnStructureUpgrade", buildingBlock, player, (BuildingGrade.Enum) playerGrade) != null)
            {
                return;
            }

            if (!player.HasPluginPerm("nores"))
            {
                Dictionary<int, int> itemsToTake;
                var resourceResponse = TakeResources(player, playerGrade, buildingBlock, out itemsToTake);
                if (!string.IsNullOrEmpty(resourceResponse))
                {
                    player.ChatMessage(resourceResponse);
                    return;
                }

                foreach (var itemToTake in itemsToTake)
                {
                    player.TakeItem(itemToTake.Key, itemToTake.Value);
                }
            }

            if (DelayUpgradeOneTick)
            {
                NextTick(() => ApplyGradeUpgrade(buildingBlock, playerGrade));
            }
            else
            {
                ApplyGradeUpgrade(buildingBlock, playerGrade);
            }
        }

        private object OnPayForPlacement( BasePlayer player, Planner planner, Construction component )
        {
            if ( planner.isTypeDeployable )
            {
                return null;
            }

            if ( !BGradePlayer.Players.ContainsKey( player ) )
            {
                return null;
            }

            if ( !player.HasPluginPerm( "nores" ) )
            {
                return null;
            }

            var bgradePlayer = BGradePlayer.Players[player];
            if ( bgradePlayer.GetGrade() == 0 )
            {
                return null;
            }

            return false;
        }

        private void OnEntityTakeDamage(BasePlayer victim, HitInfo info)
        {
            if (!PvpLockoutEnabled || victim == null || info == null)
            {
                return;
            }

            BGradePlayer bgradePlayer;
            if (!BGradePlayer.Players.TryGetValue(victim, out bgradePlayer) || bgradePlayer.GetSelectedGrade() == 0)
            {
                return;
            }

            string attackerKey;
            string attackerName;
            if (!TryResolveQualifyingPvpAttacker(victim, info, out attackerKey, out attackerName))
            {
                return;
            }

            var damageAmount = ActualHealthDamage(victim, info);
            if (damageAmount <= 0f)
            {
                return;
            }

            bgradePlayer.ApplyPvpHit(attackerKey, attackerName, damageAmount, PlayerMaxHealth(victim));
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var buildingBlock = entity as BuildingBlock;
            if (CheckLastAttack && buildingBlock != null)
            {
                var attacker = info?.InitiatorPlayer;
                if (attacker != null && info.damageTypes.GetMajorityDamageType() == DamageType.Explosion)
                {
                    _lastAttacked[buildingBlock.transform.position] = Facepunch.Math.Epoch.Current + UpgradeCooldown;
                }
            }

            if (!PvpLockoutEnabled || entity == null || info == null)
            {
                return;
            }

            var killer = info.InitiatorPlayer ?? info.Initiator as BasePlayer;
            if (killer == null || !ReferenceEquals(info.Initiator, killer) || !IsSteamId64(killer.UserIDString))
            {
                return;
            }

            BGradePlayer bgradePlayer;
            if (!BGradePlayer.Players.TryGetValue(killer, out bgradePlayer))
            {
                return;
            }

            string killedCombatantKey;
            if (TryGetDeathCombatantKey(entity, out killedCombatantKey))
            {
                bgradePlayer.TryCancelPvpLockout(killedCombatantKey);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            BGradePlayer bgradePlayer;
            if ( !BGradePlayer.Players.TryGetValue( player, out bgradePlayer ) )
            {
                return;
            }

            bgradePlayer.Destroy();
        }

        #endregion

        #region Commands

        private void BGradeCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.HasAnyPermission(_registeredPermissions))
            {
                player.ChatMessage("Permission".Lang(player.UserIDString));
                return;
            }

            if (args.Length == 0)
            {
                player.ChatMessage("Error.InvalidArgs".Lang(player.UserIDString, command));
                return;
            }

            var chatMsgs = new List<string>();

            switch (args[0].ToLower())
            {
                case "0":
                    {
                        player.ChatMessage("Notice.Disabled".Lang(player.UserIDString));
                        BGradePlayer bgradePlayer;
                        if ( BGradePlayer.Players.TryGetValue( player, out bgradePlayer ) )
                        {
                            bgradePlayer.SetGrade( 0 );
                        }
                        return;
                    }

                case "1":
                case "2":
                case "3":
                case "4":
                    {
                        if (!player.HasPluginPerm("all") && !player.HasPluginPerm(args[0]))
                        {
                            player.ChatMessage("Permission".Lang(player.UserIDString));
                            return;
                        }

                        var grade = Convert.ToInt32(args[0]);

                        BGradePlayer bgradePlayer;
                        if ( !BGradePlayer.Players.TryGetValue( player, out bgradePlayer ) )
                        {
                            bgradePlayer = player.gameObject.AddComponent<BGradePlayer>();
                        }

                        bgradePlayer.SetGrade(grade);
                        chatMsgs.Add((bgradePlayer.IsPvpLocked ? "Notice.SetGrade.Locked" : "Notice.SetGrade").Lang(player.UserIDString, grade));

                        player.ChatMessage(string.Join("\n", chatMsgs.ToArray()));
                        return;
                    }

                case "t":
                    {
                        goto default;
                    }

                case "help":
                    {
                        chatMsgs.Add("Command.Help".Lang(player.UserIDString));
                        chatMsgs.Add("Command.Help.0".Lang(player.UserIDString, command));

                        for (var i = 1; i < 5; i++)
                        {
                            if (player.HasPluginPerm(i.ToString()) || player.HasPluginPerm("all"))
                                chatMsgs.Add($"Command.Help.{i}".Lang(player.UserIDString, command));
                        }

                        if (chatMsgs.Count <= 3 && !player.HasPluginPerm("all"))
                        {
                            player.ChatMessage("Permission".Lang(player.UserIDString));
                            return;
                        }

                        BGradePlayer bgradePlayer;
                        if ( BGradePlayer.Players.TryGetValue( player, out bgradePlayer ) )
                        {
                            chatMsgs.Add( "Command.Settings".Lang( player.UserIDString ) );
                            var fetchedGrade = bgradePlayer.GetSelectedGrade();
                            chatMsgs.Add( "Command.Settings.Grade".Lang( player.UserIDString, fetchedGrade == 0 ? "Words.Disabled".Lang( player.UserIDString ) : fetchedGrade.ToString() ) );
                            chatMsgs.Add( "Command.Settings.Lockout".Lang( player.UserIDString, bgradePlayer.IsPvpLocked ? "Words.SecondsRemaining".Lang(player.UserIDString, bgradePlayer.PvpLockoutRemainingSeconds) : "Words.Inactive".Lang(player.UserIDString) ) );
                        }

                        chatMsgs.Add("Command.Help.Persistent".Lang(player.UserIDString, command));

                        player.ChatMessage(string.Join("\n", chatMsgs.ToArray()));
                        return;
                    }

                default:
                    {
                        player.ChatMessage("Error.InvalidArgs".Lang(player.UserIDString, command));
                        return;
                    }
            }
        }

        private void BGradeUpCommand(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null)
            {
                return;
            }

            if (!player.HasAnyPermission(_registeredPermissions))
            {
                player.ChatMessage("Permission".Lang(player.UserIDString));
                return;
            }

            BGradePlayer bgradePlayer;
            if ( !BGradePlayer.Players.TryGetValue( player, out bgradePlayer ) )
            {
                bgradePlayer = player.gameObject.AddComponent<BGradePlayer>();
            }
            var grade = bgradePlayer.GetSelectedGrade() + 1;
            var count = 0;

            if (!player.HasPluginPerm("all"))
            {
                while (!player.HasPluginPerm(grade.ToString()))
                {
                    count++;
                    var newGrade = grade++;
                    if (newGrade > 4)
                    {
                        grade = 1;
                    }

                    if (count > bgradePlayer.GetSelectedGrade() + 4)
                    {
                        player.ChatMessage("Permission".Lang(player.UserIDString));
                        return;
                    }
                }
            }
            else if (grade > 4) grade = 1;

            var chatMsgs = new List<string>();
            bgradePlayer.SetGrade(grade);

            chatMsgs.Add((bgradePlayer.IsPvpLocked ? "Notice.SetGrade.Locked" : "Notice.SetGrade").Lang(player.UserIDString, grade));

            player.ChatMessage(string.Join("\n", chatMsgs.ToArray()));
        }

        #endregion
    }
}

namespace Oxide.Plugins.BGradeExt
{
    public static class BGradeExtensions
    {
        private static readonly Permission permission = Interface.Oxide.GetLibrary<Permission>();
        private static readonly Lang lang = Interface.Oxide.GetLibrary<Lang>();

        public static bool HasAnyPermission(this BasePlayer player, ListHashSet<string> perms)
        {
            foreach (var perm in perms)
            {
                if (!player.HasPermission(perm))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        public static bool HasPermission(this BasePlayer player, string perm)
        {
            return permission.UserHasPermission(player.UserIDString, perm);
        }

        public static bool HasPluginPerm(this BasePlayer player, string perm)
        {
            return permission.UserHasPermission(player.UserIDString, BGrade.Instance.Name.ToLower() + "." + perm);
        }

        public static string Lang(this string key, string id = null, params object[] args)
        {
            return string.Format(lang.GetMessage(key, BGrade.Instance, id), args);
        }

        public static bool HasItemAmount(this BasePlayer player , int itemId , int itemAmount)
        {
            var count = 0;

            foreach (var item in player.inventory.containerMain.itemList)
            {
                if (item.info.itemid == itemId)
                {
                    count += item.amount;
                }
            }

            foreach (var item in player.inventory.containerBelt.itemList)
            {
                if (item.info.itemid == itemId)
                {
                    count += item.amount;
                }
            }

            foreach (var item in player.inventory.containerWear.itemList)
            {
                if (item.info.itemid == itemId)
                {
                    count += item.amount;
                }
            }

            return count >= itemAmount;
        }


        public static bool HasItemAmount(this BasePlayer player , int itemId , int itemAmount , out int amountGot)
        {
            var count = 0;

            foreach (var item in player.inventory.containerMain.itemList)
            {
                if (item.info.itemid == itemId)
                {
                    count += item.amount;
                }
            }

            foreach (var item in player.inventory.containerBelt.itemList)
            {
                if (item.info.itemid == itemId)
                {
                    count += item.amount;
                }
            }

            foreach (var item in player.inventory.containerWear.itemList)
            {
                if (item.info.itemid == itemId)
                {
                    count += item.amount;
                }
            }

            amountGot = count;
            return count >= itemAmount;
        }


        public static void TakeItem(this BasePlayer player, int itemId, int itemAmount)
        {
            if (player.inventory.Take(null, itemId, itemAmount) > 0)
            {
                player.SendConsoleCommand("note.inv", itemId, itemAmount * -1);
            }
        }
    }
}
