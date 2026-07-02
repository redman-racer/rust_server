using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Rocket Fire", "birthdates", "1.0.4")]
    [Description("Ability to fire x amount of rockets at once")]
    public class RocketFire : RustPlugin
    {
        #region Variables
        private const string Perm = "rocketfire.use";
        #endregion

        #region Hooks
        private void Init()
        {
            permission.RegisterPermission(Perm, this);
            LoadConfig();
        }

        [ConsoleCommand("fr")]
        private void FireRocketsConsoleCMD(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                return;
            }

            var args = arg.Args?.Select(x => x.ToString()).ToArray() ?? new string[0];
            FRCommand(player, args);
        }

        [ChatCommand("fr")]
        private void FireRocketsCMD(BasePlayer player, string command, string[] args)
            => FRCommand(player, args);

        private void FRCommand(BasePlayer player, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, Perm) && !player.IsAdmin)
            {
                SendReply(player, lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }

            if (args.Length < 1)
            {
                SendReply(player, lang.GetMessage("InvalidArgs", this, player.UserIDString));
                return;
            }

            if (!int.TryParse(args[0], out int amount))
            {
                SendReply(player, lang.GetMessage("InvalidNumber", this, player.UserIDString));
                return;
            }

            if (amount > _config.maxRockets)
            {
                SendReply(player, string.Format(lang.GetMessage("TooManyRockets", this, player.UserIDString), _config.maxRockets));
                return;
            }

            if (!StringPool.toString.ContainsValue($"assets/prefabs/ammo/rocket/{_config.rocketType}.prefab"))
            {
                SendReply(player, lang.GetMessage("InvalidPrefab", this, player.UserIDString));
                return;
            }

            var pos = player.eyes.position;
            var forward = player.eyes.HeadForward();
            var rot = player.transform.rotation;
            var aim = player.serverInput.current.aimAngles;
            var staticPos = _config.staticRockets ? pos + forward : default(Vector3);

            timer.Repeat(_config.delay, amount, delegate
            {
                var rocket = GameManager.server.CreateEntity(
                    $"assets/prefabs/ammo/rocket/{_config.rocketType}.prefab",
                    _config.staticRockets ? staticPos : player.eyes.position + player.eyes.HeadForward(),
                    _config.staticRockets ? rot : player.transform.rotation
                );

                if (rocket == null) return;

                var proj = rocket.GetComponent<ServerProjectile>();
                if (proj == null) return;

                proj.InitializeVelocity(
                    Quaternion.Euler(_config.staticRockets ? aim : player.serverInput.current.aimAngles)
                    * rocket.transform.forward
                    * _config.velocity
                );

                rocket.Spawn();
            });

            SendReply(player, string.Format(lang.GetMessage("RocketsFired", this, player.UserIDString), amount));
        }
        #endregion

        #region Configuration & Language

        public ConfigFile _config;

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"TooManyRockets", "You have attempted to fire too many rockets, you can only fire {0} at once!"},
                {"RocketsFired", "You have launched {0} rockets"},
                {"NoPermission", "You have no permission!"},
                {"InvalidArgs", "/fr <amount of rockets to fire>"},
                {"InvalidNumber", "That is not a valid number!"},
                {"InvalidPrefab", "Please fix the rocket type in the configuration file, that is not a valid rocket type!"},
                {"PlayerOnlyCommand", "This command is only accessible by players."}
            }, this);
        }

        public class ConfigFile
        {
            [JsonProperty("Max amount of rockets to fire at once")]
            public int maxRockets;

            [JsonProperty("Delay in between multiple rocket shots")]
            public float delay;

            [JsonProperty("Rockets fire at same position")]
            public bool staticRockets;

            [JsonProperty("The rocket velocity")]
            public float velocity;

            [JsonProperty("The rocket type")]
            public string rocketType;

            public static ConfigFile DefaultConfig()
            {
                return new ConfigFile
                {
                    maxRockets = 2,
                    delay = 0.2f,
                    staticRockets = false,
                    velocity = 22,
                    rocketType = "rocket_basic"
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<ConfigFile>();
            if (_config == null)
                LoadDefaultConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = ConfigFile.DefaultConfig();
            PrintWarning("Default configuration has been loaded.");
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config);
        }

        #endregion
    }
}