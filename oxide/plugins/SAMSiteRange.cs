using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SAM Site Range", "nivex", "1.2.9")]
    [Description("Modifies SAM site target scan ranges.")]
    internal class SAMSiteRange : RustPlugin
    {
        [PluginReference]
        private Core.Plugins.Plugin RaidableBases;

        private ConfigData config;

        #region Oxide Hooks

        private void Init()
        {
            if (config?.permissions == null)
            {
                return;
            }

            foreach (string permissionName in config.permissions.Keys)
            {
                if (!permission.PermissionExists(permissionName))
                {
                    permission.RegisterPermission(permissionName, this);
                }
            }
        }

        private object OnSamSiteTargetScan(SamSite samSite, List<SamSite.ISamSiteTarget> targets)
        {
            if (samSite == null || targets == null || !TryGetScanRanges(samSite, out float vehicleRange, out float missileRange))
            {
                return null;
            }

            if (!samSite.IsInDefenderMode())
            {
                AddVehicleTargets(samSite, targets, vehicleRange);
            }

            AddMlrsTargets(samSite, targets, missileRange);
            return true;
        }

        #endregion

        #region Target Scanning

        private static void AddVehicleTargets(SamSite samSite, List<SamSite.ISamSiteTarget> targets, float scanRadius)
        {
            if (scanRadius <= 0f || SamSite.ISamSiteTarget.serverList == null || SamSite.ISamSiteTarget.serverList.Count == 0)
            {
                return;
            }

            Vector3 origin = samSite.eyePoint != null
                ? samSite.eyePoint.transform.position
                : samSite.transform.position;
            float scanRadiusSquared = scanRadius * scanRadius;

            foreach (SamSite.ISamSiteTarget target in SamSite.ISamSiteTarget.serverList)
            {
                // Rust can temporarily leave destroyed/null entries in this server list.
                if (target == null || target is MLRSRocket || !(target is BaseEntity entity) || entity == null || entity.IsDestroyed)
                {
                    continue;
                }

                if ((entity.CenterPoint() - origin).sqrMagnitude <= scanRadiusSquared && !targets.Contains(target))
                {
                    targets.Add(target);
                }
            }
        }

        private static void AddMlrsTargets(SamSite samSite, List<SamSite.ISamSiteTarget> targets, float scanRadius)
        {
            if (scanRadius <= 0f || MLRSRocket.serverList == null || MLRSRocket.serverList.Count == 0)
            {
                return;
            }

            Vector3 origin = samSite.transform.position;
            float scanRadiusSquared = scanRadius * scanRadius;

            foreach (MLRSRocket rocket in MLRSRocket.serverList)
            {
                if (rocket == null || rocket.IsDestroyed)
                {
                    continue;
                }

                if ((rocket.transform.position - origin).sqrMagnitude <= scanRadiusSquared && !targets.Contains(rocket))
                {
                    targets.Add(rocket);
                }
            }
        }

        #endregion

        #region Range Selection

        private bool TryGetScanRanges(SamSite samSite, out float vehicleRange, out float missileRange)
        {
            vehicleRange = 0f;
            missileRange = 0f;

            if (samSite == null || samSite.IsDestroyed || config == null)
            {
                return false;
            }

            if (samSite.OwnerID == 0UL || samSite.staticRespawn)
            {
                vehicleRange = Mathf.Max(0f, config.staticVehicleRange);
                missileRange = Mathf.Max(0f, config.staticMissileRange);
                return !IsRaidableTerritory(samSite);
            }

            if (!samSite.OwnerID.IsSteamId() || !TryGetPermissionSettings(samSite.OwnerID.ToString(), out PermissionSettings settings))
            {
                return false;
            }

            vehicleRange = Mathf.Max(0f, settings.vehicleScanRadius);
            missileRange = Mathf.Max(0f, settings.missileScanRadius);
            return true;
        }

        private bool TryGetPermissionSettings(string playerId, out PermissionSettings selectedSettings)
        {
            selectedSettings = null;

            if (config?.permissions == null || string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            int selectedPriority = int.MinValue;

            foreach (KeyValuePair<string, PermissionSettings> entry in config.permissions)
            {
                PermissionSettings settings = entry.Value;
                if (settings == null || settings.priority < selectedPriority || !permission.UserHasPermission(playerId, entry.Key))
                {
                    continue;
                }

                selectedPriority = settings.priority;
                selectedSettings = settings;
            }

            return selectedSettings != null;
        }

        private bool IsRaidableTerritory(BaseEntity entity)
        {
            if (RaidableBases == null || entity == null)
            {
                return false;
            }

            object result = RaidableBases.Call("HasEventEntity", entity);
            return result is bool value && value;
        }

        #endregion

        #region Configuration

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Static SamSite Vehicle Scan Range")]
            public float staticVehicleRange = 150f;

            [JsonProperty(PropertyName = "Static SamSite Missile Scan Range")]
            public float staticMissileRange = 225f;

            [JsonProperty(PropertyName = "Permissions", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, PermissionSettings> permissions = new Dictionary<string, PermissionSettings>
            {
                ["samsiterange.use"] = new PermissionSettings
                {
                    priority = 0,
                    vehicleScanRadius = 200f,
                    missileScanRadius = 275f
                },
                ["samsiterange.vip"] = new PermissionSettings
                {
                    priority = 1,
                    vehicleScanRadius = 250f,
                    missileScanRadius = 325f
                }
            };
        }

        private class PermissionSettings
        {
            [JsonProperty(PropertyName = "Priority")]
            public int priority;

            [JsonProperty(PropertyName = "Vehicle Scan Radius")]
            public float vehicleScanRadius;

            [JsonProperty(PropertyName = "Missile Scan Radius")]
            public float missileScanRadius;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null)
                {
                    throw new JsonException("Configuration deserialized to null.");
                }

                if (config.permissions == null)
                {
                    config.permissions = new ConfigData().permissions;
                }
            }
            catch (Exception exception)
            {
                PrintError($"The configuration file is invalid; defaults will be used.\n{exception}");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            config = new ConfigData();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion
    }
}