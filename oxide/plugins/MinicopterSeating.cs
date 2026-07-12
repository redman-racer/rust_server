using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Minicopter Seating", "Bazz3l", "1.1.7")]
    [Description("Spawns one extra seat on the right side of the minicopter.")]
    internal class MinicopterSeating : RustPlugin
    {
        #region Fields

        private readonly GameObjectRef _gameObjectSeat = new GameObjectRef
        {
            guid = "dc329880dec7ab343bc454fd969d5709"
        };

        private readonly Vector3 _rightSeat = new Vector3(0.6f, 0.2f, -0.3f);

        #endregion

        #region Oxide Hooks

        private void OnServerInitialized() => Subscribe("OnEntitySpawned");

        private void Init() => Unsubscribe("OnEntitySpawned");

        private void OnEntitySpawned(Minicopter copter)
        {
            if (copter == null ||
                copter.IsDestroyed ||
                copter.mountPoints == null ||
                copter is ScrapTransportHelicopter)
            {
                return;
            }

            if (copter.mountPoints.Count >= 3)
                return;

            SetupSeating(copter);
        }

        #endregion

        #region Core

        private void SetupSeating(BaseVehicle vehicle)
        {
            vehicle.mountPoints.Add(
                CreateMount(vehicle.mountPoints[1], _rightSeat)
            );
        }

        private BaseVehicle.MountPointInfo CreateMount(
            BaseVehicle.MountPointInfo mountPoint,
            Vector3 position)
        {
            return new BaseVehicle.MountPointInfo
            {
                pos = position,
                rot = mountPoint.rot,
                prefab = _gameObjectSeat,
                mountable = mountPoint.mountable
            };
        }

        #endregion
    }
}