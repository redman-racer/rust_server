/*
 * Copyright (c) 2022 Bazz3l
 *
 * Minicopter Seating cannot be copied, edited and/or (re)distributed without the express permission of Bazz3l.
 * Discord bazz3l
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 */

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Minicopter Seating", "Bazz3l", "1.1.7")]
    [Description("Spawn extra seats on each side of the minicopter.")]
    internal class MinicopterSeating : RustPlugin
    {
        #region Fields
        
        private readonly GameObjectRef _gameObjectSeat = new GameObjectRef { guid = "dc329880dec7ab343bc454fd969d5709" };

        private readonly Vector3 _seat1 = new Vector3(0.6f, 0.2f, -0.3f);
        private readonly Vector3 _seat2 = new Vector3(-0.6f, 0.2f, -0.3f);

        #endregion

        #region Oxide Hooks

        private void OnServerInitialized() => Subscribe("OnEntitySpawned");

        private void Init() => Unsubscribe("OnEntitySpawned");

        private void OnEntitySpawned(Minicopter copter)
        {
            if (copter == null || copter.IsDestroyed || copter.mountPoints == null || copter is ScrapTransportHelicopter)
                return;
            
            if (copter.mountPoints.Count >= 4)
                return;

            SetupSeating(copter);
        }

        #endregion

        #region Core

        private void SetupSeating(BaseVehicle vehicle)
        {
            vehicle.mountPoints.Add(CreateMount(vehicle.mountPoints[1], _seat1));
            vehicle.mountPoints.Add(CreateMount(vehicle.mountPoints[1], _seat2));
        }

        private BaseVehicle.MountPointInfo CreateMount(BaseVehicle.MountPointInfo mountPoint, Vector3 position)
        {
            return new BaseVehicle.MountPointInfo
           {
               pos = position,
               rot = mountPoint.rot,
               prefab = _gameObjectSeat,
               mountable = mountPoint.mountable,
           };
        }

        #endregion
    }
}