using System;
using System.Collections.Generic;


//TODO Blacklist, different multi based on each item. Different NPC vending shop support.
namespace Oxide.Plugins
{
    [Info("NPC Vender Modifier", "Default", "1.0.1")]
    [Description("Allows changing the multiplier of the NPC shops")]
    public class NPCVenderModifier : RustPlugin
    {
        int shopMulti;
        bool Changed = false;
        private const string permissionName = "npcvendermodifier.use";


        object OnGiveSoldItem(NPCVendingMachine vending, Item soldItem, BasePlayer buyer)
        {
            if (vending.OwnerID >= 1)
            {
                return null;
            }

            if (soldItem == null)
            {
                return null;
            }

            if (!permission.UserHasPermission(buyer.UserIDString, permissionName))
            {
                soldItem.amount = soldItem.amount * 1;
                buyer.GiveItem(soldItem);
                return soldItem.amount;
            }

            soldItem.amount = soldItem.amount * shopMulti;
            buyer.GiveItem(soldItem);

            return soldItem.amount;
        }


        void Init()
        {
            LoadVariables();
            permission.RegisterPermission(permissionName, this);
        }

        object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                Changed = true;
            }
            object value;
            if (!data.TryGetValue(datavalue, out value))
            {
                value = defaultValue;
                data[datavalue] = value;
                Changed = true;
            }
            return value;
        }

        void LoadVariables()
        {

            shopMulti = Convert.ToInt32(GetConfig("Vendor", "Multiplier", 2));
            //medkitPendingAmount = Convert.ToSingle(GetConfig("Medkits", "Pending health to add", 35f));

            if (!Changed) return;
            SaveConfig();
            Changed = false;
        }

        protected override void LoadDefaultConfig()
        {
            LoadVariables();
        }



    }
}