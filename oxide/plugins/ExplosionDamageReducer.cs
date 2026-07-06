/*
 ########### README ####################################################

  Explosion Damage Reducer

  Config values are percentages:
    100 = normal damage
     50 = half damage
      0 = no damage
    150 = 50% more damage

 ########### CHANGES ###################################################

 1.1.0
    - Updated hook to the current object-return form used by Oxide/uMod docs
    - Added HitInfo/config null safety
    - Added current Rust item shortname fallbacks for rockets and 40mm HE grenades
    - Avoids scaling unrelated weapon damage

 1.0.0
    - Plugin release

 #######################################################################
*/

using Oxide.Core;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Explosion Damage Reducer", "paulsimik", "1.1.0")]
    [Description("Scales player damage from Rockets, High Velocity Rockets, and 40mm HE Grenades.")]
    class ExplosionDamageReducer : RustPlugin
    {
        #region Constants

        private const string RocketPrefab = "rocket_basic";
        private const string HighVelocityRocketPrefab = "rocket_hv";
        private const string HeGrenadePrefab = "40mm_grenade_he";

        private const string RocketItemShortName = "ammo.rocket.basic";
        private const string HighVelocityRocketItemShortName = "ammo.rocket.hv";
        private const string HeGrenadeItemShortName = "ammo.grenadelauncher.he";

        #endregion

        #region Oxide Hooks

        private object OnEntityTakeDamage(BasePlayer victim, HitInfo info)
        {
            if (victim == null || victim.IsNpc || info == null || info.damageTypes == null || config == null)
                return null;

            BasePlayer attacker = info.InitiatorPlayer;
            if (attacker == null)
                return null;

            if (victim == attacker && !config.attackerReduceDamage)
                return null;

            int damagePercent;
            if (!TryGetDamagePercent(info, out damagePercent))
                return null;

            info.damageTypes.ScaleAll(ToMultiplier(damagePercent));
            return null;
        }

        #endregion

        #region Damage Matching

        private bool TryGetDamagePercent(HitInfo info, out int damagePercent)
        {
            damagePercent = 100;

            // Primary path: projectile/explosion prefab names used by the original plugin.
            if (TryGetDamagePercentByName(info.WeaponPrefab?.ShortPrefabName, out damagePercent))
                return true;

            if (TryGetDamagePercentByName(info.Weapon?.ShortPrefabName, out damagePercent))
                return true;

            // Fallbacks: current Rust item shortnames and held-weapon ammo shortnames.
            // These make the plugin more tolerant of changes in what HitInfo exposes.
            if (TryGetDamagePercentByName(info.WeaponPrefab?.GetItem()?.info?.shortname, out damagePercent))
                return true;

            if (TryGetDamagePercentByName(info.Weapon?.GetItem()?.info?.shortname, out damagePercent))
                return true;

            if (TryGetDamagePercentByName(info.Weapon?.GetComponent<BaseProjectile>()?.primaryMagazine?.ammoType?.shortname, out damagePercent))
                return true;

            if (TryGetDamagePercentByName(info.WeaponPrefab?.GetComponent<BaseProjectile>()?.primaryMagazine?.ammoType?.shortname, out damagePercent))
                return true;

            return false;
        }

        private bool TryGetDamagePercentByName(string name, out int damagePercent)
        {
            damagePercent = 100;

            switch (name)
            {
                case RocketPrefab:
                case RocketItemShortName:
                    damagePercent = config.rocket;
                    return true;

                case HighVelocityRocketPrefab:
                case HighVelocityRocketItemShortName:
                    damagePercent = config.hvRocket;
                    return true;

                case HeGrenadePrefab:
                case HeGrenadeItemShortName:
                    damagePercent = config.heGrenade;
                    return true;

                default:
                    return false;
            }
        }

        private static float ToMultiplier(int percent)
        {
            return percent <= 0 ? 0f : percent * 0.01f;
        }

        #endregion

        #region Classes

        private Configuration config;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Apply reduced damage to the attacker")]
            public bool attackerReduceDamage = false;

            [JsonProperty(PropertyName = "Rocket")]
            public int rocket = 100;

            [JsonProperty(PropertyName = "High Velocity Rocket")]
            public int hvRocket = 100;

            [JsonProperty(PropertyName = "HE Grenade")]
            public int heGrenade = 100;

            public VersionNumber version;
        }

        #endregion

        #region Config

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                attackerReduceDamage = false,
                rocket = 100,
                hvRocket = 100,
                heGrenade = 100,
                version = Version
            };
        }

        protected override void LoadDefaultConfig()
        {
            config = GetDefaultConfig();
            Puts("Generating new configuration file...");
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>();
            }
            catch
            {
                PrintError("Configuration file is not valid; loading default configuration.");
                LoadDefaultConfig();
            }

            ValidateConfig();
            SaveConfig();
        }

        private void ValidateConfig()
        {
            if (config == null)
                config = GetDefaultConfig();

            if (config.rocket < 0)
                config.rocket = 0;

            if (config.hvRocket < 0)
                config.hvRocket = 0;

            if (config.heGrenade < 0)
                config.heGrenade = 0;

            config.version = Version;
        }

        #endregion
    }
}
