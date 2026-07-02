using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("SpawnStats", "YourName", "1.0.0")]
    [Description("Sets health, hunger, and thirst on respawn.")]
    public class SpawnStats : RustPlugin
    {
        private const float SpawnHealth = 100f;
        private const float SpawnHunger = 2000f;
        private const float SpawnThirst = 2000f;

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
                return;

            // Health
            player.SetHealth(SpawnHealth);

            // Hunger + Thirst (metabolism)
            player.metabolism.calories.value = SpawnHunger;
            player.metabolism.hydration.value = SpawnThirst;

            // Apply updates
            player.metabolism.SendChanges();
            player.SendNetworkUpdate();
        }
    }
}