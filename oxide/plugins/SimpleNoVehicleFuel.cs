using Oxide.Core.Plugins;

using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Simple No Vehicle Fuel", "Mabel", "1.1.2")]
    [Description("Removes requirement of fuel in all vehicles")]
	
    public class SimpleNoVehicleFuel : RustPlugin
    {
        [PluginReference] private Plugin Convoy;
        private readonly Dictionary<IFuelSystem, bool> convoyFuelSystems = new Dictionary<IFuelSystem, bool>();

        object OnFuelCheck(IFuelSystem fuelSystem)
        {
            if (fuelSystem == null)
                return null;

            if (Convoy != null)
            {
                bool isConvoy;
                if (!convoyFuelSystems.TryGetValue(fuelSystem, out isConvoy))
                {
                    isConvoy = Convoy.Call<bool>("IsConvoyVehicle", fuelSystem);
                    if (convoyFuelSystems.Count >= 2048)
                        convoyFuelSystems.Clear();
                    convoyFuelSystems[fuelSystem] = isConvoy;
                }

                if (isConvoy)
                    return null;
            }

            return true;
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "Convoy") convoyFuelSystems.Clear();
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "Convoy") convoyFuelSystems.Clear();
        }

        private void Unload() => convoyFuelSystems.Clear();
    }
}
