using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using Oxide.Core.Configuration;

namespace Oxide.Plugins
{
	[Info("Server Rewards Wipe", "ZEODE", "2.0.2")]
	[Description("Reset Server Rewards player RP on wipe or command.")]
	public class ServerRewardsWipe : CovalencePlugin
	{
		[PluginReference]
		private Plugin ServerRewards;

		private const string permAdmin = "serverrewardswipe.admin";

		private string _pendingSave;

		private DynamicConfigFile _stateFile;

		private class WipeState
		{
			public string PendingSave;
		}
		
		#region Hooks

		private void Init()
		{
			permission.RegisterPermission(permAdmin, this);

			_stateFile = Interface.Oxide.DataFileSystem.GetFile("ServerRewardsWipe/state");
			var state = _stateFile.ReadObject<WipeState>() ?? new WipeState();

			_pendingSave = state.PendingSave;

			TryExecutePendingWipe();
		}

		private void OnNewSave(string filename)
		{
			if (!config.options.ResetRPOnWipe)
				return;

			if (config.options.OnlyClearOnForceWipe && !IsForceWipe(DateTime.UtcNow))
			{
				Puts($"INFO: Wipe detected {DateTime.UtcNow:yyyy-MM-dd}, not a force wipe (first Thursday).");
				return;
			}

			_pendingSave = filename;
			SaveState();

			Puts($"INFO: Map wipe detected ({filename}), RP wipe scheduled.");

			// Attempt execution immediately in case ServerRewards is already loaded
			TryExecutePendingWipe();
		}

		private void OnPluginLoaded(Plugin plugin)
		{
			if (string.IsNullOrEmpty(_pendingSave))
				return;

			if (plugin == null || plugin.Name != "ServerRewards")
				return;

			if (!ServerRewards || !ServerRewards.IsLoaded)
				return;

			Puts($"INFO: ServerRewards loaded, executing RP wipe for save '{_pendingSave}'...");

			_pendingSave = null;
			SaveState();

			ClearRPData();
		}

		#endregion Hooks

		#region Helpers

		private void TryExecutePendingWipe()
		{
			if (string.IsNullOrEmpty(_pendingSave))
				return;

			if (!ServerRewards || !ServerRewards.IsLoaded)
				return;

			Puts($"INFO: Executing RP wipe for save {_pendingSave}");

			_pendingSave = null;
			SaveState();

			ClearRPData();
		}

		private void SaveState()
		{
			_stateFile.WriteObject(new WipeState
			{
				PendingSave = _pendingSave
			});
		}

		private string GetBackupDirectory()
		{
			var path = Path.Combine(
				Interface.Oxide.DataDirectory,
				"ServerRewards",
				"backups"
			);

			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);

			return path;
		}

		private bool IsForceWipe(DateTime utcNow)
		{
			// First Thursday of the month
			return utcNow.DayOfWeek == DayOfWeek.Thursday && utcNow.Day <= 7;
		}

		#endregion Helpers

		#region Main

		private void ClearRPData()
		{
			if (!ServerRewards || !ServerRewards.IsLoaded)
			{
				Puts("ERROR: ServerRewards is not loaded. Cannot wipe RP.");
				return;
			}

			if (config.options.BackupRP)
				BackupPlayerBalances();

			if (ClearRPViaInternalCommand())
			{
				if (config.options.DoServerSave)
				{
					ConsoleSystem.Run(ConsoleSystem.Option.Server, "server.save");
					Puts("INFO: ServerRewards RP wiped successfully & new balances persisted to disk immediately.");
				}
				else
				{
					Puts("INFO: ServerRewards RP wiped successfully, balance data will be saved to disk on next server save.");
				}
			}
			else
				PrintError("ERROR: ServerRewards RP wipe failed!");
		}

		private bool ClearRPViaInternalCommand()
		{
			try
			{
				var arg = new ConsoleSystem.Arg(ConsoleSystem.Option.Server, "rp clear *");
				var result = ServerRewards.Call("ConsolePointsManagement", arg);

				if (result is bool success)
					return success;

				return false;
			}
			catch (Exception ex)
			{
				PrintError($"ERROR: Internal RP wipe failed: {ex}");
				return false;
			}
		}

		private bool BackupPlayerBalances()
		{
			try
			{
				var sourcePath = Path.Combine(
					Interface.Oxide.DataDirectory,
					"ServerRewards",
					"player_balances.json"
				);

				if (!File.Exists(sourcePath))
				{
					Puts("WARNING: player_balances.json not found. Backup skipped.");
					return false;
				}

				var backupDir = GetBackupDirectory();

				var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
				var backupPath = Path.Combine(
					backupDir,
					$"player_balances_{timestamp}.json"
				);

				File.Copy(sourcePath, backupPath, overwrite: false);

				Puts($"INFO: ServerRewards player RP backup created: {backupPath}");
				return true;
			}
			catch (Exception ex)
			{
				Puts($"ERROR: Failed to backup ServerRewards player RP data: {ex}");
				return false;
			}
		}

		#endregion Main

		#region Commands

		[Command("clearrpdata")]
		private void cmdClearRPData(IPlayer player, string command, string[] args)
		{
			if (!player.HasPermission(permAdmin))
			{
				player.Reply(lang.GetMessage("NoPermission", this, player.Id));
				return;
			}
			ClearRPData();
		}

		#endregion Commands
		
		#region Config & Lang

		private ConfigData config;
		private class ConfigData
		{
			[JsonProperty(PropertyName = "Options")]
			public Options options;

			public class Options
			{
				[JsonProperty(PropertyName = "Reset ServerRewards player RP on wipe")]
				public bool ResetRPOnWipe { get; set; }
				[JsonProperty(PropertyName = "Only clear RP on force wipe (first Thursday of the month)")]
				public bool OnlyClearOnForceWipe { get; set; }
				[JsonProperty(PropertyName = "Backup player RP before wiping")]
				public bool BackupRP { get; set; }
				[JsonProperty(PropertyName = "Call 'server.save' after wiping so SR writes new data to disk immediately (protects against server crash)")]
				public bool DoServerSave { get; set; }
			}
			public VersionNumber Version { get; set; }
		}

		private ConfigData GetDefaultConfig()
		{
			return new ConfigData
			{
				options = new ConfigData.Options
				{
					ResetRPOnWipe = true,
					OnlyClearOnForceWipe = false,
					BackupRP = true,
					DoServerSave = false
				},
				Version = Version
			};
		}

		protected override void LoadConfig()
		{
			base.LoadConfig();
			try
			{
				config = Config.ReadObject<ConfigData>();
				if (config == null)
				{
					LoadDefaultConfig();
				}
				else
				{
					UpdateConfigValues();
				}
			}
			catch (Exception ex)
			{
				if (ex is JsonSerializationException || ex is NullReferenceException || ex is JsonReaderException)
				{
					LoadDefaultConfig();
					return;
				}
				throw;
			}
		}

		protected override void LoadDefaultConfig()
		{
			Puts("Configuration file missing or corrupt, creating default config file. Ignore if this is first load.");
			config = GetDefaultConfig();
			SaveConfig();
		}

		protected override void SaveConfig()
		{
			Config.WriteObject(config);
		}

		private void UpdateConfigValues()
		{
			if (config.Version < Version)
			{
				ConfigData defaultConfig = GetDefaultConfig();

				Puts("Config update detected! Updating config file...");

				if (config.Version < new VersionNumber(2, 0, 0))
				{
					config = defaultConfig;
				}

				if (config.Version < new VersionNumber(2, 0, 1))
				{
					config.options.OnlyClearOnForceWipe = defaultConfig.options.OnlyClearOnForceWipe;
				}

				Puts("Config update completed!");
			}
			config.Version = Version;
			SaveConfig();
		}

		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>
			{
				["NoPermission"] = "You do not have permission to use this command."
			}, this, "en");
		}

		#endregion Config & Lang
	}
}