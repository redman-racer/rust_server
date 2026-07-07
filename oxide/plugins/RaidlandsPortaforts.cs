using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RaidlandsPortaforts", "Raidlands", "1.2.7")]
    [Description("Provides Raidlands Portafort token items backed by CopyPaste placement.")]
    public class RaidlandsPortaforts : RustPlugin
    {
        private const string AdminPermission = "raidlands.portaforts.admin";
        private static readonly int GroundLayer = LayerMask.GetMask("Terrain", "World", "Water", "Default");
        private static readonly int PlacementBlockLayer = LayerMask.GetMask("Construction", "Construction Trigger", "Deployed", "Vehicle Large");
        private static readonly int RoadCheckLayer = LayerMask.GetMask("World");

        [PluginReference]
        private Plugin CopyPaste;

        [PluginReference]
        private Plugin ImageLibrary;

        private Configuration config;
        private readonly Dictionary<ulong, float> pendingThrowableTokens = new Dictionary<ulong, float>();
        private readonly List<MonumentPlacementZone> monumentPlacementZones = new List<MonumentPlacementZone>();
        private bool monumentPlacementZonesLoaded;

        private class Configuration
        {
            [JsonProperty("Token Item")]
            public TokenItem TokenItem = new TokenItem();

            [JsonProperty("Additional Token Items")]
            public TokenItem[] AdditionalTokenItems =
            {
                new TokenItem
                {
                    Shortname = "wrappedgift",
                    DisplayName = "Portafort Token",
                    RequireDisplayNameMatch = true
                }
            };

            [JsonProperty("CopyPaste File")]
            public string CopyPasteFile = "raidlands_portafort";

            [JsonProperty("CopyPaste Arguments")]
            public string[] CopyPasteArguments =
            {
                "deployables", "true",
                "inventories", "true",
                "autoheight", "false",
                "height", "0",
                "blockcollision", "0"
            };

            [JsonProperty("Use Ground Anchored Placement")]
            public bool UseGroundAnchoredPlacement = true;

            [JsonProperty("Ground Placement Distance")]
            public float GroundPlacementDistance = 12f;

            [JsonProperty("Ground Clearance")]
            public float GroundClearance = 0.15f;

            [JsonProperty("Deploy On Item Drop")]
            public bool DeployOnItemDrop = false;

            [JsonProperty("Validate Placement Footprint")]
            public bool ValidatePlacementFootprint = true;

            [JsonProperty("Placement Footprint Padding")]
            public float PlacementFootprintPadding = 0.65f;

            [JsonProperty("Placement Player Clearance")]
            public float PlacementPlayerClearance = 0.85f;

            [JsonProperty("Block Road Placement")]
            public bool BlockRoadPlacement = true;

            [JsonProperty("Road Check Height")]
            public float RoadCheckHeight = 8f;

            [JsonProperty("Road Check Depth")]
            public float RoadCheckDepth = 30f;

            [JsonProperty("Block Monument Placement")]
            public bool BlockMonumentPlacement = true;

            [JsonProperty("Monument Radius Padding")]
            public float MonumentRadiusPadding = 15f;

            [JsonProperty("Default Monument Radius")]
            public float DefaultMonumentRadius = 50f;

            [JsonProperty("Block Water Placement")]
            public bool BlockWaterPlacement = true;

            [JsonProperty("Water Clearance")]
            public float WaterClearance = 0.25f;

            [JsonProperty("Deploy On Throwable Throw")]
            public bool DeployOnThrowableThrow = true;

            [JsonProperty("Throwable Deploy Delay Seconds")]
            public float ThrowableDeployDelaySeconds = 1.25f;

            [JsonProperty("Throwable Intent Timeout Seconds")]
            public float ThrowableIntentTimeoutSeconds = 5f;

            [JsonProperty("Throwable Token Shortnames")]
            public string[] ThrowableTokenShortnames =
            {
                "grenade.f1",
                "grenade.beancan",
                "grenade.smoke",
                "grenade.flashbang",
                "grenade.molotov",
                "grenade.bee",
                "supply.signal",
                "surveycharge",
                "explosive.satchel",
                "explosive.timed"
            };

            [JsonProperty("Use Actions")]
            public string[] UseActions = { "unwrap", "open", "use" };

            [JsonProperty("Token Icon URL")]
            public string TokenIconUrl = "https://raidlands.net/assets/media/kits/portafort-token.webp";

            [JsonProperty("Token Icon ImageLibrary Key")]
            public string TokenIconImageLibraryKey = "RaidlandsPortafortToken";

            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "<color=#ce422b>[Raidlands]</color>";

            [JsonProperty("Kit Grants")]
            public Dictionary<string, int> KitGrants = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["custom.pending.portafort"] = 1,
                ["pack_portafort"] = 1
            };
        }

        private class TokenItem
        {
            [JsonProperty("Shortname")]
            public string Shortname = "grenade.smoke";

            [JsonProperty("Display Name")]
            public string DisplayName = "Portafort Token";

            [JsonProperty("Skin")]
            public ulong Skin;

            [JsonProperty("Require Display Name Match")]
            public bool RequireDisplayNameMatch = true;
        }

        private class MonumentPlacementZone
        {
            public Vector3 Center;
            public float Radius;
            public string Name;
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>() ?? new Configuration();
            }
            catch
            {
                PrintWarning("Could not read config; creating a new default config.");
                config = new Configuration();
            }

            if (config.TokenItem == null)
            {
                config.TokenItem = new TokenItem();
            }

            if (config.AdditionalTokenItems == null)
            {
                config.AdditionalTokenItems = new TokenItem[0];
            }

            if (config.CopyPasteArguments == null || config.CopyPasteArguments.Length == 0)
            {
                config.CopyPasteArguments = new Configuration().CopyPasteArguments;
            }

            if (config.GroundPlacementDistance <= 0f)
            {
                config.GroundPlacementDistance = 12f;
            }

            if (config.GroundClearance < 0f)
            {
                config.GroundClearance = 0.15f;
            }

            if (config.PlacementFootprintPadding <= 0f)
            {
                config.PlacementFootprintPadding = 0.65f;
            }

            if (config.PlacementPlayerClearance <= 0f)
            {
                config.PlacementPlayerClearance = 0.85f;
            }

            if (config.RoadCheckHeight <= 0f)
            {
                config.RoadCheckHeight = 8f;
            }

            if (config.RoadCheckDepth <= 0f)
            {
                config.RoadCheckDepth = 30f;
            }

            if (config.MonumentRadiusPadding < 0f)
            {
                config.MonumentRadiusPadding = 15f;
            }

            if (config.DefaultMonumentRadius <= 0f)
            {
                config.DefaultMonumentRadius = 50f;
            }

            if (config.WaterClearance < 0f)
            {
                config.WaterClearance = 0.25f;
            }

            if (config.ThrowableDeployDelaySeconds < 0f)
            {
                config.ThrowableDeployDelaySeconds = 1.25f;
            }

            if (config.ThrowableIntentTimeoutSeconds <= 0f)
            {
                config.ThrowableIntentTimeoutSeconds = 5f;
            }

            if (config.ThrowableTokenShortnames == null || config.ThrowableTokenShortnames.Length == 0)
            {
                config.ThrowableTokenShortnames = new Configuration().ThrowableTokenShortnames;
            }

            if (config.UseActions == null || config.UseActions.Length == 0)
            {
                config.UseActions = new Configuration().UseActions;
            }

            if (string.IsNullOrWhiteSpace(config.TokenIconUrl))
            {
                config.TokenIconUrl = new Configuration().TokenIconUrl;
            }

            if (string.IsNullOrWhiteSpace(config.TokenIconImageLibraryKey))
            {
                config.TokenIconImageLibraryKey = new Configuration().TokenIconImageLibraryKey;
            }

            if (config.KitGrants == null)
            {
                config.KitGrants = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
        }

        private void OnServerInitialized()
        {
            RegisterTokenIcon();
            LoadMonumentPlacementZones();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null || !string.Equals(plugin.Name, "ImageLibrary", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ImageLibrary = plugin;
            timer.Once(1f, RegisterTokenIcon);
        }

        [HookMethod(nameof(API_GetPortafortTokenIconUrl))]
        public string API_GetPortafortTokenIconUrl()
        {
            return GetTokenIconUrl();
        }

        [HookMethod(nameof(API_GetPortafortTokenImageKey))]
        public string API_GetPortafortTokenImageKey()
        {
            return GetTokenIconKey();
        }

        [HookMethod(nameof(API_GetPortafortTokenImageId))]
        public string API_GetPortafortTokenImageId()
        {
            if (ImageLibrary == null || !ImageLibrary.IsLoaded)
            {
                return string.Empty;
            }

            var image = ImageLibrary.Call("GetImage", GetTokenIconKey(), 0UL, false) as string;
            return image ?? string.Empty;
        }

        [ChatCommand("portafort")]
        private void CmdPortafort(BasePlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            var item = FindToken(player);
            if (item == null)
            {
                Reply(player, "You need a Portafort Token to do that.");
                return;
            }

            TryRedeem(player, item);
        }

        [ConsoleCommand("raidlands.portafort.give")]
        private void CCmdGivePortafort(ConsoleSystem.Arg arg)
        {
            if (!CanUseAdminCommand(arg))
            {
                arg.ReplyWith("You do not have permission to use this command.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                arg.ReplyWith("Usage: raidlands.portafort.give <playerNameOrSteamId> [amount]");
                return;
            }

            var targetName = arg.GetString(0);
            var target = FindPlayer(targetName);
            if (target == null)
            {
                arg.ReplyWith("Player not found.");
                return;
            }

            var amount = 1;
            if (arg.Args.Length >= 2)
            {
                int.TryParse(arg.GetString(1), out amount);
            }

            amount = Math.Max(1, amount);

            var grant = GiveTokensDetailed(target, amount);
            var detail = string.IsNullOrWhiteSpace(grant.ItemShortname) ? "" : $" ({grant.ItemShortname})";
            var dropped = grant.Dropped > 0 ? $" {grant.Dropped} dropped at their feet because inventory was full." : "";
            var failure = string.IsNullOrWhiteSpace(grant.Failure) ? "" : $" Last failure: {grant.Failure}";
            arg.ReplyWith($"Gave {grant.Given} Portafort Token(s){detail} to {target.displayName}.{dropped}{failure}");
        }

        public int GivePortafortTokens(ulong playerId, int amount)
        {
            var player = BasePlayer.FindAwakeOrSleeping(playerId.ToString());
            if (player == null)
            {
                return 0;
            }

            return GiveTokens(player, Math.Max(1, amount));
        }

        private void OnKitRedeemed(BasePlayer player, string kitName)
        {
            if (player == null || string.IsNullOrWhiteSpace(kitName) || config.KitGrants == null)
            {
                return;
            }

            int amount;
            if (!config.KitGrants.TryGetValue(kitName, out amount) || amount <= 0)
            {
                return;
            }

            var given = GiveTokens(player, amount);
            if (given > 0)
            {
                Reply(player, $"Granted {given} Portafort Token(s) from kit '{kitName}'.");
            }
        }

        private object OnItemAction(Item item, string action, BasePlayer player)
        {
            if (player == null || item == null || !IsConfiguredUseAction(action) || !IsToken(item))
            {
                return null;
            }

            TryRedeem(player, item);
            return true;
        }

        private void OnItemUse(Item item, int amountToUse)
        {
            if (!config.DeployOnThrowableThrow || !IsThrowableToken(item))
            {
                return;
            }

            var player = item?.parent?.playerOwner ?? item?.GetOwnerPlayer();
            if (player == null)
            {
                return;
            }

            MarkPendingThrowable(player.userID);
        }

        private void OnItemDropped(Item item, BaseEntity entity)
        {
            if (!config.DeployOnItemDrop || item == null || entity == null || !IsToken(item))
            {
                return;
            }

            var player = item.GetOwnerPlayer() ?? item.parent?.playerOwner;
            ulong playerId;
            if (player != null)
            {
                playerId = player.userID.Get();
            }
            else
            {
                playerId = entity.OwnerID;
            }

            if (playerId == 0)
            {
                return;
            }

            timer.Once(0.1f, () => TryRedeemWorldToken(playerId, entity, false));
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
        {
            TryHandleThrowableToken(player, entity);
        }

        private void OnExplosiveDropped(BasePlayer player, BaseEntity entity, ThrownWeapon thrown)
        {
            TryHandleThrowableToken(player, entity);
        }

        private bool TryRedeem(BasePlayer player, Item item)
        {
            if (CopyPaste == null || !CopyPaste.IsLoaded)
            {
                Reply(player, "Portaforts are not ready yet. CopyPaste is not loaded.");
                return false;
            }

            var filename = (config.CopyPasteFile ?? "").Trim();
            if (string.IsNullOrEmpty(filename))
            {
                Reply(player, "Portaforts are not configured yet.");
                return false;
            }

            var dataPath = $"copypaste/{filename}";
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(dataPath))
            {
                Reply(player, $"Portafort layout '{filename}' is missing. Your token was not consumed.");
                return false;
            }

            string unusableReason;
            if (!IsCopyPasteDataUsable(dataPath, out unusableReason))
            {
                Reply(player, $"Portafort layout '{filename}' is broken or empty: {unusableReason}. Your token was not consumed. Re-copy it after reloading CopyPaste.");
                return false;
            }

            var result = PastePortafort(player, filename, dataPath, null);
            if (!IsPasteSuccess(result))
            {
                Reply(player, $"Portafort placement failed: {FormatResult(result)}. Your token was not consumed.");
                return false;
            }

            ConsumeOne(item);
            Reply(player, "Portafort deployed.");
            return true;
        }

        private bool TryRedeemWorldToken(ulong playerId, BaseEntity tokenEntity, bool refundOnFailure)
        {
            if (tokenEntity == null || tokenEntity.IsDestroyed)
            {
                return false;
            }

            var player = BasePlayer.FindAwakeOrSleeping(playerId.ToString());
            if (player == null)
            {
                return false;
            }

            Vector3 groundPoint;
            if (!TrySnapToGround(tokenEntity.transform.position, out groundPoint))
            {
                Reply(player, "Could not find a ground placement point. Your token was not consumed.");
                if (refundOnFailure)
                {
                    GiveTokens(player, 1);
                    Reply(player, "Your Portafort Token was refunded.");
                }
                return false;
            }

            var deployed = TryRedeemAtGroundPoint(player, groundPoint, refundOnFailure);
            if (deployed && tokenEntity != null && !tokenEntity.IsDestroyed)
            {
                tokenEntity.Kill();
            }

            return deployed;
        }

        private bool TryRedeemAtGroundPoint(BasePlayer player, Vector3 groundPoint, bool refundOnFailure)
        {
            if (CopyPaste == null || !CopyPaste.IsLoaded)
            {
                Reply(player, "Portaforts are not ready yet. CopyPaste is not loaded.");
                if (refundOnFailure)
                {
                    GiveTokens(player, 1);
                }
                return false;
            }

            var filename = (config.CopyPasteFile ?? "").Trim();
            if (string.IsNullOrEmpty(filename))
            {
                Reply(player, "Portaforts are not configured yet.");
                if (refundOnFailure)
                {
                    GiveTokens(player, 1);
                }
                return false;
            }

            var dataPath = $"copypaste/{filename}";
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(dataPath))
            {
                Reply(player, $"Portafort layout '{filename}' is missing.");
                if (refundOnFailure)
                {
                    GiveTokens(player, 1);
                }
                return false;
            }

            string unusableReason;
            if (!IsCopyPasteDataUsable(dataPath, out unusableReason))
            {
                Reply(player, $"Portafort layout '{filename}' is broken or empty: {unusableReason}. Re-copy it after reloading CopyPaste.");
                if (refundOnFailure)
                {
                    GiveTokens(player, 1);
                }
                return false;
            }

            var result = PastePortafort(player, filename, dataPath, groundPoint);
            if (!IsPasteSuccess(result))
            {
                Reply(player, $"Portafort placement failed: {FormatResult(result)}.");
                if (refundOnFailure)
                {
                    GiveTokens(player, 1);
                    Reply(player, "Your Portafort Token was refunded.");
                }
                return false;
            }

            Reply(player, "Portafort deployed.");
            return true;
        }

        private object PastePortafort(BasePlayer player, string filename, string dataPath, Vector3? groundOverride)
        {
            string placementError;
            var args = BuildPasteArguments(player, dataPath, groundOverride, out placementError);
            if (args == null)
            {
                return placementError;
            }

            object result;
            if (HasPositionArgument(args))
            {
                result = CopyPaste.Call("API_RaidlandsTryPasteAtPosition", player.UserIDString, filename, args);
                if (result != null)
                {
                    return result;
                }
            }

            result = CopyPaste.Call("API_RaidlandsTryPaste", player.UserIDString, filename, args);
            if (result != null)
            {
                return result;
            }

            result = CopyPaste.Call("API_RaidlandsTryPasteFromSteamId", player.userID, filename, args);
            if (result != null)
            {
                return result;
            }

            result = CopyPaste.Call("TryPasteFromSteamId", player.userID, filename, args, null, null);
            if (result != null)
            {
                return result;
            }

            result = CopyPaste.Call("TryPasteFromSteamId", player.userID, filename, args, null);
            if (result != null)
            {
                return result;
            }

            result = CopyPaste.Call("TryPasteFromSteamId", player.userID, filename, args);
            if (result != null)
            {
                return result;
            }

            return "CopyPaste API did not respond. Upload and reload Copy Paste v4.2.15 or newer.";
        }

        private string[] BuildPasteArguments(BasePlayer player, string dataPath, Vector3? groundOverride, out string error)
        {
            error = null;

            var args = new List<string>(config.CopyPasteArguments ?? new string[0]);
            if (!config.UseGroundAnchoredPlacement)
            {
                return args.ToArray();
            }

            Vector3 groundPoint;
            if (groundOverride.HasValue)
            {
                groundPoint = groundOverride.Value;
            }
            else if (!TryGetGroundTarget(player, out groundPoint))
            {
                error = "Could not find a ground placement point. Aim at open ground and try again";
                return null;
            }

            float lowestRelativeY;
            if (!TryGetLowestRelativeY(dataPath, out lowestRelativeY))
            {
                error = "Could not read the Portafort layout height. Re-copy the Portafort layout and try again";
                return null;
            }

            var pasteOrigin = groundPoint;
            pasteOrigin.y = groundPoint.y - lowestRelativeY + config.GroundClearance;

            if (!TryValidatePlacement(player, dataPath, pasteOrigin, out error))
            {
                return null;
            }

            args.Add("height");
            args.Add("0");
            args.Add("autoheight");
            args.Add("false");
            args.Add("position");
            args.Add(FormatVector(pasteOrigin));

            return args.ToArray();
        }

        private bool TryValidatePlacement(BasePlayer player, string dataPath, Vector3 pasteOrigin, out string error)
        {
            error = null;

            if (!config.ValidatePlacementFootprint)
            {
                return true;
            }

            Vector3 min;
            Vector3 max;
            if (!TryGetLayoutBounds(dataPath, out min, out max))
            {
                error = "Could not read the Portafort footprint. Re-copy the Portafort layout and try again";
                return false;
            }

            var yaw = player == null ? 0f : player.GetNetworkRotation().eulerAngles.y;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var footprintPadding = Math.Max(0.1f, config.PlacementFootprintPadding);
            var playerClearance = Math.Max(0.25f, config.PlacementPlayerClearance);

            if (PlacementTouchesRestrictedWorld(pasteOrigin, rotation, min, max, footprintPadding, out error))
            {
                return false;
            }

            if (PlacementOverlapsBlockedWorld(pasteOrigin, rotation, min, max, footprintPadding))
            {
                error = "That spot is too close to a building, deployable, or vehicle. Aim at open ground and try again";
                return false;
            }

            if (PlacementOverlapsPlayer(pasteOrigin, rotation, min, max, playerClearance))
            {
                error = "That spot is too close to a player. Aim at clear ground and try again";
                return false;
            }

            return true;
        }

        private bool PlacementTouchesRestrictedWorld(Vector3 pasteOrigin, Quaternion rotation, Vector3 min, Vector3 max, float padding, out string error)
        {
            error = null;

            foreach (var sample in PlacementFootprintSamples(pasteOrigin, rotation, min, max, padding))
            {
                if (config.BlockRoadPlacement && IsRoadSurface(sample))
                {
                    error = "You cannot deploy a Portafort on a road. Aim at open ground and try again";
                    return true;
                }

                string monumentName;
                if (config.BlockMonumentPlacement && IsInBlockedMonument(sample, out monumentName))
                {
                    error = "You cannot deploy a Portafort inside or too close to a monument. Aim farther away and try again";
                    return true;
                }

                if (config.BlockWaterPlacement && IsWaterSurface(sample))
                {
                    error = "You cannot deploy a Portafort in water. Aim at dry open ground and try again";
                    return true;
                }
            }

            return false;
        }

        private IEnumerable<Vector3> PlacementFootprintSamples(Vector3 pasteOrigin, Quaternion rotation, Vector3 min, Vector3 max, float padding)
        {
            var minX = min.x - padding;
            var maxX = max.x + padding;
            var minZ = min.z - padding;
            var maxZ = max.z + padding;
            var centerX = (minX + maxX) * 0.5f;
            var centerZ = (minZ + maxZ) * 0.5f;

            yield return pasteOrigin + rotation * new Vector3(centerX, 0f, centerZ);
            yield return pasteOrigin + rotation * new Vector3(minX, 0f, minZ);
            yield return pasteOrigin + rotation * new Vector3(minX, 0f, maxZ);
            yield return pasteOrigin + rotation * new Vector3(maxX, 0f, minZ);
            yield return pasteOrigin + rotation * new Vector3(maxX, 0f, maxZ);
            yield return pasteOrigin + rotation * new Vector3(centerX, 0f, minZ);
            yield return pasteOrigin + rotation * new Vector3(centerX, 0f, maxZ);
            yield return pasteOrigin + rotation * new Vector3(minX, 0f, centerZ);
            yield return pasteOrigin + rotation * new Vector3(maxX, 0f, centerZ);
        }

        private bool IsRoadSurface(Vector3 position)
        {
            if (RoadCheckLayer == 0)
            {
                return false;
            }

            var start = position + Vector3.up * Math.Max(1f, config.RoadCheckHeight);
            var distance = Math.Max(1f, config.RoadCheckHeight + config.RoadCheckDepth);
            foreach (var hit in Physics.RaycastAll(start, Vector3.down, distance, RoadCheckLayer, QueryTriggerInteraction.Ignore))
            {
                var colliderName = hit.collider == null ? string.Empty : hit.collider.name;
                if (colliderName.IndexOf("road", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsWaterSurface(Vector3 position)
        {
            if (TerrainMeta.WaterMap == null || TerrainMeta.HeightMap == null)
            {
                return false;
            }

            var terrainHeight = TerrainMeta.HeightMap.GetHeight(position);
            var waterHeight = TerrainMeta.WaterMap.GetHeight(position);
            return waterHeight > terrainHeight + Math.Max(0f, config.WaterClearance);
        }

        private bool IsInBlockedMonument(Vector3 position, out string monumentName)
        {
            monumentName = null;
            EnsureMonumentPlacementZones();

            foreach (var zone in monumentPlacementZones)
            {
                var dx = position.x - zone.Center.x;
                var dz = position.z - zone.Center.z;
                if (dx * dx + dz * dz > zone.Radius * zone.Radius)
                {
                    continue;
                }

                monumentName = zone.Name;
                return true;
            }

            return false;
        }

        private void EnsureMonumentPlacementZones()
        {
            if (monumentPlacementZonesLoaded)
            {
                return;
            }

            LoadMonumentPlacementZones();
        }

        private void LoadMonumentPlacementZones()
        {
            monumentPlacementZones.Clear();
            monumentPlacementZonesLoaded = true;

            if (TerrainMeta.Path?.Monuments == null)
            {
                return;
            }

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null)
                {
                    continue;
                }

                var radius = GetMonumentPlacementRadius(monument) + Math.Max(0f, config.MonumentRadiusPadding);
                if (radius <= 0f)
                {
                    continue;
                }

                monumentPlacementZones.Add(new MonumentPlacementZone
                {
                    Center = monument.transform.position,
                    Radius = radius,
                    Name = GetMonumentShortName(monument)
                });
            }
        }

        private float GetMonumentPlacementRadius(MonumentInfo monument)
        {
            switch (GetMonumentShortName(monument))
            {
                case "airfield_1": return 255f;
                case "bandit_town": return 105f;
                case "cave_large_hard":
                case "cave_large_medium":
                case "cave_large_sewers_hard":
                case "cave_medium_easy":
                case "cave_medium_hard":
                case "cave_medium_medium":
                case "cave_small_easy":
                case "cave_small_hard":
                case "cave_small_medium": return 75f;
                case "compound": return 255f;
                case "entrance": return 20f;
                case "excavator_1": return 150f;
                case "fishing_village_a":
                case "fishing_village_b":
                case "fishing_village_c": return 55f;
                case "gas_station_1": return 60f;
                case "harbor_1":
                case "harbor_2": return 135f;
                case "junkyard_1": return 105f;
                case "launch_site_1": return 245f;
                case "lighthouse": return 50f;
                case "military_tunnel_1": return 105f;
                case "mining_quarry_a":
                case "mining_quarry_b":
                case "mining_quarry_c": return 30f;
                case "OilrigAI": return 100f;
                case "OilrigAI2": return 200f;
                case "power_sub_big_1":
                case "power_sub_big_2": return 30f;
                case "power_sub_small_1":
                case "power_sub_small_2": return 25f;
                case "powerplant_1": return 145f;
                case "radtown_small_3": return 95f;
                case "satellite_dish": return 85f;
                case "sphere_tank": return 75f;
                case "stables_a":
                case "stables_b": return 80f;
                case "supermarket_1": return 60f;
                case "swamp_a":
                case "swamp_b": return 30f;
                case "swamp_c": return 55f;
                case "trainyard_1": return 145f;
                case "warehouse": return 50f;
                case "water_treatment_plant_1": return 175f;
                case "water_well_a":
                case "water_well_b":
                case "water_well_c":
                case "water_well_d":
                case "water_well_e": return 30f;
            }

            return Math.Max(1f, config.DefaultMonumentRadius);
        }

        private string GetMonumentShortName(MonumentInfo monument)
        {
            var name = monument?.name ?? string.Empty;
            var separator = name.LastIndexOf('/');
            return (separator > 0 ? name.Substring(separator + 1) : name).Replace(".prefab", "");
        }

        private bool PlacementOverlapsBlockedWorld(Vector3 pasteOrigin, Quaternion rotation, Vector3 min, Vector3 max, float padding)
        {
            if (PlacementBlockLayer == 0)
            {
                return false;
            }

            var localCenter = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, (min.z + max.z) * 0.5f);
            var center = pasteOrigin + rotation * localCenter;
            var halfExtents = new Vector3(
                Math.Max(0.5f, (max.x - min.x) * 0.5f + padding),
                Math.Max(0.5f, (max.y - min.y) * 0.5f + padding),
                Math.Max(0.5f, (max.z - min.z) * 0.5f + padding));

            return Physics.CheckBox(center, halfExtents, rotation, PlacementBlockLayer, QueryTriggerInteraction.Ignore);
        }

        private bool PlacementOverlapsPlayer(Vector3 pasteOrigin, Quaternion rotation, Vector3 min, Vector3 max, float clearance)
        {
            return PlayerListOverlapsPlacement(BasePlayer.activePlayerList, pasteOrigin, rotation, min, max, clearance)
                || PlayerListOverlapsPlacement(BasePlayer.sleepingPlayerList, pasteOrigin, rotation, min, max, clearance);
        }

        private bool PlayerListOverlapsPlacement(IEnumerable<BasePlayer> players, Vector3 pasteOrigin, Quaternion rotation, Vector3 min, Vector3 max, float clearance)
        {
            if (players == null)
            {
                return false;
            }

            foreach (var player in players)
            {
                if (PlayerOverlapsPlacement(player, pasteOrigin, rotation, min, max, clearance))
                {
                    return true;
                }
            }

            return false;
        }

        private bool PlayerOverlapsPlacement(BasePlayer player, Vector3 pasteOrigin, Quaternion rotation, Vector3 min, Vector3 max, float clearance)
        {
            if (player == null || player.IsDestroyed || player.IsDead())
            {
                return false;
            }

            var position = player.transform.position;
            var minY = pasteOrigin.y + min.y - clearance;
            var maxY = pasteOrigin.y + max.y + clearance;
            if (position.y > maxY || position.y + 1.9f < minY)
            {
                return false;
            }

            var local = Quaternion.Inverse(rotation) * (position - pasteOrigin);
            return local.x >= min.x - clearance
                && local.x <= max.x + clearance
                && local.z >= min.z - clearance
                && local.z <= max.z + clearance;
        }

        private bool HasPositionArgument(string[] args)
        {
            if (args == null)
            {
                return false;
            }

            for (var i = 0; i + 1 < args.Length; i += 2)
            {
                if (string.Equals(args[i], "position", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetGroundTarget(BasePlayer player, out Vector3 groundPoint)
        {
            groundPoint = Vector3.zero;

            if (player?.eyes == null)
            {
                return false;
            }

            var distance = Math.Max(1f, config.GroundPlacementDistance);
            var headRay = player.eyes.HeadRay();

            RaycastHit hitInfo;
            if (Physics.Raycast(headRay, out hitInfo, distance, GroundLayer))
            {
                return TrySnapToGround(hitInfo.point, out groundPoint);
            }

            return TrySnapToGround(headRay.origin + headRay.direction * distance, out groundPoint);
        }

        private bool TrySnapToGround(Vector3 position, out Vector3 groundPoint)
        {
            groundPoint = position;

            RaycastHit hitInfo;
            if (Physics.Raycast(position + Vector3.up * 250f, Vector3.down, out hitInfo, 500f, GroundLayer))
            {
                groundPoint = hitInfo.point;
                return true;
            }

            if (TerrainMeta.HeightMap == null)
            {
                return false;
            }

            groundPoint.y = TerrainMeta.HeightMap.GetHeight(position);
            return true;
        }

        private void MarkPendingThrowable(ulong playerId)
        {
            var timeout = Math.Max(1f, config.ThrowableIntentTimeoutSeconds);
            pendingThrowableTokens[playerId] = Time.realtimeSinceStartup + timeout;

            timer.Once(timeout + 0.25f, () =>
            {
                float expiresAt;
                if (pendingThrowableTokens.TryGetValue(playerId, out expiresAt) && expiresAt <= Time.realtimeSinceStartup)
                {
                    pendingThrowableTokens.Remove(playerId);
                }
            });
        }

        private bool ConsumePendingThrowable(ulong playerId)
        {
            float expiresAt;
            if (!pendingThrowableTokens.TryGetValue(playerId, out expiresAt))
            {
                return false;
            }

            pendingThrowableTokens.Remove(playerId);
            return expiresAt >= Time.realtimeSinceStartup;
        }

        private void TryHandleThrowableToken(BasePlayer player, BaseEntity entity)
        {
            if (!config.DeployOnThrowableThrow || player == null || entity == null)
            {
                return;
            }

            if (!ConsumePendingThrowable(player.userID) && !IsThrowableToken(player.GetActiveItem()))
            {
                return;
            }

            var playerId = player.userID;
            var delay = Math.Max(0f, config.ThrowableDeployDelaySeconds);
            timer.Once(delay, () =>
            {
                if (entity == null || entity.IsDestroyed)
                {
                    var refundPlayer = BasePlayer.FindAwakeOrSleeping(playerId.ToString());
                    if (refundPlayer != null)
                    {
                        GiveTokens(refundPlayer, 1);
                        Reply(refundPlayer, "Your Portafort Token was refunded because the thrown token disappeared before deployment.");
                    }
                    return;
                }

                TryRedeemWorldToken(playerId, entity, true);

                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill();
                }
            });
        }

        private bool TryGetLowestRelativeY(string dataPath, out float lowestY)
        {
            lowestY = 0f;

            try
            {
                var data = Interface.Oxide.DataFileSystem.GetDatafile(dataPath);
                var entities = data?["entities"] as System.Collections.IEnumerable;
                if (entities == null)
                {
                    return false;
                }

                var found = false;
                foreach (var entity in entities)
                {
                    float y;
                    if (!TryGetRelativeY(entity, out y))
                    {
                        continue;
                    }

                    if (!found || y < lowestY)
                    {
                        lowestY = y;
                    }

                    found = true;
                }

                return found;
            }
            catch (Exception exception)
            {
                PrintWarning($"Could not read Portafort layout height from '{dataPath}': {exception.Message}");
                return false;
            }
        }

        private bool TryGetLayoutBounds(string dataPath, out Vector3 min, out Vector3 max)
        {
            min = Vector3.zero;
            max = Vector3.zero;

            try
            {
                var data = Interface.Oxide.DataFileSystem.GetDatafile(dataPath);
                var entities = data?["entities"] as System.Collections.IEnumerable;
                if (entities == null)
                {
                    return false;
                }

                var found = false;
                foreach (var entity in entities)
                {
                    Vector3 position;
                    if (!TryGetRelativePosition(entity, out position))
                    {
                        continue;
                    }

                    if (!found)
                    {
                        min = position;
                        max = position;
                        found = true;
                        continue;
                    }

                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }

                return found;
            }
            catch (Exception exception)
            {
                PrintWarning($"Could not read Portafort layout bounds from '{dataPath}': {exception.Message}");
                return false;
            }
        }

        private bool TryGetRelativeY(object entityObject, out float y)
        {
            y = 0f;

            Vector3 position;
            if (!TryGetRelativePosition(entityObject, out position))
            {
                return false;
            }

            y = position.y;
            return true;
        }

        private bool TryGetRelativePosition(object entityObject, out Vector3 position)
        {
            position = Vector3.zero;

            var entity = entityObject as Dictionary<string, object>;
            if (entity == null)
            {
                return false;
            }

            object rawPos;
            if (!entity.TryGetValue("pos", out rawPos))
            {
                return false;
            }

            var pos = rawPos as Dictionary<string, object>;
            if (pos == null)
            {
                return false;
            }

            object rawX;
            object rawY;
            object rawZ;
            if (!pos.TryGetValue("x", out rawX) || rawX == null)
            {
                return false;
            }

            if (!pos.TryGetValue("y", out rawY) || rawY == null)
            {
                return false;
            }

            if (!pos.TryGetValue("z", out rawZ) || rawZ == null)
            {
                return false;
            }

            position = new Vector3(
                Convert.ToSingle(rawX, CultureInfo.InvariantCulture),
                Convert.ToSingle(rawY, CultureInfo.InvariantCulture),
                Convert.ToSingle(rawZ, CultureInfo.InvariantCulture));
            return true;
        }

        private string FormatVector(Vector3 position)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", position.x, position.y, position.z);
        }

        private bool IsCopyPasteDataUsable(string dataPath, out string reason)
        {
            reason = null;

            try
            {
                var data = Interface.Oxide.DataFileSystem.GetDatafile(dataPath);
                if (data == null)
                {
                    reason = "the file could not be opened";
                    return false;
                }

                var defaultSection = data["default"];
                if (defaultSection == null)
                {
                    reason = "it has no default section";
                    return false;
                }

                var entities = data["entities"];
                if (entities == null)
                {
                    reason = "it has no entities section";
                    return false;
                }

                if (CountEntities(entities) <= 0)
                {
                    reason = "it has no saved entities";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                reason = $"it could not be read ({exception.GetType().Name})";
                return false;
            }
        }

        private int CountEntities(object entities)
        {
            if (entities == null || entities is string)
            {
                return 0;
            }

            var collection = entities as System.Collections.ICollection;
            if (collection != null)
            {
                return collection.Count;
            }

            var enumerable = entities as System.Collections.IEnumerable;
            if (enumerable == null)
            {
                return 0;
            }

            foreach (var ignored in enumerable)
            {
                return 1;
            }

            return 0;
        }

        private bool IsPasteSuccess(object result)
        {
            if (result is bool)
            {
                return (bool)result;
            }

            return false;
        }

        private string FormatResult(object result)
        {
            if (result == null)
            {
                return "CopyPaste did not return a success result";
            }

            return result.ToString();
        }

        private int GiveTokens(BasePlayer player, int amount)
        {
            return GiveTokensDetailed(player, amount).Given;
        }

        private class GiveTokenResult
        {
            public int Given;
            public int Dropped;
            public string ItemShortname;
            public string Failure;
        }

        private GiveTokenResult GiveTokensDetailed(BasePlayer player, int amount)
        {
            var result = new GiveTokenResult();
            for (var i = 0; i < amount; i++)
            {
                TokenItem tokenItem;
                var item = CreateToken(out tokenItem);
                if (item == null)
                {
                    result.Failure = "Could not create any configured Portafort token item.";
                    return result;
                }

                result.ItemShortname = tokenItem?.Shortname ?? item.info?.shortname;
                if (player.inventory.GiveItem(item))
                {
                    result.Given++;
                    continue;
                }

                item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                result.Given++;
                result.Dropped++;
            }

            return result;
        }

        private Item CreateToken(out TokenItem usedTokenItem)
        {
            usedTokenItem = null;

            var tokenItems = GetTokenCreationCandidates();
            foreach (var tokenItem in tokenItems)
            {
                var item = CreateTokenFromDefinition(tokenItem);
                if (item == null)
                {
                    PrintWarning($"Could not create Portafort token item '{tokenItem.Shortname}'.");
                    continue;
                }

                usedTokenItem = tokenItem;
                return item;
            }

            return null;
        }

        private List<TokenItem> GetTokenCreationCandidates()
        {
            var tokenItems = new List<TokenItem>();
            AddTokenCreationCandidate(tokenItems, config.TokenItem);

            if (config.AdditionalTokenItems != null)
            {
                foreach (var tokenItem in config.AdditionalTokenItems)
                {
                    AddTokenCreationCandidate(tokenItems, tokenItem);
                }
            }

            return tokenItems;
        }

        private void AddTokenCreationCandidate(List<TokenItem> tokenItems, TokenItem tokenItem)
        {
            if (tokenItem == null || string.IsNullOrWhiteSpace(tokenItem.Shortname))
            {
                return;
            }

            foreach (var existing in tokenItems)
            {
                if (string.Equals(existing.Shortname, tokenItem.Shortname, StringComparison.OrdinalIgnoreCase)
                    && existing.Skin == tokenItem.Skin)
                {
                    return;
                }
            }

            tokenItems.Add(tokenItem);
        }

        private void RegisterTokenIcon()
        {
            if (ImageLibrary == null || !ImageLibrary.IsLoaded)
            {
                return;
            }

            var url = GetTokenIconUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            ImageLibrary.Call("AddImage", url, GetTokenIconKey(), 0UL, null);
        }

        private string GetTokenIconUrl()
        {
            var url = (config.TokenIconUrl ?? "").Trim();
            if (url.Length == 0)
            {
                return string.Empty;
            }

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            if (url.StartsWith("/", StringComparison.Ordinal))
            {
                return "https://raidlands.net" + url;
            }

            return url;
        }

        private string GetTokenIconKey()
        {
            var key = (config.TokenIconImageLibraryKey ?? "").Trim();
            return key.Length == 0 ? "RaidlandsPortafortToken" : key;
        }

        private Item CreateTokenFromDefinition(TokenItem tokenItem)
        {
            var item = ItemManager.CreateByName(tokenItem.Shortname, 1, tokenItem.Skin);
            if (item == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(tokenItem.DisplayName))
            {
                item.name = tokenItem.DisplayName;
            }

            item.MarkDirty();
            return item;
        }

        private Item FindToken(BasePlayer player)
        {
            return FindTokenInContainer(player.inventory.containerMain)
                ?? FindTokenInContainer(player.inventory.containerBelt)
                ?? FindTokenInContainer(player.inventory.containerWear);
        }

        private Item FindTokenInContainer(ItemContainer container)
        {
            if (container?.itemList == null)
            {
                return null;
            }

            foreach (var item in container.itemList)
            {
                if (IsToken(item))
                {
                    return item;
                }
            }

            return null;
        }

        private bool IsToken(Item item)
        {
            if (item?.info == null)
            {
                return false;
            }

            if (MatchesTokenItem(item, config.TokenItem))
            {
                return true;
            }

            if (config.AdditionalTokenItems != null)
            {
                foreach (var tokenItem in config.AdditionalTokenItems)
                {
                    if (MatchesTokenItem(item, tokenItem))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool MatchesTokenItem(Item item, TokenItem tokenItem)
        {
            if (item?.info == null || tokenItem == null)
            {
                return false;
            }

            if (!string.Equals(item.info.shortname, tokenItem.Shortname, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (tokenItem.Skin != 0 && item.skin != tokenItem.Skin)
            {
                return false;
            }

            return !tokenItem.RequireDisplayNameMatch
                || string.Equals(item.name ?? "", tokenItem.DisplayName ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsThrowableToken(Item item)
        {
            return IsToken(item) && IsConfiguredThrowableShortname(item.info.shortname);
        }

        private bool IsConfiguredThrowableShortname(string shortname)
        {
            if (string.IsNullOrWhiteSpace(shortname) || config.ThrowableTokenShortnames == null)
            {
                return false;
            }

            foreach (var throwableShortname in config.ThrowableTokenShortnames)
            {
                if (string.Equals(shortname, throwableShortname, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsConfiguredUseAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            foreach (var useAction in config.UseActions)
            {
                if (string.Equals(action, useAction, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ConsumeOne(Item item)
        {
            if (item.amount > 1)
            {
                item.amount -= 1;
                item.MarkDirty();
                return;
            }

            item.RemoveFromContainer();
            item.Remove();
        }

        private bool CanUseAdminCommand(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null || arg.Connection.authLevel > 0 || arg.IsAdmin)
            {
                return true;
            }

            var player = arg.Connection.player as BasePlayer;
            return player != null && permission.UserHasPermission(player.UserIDString, AdminPermission);
        }

        private BasePlayer FindPlayer(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            ulong id;
            if (ulong.TryParse(value, out id))
            {
                return BasePlayer.FindAwakeOrSleeping(id.ToString());
            }

            value = value.ToLowerInvariant();
            foreach (var player in BasePlayer.allPlayerList)
            {
                if (player.displayName != null && player.displayName.ToLowerInvariant().Contains(value))
                {
                    return player;
                }
            }

            return null;
        }

        private void Reply(BasePlayer player, string message)
        {
            player.ChatMessage($"{config.ChatPrefix} {message}");
        }
    }
}
