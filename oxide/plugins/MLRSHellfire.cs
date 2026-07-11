using UnityEngine;
using System.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Text;
using System;
using Facepunch;

namespace Oxide.Plugins
{
    [Info("MLRS Hellfire", "Timm3D", "1.0.9")]
    [Description("Manage MLRS and/or call MLRS missiles to a specific point, in different ways.")]
    internal class MLRSHellfire : RustPlugin
    {
        #region Permissions

        //Master Permission -> Let u do everything. DON'T GIVE THIS PERMISSION NORMAL PLAYERS!
        private const string MLRSHellfire_Admin = "mlrshellfire.admin";

        private const string MLRSHellfire_MLRSMountBypass = "mlrshellfire.mlrsmountbypass";
        private const string MLRSHellfire_UseRemoteMLRS = "mlrshellfire.useremotemlrs";
        private const string MLRSHellfire_RemoteMLRS_AllowTargetingPlayer = "mlrshellfire.allowtargetingplayer";
        private const string MLRSHellfire_RemoteMLRS_MLRSBrokenBypass = "mlrshellfire.mlrsbrokenbypass";

        #endregion

        #region Oxide Stuff

        private PluginConfig mConfig;

        private void Init()
        {
            permission.RegisterPermission(MLRSHellfire_Admin, this);

            permission.RegisterPermission(MLRSHellfire_MLRSMountBypass, this);
            permission.RegisterPermission(MLRSHellfire_UseRemoteMLRS, this);
            permission.RegisterPermission(MLRSHellfire_RemoteMLRS_AllowTargetingPlayer, this);
            permission.RegisterPermission(MLRSHellfire_RemoteMLRS_MLRSBrokenBypass, this);
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                mConfig = Config.ReadObject<PluginConfig>();

                if (mConfig == null)
                {
                    LoadDefaultConfig();
                    SaveConfig();
                }

                if (mConfig.HellfireFireInterval <= 0.0f)
                    mConfig.HellfireFireInterval = 0.1f;

                if (mConfig.MLRSFireInterval <= 0.0f)
                    mConfig.MLRSFireInterval = 0.1f;

                if (mConfig.RemoteMLRSFireInterval <= 0f)
                    mConfig.RemoteMLRSFireInterval = 0.1f;
            }
            catch
            {
                LoadDefaultConfig();
                SaveConfig();
                return;
            }
        }

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                PluginPicture = 76561198838421574,
                PluginPrefix = "<color=#191A28>[</color><color=#CC3B28>MLRS Hellfire</color><color=#191A28>]</color> ",
                HellfireRocketDamageModifier = 1.0f,
                HellfireRocketExplosiveRadiusModifier = 1.0f,
                HellfireFireInterval = 0.3f,
                MLRSFireInterval = 1.0f,
                RemoteMLRSFireInterval = 1.0f,
                AllowUsingOfMLRSForAllPlayers = true,
                HellFireMaxRocketAmountToSpawn = 50,
            };
        }

        protected override void LoadDefaultConfig()
        {
            mConfig = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(mConfig, true);
        }

        class PluginConfig
        {
            [JsonProperty]
            public ulong PluginPicture { get; set; }

            [JsonProperty]
            public string PluginPrefix { get; set; }

            [JsonProperty]
            public float HellfireRocketDamageModifier { get; set; }

            [JsonProperty]
            public float HellfireRocketExplosiveRadiusModifier { get; set; }

            [JsonProperty("MLRSFireInterval in seconds")]
            public float MLRSFireInterval { get; set; }

            [JsonProperty("RemoteMLRSFireInterval in seconds")]
            public float RemoteMLRSFireInterval { get; set; }

            [JsonProperty("HellfireFireInterval in seconds")]
            public float HellfireFireInterval { get; set; }

            [JsonProperty("Allow using of MLRS for all players like in vanilla rust")]
            public bool AllowUsingOfMLRSForAllPlayers { get; set; }

            [JsonProperty("Max amount of rockets which can spawn when using hellfire command with custom missle amount")]
            public uint HellFireMaxRocketAmountToSpawn { get; set; }
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Note_MLRS_StartedAttack"] = Note_MLRS_StartedAttack,
                ["Note_MLRS_FiringIn10Sec"] = Note_MLRS_FiringIn10Sec,
                ["Note_MLRS_Fixed"] = Note_MLRS_Fixed,
                ["Note_All_MLRS_Fixed"] = Note_All_MLRS_Fixed,
                ["Note_MLRS_Enabled"] = Note_MLRS_Enabled,
                ["Note_MLRS_Disabled"] = Note_MLRS_Disabled,
                ["Note_MLRS_Status_Enabled"] = Note_MLRS_Status_Enabled,
                ["Note_MLRS_Status_Disabled"] = Note_MLRS_Status_Disabled,

                ["Error_MLRS_NoMLRS"] = Error_MLRS_NoMLRS,
                ["Error_MLRS_CantFindPlayer"] = Error_MLRS_CantFindPlayer,
                ["Error_MLRS_NoMapMark"] = Error_MLRS_NoMapMark,
                ["Error_MLRS_Disabled"] = Error_MLRS_Disabled,
                ["Error_MLRS_IsBusy"] = Error_MLRS_IsBusy,

                ["Error_FoundMultiplePlayer"] = Error_FoundMultiplePlayer,

                ["Note_HellFire_StartedAttack"] = Note_HellFire_StartedAttack,

                ["Error_HellFire_NoMapMark"] = Error_HellFire_NoMapMark,
                ["Error_HellFire_InvalidAmount"] = Error_HellFire_InvalidAmount,
                ["Error_HellFire_CantFindPlayer"] = Error_HellFire_CantFindPlayer,

                ["Error_RemoteMLRS_NoMissles"] = Error_RemoteMLRS_NoMissles,
                ["Error_RemoteMLRS_InvalidAmount"] = Error_RemoteMLRS_InvalidAmount,
                ["Error_RemoteMLRS_NoMapMark"] = Error_RemoteMLRS_NoMapMark,
                ["Error_RemoteMLRS_MRLSBroken"] = Error_RemoteMLRS_MRLSBroken,

                ["Error_NoPermission"] = Error_NoPermission,

                ["Syntax_MLRS"] = Syntax_MLRS,
                ["Syntax_HellFire"] = Syntax_HellFire,
                ["Syntax_RemoteMLRS"] = Syntax_RemoteMLRS
            }, this, "en");
        }

        #endregion

        #region Default Chat Messages

        private const string Note_MLRS_StartedAttack = "<color=#F75B00>MLRS started to attack the target.</color>";
        private const string Note_MLRS_FiringIn10Sec = "MLRS will start firing in 10 seconds!";
        private const string Note_MLRS_Fixed = "Main MLRS has been fixed.";
        private const string Note_All_MLRS_Fixed = "All found MLRS has been fixed.";
        private const string Note_MLRS_Enabled = "The using of MLRS has been </color><color=#48B11E>enabled</color> for all players.";
        private const string Note_MLRS_Disabled = "The using of MLRS has been </color><color=#F70000>disabled</color> for all players.";
        private const string Note_MLRS_Status_Enabled = "MLRS is </color><color=#48B11E>enabled</color> for all players on the server.";
        private const string Note_MLRS_Status_Disabled = "MLRS is </color><color=#F70000>disabled</color> for all players on the server.";

        private const string Error_MLRS_NoMLRS = "</color><color=#F70000>No MRLS was found on the server.</color>";
        private const string Error_MLRS_CantFindPlayer = "</color><color=#F70000>Couldn't find this player!</color>\n" +
            "Type <color=#E9AC3C>/mlrs</color> for help.";
        private const string Error_MLRS_NoMapMark = "</color><color=#F70000>No map mark set. Please create a map marker on the map with the name \"MLRSTARGET\" to attack.</color>\n" +
            "Type <color=#E9AC3C>/mlrs</color> for help.";
        private const string Error_MLRS_Disabled = "</color><color=#F70000>MLRS has been disabled on this server.</color>\n";
        private const string Error_MLRS_IsBusy = "</color><color=#F70000>MLRS is currently busy. Try it again later.</color>\n";

        private const string Note_HellFire_StartedAttack = "<color=#F75B00>Started hellfire attack to the target.</color>";

        private const string Error_HellFire_NoMapMark = "</color><color=#F70000>No map mark set. Please mark a point on the map to attack.</color>\n" +
            "Type <color=#E9AC3C>/hellfire</color> for help.";
        private const string Error_HellFire_InvalidAmount = "</color><color=#F70000>Invalid amount of rockets.</color>\n" +
            "Type <color=#E9AC3C>/hellfire</color> for help.";
        private const string Error_HellFire_CantFindPlayer = "</color><color=#F70000>Couldn't find this player!</color>\n" +
            "Type <color=#E9AC3C>/hellfire</color> for help.";

        private const string Error_FoundMultiplePlayer = "</color><color=#ffff00>Found multiple target players, you have to specify it more:</color> ";

        private const string Error_RemoteMLRS_NoMissles = "</color><color=#F70000>You don't have any MLRS missles in your inventory!</color>\n" +
            "Type <color=#E9AC3C>/remotemlrs</color> for help.";
        private const string Error_RemoteMLRS_InvalidAmount = "</color><color=#F70000>Invalid amount of rockets.</color>\n" +
            "Type <color=#E9AC3C>/remotemlrs</color> for help.";
        private const string Error_RemoteMLRS_NoMapMark = "</color><color=#F70000>No map mark set. Please mark a point on the map to attack.</color>\n" +
            "Type <color=#E9AC3C>/remotemlrs</color> for help.";
        private const string Error_RemoteMLRS_MRLSBroken = "</color><color=#F70000>MLRS is currently broken! Try it again later.</color>";

        private const string Error_NoPermission = "</color><color=#F70000>You don't have the permission to use this plugin command!</color>";

        private const string Syntax_MLRS = "<color=#4BF0FF>MLRS Commands:</color>\n" +
            "<color=#D97E29>/mlrsfire map</color> => MLRS will start to attack the marked spot on the map.\n" +
            "<color=#D97E29>/mlrsfire map</color> <color=#2990D9>(amount)</color> => MLRS will start to attack the marked spot on the map with the entered amount of rockets.\n" +
            "<color=#D97E29>/mlrsfire p {playername}</color> => MLRS will start to attack the target players spot.\n" +
            "<color=#D97E29>/mlrsfire p {playername}</color> <color=#2990D9>(amount)</color> => MLRS will start to attack the target players spot with the entered amount of rockets.\n" +
            "<color=#D97E29>/mlrsfireall map</color> => All found MLRS start to attack the marked spot on the map.\n" +
            "<color=#D97E29>/mlrsfireall map</color> <color=#2990D9>(amount)</color> => All found MLRS on this server start to attack the marked spot on the map with the entered amount of rockets.\n" +
            "<color=#D97E29>/mlrsfireall p {playername}</color> => All found MLRS will start to attack the target players spot.\n" +
            "<color=#D97E29>/mlrsfireall p {playername}</color> <color=#2990D9>(amount)</color> => All found MLRS will start to attack the target players spot with the entered amount of rockets.\n" +
            "<color=#D97E29>/mlrsfix</color> => Repears the active MLRS on the server.\n" +
            "<color=#D97E29>/mlrsfixall</color> => Repears all found MLRS on the server.\n" +
            "<color=#D97E29>/mlrs enable</color> => Allows all players to use MLRS like in vanilla rust.\n" +
            "<color=#D97E29>/mlrs disable</color> => Prevent all players to mount/use the MLRS on the server (can be bypassed with perm).\n" +
            "<color=#D97E29>/mlrs status</color> => Shows you if MLRS is enabled or disabled on this server.";

        private const string Syntax_HellFire = "<color=#4BF0FF>HellFire Commands:</color>\n" +
            "<color=#D97E29>/hellfire p {playername}</color> => MLRS rocket spawns and start to attack the target players spot.\n" +
            "<color=#D97E29>/hellfire p {playername}</color> <color=#2990D9>(amount)</color> => x MLRS rockets spawning and start to attack the target players spot.\n" +
            "<color=#D97E29>/hellfire map</color> => MLRS Rocket spawns and start to attack the marked spot on the map.\n" +
            "<color=#D97E29>/hellfire map</color> <color=#2990D9>(amount)</color> => x MLRS Rocket spawning and start to attack the marked spot on the map.";

        private const string Syntax_RemoteMLRS = "<color=#4BF0FF>RemoteMLRS Commands:</color>\n" +
            "<color=#FFED47>Info:</color> You need MLRS Rockets to call a remote MLRS attack.\n" +
            "<color=#D97E29>/remotemlrs p {playername}</color> => Calls MLRS attack to target players spot. Takes up to 12 MLRS rockets. (need extra perms)\n" +
            "<color=#D97E29>/remotemlrs p {playername}</color> <color=#2990D9>(amount)</color> => MLRS will start to attack the target players spot. Takes x (max 12) MLRS rockets (need extra perms)\n" +
            "<color=#D97E29>/remotemlrs map</color> => MLRS will start to attack the marked spot on the map. Takes up to 12 MLRS rockets.\n" +
            "<color=#D97E29>/remotemlrs map</color> <color=#2990D9>(amount)</color> => MLRS will start to attack the marked spot on the map. Takes x (max 12) MLRS rockets.\n";

        #endregion

        #region Used Hooks

        object CanMountEntity(BasePlayer player, BaseMountable entity)
        {
            if (!(entity is MLRS) || mConfig.AllowUsingOfMLRSForAllPlayers)
                return null;

            if (!(player.IPlayer.HasPermission(MLRSHellfire_Admin)
                || player.IPlayer.HasPermission(MLRSHellfire_MLRSMountBypass)))
            {
                ChatReply(player, lang.GetMessage("Error_MLRS_Disabled", this, player.UserIDString));
                return false;
            }

            if (mainMLRS.isBusy)
            {
                ChatReply(player, lang.GetMessage("Error_MLRS_IsBusy", this, player.UserIDString));
                return false;
            }

            return null;
        }

        #endregion

        #region Chat

        private void ChatReply(BasePlayer player, string message) =>
            Player.Reply(player, mConfig.PluginPrefix + message, mConfig.PluginPicture);

        #endregion

        #region Fields/Constants

        /// <summary>
        /// Path to MLRS missle prefab.
        /// </summary>
        private const string MLRSRocketPrefab = "assets/content/vehicles/mlrs/rocket_mlrs.prefab";

        /// <summary>
        /// Saves all active MLRS on the server.
        /// </summary>
        private static List<CustomMLRS> allActiveMRLSOnServer = GetAllActiveMLRS();

        /// <summary>
        /// This is used to save the damage of MLRS rockets.
        /// </summary>
        private static TimedExplosive mlrsRocketTimedExplosive;

        /// <summary>
        /// This is your main MLRS on your server.
        /// </summary>
        private static CustomMLRS mainMLRS = GetActiveMainMLRS();

        private const string MapNoteTargetName = "MLRSTARGET";

        #endregion

        #region Plugin Logic

        #region MLRS

        [ChatCommand("mlrs")]
        private void Mlrs(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(MLRSHellfire_Admin))
            {
                ChatReply(player, lang.GetMessage("Error_NoPermission", this, player.UserIDString));
                return;
            }

            if (args.Length != 1)
            {
                ChatReply(player, lang.GetMessage("Syntax_MLRS", this, player.UserIDString));
                return;
            }

            switch (args[0].ToLower())
            {
                case "enable":
                    {
                        mConfig.AllowUsingOfMLRSForAllPlayers = true;
                        SaveConfig();
                        ChatReply(player, lang.GetMessage("Note_MLRS_Enabled", this, player.UserIDString));
                        break;
                    }
                case "disable":
                    {
                        mConfig.AllowUsingOfMLRSForAllPlayers = false;
                        SaveConfig();
                        ChatReply(player, lang.GetMessage("Note_MLRS_Disabled", this, player.UserIDString));
                        break;
                    }
                case "status":
                    {
                        if (mConfig.AllowUsingOfMLRSForAllPlayers)
                            ChatReply(player, lang.GetMessage("Note_MLRS_Status_Enabled", this, player.UserIDString));
                        else
                            ChatReply(player, lang.GetMessage("Note_MLRS_Status_Disabled", this, player.UserIDString));
                        break;
                    }
                default:
                    {
                        ChatReply(player, lang.GetMessage("Syntax_MLRS", this, player.UserIDString));
                        return;
                    }
            }
        }

        [ChatCommand("mlrsfire")]
        private void MlrsFire(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(MLRSHellfire_Admin))
            {
                ChatReply(player, lang.GetMessage("Error_NoPermission", this, player.UserIDString));
                return;
            }

            var mlrs = GetActiveMainMLRS();

            if (mlrs == null)
            {
                ChatReply(player, lang.GetMessage("Error_MLRS_NoMLRS", this, player.UserIDString));
                return;
            }

            if (args.Length < 1 || args.Length > 3)
            {
                ChatReply(player, lang.GetMessage("Syntax_MLRS", this, player.UserIDString));
                return;
            }

            if (mainMLRS.isBusy)
            {
                ChatReply(player, lang.GetMessage("Error_MLRS_IsBusy", this, player.UserIDString));
                return;
            }

            Vector3 targetPosition;
            int rocketsToSpawn = 0;

            switch (args[0].ToLower())
            {
                case "p":
                    {
                        if (args.Length < 2 || args.Length > 3)
                        {
                            ChatReply(player, lang.GetMessage("Syntax_MLRS", this, player.UserIDString));
                            return;
                        }

                        var targetPlayer = SearchAndGetTargetPlayer(args[1], player);

                        if (targetPlayer != null)
                        {
                            targetPosition = targetPlayer.ServerPosition;
                        }
                        else
                        {
                            return;
                        }

                        if (args.Length == 3)
                        {
                            int tmpAmount;

                            if (IsValidMLRSMissleAmount(args[2], out tmpAmount))
                                rocketsToSpawn = tmpAmount;
                            else
                            {
                                ChatReply(player, lang.GetMessage("Error_HellFire_InvalidAmount", this, player.UserIDString));
                                return;
                            }
                        }

                        break;
                    }
                case "map":
                    {
                        if (args.Length < 1 || args.Length > 2)
                        {
                            ChatReply(player, lang.GetMessage("Syntax_MLRS", this, player.UserIDString));
                            return;
                        }

                        var worldPosition = GetTargetMapNotePosition(player);

                        if (worldPosition == Vector3.zero)
                        {
                            ChatReply(player, lang.GetMessage("Error_MLRS_NoMapMark", this, player.UserIDString));
                            return;
                        }

                        targetPosition = worldPosition;

                        if (args.Length == 2)
                        {
                            int tmpAmount;

                            if (IsValidMLRSMissleAmount(args[1], out tmpAmount))
                                rocketsToSpawn = tmpAmount;
                            else
                            {
                                ChatReply(player, lang.GetMessage("Error_HellFire_InvalidAmount", this, player.UserIDString));
                                return;
                            }
                        }

                        break;
                    }
                default:
                    {
                        ChatReply(player, lang.GetMessage("Syntax_MLRS", this, player.UserIDString));
                        return;
                    }
            }

            mainMLRS.isBusy = true;

            if (rocketsToSpawn == 0)
                rocketsToSpawn = 12;

            mlrs.instance.SetRepaired();

            var rocketContainer = mlrs.instance.GetRocketContainer();
            var mlrsRocket = rocketContainer.allowedItem;

            if (rocketsToSpawn > 12)
            {
                rocketContainer.inventory.AddItem(mlrsRocket, rocketsToSpawn);
                rocketContainer.inventory.AddItem(mlrsRocket, rocketsToSpawn - 12);
            }
            else
                rocketContainer.inventory.AddItem(mlrsRocket, rocketsToSpawn);

            mlrs.instance.RocketAmmoCount = rocketsToSpawn;
            mlrs.instance.nextRocketIndex = rocketsToSpawn - 1;

            mlrs.instance.SetUserTargetHitPos(targetPosition);

            ChatReply(player, lang.GetMessage("Note_MLRS_FiringIn10Sec", this, player.UserIDString));

            timer.Once(10f, () =>
            {
                mlrs.instance.SetFlag(BaseEntity.Flags.Reserved8, b: true);
                mlrs.instance.nextRocketIndex = Mathf.Min(mlrs.instance.RocketAmmoCount - 1, mlrs.instance.rocketTubes.Length - 1);
                mlrs.instance.radiusModIndex = 0;
                mlrs.instance.InvokeRepeating(mlrs.instance.FireNextRocket, 0f, mConfig.MLRSFireInterval);

                ChatReply(player, lang.GetMessage("Note_MLRS_StartedAttack", this, player.UserIDString));
            });

            timer.Once((rocketsToSpawn * mConfig.MLRSFireInterval) + 11f, () =>
            {
                mainMLRS.isBusy = false;
            });
        }

        [ChatCommand("mlrsfireall")]
        private void MlrsFireAll(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(MLRSHellfire_Admin))
            {
                ChatReply(player, lang.GetMessage("Error_NoPermission", this, player.UserIDString));
                return;
            }

            if (args.Length < 1 || args.Length > 3)
            {
                ChatReply(player, lang.GetMessage("Syntax_MLRS", this, player.UserIDString));
                return;
            }

            var allMLRS = GetAllActiveMLRS();

            if (allMLRS.Count == 0)
            {
                ChatReply(player, lang.GetMessage("Error_MLRS_NoMLRS", this, player.UserIDString));
                return;
            }

            Vector3 targetPosition;
            int rocketsToSpawn = 0;

            switch (args[0].ToLower())
            {
                case "p":
                    {
                        if (args.Length < 2 || args.Length > 3)
                        {
                            ChatReply(player, lang.GetMessage("Syntax_MLRS", this, player.UserIDString));
                            return;
                        }

                        var targetPlayer = SearchAndGetTargetPlayer(args[1], player);

                        if (targetPlayer != null)
                        {
                            targetPosition = targetPlayer.ServerPosition;
                        }
                        else
                        {
                            return;
                        }

                        if (args.Length == 3)
                        {
                            int tmpAmount;

                            if (IsValidMLRSMissleAmount(args[2], out tmpAmount))
                                rocketsToSpawn = tmpAmount;
                            else
                            {
                                ChatReply(player, lang.GetMessage("Error_HellFire_InvalidAmount", this, player.UserIDString));
                                return;
                            }
                        }

                        break;
                    }
                case "map":
                    {
                        if (args.Length < 1 || args.Length > 2)
                        {
                            ChatReply(player, lang.GetMessage("Syntax_MLRS", this, player.UserIDString));
                            return;
                        }

                        var worldPosition = GetTargetMapNotePosition(player);

                        if (worldPosition == Vector3.zero)
                        {
                            ChatReply(player, lang.GetMessage("Error_MLRS_NoMapMark", this, player.UserIDString));
                            return;
                        }

                        targetPosition = worldPosition;

                        if (args.Length == 2)
                        {
                            int tmpAmount;

                            if (IsValidMLRSMissleAmount(args[1], out tmpAmount))
                                rocketsToSpawn = tmpAmount;
                            else
                            {
                                ChatReply(player, lang.GetMessage("Error_HellFire_InvalidAmount", this, player.UserIDString));
                                return;
                            }
                        }

                        break;
                    }
                default:
                    {
                        ChatReply(player, lang.GetMessage("Syntax_MLRS", this, player.UserIDString));
                        return;
                    }
            }

            if (rocketsToSpawn == 0)
                rocketsToSpawn = 12;

            int busyMLRSCounter = 0;
            int removedMLRSCounter = 0;

            foreach (var mlrs in allMLRS)
            {
                if (mlrs.instance == null)
                {
                    removedMLRSCounter++;
                    continue;
                }

                bool isMainMLRS = mlrs.mlrsId == mainMLRS.mlrsId;

                if (isMainMLRS && mainMLRS.isBusy)
                {
                    busyMLRSCounter++;
                    continue;
                }

                if (mlrs.isBusy)
                {
                    busyMLRSCounter++;
                    continue;
                }

                if (isMainMLRS)
                    mainMLRS.isBusy = true;

                mlrs.isBusy = true;

                mlrs.instance.SetRepaired();

                var rocketContainer = mlrs.instance.GetRocketContainer();
                var mlrsRocket = rocketContainer.allowedItem;

                if (rocketsToSpawn > 12)
                {
                    rocketContainer.inventory.AddItem(mlrsRocket, rocketsToSpawn);
                    rocketContainer.inventory.AddItem(mlrsRocket, rocketsToSpawn - 12);
                }
                else
                    rocketContainer.inventory.AddItem(mlrsRocket, rocketsToSpawn);

                mlrs.instance.RocketAmmoCount = rocketsToSpawn;
                mlrs.instance.nextRocketIndex = rocketsToSpawn - 1;

                mlrs.instance.SetUserTargetHitPos(targetPosition);

                timer.Once(10f, () =>
                {
                    mlrs.instance.SetFlag(BaseEntity.Flags.Reserved8, b: true);
                    mlrs.instance.nextRocketIndex = Mathf.Min(mlrs.instance.RocketAmmoCount - 1, mlrs.instance.rocketTubes.Length - 1);
                    mlrs.instance.radiusModIndex = 0;
                    mlrs.instance.InvokeRepeating(mlrs.instance.FireNextRocket, 0f, mConfig.MLRSFireInterval);
                });

                timer.Once((rocketsToSpawn * mConfig.MLRSFireInterval) + 11f, () =>
                {
                    if (isMainMLRS)
                        mainMLRS.isBusy = false;

                    mlrs.isBusy = false;
                });
            }

            if (busyMLRSCounter + removedMLRSCounter != allMLRS.Count)
            {
                ChatReply(player, lang.GetMessage("Note_MLRS_FiringIn10Sec", this, player.UserIDString));

                timer.Once(10f, () =>
                {
                    ChatReply(player, lang.GetMessage("Note_MLRS_StartedAttack", this, player.UserIDString));
                });
            }

            if (busyMLRSCounter != 0) // Neue Note
                ChatReply(player, $"<color=#F75B00>{busyMLRSCounter}</color> of <color=#2990D9>{allMLRS.Count}</color> MLRS are currently busy and will be skipped for this operation.");

            if (removedMLRSCounter != 0) // Neute Note
                ChatReply(player, $"<color=#F75B00>{removedMLRSCounter}</color> of <color=#2990D9>{allMLRS.Count}</color> MLRS aren't there anymore.");
        }

        [ChatCommand("mlrsfix")]
        private void MLRSFix(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(MLRSHellfire_Admin))
            {
                ChatReply(player, lang.GetMessage("Error_NoPermission", this, player.UserIDString));
                return;
            }

            var mlrs = GetActiveMainMLRS();

            if (mlrs == null)
            {
                ChatReply(player, lang.GetMessage("Error_MLRS_NoMLRS", this, player.UserIDString));
                return;
            }

            mlrs.instance.SetRepaired();

            ChatReply(player, lang.GetMessage("Note_MLRS_Fixed", this, player.UserIDString));
        }

        [ChatCommand("mlrsfixall")]
        private void MLRSFixAll(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(MLRSHellfire_Admin))
            {
                ChatReply(player, lang.GetMessage("Error_NoPermission", this, player.UserIDString));
                return;
            }

            var allMlrs = GetAllActiveMLRS();

            if (allMlrs == null)
            {
                ChatReply(player, lang.GetMessage("Error_MLRS_NoMLRS", this, player.UserIDString));
                return;
            }

            foreach (var mlrs in allMlrs)
                mlrs.instance.SetRepaired();

            ChatReply(player, lang.GetMessage("Note_All_MLRS_Fixed", this, player.UserIDString));
        }

        #endregion

        #region Remote MLRS

        [ChatCommand("remotemlrs")]
        private void RemoteMlrs(BasePlayer player, string command, string[] args)
        {
            if (!(player.IPlayer.HasPermission(MLRSHellfire_Admin)
                || player.IPlayer.HasPermission(MLRSHellfire_UseRemoteMLRS)))
            {
                ChatReply(player, lang.GetMessage("Error_NoPermission", this, player.UserIDString));
                return;
            }

            var mlrs = GetActiveMainMLRS();

            if (mlrs == null)
            {
                ChatReply(player, lang.GetMessage("Error_MLRS_NoMLRS", this, player.UserIDString));
                return;
            }

            if (mainMLRS.isBusy)
            {
                ChatReply(player, lang.GetMessage("Error_MLRS_IsBusy", this, player.UserIDString));
                return;
            }

            if (mlrs.instance.IsBroken())
            {
                if (!(player.IPlayer.HasPermission(MLRSHellfire_Admin)
                || player.IPlayer.HasPermission(MLRSHellfire_RemoteMLRS_MLRSBrokenBypass)))
                {
                    ChatReply(player, lang.GetMessage("Error_RemoteMLRS_MRLSBroken", this, player.UserIDString));
                    return;
                }
            }

            if (args.Length < 1 || args.Length > 3)
            {
                ChatReply(player, lang.GetMessage("Syntax_RemoteMLRS", this, player.UserIDString));
                return;
            }

            List<Item> allItems = Pool.Get<List<Item>>();
            List<Item> mlrsMissleContainer = Pool.Get<List<Item>>();
            player.inventory.GetAllItems(allItems);
            mlrsMissleContainer = allItems.Where(item => item.HasAmmo(Rust.AmmoTypes.MLRS_ROCKET)).ToList();

            try
            {
	            if (mlrsMissleContainer.Count == 0)
	            {
	                ChatReply(player, lang.GetMessage("Error_RemoteMLRS_NoMissles", this, player.UserIDString));
	                return;
	            }

	            Vector3 targetPosition;
	            int rocketsToSpawn = 0;
                switch (args[0].ToLower())
                {
                    case "p":
                        {
                            if (!(player.IPlayer.HasPermission(MLRSHellfire_Admin)
                                || player.IPlayer.HasPermission(MLRSHellfire_RemoteMLRS_AllowTargetingPlayer)))
                            {
                                ChatReply(player, lang.GetMessage("Error_NoPermission", this, player.UserIDString));
                                return;
                            }

                            if (args.Length < 2 || args.Length > 3)
                            {
                                ChatReply(player, lang.GetMessage("Syntax_RemoteMLRS", this, player.UserIDString));
                                return;
                            }

                            var targetPlayer = SearchAndGetTargetPlayer(args[1], player);

                            if (targetPlayer != null)
                            {
                                targetPosition = targetPlayer.ServerPosition;
                            }
                            else
                            {
                                return;
                            }

                            if (args.Length == 3)
                            {
                                int tmpAmount;

                                if (IsValidMLRSMissleAmount(args[2], out tmpAmount))
                                    rocketsToSpawn = tmpAmount;
                                else
                                {
                                    ChatReply(player, lang.GetMessage("Error_HellFire_InvalidAmount", this, player.UserIDString));
                                    return;
                                }
                            }

                            break;
                        }
                    case "map":
                        {
                            if (args.Length < 1 || args.Length > 2)
                            {
                                ChatReply(player, lang.GetMessage("Syntax_RemoteMLRS", this, player.UserIDString));
                                return;
                            }

                            var worldPosition = GetTargetMapNotePosition(player);

                            if (worldPosition == Vector3.zero)
                            {
                                ChatReply(player, lang.GetMessage("Error_MLRS_NoMapMark", this, player.UserIDString));
                                return;
                            }

                            targetPosition = worldPosition;

                            if (args.Length == 2)
                            {
                                int tmpAmount;

                                if (IsValidMLRSMissleAmount(args[1], out tmpAmount))
                                    rocketsToSpawn = tmpAmount;
                                else
                                {
                                    ChatReply(player, lang.GetMessage("Error_HellFire_InvalidAmount", this, player.UserIDString));
                                    return;
                                }
                            }

                            break;
                        }
                    default:
                        {
                            ChatReply(player, lang.GetMessage("Syntax_RemoteMLRS", this, player.UserIDString));
                            return;
                        }
                }

                mainMLRS.isBusy = true;

                if (rocketsToSpawn == 0)
                    rocketsToSpawn = 12;

                int missleAmount = 0;

                foreach (var itemRocketContainer in mlrsMissleContainer)
                {
                    if (itemRocketContainer.amount <= 0)
                        continue;

                    if (itemRocketContainer.amount >= rocketsToSpawn)
                    {
                        missleAmount = rocketsToSpawn;

                        if ((itemRocketContainer.amount - rocketsToSpawn) > 0)
                            itemRocketContainer.amount -= rocketsToSpawn;
                        else
                            itemRocketContainer.DoRemove();

                        break;
                    }
                    else
                    {
                        if ((missleAmount + itemRocketContainer.amount) > rocketsToSpawn)
                        {
                            while (missleAmount != rocketsToSpawn)
                            {
                                missleAmount++;
                                itemRocketContainer.amount--;
                            }

                            break;
                        }
                        else
                        {
                            missleAmount += itemRocketContainer.amount;
                            itemRocketContainer.DoRemove();
                        }
                    }
                }

                mlrs.instance.SetRepaired();

                var rocketContainer = mlrs.instance.GetRocketContainer();
                var mlrsRocket = rocketContainer.allowedItem;

                rocketContainer.inventory.AddItem(mlrsRocket, missleAmount);

                mlrs.instance.RocketAmmoCount = missleAmount;
                mlrs.instance.nextRocketIndex = missleAmount - 1;

                mlrs.instance.rocketOwnerRef.Set(player);

                mlrs.instance.SetUserTargetHitPos(targetPosition);

                ChatReply(player, lang.GetMessage("Note_MLRS_FiringIn10Sec", this, player.UserIDString));

                timer.Once(10f, () =>
                {
                    mlrs.instance.SetFlag(BaseEntity.Flags.Reserved8, b: true);
                    mlrs.instance.nextRocketIndex = Mathf.Min(mlrs.instance.RocketAmmoCount - 1, mlrs.instance.rocketTubes.Length - 1);
                    mlrs.instance.radiusModIndex = 0;
                    mlrs.instance.InvokeRepeating(mlrs.instance.FireNextRocket, 0f, mConfig.RemoteMLRSFireInterval);

                    ChatReply(player, lang.GetMessage("Note_MLRS_StartedAttack", this, player.UserIDString));
                });

                timer.Once((missleAmount * mConfig.RemoteMLRSFireInterval) + 11f, () =>
                {
                    mainMLRS.isBusy = false;
                });
            }
            finally
            {
                Pool.Free(ref allItems);
                Pool.Free(ref mlrsMissleContainer);
            }
        }

        #endregion

        #region Hellfire

        [ChatCommand("hellfire")]
        private void Hellfire(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(MLRSHellfire_Admin))
            {
                ChatReply(player, lang.GetMessage("Error_NoPermission", this, player.UserIDString));
                return;
            }

            if (args.Length < 1 || args.Length > 3)
            {
                ChatReply(player, lang.GetMessage("Syntax_HellFire", this, player.UserIDString));
                return;
            }

            Vector3 targetPosition;
            int rocketsToSpawn = 1;

            switch (args[0].ToLower())
            {
                case "p":
                    {
                        if (args.Length < 2 || args.Length > 3)
                        {
                            ChatReply(player, lang.GetMessage("Syntax_HellFire", this, player.UserIDString));
                            return;
                        }

                        var targetPlayer = SearchAndGetTargetPlayer(args[1], player);

                        if (targetPlayer != null)
                        {
                            targetPosition = targetPlayer.ServerPosition;
                        }
                        else
                        {
                            return;
                        }

                        if (args.Length == 3)
                        {
                            int tmpAmount;

                            if (IsValidHellfireMissleAmount(args[2], out tmpAmount))
                                rocketsToSpawn = tmpAmount;
                            else
                            {
                                ChatReply(player, lang.GetMessage("Error_HellFire_InvalidAmount", this, player.UserIDString));
                                return;
                            }
                        }

                        break;
                    }
                case "map":
                    {
                        if (args.Length < 1 || args.Length > 2)
                        {
                            ChatReply(player, lang.GetMessage("Syntax_HellFire", this, player.UserIDString));
                            return;
                        }

                        var worldPosition = GetTargetMapNotePosition(player);

                        if (worldPosition == Vector3.zero)
                        {
                            ChatReply(player, lang.GetMessage("Error_MLRS_NoMapMark", this, player.UserIDString));
                            return;
                        }

                        targetPosition = worldPosition;

                        if (args.Length == 2)
                        {
                            int tmpAmount;

                            if (IsValidHellfireMissleAmount(args[1], out tmpAmount))
                                rocketsToSpawn = tmpAmount;
                            else
                            {
                                ChatReply(player, lang.GetMessage("Error_HellFire_InvalidAmount", this, player.UserIDString));
                                return;
                            }
                        }

                        break;
                    }
                default:
                    {
                        ChatReply(player, lang.GetMessage("Syntax_HellFire", this, player.UserIDString));
                        return;
                    }
            }

            if (rocketsToSpawn != 1)
            {
                timer.Repeat(mConfig.HellfireFireInterval, rocketsToSpawn, () =>
                {
                    ExecuteFireOperation(player, targetPosition);
                });
            }
            else
                ExecuteFireOperation(player, targetPosition);

            ChatReply(player, lang.GetMessage("Note_HellFire_StartedAttack", this, player.UserIDString));
        }

        #endregion

        #region Missle Methods

        private void ExecuteFireOperation(BasePlayer player, Vector3 targetPosition)
        {
            float baseGravity;
            Vector3 aimToTarget = GetAimToTarget(player.ServerPosition, targetPosition, out baseGravity);

            var startPoint = player.ServerPosition;
            startPoint.y += 15f;

            ServerProjectile projectile;

            if (CreateAndSpawnRocket(startPoint, aimToTarget, out projectile) == false)
                return;

            projectile.gravityModifier = baseGravity / (0f - Physics.gravity.y);
        }

        private Vector3 GetAimToTarget(Vector3 startPosition, Vector3 targetPos, out float baseGravity)
        {
            Vector3 vector = targetPos - startPosition;

            float num = 90f;
            float num2 = vector.Magnitude2D();
            float y = vector.y;
            float num5 = 40f;

            baseGravity = ProjectileDistToGravity(Mathf.Max(num2, 50f), y, num5, num);

            vector.Normalize();
            vector.y = 0f;

            Vector3 axis = Vector3.Cross(vector, Vector3.up);

            vector = Quaternion.AngleAxis(num5, axis) * vector;

            return vector;
        }

        private bool CreateAndSpawnRocket(Vector3 firingPos, Vector3 firingDir,
            out ServerProjectile mlrsRocketProjectile)
        {
            RaycastHit hitInfo;

            float launchOffset = 0f;

            if (Physics.Raycast(firingPos, firingDir, out hitInfo, launchOffset, 1236478737))
                launchOffset = hitInfo.distance - 0.1f;

            var mlrsRocketEntity = GameManager.server.CreateEntity(MLRSRocketPrefab, firingPos + firingDir * launchOffset);

            if (mlrsRocketEntity == null)
            {
                mlrsRocketProjectile = null;
                return false;
            }

            mlrsRocketProjectile = mlrsRocketEntity.GetComponent<ServerProjectile>();

            var velocityVector = mlrsRocketProjectile.initialVelocity + firingDir * mlrsRocketProjectile.speed;

            mlrsRocketProjectile.InitializeVelocity(velocityVector);

            var mlrsRocket = mlrsRocketEntity as MLRSRocket;

            if (mlrsRocket == null)
                return false;

            ApplyMLRSRocketModfications(ref mlrsRocket);

            mlrsRocket.Spawn();

            return true;
        }

        #endregion

        #region Helper Methods

        private float ProjectileDistToGravity(float x, float y, float θ, float v)
        {
            float num = θ * ((float)Math.PI / 180f);
            float num2 = (v * v * x * Mathf.Sin(2f * num) - 2f * v * v * y * Mathf.Cos(num) * Mathf.Cos(num)) / (x * x);
            if (float.IsNaN(num2) || num2 < 0.01f)
            {
                num2 = 0f - Physics.gravity.y;
            }

            return num2;
        }

        private void ApplyMLRSRocketModfications(ref MLRSRocket mlrsRocket)
        {
            mlrsRocket.explosionRadius *= mConfig.HellfireRocketExplosiveRadiusModifier;
            mlrsRocket.damageTypes = GetDamageOfHellfireRocket(mlrsRocket).damageTypes;
        }

        private BasePlayer SearchAndGetTargetPlayer(string targetPlayername, BasePlayer callingPlayer)
        {
            var targetPlayer = BasePlayer.activePlayerList.Where(x => x.displayName.ToLower().Contains(targetPlayername.ToLower())).ToArray();

            var possibleAmountOfTargetPlayer = targetPlayer.Count();

            if (possibleAmountOfTargetPlayer == 1)
            {
                return targetPlayer[0];
            }

            if (!targetPlayer.Any())
            {
                ChatReply(callingPlayer, lang.GetMessage("Error_MLRS_CantFindPlayer", this, callingPlayer.UserIDString));
                return null;
            }

            if (possibleAmountOfTargetPlayer > 1)
            {
                var possibleTargets = string.Empty;

                var allTargets = targetPlayer.Select(x => x.displayName).ToArray();

                for (int i = 0; i < allTargets.Length; i++)
                {
                    if (i + 1 != allTargets.Length)
                    {
                        possibleTargets += $"</color><color=#005fff>{allTargets[i]}</color>, ";
                        continue;
                    }
                    else
                    {
                        possibleTargets += $"</color><color=#005fff>{allTargets[i]}</color>";
                    }
                }

                ChatReply(callingPlayer, lang.GetMessage("Error_FoundMultiplePlayer", this, callingPlayer.UserIDString) + possibleTargets);

                return null;
            }

            return null;
        }

        private bool IsValidHellfireMissleAmount(string amount, out int rocketsToSpawn)
        {
            uint tmpAmount;
            rocketsToSpawn = 0;

            if (uint.TryParse(amount, out tmpAmount))
            {
                if (tmpAmount > mConfig.HellFireMaxRocketAmountToSpawn)
                    rocketsToSpawn = (int)mConfig.HellFireMaxRocketAmountToSpawn;
                else
                    rocketsToSpawn = (int)tmpAmount;

                return true;
            }

            return false;
        }

        private bool IsValidMLRSMissleAmount(string amount, out int rocketsToSpawn)
        {
            uint tmpAmount;
            rocketsToSpawn = 0;

            if (uint.TryParse(amount, out tmpAmount))
            {
                if (tmpAmount > 12)
                    rocketsToSpawn = 12;
                else
                    rocketsToSpawn = (int)tmpAmount;

                return true;
            }

            return false;
        }

        private static CustomMLRS GetActiveMainMLRS()
        {
            if (mainMLRS != null)
                return mainMLRS;

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is MLRS)
                {
                    mainMLRS = new CustomMLRS((MLRS)entity);
                    return mainMLRS;
                }
            }

            return null;
        }

        private static List<CustomMLRS> GetAllActiveMLRS()
        {
            var allMLRS = _GetAllActiveMLRS();

            if (allActiveMRLSOnServer != null)
                if (allMLRS.Count == allActiveMRLSOnServer.Count) // Bei löschen und neuspawnen eines MLRS, kann das neue nicht angesteuert werden, da dies nicht bemerkt wurde.
                    return allActiveMRLSOnServer; // Einfach HashCode zusätzlich abgleichen und Yalla

            allActiveMRLSOnServer = allMLRS;
            return allActiveMRLSOnServer;
        }

        private static List<CustomMLRS> _GetAllActiveMLRS()
        {
            var allFoundMLRS = new List<CustomMLRS>();

            foreach (var entity in BaseNetworkable.serverEntities)
                if (entity is MLRS)
                    allFoundMLRS.Add(new CustomMLRS((MLRS)entity));

            return allFoundMLRS;
        }

        private TimedExplosive GetDamageOfHellfireRocket(MLRSRocket mlrsRocket)
        {
            if (mlrsRocketTimedExplosive != null)
                return mlrsRocketTimedExplosive;

            foreach (var damage in mlrsRocket.damageTypes)
                damage.amount *= mConfig.HellfireRocketDamageModifier;

            mlrsRocketTimedExplosive = new TimedExplosive
            {
                damageTypes = mlrsRocket.damageTypes
            };

            return mlrsRocketTimedExplosive;
        }

        private Vector3 GetTargetMapNotePosition(BasePlayer player)
        {
            var target = player.State.pointsOfInterest.Where(p => p.label == MapNoteTargetName).ToList();

            if (!target.Any())
            {
                return Vector3.zero;
            }

            return target.First().worldPosition;
        }

        #endregion

        #region Custom MLRS

        class CustomMLRS
        {
            public CustomMLRS(MLRS mlrs)
            {
                instance = mlrs;
                mlrsId = mlrs.GetHashCode();
            }

            public readonly MLRS instance;

            public bool isBusy = false;

            public readonly int mlrsId;
        }

        #endregion

        #endregion
    }
}