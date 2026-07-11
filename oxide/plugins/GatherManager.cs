using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Gather Manager", "Mughisi/Updated", "2.2.84-shortname")]
    [Description("Increases the amount of items gained from gathering resources")]
    class GatherManager : RustPlugin
    {
        #region Configuration Data
        // Do not modify these values because this will not change anything, the values listed below are only used to create
        // the initial configuration file. If you wish changes to the configuration file you should edit 'GatherManager.json'
        // which is located in your server's config folder: <drive>:\...\server\<your_server_identity>\oxide\config\

        private bool configChanged;

        // Plugin settings
        private const string DefaultChatPrefix = "Gather Manager";
        private const string DefaultChatPrefixColor = "#008000ff";

        public string ChatPrefix { get; private set; }
        public string ChatPrefixColor { get; private set; }

        // Plugin options
        private static readonly Dictionary<string, object> DefaultGatherResourceModifiers = new Dictionary<string, object>();
        private static readonly Dictionary<string, object> DefaultGatherDispenserModifiers = new Dictionary<string, object>();
        private static readonly Dictionary<string, object> DefaultQuarryResourceModifiers = new Dictionary<string, object>();
        private static readonly Dictionary<string, object> DefaultPickupResourceModifiers = new Dictionary<string, object>();
        private static readonly Dictionary<string, object> DefaultSurveyResourceModifiers = new Dictionary<string, object>();

        // Defaults
        private const float DefaultMiningQuarryResourceTickRate = 5f;
        private const float DefaultExcavatorResourceTickRate = 3f;
        private const float DefaultExcavatorTimeForFullResources = 120f;
        private const float DefaultExcavatorBeltSpeedMax = 0.1f;

        public Dictionary<string, float> GatherResourceModifiers { get; private set; }
        public Dictionary<string, float> GatherDispenserModifiers { get; private set; }
        public Dictionary<string, float> QuarryResourceModifiers { get; private set; }
        public Dictionary<string, float> ExcavatorResourceModifiers { get; private set; }
        public Dictionary<string, float> PickupResourceModifiers { get; private set; }
        public Dictionary<string, float> SurveyResourceModifiers { get; private set; }
        public float MiningQuarryResourceTickRate { get; private set; }
        public float ExcavatorResourceTickRate { get; private set; }
        public float ExcavatorTimeForFullResources { get; private set; }
        public float ExcavatorBeltSpeedMax { get; private set; }

        // Plugin messages
        private const string DefaultNotAllowed = "You don't have permission to use this command.";
        private const string DefaultInvalidArgumentsGather =
            "Invalid arguments supplied! Use gather.rate <type:dispenser|pickup|quarry|excavator|survey> <resource> <multiplier>";
        private const string DefaultInvalidArgumentsDispenser =
            "Invalid arguments supplied! Use dispenser.scale <dispenser:tree|ore|corpse> <multiplier>";
        private const string DefaultInvalidArgumentsSpeed =
            "Invalid arguments supplied! Use quarry.tickrate <seconds> or excavator.tickrate <seconds>";
        private const string DefaultInvalidModifier =
            "Invalid modifier supplied! The new modifier always needs to be bigger than 0!";
        private const string DefaultInvalidSpeed = "You can't set the speed lower than 1 second!";
        private const string DefaultModifyResource = "You have set the gather rate for {0} to x{1} from {2}.";
        private const string DefaultModifyResourceRemove = "You have reset the gather rate for {0} from {1}.";
        private const string DefaultModifySpeed = "{0} will now provide resources every {1} seconds.";
        private const string DefaultInvalidResource =
            "{0} was accepted as a custom/shortname resource key. If rates do not apply, run gather.resources and use the listed shortname.";
        private const string DefaultModifyDispenser = "You have set the resource amount for {0} dispensers to x{1}";
        private const string DefaultInvalidDispenser =
            "{0} is not a valid dispenser. Check gather.dispensers for a list of available options.";

        private const string DefaultHelpText = "/gather - Shows you detailed gather information.";
        private const string DefaultHelpTextPlayer = "Resources gained from gathering have been scaled to the following:";
        private const string DefaultHelpTextAdmin = "To change the resources gained by gathering use the command:\r\ngather.rate <type:dispenser|pickup|quarry|excavator|survey> <resource> <multiplier>\r\nTo change the amount of resources in a dispenser type use the command:\r\ndispenser.scale <dispenser:tree|ore|corpse|flesh> <multiplier>\r\nTo change the time between Mining Quarry gathers:\r\nquarry.tickrate <seconds>\r\nTo change the time between Excavator gathers:\r\nexcavator.tickrate <seconds>";
        private const string DefaultHelpTextPlayerGains = "Resources gained from {0}:";
        private const string DefaultHelpTextPlayerMiningQuarrySpeed = "Time between Mining Quarry gathers: {0} second(s).";
        private const string DefaultHelpTextPlayerExcavatorSpeed = "Time between Excavator gathers: {0} second(s).";
        private const string DefaultHelpTextPlayerDefault = "Default values.";
        private const string DefaultDispensers = "Resource Dispensers";
        private const string DefaultCharges = "Survey Charges";
        private const string DefaultQuarries = "Mining Quarries";
        private const string DefaultExcavators = "Excavators";
        private const string DefaultPickups = "pickups";

        public string NotAllowed { get; private set; }
        public string InvalidArgumentsGather { get; private set; }
        public string InvalidArgumentsDispenser { get; private set; }
        public string InvalidArgumentsSpeed { get; private set; }
        public string InvalidModifier { get; private set; }
        public string InvalidSpeed { get; private set; }
        public string ModifyResource { get; private set; }
        public string ModifyResourceRemove { get; private set; }
        public string ModifySpeed { get; private set; }
        public string InvalidResource { get; private set; }
        public string ModifyDispenser { get; private set; }
        public string InvalidDispenser { get; private set; }
        public string HelpText { get; private set; }
        public string HelpTextPlayer { get; private set; }
        public string HelpTextAdmin { get; private set; }
        public string HelpTextPlayerGains { get; private set; }
        public string HelpTextPlayerDefault { get; private set; }
        public string HelpTextPlayerMiningQuarrySpeed { get; private set; }
        public string HelpTextPlayerExcavatorSpeed { get; private set; }
        public string Dispensers { get; private set; }
        public string Charges { get; private set; }
        public string Quarries { get; private set; }
        public string Excavators { get; private set; }
        public string Pickups { get; private set; }

        #endregion

        private readonly List<string> subcommands = new List<string>() { "dispenser", "pickup", "quarry", "excavator", "survey" };

        private readonly Hash<string, ItemDefinition> validResources = new Hash<string, ItemDefinition>();

        private readonly Hash<string, ResourceDispenser.GatherType> validDispensers = new Hash<string, ResourceDispenser.GatherType>();

        private void Init() => LoadConfigValues();

        private void OnServerInitialized()
        {
            Puts("GatherManager shortname build loaded: accepts metal.ore, sulfur.ore, hq.metal.ore");
            var resourceDefinitions = ItemManager.itemList;
            foreach (var def in resourceDefinitions.Where(def => def.category == ItemCategory.Food || def.category == ItemCategory.Resources))
            {
                if (!string.IsNullOrEmpty(def.displayName?.english))
                    validResources[def.displayName.english.ToLower()] = def;
                if (!string.IsNullOrEmpty(def.shortname))
                    validResources[def.shortname.ToLower()] = def;
                validResources[def.itemid.ToString()] = def;
            }

            validDispensers.Add("tree", ResourceDispenser.GatherType.Tree);
            validDispensers.Add("ore", ResourceDispenser.GatherType.Ore);
            validDispensers.Add("corpse", ResourceDispenser.GatherType.Flesh);
            validDispensers.Add("flesh", ResourceDispenser.GatherType.Flesh);

            foreach (var excavator in UnityEngine.Object.FindObjectsOfType<ExcavatorArm>())
                ApplyExcavatorSettings(excavator);
        }

        private void Unload()
        {
            foreach (var excavator in UnityEngine.Object.FindObjectsOfType<ExcavatorArm>())
                ResetExcavatorSettings(excavator);
        }



        private void ApplyExcavatorSettings(ExcavatorArm excavator)
        {
            if (excavator == null) return;

            if (ExcavatorResourceTickRate != DefaultExcavatorResourceTickRate)
            {
                excavator.CancelInvoke("ProcessResources");
                excavator.InvokeRepeating("ProcessResources", ExcavatorResourceTickRate, ExcavatorResourceTickRate);
            }

            if (ExcavatorBeltSpeedMax != DefaultExcavatorBeltSpeedMax)
                excavator.beltSpeedMax = ExcavatorBeltSpeedMax;

            if (ExcavatorTimeForFullResources != DefaultExcavatorTimeForFullResources)
                excavator.timeForFullResources = ExcavatorTimeForFullResources;
        }

        private void ResetExcavatorSettings(ExcavatorArm excavator)
        {
            if (excavator == null) return;

            if (ExcavatorResourceTickRate != DefaultExcavatorResourceTickRate)
            {
                excavator.CancelInvoke("ProcessResources");
                excavator.InvokeRepeating("ProcessResources", DefaultExcavatorResourceTickRate, DefaultExcavatorResourceTickRate);
            }

            if (ExcavatorBeltSpeedMax != DefaultExcavatorBeltSpeedMax)
                excavator.beltSpeedMax = DefaultExcavatorBeltSpeedMax;

            if (ExcavatorTimeForFullResources != DefaultExcavatorTimeForFullResources)
                excavator.timeForFullResources = DefaultExcavatorTimeForFullResources;
        }

        private void OnEntitySpawned(ExcavatorArm excavator)
        {
            if (excavator == null) return;
            NextTick(() => ApplyExcavatorSettings(excavator));
        }

        protected override void LoadDefaultConfig() => PrintWarning("New configuration file created.");

        [ChatCommand("gather")]
        private void Gather(BasePlayer player, string command, string[] args)
        {
            var help = HelpTextPlayer;
            if (GatherResourceModifiers.Count == 0 && SurveyResourceModifiers.Count == 0 && PickupResourceModifiers.Count == 0 && QuarryResourceModifiers.Count == 0 && ExcavatorResourceModifiers.Count == 0)
                help += HelpTextPlayerDefault;
            else
            {
                if (GatherResourceModifiers.Count > 0)
                {
                    var dispensers = string.Format(HelpTextPlayerGains, Dispensers);
                    dispensers = GatherResourceModifiers.Aggregate(dispensers, (current, entry) => current + ("\r\n    " + entry.Key + ": x" + entry.Value));
                    help += "\r\n" + dispensers;
                }
                if (PickupResourceModifiers.Count > 0)
                {
                    var pickups = string.Format(HelpTextPlayerGains, Pickups);
                    pickups = PickupResourceModifiers.Aggregate(pickups, (current, entry) => current + ("\r\n    " + entry.Key + ": x" + entry.Value));
                    help += "\r\n" + pickups;
                }
                if (QuarryResourceModifiers.Count > 0)
                {
                    var quarries = string.Format(HelpTextPlayerGains, Quarries);
                    quarries = QuarryResourceModifiers.Aggregate(quarries, (current, entry) => current + ("\r\n    " + entry.Key + ": x" + entry.Value));
                    help += "\r\n" + quarries;
                }
                if (ExcavatorResourceModifiers.Count > 0)
                {
                    var excavators = string.Format(HelpTextPlayerGains, Excavators);
                    excavators = ExcavatorResourceModifiers.Aggregate(excavators, (current, entry) => current + ("\r\n    " + entry.Key + ": x" + entry.Value));
                    help += "\r\n" + excavators;
                }
                if (SurveyResourceModifiers.Count > 0)
                {
                    var charges = string.Format(HelpTextPlayerGains, Charges);
                    charges = SurveyResourceModifiers.Aggregate(charges, (current, entry) => current + ("\r\n    " + entry.Key + ": x" + entry.Value));
                    help += "\r\n" + charges;
                }
            }

            if (MiningQuarryResourceTickRate != DefaultMiningQuarryResourceTickRate)
                help += "\r\n" + string.Format(HelpTextPlayerMiningQuarrySpeed, MiningQuarryResourceTickRate);

            if (ExcavatorResourceTickRate != DefaultExcavatorResourceTickRate)
                help += "\r\n" + string.Format(HelpTextPlayerExcavatorSpeed, ExcavatorResourceTickRate);

            SendMessage(player, help);
            if (!player.IsAdmin) return;
            SendMessage(player, HelpTextAdmin);
        }

        private void SendHelpText(BasePlayer player) => SendMessage(player, HelpText);

        [ConsoleCommand("gather.rate")]
        private void GatherRate(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith(NotAllowed);
                return;
            }

            if (!arg.HasArgs(3))
            {
                arg.ReplyWith(InvalidArgumentsGather);
                return;
            }

            var subcommand = arg.GetString(0).ToLower();
            if (!subcommands.Contains(subcommand))
            {
                arg.ReplyWith(InvalidArgumentsGather);
                return;
            }

            var resourceArg = arg.GetString(1);
            var resource = ResolveResourceKey(resourceArg);
            var modifier = arg.GetFloat(2, -1);
            var remove = false;
            if (modifier < 0)
            {
                if (arg.GetString(2).ToLower() == "remove")
                    remove = true;
                else
                {
                    arg.ReplyWith(InvalidModifier);
                    return;
                }
            }

            switch (subcommand)
            {
                case "dispenser":
                    if (remove)
                    {
                        if (GatherResourceModifiers.ContainsKey(resource))
                            GatherResourceModifiers.Remove(resource);
                        arg.ReplyWith(string.Format(ModifyResourceRemove, resource, Dispensers));
                    }
                    else
                    {
                        if (GatherResourceModifiers.ContainsKey(resource))
                            GatherResourceModifiers[resource] = modifier;
                        else
                            GatherResourceModifiers.Add(resource, modifier);
                        arg.ReplyWith(string.Format(ModifyResource, resource, modifier, Dispensers));
                    }
                    SetConfigValue("Options", "GatherResourceModifiers", GatherResourceModifiers);
                    break;
                case "pickup":
                    if (remove)
                    {
                        if (PickupResourceModifiers.ContainsKey(resource))
                            PickupResourceModifiers.Remove(resource);
                        arg.ReplyWith(string.Format(ModifyResourceRemove, resource, Pickups));
                    }
                    else
                    {
                        if (PickupResourceModifiers.ContainsKey(resource))
                            PickupResourceModifiers[resource] = modifier;
                        else
                            PickupResourceModifiers.Add(resource, modifier);
                        arg.ReplyWith(string.Format(ModifyResource, resource, modifier, Pickups));
                    }
                    SetConfigValue("Options", "PickupResourceModifiers", PickupResourceModifiers);
                    break;
                case "quarry":
                    if (remove)
                    {
                        if (QuarryResourceModifiers.ContainsKey(resource))
                            QuarryResourceModifiers.Remove(resource);
                        arg.ReplyWith(string.Format(ModifyResourceRemove, resource, Quarries));
                    }
                    else
                    {
                        if (QuarryResourceModifiers.ContainsKey(resource))
                            QuarryResourceModifiers[resource] = modifier;
                        else
                            QuarryResourceModifiers.Add(resource, modifier);
                        arg.ReplyWith(string.Format(ModifyResource, resource, modifier, Quarries));
                    }
                    SetConfigValue("Options", "QuarryResourceModifiers", QuarryResourceModifiers);
                    break;
                case "excavator":
                    if (remove)
                    {
                        if (ExcavatorResourceModifiers.ContainsKey(resource))
                            ExcavatorResourceModifiers.Remove(resource);
                        arg.ReplyWith(string.Format(ModifyResourceRemove, resource, Excavators));
                    }
                    else
                    {
                        if (ExcavatorResourceModifiers.ContainsKey(resource))
                            ExcavatorResourceModifiers[resource] = modifier;
                        else
                            ExcavatorResourceModifiers.Add(resource, modifier);
                        arg.ReplyWith(string.Format(ModifyResource, resource, modifier, Excavators));
                    }
                    SetConfigValue("Options", "ExcavatorResourceModifiers", ExcavatorResourceModifiers);
                    break;
                case "survey":
                    if (remove)
                    {
                        if (SurveyResourceModifiers.ContainsKey(resource))
                            SurveyResourceModifiers.Remove(resource);
                        arg.ReplyWith(string.Format(ModifyResourceRemove, resource, Charges));
                    }
                    else
                    {
                        if (SurveyResourceModifiers.ContainsKey(resource))
                            SurveyResourceModifiers[resource] = modifier;
                        else
                            SurveyResourceModifiers.Add(resource, modifier);
                        arg.ReplyWith(string.Format(ModifyResource, resource, modifier, Charges));
                    }
                    SetConfigValue("Options", "SurveyResourceModifiers", SurveyResourceModifiers);
                    break;
            }
        }

        [ConsoleCommand("gather.resources")]
        private void GatherResources(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith(NotAllowed);
                return;
            }

            var resources = ItemManager.itemList
                .Where(def => def.category == ItemCategory.Food || def.category == ItemCategory.Resources)
                .OrderBy(def => GetResourceName(def))
                .Aggregate("Available resources:\r\n", (current, def) => current + ($"{GetResourceName(def)} ({def.shortname})\r\n"));
            arg.ReplyWith(resources + "* (For all resources that are not setup separately)\r\nAliases: metal.ore, sulfur.ore, hq.metal.ore");
        }

        [ConsoleCommand("gather.dispensers")]
        private void GatherDispensers(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith(NotAllowed);
                return;
            }

            arg.ReplyWith(validDispensers.Aggregate("Available dispensers:\r\n", (current, dispenser) => current + (dispenser.Value.ToString("G") + "\r\n")));
        }


        [ConsoleCommand("dispenser.scale")]
        private void DispenserRate(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith(NotAllowed);
                return;
            }

            if (!arg.HasArgs(2))
            {
                arg.ReplyWith(InvalidArgumentsDispenser);
                return;
            }

            if (!validDispensers.ContainsKey(arg.GetString(0).ToLower()))
            {
                arg.ReplyWith(string.Format(InvalidDispenser, arg.GetString(0)));
                return;
            }

            var dispenser = validDispensers[arg.GetString(0).ToLower()].ToString("G");
            var modifier = arg.GetFloat(1, -1);
            if (modifier < 0)
            {
                arg.ReplyWith(InvalidModifier);
                return;
            }

            if (GatherDispenserModifiers.ContainsKey(dispenser))
                GatherDispenserModifiers[dispenser] = modifier;
            else
                GatherDispenserModifiers.Add(dispenser, modifier);
            SetConfigValue("Options", "GatherDispenserModifiers", GatherDispenserModifiers);
            arg.ReplyWith(string.Format(ModifyDispenser, dispenser, modifier));
        }

        [ConsoleCommand("quarry.tickrate")]
        private void MiningQuarryTickRate(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith(NotAllowed);
                return;
            }

            if (!arg.HasArgs())
            {
                arg.ReplyWith(InvalidArgumentsSpeed);
                return;
            }

            var modifier = arg.GetFloat(0, -1);
            if (modifier < 1)
            {
                arg.ReplyWith(InvalidSpeed);
                return;
            }

            MiningQuarryResourceTickRate = modifier;
            SetConfigValue("Options", "MiningQuarryResourceTickRate", MiningQuarryResourceTickRate);
            arg.ReplyWith(string.Format(ModifySpeed, Quarries, modifier));
            var quarries = UnityEngine.Object.FindObjectsOfType<MiningQuarry>();
            foreach (var quarry in quarries.Where(quarry => quarry.IsOn()))
            {
                quarry.CancelInvoke("ProcessResources");
                quarry.InvokeRepeating("ProcessResources", MiningQuarryResourceTickRate, MiningQuarryResourceTickRate);
            }
        }

        [ConsoleCommand("excavator.tickrate")]
        private void ExcavatorTickRate(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith(NotAllowed);
                return;
            }

            if (!arg.HasArgs())
            {
                arg.ReplyWith(InvalidArgumentsSpeed);
                return;
            }

            var modifier = arg.GetFloat(0, -1);
            if (modifier < 1)
            {
                arg.ReplyWith(InvalidSpeed);
                return;
            }

            ExcavatorResourceTickRate = modifier;
            SetConfigValue("Options", "ExcavatorResourceTickRate", ExcavatorResourceTickRate);
            arg.ReplyWith(string.Format(ModifySpeed, Excavators, modifier));
            foreach (var excavator in UnityEngine.Object.FindObjectsOfType<ExcavatorArm>())
                ApplyExcavatorSettings(excavator);
        }

        private string ResolveResourceKey(string resourceArg)
        {
            if (string.IsNullOrEmpty(resourceArg) || resourceArg == "*") return "*";

            var key = resourceArg.Trim().Trim('"').ToLower();
            key = GetResourceAlias(key);

            ItemDefinition itemDefinition;
            if (validResources.TryGetValue(key, out itemDefinition))
                return !string.IsNullOrEmpty(itemDefinition.shortname) ? itemDefinition.shortname.ToLower() : GetResourceName(itemDefinition);

            // Accept raw shortnames or custom keys instead of rejecting them. This keeps commands like
            // gather.rate dispenser metal.ore 1000 working even if Rust/uMod changes item display names.
            return key;
        }

        private string GetResourceAlias(string key)
        {
            switch (key)
            {
                case "metal":
                case "metal ore":
                case "metal_ore":
                    return "metal.ore";
                case "sulfur":
                case "sulfur ore":
                case "sulfur_ore":
                    return "sulfur.ore";
                case "hq":
                case "hqm":
                case "hq metal":
                case "hq metal ore":
                case "high quality metal":
                case "high quality metal ore":
                case "highqualitymetal.ore":
                case "high.quality.metal.ore":
                    return "hq.metal.ore";
                case "wood":
                    return "wood";
                case "stone":
                    return "stones";
                default:
                    return key;
            }
        }

        private bool TryGetResourceModifier(Dictionary<string, float> modifiers, ItemDefinition itemDefinition, out float modifier)
        {
            modifier = 1f;
            if (itemDefinition == null) return false;

            var keys = new List<string>();

            if (!string.IsNullOrEmpty(itemDefinition.displayName?.english))
            {
                keys.Add(itemDefinition.displayName.english);
                keys.Add(itemDefinition.displayName.english.ToLower());
                keys.Add(GetResourceAlias(itemDefinition.displayName.english.ToLower()));
            }

            if (!string.IsNullOrEmpty(itemDefinition.shortname))
            {
                keys.Add(itemDefinition.shortname);
                keys.Add(itemDefinition.shortname.ToLower());
                keys.Add(GetResourceAlias(itemDefinition.shortname.ToLower()));
            }

            keys.Add(itemDefinition.itemid.ToString());

            foreach (var key in keys.Distinct())
            {
                if (modifiers.TryGetValue(key, out modifier)) return true;
            }

            if (modifiers.TryGetValue("*", out modifier)) return true;
            return false;
        }

        private string GetResourceName(ItemDefinition itemDefinition)
        {
            if (itemDefinition == null) return "*";
            return itemDefinition.displayName?.english ?? itemDefinition.shortname ?? itemDefinition.itemid.ToString();
        }

        private void ApplyGatherModifier(Dictionary<string, float> modifiers, Item item)
        {
            if (item == null || item.info == null) return;

            float modifier;
            if (TryGetResourceModifier(modifiers, item.info, out modifier))
                item.amount = Mathf.Max(1, (int)(item.amount * modifier));
        }

        private void OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            if (dispenser == null || player == null || item == null) return;

            var gatherType = dispenser.gatherType.ToString("G");
            var amount = item.amount;

            ApplyGatherModifier(GatherResourceModifiers, item);

            float dispenserModifier;
            if (!GatherDispenserModifiers.TryGetValue(gatherType, out dispenserModifier)) return;

            try
            {
                var containedItem = dispenser.containedItems.SingleOrDefault(x => x.itemid == item.info.itemid);
                if (containedItem == null) return;

                containedItem.amount += amount - item.amount / dispenserModifier;

                if (containedItem.amount < 0)
                    item.amount += (int)containedItem.amount;
            }
            catch { }
        }

        private Item OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item)
        {
            // Current Rust/uMod sends finish bonuses such as metal ore through BasePlayer here.
            // Returning the same item guarantees Rust uses the modified amount.
            ApplyGatherModifier(GatherResourceModifiers, item);
            return item;
        }

        private void OnGrowableGathered(GrowableEntity growable, Item item, BasePlayer player)
        {
            ApplyGatherModifier(GatherResourceModifiers, item);
        }

        private void OnQuarryGather(MiningQuarry quarry, Item item)
        {
            ApplyGatherModifier(QuarryResourceModifiers, item);
        }

        private void OnExcavatorGather(ExcavatorArm excavator, Item item)
        {
            ApplyGatherModifier(ExcavatorResourceModifiers, item);
        }

        private void OnCollectiblePickup(CollectibleEntity collectible, BasePlayer player)
        {
            foreach (ItemAmount item in collectible.itemList)
            {
                float modifier;
                if (TryGetResourceModifier(PickupResourceModifiers, item.itemDef, out modifier))
                    item.amount = (int)(item.amount * modifier);
            }
        }

        private void OnSurveyGather(SurveyCharge surveyCharge, Item item)
        {
            ApplyGatherModifier(SurveyResourceModifiers, item);
        }

        private void OnMiningQuarryEnabled(MiningQuarry quarry)
        {
            if (MiningQuarryResourceTickRate == DefaultMiningQuarryResourceTickRate) return;
            quarry.CancelInvoke("ProcessResources");
            quarry.InvokeRepeating("ProcessResources", MiningQuarryResourceTickRate, MiningQuarryResourceTickRate);
        }

        private void LoadConfigValues()
        {
            // Plugin settings
            ChatPrefix = GetConfigValue("Settings", "ChatPrefix", DefaultChatPrefix);
            ChatPrefixColor = GetConfigValue("Settings", "ChatPrefixColor", DefaultChatPrefixColor);

            // Plugin options
            var gatherResourceModifiers = GetConfigValue("Options", "GatherResourceModifiers", DefaultGatherResourceModifiers);
            var gatherDispenserModifiers = GetConfigValue("Options", "GatherDispenserModifiers", DefaultGatherDispenserModifiers);
            var quarryResourceModifiers = GetConfigValue("Options", "QuarryResourceModifiers", DefaultQuarryResourceModifiers);
            var excavatorResourceModifiers = GetConfigValue("Options", "ExcavatorResourceModifiers", quarryResourceModifiers);
            var pickupResourceModifiers = GetConfigValue("Options", "PickupResourceModifiers", DefaultPickupResourceModifiers);
            var surveyResourceModifiers = GetConfigValue("Options", "SurveyResourceModifiers", DefaultSurveyResourceModifiers);

            MiningQuarryResourceTickRate = GetConfigValue("Options", "MiningQuarryResourceTickRate", DefaultMiningQuarryResourceTickRate);

            ExcavatorResourceTickRate = GetConfigValue("Options", "ExcavatorResourceTickRate", DefaultExcavatorResourceTickRate);
            ExcavatorBeltSpeedMax = GetConfigValue("Options", "ExcavatorBeltSpeedMax", DefaultExcavatorBeltSpeedMax);
            ExcavatorTimeForFullResources = GetConfigValue("Options", "ExcavatorTimeForFullResources", DefaultExcavatorTimeForFullResources);

            GatherResourceModifiers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in gatherResourceModifiers)
            {
                float rate;
                if (!float.TryParse(entry.Value.ToString(), out rate)) continue;
                GatherResourceModifiers.Add(entry.Key, rate);
            }

            GatherDispenserModifiers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in gatherDispenserModifiers)
            {
                float rate;
                if (!float.TryParse(entry.Value.ToString(), out rate)) continue;
                GatherDispenserModifiers.Add(entry.Key, rate);
            }

            QuarryResourceModifiers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in quarryResourceModifiers)
            {
                float rate;
                if (!float.TryParse(entry.Value.ToString(), out rate)) continue;
                QuarryResourceModifiers.Add(entry.Key, rate);
            }

            ExcavatorResourceModifiers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in excavatorResourceModifiers)
            {
                float rate;
                if (!float.TryParse(entry.Value.ToString(), out rate)) continue;
                ExcavatorResourceModifiers.Add(entry.Key, rate);
            }

            PickupResourceModifiers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in pickupResourceModifiers)
            {
                float rate;
                if (!float.TryParse(entry.Value.ToString(), out rate)) continue;
                PickupResourceModifiers.Add(entry.Key, rate);
            }

            SurveyResourceModifiers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in surveyResourceModifiers)
            {
                float rate;
                if (!float.TryParse(entry.Value.ToString(), out rate)) continue;
                SurveyResourceModifiers.Add(entry.Key, rate);
            }

            // Plugin messages
            NotAllowed = GetConfigValue("Messages", "NotAllowed", DefaultNotAllowed);
            InvalidArgumentsGather = GetConfigValue("Messages", "InvalidArgumentsGather", DefaultInvalidArgumentsGather);
            InvalidArgumentsDispenser = GetConfigValue("Messages", "InvalidArgumentsDispenserType", DefaultInvalidArgumentsDispenser);
            InvalidArgumentsSpeed = GetConfigValue("Messages", "InvalidArgumentsMiningQuarrySpeed", DefaultInvalidArgumentsSpeed);
            InvalidModifier = GetConfigValue("Messages", "InvalidModifier", DefaultInvalidModifier);
            InvalidSpeed = GetConfigValue("Messages", "InvalidMiningQuarrySpeed", DefaultInvalidSpeed);
            ModifyResource = GetConfigValue("Messages", "ModifyResource", DefaultModifyResource);
            ModifyResourceRemove = GetConfigValue("Messages", "ModifyResourceRemove", DefaultModifyResourceRemove);
            ModifySpeed = GetConfigValue("Messages", "ModifyMiningQuarrySpeed", DefaultModifySpeed);
            InvalidResource = GetConfigValue("Messages", "InvalidResource", DefaultInvalidResource);
            ModifyDispenser = GetConfigValue("Messages", "ModifyDispenser", DefaultModifyDispenser);
            InvalidDispenser = GetConfigValue("Messages", "InvalidDispenser", DefaultInvalidDispenser);
            HelpText = GetConfigValue("Messages", "HelpText", DefaultHelpText);
            HelpTextAdmin = GetConfigValue("Messages", "HelpTextAdmin", DefaultHelpTextAdmin);
            HelpTextPlayer = GetConfigValue("Messages", "HelpTextPlayer", DefaultHelpTextPlayer);
            HelpTextPlayerGains = GetConfigValue("Messages", "HelpTextPlayerGains", DefaultHelpTextPlayerGains);
            HelpTextPlayerDefault = GetConfigValue("Messages", "HelpTextPlayerDefault", DefaultHelpTextPlayerDefault);
            HelpTextPlayerMiningQuarrySpeed = GetConfigValue("Messages", "HelpTextMiningQuarrySpeed", DefaultHelpTextPlayerMiningQuarrySpeed);
            HelpTextPlayerExcavatorSpeed = GetConfigValue("Messages", "HelpTextExcavatorSpeed", DefaultHelpTextPlayerExcavatorSpeed);
            Dispensers = GetConfigValue("Messages", "Dispensers", DefaultDispensers);
            Quarries = GetConfigValue("Messages", "MiningQuarries", DefaultQuarries);
            Excavators = GetConfigValue("Messages", "Excavators", DefaultExcavators);
            Charges = GetConfigValue("Messages", "SurveyCharges", DefaultCharges);
            Pickups = GetConfigValue("Messages", "Pickups", DefaultPickups);

            if (!configChanged) return;
            PrintWarning("Configuration file updated.");
            SaveConfig();
        }

        private T GetConfigValue<T>(string category, string setting, T defaultValue)
        {
            var data = Config[category] as Dictionary<string, object>;
            object value;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[category] = data;
                configChanged = true;
            }
            if (data.TryGetValue(setting, out value)) return (T)Convert.ChangeType(value, typeof(T));
            value = defaultValue;
            data[setting] = value;
            configChanged = true;
            return (T)Convert.ChangeType(value, typeof(T));
        }

        private void SetConfigValue<T>(string category, string setting, T newValue)
        {
            var data = Config[category] as Dictionary<string, object>;
            object value;
            if (data != null && data.TryGetValue(setting, out value))
            {
                value = newValue;
                data[setting] = value;
                configChanged = true;
            }
            SaveConfig();
        }

        private void SendMessage(BasePlayer player, string message, params object[] args)
        {
            if (player == null) return;
            player.ChatMessage(string.Format($"<color={ChatPrefixColor}>{ChatPrefix}</color>: {message}", args));
        }
    }
}
