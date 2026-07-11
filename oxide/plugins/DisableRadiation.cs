using System;
using UnityEngine.Experimental;

namespace Oxide.Plugins
{
    [Info("Disable Radiation", "SwenenzY/Bushhy", "1.0.2")]
    [Description("Disable radiation with permission")]
    class DisableRadiation : CovalencePlugin
    {
        private const string permUse = "disableradiation.use";

        private void Init()
        {
            permission.RegisterPermission(permUse, this);
        }

        private void OnUserPermissionGranted(string playerId, string perm)
        {
            if (perm == permUse)
            {
                BasePlayer basePlayer = BasePlayer.FindByID(Convert.ToUInt64(playerId));
                if (basePlayer != null)
                {
                    basePlayer.metabolism.radiation_level.max = 0;
                    basePlayer.metabolism.radiation_poison.max = 0;
                }
            }
        }

        private void OnUserPermissionRevoked(string playerId, string perm)
        {
            if (perm == permUse)
            {
                BasePlayer basePlayer = BasePlayer.FindByID(Convert.ToUInt64(playerId));
                if (basePlayer != null)
                {
                    basePlayer.metabolism.radiation_level.max = 500;
                    basePlayer.metabolism.radiation_poison.max = 500;
                }
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            player.metabolism.radiation_level.max = permission.UserHasPermission(player.UserIDString, permUse) ? 0 : 500;
            player.metabolism.radiation_poison.max = permission.UserHasPermission(player.UserIDString, permUse) ? 0 : 500;
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            player.metabolism.radiation_level.max = 500;
            player.metabolism.radiation_poison.max = 500;
        }
    }
}
