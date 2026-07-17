using UnityEngine;

using Newtonsoft.Json;
using Oxide.Core.Plugins;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    // Raidlands compatibility: shared authorization and empty-vehicle SAM filtering.
    [Info("Tool Cupboard Turrets", "0x89A/Raidlands", "1.3.10")]
    [Description("Turrets only attack building blocked players")]

    class ToolCupboardTurrets : RustPlugin
    {
        [PluginReference]
        private Plugin AutomaticAuthorization;

        private const string turretsIgnore = "toolcupboardturrets.ignore";
        private const string turretsNeverIgnore = "toolcupboardturrets.neverIgnore";
        private readonly Dictionary<ulong, AuthorizationCacheEntry> authorizationCache = new Dictionary<ulong, AuthorizationCacheEntry>();

        private struct AuthorizationCacheEntry
        {
            public bool IsAuthorized;
            public float ExpiresAt;
        }

        #region -Init-

        void Init()
        {
            permission.RegisterPermission(turretsIgnore, this);
            permission.RegisterPermission(turretsNeverIgnore, this);

            if (!_config.samSitesAffected && !_config.staticSamSitesAffected)
            {
                Unsubscribe(nameof(OnSamSiteTarget));
                Unsubscribe(nameof(OnSamSiteTargetScan));
            }

            if (!_config.autoturretsAffected && !_config.shotgunTrapsAffected && !_config.flameTrapsAffected && !_config.NPCTurretsAffected)
                Unsubscribe(nameof(CanBeTargeted));
        }

        #endregion

        #region -Hooks-

        object CanBeTargeted(BasePlayer player, BaseCombatEntity entity)
        {
            if (player == null || string.IsNullOrEmpty(player.UserIDString))
                return null;

            if (permission.UserHasPermission(player.UserIDString, turretsIgnore))
                return true;

            if (permission.UserHasPermission(player.UserIDString, turretsNeverIgnore))
                return null;

            AutoTurret autoTurret = entity as AutoTurret;

            if (autoTurret != null && !(entity is NPCAutoTurret) && _config.autoturretsAffected)
            {
                if (!IsAuthedOnOwnerTc(entity, player))
                {
                    return null;
                }

                return true;
            }

            if (entity is NPCAutoTurret && _config.NPCTurretsAffected)
            {
                if (!IsAuthedOnOwnerTc(entity, player))
                {
                    return null;
                }

                return true;
            }

            bool isAffectedFlameTrap = entity is FlameTurret && _config.flameTrapsAffected;
            bool isAffectedShotgunTrap = entity is GunTrap && _config.shotgunTrapsAffected;
            if ((isAffectedFlameTrap || isAffectedShotgunTrap) && IsSafeForTrap(entity, player))
            {
                return true;
            }

            return null;
        }

        void OnSamSiteTargetScan(SamSite samsite, List<SamSite.ISamSiteTarget> targetList)
        {
            if (!IsAffectedSamSite(samsite))
                return;

            // Rust already supplies nearby SAM candidates. Only filter that list;
            // performing another world/physics scan here is prohibitively expensive.
            RemoveSafeVehicleTargets(samsite, targetList);
        }

        object OnSamSiteTarget(SamSite samsite, BaseHelicopter target)
        {
            if (!IsAffectedSamSite(samsite) || target == null)
                return null;

            if (_config.ignoreEmptyVehicles && !HasVehicleOccupants(target))
                return false;

            BasePlayer player = target.GetDriver();
            if (player != null && permission.UserHasPermission(player.UserIDString, turretsIgnore))
                return true;

            if (HasAuthorizedVehicleOccupant(samsite, target))
                return false;

            return null;
        }

        private void RemoveSafeVehicleTargets(SamSite samsite, List<SamSite.ISamSiteTarget> targetList)
        {
            if (targetList == null)
                return;

            for (int index = targetList.Count - 1; index >= 0; index--)
            {
                BaseEntity targetEntity = targetList[index] as BaseEntity;
                if (targetEntity == null || !(targetEntity is BaseVehicle))
                    continue;

                if ((_config.ignoreEmptyVehicles && !HasVehicleOccupants(targetEntity)) ||
                    HasAuthorizedVehicleOccupant(samsite, targetEntity))
                    targetList.RemoveAt(index);
            }
        }

        private bool HasAuthorizedVehicleOccupant(SamSite samsite, BaseEntity vehicle)
        {
            if (samsite == null || vehicle == null || vehicle.IsDestroyed)
                return false;

            BuildingPrivlidge privilege = samsite.GetBuildingPrivilege();
            if (privilege == null)
                return false;

            BaseVehicle baseVehicle = vehicle as BaseVehicle;
            if (baseVehicle != null)
            {
                foreach (var mountPoint in baseVehicle.allMountPoints)
                {
                    BasePlayer mountedPlayer = mountPoint == null || mountPoint.mountable == null
                        ? null
                        : mountPoint.mountable.GetMounted();

                    if (IsProtectedSamOccupant(privilege, mountedPlayer))
                        return true;
                }
            }

            if (vehicle.children == null)
                return false;

            foreach (BaseEntity child in vehicle.children)
            {
                BasePlayer parentedPlayer = child as BasePlayer;
                if (IsProtectedSamOccupant(privilege, parentedPlayer))
                    return true;
            }

            return false;
        }

        private bool IsProtectedSamOccupant(BuildingPrivlidge privilege, BasePlayer player)
        {
            return player != null &&
                   !player.IsDead() &&
                   !permission.UserHasPermission(player.UserIDString, turretsIgnore) &&
                   IsPlayerAuthedOrShared(privilege, player, "sam");
        }

        private bool HasVehicleOccupants(BaseEntity vehicle)
        {
            if (vehicle == null || vehicle.IsDestroyed)
                return false;

            BaseVehicle baseVehicle = vehicle as BaseVehicle;
            if (baseVehicle != null && baseVehicle.AnyMounted())
                return true;

            if (vehicle.children == null)
                return false;

            foreach (BaseEntity child in vehicle.children)
            {
                BasePlayer player = child as BasePlayer;
                if (player != null && !player.IsDead())
                    return true;
            }

            return false;
        }

        #endregion

        private bool IsAuthedOnOwnerTc(BaseEntity entity, BasePlayer player)
        {
            if (entity == null || entity.net == null || player == null)
                return false;

            ulong cacheKey = (entity.net.ID.Value * 397UL) ^ player.userID;
            AuthorizationCacheEntry cached;
            float now = Time.realtimeSinceStartup;
            if (authorizationCache.TryGetValue(cacheKey, out cached) && cached.ExpiresAt > now)
                return cached.IsAuthorized;

            BuildingPrivlidge privilege = entity.GetBuildingPrivilege();
            bool isAuthorized = privilege != null && IsPlayerAuthedOrShared(privilege, player, "turret");

            if (authorizationCache.Count > 4096)
                authorizationCache.Clear();

            authorizationCache[cacheKey] = new AuthorizationCacheEntry
            {
                IsAuthorized = isAuthorized,
                ExpiresAt = now + 0.5f
            };

            return isAuthorized;
        }

        private bool IsAffectedSamSite(SamSite samsite)
        {
            return samsite != null &&
                   ((samsite.ShortPrefabName == "sam_site_turret_deployed" && _config.samSitesAffected) ||
                    (samsite.ShortPrefabName == "sam_static" && _config.staticSamSitesAffected));
        }

        private bool IsAuthedOnSamTc(SamSite samsite, BasePlayer player)
        {
            BuildingPrivlidge privilege = samsite == null ? null : samsite.GetBuildingPrivilege();
            return player != null && privilege != null && IsPlayerAuthedOrShared(privilege, player, "sam");
        }

        private bool IsSafeForTrap(BaseCombatEntity entity, BasePlayer player)
        {
            if (player == null)
            {
                return false;
            }

            if (!player.IsBuildingBlocked())
            {
                return true;
            }

            BuildingPrivlidge privilege = entity == null ? null : entity.GetBuildingPrivilege();
            return privilege != null && IsPlayerAuthedOrShared(privilege, player, "trap");
        }

        private bool IsPlayerAuthedOrShared(BuildingPrivlidge privilege, BasePlayer player, string surface)
        {
            if (privilege == null || player == null)
            {
                return false;
            }

            if (privilege.IsAuthed(player))
            {
                return true;
            }

            ulong ownerId = privilege.OwnerID;
            if (!ownerId.IsSteamId())
            {
                return false;
            }

            if (AutomaticAuthorization == null || !AutomaticAuthorization.IsLoaded)
            {
                return false;
            }

            object sharedAuthorization = AutomaticAuthorization.Call("API_IsSharedAuthorization", ownerId, player.userID, surface);
            return sharedAuthorization is bool && (bool)sharedAuthorization;
        }

        #region -Configuration-

        private Configuration _config;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Auto-turrets affected")]
            public bool autoturretsAffected = true;

            [JsonProperty(PropertyName = "shotgun traps affected")]
            public bool shotgunTrapsAffected = true;

            [JsonProperty(PropertyName = "flame traps affected")]
            public bool flameTrapsAffected = true;

            [JsonProperty(PropertyName = "Sam sites affected")]
            public bool samSitesAffected = true;

            [JsonProperty(PropertyName = "Launch site sams affected")]
            public bool staticSamSitesAffected = false;

            [JsonProperty(PropertyName = "Ignore empty vehicles")]
            public bool ignoreEmptyVehicles = true;

            [JsonProperty(PropertyName = "Outpost turrets affected")]
            public bool NPCTurretsAffected = false;

        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new System.Exception();
                SaveConfig();
            }
            catch
            {
                PrintWarning("Error loading config (either corrupt or does not exist), using default values");

                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        protected override void LoadDefaultConfig() => _config = new Configuration();

        #endregion
    }
}
