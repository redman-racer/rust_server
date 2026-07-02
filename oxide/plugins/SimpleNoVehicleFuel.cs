using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Simple No Vehicle Fuel", "Mabel", "1.1.1")]
    [Description("Removes requirement of fuel in all vehicles")]
	
    public class SimpleNoVehicleFuel : RustPlugin
    {
        [PluginReference] private Plugin Convoy;

        object OnFuelCheck(IFuelSystem fuelSystem)
        {
            if (fuelSystem == null)
                return null;

            if (Convoy != null)
            {
                var isConvoy = Convoy.Call("IsConvoyVehicle", fuelSystem);
                if (isConvoy is bool b && b)
                    return null;
            }

            return true;
        }
    }
}