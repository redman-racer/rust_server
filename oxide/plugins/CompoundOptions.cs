using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rust.Ai;
using Oxide.Core;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Compound Options", "FastBurst", "1.4.6")]
    [Description("Compound, Bandit Camp, and Apartment Complex monument options")]
    class CompoundOptions : RustPlugin
    {
        #region Vars
        private bool dataChanged;
        private StorageData data;
        private StorageData defaultOrders;
        private readonly List<AppliedPricingMember> appliedPricingMembers = new List<AppliedPricingMember>();
        private readonly List<AppliedPricingListEntry> appliedPricingListEntries = new List<AppliedPricingListEntry>();
        private readonly List<AppliedPricingDictionaryEntry> appliedPricingDictionaryEntries = new List<AppliedPricingDictionaryEntry>();
        #endregion

        #region Oxide hooks
        private void Loaded()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StorageData>(Name);
                defaultOrders = Interface.Oxide.DataFileSystem.ReadObject<StorageData>(Name + "_default");
            }
            catch { }

            if (data == null)
            {
                data = new StorageData();
            }
            if (defaultOrders == null)
            {
                defaultOrders = new StorageData();
            }

            if (data.VendingMachinesOrders == null)
            {
                data.VendingMachinesOrders = new Dictionary<string, Order[]>();
            }
            if (defaultOrders.VendingMachinesOrders == null)
            {
                defaultOrders.VendingMachinesOrders = new Dictionary<string, Order[]>();
            }
        }

        private void Unload()
        {
            RestoreApartmentPricing();

            foreach (var entity in BaseNetworkable.serverEntities.ToList())
            {
                if (entity is NPCVendingMachine)
                {
                    var vending = entity as NPCVendingMachine;
                    bool isApartment = IsApartmentEntity(vending);
                    bool shouldRestore = isApartment
                        ? configData.General.disableApartmentVendingMachines || configData.General.allowCustomApartmentVendingMachines
                        : configData.General.disableCompoundVendingMachines || configData.General.allowCustomCompoundVendingMachines;

                    if (!shouldRestore)
                        continue;

                    if (configData.General.allowConsoleOutput)
                        Puts($"Restoring default orders for {vending.ShortPrefabName}");
                    if (HasNpcVendingOrders(vending) && defaultOrders.VendingMachinesOrders != null)
                    {
                        var orders = GetDefaultOrders(vending);
                        if (orders != null)
                        {
                            vending.vendingOrders.orders = orders;
                            vending.InstallFromVendingOrders();
                        }
                    }
                }
            }
        }

        private void Init()
        {
            Unsubscribe(nameof(OnEntitySpawned));
            //LoadVariables();
        }

        private void OnServerInitialized()
        {
            Subscribe(nameof(OnEntitySpawned));

            foreach (var entity in BaseNetworkable.serverEntities.ToList())
            {
                if (entity is NPCVendingMachine)
                {
                    var vending = entity as NPCVendingMachine;
                    UpdateVending(vending);
                }
                else if (entity is NPCPlayer)
                {
                    KillNPCPlayer(entity as NPCPlayer);
                }
                else if (entity is NPCAutoTurret)
                {
                    ProcessNPCTurret(entity as NPCAutoTurret);
                }
            }

            //LoadVariables();
            ApplyApartmentPricing();
            SaveData();
        }

        private void OnEntityEnter(TriggerBase trigger, BaseEntity entity)
        {
            if (!(trigger is TriggerSafeZone) || !(entity is BasePlayer)) return;

            var safeZone = trigger as TriggerSafeZone;
            if (safeZone == null) return;

            safeZone.enabled = IsApartmentPosition(safeZone.transform.position, safeZone.name)
                ? !configData.General.disableApartmentTrigger
                : !configData.General.disableCompoundTrigger;
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity is NPCVendingMachine)
            {
                UpdateVending(entity as NPCVendingMachine);
                SaveData();
            }
            else if (entity is NPCPlayer)
            {
                KillNPCPlayer(entity as NPCPlayer);
            }
            else if (entity is NPCAutoTurret)
            {
                ProcessNPCTurret(entity as NPCAutoTurret);
            }

            var baseEntity = entity as BaseEntity;
            if (baseEntity != null && IsApartmentEntity(baseEntity))
            {
                NextTick(() => ApplyApartmentPricingToEntity(baseEntity));
            }
        }
        #endregion

        #region Implementation
        private void KillNPCPlayer(NPCPlayer npcPlayer)
        {
            if (IsApartmentEntity(npcPlayer))
            {
                if (configData.General.disallowApartmentNPC && !npcPlayer.IsDestroyed)
                {
                    npcPlayer.Kill(BaseNetworkable.DestroyMode.Gib);
                }

                return;
            }

            var npcSpawner = npcPlayer.gameObject.GetComponent<ScientistSpawner>();
            if (npcSpawner == null) return;

            if (npcSpawner.IsMilitaryTunnelLab && configData.General.disallowCompoundNPC || npcSpawner.IsBandit && configData.General.disallowBanditNPC)
            {
                if (!npcPlayer.IsDestroyed) npcPlayer.Kill(BaseNetworkable.DestroyMode.Gib);
            }
        }

        private void ProcessNPCTurret(NPCAutoTurret npcAutoTurret)
        {
            bool disabled = IsApartmentEntity(npcAutoTurret)
                ? configData.General.disableApartmentTurrets
                : configData.General.disableCompoundTurrets;

            npcAutoTurret.SetFlag(NPCAutoTurret.Flags.On, !disabled, !disabled);
            npcAutoTurret.UpdateNetworkGroup();
            npcAutoTurret.SendNetworkUpdateImmediate();
        }

        private bool IsApartmentEntity(BaseEntity entity)
        {
            if (entity == null) return false;

            string prefabName = entity.PrefabName ?? entity.ShortPrefabName ?? string.Empty;
            return IsApartmentPosition(entity.transform.position, prefabName);
        }

        private bool IsApartmentPosition(UnityEngine.Vector3 position, string objectName = null)
        {
            if (!string.IsNullOrEmpty(objectName)
                && objectName.IndexOf("apartment", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null)
            {
                return false;
            }

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (!IsApartmentMonument(monument)) continue;

                var localPosition = monument.transform.InverseTransformPoint(position);
                if (monument.Bounds.Contains(localPosition))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsApartmentMonument(MonumentInfo monument)
        {
            if (monument == null) return false;

            string monumentName = monument.name ?? string.Empty;
            if (monumentName.IndexOf("apartment", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            try
            {
                string displayName = monument.displayPhrase.english ?? string.Empty;
                return displayName.IndexOf("apartment", System.StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static readonly string[] RoomPurchasePriceMembers =
        {
            "PurchasePrice", "ScrapPurchasePrice", "InitialPurchasePrice", "PurchaseCost", "RentPrice"
        };

        private static readonly string[] RoomDailyUpkeepMembers =
        {
            "MinimumRent", "BaseDailyUpkeep", "BaseScrapUpkeep", "BaseUpkeep", "ScrapUpkeepPerDay",
            "CachedDailyUpkeep", "DailyScrapUpkeep", "DailyUpkeep", "UpkeepPerDay", "DailyRent"
        };

        private static readonly string[] ShopUpfrontPriceMembers =
        {
            "InitialScrapFee", "StartingFee", "UpfrontCost", "UpfrontFee", "InitialCost", "StartupCost", "PurchasePrice", "BasePurchasePrice"
        };

        private static readonly string[] ShopHourlyUpkeepMembers =
        {
            "ScrapCostPerRealTimeHour", "ScrapCostPerHour", "HourlyUpkeep", "HourlyRent", "RentPerHour", "UpkeepPerHour", "HourlyCost"
        };

        private void ApplyApartmentPricing()
        {
            if (configData.ApartmentPricing == null || !configData.ApartmentPricing.Enabled)
            {
                return;
            }

            ApplyMasterKeyPrice();
            int changed = 0;
            foreach (var entity in BaseNetworkable.serverEntities.ToList())
            {
                var baseEntity = entity as BaseEntity;
                if (baseEntity == null || baseEntity.IsDestroyed || !IsApartmentEntity(baseEntity)) continue;
                changed += ApplyApartmentPricingToEntity(baseEntity);
            }

            if (changed > 0)
            {
                Puts($"Applied {changed} Apartment Complex pricing override(s). Values set to -1 kept their vanilla defaults.");
            }
            else
            {
                PrintWarning("Room/shop pricing overrides are enabled, but no matching runtime price members were found. Ensure this plugin is loaded after the Apartment Complex entities spawn.");
            }
        }

        private int ApplyMasterKeyPrice()
        {
            int price = configData.ApartmentPricing.MasterKeyPrice;
            if (price < 0) return 0;

            ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), $"apartment.masterkeyprice {price}");
            Puts($"Set the Apartment Complex basement master-key price to {price} scrap.");
            return 1;
        }

        private int ApplyApartmentPricingToEntity(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed || configData.ApartmentPricing == null || !configData.ApartmentPricing.Enabled)
            {
                return 0;
            }

            int changed = ApplyApartmentPricingToTarget(entity, GetPricingIdentity(entity));
            foreach (var component in entity.GetComponents<UnityEngine.Component>())
            {
                if (component == null || object.ReferenceEquals(component, entity)) continue;
                changed += ApplyApartmentPricingToTarget(component, GetPricingIdentity(component));
            }

            if (changed > 0)
            {
                entity.SendNetworkUpdateImmediate();
            }

            return changed;
        }

        private int ApplyApartmentPricingToTarget(object target, string identity)
        {
            if (target == null) return 0;

            string typeName = target.GetType().Name;
            string searchable = (typeName + " " + identity).ToLowerInvariant();

            if (string.Equals(typeName, "ApartmentBuilding", System.StringComparison.OrdinalIgnoreCase))
            {
                return ApplyApartmentBuildingPurchaseCosts(target)
                    + ApplyApartmentBuildingPrefabPricing(target);
            }

            bool isShop = searchable.Contains("rentableshop")
                || searchable.Contains("rentable_shop")
                || searchable.Contains("rentable shop")
                || searchable.Contains("rentalshop")
                || searchable.Contains("rental_shop");

            if (isShop)
            {
                var shop = configData.ApartmentPricing.RentableShops;
                if (shop == null) return 0;

                int upfrontChanged = SetNumericMember(target, ShopUpfrontPriceMembers, shop.UpfrontPrice);
                int hourlyChanged = SetNumericMember(target, ShopHourlyUpkeepMembers, shop.HourlyUpkeep);

                // These fields are private/obfuscated in current Rust builds. Only fall back
                // to their known vanilla values while already scoped to a RentableShop.
                if (upfrontChanged == 0)
                    upfrontChanged = SetNumericMemberByCurrentValue(target, 100, shop.UpfrontPrice);
                if (hourlyChanged == 0)
                    hourlyChanged = SetNumericMemberByCurrentValue(target, 10, shop.HourlyUpkeep);

                return upfrontChanged + hourlyChanged;
            }

            if (!searchable.Contains("apartment")) return 0;

            var room = GetApartmentRoomPricing(searchable);
            if (room == null) return 0;

            int roomChanged = 0;
            roomChanged += SetNumericMember(target, RoomPurchasePriceMembers, room.PurchasePrice);
            roomChanged += SetNumericMember(target, RoomDailyUpkeepMembers, room.DailyUpkeep);
            return roomChanged;
        }

        private ApartmentRoomPricing GetApartmentRoomPricing(string identity)
        {
            var rooms = configData.ApartmentPricing.Rooms;
            if (rooms == null) return null;

            string key;
            if (identity.Contains("penthouse") || identity.Contains("large")) key = "Large";
            else if (identity.Contains("medium")) key = "Medium";
            else if (identity.Contains("small") || identity.Contains("basement")) key = "Small";
            else return null;

            ApartmentRoomPricing pricing;
            return rooms.TryGetValue(key, out pricing) ? pricing : null;
        }

        private int ApplyApartmentBuildingPurchaseCosts(object building)
        {
            object purchaseCosts;
            if (!TryGetMemberValue(building, "PurchaseCosts", out purchaseCosts) || purchaseCosts == null)
            {
                return 0;
            }

            var dictionary = purchaseCosts as System.Collections.IDictionary;
            if (dictionary != null)
            {
                int dictionaryChanged = ApplyApartmentPurchaseCostDictionary(dictionary);
                if (dictionaryChanged > 0)
                    Puts($"Applied {dictionaryChanged} network-synced concierge purchase cost override(s).");
                return dictionaryChanged;
            }

            var entries = purchaseCosts as System.Collections.IList;
            if (entries == null)
            {
                PrintWarning($"ApartmentBuilding.PurchaseCosts uses unsupported type {purchaseCosts.GetType().FullName}.");
                return 0;
            }

            int changed = 0;
            for (int index = 0; index < entries.Count; index++)
            {
                object entry = entries[index];
                if (entry == null) continue;

                if (IsNumericObject(entry))
                {
                    var scalarRoom = GetApartmentRoomPricingForPurchaseCost(entry, index, entries.Count);
                    if (scalarRoom != null && scalarRoom.PurchasePrice >= 0
                        && TrySetPricingListEntry(entries, index, entry, scalarRoom.PurchasePrice))
                    {
                        changed++;
                    }
                    continue;
                }

                string sizeIdentity = GetApartmentPurchaseCostIdentity(entry).ToLowerInvariant();
                var room = GetApartmentRoomPricing(sizeIdentity);
                if (room == null || room.PurchasePrice < 0) continue;

                int entryChanged = SetNumericMember(entry,
                    new[] { "PurchaseCost", "Cost", "ScrapCost", "Price" }, room.PurchasePrice);

                if (entryChanged == 0)
                {
                    int vanillaPrice = sizeIdentity.Contains("small") ? 25
                        : sizeIdentity.Contains("medium") ? 150
                        : sizeIdentity.Contains("large") || sizeIdentity.Contains("penthouse") ? 350
                        : -1;
                    entryChanged = SetNumericMemberByCurrentValue(entry, vanillaPrice, room.PurchasePrice);
                }

                if (entryChanged > 0)
                {
                    changed += entryChanged;

                    // ApartmentPurchaseCost may be a value type, so write the boxed value
                    // back into the collection after changing it.
                    try { entries[index] = entry; }
                    catch { }
                }
            }

            if (changed > 0)
                Puts($"Applied {changed} network-synced concierge purchase cost override(s).");

            return changed;
        }

        private int ApplyApartmentPurchaseCostDictionary(System.Collections.IDictionary dictionary)
        {
            int changed = 0;
            var keys = new List<object>();
            foreach (object key in dictionary.Keys) keys.Add(key);

            foreach (object key in keys)
            {
                object originalValue = dictionary[key];
                if (originalValue == null || !IsNumericObject(originalValue)) continue;

                var room = GetApartmentRoomPricing((key == null ? string.Empty : key.ToString()).ToLowerInvariant())
                    ?? GetApartmentRoomPricingForPurchaseCost(originalValue, -1, dictionary.Count);
                if (room == null || room.PurchasePrice < 0) continue;

                try
                {
                    if (!appliedPricingDictionaryEntries.Any(x => object.ReferenceEquals(x.Dictionary, dictionary) && object.Equals(x.Key, key)))
                    {
                        appliedPricingDictionaryEntries.Add(new AppliedPricingDictionaryEntry
                        {
                            Dictionary = dictionary,
                            Key = key,
                            OriginalValue = originalValue
                        });
                    }

                    dictionary[key] = System.Convert.ChangeType(room.PurchasePrice, originalValue.GetType());
                    changed++;
                }
                catch (System.Exception ex)
                {
                    PrintWarning($"Could not update ApartmentBuilding.PurchaseCosts[{key}]: {ex.Message}");
                }
            }

            return changed;
        }

        private ApartmentRoomPricing GetApartmentRoomPricingForPurchaseCost(object value, int index, int count)
        {
            int current;
            try { current = System.Convert.ToInt32(value); }
            catch { return null; }

            string key = current == 25 ? "Small"
                : current == 150 ? "Medium"
                : current == 350 ? "Large"
                : null;

            if (key == null && count == 3 && index >= 0)
                key = index == 0 ? "Small" : index == 1 ? "Medium" : index == 2 ? "Large" : null;
            else if (key == null && count == 4 && index > 0)
                key = index == 1 ? "Small" : index == 2 ? "Medium" : index == 3 ? "Large" : null;

            if (key == null || configData.ApartmentPricing.Rooms == null) return null;
            ApartmentRoomPricing room;
            return configData.ApartmentPricing.Rooms.TryGetValue(key, out room) ? room : null;
        }

        private bool TrySetPricingListEntry(System.Collections.IList entries, int index, object originalValue, int configuredValue)
        {
            try
            {
                if (!appliedPricingListEntries.Any(x => object.ReferenceEquals(x.List, entries) && x.Index == index))
                {
                    appliedPricingListEntries.Add(new AppliedPricingListEntry
                    {
                        List = entries,
                        Index = index,
                        OriginalValue = originalValue
                    });
                }

                entries[index] = System.Convert.ChangeType(configuredValue, originalValue.GetType());
                return true;
            }
            catch (System.Exception ex)
            {
                PrintWarning($"Could not update ApartmentBuilding.PurchaseCosts[{index}]: {ex.Message}");
                return false;
            }
        }

        private bool IsNumericObject(object value)
        {
            if (value == null || value is bool || value.GetType().IsEnum) return false;
            switch (System.Type.GetTypeCode(value.GetType()))
            {
                case System.TypeCode.Byte:
                case System.TypeCode.SByte:
                case System.TypeCode.Int16:
                case System.TypeCode.UInt16:
                case System.TypeCode.Int32:
                case System.TypeCode.UInt32:
                case System.TypeCode.Int64:
                case System.TypeCode.UInt64:
                case System.TypeCode.Single:
                case System.TypeCode.Double:
                case System.TypeCode.Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private int ApplyApartmentBuildingPrefabPricing(object building)
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var getApartmentPrefab = building.GetType().GetMethods(flags).FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "GetApartmentPrefab", System.StringComparison.OrdinalIgnoreCase)) return false;
                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsEnum;
            });

            if (getApartmentPrefab == null) return 0;

            int changed = 0;
            var apartmentSizeType = getApartmentPrefab.GetParameters()[0].ParameterType;
            foreach (object apartmentSize in System.Enum.GetValues(apartmentSizeType))
            {
                string sizeIdentity = apartmentSize.ToString().ToLowerInvariant();
                var room = GetApartmentRoomPricing(sizeIdentity);
                if (room == null) continue;

                object prefab;
                try { prefab = getApartmentPrefab.Invoke(building, new[] { apartmentSize }); }
                catch (System.Exception ex)
                {
                    PrintWarning($"Could not resolve the {apartmentSize} apartment prefab: {ex.Message}");
                    continue;
                }

                changed += ApplyApartmentPrefabPricingTarget(prefab, room, 0);
            }

            if (changed > 0)
                Puts($"Applied {changed} prefab-backed concierge apartment price override(s).");
            else
                PrintWarning("Found ApartmentBuilding.GetApartmentPrefab, but could not update its ApartmentRoom prefab prices.");

            return changed;
        }

        private int ApplyApartmentPrefabPricingTarget(object target, ApartmentRoomPricing room, int depth)
        {
            if (target == null || room == null || depth > 2) return 0;

            int changed = 0;
            if (string.Equals(target.GetType().Name, "ApartmentRoom", System.StringComparison.OrdinalIgnoreCase))
            {
                changed += SetNumericMember(target, RoomPurchasePriceMembers, room.PurchasePrice);
                changed += SetNumericMember(target, RoomDailyUpkeepMembers, room.DailyUpkeep);
                return changed;
            }

            var gameObject = target as UnityEngine.GameObject;
            if (gameObject != null)
            {
                foreach (var component in gameObject.GetComponentsInChildren<UnityEngine.Component>(true))
                {
                    if (component != null && string.Equals(component.GetType().Name, "ApartmentRoom", System.StringComparison.OrdinalIgnoreCase))
                    {
                        changed += SetNumericMember(component, RoomPurchasePriceMembers, room.PurchasePrice);
                        changed += SetNumericMember(component, RoomDailyUpkeepMembers, room.DailyUpkeep);
                    }
                }
                return changed;
            }

            var componentTarget = target as UnityEngine.Component;
            if (componentTarget != null)
            {
                foreach (var component in componentTarget.GetComponentsInChildren<UnityEngine.Component>(true))
                {
                    if (component != null && string.Equals(component.GetType().Name, "ApartmentRoom", System.StringComparison.OrdinalIgnoreCase))
                    {
                        changed += SetNumericMember(component, RoomPurchasePriceMembers, room.PurchasePrice);
                        changed += SetNumericMember(component, RoomDailyUpkeepMembers, room.DailyUpkeep);
                    }
                }
                return changed;
            }

            // GameObjectRef and similar prefab wrappers expose the referenced object through Get/Load.
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string methodName in new[] { "Get", "Load" })
            {
                var resolver = target.GetType().GetMethods(flags).FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, methodName, System.StringComparison.OrdinalIgnoreCase)
                        || method.ReturnType == typeof(void)) return false;
                    var parameters = method.GetParameters();
                    return parameters.Length == 0
                        || (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool));
                });
                if (resolver == null) continue;

                try
                {
                    var parameters = resolver.GetParameters();
                    object resolved = resolver.Invoke(target, parameters.Length == 0 ? null : new object[] { true });
                    if (resolved != null && !object.ReferenceEquals(resolved, target))
                    {
                        return ApplyApartmentPrefabPricingTarget(resolved, room, depth + 1);
                    }
                }
                catch { }
            }

            return changed;
        }

        private string GetApartmentPurchaseCostIdentity(object entry)
        {
            foreach (string memberName in new[] { "ApartmentSize", "Size", "RoomSize", "RoomType", "Tier" })
            {
                object value;
                if (TryGetMemberValue(entry, memberName, out value) && value != null)
                {
                    return value.ToString();
                }
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in entry.GetType().GetFields(flags))
            {
                if (!field.FieldType.IsEnum) continue;
                object value = field.GetValue(entry);
                if (value != null) return value.ToString();
            }

            foreach (var property in entry.GetType().GetProperties(flags))
            {
                if (!property.CanRead || !property.PropertyType.IsEnum || property.GetIndexParameters().Length != 0) continue;
                object value = property.GetValue(entry, null);
                if (value != null) return value.ToString();
            }

            return string.Empty;
        }

        private string GetPricingIdentity(UnityEngine.Component component)
        {
            if (component == null) return string.Empty;

            string identity = component.name ?? string.Empty;
            var entity = component as BaseEntity;
            if (entity != null)
            {
                identity += " " + (entity.PrefabName ?? string.Empty) + " " + (entity.ShortPrefabName ?? string.Empty);
            }

            foreach (string memberName in new[] { "ApartmentSize", "RoomType", "ApartmentType", "RoomSize", "Tier" })
            {
                object value;
                if (TryGetMemberValue(component, memberName, out value) && value != null)
                {
                    identity += " " + value;
                }
            }

            return identity;
        }

        private bool TryGetMemberValue(object target, string memberName, out object value)
        {
            value = null;
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            var field = target.GetType().GetField(memberName, flags);
            if (field != null)
            {
                value = field.GetValue(target);
                return true;
            }

            var property = target.GetType().GetProperty(memberName, flags);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                value = property.GetValue(target, null);
                return true;
            }

            string normalizedName = NormalizePricingMemberName(memberName);
            field = target.GetType().GetFields(flags).FirstOrDefault(candidate =>
                PricingMemberNameMatches(NormalizePricingMemberName(candidate.Name), normalizedName));
            if (field == null) return false;
            value = field.GetValue(target);
            return true;
        }

        private int SetNumericMember(object target, string[] candidateNames, int configuredValue)
        {
            if (configuredValue < 0) return 0;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            foreach (string candidateName in candidateNames)
            {
                var field = target.GetType().GetField(candidateName, flags);
                if (field != null && TrySetPricingMember(target, field, field.FieldType, configuredValue)) return 1;

                var property = target.GetType().GetProperty(candidateName, flags);
                if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0
                    && TrySetPricingMember(target, property, property.PropertyType, configuredValue)) return 1;
            }

            foreach (var field in target.GetType().GetFields(flags))
            {
                string normalizedField = NormalizePricingMemberName(field.Name);
                if (candidateNames.Any(x => PricingMemberNameMatches(normalizedField, NormalizePricingMemberName(x)))
                    && TrySetPricingMember(target, field, field.FieldType, configuredValue)) return 1;
            }

            foreach (var property in target.GetType().GetProperties(flags))
            {
                if (!property.CanWrite || property.GetIndexParameters().Length != 0) continue;
                string normalizedProperty = NormalizePricingMemberName(property.Name);
                if (candidateNames.Any(x => PricingMemberNameMatches(normalizedProperty, NormalizePricingMemberName(x)))
                    && TrySetPricingMember(target, property, property.PropertyType, configuredValue)) return 1;
            }

            return 0;
        }

        private int SetNumericMemberByCurrentValue(object target, int expectedValue, int configuredValue)
        {
            if (target == null || expectedValue < 0 || configuredValue < 0) return 0;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in target.GetType().GetFields(flags))
            {
                object currentValue;
                try { currentValue = field.GetValue(target); }
                catch { continue; }

                if (IsNumericValue(currentValue, expectedValue)
                    && TrySetPricingMember(target, field, field.FieldType, configuredValue)) return 1;
            }

            foreach (var property in target.GetType().GetProperties(flags))
            {
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0) continue;

                object currentValue;
                try { currentValue = property.GetValue(target, null); }
                catch { continue; }

                if (IsNumericValue(currentValue, expectedValue)
                    && TrySetPricingMember(target, property, property.PropertyType, configuredValue)) return 1;
            }

            return 0;
        }

        private bool IsNumericValue(object value, int expectedValue)
        {
            if (value == null || value is bool || value.GetType().IsEnum) return false;
            try { return System.Convert.ToDecimal(value) == expectedValue; }
            catch { return false; }
        }

        private string NormalizePricingMemberName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private bool PricingMemberNameMatches(string actual, string candidate)
        {
            return actual == candidate || actual == candidate + "kbackingfield";
        }

        private bool TrySetPricingMember(object target, MemberInfo member, System.Type valueType, int configuredValue)
        {
            try
            {
                object originalValue = member is FieldInfo
                    ? ((FieldInfo)member).GetValue(target)
                    : ((PropertyInfo)member).GetValue(target, null);
                object convertedValue = System.Convert.ChangeType(configuredValue, System.Nullable.GetUnderlyingType(valueType) ?? valueType);

                if (!appliedPricingMembers.Any(x => object.ReferenceEquals(x.Target, target) && x.Member == member))
                {
                    appliedPricingMembers.Add(new AppliedPricingMember
                    {
                        Target = target,
                        Member = member,
                        OriginalValue = originalValue
                    });
                }

                if (member is FieldInfo)
                    ((FieldInfo)member).SetValue(target, convertedValue);
                else
                    ((PropertyInfo)member).SetValue(target, convertedValue, null);

                return true;
            }
            catch (System.Exception ex)
            {
                PrintWarning($"Could not set apartment pricing member {target.GetType().Name}.{member.Name}: {ex.Message}");
                return false;
            }
        }

        private void RestoreApartmentPricing()
        {
            foreach (var applied in appliedPricingListEntries)
            {
                try { applied.List[applied.Index] = applied.OriginalValue; }
                catch { }
            }
            appliedPricingListEntries.Clear();

            foreach (var applied in appliedPricingDictionaryEntries)
            {
                try { applied.Dictionary[applied.Key] = applied.OriginalValue; }
                catch { }
            }
            appliedPricingDictionaryEntries.Clear();

            foreach (var applied in appliedPricingMembers)
            {
                try
                {
                    if (applied.Member is FieldInfo)
                        ((FieldInfo)applied.Member).SetValue(applied.Target, applied.OriginalValue);
                    else if (applied.Member is PropertyInfo)
                        ((PropertyInfo)applied.Member).SetValue(applied.Target, applied.OriginalValue, null);
                }
                catch { }
            }

            appliedPricingMembers.Clear();
        }

        private class AppliedPricingMember
        {
            public object Target;
            public MemberInfo Member;
            public object OriginalValue;
        }

        private class AppliedPricingListEntry
        {
            public System.Collections.IList List;
            public int Index;
            public object OriginalValue;
        }

        private class AppliedPricingDictionaryEntry
        {
            public System.Collections.IDictionary Dictionary;
            public object Key;
            public object OriginalValue;
        }

        private void AddVendingOrders(NPCVendingMachine vending, bool def = false)
        {
            if (vending == null || vending.IsDestroyed)
            {
                Puts("Null or destroyed machine...");
                return;
            }

            if (!HasNpcVendingOrders(vending))
            {
                if (configData.General.allowConsoleOutput)
                    Puts($"Skipping vending machine without NPC orders: {vending.ShortPrefabName}");
                return;
            }

            string orderName = vending.vendingOrders.name;
            if (!def)
            {
                if (data.VendingMachinesOrders.ContainsKey(orderName))
                {
                    return;
                }
            }
            List<Order> orders = new List<Order>();
            foreach (var order in vending.vendingOrders.orders)
            {
                if (order == null || order.sellItem == null || order.currencyItem == null)
                {
                    PrintWarning($"Skipping an incomplete sell order on {vending.ShortPrefabName}.");
                    continue;
                }

                orders.Add(new Order
                {
                    _comment = $"Sell {order.sellItem.displayName.english} x {order.sellItemAmount} for {order.currencyItem.displayName.english} x {order.currencyAmount}",
                    sellAmount = order.currencyAmount,
                    currencyAmount = order.sellItemAmount,
                    sellId = order.sellItem.itemid,
                    sellAsBP = order.sellItemAsBP,
                    currencyId = order.currencyItem.itemid,
                    weight = 100,
                    refillAmount = 100000,
                    refillDelay = 0.0f
                });
            }
            if (def)
            {
                if (orders == null) return;

                if (configData.General.allowConsoleOutput)
                    Puts($"Trying to save default vendingOrders for {orderName}");

                if (defaultOrders == null) defaultOrders = new StorageData();
                if (defaultOrders.VendingMachinesOrders.ContainsKey(orderName)) return;
                defaultOrders.VendingMachinesOrders.Add(orderName, orders.ToArray());
            }
            else
            {
                data.VendingMachinesOrders.Add(orderName, orders.ToArray());
            }
            if (configData.General.allowConsoleOutput)
                Puts($"Added Vending Machine: {orderName} to data file!");
            dataChanged = true;
        }

        private bool HasNpcVendingOrders(NPCVendingMachine vending)
        {
            return vending != null
                && vending.vendingOrders != null
                && vending.vendingOrders.orders != null
                && !string.IsNullOrEmpty(vending.vendingOrders.name);
        }

        private void UpdateVending(NPCVendingMachine vending)
        {
            if (vending == null || vending.IsDestroyed)
            {
                return;
            }

            // Apartment rental shops can use NPCVendingMachine without an NPC order asset.
            // They are player-operated and must not be treated as monument NPC vendors.
            if (!HasNpcVendingOrders(vending))
            {
                if (configData.General.allowConsoleOutput)
                    Puts($"Ignoring player-operated or uninitialized vending machine: {vending.ShortPrefabName}");
                return;
            }

            bool isApartment = IsApartmentEntity(vending);
            bool disableVending = isApartment
                ? configData.General.disableApartmentVendingMachines
                : configData.General.disableCompoundVendingMachines;
            bool allowCustomOrders = isApartment
                ? configData.General.allowCustomApartmentVendingMachines
                : configData.General.allowCustomCompoundVendingMachines;

            // CustomVendingSetup is the authoritative owner of Outpost prices when both
            // Compound vending options are disabled. Do not capture, install, or restore
            // sell orders in that mode.
            if (!disableVending && !allowCustomOrders)
            {
                return;
            }

            if (allowCustomOrders)
            {
                AddVendingOrders(vending, true);
                AddVendingOrders(vending);
            }

            if (disableVending)
            {
                vending.ClearSellOrders();
                vending.inventory.Clear();
            }
            else if (allowCustomOrders)
            {
                vending.vendingOrders.orders = GetNewOrders(vending);
                vending.InstallFromVendingOrders();
            }

            NextTick(() =>
            {
                if (vending == null || vending.IsDestroyed)
                    return;

                vending.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                vending.SendNetworkUpdateImmediate();
            });
        }

        private NPCVendingOrder.Entry[] GetDefaultOrders(NPCVendingMachine vending)
        {
            Order[] storedOrders;
            if (!HasNpcVendingOrders(vending)
                || defaultOrders == null
                || defaultOrders.VendingMachinesOrders == null
                || !defaultOrders.VendingMachinesOrders.TryGetValue(vending.vendingOrders.name, out storedOrders))
            {
                return null;
            }

            List<NPCVendingOrder.Entry> temp = new List<NPCVendingOrder.Entry>();
            foreach (var order in storedOrders)
            {
                temp.Add(new NPCVendingOrder.Entry
                {
                    currencyAmount = order.sellAmount,
                    currencyAsBP = order.currencyAsBP,
                    currencyItem = ItemManager.FindItemDefinition(order.currencyId),
                    sellItem = ItemManager.FindItemDefinition(order.sellId),
                    sellItemAmount = order.currencyAmount,
                    sellItemAsBP = order.sellAsBP,
                    refillAmount = 100000,
                    refillDelay = 0.0f,
                    randomDetails = new NPCVendingOrder.EntryRandom
                    {
                        weight = 100
                    }
                });
            }
            return temp.ToArray();
        }

        private NPCVendingOrder.Entry[] GetNewOrders(NPCVendingMachine vending)
        {
            Order[] storedOrders;
            if (!HasNpcVendingOrders(vending)
                || data == null
                || data.VendingMachinesOrders == null
                || !data.VendingMachinesOrders.TryGetValue(vending.vendingOrders.name, out storedOrders))
            {
                return new NPCVendingOrder.Entry[0];
            }

            List<NPCVendingOrder.Entry> temp = new List<NPCVendingOrder.Entry>();
            foreach (var order in storedOrders)
            {
                ItemDefinition currencyItem = ItemManager.FindItemDefinition(order.currencyId);
                if (currencyItem == null)
                {
                    PrintError($"Item id {order.currencyId} is invalid. Skipping sell order.");
                    continue;
                }

                ItemDefinition sellItem = ItemManager.FindItemDefinition(order.sellId);
                if (sellItem == null)
                {
                    PrintError($"Item id {order.sellId} is invalid. Skipping sell order.");
                    continue;
                }

                temp.Add(new NPCVendingOrder.Entry
                {
                    currencyAmount = order.sellAmount,
                    currencyAsBP = order.currencyAsBP,
                    currencyItem = currencyItem,
                    sellItem = sellItem,
                    sellItemAmount = order.currencyAmount,
                    sellItemAsBP = order.sellAsBP,
                    refillAmount = 100000,
                    refillDelay = 0.0f,
                    randomDetails = new NPCVendingOrder.EntryRandom
                    {
                        weight = 100
                    }
                });
            }
            return temp.ToArray();
        }
        #endregion       

        #region Commmands
        [ChatCommand("compreset")]
        private void cmdCompReset(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You doesn't have permission to that!");
                return;
            }

            Interface.Oxide.ReloadPlugin(Name);
        }

        [ConsoleCommand("compreset")]
        private void ccmdCompReset(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
                return;

            if (arg.IsAdmin == false)
            {
                Puts("You doesn't have permission to that!");
                return;
            }

            Interface.Oxide.ReloadPlugin(Name);
        }
        #endregion

        #region Save data classes
        private class StorageData
        {
            public Dictionary<string, Order[]> VendingMachinesOrders { get; set; }
        }

        private class Order
        {
            public string _comment;
            public int sellId;
            public int sellAmount;
            public bool sellAsBP;
            public int currencyId;
            public int currencyAmount;
            public bool currencyAsBP;
            public int weight;
            public int refillAmount;
            public float refillDelay;
        }
        private void SaveData()
        {
            if (dataChanged)
            {
                Interface.Oxide.DataFileSystem.WriteObject(Name, data);
                Interface.Oxide.DataFileSystem.WriteObject(Name + "_default", defaultOrders);
                dataChanged = false;
            }
        }

        #endregion

        #region Config
        private static ConfigData configData;

        private class ApartmentPricingSettings
        {
            [JsonProperty(PropertyName = "Enable Apartment Complex price overrides")]
            public bool Enabled { get; set; }

            [JsonProperty(PropertyName = "Room prices in scrap (-1 keeps vanilla)")]
            public Dictionary<string, ApartmentRoomPricing> Rooms { get; set; }

            [JsonProperty(PropertyName = "Rentable shop prices in scrap (-1 keeps vanilla)")]
            public RentableShopPricing RentableShops { get; set; }

            [JsonProperty(PropertyName = "Basement NPC master key price in scrap (-1 keeps vanilla)")]
            public int MasterKeyPrice { get; set; }
        }

        private class ApartmentRoomPricing
        {
            [JsonProperty(PropertyName = "Initial rental price")]
            public int PurchasePrice { get; set; }

            [JsonProperty(PropertyName = "Daily upkeep")]
            public int DailyUpkeep { get; set; }
        }

        private class RentableShopPricing
        {
            [JsonProperty(PropertyName = "Upfront rental price")]
            public int UpfrontPrice { get; set; }

            [JsonProperty(PropertyName = "Hourly upkeep")]
            public int HourlyUpkeep { get; set; }
        }

        class ConfigData
        {
            [JsonProperty(PropertyName = "General Settings")]
            public GeneralSettings General { get; set; }

            [JsonProperty(PropertyName = "Apartment Complex Pricing")]
            public ApartmentPricingSettings ApartmentPricing { get; set; }

            public class GeneralSettings
            {
                [JsonProperty(PropertyName = "Allow console status outputs")]
                public bool allowConsoleOutput { get; set; }
                [JsonProperty(PropertyName = "Allow custom sell list for Compound vending machines (see in data)")]
                public bool allowCustomCompoundVendingMachines { get; set; }
                [JsonProperty(PropertyName = "Allow custom sell list for Apartment Complex NPC vending machines (see in data)")]
                public bool allowCustomApartmentVendingMachines { get; set; }
                [JsonProperty(PropertyName = "Disallow Bandit NPC")]
                public bool disallowBanditNPC { get; set; }
                [JsonProperty(PropertyName = "Disallow Compound NPC")]
                public bool disallowCompoundNPC { get; set; }
                [JsonProperty(PropertyName = "Disallow Apartment Complex NPCs")]
                public bool disallowApartmentNPC { get; set; }
                [JsonProperty(PropertyName = "Disable Compound Turrets")]
                public bool disableCompoundTurrets { get; set; }
                [JsonProperty(PropertyName = "Disable Apartment Complex Turrets")]
                public bool disableApartmentTurrets { get; set; }
                [JsonProperty(PropertyName = "Disable Compound SafeZone trigger")]
                public bool disableCompoundTrigger { get; set; }
                [JsonProperty(PropertyName = "Disable Apartment Complex SafeZone trigger")]
                public bool disableApartmentTrigger { get; set; }
                [JsonProperty(PropertyName = "Disable Compound Vending Machines")]
                public bool disableCompoundVendingMachines { get; set; }
                [JsonProperty(PropertyName = "Disable Apartment Complex NPC Vending Machines")]
                public bool disableApartmentVendingMachines { get; set; }
            }

            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(configData, true);
        }

        protected override void LoadDefaultConfig() => configData = GetBaseConfig();
        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                General = new ConfigData.GeneralSettings
                {
                    allowConsoleOutput = true,
                    allowCustomCompoundVendingMachines = false,
                    allowCustomApartmentVendingMachines = true,
                    disallowBanditNPC = false,
                    disallowCompoundNPC = false,
                    disallowApartmentNPC = false,
                    disableCompoundTurrets = false,
                    disableApartmentTurrets = false,
                    disableCompoundTrigger = false,
                    disableApartmentTrigger = false,
                    disableCompoundVendingMachines = false,
                    disableApartmentVendingMachines = false
                },
                ApartmentPricing = new ApartmentPricingSettings
                {
                    Enabled = true,
                    Rooms = new Dictionary<string, ApartmentRoomPricing>
                    {
                        ["Small"] = new ApartmentRoomPricing { PurchasePrice = 25, DailyUpkeep = 10 },
                        ["Medium"] = new ApartmentRoomPricing { PurchasePrice = 150, DailyUpkeep = 50 },
                        ["Large"] = new ApartmentRoomPricing { PurchasePrice = 350, DailyUpkeep = 100 }
                    },
                    RentableShops = new RentableShopPricing
                    {
                        UpfrontPrice = 100,
                        HourlyUpkeep = 10
                    },
                    MasterKeyPrice = 1000
                },
                Version = Version
            };
        }
        protected override void SaveConfig() => Config.WriteObject(configData, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            ConfigData baseConfig = GetBaseConfig();
            if (configData.Version < new Core.VersionNumber(1, 2, 5))
            {
                configData = baseConfig;
            }
            else if (configData.Version < new Core.VersionNumber(1, 3, 0))
            {
                configData.General.allowCustomApartmentVendingMachines = true;
            }

            if (configData.Version < new Core.VersionNumber(1, 4, 0) || configData.ApartmentPricing == null)
            {
                configData.ApartmentPricing = baseConfig.ApartmentPricing;
            }
            else if (configData.Version < new Core.VersionNumber(1, 4, 1))
            {
                configData.ApartmentPricing.MasterKeyPrice = baseConfig.ApartmentPricing.MasterKeyPrice;
            }

            if (configData.Version < new Core.VersionNumber(1, 4, 6))
            {
                // CustomVendingSetup owns monument vending offers. Keeping this legacy
                // writer enabled causes its saved Compound orders to win again on restart.
                configData.General.allowCustomCompoundVendingMachines = false;
            }

            configData.Version = Version;
            PrintWarning("Config update completed!");
        }
        #endregion
    }
}
