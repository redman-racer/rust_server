using UnityEngine;

using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Tool Cupboard Turrets", "0x89A", "1.3.1")]
    [Description("Turrets only attack building blocked players")]

    class ToolCupboardTurrets : RustPlugin
    {
        private const string turretsIgnore = "toolcupboardturrets.ignore";
        private const string turretsNeverIgnore = "toolcupboardturrets.neverIgnore";

        #region -Init-

        void Init()
        {
            permission.RegisterPermission(turretsIgnore, this);
            permission.RegisterPermission(turretsNeverIgnore, this);

            if (!_config.samSitesAffected && !_config.staticSamSitesAffected)
                Unsubscribe(nameof(OnSamSiteTarget));

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

            if ((entity is FlameTurret && _config.flameTrapsAffected) || (entity is GunTrap && _config.shotgunTrapsAffected) && !player.IsBuildingBlocked())
            {
                return true;
            }

            return null;
        }

        object OnSamSiteTarget(SamSite samsite, BaseHelicopter target)
        {
            BasePlayer player = target.GetDriver();
            if (player == null)
            {
                return null;
            }

            if (permission.UserHasPermission(player.UserIDString, turretsIgnore))
            {
                return true;
            }

            //If not affected, default behaviour
            if ((samsite.ShortPrefabName == "sam_site_turret_deployed" && !_config.samSitesAffected) ||
                (samsite.ShortPrefabName == "sam_static" && !_config.staticSamSitesAffected))
            {
                return null;
            }

            BuildingPrivlidge privilege = samsite.GetBuildingPrivilege();
            if (privilege == null || privilege.IsAuthed(player) || !player.IsBuildingBlocked())
            {
                return false;
            }

            return null;
        }

        #endregion

        private bool IsAuthedOnOwnerTc(BaseEntity entity, BasePlayer player)
        {
            BuildingPrivlidge privilege = entity.GetBuildingPrivilege();

            Vector3 entityPosition = entity.transform.position;

            return privilege != null && privilege.IsAuthed(player) &&
                   player.IsVisible(new Vector3(entityPosition.x, entityPosition.y + 0.8f, entityPosition.z), player.CenterPoint());
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