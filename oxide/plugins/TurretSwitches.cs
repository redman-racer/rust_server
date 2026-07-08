using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using Rust;

namespace Oxide.Plugins
{
    [Info("Turret Switches", "ziptie/Raidlands", 1.8)]
    [Description("Spawns switches on turrets and SAM sites for players with permission.")]
    public class TurretSwitches : CovalencePlugin
    {
        private const string SwitchPrefab = "assets/prefabs/io/electric/switches/simpleswitch/simpleswitch.prefab";
        private const string SwitchShortPrefabName = "simpleswitch";
        private static readonly Vector3[] VanillaTurretSwitchOffsets =
        {
            new Vector3(0f, -0.65f, 0.3f),
            new Vector3(0.3f, -0.65f, 0f),
            new Vector3(-0.3f, -0.65f, 0f),
            new Vector3(0f, -0.65f, -0.3f)
        };
        private static readonly Vector3[] NpcSentryLegSwitchOffsets =
        {
            new Vector3(0f, -0.62f, 0.9f),
            new Vector3(0.78f, -0.62f, -0.46f),
            new Vector3(-0.78f, -0.62f, -0.46f)
        };
        private static readonly Vector3[] SamSiteSwitchOffsets =
        {
            new Vector3(0f, -0.65f, 0.95f),
            new Vector3(0.95f, -0.65f, 0f),
            new Vector3(-0.95f, -0.65f, 0f),
            new Vector3(0f, -0.65f, -0.95f)
        };

        #region Config
        public static TurretSwitchesConfig config;
        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(GetDefaultConfig(), true);
        }
        private TurretSwitchesConfig GetDefaultConfig()
        {
            return new TurretSwitchesConfig();
        }
        private void Init()
        {
            TurretSwitches.config = Config.ReadObject<TurretSwitchesConfig>();
            RegisterPermissions();
            AddSwitchesToAllTurrets();
        }
        #endregion

        #region Localisation
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["SwitchNoPermission"] = "You do not have permission to toggle this switch.",
            }, this);
        }
        #endregion

        #region Permissions
        public const string TurretPermission = "turretswitches.turret";
        public const string SAMPermission = "turretswitches.sam";
        private void RegisterPermissions()
        {
            permission.RegisterPermission(TurretPermission, this);
            permission.RegisterPermission(SAMPermission, this);
        }
        #endregion

        #region Hooks
        object OnSwitchToggle(IOEntity entity, BasePlayer player)
        {
            if (entity.HasComponent<TurretSwitch>())
            {
                TurretSwitch ts = entity.GetComponent<TurretSwitch>();
                ElectricSwitch s = entity as ElectricSwitch;
                if (ts.CanToggleTurret(player))
                {
                    ts.ToggleTurret(!s.IsOn());
                    if (config.PlaySoundEffects)
                        PlayEffect("assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab", player.transform.position);
                    return null;
                }
                if (config.PlaySoundEffects)
                    PlayEffect("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", player.transform.position);
                player.IPlayer.Reply(lang.GetMessage("SwitchNoPermission", this, player.UserIDString));
                return false;
            }
            if (entity.HasComponent<SAMSwitch>())
            {
                SAMSwitch ts = entity.GetComponent<SAMSwitch>();
                ElectricSwitch s = entity as ElectricSwitch;
                if (ts.CanToggleSamSite(player))
                {
                    ts.ToggleTurret(!s.IsOn());
                    if (config.PlaySoundEffects)
                        PlayEffect("assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab", player.transform.position);
                    return null;
                }
                if (config.PlaySoundEffects)
                    PlayEffect("assets/prefabs/locks/keypad/effects/lock.code.denied.prefab", player.transform.position);
                player.IPlayer.Reply(lang.GetMessage("SwitchNoPermission", this, player.UserIDString));
                return false;
            }
            return null;
        }
        void OnEntitySpawned(AutoTurret entity)
        {
            timer.Once(0.25f, () => AddSwitchToTurret(entity));
        }
        void OnEntitySpawned(SamSite entity)
        {
            timer.Once(0.25f, () => AddSwitchToSam(entity));
        }
        void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            HandlePlayerPlacedEntity(planner?.GetOwnerPlayer(), gameObject?.ToBaseEntity());
        }
        void OnItemDeployed(Deployer deployer, BaseEntity entity)
        {
            HandlePlayerPlacedEntity(deployer?.GetOwnerPlayer(), entity);
        }
        void OnItemDeployed(Deployer deployer, ItemModDeployable modDeployable, BaseEntity entity)
        {
            HandlePlayerPlacedEntity(deployer?.GetOwnerPlayer(), entity);
        }
        void OnItemDeployed(Deployer deployer, BaseEntity slotEntity, BaseEntity entity)
        {
            HandlePlayerPlacedEntity(deployer?.GetOwnerPlayer(), entity);
        }
        void Unload()
        {
            config = null;
            KillAllSwitches();
        }
        #endregion

        #region Helpers
        public void PlayEffect(string EffectPath, Vector3 position)
        {
            Effect.server.Run(EffectPath, position);
        }
        public IList<ElectricSwitch> switches = new List<ElectricSwitch>();
        public void KillAllSwitches()
        {
            foreach (var item in switches.ToList())
            {
                KillSwitch(item);
            }
            switches.Clear();
        }
        public void AddSwitchesToAllTurrets()
        {
            foreach (var networkable in BaseNetworkable.serverEntities.ToList())
            {
                var turret = networkable as AutoTurret;
                if (turret != null)
                {
                    AddSwitchToTurret(turret);
                    continue;
                }

                var samSite = networkable as SamSite;
                if (samSite != null)
                {
                    AddSwitchToSam(samSite);
                }
            }
        }

        public void AddSwitchToTurret(AutoTurret entity)
        {
            AddSwitchToTurret(entity, null);
        }

        public void AddSwitchToTurret(AutoTurret entity, Vector3? preferredWorldPosition)
        {
            AddSwitchToTurret(entity, preferredWorldPosition, false);
        }

        private void AddSwitchToTurret(AutoTurret entity, Vector3? preferredWorldPosition, bool allowRecentPlayerPlacement)
        {
            if (entity == null || entity.IsDestroyed)
                return;

            if (!ShouldHaveSwitch(entity, allowRecentPlayerPlacement))
            {
                RemoveSwitchChildren(entity);
                return;
            }

            var existingSwitch = FindTurretSwitch(entity);
            if (existingSwitch != null)
            {
                var turretSwitch = existingSwitch.GetComponent<TurretSwitch>() ?? existingSwitch.gameObject.AddComponent<TurretSwitch>();
                turretSwitch.Turret = entity;
                if (preferredWorldPosition.HasValue)
                    PositionTurretSwitch(entity, existingSwitch, preferredWorldPosition);
                if (!switches.Contains(existingSwitch))
                    switches.Add(existingSwitch);
                return;
            }

            ElectricSwitch s = GameManager.server.CreateEntity(SwitchPrefab, entity.transform.position, entity.transform.rotation) as ElectricSwitch;
            if (s == null)
                return;

            s.OwnerID = entity.OwnerID;
            s.Spawn();
            s.SetParent(entity);
            s.gameObject.AddComponent<TurretSwitch>().Turret = entity;
            PositionTurretSwitch(entity, s, preferredWorldPosition);
            ConfigureSwitchEntity(s);
            switches.Add(s);
        }

        public void AddSwitchToSam(SamSite entity)
        {
            AddSwitchToSam(entity, null);
        }

        public void AddSwitchToSam(SamSite entity, Vector3? preferredWorldPosition)
        {
            AddSwitchToSam(entity, preferredWorldPosition, false);
        }

        private void AddSwitchToSam(SamSite entity, Vector3? preferredWorldPosition, bool allowRecentPlayerPlacement)
        {
            if (entity == null || entity.IsDestroyed)
                return;

            if (!ShouldHaveSwitch(entity, allowRecentPlayerPlacement))
            {
                RemoveSwitchChildren(entity);
                return;
            }

            var existingSwitch = FindSamSwitch(entity);
            if (existingSwitch != null)
            {
                var samSwitch = existingSwitch.GetComponent<SAMSwitch>() ?? existingSwitch.gameObject.AddComponent<SAMSwitch>();
                samSwitch.SamSite = entity;
                if (preferredWorldPosition.HasValue)
                    PositionSamSwitch(entity, existingSwitch, preferredWorldPosition);
                if (!switches.Contains(existingSwitch))
                    switches.Add(existingSwitch);
                return;
            }

            ElectricSwitch s = GameManager.server.CreateEntity(SwitchPrefab, entity.transform.position, entity.transform.rotation) as ElectricSwitch;
            if (s == null)
                return;

            s.OwnerID = entity.OwnerID;
            s.Spawn();
            s.SetParent(entity);
            s.gameObject.AddComponent<SAMSwitch>().SamSite = entity;
            PositionSamSwitch(entity, s, preferredWorldPosition);
            ConfigureSwitchEntity(s);
            switches.Add(s);
        }

        public void API_RepositionTurretSwitch(BaseEntity entity, Vector3 preferredWorldPosition)
        {
            var turret = entity as AutoTurret;
            if (turret == null)
                turret = entity?.GetComponent<AutoTurret>();

            AddSwitchToTurret(turret, preferredWorldPosition);
        }

        public void API_RepositionSamSwitch(BaseEntity entity, Vector3 preferredWorldPosition)
        {
            var samSite = entity as SamSite;
            if (samSite == null)
                samSite = entity?.GetComponent<SamSite>();

            AddSwitchToSam(samSite, preferredWorldPosition);
        }

        private void HandlePlayerPlacedEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || !player.userID.IsSteamId() || entity == null || entity.IsDestroyed)
                return;

            var preferredWorldPosition = player.transform.position;
            var turret = entity as AutoTurret;
            if (turret != null)
            {
                AddSwitchToTurret(turret, preferredWorldPosition, true);
                timer.Once(0.1f, () => AddSwitchToTurret(turret, preferredWorldPosition, true));
                return;
            }

            var samSite = entity as SamSite;
            if (samSite != null)
            {
                AddSwitchToSam(samSite, preferredWorldPosition, true);
                timer.Once(0.1f, () => AddSwitchToSam(samSite, preferredWorldPosition, true));
            }
        }

        private ElectricSwitch FindTurretSwitch(AutoTurret entity)
        {
            if (entity.children == null)
                return null;

            foreach (var child in entity.children)
            {
                if (child != null && (child.HasComponent<TurretSwitch>() || IsSimpleSwitch(child)))
                {
                    var electricSwitch = child as ElectricSwitch;
                    if (electricSwitch != null)
                        return electricSwitch;
                }
            }

            return null;
        }

        private ElectricSwitch FindSamSwitch(SamSite entity)
        {
            if (entity.children == null)
                return null;

            foreach (var child in entity.children)
            {
                if (child != null && (child.HasComponent<SAMSwitch>() || IsSimpleSwitch(child)))
                {
                    var electricSwitch = child as ElectricSwitch;
                    if (electricSwitch != null)
                        return electricSwitch;
                }
            }

            return null;
        }

        private void PositionTurretSwitch(AutoTurret entity, ElectricSwitch switchEntity, Vector3? preferredWorldPosition)
        {
            if (entity == null || switchEntity == null)
                return;

            var localPosition = GetTurretSwitchOffset(entity, preferredWorldPosition);
            switchEntity.transform.localPosition = localPosition;
            switchEntity.transform.localRotation = GetOutwardSwitchRotation(localPosition);
            switchEntity.SendNetworkUpdate();
        }

        private void PositionSamSwitch(SamSite entity, ElectricSwitch switchEntity, Vector3? preferredWorldPosition)
        {
            if (entity == null || switchEntity == null)
                return;

            var localPosition = GetClosestSwitchOffset(entity, preferredWorldPosition, SamSiteSwitchOffsets);
            switchEntity.transform.localPosition = localPosition;
            switchEntity.transform.localRotation = GetOutwardSwitchRotation(localPosition);
            switchEntity.SendNetworkUpdate();
        }

        private Vector3 GetTurretSwitchOffset(AutoTurret entity, Vector3? preferredWorldPosition)
        {
            if (!(entity is NPCAutoTurret))
                return GetClosestSwitchOffset(entity, preferredWorldPosition, VanillaTurretSwitchOffsets);

            return GetClosestSwitchOffset(entity, preferredWorldPosition, NpcSentryLegSwitchOffsets);
        }

        private Vector3 GetClosestSwitchOffset(BaseEntity entity, Vector3? preferredWorldPosition, Vector3[] offsets)
        {
            if (offsets == null || offsets.Length == 0)
                return Vector3.zero;

            if (entity == null || !preferredWorldPosition.HasValue)
                return offsets[0];

            var localDirection = entity.transform.InverseTransformDirection(preferredWorldPosition.Value - entity.transform.position);
            localDirection.y = 0f;
            if (localDirection.sqrMagnitude < 0.01f)
                return offsets[0];

            localDirection.Normalize();
            var bestOffset = offsets[0];
            var bestScore = float.MinValue;
            foreach (var offset in offsets)
            {
                var offsetDirection = new Vector3(offset.x, 0f, offset.z);
                if (offsetDirection.sqrMagnitude < 0.01f)
                    continue;

                offsetDirection.Normalize();
                var score = Vector3.Dot(localDirection, offsetDirection);
                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestOffset = offset;
            }

            return bestOffset;
        }

        private Quaternion GetOutwardSwitchRotation(Vector3 localPosition)
        {
            var outward = new Vector3(localPosition.x, 0f, localPosition.z);
            return outward.sqrMagnitude < 0.01f
                ? Quaternion.identity
                : Quaternion.LookRotation(outward.normalized, Vector3.up);
        }

        private bool ShouldHaveSwitch(BaseEntity entity, bool allowRecentPlayerPlacement = false)
        {
            if (entity == null || entity.IsDestroyed)
                return false;

            return entity.OwnerID.IsSteamId() || allowRecentPlayerPlacement;
        }

        private bool IsSimpleSwitch(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
                return false;

            return string.Equals(entity.ShortPrefabName, SwitchShortPrefabName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entity.PrefabName, SwitchPrefab, StringComparison.OrdinalIgnoreCase);
        }

        private void ConfigureSwitchEntity(ElectricSwitch switchEntity)
        {
            if (switchEntity == null)
                return;

            switchEntity.SetFlag(IOEntity.Flag_HasPower, true);
            switchEntity.InitializeHealth(float.MaxValue, float.MaxValue);
            GameObject.Destroy(switchEntity.GetComponent<GroundWatch>());
            GameObject.Destroy(switchEntity.GetComponent<DestroyOnGroundMissing>());
            switchEntity.SendNetworkUpdate();
        }

        private void RemoveSwitchChildren(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed || entity.children == null || entity.children.Count == 0)
                return;

            foreach (var child in entity.children.ToList())
            {
                if (child == null || child.IsDestroyed)
                    continue;

                if (child.HasComponent<TurretSwitch>() || child.HasComponent<SAMSwitch>() || IsSimpleSwitch(child))
                    KillSwitch(child);
            }
        }

        private void KillSwitch(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
                return;

            var electricSwitch = entity as ElectricSwitch;
            if (electricSwitch != null)
                switches.Remove(electricSwitch);

            entity.AdminKill();
        }
        #endregion
    }
    #region Other Classes
    public class TurretSwitch : MonoBehaviour
    {
        public AutoTurret Turret;

        public bool CanToggleTurret(BasePlayer player)
        {
            if (Turret == null)
                return false;

            if (TurretSwitches.config.RequiresPermission && !player.IPlayer.HasPermission(TurretSwitches.TurretPermission))
                return false;

            if (TurretSwitches.config.NeedsBuildingPrivilegeToUseSwitch && Turret.GetBuildingPrivilege() != null)
                return Turret.GetBuildingPrivilege().authorizedPlayers.ToList().Exists(x => x == player.userID);

            if (Turret.GetBuildingPrivilege() == null)
                return true;

            return true;
        }
        public void ToggleTurret(bool toggle)
        {
            if (Turret == null)
                return;
            Turret.SetFlag(IOEntity.Flag_HasPower, toggle);
            Turret.SetFlag(BaseEntity.Flags.Reserved8, toggle);
            if (toggle)
                Turret.InitiateStartup();
            else
            {
                Turret.InitiateShutdown();
                Turret.target = null;
            }
            Turret.SetIsOnline(toggle);
            Turret.SendNetworkUpdate();
        }
    }
    public class SAMSwitch : MonoBehaviour
    {
        public SamSite SamSite;
        public bool CanToggleSamSite(BasePlayer player)
        {
            if (SamSite == null)
                return false;

            if (TurretSwitches.config.RequiresPermission && !player.IPlayer.HasPermission(TurretSwitches.SAMPermission))
                return false;

            if (TurretSwitches.config.NeedsBuildingPrivilegeToUseSwitch && SamSite.GetBuildingPrivilege() != null)
                return SamSite.GetBuildingPrivilege().authorizedPlayers.ToList().Exists(x => x == player.userID);

            if (SamSite.GetBuildingPrivilege() == null)
                return true;

            return true;
        }
        public void ToggleTurret(bool toggle)
        {
            if (SamSite == null)
                return;
            SamSite.SetFlag(IOEntity.Flag_HasPower, toggle);
        }
    }
    public class TurretSwitchesConfig
    {
        public bool NeedsBuildingPrivilegeToUseSwitch = true;
        public bool RequiresPermission = true;
        public bool PlaySoundEffects = true;
    }
    #endregion
}
