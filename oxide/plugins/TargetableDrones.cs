using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Rust.Ai;
using UnityEngine;
using VLB;
using static SamSite;
using HumanNpc = global::HumanNPC;

namespace Oxide.Plugins
{
    [Info("Targetable Drones", "WhiteThunder", "1.2.8")]
    [Description("Allows RC drones to be targeted by Auto Turrets and SAM Sites.")]
    internal class TargetableDrones : CovalencePlugin
    {
        #region Fields

        [PluginReference]
        private readonly Plugin Clans, Friends, DroneScaleManager, IQGuardianDrone;

        private const string PermissionUntargetable = "targetabledrones.untargetable";

        private const BaseEntity.Flags UntargetableFlag = BaseEntity.Flags.Reserved10;

        private Configuration _config;

        private readonly object True = true;

        private float? SqrScanRadius;

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermissionUntargetable, this);

            Unsubscribe(nameof(OnEntitySpawned));

            if (!_config.EnableSamTargeting)
            {
                Unsubscribe(nameof(OnSamSiteTargetScan));
                Unsubscribe(nameof(OnSamSiteTarget));
            }

            if (!_config.EnableTurretTargeting)
            {
                Unsubscribe(nameof(OnEntityEnter));
                Unsubscribe(nameof(OnTurretTarget));
                Unsubscribe(nameof(OnDroneScaled));
            }
        }

        private void OnServerInitialized()
        {
            if (_config.OnServerInitialized() && !_config.UsingDefaults)
            {
                SaveConfig();
            }

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                var drone = entity as Drone;
                if (drone == null)
                    continue;

                OnEntitySpawned(drone);
            }

            Subscribe(nameof(OnEntitySpawned));

            if (!_config.EnableTurretTargeting && !_config.NPCTargetingSettings.DamageMultiplierEnabled)
            {
                Unsubscribe(nameof(OnEntityTakeDamage));
            }
        }

        private void Unload()
        {
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                var drone = entity as Drone;
                if (drone == null || !IsDroneEligible(drone))
                    continue;

                if (_config.EnableTurretTargeting)
                {
                    TurretTargetComponent.RemoveFromDrone(this, drone);
                }

                if (_config.EnableSamTargeting)
                {
                    SAMTargetComponent.RemoveFromDrone(drone);
                }

                if (_config.NPCTargetingSettings.Enabled)
                {
                    NPCTargetComponent.RemoveFromDrone(drone);
                }
            }

            // Just in case since this is static.
            SAMTargetComponent.DroneComponents.Clear();
        }

        private void OnEntitySpawned(Drone drone)
        {
            if (!IsDroneEligible(drone))
                return;

            if (_config.EnableTurretTargeting)
            {
                TurretTargetComponent.AddToDroneIfMissing(this, drone);
            }

            if (_config.EnableSamTargeting)
            {
                SAMTargetComponent.AddToDroneIfMissing(this, drone);
            }

            if (_config.NPCTargetingSettings.Enabled)
            {
                NPCTargetComponent.AddToDrone(this, drone);
            }
        }

        // Avoid unwanted trigger interactions.
        private object OnEntityEnter(TriggerBase trigger, Drone drone)
        {
            if (trigger is PlayerDetectionTrigger)
            {
                // Only allow interaction with Laser Detectors.
                // This avoids NREs with HBHF sensors or anything unknown.
                if (trigger.GetComponentInParent<BaseEntity>() is LaserDetector)
                    return null;

                return True;
            }

            if (trigger is TargetTrigger)
            {
                // Only allow interaction with Auto Turrets.
                // This avoids NREs with flame turrets, shotgun traps, tesla coils, or anything unknown.
                if (trigger.GetComponentInParent<BaseEntity>() is AutoTurret)
                    return null;

                return True;
            }

            return null;
        }

        private static ulong GetDroneControllerOrOwnerId(Drone drone)
        {
            var controllerSteamId = drone.ControllingViewerId?.SteamId ?? 0;
            if (controllerSteamId != 0)
                return controllerSteamId;

            var droneTurret = GetDroneTurret(drone);
            if ((object)droneTurret != null)
            {
                controllerSteamId = droneTurret.ControllingViewerId?.SteamId ?? 0;
                if (controllerSteamId != 0)
                    return controllerSteamId;

                var turretOwnerId = droneTurret.OwnerID;
                if (turretOwnerId != 0)
                    return turretOwnerId;
            }

            return drone.OwnerID;
        }

        private object OnTurretTarget(AutoTurret turret, Drone drone)
        {
            if (drone == null || drone.IsDestroyed)
                return null;

            if (!ShouldTurretTargetDrone(turret, drone))
                return True;

            return null;
        }

        private void OnSamSiteTargetScan(SamSite samSite, List<ISamSiteTarget> targetList)
        {
            if (SAMTargetComponent.DroneComponents.Count == 0)
                return;

            var samSitePosition = samSite.transform.position;
            SqrScanRadius ??= Mathf.Pow(targetTypeVehicle.scanRadius, 2);

            foreach (var droneComponent in SAMTargetComponent.DroneComponents)
            {
                // Distance checking is way more efficient than collider checking, even with hundreds of drones.
                if ((samSitePosition - droneComponent.Position).sqrMagnitude <= SqrScanRadius.Value)
                {
                    targetList.Add(droneComponent);
                }
            }
        }

        private object OnSamSiteTarget(SamSite samSite, SAMTargetComponent droneComponent)
        {
            var drone = droneComponent.Drone;
            if (drone == null || drone.IsDestroyed)
                return null;

            if (!ShouldSamSiteTargetDrone(samSite, drone)
                || ExposedHooks.OnSamSiteTarget(samSite, drone) is false)
                return True;

            return null;
        }

        private void OnEntityTakeDamage(Drone drone, HitInfo hitInfo)
        {
            if (hitInfo == null)
                return;

            // Multiply damage taken by NPCs.
            var humanNpc = hitInfo.Initiator as HumanNpc;
            if ((object)humanNpc != null)
            {
                if (!_config.NPCTargetingSettings.DamageMultiplierEnabled
                    || !_config.NPCTargetingSettings.IsAllowed(humanNpc))
                    return;

                hitInfo.damageTypes.ScaleAll(_config.NPCTargetingSettings.DamageMultiplier);
                return;
            }

            // Make drone turrets retaliate against other turrets.
            var turret = hitInfo.Initiator as AutoTurret;
            if ((object)turret != null)
            {
                if (turret == null || turret.IsDestroyed)
                    return;

                // Ignore if this drone does not have a turret since it can't retaliate.
                var droneTurret = GetDroneTurret(drone);
                if (droneTurret == null)
                    return;

                // Ignore if the turret damaged its owner drone.
                if (droneTurret == turret)
                    return;

                // Ignore if the turret is not online or has an existing visible target.
                if (!droneTurret.IsOnline()
                    || droneTurret.HasTarget() && droneTurret.targetVisible)
                    return;

                var attackerDrone = GetParentDrone(turret);
                if (attackerDrone != null)
                {
                    // If the attacker turret is on a drone, target that drone.
                    droneTurret.SetTarget(attackerDrone);
                    return;
                }

                // Shoot back at the turret.
                droneTurret.SetTarget(turret);
                return;
            }
        }

        private void OnDroneScaled(Drone drone, BaseEntity rootEntity, float scale, float previousScale)
        {
            if (scale == 1 || rootEntity.IsDestroyed)
                return;

            TurretTargetComponent.AddToRootEntityIfMissing(drone, rootEntity);
        }

        #endregion

        #region Exposed Hooks

        private static class ExposedHooks
        {
            public static object OnSamSiteTarget(SamSite samSite, Drone drone)
            {
                return Interface.CallHook("OnSamSiteTarget", samSite, drone);
            }
        }

        #endregion

        #region Helper Methods

        public static void LogInfo(string message) => Interface.Oxide.LogInfo($"[Targetable Drones] {message}");
        public static void LogWarning(string message) => Interface.Oxide.LogWarning($"[Targetable Drones] {message}");
        public static void LogError(string message) => Interface.Oxide.LogError($"[Targetable Drones] {message}");

        private static bool SameTeam(ulong userId, ulong otherUserId)
        {
            return RelationshipManager.ServerInstance.FindPlayersTeam(userId)?.members.Contains(otherUserId) ?? false;
        }

        private static bool IsDroneEligible(Drone drone)
        {
            return drone is not DeliveryDrone && !drone.HasFlag(UntargetableFlag);
        }

        private static Drone GetParentDrone(BaseEntity entity)
        {
            var sphereEntity = entity.GetParentEntity() as SphereEntity;
            return sphereEntity != null ? sphereEntity.GetParentEntity() as Drone : null;
        }

        private static AutoTurret GetDroneTurret(Drone drone)
        {
            return drone.GetSlot(BaseEntity.Slot.UpperModifier) as AutoTurret;
        }

        private static void RemoveFromAutoTurretTriggers(BaseEntity entity)
        {
            if (entity.triggers == null || entity.triggers.Count == 0)
                return;

            foreach (var trigger in entity.triggers.ToArray())
            {
                if (trigger is not TargetTrigger)
                    continue;

                var autoTurret = trigger.gameObject.ToBaseEntity() as AutoTurret;
                if (autoTurret != null && autoTurret.targetTrigger == trigger)
                {
                    trigger.RemoveEntity(entity);
                }
            }
        }

        private bool IsTargetableIQGuardianDrone(Drone drone)
        {
            return IQGuardianDrone?.Call("IsValidDrone", drone) is true;
        }

        private bool IsDroneBeingControlled(Drone drone)
        {
            // Valid IQGuardian Drones are considered to be controlled.
            return drone.IsBeingControlled || IsTargetableIQGuardianDrone(drone);
        }

        private bool IsPlayerTargetExempt(ulong userId)
        {
            return userId != 0 && permission.UserHasPermission(userId.ToString(), PermissionUntargetable);
        }

        private bool IsTargetExempt(Drone drone)
        {
            return drone.isGrounded || drone.InSafeZone() || IsPlayerTargetExempt(GetDroneControllerOrOwnerId(drone));
        }

        private BaseEntity GetRootEntity(Drone drone)
        {
            return DroneScaleManager?.Call("API_GetRootEntity", drone) as BaseEntity;
        }

        private bool HasFriend(ulong userId, ulong otherUserId)
        {
            return Friends?.Call("HasFriend", userId, otherUserId) is true;
        }

        private bool SameClan(ulong userId, ulong otherUserId)
        {
            return Clans?.Call("IsClanMember", userId.ToString(), otherUserId.ToString()) is true;
        }

        private bool AreAllies(ulong userId, ulong otherUserId)
        {
            return Clans?.Call("IsAllyPlayer", userId.ToString(), otherUserId.ToString()) is true;
        }

        private bool ShouldTurretTargetDrone(AutoTurret turret, Drone drone)
        {
            // Drones are not inherently hostile.
            // TODO: Revisit this for player-deployed sentry turrets
            if (turret is NPCAutoTurret)
                return false;

            if (IsTargetExempt(drone))
                return false;

            // Don't allow a drone turret to target its parent drone.
            if (GetParentDrone(turret) == drone)
                return false;

            // Skip auth/team/friends/clan logic for drones that have no apparent controller/owner (likely NPC drones).
            var droneControllerOrOwnerId = GetDroneControllerOrOwnerId(drone);
            if (droneControllerOrOwnerId == 0)
                return true;

            // Direct authorization trumps anything else.
            if (turret.IsAuthed(droneControllerOrOwnerId))
                return false;

            // In case the owner lost authorization, don't share with team/friends/clan.
            if (turret.OwnerID == 0 || !turret.IsAuthed(turret.OwnerID))
                return true;

            if (turret.OwnerID == droneControllerOrOwnerId
                || _config.DefaultSharingSettings.Team && SameTeam(turret.OwnerID, droneControllerOrOwnerId)
                || _config.DefaultSharingSettings.Friends && HasFriend(turret.OwnerID, droneControllerOrOwnerId)
                || _config.DefaultSharingSettings.Clan && SameClan(turret.OwnerID, droneControllerOrOwnerId)
                || _config.DefaultSharingSettings.Allies && AreAllies(turret.OwnerID, droneControllerOrOwnerId))
                return false;

            return true;
        }

        private bool ShouldSamSiteTargetDrone(SamSite samSite, Drone drone)
        {
            if (samSite.staticRespawn || samSite.OwnerID == 0)
                return true;

            var droneOwnerId = GetDroneControllerOrOwnerId(drone);
            if (droneOwnerId == 0)
                return true;

            if (samSite.OwnerID == droneOwnerId
                || _config.DefaultSharingSettings.Team && SameTeam(samSite.OwnerID, droneOwnerId)
                || _config.DefaultSharingSettings.Friends && HasFriend(samSite.OwnerID, droneOwnerId)
                || _config.DefaultSharingSettings.Clan && SameClan(samSite.OwnerID, droneOwnerId)
                || _config.DefaultSharingSettings.Allies && AreAllies(samSite.OwnerID, droneOwnerId))
                return false;

            return true;
        }

        #endregion

        #region Custom Targeting

        private class NPCTargetTriggerComponent : TriggerBase
        {
            public static NPCTargetTriggerComponent AddToDrone(TargetableDrones plugin, Drone drone, GameObject host)
            {
                var component = host.AddComponent<NPCTargetTriggerComponent>();
                component._plugin = plugin;
                component._drone = drone;
                component.interestLayers = Rust.Layers.Mask.Player_Server;
                return component;
            }

            private const int LayerMask = Rust.Layers.Mask.Default
                | Rust.Layers.Mask.Vehicle_Detailed
                | Rust.Layers.Mask.World
                | Rust.Layers.Mask.Construction
                | Rust.Layers.Mask.Terrain
                | Rust.Layers.Mask.Vehicle_Large
                | Rust.Layers.Mask.Tree;

            private TargetableDrones _plugin;
            private Drone _drone;
            private Action _checkTriggerContents;

            private NPCTargetTriggerComponent()
            {
                _checkTriggerContents = CheckTriggerContents;
            }

            public void CheckTriggerContents()
            {
                if (!HasAnyEntityContents)
                {
                    CancelInvoke(_checkTriggerContents);
                    return;
                }

                foreach (var entity in entityContents)
                {
                    var humanNpc = entity as HumanNpc;
                    if (humanNpc == null)
                        continue;

                    var memory = GetMemory(entity, out var senses);
                    if (memory == null)
                        continue;

                    if (IsTargetableBy(humanNpc))
                    {
                        AddToMemory(memory, senses);
                    }
                    else
                    {
                        RemoveFromMemory(memory);
                    }
                }
            }

            public override GameObject InterestedInObject(GameObject obj)
            {
                obj = base.InterestedInObject(obj);
                if (obj == null)
                    return null;

                var humanNpc = obj.ToBaseEntity() as HumanNpc;
                if (humanNpc == null || GetMemory(humanNpc) == null)
                    return null;

                if (!_plugin._config.NPCTargetingSettings.IsAllowed(humanNpc))
                    return null;

                return humanNpc.gameObject;
            }

            public override void OnEntityEnter(BaseEntity entity)
            {
                base.OnEntityEnter(entity);

                if (!HasAnyEntityContents || !entityContents.Contains(entity))
                    return;

                if (!IsInvoking(_checkTriggerContents))
                {
                    InvokeRepeating(_checkTriggerContents, UnityEngine.Random.Range(0, 1), 1);
                }
            }

            public override void OnEntityLeave(BaseEntity entity)
            {
                base.OnEntityLeave(entity);
                var memory = GetMemory(entity);
                if (memory != null)
                {
                    RemoveFromMemory(memory);
                }
            }

            private SimpleAIMemory GetMemory(BaseEntity entity, out AIBrainSenses senses)
            {
                var brain = (entity as HumanNpc)?.Brain;
                senses = brain?.Senses;
                return senses?.Memory;
            }

            private SimpleAIMemory GetMemory(BaseEntity entity)
            {
                return GetMemory(entity, out _);
            }

            private bool IsTargetableBy(HumanNpc humanNpc)
            {
                if (!_plugin.IsDroneBeingControlled(_drone))
                    return false;

                var eyesPosition = humanNpc.isMounted
                    ? humanNpc.eyes.worldMountedPosition
                    : humanNpc.IsDucked()
                        ? humanNpc.eyes.worldCrouchedPosition
                        : !humanNpc.IsCrawling()
                            ? humanNpc.eyes.worldStandingPosition
                            : humanNpc.eyes.worldCrawlingPosition;

                var layerMask = LayerMask;

                if (humanNpc.AdditionalLosBlockingLayer != 0)
                {
                    layerMask |= 1 << humanNpc.AdditionalLosBlockingLayer;
                }

                return humanNpc.IsVisibleSpecificLayers(_drone.CenterPoint(), eyesPosition, layerMask);
            }

            private bool AddToMemory(SimpleAIMemory memory, AIBrainSenses senses)
            {
                if (_drone.ControllingViewerId.HasValue
                    && _plugin.IsPlayerTargetExempt(_drone.ControllingViewerId.Value.SteamId))
                    return false;

                if (!memory.LOS.Add(_drone))
                    return false;

                senses.LastThreatTimestamp = Time.realtimeSinceStartup;

                memory.All.Add(new SimpleAIMemory.SeenInfo
                {
                    Entity = _drone,
                    Position = _drone.transform.position,
                    Timestamp = Time.realtimeSinceStartup,
                });

                memory.Players.Add(_drone);
                memory.Targets.Add(_drone);
                memory.Threats.Add(_drone);
                return true;
            }

            private bool RemoveFromMemory(SimpleAIMemory memory)
            {
                if (!memory.LOS.Remove(_drone))
                    return false;

                for (var i = 0; i < memory.All.Count; i++)
                {
                    var seenInfo = memory.All[i];
                    if (seenInfo.Entity == _drone)
                    {
                        memory.All.RemoveAt(i);
                        break;
                    }
                }

                memory.Players.Remove(_drone);
                memory.Targets.Remove(_drone);
                memory.Threats.Remove(_drone);
                return true;
            }

            private void OnDestroy()
            {
                if (HasAnyEntityContents)
                {
                    foreach (var entity in entityContents)
                    {
                        var memory = GetMemory(entity);
                        if (memory == null)
                            continue;

                        RemoveFromMemory(memory);
                    }
                }
            }
        }

        private class NPCTargetComponent : FacepunchBehaviour
        {
            public static void AddToDrone(TargetableDrones plugin, Drone drone)
            {
                var component = drone.gameObject.AddComponent<NPCTargetComponent>();

                var child = drone.gameObject.CreateChild();
                child.layer = (int)Rust.Layer.Trigger;

                // HACK: Prevent the drone's sweep test from using incorporating the child collider.
                child.AddComponent<Rigidbody>().isKinematic = true;

                var collider = child.AddComponent<SphereCollider>();
                collider.isTrigger = true;
                collider.radius = plugin._config.NPCTargetingSettings.MaxRange;

                component._trigger = NPCTargetTriggerComponent.AddToDrone(plugin, drone, child);
            }

            public static void RemoveFromDrone(Drone drone)
            {
                DestroyImmediate(drone.gameObject.GetComponent<NPCTargetComponent>());
            }

            private NPCTargetTriggerComponent _trigger;

            private void OnDestroy()
            {
                if (_trigger != null)
                {
                    Destroy(_trigger.gameObject);
                }
            }
        }

        private class TurretTargetComponent : EntityComponent<BaseEntity>
        {
            public static void AddToRootEntityIfMissing(Drone drone, BaseEntity rootEntity)
            {
                rootEntity.GetOrAddComponent<TurretTargetComponent>().InitForDrone(drone);
            }

            public static void AddToDroneIfMissing(TargetableDrones plugin, Drone drone)
            {
                // Must be added to the drone itself since the root entity (SphereEntity) is not a BaseCombatEntity.
                drone.GetOrAddComponent<TurretTargetComponent>().InitForDrone(drone);

                // Add to the root entity to ensure consistency with side effect of landing on cargo ship.
                var rootEntity = plugin.GetRootEntity(drone);
                if (rootEntity != null)
                {
                    AddToRootEntityIfMissing(drone, rootEntity);
                }
            }

            private static void RemoveFromEntity(BaseEntity entity)
            {
                var turretComponent = entity.GetComponent<TurretTargetComponent>();
                if (turretComponent != null)
                {
                    DestroyImmediate(turretComponent);
                    RemoveFromAutoTurretTriggers(entity);
                }
            }

            public static void RemoveFromDrone(TargetableDrones plugin, Drone drone)
            {
                RemoveFromEntity(drone);

                var rootEntity = plugin.GetRootEntity(drone);
                if (rootEntity != null)
                {
                    RemoveFromEntity(rootEntity);
                }
            }

            private Drone _ownerDrone;
            private GameObject _child;

            private TurretTargetComponent InitForDrone(Drone drone)
            {
                _ownerDrone = drone;
                AddChildLayerForAutoTurrets();
                return this;
            }

            private void AddChildLayerForAutoTurrets()
            {
                _child = gameObject.CreateChild();
                _child.layer = (int)Rust.Layer.Player_Server;

                var triggerCollider = _child.gameObject.AddComponent<BoxCollider>();
                triggerCollider.size = _ownerDrone.bounds.extents;
                triggerCollider.isTrigger = true;
            }

            private void OnDestroy()
            {
                if (_child != null)
                {
                    Destroy(_child);
                }
            }
        }

        private class SAMTargetComponent : FacepunchBehaviour, ISamSiteTarget
        {
            public static HashSet<SAMTargetComponent> DroneComponents = new();

            public static void AddToDroneIfMissing(TargetableDrones plugin, Drone drone)
            {
                var component = drone.GetOrAddComponent<SAMTargetComponent>();
                component._plugin = plugin;
            }

            public static void RemoveFromDrone(Drone drone)
            {
                var samComponent = drone.GetComponent<SAMTargetComponent>();
                if (samComponent != null)
                {
                    DestroyImmediate(samComponent);
                }
            }

            public Drone Drone { get; private set; }
            private TargetableDrones _plugin;
            private Transform _transform;

            private void Awake()
            {
                Drone = GetComponent<Drone>();
                _transform = transform;
                DroneComponents.Add(this);
            }

            public Vector3 Position => _transform.position;

            public SamTargetType SAMTargetType => targetTypeVehicle;

            public bool isClient => false;

            public bool IsValidSAMTarget(bool isStaticSamSite)
            {
                if (_plugin.IsTargetExempt(Drone))
                    return false;

                return isStaticSamSite
                    ? _plugin._config.EnableStaticSAMTargeting
                    : _plugin._config.EnablePlayerSAMTargeting;
            }

            public Vector3 CenterPoint() => Drone.CenterPoint();

            public Vector3 GetWorldVelocity() => Drone.body.velocity;

            public bool IsVisible(Vector3 position, float distance) => Drone.IsVisible(position, distance);

            private void OnDestroy() => DroneComponents.Remove(this);
        }

        #endregion

        #region Configuration

        private class CaseInsensitiveDictionary<TValue> : Dictionary<string, TValue>
        {
            public CaseInsensitiveDictionary() : base(StringComparer.OrdinalIgnoreCase) {}

            public CaseInsensitiveDictionary(IEnumerable<KeyValuePair<string, TValue>> collection)
                : base(collection, StringComparer.OrdinalIgnoreCase) {}
        }

        [JsonObject(MemberSerialization.OptIn)]
        private class NPCTargetingSettings
        {
            [JsonProperty("MaxRange")]
            public float MaxRange = 45;

            [JsonProperty("DamageMultiplier")]
            public float DamageMultiplier = 4f;

            [JsonProperty("EnabledByNpcPrefab")]
            private CaseInsensitiveDictionary<bool> EnabledByNpcPrefabName = new();

            private Dictionary<uint, bool> EnabledByNpcPrefabId = new();

            public bool Enabled { get; private set; }

            public bool DamageMultiplierEnabled => Enabled && DamageMultiplier != 1;

            public bool IsAllowed(BaseEntity entity)
            {
                return EnabledByNpcPrefabId.TryGetValue(entity.prefabID, out var canTarget) && canTarget;
            }

            public bool OnServerInitialized()
            {
                var changed = AddMissingNpcPrefabs();

                foreach (var (prefabPath, value) in EnabledByNpcPrefabName)
                {
                    var humanNpc = GameManager.server.FindPrefab(prefabPath)?.GetComponent<HumanNpc>();
                    if (humanNpc == null)
                    {
                        LogWarning($"Invalid HumanNPC prefab in config: {prefabPath}");
                        continue;
                    }

                    EnabledByNpcPrefabId[humanNpc.prefabID] = value;
                    Enabled = true;
                }

                return changed;
            }

            private bool AddMissingNpcPrefabs()
            {
                var changed = false;

                foreach (var prefabPath in GameManifest.Current.entities)
                {
                    var humanNpc = GameManager.server.FindPrefab(prefabPath)?.GetComponent<HumanNpc>();
                    if (humanNpc == null)
                        continue;

                    if (humanNpc.GetComponent<ScientistBrain>()?.HostileTargetsOnly ?? false)
                        continue;

                    if (!EnabledByNpcPrefabName.ContainsKey(prefabPath))
                    {
                        EnabledByNpcPrefabName[prefabPath.ToLower()] = false;
                        changed = true;
                    }
                }

                if (changed)
                {
                    EnabledByNpcPrefabName = new CaseInsensitiveDictionary<bool>(EnabledByNpcPrefabName.OrderBy(entry => entry.Key));
                }

                return changed;
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private class SharingSettings
        {
            [JsonProperty("Team")]
            public bool Team = false;

            [JsonProperty("Friends")]
            public bool Friends = false;

            [JsonProperty("Clan")]
            public bool Clan = false;

            [JsonProperty("Allies")]
            public bool Allies = false;
        }

        [JsonObject(MemberSerialization.OptIn)]
        private class Configuration : BaseConfiguration
        {
            [JsonProperty("EnableTurretTargeting")]
            public bool EnableTurretTargeting = true;

            [JsonProperty("EnablePlayerSAMTargeting")]
            public bool EnablePlayerSAMTargeting = true;

            [JsonProperty("EnableStaticSAMTargeting")]
            public bool EnableStaticSAMTargeting = true;

            [JsonProperty("EnableSAMTargeting")]
            public bool DeprecatedEnableSAMTargeting
            {
                set
                {
                    EnablePlayerSAMTargeting = value;
                    EnableStaticSAMTargeting = value;
                }
            }

            [JsonProperty("NPCTargeting")]
            public NPCTargetingSettings NPCTargetingSettings = new();

            [JsonProperty("DefaultSharingSettings")]
            public SharingSettings DefaultSharingSettings = new();

            [JsonIgnore]
            public bool EnableSamTargeting => EnablePlayerSAMTargeting || EnableStaticSAMTargeting;

            public bool OnServerInitialized()
            {
                return NPCTargetingSettings.OnServerInitialized();
            }
        }

        private Configuration GetDefaultConfig() => new();

        #endregion

        #region Configuration Helpers

        [JsonObject(MemberSerialization.OptIn)]
        private class BaseConfiguration
        {
            public bool UsingDefaults;

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(BaseConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            var changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                if (currentRaw.TryGetValue(key, out var currentRawValue))
                {
                    var currentDictValue = currentRawValue as Dictionary<string, object>;
                    if (currentWithDefaults[key] is Dictionary<string, object> defaultDictValue)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                            changed = true;
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _config = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_config))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
                if (_config != null)
                {
                    _config.UsingDefaults = true;
                }
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_config, true);
        }

        #endregion
    }
}
