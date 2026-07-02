using System;
namespace Oxide.Plugins
{
    [Info("Disable Wet", "SwenenzY", "1.0.4")]
    [Description("disable wet count, with permission.")]
    public class DisableWet : RustPlugin
    {
        private const string Perm = "disablewet.use";

        private void Init()
        {
             permission.RegisterPermission(Perm, this);
        }
        private void OnUserPermissionGranted(string id, string permName)
        {
            if (permName != Perm) return;
            var basePlayer = BasePlayer.FindByID(Convert.ToUInt64(id));
            if (basePlayer == null)
            {
                return;       
            }
            basePlayer.metabolism.wetness.max = 0;
        }

        private void OnUserPermissionRevoked(string id, string permName)
        {
            if (permName != Perm) return;
            var basePlayer = BasePlayer.FindByID(Convert.ToUInt64(id));
            if (basePlayer == null)
            {
                return;  
            }
            basePlayer.metabolism.wetness.max = 0;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
        if (player == null) return;
        player.metabolism.wetness.max = permission.UserHasPermission(player.userID.ToString(), Perm) ? 0 : 100;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
        if (player == null) return;
        player.metabolism.wetness.max = 9;
        }

    }
}