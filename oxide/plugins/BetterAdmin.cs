using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Better Admin", "Malmo", "1.0.0")]
    public class BetterAdmin : RustPlugin
    {
        #region Fields

        private const string PermissionPrefix = "betteradmin.";

        private PluginConfig _config;

        #endregion

        #region Setup & Teardown

        private void Init()
        {
            _config = Config.ReadObject<PluginConfig>();

            InitPermissions();
            SubscribeToHooks();
        }

        private void SubscribeToHooks()
        {
            ConditionalSubscribe(nameof(OnDefaultItemsReceive), _config.AdminSpawnWithoutStarterItems);
            ConditionalSubscribe(nameof(OnPlayerCorpseSpawn), _config.DisableAdminCorpses || _config.DisableAdminDropLoot || _config.KillAdminsOnDisconnect);
            ConditionalSubscribe(nameof(CanDropActiveItem), _config.DisableAdminCorpses);
            ConditionalSubscribe(nameof(OnItemDropped), _config.DisableAdminDropLoot);
            ConditionalSubscribe(nameof(OnTeamInvite), _config.DisableAdminTeamInvites);
            ConditionalSubscribe(nameof(OnNpcTarget), _config.DisableAdminTargeting);
            ConditionalSubscribe(nameof(OnHelicopterTarget), _config.DisableAdminTargeting);
            ConditionalSubscribe(nameof(OnTurretTarget), _config.DisableAdminTargeting);
            ConditionalSubscribe(nameof(CanBradleyApcTarget), _config.DisableAdminTargeting);
            ConditionalSubscribe(nameof(CanBeTargeted), _config.DisableAdminTargeting);
            ConditionalSubscribe(nameof(OnEntityMarkHostile), _config.DisableAdminHostile);
            ConditionalSubscribe(nameof(OnPlayerViolation), _config.DisableAntiHackViolations);
            ConditionalSubscribe(nameof(OnPlayerDisconnected), _config.KillAdminsOnDisconnect);
        }

        #endregion

        #region Oxide Hooks

        private object OnDefaultItemsReceive(PlayerInventory inventory)
        {
            var player = inventory.GetComponent<BasePlayer>();
            if (!IsBetter(player)) return null;

            if (_config.AdminSpawnWithoutStarterItems && !UserHasPermission(player, "bypass.starter-items"))
            {
                Debug("Removed starter items");
                return false;
            }

            return null;
        }

        private object OnPlayerViolation(BasePlayer player, AntiHackType type, float amount)
        {
            if (!IsBetter(player)) return null;

            if (_config.DisableAntiHackViolations && !UserHasPermission(player, "bypass.anti-hack"))
            {
                Debug("Disabled anti-hack violation");
                return false;
            }

            return null;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (!IsBetter(player)) return;

            if (_config.KillAdminsOnDisconnect && !UserHasPermission(player, "bypass.disconnect-die"))
            {
                Debug("Killed admin on disconnect");
                player.inventory.Strip();
                player.Die();
            }
        }

        private object OnPlayerCorpseSpawn(BasePlayer player)
        {
            if (!IsBetter(player)) return null;

            if (
                (_config.DisableAdminCorpses || _config.KillAdminsOnDisconnect || _config.DisableAdminDropLoot)
                && !UserHasPermission(player, "bypass.corpse"))
            {
                Debug("Disabled corpse");
                return false;
            }

            return null;
        }

        private object CanDropActiveItem(BasePlayer player)
        {
            if (!IsBetter(player)) return null;

            if (_config.DisableAdminCorpses && !UserHasPermission(player, "bypass.corpse"))
            {
                Debug("Dont drop active item");
                return false;
            }

            return null;
        }

        private void OnItemDropped(Item item, BaseEntity entity)
        {
            if (item == null || entity == null) return;
            var player = item?.GetOwnerPlayer();

            if (!IsBetter(player)) return;

            if (player != null && _config.DisableAdminDropLoot && !UserHasPermission(player, "bypass.drop-loot"))
            {
                Debug("Deleted dropped item");
                entity.Kill();
            }
        }

        private object OnTeamInvite(BasePlayer inviter, BasePlayer target)
        {
            if (!IsBetter(inviter)) return null;

            if (_config.DisableAdminTeamInvites && !UserHasPermission(inviter, "bypass.team-invite"))
            {
                Debug("Disabled team invite");
                return false;
            }

            return null;
        }

        #endregion

        #region Oxide Target Hooks

        private object OnNpcTarget(BaseEntity npc, BasePlayer entity)
        {
            return CheckTarget(entity);
        }

        private object OnHelicopterTarget(HelicopterTurret turret, BasePlayer entity)
        {
            return CheckTarget(entity);
        }

        private object OnTurretTarget(AutoTurret turret, BasePlayer entity)
        {
            return CheckTarget(entity);
        }

        private object CanBradleyApcTarget(BradleyAPC apc, BasePlayer entity)
        {
            return CheckTarget(entity);
        }

        private object CanBeTargeted(BasePlayer player, MonoBehaviour behaviour)
        {
            return CheckTarget(player);
        }

        private object CheckTarget(BasePlayer player)
        {
            if (player == null || !IsBetter(player)) return null;

            if (_config.DisableAdminTargeting && !UserHasPermission(player, "bypass.targeting"))
            {
                Debug("Disabled targeting");
                return false;
            }

            return null;
        }

        private object OnEntityMarkHostile(BasePlayer entity, float duration)
        {
            if (entity == null || !IsBetter(entity)) return null;

            if (_config.DisableAdminHostile && !UserHasPermission(entity, "bypass.hostile"))
            {
                Debug("Disabled hostile");
                return false;
            }

            return null;
        }

        #endregion

        #region Helper Methods

        private void Debug(string message)
        {
#if DEBUG
            PrintWarning(message);
#endif
        }

        private void ConditionalSubscribe(string hook, bool subscribe)
        {
            if (subscribe)
            {
                Subscribe(hook);
                Debug($"Subscribed to {hook}");
            }
            else
            {
                Unsubscribe(hook);
                Debug($"Unsubscribed from {hook}");
            }
        }

        #endregion

        #region Permissions

        private void InitPermissions()
        {
            permission.RegisterPermission(PermissionPrefix + "active", this);

            permission.RegisterPermission(PermissionPrefix + "bypass.corpse", this);
            permission.RegisterPermission(PermissionPrefix + "bypass.team-invite", this);
            permission.RegisterPermission(PermissionPrefix + "bypass.drop-loot", this);
            permission.RegisterPermission(PermissionPrefix + "bypass.disconnect-die", this);
            permission.RegisterPermission(PermissionPrefix + "bypass.targeting", this);
            permission.RegisterPermission(PermissionPrefix + "bypass.hostile", this);
        }

        private bool UserHasPermission(BasePlayer player, string permissionName) =>
            permission.UserHasPermission(player.UserIDString, PermissionPrefix + permissionName);

        private bool IsBetter(BasePlayer player)
        {
            if (player == null || player.IsNpc) return false;
            return UserHasPermission(player, "active");
        }

        #endregion

        #region Config

        internal class PluginConfig
        {
            [JsonProperty("Disable anti-hack violations for admins")]
            public bool DisableAntiHackViolations = true;
            [JsonProperty("Kill Admins on Disconnect (if true, admins get killed without corpse when they disconnect)")]
            public bool KillAdminsOnDisconnect = true;

            [JsonProperty("Admin Spawns without starter items")]
            public bool AdminSpawnWithoutStarterItems = true;

            [JsonProperty("Disabled Admin Corpses")]
            public bool DisableAdminCorpses = true;

            [JsonProperty("Disable Admin Team Invites (if true, admins can't invite players to their team)")]
            public bool DisableAdminTeamInvites = true;

            [JsonProperty("Disable Admin From Dropping Loot (if true, loot that admin drops gets deleted instead)")]
            public bool DisableAdminDropLoot = true;

            [JsonProperty("Disable npc/turret/environment targeting admins")]
            public bool DisableAdminTargeting = true;

            [JsonProperty("Disable admin hostile (if true, admins can't be hostile to safezone sentries)")]
            public bool DisableAdminHostile = true;
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(GetDefaultConfig(), true);
        }

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig();
        }

        #endregion
    }
} 