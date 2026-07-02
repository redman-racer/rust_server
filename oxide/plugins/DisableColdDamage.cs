using System.Linq;

namespace Oxide.Plugins
{
    [Info("Disable Cold Damage", "Talha", "1.0.4")]
    [Description("Prevents cold damage for players, with permission.")]
    public class DisableColdDamage : RustPlugin
    { 
        private const string permDisable = "disablecolddamage.use";
        void Init() { permission.RegisterPermission(permDisable, this); }
        void OnServerInitialized() { CheckAll(); }
        void OnGroupPermissionGranted() { CheckAll(); }
        void OnGroupPermissionRevoked() { CheckAll(); }
        void OnPlayerConnected(BasePlayer player) { Cold(player); }
        void Unload() 
        {
            foreach (var player in BasePlayer.activePlayerList.ToList()) { player.metabolism.temperature.min = -100; }
        }
        void CheckAll()
        {
            foreach (var player in BasePlayer.activePlayerList.ToList()) { Cold(player); }
        }
        void Cold(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, permDisable)) {
                player.metabolism.temperature.value = 20;
                player.metabolism.temperature.min = 20;
            }
            else
            {
                player.metabolism.temperature.min = -100;
            }
        }
    }
}