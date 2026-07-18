using Newtonsoft.Json;
using Oxide.Core.Libraries;
using Rust;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Monuments Recycler", "VisEntities / Raidlands", "0.3.1")]
    [Description("Adds missing recyclers to monuments, including the Apartment Complex and Cargo Ship.")]
    internal class MonumentsRecycler : RustPlugin
    {
        private const string RecyclerPrefab = "assets/bundled/prefabs/static/recycler_static.prefab";
        private const string SmallOilRigKey = "oil_rig_small";
        private const string LargeOilRigKey = "large_oil_rig";
        private const string DomeKey = "dome_monument_name";
        private const string FishingVillageLargePrefab = "assets/bundled/prefabs/autospawn/monument/fishing_village/fishing_village_a.prefab";
        private const string FishingVillageSmallBPrefab = "assets/bundled/prefabs/autospawn/monument/fishing_village/fishing_village_b.prefab";
        private const string FishingVillageSmallAPrefab = "assets/bundled/prefabs/autospawn/monument/fishing_village/fishing_village_c.prefab";
        private const int PlacementCollisionLayers = Layers.Mask.Terrain | Layers.Mask.World | Layers.Mask.Construction | Layers.Mask.Deployed;

        private readonly List<BaseEntity> _recyclers = new List<BaseEntity>();
        private Configuration _config;

        private readonly SpawnData _domeSpawnData = new SpawnData(new Vector3(19.9f, 37.8f, 16.57f), new Vector3(0f, 235f, 0f));

        private readonly Dictionary<string, SpawnData> _smallOilRigRecyclerPositions = new Dictionary<string, SpawnData>
        {
            ["3"] = new SpawnData(new Vector3(21.01f, 22.5f, -30.8f), new Vector3(0f, 180f, 0f)),
            ["4"] = new SpawnData(new Vector3(32.1f, 27f, -34.5f), new Vector3(0f, 270f, 0f))
        };

        private readonly Dictionary<string, SpawnData> _largeOilRigRecyclerPositions = new Dictionary<string, SpawnData>
        {
            ["4"] = new SpawnData(new Vector3(20.57f, 27.1f, -44.52f), Vector3.zero),
            ["6"] = new SpawnData(new Vector3(-13.6f, 36.1f, -3.4f), new Vector3(0f, 180f, 0f))
        };

        private readonly Dictionary<string, SpawnData> _fishingVillageRecyclerPositions = new Dictionary<string, SpawnData>
        {
            ["smallB"] = new SpawnData(new Vector3(-21f, 0.3f, 6.5f), new Vector3(0f, 130f, 0f)),
            ["smallA"] = new SpawnData(new Vector3(-8.5f, 2f, 16.06067f), new Vector3(0f, 90f, 0f)),
            ["large"] = new SpawnData(new Vector3(-20.7f, 0.2f, -9.6f), new Vector3(0f, 45f, 0f))
        };

        private static readonly string[] DefaultAdditionalMonuments =
        {
            "Abandoned Cabins",
            "Abandoned Military Base",
            "Abandoned Supermarket",
            "Airfield",
            "Apartment Complex",
            "Arctic Research Base",
            "Bandit Camp",
            "Ferry Terminal",
            "Giant Excavator Pit",
            "Harbor",
            "HQM Quarry",
            "Jungle Ruin",
            "Jungle Ziggurat",
            "Junkyard",
            "Large Barn",
            "Launch Site",
            "Lighthouse",
            "Military Tunnel",
            "Mining Outpost",
            "Missile Silo",
            "Outpost",
            "Oxum's Gas Station",
            "Power Plant",
            "Ranch",
            "Radtown",
            "Satellite Dish",
            "Sewer Branch",
            "Stone Quarry",
            "Sulfur Quarry",
            "The Dome",
            "Train Yard",
            "Water Treatment Plant",
            "Water Well"
        };

        private class Configuration
        {
            [JsonProperty("Cargo Ship - Recycler Position - Front")]
            public bool RecyclerPositionFront = true;

            [JsonProperty("Cargo Ship - Recycler Position - Back")]
            public bool RecyclerPositionBack = true;

            [JsonProperty("Cargo Ship - Recycler Position - Bottom")]
            public bool RecyclerPositionBottom = true;

            [JsonProperty("Large Oil Rig - Recycler Position - Level 4")]
            public bool LargeOilRigRecyclerPositionLevel4 = true;

            [JsonProperty("Large Oil Rig - Recycler Position - Level 6")]
            public bool LargeOilRigRecyclerPositionLevel6 = true;

            [JsonProperty("Small Oil Rig - Recycler Position - Level 3")]
            public bool SmallOilRigRecyclerPositionLevel3 = true;

            [JsonProperty("Small Oil Rig - Recycler Position - Level 4")]
            public bool SmallOilRigRecyclerPositionLevel4 = true;

            [JsonProperty("Fishing Village - Recycler - Large")]
            public bool FishingVillageRecyclerLarge = true;

            [JsonProperty("Fishing Village - Recycler - Small A")]
            public bool FishingVillageRecyclerSmallA = true;

            [JsonProperty("Fishing Village - Recycler - Small B")]
            public bool FishingVillageRecyclerSmallB = true;

            [JsonProperty("Dome - Recycler")]
            public bool DomeRecycler = true;

            [JsonProperty("Add one recycler to named monuments that do not already have one")]
            public bool AddMissingMonumentRecyclers = true;

            [JsonProperty("Named monuments eligible for a missing recycler")]
            public List<string> AdditionalMonuments = DefaultAdditionalMonuments.ToList();

            [JsonProperty("Maximum distance for associating entities with a monument")]
            public float MonumentAssociationDistance = 400f;

            [JsonProperty("Log existing and added monument recyclers")]
            public bool LogDiscovery = true;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<Configuration>();
            }
            catch (Exception ex)
            {
                PrintError("Configuration was invalid; loading defaults: " + ex.Message);
                LoadDefaultConfig();
            }

            if (_config == null)
            {
                LoadDefaultConfig();
            }

            if (_config.AdditionalMonuments == null || _config.AdditionalMonuments.Count == 0)
            {
                _config.AdditionalMonuments = DefaultAdditionalMonuments.ToList();
            }

            _config.MonumentAssociationDistance = Mathf.Clamp(_config.MonumentAssociationDistance, 75f, 750f);
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void OnServerInitialized()
        {
            timer.Once(1f, ShowRecyclers);
        }

        private void OnEntitySpawned(CargoShip ship)
        {
            if (ship == null || ship.IsDestroyed || !AnyCargoRecyclerEnabled())
            {
                return;
            }

            // The old plugin moved the whole ship every 0.01 seconds for three seconds.
            // That interfered with current Cargo Ship initialization and teardown. Waiting
            // for initialization is sufficient; local transforms keep the recyclers attached.
            timer.Once(3f, delegate
            {
                if (ship == null || ship.IsDestroyed)
                {
                    return;
                }

                if (_config.RecyclerPositionFront)
                {
                    SpawnCargoRecycler(ship, new Vector3(-0.2172264f, 9.574753f, 79.05922f), new Vector3(0f, 180f, 0f));
                }

                if (_config.RecyclerPositionBack)
                {
                    SpawnCargoRecycler(ship, new Vector3(0.2407f, 9.5f, -36.87305f), Vector3.zero);
                }

                if (_config.RecyclerPositionBottom)
                {
                    SpawnCargoRecycler(ship, new Vector3(0f, 0.57767f, 10f), new Vector3(0f, 180f, 0f));
                }
            });
        }

        private bool AnyCargoRecyclerEnabled()
        {
            return _config.RecyclerPositionFront || _config.RecyclerPositionBack || _config.RecyclerPositionBottom;
        }

        private void ShowRecyclers()
        {
            List<MonumentInfo> monuments = GetMonuments();
            if (monuments.Count == 0)
            {
                PrintWarning("No TerrainMeta monuments were available; recycler placement was skipped.");
                return;
            }

            SpawnConfiguredOilRigRecyclers(monuments);
            SpawnConfiguredDomeRecyclers(monuments);
            SpawnConfiguredFishingVillageRecyclers(monuments);

            if (_config.AddMissingMonumentRecyclers)
            {
                AddMissingNamedMonumentRecyclers(monuments);
            }
        }

        private List<MonumentInfo> GetMonuments()
        {
            return TerrainMeta.Path?.Monuments?.Where(monument => monument != null).ToList()
                ?? new List<MonumentInfo>();
        }

        private void SpawnConfiguredOilRigRecyclers(List<MonumentInfo> monuments)
        {
            foreach (MonumentInfo monument in monuments)
            {
                string token = monument.displayPhrase?.token ?? string.Empty;
                SpawnData spawnData;

                if (token == SmallOilRigKey)
                {
                    if (_config.SmallOilRigRecyclerPositionLevel3 && _smallOilRigRecyclerPositions.TryGetValue("3", out spawnData))
                    {
                        SpawnStaticRecycler(monument.transform, spawnData.Position, spawnData.Rotation, "Small Oil Rig level 3");
                    }

                    if (_config.SmallOilRigRecyclerPositionLevel4 && _smallOilRigRecyclerPositions.TryGetValue("4", out spawnData))
                    {
                        SpawnStaticRecycler(monument.transform, spawnData.Position, spawnData.Rotation, "Small Oil Rig level 4");
                    }
                }
                else if (token == LargeOilRigKey)
                {
                    if (_config.LargeOilRigRecyclerPositionLevel4 && _largeOilRigRecyclerPositions.TryGetValue("4", out spawnData))
                    {
                        SpawnStaticRecycler(monument.transform, spawnData.Position, spawnData.Rotation, "Large Oil Rig level 4");
                    }

                    if (_config.LargeOilRigRecyclerPositionLevel6 && _largeOilRigRecyclerPositions.TryGetValue("6", out spawnData))
                    {
                        SpawnStaticRecycler(monument.transform, spawnData.Position, spawnData.Rotation, "Large Oil Rig level 6");
                    }
                }
            }
        }

        private void SpawnConfiguredDomeRecyclers(List<MonumentInfo> monuments)
        {
            if (!_config.DomeRecycler)
            {
                return;
            }

            foreach (MonumentInfo monument in monuments)
            {
                if ((monument.displayPhrase?.token ?? string.Empty) == DomeKey)
                {
                    // Current Rust builds may already provide a Dome recycler.
                    if (!MonumentAlreadyHasRecycler(monument, monuments))
                    {
                        SpawnStaticRecycler(monument.transform, _domeSpawnData.Position, _domeSpawnData.Rotation, "Dome");
                    }
                    else
                    {
                        LogDiscovery("Dome already has a recycler; custom Dome recycler skipped.");
                    }
                }
            }
        }

        private void SpawnConfiguredFishingVillageRecyclers(List<MonumentInfo> monuments)
        {
            foreach (MonumentInfo monument in monuments)
            {
                SpawnData spawnData;

                if (monument.name == FishingVillageLargePrefab && _config.FishingVillageRecyclerLarge
                    && _fishingVillageRecyclerPositions.TryGetValue("large", out spawnData))
                {
                    SpawnStaticRecycler(monument.transform, spawnData.Position, spawnData.Rotation, "Large Fishing Village");
                }
                else if (monument.name == FishingVillageSmallAPrefab && _config.FishingVillageRecyclerSmallA
                    && _fishingVillageRecyclerPositions.TryGetValue("smallA", out spawnData))
                {
                    SpawnStaticRecycler(monument.transform, spawnData.Position, spawnData.Rotation, "Small Fishing Village A");
                }
                else if (monument.name == FishingVillageSmallBPrefab && _config.FishingVillageRecyclerSmallB
                    && _fishingVillageRecyclerPositions.TryGetValue("smallB", out spawnData))
                {
                    SpawnStaticRecycler(monument.transform, spawnData.Position, spawnData.Rotation, "Small Fishing Village B");
                }
            }
        }

        private void AddMissingNamedMonumentRecyclers(List<MonumentInfo> monuments)
        {
            int existing = 0;
            int added = 0;
            int failed = 0;

            foreach (MonumentInfo monument in monuments)
            {
                string label = MonumentLabel(monument);
                if (!IsEligibleAdditionalMonument(monument, label))
                {
                    continue;
                }

                if (MonumentAlreadyHasRecycler(monument, monuments))
                {
                    existing++;
                    LogDiscovery(label + " already has a recycler.");
                    continue;
                }

                Vector3 position;
                Quaternion rotation;
                string anchor;
                if (!TryFindRecyclerPlacement(monument, monuments, out position, out rotation, out anchor))
                {
                    failed++;
                    PrintWarning("Could not find a safe recycler placement for " + label + ". No recycler was spawned there.");
                    continue;
                }

                if (SpawnRecyclerEntity(position, rotation, label + " via " + anchor, null) != null)
                {
                    added++;
                }
                else
                {
                    failed++;
                }
            }

            Puts("Monument recycler audit complete: " + existing + " eligible monument(s) already had one, "
                + added + " missing recycler(s) added, " + failed + " placement(s) skipped.");
        }

        private bool IsEligibleAdditionalMonument(MonumentInfo monument, string label)
        {
            if (monument == null || !monument.shouldDisplayOnMap)
            {
                return false;
            }

            string prefab = monument.name ?? string.Empty;
            foreach (string configuredName in _config.AdditionalMonuments)
            {
                string candidate = (configuredName ?? string.Empty).Trim();
                if (candidate.Length == 0)
                {
                    continue;
                }

                if (label.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0
                    || prefab.IndexOf(NormalizeName(candidate), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private string MonumentLabel(MonumentInfo monument)
        {
            string english = monument?.displayPhrase?.english;
            if (!string.IsNullOrWhiteSpace(english))
            {
                return english.Trim();
            }

            string name = monument?.name ?? "Unknown Monument";
            int slash = name.LastIndexOf('/');
            if (slash >= 0)
            {
                name = name.Substring(slash + 1);
            }

            return name.Replace(".prefab", string.Empty).Replace('_', ' ');
        }

        private string NormalizeName(string value)
        {
            return (value ?? string.Empty).ToLowerInvariant().Replace("'", string.Empty).Replace(" ", "_").Replace("-", "_");
        }

        private bool MonumentAlreadyHasRecycler(MonumentInfo monument, List<MonumentInfo> monuments)
        {
            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                BaseEntity entity = networkable as BaseEntity;
                if (!IsLiveRecycler(entity))
                {
                    continue;
                }

                if (NearestMonument(entity.transform.position, monuments) == monument)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsLiveRecycler(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
            {
                return false;
            }

            return entity is Recycler
                || (entity.ShortPrefabName ?? string.Empty).IndexOf("recycler", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private MonumentInfo NearestMonument(Vector3 position, List<MonumentInfo> monuments)
        {
            MonumentInfo nearest = null;
            float nearestSquared = _config.MonumentAssociationDistance * _config.MonumentAssociationDistance;

            foreach (MonumentInfo candidate in monuments)
            {
                if (candidate == null)
                {
                    continue;
                }

                Vector3 delta = candidate.transform.position - position;
                delta.y = 0f;
                float squared = delta.sqrMagnitude;
                if (squared < nearestSquared)
                {
                    nearestSquared = squared;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        private bool TryFindRecyclerPlacement(MonumentInfo monument, List<MonumentInfo> monuments, out Vector3 position, out Quaternion rotation, out string anchorName)
        {
            if (MonumentLabel(monument).IndexOf("Apartment Complex", StringComparison.OrdinalIgnoreCase) >= 0
                && TryFindApartmentComplexPlacement(monument, monuments, out position, out rotation, out anchorName))
            {
                return true;
            }

            BaseEntity bestAnchor = null;
            float bestDistance = float.MaxValue;

            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                BaseEntity entity = networkable as BaseEntity;
                if (entity == null || entity.IsDestroyed || !IsPlacementAnchor(entity))
                {
                    continue;
                }

                if (NearestMonument(entity.transform.position, monuments) != monument)
                {
                    continue;
                }

                float distance = (entity.transform.position - monument.transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestAnchor = entity;
                }
            }

            if (bestAnchor != null && TryPlacementAroundAnchor(bestAnchor, out position, out rotation))
            {
                anchorName = bestAnchor.ShortPrefabName ?? "utility anchor";
                return true;
            }

            if (TryTerrainPlacement(monument, out position, out rotation))
            {
                anchorName = "terrain fallback";
                return true;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            anchorName = string.Empty;
            return false;
        }

        private bool TryFindApartmentComplexPlacement(MonumentInfo monument, List<MonumentInfo> monuments, out Vector3 position, out Quaternion rotation, out string anchorName)
        {
            BaseEntity selectedAnchor = null;
            int selectedPriority = int.MaxValue;
            float selectedHeight = float.MaxValue;
            float selectedDistance = float.MaxValue;

            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                BaseEntity entity = networkable as BaseEntity;
                if (entity == null || entity.IsDestroyed)
                {
                    continue;
                }

                string name = (entity.ShortPrefabName ?? entity.PrefabName ?? string.Empty).ToLowerInvariant();
                int priority;
                if (name.Contains("repairbench"))
                {
                    priority = 0;
                }
                else if (name.Contains("researchtable"))
                {
                    priority = 1;
                }
                else
                {
                    continue;
                }

                if (NearestMonument(entity.transform.position, monuments) != monument)
                {
                    continue;
                }

                float height = entity.transform.position.y;
                float distance = (entity.transform.position - monument.transform.position).sqrMagnitude;
                if (priority < selectedPriority
                    || (priority == selectedPriority && height < selectedHeight - 0.25f)
                    || (priority == selectedPriority && Mathf.Abs(height - selectedHeight) <= 0.25f && distance < selectedDistance))
                {
                    selectedAnchor = entity;
                    selectedPriority = priority;
                    selectedHeight = height;
                    selectedDistance = distance;
                }
            }

            if (selectedAnchor != null && TryPlacementAroundAnchor(selectedAnchor, out position, out rotation))
            {
                anchorName = "ground-level " + (selectedAnchor.ShortPrefabName ?? "courtyard utility");
                return true;
            }

            // The Apartment Complex has a central open courtyard. If its repair bench or
            // research table cannot be used, scan ground-level points near the monument
            // origin instead of selecting apartment workbenches on upper floors.
            if (TryTerrainPlacement(monument, out position, out rotation))
            {
                anchorName = "Apartment Complex courtyard fallback";
                return true;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            anchorName = string.Empty;
            return false;
        }

        private bool IsPlacementAnchor(BaseEntity entity)
        {
            string name = (entity.ShortPrefabName ?? entity.PrefabName ?? string.Empty).ToLowerInvariant();
            return name.Contains("repairbench")
                || name.Contains("researchtable")
                || name.Contains("workbench")
                || name.Contains("marketplace")
                || name.Contains("vendingmachine");
        }

        private bool TryPlacementAroundAnchor(BaseEntity anchor, out Vector3 position, out Quaternion rotation)
        {
            Transform transform = anchor.transform;
            Vector3[] offsets =
            {
                transform.right * 2.75f,
                -transform.right * 2.75f,
                -transform.forward * 2.75f,
                transform.forward * 2.75f,
                transform.right * 3.5f - transform.forward * 1.5f,
                -transform.right * 3.5f - transform.forward * 1.5f
            };

            foreach (Vector3 offset in offsets)
            {
                Vector3 candidate = transform.position + offset;
                if (!TrySnapToSurface(ref candidate))
                {
                    continue;
                }

                Vector3 look = transform.position - candidate;
                look.y = 0f;
                Quaternion candidateRotation = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look.normalized) : transform.rotation;
                if (!IsRecyclerPositionFree(candidate, candidateRotation))
                {
                    continue;
                }

                position = candidate;
                rotation = candidateRotation;
                return true;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        private bool TryTerrainPlacement(MonumentInfo monument, out Vector3 position, out Quaternion rotation)
        {
            float[] radii = { 12f, 18f, 25f, 35f, 50f };
            for (int radiusIndex = 0; radiusIndex < radii.Length; radiusIndex++)
            {
                for (int angle = 0; angle < 360; angle += 30)
                {
                    float radians = angle * Mathf.Deg2Rad;
                    Vector3 localOffset = new Vector3(Mathf.Sin(radians) * radii[radiusIndex], 0f, Mathf.Cos(radians) * radii[radiusIndex]);
                    Vector3 candidate = monument.transform.position + monument.transform.rotation * localOffset;
                    candidate.y = monument.transform.position.y + 20f;

                    if (!TrySnapToSurface(ref candidate))
                    {
                        continue;
                    }

                    float water = TerrainMeta.WaterMap.GetHeight(candidate);
                    if (water > candidate.y + 0.25f)
                    {
                        continue;
                    }

                    Vector3 look = monument.transform.position - candidate;
                    look.y = 0f;
                    Quaternion candidateRotation = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look.normalized) : monument.transform.rotation;
                    if (!IsRecyclerPositionFree(candidate, candidateRotation))
                    {
                        continue;
                    }

                    position = candidate;
                    rotation = candidateRotation;
                    return true;
                }
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        private bool TrySnapToSurface(ref Vector3 position)
        {
            RaycastHit hit;
            Vector3 origin = position + Vector3.up * 3f;
            if (!Physics.Raycast(origin, Vector3.down, out hit, 45f, PlacementCollisionLayers, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (hit.normal.y < 0.75f)
            {
                return false;
            }

            position = hit.point + Vector3.up * 0.05f;
            return IsFinite(position);
        }

        private bool IsRecyclerPositionFree(Vector3 position, Quaternion rotation)
        {
            if (!IsFinite(position))
            {
                return false;
            }

            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                BaseEntity entity = networkable as BaseEntity;
                if (entity == null || entity.IsDestroyed)
                {
                    continue;
                }

                if ((entity.transform.position - position).sqrMagnitude < 4f)
                {
                    return false;
                }
            }

            // The clearance box begins above the supporting surface, so the floor itself
            // does not reject the candidate while walls, ceilings and façade geometry do.
            Vector3 center = position + Vector3.up * 1.05f;
            Vector3 halfExtents = new Vector3(0.72f, 0.9f, 0.72f);
            return !Physics.CheckBox(center, halfExtents, rotation, PlacementCollisionLayers, QueryTriggerInteraction.Ignore);
        }

        private bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private void SpawnStaticRecycler(Transform monumentTransform, Vector3 localPosition, Vector3 localRotation, string source)
        {
            Matrix4x4 matrix = monumentTransform.localToWorldMatrix;
            Vector3 finalPosition = matrix.MultiplyPoint3x4(localPosition);
            Quaternion finalRotation = matrix.rotation * Quaternion.Euler(localRotation);
            SpawnRecyclerEntity(finalPosition, finalRotation, source, null);
        }

        private void SpawnCargoRecycler(CargoShip ship, Vector3 localPosition, Vector3 localRotation)
        {
            if (ship == null || ship.IsDestroyed)
            {
                return;
            }

            Vector3 finalPosition = ship.transform.TransformPoint(localPosition);
            Quaternion finalRotation = ship.transform.rotation * Quaternion.Euler(localRotation);
            SpawnRecyclerEntity(finalPosition, finalRotation, "Cargo Ship", ship);
        }

        private BaseEntity SpawnRecyclerEntity(Vector3 position, Quaternion rotation, string source, BaseEntity parent)
        {
            if (!IsFinite(position))
            {
                PrintWarning("Skipped invalid recycler position for " + source + ".");
                return null;
            }

            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                BaseEntity existing = networkable as BaseEntity;
                if (IsLiveRecycler(existing) && (existing.transform.position - position).sqrMagnitude < 2.25f)
                {
                    LogDiscovery(source + " recycler already exists at the configured position; duplicate skipped.");
                    return existing;
                }
            }

            BaseEntity recycler = GameManager.server.CreateEntity(RecyclerPrefab, position, rotation, true);
            if (recycler == null)
            {
                PrintWarning("Unable to create recycler for " + source + ".");
                return null;
            }

            recycler.EnableSaving(false);
            recycler.Spawn();

            if (parent != null && !parent.IsDestroyed)
            {
                recycler.SetParent(parent, true, true);
                Rigidbody body = recycler.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.isKinematic = true;
                }
            }

            _recyclers.Add(recycler);
            LogDiscovery("Added recycler for " + source + " at " + FormatVector(position) + ".");
            return recycler;
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            BaseEntity baseEntity = entity as BaseEntity;
            if (baseEntity != null)
            {
                _recyclers.Remove(baseEntity);
            }
        }

        private void Unload()
        {
            foreach (BaseEntity recycler in _recyclers.ToList())
            {
                if (recycler == null || recycler.IsDestroyed)
                {
                    continue;
                }

                Recycler recyclerComponent = recycler as Recycler;
                if (recyclerComponent != null)
                {
                    recyclerComponent.DropItems();
                }

                recycler.Kill(BaseNetworkable.DestroyMode.None);
            }

            _recyclers.Clear();
        }

        private void LogDiscovery(string message)
        {
            if (_config.LogDiscovery)
            {
                Puts(message);
            }
        }

        private string FormatVector(Vector3 value)
        {
            return value.x.ToString("0.0") + ", " + value.y.ToString("0.0") + ", " + value.z.ToString("0.0");
        }

        private class SpawnData
        {
            public SpawnData(Vector3 position, Vector3 rotation)
            {
                Position = position;
                Rotation = rotation;
            }

            public Vector3 Position { get; private set; }
            public Vector3 Rotation { get; private set; }
        }
    }
}
