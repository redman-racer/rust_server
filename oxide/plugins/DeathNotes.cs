using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Rust;
using Rust.Ai.Gen2;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("Death Notes", "Terceran/Mr. Blue", "6.5.5")]
	[Description("Broadcasts detailed death information")]
	class DeathNotes : RustPlugin
	{
		[PluginReference]
		private Plugin UINotify;
		
		[PluginReference]
		private Plugin Notify;
		
		#region Fields

		private const string WildcardCharacter = "*";
		private const string CanSeePermission = "deathnotes.cansee";
		private const string CantSeePermission = "deathnotes.cantsee";
        private const string SuppressPermission = "deathnotes.suppress";
        private const string TeamOnlyPermission = "deathnotes.seeteamonly";

		private static DeathNotes _instance;

		private PluginConfiguration _configuration;

		private readonly Dictionary<string, string> _enemyPrefabs = new()
		{
			["spikes.floor"] = "Wooden Floor Spike Cluster",
			["spikes_static"] = "Wooden Floor Spike Cluster",
			["barricade.woodwire"] = "Barbed Wooden Barricade",
			["barricade.metal"] = "Metal Barricade",
			["wall.external.high.wood"] = "High External Wooden Wall",
			["wall.external.high.stone"] = "High External Stone Wall",
			["gates.external.high.stone"] = "High External Stone Gate",
			["campfire"] = "Campfire",
			["skull_fire_pit"] = "Skull Fire Pit",
			["heavyscientist"] = "Heavy Scientist"
		};
		private readonly Dictionary<string, string> _weaponPrefabs = new()
		{
			["rocket_basic"] = "Rocket",
			["rocket_hv"] = "High Velocity Rocket",
			["rocket_fire"] = "Incendiary Rocket",
			["grenade.f1.deployed"] = "F1 Grenade",
			["grenade.beancan.deployed"] = "Beancan Grenade",
			["survey_charge.deployed"] = "Survey Charge",
			["explosive.satchel.deployed"] = "Satchel Charge",
			["explosive.timed.deployed"] = "Timed Explosive Charge",
			["rock.entity"] = "Rock",
			["longsword.entity"] = "Longsword",
			["mace.entity"] = "Mace",
			["spear_stone.entity"] = "Stone Spear",
			["spear_wooden.entity"] = "Wooden Spear",
			["machete.weapon"] = "Machete",
			["knife_bone.entity"] = "Bone Knife",
			["bone_club.entity"] = "Bone Club",
			["salvaged_cleaver.entity"] = "Salvaged Cleaver",
			["salvaged_sword.entity"] = "Salvaged Sword",
			["candy_cane.entity"] = "Candy Cane Club",
			["flamethrower.entity"] = "Flame Thrower",
			["snowball.entity"] = "Snowball",
			["combat.knife.entity"] = "Combat Knife"
		};
		private readonly Dictionary<string, CombatEntityType> _combatEntityTypes = new()
		{
			["GunTrap"]  = CombatEntityType.Trap,
			["FlameTurret"]  = CombatEntityType.Turret,
			["AutoTurret"]  = CombatEntityType.Turret,
			["BaseHelicopter"]  = CombatEntityType.Helicopter,
			["BradleyAPC"]  = CombatEntityType.Bradley,
			["BasePlayer"]  = CombatEntityType.Player,
			["NPCMurderer"]  = CombatEntityType.Murderer,
			["CodeLock"]  = CombatEntityType.Lock,
			["Scientist"]  = CombatEntityType.Scientist,
			["ScientistNPC"]  = CombatEntityType.Scientist,
			["HTNPlayer"]  = CombatEntityType.Scientist,
			["NPCAutoTurret"]  = CombatEntityType.Sentry,
			["FireBall"]  = CombatEntityType.Fire,
			["scarecrow"]  = CombatEntityType.ScarecrowNPC,
			["ScientistNPCNew"]  = CombatEntityType.Scientist,
			["tunneldweller"]  = CombatEntityType.TunnelDweller,
			["underwaterdweller"]  = CombatEntityType.UnderwaterDweller
		};

		private readonly Regex _colorTagRegex =
			new Regex(@"<color=.{0,7}>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		private readonly Regex _sizeTagRegex =
			new Regex(@"<size=\d*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		private readonly List<string> _richTextLiterals = new List<string>
		{
			"</color>", "</size>", "<b>", "</b>", "<i>", "</i>"
		};

		private readonly Dictionary<ulong, AttackInfo> _previousAttack = new Dictionary<ulong, AttackInfo>();

        private Dictionary<ulong, HitInfo> _patrolHeliTagTracker = new Dictionary<ulong, HitInfo>();
        private HashSet<ulong> _bradleyApcTagTracker = new HashSet<ulong>();

        private void LogOutput(string text)
        {
	        //Puts(text);
	        string logText = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss") + " : " + text;
	        LogToFile("DeathNotes-log", logText, this);
        }
        
		private readonly Func<PluginConfiguration.DeathMessage, DeathData, bool>[] _messageMatchingStages =
		{
			(m, d) => m.Enabled == true && MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => m.Enabled == true && MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          m.DamageType == WildcardCharacter,

			(m, d) => m.Enabled == true && MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          m.VictimType == WildcardCharacter &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => m.Enabled == true && m.KillerType == WildcardCharacter &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => m.Enabled == true && MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          m.VictimType == WildcardCharacter &&
			          m.DamageType == WildcardCharacter,

			(m, d) => m.Enabled == true && m.KillerType == WildcardCharacter &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          m.DamageType == WildcardCharacter,

			(m, d) => m.Enabled == true && m.KillerType == WildcardCharacter &&
			          m.VictimType == WildcardCharacter &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => m.Enabled == true && m.KillerType == WildcardCharacter &&
			          m.VictimType == WildcardCharacter &&
			          m.DamageType == WildcardCharacter
		};

		#endregion

		#region Hooks

		private void Init()
		{
			_instance = this;

			permission.RegisterPermission(CanSeePermission, this);
            permission.RegisterPermission(CantSeePermission, this);
            permission.RegisterPermission(SuppressPermission, this);
            permission.RegisterPermission(TeamOnlyPermission, this);

			_configuration = Config.ReadObject<PluginConfiguration>();
			_configuration.LoadDefaults();
			Config.WriteObject(_configuration);

            string tmpPerm;
            foreach (string perm in _configuration.PatrolHeliDisplayPerms)
            {
                tmpPerm = perm.Trim().ToLower();
                if (!perm.StartsWith("deathnotes."))
                    continue;
                permission.RegisterPermission(perm, this);
            }

            foreach (string perm in _configuration.BradleyDisplayPerms)
            {
                tmpPerm = perm.Trim().ToLower();
                if (!perm.StartsWith("deathnotes."))
                    continue;
                permission.RegisterPermission(perm, this);
            }

            foreach (var msg in _configuration.Translations.Messages)
			{
                foreach (string perm in msg.DisplayPerms)
                {
                    tmpPerm = perm.Trim().ToLower();
                    if (!perm.StartsWith("deathnotes."))
                        continue;
                    permission.RegisterPermission(perm, this);
                }
            }

			LogOutput("Initializing DeathNotes plugin");
		}

		private void OnPluginLoaded(Plugin plugin)
		{
			if (plugin.Name == "Notify")
				Notify = plugin;
			else if (plugin.Name == "UINotify")
				UINotify = plugin;
		}

		private void OnPluginUnloaded(Plugin plugin)
		{
			if (plugin.Name == "Notify")
				Notify = null;
			else if (plugin.Name == "UINotify")
				UINotify = null;
		}
		
		private void Unload()
		{
			LogOutput("Unloading DeathNotes plugin");
			_instance = null;
		}

		private void OnEntityTakeDamage(BasePlayer victimEntity, HitInfo hitInfo)
		{
			if (victimEntity == null || hitInfo == null)
            	return;

            BaseEntity attacker = victimEntity.lastAttacker ?? hitInfo?.Initiator;

            var userId = victimEntity.ToPlayer().userID;

            AttackInfo currentAttack;
            if (_previousAttack.TryGetValue(userId, out currentAttack))
            {
                if (attacker != null)
                    currentAttack.Attacker = attacker;
                
                if (hitInfo != null)
                    currentAttack.HitInfo = hitInfo;

                if (victimEntity.lastDamage != null)
                    currentAttack.DamageType = victimEntity.lastDamage;
            }
            else
            {
                _previousAttack[userId] = new AttackInfo
                {
                    HitInfo = hitInfo,
                    Attacker = victimEntity.lastAttacker ?? hitInfo?.Initiator,
                    DamageType = victimEntity.lastDamage
                };
            }
		}

        //This method tracks Bradley APC damage and reports whoever tagged it.
        private void OnEntityTakeDamage(BradleyAPC victimEntity, HitInfo hitInfo) => HandleEntityDamage(victimEntity, hitInfo);
        
        //This method tracks Patrol Helicopter damage and reports whoever tagged it.
		private void OnPatrolHelicopterTakeDamage(PatrolHelicopter victimEntity, HitInfo hitInfo) => HandleEntityDamage(victimEntity, hitInfo);

		private void HandleEntityDamage( BaseCombatEntity victimEntity, HitInfo hitInfo )
		{
			if (victimEntity == null || hitInfo == null)
				return;

			bool isPatrolHeli = victimEntity is PatrolHelicopter;

			HitInfo storedHitInfo;
			ulong netID = victimEntity.net.ID.Value;

			if (isPatrolHeli)
			{
				bool wasHeliIdFound = _patrolHeliTagTracker.TryGetValue(netID, out storedHitInfo);
				if (!wasHeliIdFound)
				{
					storedHitInfo = new HitInfo();
					_patrolHeliTagTracker.Add(netID, storedHitInfo);
				}

				if (hitInfo.WeaponPrefab != null)
					storedHitInfo.WeaponPrefab = hitInfo.WeaponPrefab;

				if (hitInfo.Weapon != null)
					storedHitInfo.Weapon = hitInfo.Weapon;

				if (wasHeliIdFound || (victimEntity.lastAttackedTime != float.NegativeInfinity))
					return;
				
				if (storedHitInfo.Initiator == null && hitInfo.Initiator != null)
					storedHitInfo.Initiator = hitInfo.Initiator;
			}
			else //If it's a Bradley APC
			{
				if ((victimEntity.lastAttackedTime != float.NegativeInfinity) || (_bradleyApcTagTracker.Contains(netID)))
					return;
				_bradleyApcTagTracker.Add(netID);
				storedHitInfo = hitInfo;
			}

			var attackerPlayer = hitInfo.Initiator?.ToPlayer();
			if( attackerPlayer?.displayName == null )
			{
				return;
			}

			storedHitInfo.Initiator = hitInfo.Initiator;

			if( (isPatrolHeli ? !ShowPatrolHeliTags() : !ShowBradleyTags()) || permission.UserHasPermission( attackerPlayer.UserIDString, SuppressPermission ) )
			{
				return;
			}

			var data = new DeathData
			{
				VictimEntity = victimEntity,
				KillerEntity = victimEntity.lastAttacker ?? hitInfo.Initiator,
				VictimEntityType = GetCombatEntityType(victimEntity),
				KillerEntityType = GetCombatEntityType(victimEntity.lastAttacker),
				DamageType = victimEntity.lastDamage,
				HitInfo = storedHitInfo
			};

			if (data.KillerEntity != null)
				data.KillerEntityType = CombatEntityType.Player;
				
			string message = PopulateMessageVariables(isPatrolHeli ? _configuration.PatrolHeliTagMessage : _configuration.BradleyTagMessage, data);

			object hookResult = false;
			try
			{
				hookResult = Interface.Call("OnDeathNotice", data.ToDictionary(), message);
			}
			catch (NullReferenceException)
			{
				return;
			}
			
			if (hookResult?.Equals(false) ?? false)
				return;

			//Prints to console
			if (isPatrolHeli ? _configuration.ShowPatrolHeliTagsInConsole : _configuration.ShowBradleyTagsInConsole)
				Puts(StripRichText(message));
				
			//Prints to chat, the Notify plugin, and/or the UINotify plugin
			foreach (var player in BasePlayer.activePlayerList)
			{
				if ((_configuration.RequirePermission &&
				     !permission.UserHasPermission(player.UserIDString, CanSeePermission)) ||
				    permission.UserHasPermission(player.UserIDString, CantSeePermission) ||
                    (permission.UserHasPermission(player.UserIDString, TeamOnlyPermission) && !ArePlayersOnSameTeam(player, data)))
					continue;

                if (isPatrolHeli ? !PlayersPassesPermsCheck(player.UserIDString, _configuration.PatrolHeliDisplayPerms) : !PlayersPassesPermsCheck(player.UserIDString, _configuration.BradleyDisplayPerms))
                        continue;

				if (_configuration.MessageRadius != -1 &&
				    player.Distance(data.VictimEntity) > _configuration.MessageRadius)
					continue;

				if (isPatrolHeli ? _configuration.ShowPatrolHeliTagsInChat : _configuration.ShowBradleyTagsInChat)
				{
					Player.Reply(
						player,
						_configuration.ChatFormat.Replace("{message}", message),
						ulong.Parse(_configuration.ChatIcon)
					);
				}
					
				if ((isPatrolHeli ? _configuration.ShowPatrolHeliTagsInNotify : _configuration.ShowBradleyTagsInNotify) && Notify != null)
					Notify.Call("SendNotify", player, _configuration.NotifyMessageType, _configuration.ChatFormat.Replace("{message}", message));
					
				if ((isPatrolHeli ? _configuration.ShowPatrolHeliTagsInUINotify : _configuration.ShowBradleyTagsInUINotify) && UINotify != null)
					UINotify.Call("SendNotify", player, _configuration.UINotifyMessageType, _configuration.ChatFormat.Replace("{message}", message));
			}
		}
		
		//This method tracks when Patrol Helicopters are killed or despawns naturally, and cleans them from the tag trackers.
		private void OnEntityKill(PatrolHelicopter entity)
		{
			_patrolHeliTagTracker.Remove(entity.net.ID.Value);
		}

		//This method tracks when Bradley APCs are killed or despawns naturally, and cleans them from the tag trackers.
		private void OnEntityKill(BradleyAPC entity)
		{
			_bradleyApcTagTracker.Remove(entity.net.ID.Value);
		}
		
		private void OnEntityDeath(BaseCombatEntity victimEntity, HitInfo hitInfo)
		{
			//There is no victim information for some reason.
			// Try to avoid error when entity was destroyed
			//Note: If someone is wounded and dies, there will be null hitInfo
			if ((victimEntity == null) || (victimEntity.gameObject == null))
				return;

			//Snakes produce two death messages, one when it is killed and one when it is skinned.
			//The below is to prevent a death note related to skinning.
			if ((victimEntity is SnakeHazard) && (((SnakeHazard)victimEntity).IsCorpse))
				return;
			
			//Some corpses produce two death messages, one when it is killed and one when it is skinned.
			//The below is to prevent a death note related to skinning.
			if (victimEntity is BaseCorpse)
				return;
			
			if (hitInfo == null)
			{
				hitInfo = new HitInfo();
				hitInfo.Initiator = null;
			}

			//Attempts to get the NetID. This was occasionally resulting in a NullReferenceException, so we're being careful.
			ulong netID = 0;
			try
			{
				netID = victimEntity.net.ID.Value;
			}
			catch (Exception e)
			{
				LogOutput($"Exception in OnEntityDeath while retrieving victimEntity.net: {e.ToString()}");
				netID = 0;
			}
			
			if ((victimEntity is PatrolHelicopter || victimEntity is BradleyAPC) && netID == 0)
				return;

			DeathData data = new DeathData
			{
				VictimEntity = victimEntity,
				KillerEntity = victimEntity.lastAttacker ?? hitInfo?.Initiator,
				VictimEntityType = GetCombatEntityType(victimEntity),
				KillerEntityType = GetCombatEntityType(victimEntity.lastAttacker),
				DamageType = victimEntity.lastDamage,
				HitInfo = hitInfo
			};

			if (victimEntity is PatrolHelicopter && _patrolHeliTagTracker.TryGetValue( netID, out var value ))
				data.HitInfo = value;

			// Handle inconsistencies/exceptions
			HandleInconsistencies(ref data);

            if (data.VictimEntityType == CombatEntityType.Player)
                _previousAttack.Remove(data.VictimEntity.ToPlayer().userID);
            
			if (_configuration.DebugMode)
			{
				LogOutput("[DEATHNOTES DEBUG]");
				LogOutput(
					$"\tKillerEntity: {data.KillerEntity?.GetType().Name ?? "NULL"} / {data.KillerEntity?.ShortPrefabName ?? "NULL"} / {data.KillerEntity?.PrefabName ?? "NULL"}");
				if (data.KillerEntity != null)
					LogOutput(
						$"\tKiller's Owner: {covalence.Players.FindPlayerById(data.KillerEntity?.OwnerID.ToString())?.Name}");
				LogOutput(
					$"\tVictimEntity: {data.VictimEntity?.GetType().Name ?? "NULL"} / {data.VictimEntity?.ShortPrefabName ?? "NULL"} / {data.VictimEntity?.PrefabName ?? "NULL"}");
				LogOutput($"\tInitiator: {data.HitInfo?.Initiator?.ShortPrefabName}");
				LogOutput($"\tLastAttacker: {data.VictimEntity?.lastAttacker?.ShortPrefabName}");
				LogOutput($"\tKillerEntityType: {data.KillerEntityType}");
				LogOutput($"\tVictimEntityType: {data.VictimEntityType}");
				LogOutput($"\tDamageType: {data.DamageType}");
				LogOutput($"\tBodypart: {GetCustomizedBodypartName(data.HitInfo)}");
				LogOutput($"\tWeapon: {hitInfo?.WeaponPrefab?.ShortPrefabName ?? "NULL"}");
                if (data.HitInfo != null)
                    LogOutput($"\tHitPositionWorld: {GetPositionString(data.HitInfo.HitPositionWorld)}");
			}
			

			// Change entity type for dwellers
			RepairEntityTypes(ref data);

			// Need do this before we cancel out of the method, as we need to track all these entities dying. Even if it's not a player killing them.
			if (victimEntity is PatrolHelicopter)
				_patrolHeliTagTracker.Remove(netID);
			else if (victimEntity is BradleyAPC)
				_bradleyApcTagTracker.Remove(netID);

			// Ignore deaths of other entities
			if (data.KillerEntityType == CombatEntityType.Other || data.VictimEntityType == CombatEntityType.Other)
				return;
			
            PluginConfiguration.DeathMessage deathMessage = GetDeathMessage(data);

			// Populate the variables in the message
			string message = PopulateMessageVariables(
				// Find the best matching death message for this death
				GetDeathMessageString(deathMessage),
				data
			);

			if ((message == null) || (deathMessage == null))
				return;
	
			object hookResult = false;
			try
			{ 
				hookResult = Interface.Call("OnDeathNotice", data.ToDictionary(), message);
				if (hookResult?.Equals(false) ?? false)
					return;
			}
			catch (Exception e)
			{
				LogOutput($"Exception in OnEntityDeath while calling OnDeathNotice hook: {e.ToString()}");
				return;
			}
			
			if (
					(deathMessage.ShowKillsInChat == true || deathMessage.ShowKillsInConsole == true || deathMessage.ShowKillsInNotify == true || deathMessage.ShowKillsInUINotify == true) &&
					(data.KillerEntityType != CombatEntityType.Player || !permission.UserHasPermission(data.KillerEntity?.ToPlayer()?.UserIDString, SuppressPermission)) &&
					(data.VictimEntityType != CombatEntityType.Player || !permission.UserHasPermission(data.VictimEntity?.ToPlayer()?.UserIDString, SuppressPermission))
			    )
			{

				//Prints to console
				if (deathMessage.ShowKillsInConsole == true)
					Puts(StripRichText(message));
				
				//Prints to chat, the Notify plugin, and/or the UINotify plugin
				foreach (var player in BasePlayer.activePlayerList)
				{
					if ((_configuration.RequirePermission &&
						!permission.UserHasPermission(player.UserIDString, CanSeePermission)) ||
					    permission.UserHasPermission(player.UserIDString, CantSeePermission) ||
                        (permission.UserHasPermission(player.UserIDString, TeamOnlyPermission) && !ArePlayersOnSameTeam(player, data)))
						continue;
                    
                    if (!PlayersPassesPermsCheck(player.UserIDString, deathMessage.DisplayPerms))
                        continue;

					if (_configuration.MessageRadius != -1 &&
						player.Distance(data.VictimEntity) > _configuration.MessageRadius)
						continue;

					if (deathMessage.ShowKillsInChat == true)
					{
						Player.Reply(
							player,
							_configuration.ChatFormat.Replace("{message}", message),
							ulong.Parse(_configuration.ChatIcon)
						);
					}
					
					if ((deathMessage.ShowKillsInNotify == true) && Notify != null)
						Notify.Call("SendNotify", player, _configuration.NotifyMessageType, _configuration.ChatFormat.Replace("{message}", message));

					if ((deathMessage.ShowKillsInUINotify == true) && UINotify != null)
						UINotify.Call("SendNotify", player, _configuration.UINotifyMessageType, _configuration.ChatFormat.Replace("{message}", message));
				}
			}
		}

		private void RepairEntityTypes(ref DeathData data)
		{
			if (data.VictimEntity != null)
			{
				string victimPrefabName = data.VictimEntity.ShortPrefabName.ToLower();
				if (victimPrefabName.Contains("corpse"))
				{
					data.VictimEntityType = CombatEntityType.Other;
				}
				else if (victimPrefabName.Contains("underwaterdweller"))
				{
					data.VictimEntityType = CombatEntityType.UnderwaterDweller;
				}
				else if (victimPrefabName.Contains("tunneldweller"))
				{
					data.VictimEntityType = CombatEntityType.TunnelDweller;
				}
				else if (victimPrefabName.Contains("bradleyapc"))
				{
					data.VictimEntityType = CombatEntityType.Bradley;
				}
				else if (victimPrefabName.Contains("shark"))
				{
					data.VictimEntityType = CombatEntityType.Shark;
				}
			}

			if (data.KillerEntity != null)
			{
				string killerPrefabName = data.KillerEntity.ShortPrefabName.ToLower();

				if (killerPrefabName.Contains("corpse"))
				{
					data.KillerEntityType = CombatEntityType.Other;
				}
				else if (killerPrefabName.Contains("underwaterdweller"))
				{
					data.KillerEntityType = CombatEntityType.UnderwaterDweller;
				}
				else if (killerPrefabName.StartsWith("excavator"))
				{
					data.KillerEntityType = CombatEntityType.ExcavatorArm;
				}
				else if (killerPrefabName.StartsWith("carshredder"))
				{
					data.KillerEntityType = CombatEntityType.CarShredder;
				}
				else if (killerPrefabName.Contains("tunneldweller"))
				{
					data.KillerEntityType = CombatEntityType.TunnelDweller;
				}
				else if (killerPrefabName.Contains("bradleyapc"))
				{
					data.KillerEntityType = CombatEntityType.Bradley;
				}
				else if (killerPrefabName.Contains("shark"))
				{
					data.KillerEntityType = CombatEntityType.Shark;
				}
				else if (killerPrefabName.Contains("cactus"))
				{
					data.KillerEntityType = CombatEntityType.Cactus;
				}
				else if (killerPrefabName.Contains("locomotive"))
				{
					data.KillerEntityType = CombatEntityType.Train;
				}
				else if (killerPrefabName.Contains("workcart"))
				{
					data.KillerEntityType = CombatEntityType.Train;
				}
				else if (killerPrefabName.Contains("train"))
				{
					data.KillerEntityType = CombatEntityType.Train;
				}

			}
		}

		private void OnFlameThrowerBurn(FlameThrower flameThrower, BaseEntity baseEntity)
		{
			if (flameThrower == null || baseEntity == null) return;

			var flame = baseEntity.gameObject.AddComponent<Flame>();
			flame.Source = Flame.FlameSource.Flamethrower;
			flame.SourceEntity = flameThrower;
			flame.Initiator = flameThrower.GetOwnerPlayer();
		}

		private void OnFlameExplosion(FlameExplosive explosive, BaseEntity baseEntity)
		{
			if (explosive == null || baseEntity == null) return;

			var flame = baseEntity.gameObject.AddComponent<Flame>();
			flame.Source = Flame.FlameSource.IncendiaryProjectile;
			flame.SourceEntity = explosive;
			flame.Initiator = explosive.creatorEntity;
		}

		private void OnFireBallSpread(FireBall fireBall, BaseEntity newFire)
		{
			if (fireBall == null) return;

			var flame = fireBall.GetComponent<Flame>();
			if (flame == null) return;

			var newFlame = newFire.gameObject.AddComponent<Flame>();
			newFlame.Source = flame.Source;
			newFlame.SourceEntity = flame.SourceEntity;
			newFlame.Initiator = flame.Initiator;
		}

		private void OnFireBallDamage(FireBall fireBall, BaseCombatEntity target, HitInfo hitInfo)
        {
            if (hitInfo.Initiator == null)
			    hitInfo.Initiator = fireBall;
        }

		#endregion

		#region Death Messages

		private PluginConfiguration.DeathMessage GetDeathMessage(DeathData data)
		{
			foreach (var matchingStage in _messageMatchingStages)
			{
				var match = _configuration.Translations.Messages.Find(m => matchingStage.Invoke(m, data));

				if (match != null)
					return match;
			}

			return null;
		}

		private string GetDeathMessageString(PluginConfiguration.DeathMessage deathMessage)
		{
            if (deathMessage != null)
                return deathMessage.Messages.GetRandom((uint) DateTime.UtcNow.Millisecond);
            else return null;
        }

		private string PopulateMessageVariables(string message, DeathData data)
		{
			if (string.IsNullOrEmpty(message))
				return null;

			var replacements = new Dictionary<string, string>
			{
				["victim"] = GetCustomizedEntityName(data.VictimEntity, data.VictimEntityType),
                ["position"] = GetPositionString(data.HitInfo.HitPositionWorld),
                ["mapgrid"] = MapHelper.GridToString(MapHelper.PositionToGrid(data.HitInfo.HitPositionWorld))
			};

			if (data.KillerEntityType != CombatEntityType.None)
			{
				replacements.Add("killer", GetCustomizedEntityName(data.KillerEntity, data.KillerEntityType));
				replacements.Add("bodypart", GetCustomizedBodypartName(data.HitInfo));

				if (data.KillerEntity != null)
				{
					var distance = data.KillerEntity.Distance(data.VictimEntity);
					replacements.Add("distance", GetDistance(distance, _configuration.UseMetricDistance));
				}

				if ((data.KillerEntityType == CombatEntityType.Player) || (data.KillerEntityType == CombatEntityType.CustomNPC) || (data.KillerEntityType == CombatEntityType.Drone))
				{
					replacements.Add("hp", data.KillerEntity.Health().ToString("#0.#"));
					replacements.Add("weapon", GetCustomizedWeaponName(data));
					replacements.Add("attachments", string.Join(", ", GetCustomizedAttachmentNames(data.HitInfo)));
				}
				else if (data.KillerEntityType == CombatEntityType.Turret
						 || data.KillerEntityType == CombatEntityType.Lock
						 || data.KillerEntityType == CombatEntityType.Trap
						 || data.KillerEntityType == CombatEntityType.SAM)
				{
					replacements.Add("owner",
						covalence.Players.FindPlayerById(data.KillerEntity.OwnerID.ToString())?.Name ?? "Unknown Owner"
					);
				}
			}

			message = InsertPlaceholderValues(message, replacements);

			replacements = null;
			return message;
		}

		private struct DeathData
		{
			public CombatEntityType VictimEntityType { get; set; }
			[JsonIgnore] public BaseCombatEntity VictimEntity { get; set; }

			public CombatEntityType KillerEntityType { get; set; }
			[JsonIgnore] public BaseEntity KillerEntity { get; set; }

			public DamageType DamageType { get; set; }
			[JsonIgnore] public HitInfo HitInfo { get; set; }

			public Dictionary<string, object> ToDictionary() => new Dictionary<string, object>
			{
				["VictimEntityType"] = VictimEntityType,
				["VictimEntity"] = VictimEntity,
				["KillerEntityType"] = KillerEntityType,
				["KillerEntity"] = KillerEntity,
				["DamageType"] = DamageType,
				["HitInfo"] = HitInfo
			};
		}

		#endregion

		#region Entity Identification

		private CombatEntityType GetCombatEntityType(BaseEntity entity)
		{
			
			if (entity == null)
				return CombatEntityType.None;
			
			string entityTypeName = entity.GetType().Name;
			if (entityTypeName != "ScientistNPC" && _combatEntityTypes.TryGetValue( entityTypeName, out var entityType ))
				return entityType;

			if (_combatEntityTypes.TryGetValue( entity.ShortPrefabName, out var type ))
				return type;

			//For those plugins that do not correctly type ZombieNPCs as Zombies.
			if (entityTypeName.StartsWith("Zombie"))
				return CombatEntityType.ZombieNPC;
			
            //For a plugin that defined a CustomPet entity name, we redefine it as a CustonNPC.
			if (entityTypeName.StartsWith("CustomPet"))
				return CombatEntityType.CustomNPC;

 			switch (entity)
			{
				case ScientistNPC:
                case ScientistNPC2:
				{
					if (entity.ShortPrefabName.EndsWith("heavy"))
						return CombatEntityType.HeavyScientist;
					return CombatEntityType.Scientist;
				}

				case Zombie:
					return CombatEntityType.ZombieNPC;
				
				case PatrolHelicopter:
					return CombatEntityType.Helicopter;
				
				case BaseAnimalNPC:
				case Wolf2:
				case Panther:
				case Tiger:
				case Crocodile:
				case SnakeHazard:
				case RidableHorse:
				case FarmableAnimal:
					return CombatEntityType.Animal;
				
				case SamSite:
					return CombatEntityType.SAM;
				
				case BaseOven:
					return CombatEntityType.HeatSource;
				
				case SimpleBuildingBlock:
					return CombatEntityType.ExternalWall;
				
				case Barricade:
					return CombatEntityType.Barricade;
				
				case BaseTrap:
				case IOEntity:
				case GunTrap:
					return CombatEntityType.Trap;

				case GingerbreadNPC:
					return CombatEntityType.GingerbreadNPC;

				case ScarecrowNPC:
					return CombatEntityType.ScarecrowNPC;
				
				case Minicopter:
					return CombatEntityType.Minicopter;
				
				case ScrapTransportHelicopter:
					return CombatEntityType.ScrapTransportHelicopter;
				
				case AttackHelicopter:
					return CombatEntityType.AttackHelicopter;
				
				case BeeSwarmAI:
					return CombatEntityType.BeeSwarm;
				
				case BaseFishNPC:
					if (entity.ShortPrefabName.ToLower().Contains("shark"))
						return CombatEntityType.Shark;
					else return CombatEntityType.Animal;
					
				case SimpleShark:
					return CombatEntityType.Shark;

				case NPCShopKeeper:
					return CombatEntityType.NPCShopKeeper;

				case ExcavatorArm:
					return CombatEntityType.ExcavatorArm;

				case LargeShredder:
					return CombatEntityType.CarShredder;

				case Drone:
					return CombatEntityType.Drone;

				case ResourceEntity:
				{
					if (entity.ShortPrefabName.EndsWith("cactus"))
						return CombatEntityType.Cactus;
					else return CombatEntityType.Other;
				}

				case TrainEngine:
				case TrainCar:
					return CombatEntityType.Train;

				case MagnetCrane:
					return CombatEntityType.MagnetCrane;

				case ElevatorLift:
					return CombatEntityType.Elevator;
				
				case HotAirBalloon:
					return CombatEntityType.HotAirBalloon;

				case BatteringRam:
					return CombatEntityType.BatteringRam;

                case Tugboat:
                    return CombatEntityType.Tugboat;

                case CargoShip:
                    return CombatEntityType.CargoShip;

                case DigitSendCodeLock:
                    return CombatEntityType.Lock;

				default:
					return CombatEntityType.Other;
			}
		}

		private string GetCustomizedEntityName(BaseEntity entity, CombatEntityType combatEntityType)
		{
			var name = GetEntityName(entity, combatEntityType);

			if (string.IsNullOrEmpty(name))
				return null;

			// Don't load player names into config
			if (combatEntityType == CombatEntityType.Player)
				return name;

			if (!_configuration.Translations.Names.ContainsKey(name))
			{
				_configuration.Translations.Names.Add(name, name);
				Config.WriteObject(_configuration);
			}

			return _configuration.Translations.Names[name];
		}

		private string GetEntityName(BaseEntity entity, CombatEntityType combatEntityType)
		{
			// Entity may be null for helicopter or bradley, see HandleExceptions(...)
			if (entity == null &&
				combatEntityType != CombatEntityType.Helicopter &&
				combatEntityType != CombatEntityType.Bradley)
				return null;

			switch (combatEntityType)
			{
				case CombatEntityType.Player:
					return StripRichText(entity.ToPlayer().displayName);
				
				case CombatEntityType.Murderer:
				case CombatEntityType.ScarecrowNPC:
				case CombatEntityType.Scientist:
				case CombatEntityType.HeavyScientist:
				case CombatEntityType.ZombieNPC:
				case CombatEntityType.GingerbreadNPC:
				case CombatEntityType.NPCShopKeeper:
					var name = entity.ToPlayer()?.displayName;
					
					if ((combatEntityType == CombatEntityType.HeavyScientist) && (name == "Scientist"))
						name = "Heavy Scientist";
					
					if (!string.IsNullOrEmpty(name) && name != entity.ToPlayer()?.userID.ToString())
					{
						return name;
					}

					if (!_enemyPrefabs.ContainsKey(entity.ShortPrefabName))
					{
						return combatEntityType.ToString();
					}

					break;

				case CombatEntityType.TunnelDweller:
					return "Tunnel Dweller";

				case CombatEntityType.UnderwaterDweller:
					return "Underwater Dweller";

				case CombatEntityType.Helicopter:
					return "Patrol Helicopter";

				case CombatEntityType.Bradley:
					return "Bradley APC";

				case CombatEntityType.Sentry:
					return "Sentry";
				
				case CombatEntityType.Fire:
					return entity.creatorEntity?.ToPlayer()?.displayName ?? "Fire";
					
				case CombatEntityType.Minicopter:
					return "Minicopter";
				
				//NOTE: Scrappy reporting can't work because a death results in two death notices. One is where the Scrappy is both the killer and the victim,
				//and the other is where it shows the player as having committed suicide, even though they did not. There's no apparent way to tie
				//the two together.
				case CombatEntityType.ScrapTransportHelicopter:
					return "Scrap Transport Helicopter";
				
				case CombatEntityType.AttackHelicopter:
					return "AttackHelicopter";
				
				case CombatEntityType.BeeSwarm:
					return "BeeSwarm";
				
				case CombatEntityType.Shark:
					return "Shark";

				case CombatEntityType.ExcavatorArm:
					return "Giant Excavator Arm";

				case CombatEntityType.CarShredder:
					return "Car Shredder";

				case CombatEntityType.Drone:
					return "Drone";

				case CombatEntityType.Cactus:
					return "Cactus";

				case CombatEntityType.Train:
					return "Train";

				case CombatEntityType.MagnetCrane:
					return "Magnet Crane";

				case CombatEntityType.Elevator:
					return "Elevator";

				case CombatEntityType.HotAirBalloon:
					return "Hot Air Balloon";

				case CombatEntityType.BatteringRam:
					return "Battering Ram";

                case CombatEntityType.Tugboat:
                    return "Tugboat";

                case CombatEntityType.CargoShip:
                    return "CargoShip";

                case CombatEntityType.CustomNPC:
                    if (entity != null && entity is BasePlayer)
                        return entity.ToPlayer().displayName;
                    else return "CustomNPC";
			}

			if (_enemyPrefabs.TryGetValue( entity.ShortPrefabName, out string entityName ))
				return entityName;

			return HumanizePascalCase(entity.GetType().Name);
		}

		internal enum CombatEntityType
		{
			Helicopter = 0,
			Bradley = 1,
			Animal = 2,
			Murderer = 3,
			Scientist = 4,
			Player = 5,
			Trap = 6,
			Turret = 7,
			Barricade = 8,
			ExternalWall = 9,
			HeatSource = 10,
			Fire = 11,
			Lock = 12,
			Sentry = 13,
			Other = 14,
			None = 15,
			ScarecrowNPC = 16,
			TunnelDweller = 17,
			UnderwaterDweller = 18,
			ZombieNPC = 19,
			GingerbreadNPC = 20,
			HeavyScientist = 21,
			Minicopter = 22,
			ScrapTransportHelicopter = 23,
			AttackHelicopter = 24,
			BeeSwarm = 25,
			SAM = 26,
			Shark = 27,
			NPCShopKeeper = 28,
			ExcavatorArm = 29,
			Drone = 30,
			CarShredder = 31,
			Cactus = 32,
			Train = 33,
			MagnetCrane = 34,
			Elevator = 35,
			HotAirBalloon = 36,
			BatteringRam = 37,
            Tugboat = 38,
            CargoShip = 39,
            CustomNPC = 40
		}

		#endregion

		#region Workarounds and Inconsistency Handling

		private void HandleInconsistencies(ref DeathData data)
		{
			// Deaths of other entity types are not of interest and might cause errors
			if (data.VictimEntityType == CombatEntityType.Other)
				return;

			if (data.KillerEntity is FireBall)
			{
				data.DamageType = DamageType.Heat;
				if (data.KillerEntity.ShortPrefabName.StartsWith("flameturret"))
					data.KillerEntityType = CombatEntityType.Turret;
			}

			// If the killer entity is null, but a weapon is given, we might be able to fall back to the parent entity of that weapon
			// Notably for the auto turret after the changes it has had
			if (data.KillerEntity == null && data.HitInfo?.Weapon != null)
			{
				data.KillerEntity = data.HitInfo.Weapon.GetParentEntity();
				data.KillerEntityType = GetCombatEntityType(data.KillerEntity);
			}

			//If the killer is a SAM Site
			if (data.KillerEntity == null && data.HitInfo != null && data.HitInfo.Initiator != null &&
			    data.HitInfo.Initiator.ShortPrefabName.StartsWith("sam_site_turret_deployed"))
			{
				data.KillerEntity = data.VictimEntity.lastAttacker ?? data.HitInfo.Initiator;
				// data.KillerEntity = data.HitInfo.Initiator;
				data.KillerEntityType = CombatEntityType.SAM;
			}

			// Get previous attacker when bleeding out
			if (data.VictimEntityType == CombatEntityType.Player &&
				(data.DamageType == DamageType.Bleeding || data.HitInfo == null))
			{
				var userId = data.VictimEntity.ToPlayer().userID;

				if (_previousAttack.ContainsKey(userId))
				{
					var attack = _previousAttack[userId];
					data.KillerEntity = attack.Attacker;
					data.KillerEntityType = GetCombatEntityType(data.KillerEntity);

					// Restore previous hitInfo for weapon determination
					if (attack.HitInfo != null)
						data.HitInfo = attack.HitInfo;

					// Use previous damagetype if this is a self inflicted death,
					// so falling to death etc. is also shown when wounded and bleeding out
					if (data.KillerEntity == null || data.KillerEntity == data.VictimEntity)
						data.DamageType = attack.DamageType;
					else
						data.DamageType = DamageType.Bleeding;
				}
			}

			if (data.KillerEntityType != CombatEntityType.None && data.KillerEntity != null)
			{
				// Workaround for deaths caused by flamethrower or rocket fire 
				var flame = data.KillerEntity.gameObject.GetComponent<Flame>();
				if (flame != null && flame.Initiator != null)
				{
					data.KillerEntity = flame.Initiator;
					data.KillerEntityType = CombatEntityType.Player;
					return;
				}
			}

			// Bradley kill with main cannon
			if (data.HitInfo?.WeaponPrefab?.ShortPrefabName == "maincannonshell")
			{
				data.KillerEntityType = CombatEntityType.Bradley;
				return;
			}

			if (data.HitInfo?.WeaponPrefab?.ShortPrefabName?.StartsWith("rocket_heli") ?? false)
			{
				data.KillerEntityType = CombatEntityType.Helicopter;
				return;
			}
			
			// Vehicle Kills
			if ((data.KillerEntityType == CombatEntityType.Player
				&& data.DamageType == DamageType.Generic
				&& data.KillerEntity.ToPlayer().isMounted) ||
				(data.KillerEntityType == CombatEntityType.Train &&
				 data.DamageType == DamageType.Generic &&
				 data.VictimEntityType == CombatEntityType.Player))
			{
				data.DamageType = DamageType.Collision;
				return;
			}
		}

		private struct AttackInfo
		{
			public HitInfo HitInfo { get; set; }
			public DamageType DamageType { get; set; }
			public BaseEntity Attacker { get; set; }
		}

		private class Flame : MonoBehaviour
		{
			public FlameSource Source { get; set; }
			public BaseEntity SourceEntity { get; set; }
			public BaseEntity Initiator { get; set; }

			public enum FlameSource
			{
				Flamethrower,
				IncendiaryProjectile
			}
		}

		#endregion

		#region Weapons

		private string GetCustomizedWeaponName(DeathData deathData)
		{
			var name = GetWeaponName(deathData);

			if (string.IsNullOrEmpty(name))
				return null;

			if (!_configuration.Translations.Weapons.ContainsKey(name))
			{
				_configuration.Translations.Weapons.Add(name, name);
				Config.WriteObject(_configuration);
			}

			return _configuration.Translations.Weapons[name];
		}

		private string GetWeaponName(DeathData deathData)
		{
			if (deathData.HitInfo == null)
				return null;

			Item item = deathData.HitInfo.Weapon?.GetItem();
			/*var parentEntity = hitInfo.Weapon?.GetParentEntity();
			Item item = null;

			if (parentEntity is BasePlayer)
			{
				(parentEntity as BasePlayer).inventory.FindItemUID(hitInfo.Weapon.ownerItemUID);
			}
			else if (parentEntity is ContainerIOEntity)
			{
				(parentEntity as ContainerIOEntity).inventory.FindItemByUID(hitInfo.Weapon.ownerItemUID);
			}*/

			if (item != null)
				return item.info.displayName.english;

			string prefab = null;
            try {
                prefab = deathData.HitInfo.Initiator?.GetComponent<Flame>()?.SourceEntity?.ShortPrefabName ??
						 deathData.HitInfo.WeaponPrefab?.ShortPrefabName;
            }
            catch (Exception e)
            {
                LogOutput($"Exception in GetWeaponName: {e.ToString()}");
                prefab = null;
            }

			if (prefab != null)
			{
				if (_weaponPrefabs.TryGetValue( prefab, out string weaponName ))
					return weaponName;

				return prefab;
			}
			
			//Added by Terceran 20250215. Ballistas are a special case because they are considered a vehicle and
			//not a weapon.
			if (deathData.HitInfo.ProjectilePrefab?.name?.StartsWith("ballista") ?? false)
			{
				return "Ballista";
			}

			// Vehicles are the only thing we classify as a weapon, while not being classified as such by the game.
			if (deathData.DamageType == DamageType.Collision)
			{
				return "Vehicle";
			}

			return null;
		}

		private string[] GetCustomizedAttachmentNames(HitInfo info)
		{
			var items = info?.Weapon?.GetItem()?.contents?.itemList;

			if (items == null)
			{
				return Array.Empty<string>();
			}

			return items.Select(i => GetCustomizedAttachmentName(i.info.displayName.english)).ToArray();
		}

		private string GetCustomizedAttachmentName(string name)
		{
			if (!_configuration.Translations.Attachments.ContainsKey(name))
			{
				_configuration.Translations.Attachments.Add(name, name);
				Config.WriteObject(_configuration);
			}

			return _configuration.Translations.Attachments[name];
		}

		#endregion

		#region Bodyparts

		private string GetCustomizedBodypartName(HitInfo hitInfo)
		{
			var name = GetBodypartName(hitInfo);

			if (string.IsNullOrEmpty(name))
				return null;

			if (!_configuration.Translations.Bodyparts.ContainsKey(name))
			{
				_configuration.Translations.Bodyparts.Add(name, name);
				Config.WriteObject(_configuration);
			}

			return _configuration.Translations.Bodyparts[name];
		}

		private string GetBodypartName(HitInfo hitInfo)
		{
			var hitArea = hitInfo?.boneArea ?? (HitArea) (-1);
			return (int) hitArea == -1 ? "Body" : hitArea.ToString();
		}

		#endregion

		#region Helper

        public bool ShowPatrolHeliTags()
        {
            return (_configuration.ShowPatrolHeliTagsInConsole || _configuration.ShowPatrolHeliTagsInChat || _configuration.ShowPatrolHeliTagsInNotify || _configuration.ShowPatrolHeliTagsInUINotify);
        }


        public bool ShowBradleyTags()
        {
            return (_configuration.ShowBradleyTagsInConsole || _configuration.ShowBradleyTagsInChat || _configuration.ShowBradleyTagsInNotify || _configuration.ShowBradleyTagsInUINotify);
        }


		[ChatCommand("dn")]
        private void CmdDeathNotes(BasePlayer player, string command, string[] args)
        {
            if (_configuration.CanPlayerUseDNCommand == false)
                return;

            if (args.Length <= 0)
            {
                bool isHidden = permission.UserHasPermission(player.UserIDString, CantSeePermission);
                if (isHidden)
                {
                    permission.RevokeUserPermission(player.UserIDString, CantSeePermission);
                    Player.Message(player, $"<color=#80D000>Death Notes</color> have been <color=#00FF00>ENABLED</color>");
                }
                else
                {
                    permission.GrantUserPermission(player.UserIDString, CantSeePermission, this);
                    Player.Message(player, $"<color=#80D000>Death Notes</color> have been <color=#FF0000>DISABLED</color>");
                }
            }
            else
            {
                
                string arg = args[0].ToLower();
                if (args[0] == "teamonly")
                {
                    bool isTeamOnly = permission.UserHasPermission(player.UserIDString, TeamOnlyPermission);
                    if (isTeamOnly)
                    {
                        permission.RevokeUserPermission(player.UserIDString, TeamOnlyPermission);
                        Player.Message(player, $"<color=#80D000>Death Notes Team Only</color> view has been <color=#FF0000>DISABLED</color>");
                    }
                    else {
                        permission.GrantUserPermission(player.UserIDString, TeamOnlyPermission, this);
                        Player.Message(player, $"<color=#80D000>Death Notes Team Only</color> view has been <color=#00FF00>ENABLED</color>");
                    }
                }                
            }
        }

		//Useful for debugging
		private static void LogDebug(string text)
		{
			if (_instance._configuration.DebugMode)
			{
				if (BasePlayer.activePlayerList.Count >= 1)
				{
					BasePlayer.activePlayerList[0].ConsoleMessage($"<color=orange>{text}</color>");
				}
			}
		}


        //Checks to see if the player has the specified Death Message permissions 
        private bool PlayersPassesPermsCheck(string playerIDString, HashSet<string> displayPerms)
        {
            if (displayPerms == null || displayPerms.Count <= 0)
                return true;

            string tmpPerm;
            foreach (string perm in displayPerms)
            {
                tmpPerm = perm.Trim().ToLower();
                if (!tmpPerm.StartsWith("deathnotes.") || tmpPerm.Equals("deathnotes.cansee") || tmpPerm.Equals("deathnotes.cantsee") || tmpPerm.Equals("deathnotes.suppress") || tmpPerm.Equals("deathnotes.seeteamonly"))
                    continue;
                
                if (permission.UserHasPermission(playerIDString, tmpPerm))
                    return true;
            }
            return false;
        }


        //Indicates if either the attacker or the victim in the DeathData parameter are on the same team as the supplied player
        private bool ArePlayersOnSameTeam(BasePlayer player, DeathData data)
        {
            if (player == null)
                return false;

            if ((data.KillerEntity is BasePlayer killer && killer != null) && (killer.userID == player.userID || (killer.currentTeam != 0 && player.currentTeam == killer.currentTeam)))
                return true;

            if ((data.VictimEntity is BasePlayer victim && victim != null) && ((victim.userID == player.userID) || (victim.currentTeam != 0 && player.currentTeam == victim.currentTeam)))
                return true;

            return false;
        }

		//Primarily for Rustcord interoperability, this indicates whether or not the death was PVP caused, or not.
		private bool IsDeathPVP(int inVictimType, int inKillerType)
        {
			CombatEntityType victimType = (CombatEntityType)inVictimType;
			CombatEntityType killerType = (CombatEntityType)inKillerType;
			return ((victimType == CombatEntityType.Player && (killerType == CombatEntityType.Player || 
										 killerType == CombatEntityType.Trap || killerType == CombatEntityType.Turret ||
										 killerType == CombatEntityType.Barricade ||
										 killerType == CombatEntityType.ExternalWall ||
										 killerType == CombatEntityType.HeatSource ||
 									     killerType == CombatEntityType.Cactus ||
 									     killerType == CombatEntityType.Fire || killerType == CombatEntityType.Lock ||
                                         killerType == CombatEntityType.Other || killerType == CombatEntityType.None ||
										 killerType == CombatEntityType.SAM)));
		}

		//Primarily for Rustcord interoperability, this indicates whether or not the death was animal caused, or not.
		private bool IsDeathAnimal(int inVictimType, int inKillerType)
        {
			CombatEntityType victimType = (CombatEntityType)inVictimType;
			CombatEntityType killerType = (CombatEntityType)inKillerType;
			return (((victimType == CombatEntityType.Animal || victimType == CombatEntityType.BeeSwarm ||
						 victimType == CombatEntityType.Shark) && killerType == CombatEntityType.Player) ||
                         (victimType == CombatEntityType.Player && (killerType == CombatEntityType.Animal ||
						 killerType == CombatEntityType.BeeSwarm || killerType == CombatEntityType.Shark)));
		}

		//Primarily for Rustcord interoperability, this indicates whether or not the death was vehicle caused, or not.
		private bool IsDeathVehicle(int inVictimType, int inKillerType)
        {
			CombatEntityType victimType = (CombatEntityType)inVictimType;
			CombatEntityType killerType = (CombatEntityType)inKillerType;
			return ((victimType == CombatEntityType.Player && (killerType == CombatEntityType.Helicopter ||
										      killerType == CombatEntityType.Bradley || killerType == CombatEntityType.Minicopter ||
                                              killerType == CombatEntityType.ScrapTransportHelicopter ||
										      killerType == CombatEntityType.AttackHelicopter ||
											  killerType == CombatEntityType.Drone ||
											  killerType == CombatEntityType.ExcavatorArm ||
											  killerType == CombatEntityType.Train ||
											  killerType == CombatEntityType.CarShredder ||
											  killerType == CombatEntityType.MagnetCrane ||
											  killerType == CombatEntityType.Elevator ||
											  killerType == CombatEntityType.HotAirBalloon ||
											  killerType == CombatEntityType.BatteringRam ||
                                              killerType == CombatEntityType.Tugboat ||
                                              killerType == CombatEntityType.CargoShip)) ||
                         					  ((victimType == CombatEntityType.Minicopter ||
											  victimType == CombatEntityType.ScrapTransportHelicopter ||
											  victimType == CombatEntityType.AttackHelicopter ||
 											  victimType == CombatEntityType.ExcavatorArm ||
										      victimType == CombatEntityType.CarShredder ||
											  victimType == CombatEntityType.MagnetCrane ||
											  victimType == CombatEntityType.Elevator ||
                          					  victimType == CombatEntityType.HotAirBalloon ||
											  victimType == CombatEntityType.BatteringRam ||
                                              victimType == CombatEntityType.Tugboat ||
                          					  victimType == CombatEntityType.CargoShip ||
                          					  victimType == CombatEntityType.Helicopter ||
											  victimType == CombatEntityType.Drone ||
											  victimType == CombatEntityType.Bradley) && (killerType == CombatEntityType.Player)));
		}

		//Primarily for Rustcord interoperability, this indicates whether or not the death was NPC caused, or not.
		private bool IsDeathNPC(int inVictimType, int inKillerType)
        {
			CombatEntityType victimType = (CombatEntityType)inVictimType;
			CombatEntityType killerType = (CombatEntityType)inKillerType;
			return ((victimType == CombatEntityType.Player && (killerType == CombatEntityType.Murderer ||
											  killerType == CombatEntityType.Scientist ||
											  killerType == CombatEntityType.Sentry ||
                                              killerType == CombatEntityType.ScarecrowNPC ||
											  killerType == CombatEntityType.TunnelDweller ||
											  killerType == CombatEntityType.UnderwaterDweller ||
                                              killerType == CombatEntityType.ZombieNPC ||
											  killerType == CombatEntityType.GingerbreadNPC ||
  											  killerType == CombatEntityType.HeavyScientist ||
  											  killerType == CombatEntityType.CustomNPC ||
                                              killerType == CombatEntityType.NPCShopKeeper)) ||
											  ((victimType == CombatEntityType.Murderer ||
											  victimType == CombatEntityType.Scientist ||
                                              victimType == CombatEntityType.Sentry ||
											  victimType == CombatEntityType.ScarecrowNPC ||
                                              victimType == CombatEntityType.TunnelDweller ||
											  victimType == CombatEntityType.UnderwaterDweller ||
                                              victimType == CombatEntityType.ZombieNPC ||
											  victimType == CombatEntityType.GingerbreadNPC ||
                                              victimType == CombatEntityType.HeavyScientist ||
                                              victimType == CombatEntityType.CustomNPC ||
											  victimType == CombatEntityType.NPCShopKeeper) &&
                                              (killerType == CombatEntityType.Player)));
		}

        private static string GetPositionString(Vector3 position)
        {
            if (position == null)
                return "NULL";
            return $"({position.x:F2},{position.y:F2},{position.z:F2})";
        }

		private static string GetDistance(float meters, bool useMetric)
		{
			double value = Math.Round(useMetric ? meters : meters * 3.28f, 1);
			string unit = value == 1
				? _instance.lang.GetMessage("Distance Unit Singular", _instance)
				: _instance.lang.GetMessage("Distance Unit Plural", _instance);

			return $"{value} {unit}";
		}

		private static string ApplyVariableFormat(string text, string variableName)
		{
			if (_instance._configuration.VariableFormats.ContainsKey(variableName))
			{
				var format = _instance._configuration.VariableFormats[variableName];
				text = format.Replace("{value}", text);
			}

			return text;
		}

		private static string InsertPlaceholderValues(string text, Dictionary<string, string> values)
		{
			foreach (var kvp in values)
			{
				string value = ApplyVariableFormat(kvp.Value, kvp.Key);
				if (string.IsNullOrEmpty(kvp.Value))
				{
					text = text.Replace($"{{{kvp.Key}}}", string.Empty);
				}
				else if (_instance._configuration.VariableColors.ContainsKey(kvp.Key))
				{
					var color = _instance._configuration.VariableColors[kvp.Key];
					text = text.Replace($"{{{kvp.Key}}}", $"<color={color}>{value}</color>");
					color = null;
				}
				else
				{
					text = text.Replace($"{{{kvp.Key}}}", value);
				}
			}

			return text;
		}

		private static string HumanizePascalCase(string text)
		{
			if (string.IsNullOrEmpty(text))
				return string.Empty;

			var sb = new StringBuilder();

			foreach (char c in text)
			{
				if (char.IsUpper(c) && sb.Length != 0 && !char.IsUpper(sb[sb.Length - 1]))
					sb.Append(" ");

				sb.Append(c);
			}

			return sb.ToString();
		}

		private string StripRichText(string text)
		{
			if (string.IsNullOrEmpty(text))
				return string.Empty;

			text = _colorTagRegex.Replace(text, string.Empty);
			text = _sizeTagRegex.Replace(text, string.Empty);

			foreach (var richTextLiteral in _richTextLiterals)
				text = text.Replace(richTextLiteral, string.Empty);

			return text;
		}

		private static bool MatchesCombatEntityType(CombatEntityType combatEntityType, string text)
		{
			if (combatEntityType == CombatEntityType.None && text == "-")
				return true;

			return combatEntityType.ToString().Equals(text);
		}

		private static bool MatchesDamageType(DamageType damageType, string text)
		{
			return damageType.ToString().Equals(text);
		}

		#endregion

		#region Configuration

		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>
			{
				["Distance Unit Singular"] = "meter",
				["Distance Unit Plural"] = "meters"
			}, this);
		}

		protected override void LoadDefaultConfig() => PrintWarning("Generating new configuration file...");

		private sealed class PluginConfiguration
		{
			[JsonProperty("Translations")] public Translation Translations = new Translation();

			[JsonProperty("Variable Formats")] public Dictionary<string, string> VariableFormats =
				new Dictionary<string, string>
				{
					["attachments"] = " ({value})"
				};

			[JsonProperty("Variable Colors")] public Dictionary<string, string> VariableColors =
				new Dictionary<string, string>
				{
					["killer"] = "#C4FF00",
					["victim"] = "#C4FF00",
					["weapon"] = "#C4FF00",
					["attachments"] = "#C4FF00",
					["distance"] = "#C4FF00",
					["owner"] = "#C4FF00",
                    ["position"] = "#C4FF00",
                    ["mapgrid"] = "#C4FF00"
				};

			[JsonProperty("Chat Format")]
			public string ChatFormat = "<color=#838383>[<color=#80D000>DeathNotes</color>] {message}</color>";

			[JsonProperty("Chat Icon (SteamID)")] public string ChatIcon = "76561198077847390";
			
			[JsonProperty("Debug Mode Enabled")] public bool DebugMode = false;

			[JsonProperty("Notify Message Type")] public int NotifyMessageType = 0;
			
			[JsonProperty("UINotify Message Type")] public int UINotifyMessageType = 0;

            [JsonProperty("Can Player Use /dn Command")] public bool CanPlayerUseDNCommand = true;

            [JsonProperty("Patrol Heli Tags Display Permissions (Display For All Players If Empty)")] public HashSet<string> PatrolHeliDisplayPerms = new HashSet<string>();
			[JsonProperty("Show Patrol Heli Tags in Console")] public bool ShowPatrolHeliTagsInConsole = true;
			[JsonProperty("Show Patrol Heli Tags in Chat")] public bool ShowPatrolHeliTagsInChat = true;
			[JsonProperty("Show Patrol Heli Tags in Notify")] public bool ShowPatrolHeliTagsInNotify = false;
			[JsonProperty("Show Patrol Heli Tags in UINotify")] public bool ShowPatrolHeliTagsInUINotify = false;
			[JsonProperty("Patrol Helicopter Tag Message")]
			public string PatrolHeliTagMessage = "{killer} has tagged the {victim} with their {weapon} over a distance of {distance}.";
		   
            [JsonProperty("Bradley APC Tags Display Permissions (Display For All Players If Empty)")] public HashSet<string> BradleyDisplayPerms = new HashSet<string>();
			[JsonProperty("Show Bradley APC Tags in Console")] public bool ShowBradleyTagsInConsole = true;
			[JsonProperty("Show Bradley APC Tags in Chat")] public bool ShowBradleyTagsInChat = true;
			[JsonProperty("Show Bradley APC Tags in Notify")] public bool ShowBradleyTagsInNotify = false;
			[JsonProperty("Show Bradley APC Tags in UINotify")] public bool ShowBradleyTagsInUINotify = false;
			[JsonProperty("Bradley Tag Message")]
			public string BradleyTagMessage = "{killer} has tagged the {victim} with their {weapon} over a distance of {distance}.";

			[JsonProperty("Message Broadcast Radius (in meters)")] public int MessageRadius = -1;

			[JsonProperty("Use Metric Distance")] public bool UseMetricDistance = true;

			[JsonProperty("Require Permission (deathnotes.cansee)")] public bool RequirePermission = false;

			public void LoadDefaults()
			{
				Translations.Names ??= new Dictionary<string, string>();
				Translations.Bodyparts ??= new Dictionary<string, string>();
				Translations.Weapons ??= new Dictionary<string, string>();
				Translations.Attachments ??= new Dictionary<string, string>();
				Translations.Messages ??= new List<DeathMessage>();
				
				Translations.Names.TryAdd("Chicken", "Chicken");
				Translations.Names.TryAdd("Boar", "Boar");
				Translations.Names.TryAdd("Stag", "Stag");
				Translations.Names.TryAdd("Bear", "Bear");
				Translations.Names.TryAdd("Wolf", "Wolf");
				Translations.Names.TryAdd("Simple Shark", "Shark");
				Translations.Names.TryAdd("Scientist", "Scientist");
				Translations.Names.TryAdd("Heavy Scientist", "Heavy Scientist");
				Translations.Names.TryAdd("HeavyScientist", "Heavy Scientist");
				Translations.Names.TryAdd("scientistnpc_heavy", "Heavy Scientist");
				Translations.Names.TryAdd("Helicopter", "Patrol Helicopter");
				Translations.Names.TryAdd("Bradley APC", "Bradley APC");
				Translations.Names.TryAdd("Polarbear", "Polar Bear");
				Translations.Names.TryAdd("Code Lock", "Code Lock");
				Translations.Names.TryAdd("Fire", "Fire");
				Translations.Names.TryAdd("Sentry", "Sentry");
				Translations.Names.TryAdd("Sam Site", "SAM Site");
				Translations.Names.TryAdd("ScarecrowNPC", "Scarecrow");
				Translations.Names.TryAdd("Tunnel Dweller", "Tunnel Dweller");
				Translations.Names.TryAdd("Barbed Wooden Barricade", "Barbed Wooden Barricade");
				Translations.Names.TryAdd("Barricade", "Barricade");
				Translations.Names.TryAdd("Auto Turret", "Auto Turret");
				Translations.Names.TryAdd("Patrol Helicopter", "Patrol Helicopter");
				Translations.Names.TryAdd("GRAF", "GRAF");
				Translations.Names.TryAdd("Underwater Dweller", "Underwater Dweller");
				Translations.Names.TryAdd("Campfire", "Campfire");
				Translations.Names.TryAdd("Wolf2", "Wolf");
				Translations.Names.TryAdd("Horse", "Horse");
				Translations.Names.TryAdd("Landmine", "Landmine");
				Translations.Names.TryAdd("Tesla Coil", "Tesla Coil");
				Translations.Names.TryAdd("Gun Trap", "Shotgun Trap");
				Translations.Names.TryAdd("Flame Turret", "Flame Turret");
				Translations.Names.TryAdd("GingerbreadNPC", "Gingerbread Man");
				Translations.Names.TryAdd("ScrapTransportHelicopter", "Scrap Transport Helicopter");
				Translations.Names.TryAdd("AttackHelicopter", "Attack Helicopter");
				Translations.Names.TryAdd("Minicopter", "Minicopter");
				Translations.Names.TryAdd("Metal Barricade", "Metal Barricade");
				Translations.Names.TryAdd("ZombieNPC", "Zombie");
				Translations.Names.TryAdd("Bear Trap", "Bear Trap");
				Translations.Names.TryAdd("Custom Bradley", "Custom Bradley");
				Translations.Names.TryAdd("Farmable Animal", "Farm Animal");
				Translations.Names.TryAdd("BeeSwarm", "Bee Swarm");
				Translations.Names.TryAdd("Custom Animal Npc", "Custom Animal NPC");
				Translations.Names.TryAdd("Zombie", "Zombie");
				Translations.Names.TryAdd("Shark", "Shark");
				Translations.Names.TryAdd("Wooden Floor Spike Cluster", "Wooden Floor Spike Cluster");
				Translations.Names.TryAdd("Crocodile", "Crocodile");
				Translations.Names.TryAdd("Snake Hazard", "Snake");
				Translations.Names.TryAdd("Panther", "Panther");
				Translations.Names.TryAdd("Tiger", "Tiger");
				Translations.Names.TryAdd("Ridable Horse", "Horse");
				Translations.Names.TryAdd("NPCShopKeeper", "Waterwell Shopkeeper");
				Translations.Names.TryAdd("Giant Excavator Arm", "Giant Excavator Arm");
				Translations.Names.TryAdd("Drone", "Drone");

				Translations.Bodyparts.TryAdd("Body", "Body");
				Translations.Bodyparts.TryAdd("Leg", "Leg");
				Translations.Bodyparts.TryAdd("Chest", "Chest");
				Translations.Bodyparts.TryAdd("Stomach", "Stomach");
				Translations.Bodyparts.TryAdd("Head", "Head");
				Translations.Bodyparts.TryAdd("Arm", "Arm");
				Translations.Bodyparts.TryAdd("Hand", "Hand");
				Translations.Bodyparts.TryAdd("Foot", "Foot");
				
				Translations.Weapons.TryAdd("Salvaged Sword", "Salvaged Sword");
				Translations.Weapons.TryAdd("Revolver", "Revolver");
				Translations.Weapons.TryAdd("Salvaged Cleaver", "Salvaged Cleaver");
				Translations.Weapons.TryAdd("Mace", "Mace");
				Translations.Weapons.TryAdd("Assault Rifle", "Assault Rifle");
				Translations.Weapons.TryAdd("Double Barrel Shotgun", "Double Barrel Shotgun");
				Translations.Weapons.TryAdd("Bolt Action Rifle", "Bolt Action Rifle");
				Translations.Weapons.TryAdd("M92 Pistol", "M92 Pistol");
				Translations.Weapons.TryAdd("Custom SMG", "Custom SMG");
				Translations.Weapons.TryAdd("LR-300 Assault Rifle", "LR-300 Assault Rifle");
				Translations.Weapons.TryAdd("Timed Explosive Charge", "Timed Explosive Charge (C4)");
				Translations.Weapons.TryAdd("Pump Shotgun", "Pump Shotgun");
				Translations.Weapons.TryAdd("Prototype 17", "Prototype 17");
				Translations.Weapons.TryAdd("Hunting Bow", "Hunting Bow");
				Translations.Weapons.TryAdd("Thompson", "Thompson");
				Translations.Weapons.TryAdd("M39 Rifle", "M39 Rifle");
				Translations.Weapons.TryAdd("Hatchet", "Hatchet");
				Translations.Weapons.TryAdd("hatchet.entity", "Hatchet");
				Translations.Weapons.TryAdd("M4 Shotgun", "M4 Shotgun");
				Translations.Weapons.TryAdd("HMLMG", "HMLMG");
				Translations.Weapons.TryAdd("l96.entity", "L96 Rifle");
				Translations.Weapons.TryAdd("L96 Rifle", "L96 Rifle");
				Translations.Weapons.TryAdd("m249.entity", "M249");
				Translations.Weapons.TryAdd("M249", "M249");
				Translations.Weapons.TryAdd("Pickaxe", "Pickaxe");
				Translations.Weapons.TryAdd("Bone Knife", "Bone Knife");
				Translations.Weapons.TryAdd("Semi-Automatic Pistol", "Semi-Automatic Pistol");
				Translations.Weapons.TryAdd("Stone Hatchet", "Stone Hatchet");
                Translations.Weapons.TryAdd("stonehatchet.entity", "Stone Hatchet");
				Translations.Weapons.TryAdd("Rock", "Rock");
				Translations.Weapons.TryAdd("Skinning Knife", "Skinning Knife");
				Translations.Weapons.TryAdd("Crossbow", "Crossbow");
				Translations.Weapons.TryAdd("Combat Knife", "Combat Knife");
				Translations.Weapons.TryAdd("Semi-Automatic Rifle", "Semi-Automatic Rifle");
				Translations.Weapons.TryAdd("MP5A4", "MP5A4");
				Translations.Weapons.TryAdd("Jackhammer", "Jackhammer");
				Translations.Weapons.TryAdd("SKS", "SKS");
				Translations.Weapons.TryAdd("militaryflamethrower.entity", "Military Flamethrower");
				Translations.Weapons.TryAdd("High Velocity Rocket", "High Velocity Rocket");
				Translations.Weapons.TryAdd("Spas-12 Shotgun", "Spas-12 Shotgun");
				Translations.Weapons.TryAdd("knife.combat.entity", "Combat Knife");
				Translations.Weapons.TryAdd("2module_car_spawned.entity", "2-module Vehicle");
				Translations.Weapons.TryAdd("car_chassis_2module.entity", "2-module Vehicle");
				Translations.Weapons.TryAdd("car_2mod_01", "2-module Vehicle");
				Translations.Weapons.TryAdd("car_2mod_02", "2-module Vehicle");
				Translations.Weapons.TryAdd("car_2mod_03", "2-module Vehicle");
				Translations.Weapons.TryAdd("car_2mod_04", "2-module Vehicle");
				Translations.Weapons.TryAdd("car_2mod_05", "2-module Vehicle");
				Translations.Weapons.TryAdd("car_2mod_06", "2-module Vehicle");
				Translations.Weapons.TryAdd("car_2mod_07", "2-module Vehicle");
				Translations.Weapons.TryAdd("car_2mod_08", "2-module Vehicle");
				Translations.Weapons.TryAdd("3module_car_spawned.entity", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_chassis_3module.entity", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_01", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_02", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_03", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_04", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_05", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_06", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_07", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_08", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_09", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_10", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_11", "3-module Vehicle");
				Translations.Weapons.TryAdd("car_3mod_12", "3-module Vehicle");
				Translations.Weapons.TryAdd("4module_car_spawned.entity", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_chassis_4module.entity", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_4mod_01", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_4mod_02", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_4mod_03", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_4mod_04", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_4mod_05", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_4mod_06", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_4mod_07", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_4mod_08", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_4mod_09", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_4mod_10", "4-module Vehicle");
				Translations.Weapons.TryAdd("car_4mod_11", "4-module Vehicle");
				Translations.Weapons.TryAdd("motorbike", "Motorbike");
				Translations.Weapons.TryAdd("motorbike_sidecar", "Motorbike with Sidecar");
				Translations.Weapons.TryAdd("pedalbike", "Bicycle");
				Translations.Weapons.TryAdd("pedaltrike", "Tricycle");
				Translations.Weapons.TryAdd("snowmobile", "Snowmobile");
				Translations.Weapons.TryAdd("F1 Grenade", "F1 Grenade");
				Translations.Weapons.TryAdd("Satchel Charge", "Satchel Charge");
				Translations.Weapons.TryAdd("Wooden Spear", "Wooden Spear");
				Translations.Weapons.TryAdd("Stone Spear", "Stone Spear");
				Translations.Weapons.TryAdd("Compound Bow", "Compound Bow");
				Translations.Weapons.TryAdd("Machete", "Machete");
				Translations.Weapons.TryAdd("Pitchfork", "Pitchfork");
				Translations.Weapons.TryAdd("Stone Pickaxe", "Stone Pickaxe");
				Translations.Weapons.TryAdd("stone_pickaxe.entity", "Stone Pickaxe");
				Translations.Weapons.TryAdd("Legacy bow", "Legacy Bow");
				Translations.Weapons.TryAdd("Rocket", "Rocket");
				Translations.Weapons.TryAdd("Incendiary Rocket", "Incendiary Rocket");
				Translations.Weapons.TryAdd("Salvaged Axe", "Salvaged Axe");
				Translations.Weapons.TryAdd("Torch", "Torch");
				Translations.Weapons.TryAdd("pickaxe.entity", "Pickaxe");
				Translations.Weapons.TryAdd("Salvaged Hammer", "Salvaged Hammer");
				Translations.Weapons.TryAdd("40mm_grenade_he", "40mm HE Grenade");
				Translations.Weapons.TryAdd("Minigun", "Minigun");
				Translations.Weapons.TryAdd("Assault Rifle - ICE", "Ice Assault Rifle");
				Translations.Weapons.TryAdd("Python Revolver", "Python Revolver");
				Translations.Weapons.TryAdd("Nailgun", "Nailgun");
				Translations.Weapons.TryAdd("Salvaged Icepick", "Salvaged Icepick");
				Translations.Weapons.TryAdd("Longsword", "Longsword");
				Translations.Weapons.TryAdd("Waterpipe Shotgun", "Waterpipe Shotgun");
				Translations.Weapons.TryAdd("Chainsaw", "Chainsaw");
				Translations.Weapons.TryAdd("workcart.entity", "Work Cart");
				Translations.Weapons.TryAdd("locomotive.entity", "Locomotive");
				Translations.Weapons.TryAdd("workcart_aboveground.entity", "Above Ground Work Cart");
				Translations.Weapons.TryAdd("Paddle", "Paddle");
				Translations.Weapons.TryAdd("workcart_aboveground2.entity", "Above Ground Work Cart");
				Translations.Weapons.TryAdd("hammer_salvaged.entity", "Salvaged Hammer");
				Translations.Weapons.TryAdd("Multiple Grenade Launcher", "Multiple Grenade Launcher");
				Translations.Weapons.TryAdd("minigun.entity", "Minigun");
				Translations.Weapons.TryAdd("rocket_mlrs", "MLRS Rocket");
				Translations.Weapons.TryAdd("Eoka Pistol", "Eoka Pistol");
				Translations.Weapons.TryAdd("High Caliber Revolver", "High Caliber Revolver");
				Translations.Weapons.TryAdd("Abyss Assault Rifle", "Abyss Assault Rifle");
				Translations.Weapons.TryAdd("Blunderbuss", "Blunderbuss");
				Translations.Weapons.TryAdd("Sickle", "Sickle");
				Translations.Weapons.TryAdd("Butcher Knife", "Butcher Knife");
				Translations.Weapons.TryAdd("pitchfork.entity", "Pitchfork");
				Translations.Weapons.TryAdd("Snowball Gun", "Snowball Gun");
				Translations.Weapons.TryAdd("vampirestake.entity", "Vampire Stake");
				Translations.Weapons.TryAdd("Baseball Bat", "Baseball Bat");
				Translations.Weapons.TryAdd("Handmade SMG", "Handmade SMG");
				Translations.Weapons.TryAdd("Beancan Grenade", "Beancan Grenade");
				Translations.Weapons.TryAdd("Candy Cane Club", "Candy Cane Club");
				Translations.Weapons.TryAdd("Snowball", "Snowball");
				Translations.Weapons.TryAdd("Flashlight", "Flashlight");
				Translations.Weapons.TryAdd("Speargun", "Speargun");
				Translations.Weapons.TryAdd("double_shotgun.entity", "Double Barrel Shotgun");
				Translations.Weapons.TryAdd("Skull", "Skull");
				Translations.Weapons.TryAdd("Mini Crossbow", "Mini Crossbow");
				Translations.Weapons.TryAdd("Ice Assault Rifle", "Ice Assault Rifle");
				Translations.Weapons.TryAdd("Frontier Hatchet", "Frontier Hatchet");
				Translations.Weapons.TryAdd("Medieval Assault Rifle", "Medieval Assault Rifle");
				Translations.Weapons.TryAdd("boulder_explosive_deployed", "Propane Explosive Bomb");
				Translations.Weapons.TryAdd("Ballista", "Ballista");
				Translations.Weapons.TryAdd("boulder_explosive", "Propane Explosive Bomb");
				Translations.Weapons.TryAdd("boulder_mid", "Scattershot");
				Translations.Weapons.TryAdd("boulder_incendiary", "Firebomb");
				Translations.Weapons.TryAdd("Flame Thrower", "Flame Thrower");
				Translations.Weapons.TryAdd("grenade.molotov.deployed", "Molotov Cocktail");
				Translations.Weapons.TryAdd("Concrete Pickaxe", "Concrete Pickaxe");
				Translations.Weapons.TryAdd("Bone Club", "Bone Club");
				Translations.Weapons.TryAdd("scraptransporthelicopter", "Scrap Transport Helicopter");
				Translations.Weapons.TryAdd("grenade.bee.deployed", "Bee Grenade");
				Translations.Weapons.TryAdd("grenade.flashbang.deployed", "Flashbang Grenade");
				Translations.Weapons.TryAdd("Lunar New Year Spear", "Lunar New Year Spear");
				Translations.Weapons.TryAdd("boulder", "Boulder");
				Translations.Weapons.TryAdd("grenade.flashbang.deployed", "Flashbang Grenade");
				Translations.Weapons.TryAdd("Prototype Hatchet", "Prototype Hatchet");
				Translations.Weapons.TryAdd("Concrete Hatchet", "Concrete Hatchet");
				Translations.Weapons.TryAdd("Blow Pipe", "Blow Pipe");
				Translations.Weapons.TryAdd("boomerang.thrown.entity", "Boomerang");
				Translations.Weapons.TryAdd("Obsidian Bone Knife", "Obsidian Bone Knife");
				Translations.Weapons.TryAdd("Jungle Relic Assault Rifle", "Jungle Relic Assault Rifle");
				Translations.Weapons.TryAdd("Abyss Metal Pickaxe", "Abyss Metal Pickaxe");
				Translations.Weapons.TryAdd("Abyss Metal Hatchet", "Abyss Metal Hatchet");
				Translations.Weapons.TryAdd("Cultist Deer Torch", "Cultist Deer Torch");
				Translations.Weapons.TryAdd("Jungle Rock", "Jungle Rock");
				Translations.Weapons.TryAdd("ak47u.entity", "Assault Rifle");
				Translations.Weapons.TryAdd("hmlmg.entity", "HMLMG");
				Translations.Weapons.TryAdd("M15 Semi-Automatic Pistol", "M15 Semi-Automatic Pistol");
				Translations.Weapons.TryAdd("icepick_salvaged.entity", "Salvaged Icepick");
                Translations.Weapons.TryAdd("rocket_hv_rpg", "High-Velocity RPG");
                Translations.Weapons.TryAdd("Krieg Shotgun", "Krieg Shotgun");
                Translations.Weapons.TryAdd("Krieg chainsword", "Krieg Chainsword");
                Translations.Weapons.TryAdd("Medieval AR", "Medieval AR");
                Translations.Weapons.TryAdd("rocket_hv_rpg", "High-Velocity RPG");
                Translations.Weapons.TryAdd("Boomerang", "Boomerang");
                Translations.Weapons.TryAdd("Space LR-300 Assault Rifle", "Space LR-300 Assault Rifle");
                Translations.Weapons.TryAdd("rocket_basic_rpg", "RPG");
                Translations.Weapons.TryAdd("rocket_fire_rpg", "Incendiary RPG");
                Translations.Weapons.TryAdd("Prototype Pickaxe", "Prototype Pickaxe");
                Translations.Weapons.TryAdd("Sunken Combat Knife", "Sunken Combat Knife");
                Translations.Weapons.TryAdd("stonehatchet.entity", "Stone Hatchet");
                Translations.Weapons.TryAdd("spas12.entity", "Spas-12 Shotgun");
				
				Translations.Attachments.TryAdd("Weapon flashlight", "Weapon Flashlight");
				Translations.Attachments.TryAdd("Extended Magazine", "Extended Magazine");
				Translations.Attachments.TryAdd("16x Zoom Scope", "16x Zoom Scope");
				Translations.Attachments.TryAdd("Weapon Lasersight", "Weapon Lasersight");
				Translations.Attachments.TryAdd("Silencer", "Silencer");
				Translations.Attachments.TryAdd("8x Zoom Scope", "8x Zoom Scope");
				Translations.Attachments.TryAdd("Holosight", "Holosight");
				Translations.Attachments.TryAdd("Muzzle Boost", "Muzzle Boost");
				Translations.Attachments.TryAdd("Muzzle Brake", "Muzzle Brake");
				Translations.Attachments.TryAdd("Simple Handmade Sight", "Simple Handmade Sight");
				Translations.Attachments.TryAdd("Gas Compression Overdrive", "Gas Compression Overdrive");
				Translations.Attachments.TryAdd("Burst Module", "Burst Module");
				Translations.Attachments.TryAdd("Variable Zoom Scope", "Variable Zoom Scope");
				Translations.Attachments.TryAdd("Military Silencer", "Military Silencer");
				Translations.Attachments.TryAdd("Oil Filter Silencer", "Oil Filter Silencer");
				Translations.Attachments.TryAdd("Targeting Attachment", "Targeting Attachment");
				Translations.Attachments.TryAdd("Soda Can Silencer", "Soda Can Silencer");
				
				var defaultMessage = new DeathMessage("Player", "Player", "Generic", "{victim} died due to damage deflection while attempting to murder {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
				
				defaultMessage = new DeathMessage("Player", "Player", "Bullet", "{killer} shot {victim} using their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
				
				defaultMessage = new DeathMessage("Player", "Player", "Arrow", "{victim} was shot by {killer} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "Heat", "{killer} inflamed {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "*", "{killer} killed {victim} using their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "Slash", "{killer} slashed {victim} into pieces with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Animal", "*", "{killer} killed a {victim} using their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Shark", "*", "The waters are a tad safer now that {killer} killed a {victim} using their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Animal", "Bullet", "{killer} shot a {victim} using their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
				
				defaultMessage = new DeathMessage("Player", "Animal", "Arrow", "{killer} shot a {victim} using their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "ZombieNPC", "*", "{killer} fought off a spooky {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("ZombieNPC", "Player", "*", "{killer} killed {victim} and ate their brains!");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "ZombieNPC", "Bullet", "{killer} shot a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "ScarecrowNPC", "*", "{killer} fought off a spooky {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("ScarecrowNPC", "Player", "*", "{killer} scared {victim} to death!");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "ScarecrowNPC", "Bullet", "{killer} did some scaring of their own against a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "GingerbreadNPC", "*", "{killer} fought off a deliciously deadly {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("GingerbreadNPC", "Player", "*", "Death was sweet for {victim}, who was killed by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "GingerbreadNPC", "Bullet", "{killer} took a bite out of a deliciously deadly {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Scientist", "*", "{killer} did not want to be a part of the {victim}'s experiments, and did some science of their own with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Scientist", "Bullet", "{killer} did some research of their own against a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Scientist", "Arrow", "{killer} did some research of their own against a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "NPCShopKeeper", "*", "{killer} was not interested in {victim}'s inventory, and sold them pain with their {weapon} instead.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("CustomNPC", "Player", "*", "{killer} demonstrated NPC superiority to {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
				
				defaultMessage = new DeathMessage("Player", "CustomNPC", "*", "{killer} conveyed their opinion of NPCs to {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("NPCShopKeeper", "Player", "*", "{killer} became unhappy enough to end {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
				
				defaultMessage = new DeathMessage("Player", "HeavyScientist", "*", "{killer} did not want to be a part of the {victim}'s experiments, and did some science of their own with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "HeavyScientist", "Bullet", "{killer} did some research of their own against a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "HeavyScientist", "Arrow", "{killer} did some research of their own against a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Bradley", "*", "{killer} blew up the {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Helicopter", "*", "The {victim} was destroyed. Good riddance.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Helicopter", "*", "The world is a safer place now that {killer} shot down the {victim} with their {weapon}. Good riddance.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("SAM", "Helicopter", "*", "A {killer} brought down {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Cactus", "Player", "*", "{victim} got in a sticky situation with a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("SAM", "Player", "*", "A {killer} brought down {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Animal", "Player", "*", "{victim} couldn't run away from the {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Shark", "Player", "*", "Baby {killer}, doo-doo, doo-doo, doo-doo ate {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("BeeSwarm", "Player", "*", "A {killer} just reminded {victim} that they are allergic to bee stings.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "BeeSwarm", "*", "{killer} just swatted a {victim} out of existence.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Bradley", "Player", "*", "{victim} was blasted by the {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Helicopter", "Player", "*", "{victim} had no chance against the {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Trap", "Player", "*", "{victim} was reckless and ran into a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Trap", "Scientist", "*", "{victim} fell victim to {owner}'s well placed {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Trap", "HeavyScientist", "*", "{victim} fell victim to {owner}'s well placed {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Barricade", "Player", "*", "{victim} was impaled by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Turret", "Player", "*", "{owner}'s {killer} did its job, killing intruder {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Turret", "Scientist", "*", "{owner}'s {killer} did its job, killing a {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Turret", "HeavyScientist", "*", "{owner}'s {killer} did its job, killing a {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Murderer", "Player", "*", "A {killer} haunted down {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("TunnelDweller", "Player", "*", "{victim} was taken out by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "TunnelDweller", "*", "{killer} took out a {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "TunnelDweller", "Bullet", "{killer} took out a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("UnderwaterDweller", "Player", "*", "{victim} was taken out by an {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "UnderwaterDweller", "*", "{killer} took out an {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "UnderwaterDweller", "Bullet", "{killer} took out a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("ExcavatorArm", "Player", "*", "A {killer} excavated {victim} for trace amounts of precious materials. None were found.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Elevator", "Player", "*", "We all have our ups and downs, but {victim} was just squashed by an {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("HotAirBalloon", "Player", "*", "{victim} learned the hard way that a {killer} can come down as well.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("BatteringRam", "Player", "*", "{victim} forgot to dodge the {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Tugboat", "Player", "*", "{victim} forgot that a {killer} is not, in fact, friendly.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("CargoShip", "Player", "*", "{victim} was suddently introduced to industrial tonnage by the {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("CarShredder", "Player", "*", "{victim} was torn to pieces in a {killer}. It turns out they consisted of 40% recyclable materials and 60% regret.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("MagnetCrane", "Player", "*", "{victim} was magnetized and traumatized by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Scientist", "Player", "*", "A {killer} shot down {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Scientist", "*", "*", "{killer} shot down a {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("HeavyScientist", "Player", "*", "A {killer} shot down {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("HeavyScientist", "*", "*", "{killer} shot down a {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Sentry", "Player", "*", "{victim} broke the rules in a safezone and was killed by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("-", "Player", "Fall", "{victim} fell to their death. Splat!");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("HeatSource", "Player", "*", "{victim} was grilled on a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Lock", "Player", "*", "{victim} was electrocuted by {owner}'s {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Heat", "{victim} burned to death.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Hunger", "{victim} forgot to eat and hungers for life.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Thirst", "{victim} died of immense thirst.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Radiation", "{victim} had a cheery, radioactive glow. Fatal, but cheery nonetheless.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Cold", "{victim} turned into an ice statue.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Drowned", "As {victim} just found out, breathing underwater is rather difficult.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Animal", "Drowned", "As the {victim} just found out, breathing underwater is rather difficult.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "Bleeding", "{victim} bled out after being attacked by {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Animal", "Collision", "{killer} ran over a {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "Collision", "{killer} ran over {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Train", "Player", "Collision", "{victim} was stopped in their tracks as they were run over by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Minicopter", "Player", "*", "{victim} tragically perished in a {killer} crash.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("ScrapTransportHelicopter", "Player", "*", "{victim} tragically perished in a {killer} crash.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("AttackHelicopter", "Player", "*", "{victim} tragically perished in a {killer} crash.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Drone", "Player", "*", "A {killer} killed {victim} with its {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "Suicide", "{victim} had enough of life.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
			
				foreach (var msg in Translations.Messages)
				{
					if (msg.Enabled == null)
						msg.Enabled = true;
				}
				
				_instance.Config.WriteObject(this, true);
			}

			public class DeathMessage
			{
				public string KillerType { get; set; }
				public string VictimType { get; set; }
				public string DamageType { get; set; }
				
				[JsonProperty("Enabled", DefaultValueHandling = DefaultValueHandling.Include, NullValueHandling = NullValueHandling.Include)]
				public bool? Enabled { get; set; }

				[JsonIgnore]
				public bool EnabledValue => Enabled ?? true;

                [JsonProperty("Display Permissions (Display For All Players If Empty)")] public HashSet<string> DisplayPerms = new HashSet<string>();
				
				[JsonProperty("Show Kills in Console", DefaultValueHandling = DefaultValueHandling.Include, NullValueHandling = NullValueHandling.Include)]
				public bool? ShowKillsInConsole { get; set; }

				[JsonIgnore]
				public bool ShowKillsInConsoleValue => ShowKillsInConsole ?? true;

				[JsonProperty("Show Kills in Chat", DefaultValueHandling = DefaultValueHandling.Include, NullValueHandling = NullValueHandling.Include)]
				public bool? ShowKillsInChat { get; set; }

				[JsonIgnore]
				public bool ShowKillsInChatValue => ShowKillsInChat ?? true;

				[JsonProperty("Show Kills in Notify", DefaultValueHandling = DefaultValueHandling.Include, NullValueHandling = NullValueHandling.Include)]
				public bool? ShowKillsInNotify { get; set; }

				[JsonIgnore]
				public bool ShowKillsInNotifyValue => ShowKillsInNotify ?? false;

				[JsonProperty("Show Kills in UINotify", DefaultValueHandling = DefaultValueHandling.Include, NullValueHandling = NullValueHandling.Include)]
				public bool? ShowKillsInUINotify { get; set; }

				[JsonIgnore]
				public bool ShowKillsInUINotifyValue => ShowKillsInUINotify ?? false;

				public string[] Messages { get; set; }
				
				public override bool Equals(object obj)
				{
					if (obj is DeathMessage other)
					{
						return string.Equals(KillerType, other.KillerType) &&
						       string.Equals(VictimType, other.VictimType) &&
						       string.Equals(DamageType, other.DamageType);
					}
					return false;
				}
				
				public override int GetHashCode()
				{
					return (KillerType + VictimType + DamageType).GetHashCode();
				}

				public DeathMessage(string killerType, string victimType, string damageType, string message)
				{
					KillerType = killerType;
					VictimType = victimType;
					DamageType = damageType;
					Enabled = true;
                    ShowKillsInConsole = true;
                    ShowKillsInChat = true;
                    ShowKillsInNotify = false;
                    ShowKillsInUINotify = false;
					Messages = new[] {message};
				}
			}

			public class Translation
			{
				[JsonProperty("Death Messages")] public List<DeathMessage> Messages = new List<DeathMessage>();

				[JsonProperty("Names")] public Dictionary<string, string> Names = new Dictionary<string, string>();

				[JsonProperty("Bodyparts")]
				public Dictionary<string, string> Bodyparts = new Dictionary<string, string>();

				[JsonProperty("Weapons")] public Dictionary<string, string> Weapons = new Dictionary<string, string>();

				[JsonProperty("Attachments")]
				public Dictionary<string, string> Attachments = new Dictionary<string, string>();
			}
		}

		#endregion
	}
}