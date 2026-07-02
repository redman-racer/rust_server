using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Turret Switches", "ziptie", 1.5)]
    [Description("Spawns switches on turrets and SAM sites for players with permission.")]
    public class TurretSwitches : CovalencePlugin
    {
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
            ElectricSwitch s = GameManager.server.CreateEntity("assets/prefabs/io/electric/switches/simpleswitch/simpleswitch.prefab", new Vector3(0, -0.65f, .3f), Quaternion.identity) as ElectricSwitch;
            s.Spawn();
            s.SetParent(entity);
            s.gameObject.AddComponent<TurretSwitch>().Turret = entity;
            s.SetFlag(IOEntity.Flag_HasPower, true);
            s.InitializeHealth(float.MaxValue, float.MaxValue);
            GameObject.Destroy(s.GetComponent<GroundWatch>());
            GameObject.Destroy(s.GetComponent<DestroyOnGroundMissing>());
            switches.Add(s);
        }
        void OnEntitySpawned(SamSite entity)
        {
            ElectricSwitch s = GameManager.server.CreateEntity("assets/prefabs/io/electric/switches/simpleswitch/simpleswitch.prefab", new Vector3(0, -0.65f, .95f), Quaternion.identity) as ElectricSwitch;
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
                if (entity.PrefabName == "assets/prefabs/npc/autoturret/autoturret_deployed.prefab")
                {
                    ElectricSwitch s = GameManager.server.CreateEntity("assets/prefabs/io/electric/switches/simpleswitch/simpleswitch.prefab", new Vector3(0, -0.65f, .3f), Quaternion.identity) as ElectricSwitch;
                    s.Spawn();
                    s.SetParent((BaseEntity)entity);
                    s.gameObject.AddComponent<TurretSwitch>().Turret = entity.GetComponent<AutoTurret>();
                    s.SetFlag(IOEntity.Flag_HasPower, true);
                    s.InitializeHealth(float.MaxValue, float.MaxValue);
                    GameObject.Destroy(s.GetComponent<GroundWatch>());
                    GameObject.Destroy(s.GetComponent<DestroyOnGroundMissing>());
                    switches.Add(s);
                }
                if (entity is SamSite)
                {
                    ElectricSwitch s = GameManager.server.CreateEntity("assets/prefabs/io/electric/switches/simpleswitch/simpleswitch.prefab", new Vector3(0, -0.65f, .95f), Quaternion.identity) as ElectricSwitch;
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
        #endregion
    }
    #region Other Classes
    public class TurretSwitch : MonoBehaviour
    {
        public AutoTurret Turret;

        public bool CanToggleTurret(BasePlayer player)
        {
            if (TurretSwitches.config.RequiresPermission && !player.IPlayer.HasPermission(TurretSwitches.TurretPermission))
                return false;

            if (TurretSwitches.config.NeedsBuildingPrivilegeToUseSwitch && Turret.GetBuildingPrivilege() != null)
                return Turret.GetBuildingPrivilege().authorizedPlayers.ToList().Exists(x => x == player.userID);

            if (Turret == null)
                return false;

            if (Turret.GetBuildingPrivilege() == null)
                return true;

            return true;
        }
        public void ToggleTurret(bool toggle)
        {
            if (Turret == null)
                return;
            Turret.SetFlag(IOEntity.Flag_HasPower, toggle);
            Turret.SetIsOnline(toggle);
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