using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Turret Switches", "ziptie/Raidlands", 1.7)]
    [Description("Spawns switches on turrets and SAM sites for players with permission.")]
    public class TurretSwitches : CovalencePlugin
    {
        private const string SwitchPrefab = "assets/prefabs/io/electric/switches/simpleswitch/simpleswitch.prefab";
        private static readonly Vector3 VanillaTurretSwitchOffset = new Vector3(0f, -0.65f, 0.3f);
        private static readonly Vector3[] NpcSentryLegSwitchOffsets =
        {
            new Vector3(0f, -0.62f, 0.9f),
            new Vector3(0.78f, -0.62f, -0.46f),
            new Vector3(-0.78f, -0.62f, -0.46f)
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
            AddSwitchToTurret(entity);
        }
        void OnEntitySpawned(SamSite entity)
        {
            ElectricSwitch s = GameManager.server.CreateEntity(SwitchPrefab, new Vector3(0, -0.65f, .95f), Quaternion.identity) as ElectricSwitch;
            s.Spawn();
            s.SetParent(entity);
            s.gameObject.AddComponent<SAMSwitch>().SamSite = entity;
            s.SetFlag(IOEntity.Flag_HasPower, true);
            s.InitializeHealth(float.MaxValue, float.MaxValue);
            GameObject.Destroy(s.GetComponent<GroundWatch>());
            GameObject.Destroy(s.GetComponent<DestroyOnGroundMissing>());
            switches.Add(s);
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
            foreach (var item in switches)
            {
                item.AdminKill();
            }
        }
        public void AddSwitchesToAllTurrets()
        {
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                AddSwitchToTurret(entity as AutoTurret);

                if (entity is SamSite)
                {
                    ElectricSwitch s = GameManager.server.CreateEntity(SwitchPrefab, new Vector3(0, -0.65f, .95f), Quaternion.identity) as ElectricSwitch;
                    s.Spawn();
                    s.SetParent((BaseEntity)entity);
                    s.gameObject.AddComponent<SAMSwitch>().SamSite = entity.GetComponent<SamSite>();
                    s.SetFlag(IOEntity.Flag_HasPower, true);
                    s.InitializeHealth(float.MaxValue, float.MaxValue);
                    GameObject.Destroy(s.GetComponent<GroundWatch>());
                    GameObject.Destroy(s.GetComponent<DestroyOnGroundMissing>());
                }
            }
        }

        public void AddSwitchToTurret(AutoTurret entity)
        {
            AddSwitchToTurret(entity, null);
        }

        public void AddSwitchToTurret(AutoTurret entity, Vector3? preferredWorldPosition)
        {
            if (entity == null || entity.IsDestroyed)
                return;

            var existingSwitch = FindTurretSwitch(entity);
            if (existingSwitch != null)
            {
                PositionTurretSwitch(entity, existingSwitch, preferredWorldPosition);
                if (!switches.Contains(existingSwitch))
                    switches.Add(existingSwitch);
                return;
            }

            ElectricSwitch s = GameManager.server.CreateEntity(SwitchPrefab, entity.transform.position, entity.transform.rotation) as ElectricSwitch;
            s.Spawn();
            s.SetParent(entity);
            s.gameObject.AddComponent<TurretSwitch>().Turret = entity;
            PositionTurretSwitch(entity, s, preferredWorldPosition);
            s.SetFlag(IOEntity.Flag_HasPower, true);
            s.InitializeHealth(float.MaxValue, float.MaxValue);
            GameObject.Destroy(s.GetComponent<GroundWatch>());
            GameObject.Destroy(s.GetComponent<DestroyOnGroundMissing>());
            switches.Add(s);
        }

        public void API_RepositionTurretSwitch(BaseEntity entity, Vector3 preferredWorldPosition)
        {
            var turret = entity as AutoTurret;
            if (turret == null)
                turret = entity?.GetComponent<AutoTurret>();

            AddSwitchToTurret(turret, preferredWorldPosition);
        }

        private ElectricSwitch FindTurretSwitch(AutoTurret entity)
        {
            if (entity.children == null)
                return null;

            foreach (var child in entity.children)
            {
                if (child != null && child.HasComponent<TurretSwitch>())
                    return child as ElectricSwitch;
            }

            return null;
        }

        private void PositionTurretSwitch(AutoTurret entity, ElectricSwitch switchEntity, Vector3? preferredWorldPosition)
        {
            if (entity == null || switchEntity == null)
                return;

            var localPosition = GetTurretSwitchOffset(entity, preferredWorldPosition);
            switchEntity.transform.localPosition = localPosition;

            if (entity is NPCAutoTurret)
                switchEntity.transform.localRotation = GetOutwardSwitchRotation(localPosition);
            else
                switchEntity.transform.localRotation = Quaternion.identity;

            switchEntity.SendNetworkUpdate();
        }

        private Vector3 GetTurretSwitchOffset(AutoTurret entity, Vector3? preferredWorldPosition)
        {
            if (!(entity is NPCAutoTurret))
                return VanillaTurretSwitchOffset;

            if (!preferredWorldPosition.HasValue)
                return NpcSentryLegSwitchOffsets[0];

            var localDirection = entity.transform.InverseTransformDirection(preferredWorldPosition.Value - entity.transform.position);
            localDirection.y = 0f;
            if (localDirection.sqrMagnitude < 0.01f)
                return NpcSentryLegSwitchOffsets[0];

            localDirection.Normalize();
            var bestOffset = NpcSentryLegSwitchOffsets[0];
            var bestScore = float.MinValue;
            foreach (var offset in NpcSentryLegSwitchOffsets)
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
            if (TurretSwitches.config.RequiresPermission && !player.IPlayer.HasPermission(TurretSwitches.SAMPermission))
                return false;

            if (TurretSwitches.config.NeedsBuildingPrivilegeToUseSwitch && SamSite.GetBuildingPrivilege() != null)
                return SamSite.GetBuildingPrivilege().authorizedPlayers.ToList().Exists(x => x == player.userID);

            if (SamSite == null)
                return false;

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
