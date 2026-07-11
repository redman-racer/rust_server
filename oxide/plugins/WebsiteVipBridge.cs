using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WebsiteVipBridge", "Raidlands", "1.6.1")]
    [Description("Syncs website VIP entitlements and player stats between Raidlands.net and the Rust server.")]
    public class WebsiteVipBridge : CovalencePlugin
    {
        private Configuration config;
        private Timer syncTimer;
        private Timer statsTimer;
        private Timer pendingStatsTimer;
        private Timer statusHeartbeatTimer;
        private Timer pendingStatusHeartbeatTimer;
        private Timer kitSyncTimer;
        private Timer pendingKitSnapshotTimer;
        private Timer permissionSyncTimer;
        private Timer pendingPermissionSnapshotTimer;
        private Timer rpPurchaseTimer;
        private Timer rpPointTimer;
        private Timer heatmapTimer;
        private DateTime rpPurchasePollStartedAt = DateTime.MinValue;
        private DateTime rpPointPollStartedAt = DateTime.MinValue;
        private int rpPurchasePollGeneration;
        private int rpPointPollGeneration;
        private long cursor;
        private long kitRevision;
        private long permissionRevision;
        private DateTime lastStatusHeartbeatAt = DateTime.MinValue;
        private Dictionary<string, string> secrets;
        private const string SecretsConfigName = "Secrets.local";
        private const string RpPurchaseDataFile = "WebsiteVipBridge/rp_purchases";
        private const string RpPointDataFile = "WebsiteVipBridge/rp_point_requests";
        private const string DeletedGroupsDataFile = "WebsiteVipBridge/deleted_groups";
        private const string StorefrontOverridesDataFile = "WebsiteVipBridge/storefront_overrides";
        private string secretsConfigSource;
        private RpPurchaseLedger rpPurchaseData;
        private RpPointLedger rpPointData;
        private DeletedGroupState deletedGroupState;
        private StorefrontOverrideState storefrontOverrideState;
        private bool rpPurchasePollInFlight;
        private bool rpPointPollInFlight;
        private readonly HashSet<string> rpResultPostsInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> rpPointResultPostsInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HeatmapBucket> heatmapBuckets = new Dictionary<string, HeatmapBucket>(StringComparer.OrdinalIgnoreCase);
        private DateTime heatmapWindowStartedAt = DateTime.UtcNow;
        private DateTime lastHeatmapSyncAt = DateTime.MinValue;
        private bool heatmapEndpointMissing;
        private bool isUnloading;

        [PluginReference]
        private Plugin ServerRewards;

        private static readonly HashSet<string> ProtectedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "default",
            "admin",
            "discord",
            "authenticated"
        };

        private static readonly string[] StorefrontRankGroups =
        {
            "rank_vip",
            "rank_vip_plus",
            "rank_mvp",
            "rank_golden_vip",
            "rank_diamond_vip",
            "rank_ultimate_vip",
            "rank_titan_vip"
        };

        private static readonly string[] StorefrontPerkGroups =
        {
            "perk_queue_priority",
            "perk_teleport_instant",
            "perk_home_5s",
            "perk_sign_art",
            "perk_chat_title",
            "perk_backpack_36",
            "perk_backpack_42",
            "perk_backpack_48",
            "perk_backpack_keep_death",
            "perk_backpack_keep_wipe",
            "perk_vehicle_hp_125",
            "perk_vehicle_hp_150",
            "perk_tc_12",
            "perk_minicopter_instant_takeoff"
        };

        private static readonly Dictionary<string, string> CanonicalKitImagePathsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["starter kit"] = "/assets/media/kits/starter-kit.png",
            ["autokit"] = "/assets/media/kits/autokit.png",
            ["raidlands_pvp_light"] = "/assets/media/kits/pvp-light-kit.png",
            ["pvp_light"] = "/assets/media/kits/pvp-light-kit.png",
            ["raidlands_pvp_rifle"] = "/assets/media/kits/pvp-rifle-kit.png",
            ["pvp_rifle"] = "/assets/media/kits/pvp-rifle-kit.png",
            ["raidlands_pvp_roamer"] = "/assets/media/kits/pvp-roamer-kit.png",
            ["pvp_roamer"] = "/assets/media/kits/pvp-roamer-kit.png",
            ["raidlands_pvp_heavy"] = "/assets/media/kits/pvp-heavy-kit.png",
            ["pvp_heavy"] = "/assets/media/kits/pvp-heavy-kit.png",
            ["raidlands_pvp_elite"] = "/assets/media/kits/pvp-elite-kit.png",
            ["pvp_elite"] = "/assets/media/kits/pvp-elite-kit.png",
            ["raidlands_pvp_breach"] = "/assets/media/kits/pvp-breach-kit.png",
            ["pvp_breach"] = "/assets/media/kits/pvp-breach-kit.png",
            ["vip"] = "/assets/media/kits/vip-kit.png",
            ["kit_vip"] = "/assets/media/kits/vip-kit.png",
            ["diamond"] = "/assets/media/kits/vip-diamond-kit.png",
            ["kit_vip_diamond"] = "/assets/media/kits/vip-diamond-kit.png",
            ["vip_plus"] = "/assets/media/kits/vip-plus-kit.png",
            ["kit_vip_plus"] = "/assets/media/kits/vip-plus-kit.png",
            ["kit_vip_plus_diamond"] = "/assets/media/kits/vip-plus-diamond-kit.png",
            ["mvp"] = "/assets/media/kits/mvp-kit.png",
            ["kit_mvp"] = "/assets/media/kits/mvp-kit.png",
            ["golden"] = "/assets/media/kits/golden-vip-kit.png",
            ["kit_golden_vip"] = "/assets/media/kits/golden-vip-kit.png",
            ["ultimate"] = "/assets/media/kits/ultimate-vip-kit.png",
            ["kit_ultimate_vip"] = "/assets/media/kits/ultimate-vip-kit.png",
            ["titan"] = "/assets/media/kits/titan-vip-kit.png",
            ["kit_titan_vip"] = "/assets/media/kits/titan-vip-kit.png",
            ["sentry"] = "/assets/media/kits/sentry-small-pack.png",
            ["pack_sentry_small"] = "/assets/media/kits/sentry-small-pack.png",
            ["sentry_large"] = "/assets/media/kits/sentry-large-pack.png",
            ["pack_sentry_large"] = "/assets/media/kits/sentry-large-pack.png",
            ["portafort"] = "/assets/media/kits/portafort-token.png",
            ["vehicles"] = "/assets/media/kits/vehicle-pack.png",
            ["pack_vehicle"] = "/assets/media/kits/vehicle-pack.png",
            ["steam_name_rewards"] = "/assets/media/kits/steam-name-rewards-kit.png",
            ["kit_claim_steam_name_rewards"] = "/assets/media/kits/steam-name-rewards-kit.png",
            ["steam"] = "/assets/media/kits/steam-rewards-kit.png",
            ["kit_claim_steam_rewards"] = "/assets/media/kits/steam-rewards-kit.png",
            ["discord_booster"] = "/assets/media/kits/discord-booster-kit.png",
            ["kit_claim_discord_booster"] = "/assets/media/kits/discord-booster-kit.png",
            ["discord_raid"] = "/assets/media/kits/discord-raid-kit.png",
            ["kit_claim_discord_raid"] = "/assets/media/kits/discord-raid-kit.png",
            ["discord"] = "/assets/media/kits/discord-kit.png",
            ["kit_claim_discord"] = "/assets/media/kits/discord-kit.png",
            ["556"] = "/assets/media/kits/556-kit.png",
            ["kit_claim_556"] = "/assets/media/kits/556-kit.png",
            ["cards"] = "/assets/media/kits/cards-kit.png",
            ["kit_claim_cards"] = "/assets/media/kits/cards-kit.png",
            ["scrap"] = "/assets/media/kits/scrap-kit.png",
            ["kit_claim_scrap"] = "/assets/media/kits/scrap-kit.png",
            ["scuba"] = "/assets/media/kits/scuba-kit.png",
            ["kit_claim_scuba"] = "/assets/media/kits/scuba-kit.png",
            ["components"] = "/assets/media/kits/comps-kit.png",
            ["comps"] = "/assets/media/kits/comps-kit.png",
            ["kit_claim_components"] = "/assets/media/kits/comps-kit.png",
            ["build"] = "/assets/media/kits/build-kit.png",
            ["build kit"] = "/assets/media/kits/build-kit.png",
            ["kit_claim_build"] = "/assets/media/kits/build-kit.png",
            ["raid"] = "/assets/media/kits/raid-kit.png",
            ["raid kit"] = "/assets/media/kits/raid-kit.png",
            ["kit_claim_raid"] = "/assets/media/kits/raid-kit.png",
            ["medical"] = "/assets/media/kits/medical-kit.png",
            ["kit_claim_medical"] = "/assets/media/kits/medical-kit.png",
            ["mp5"] = "/assets/media/kits/mp5-kit.png",
            ["kit_claim_mp5"] = "/assets/media/kits/mp5-kit.png",
            ["lr300"] = "/assets/media/kits/lr300-kit.png",
            ["kit_claim_lr300"] = "/assets/media/kits/lr300-kit.png",
            ["m16"] = "/assets/media/kits/m16a2-kit.png",
            ["kit_claim_m16a2"] = "/assets/media/kits/m16a2-kit.png",
            ["ak"] = "/assets/media/kits/ak-kit.png",
            ["kit_claim_ak"] = "/assets/media/kits/ak-kit.png"
        };

        private static readonly Dictionary<string, string> CanonicalKitImagePathsByPermission = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["kits.autokit"] = "/assets/media/kits/autokit.png",
            ["kits.pvp.light"] = "/assets/media/kits/pvp-light-kit.png",
            ["kits.pvp.rifle"] = "/assets/media/kits/pvp-rifle-kit.png",
            ["kits.pvp.roamer"] = "/assets/media/kits/pvp-roamer-kit.png",
            ["kits.pvp.heavy"] = "/assets/media/kits/pvp-heavy-kit.png",
            ["kits.pvp.elite"] = "/assets/media/kits/pvp-elite-kit.png",
            ["kits.pvp.breach"] = "/assets/media/kits/pvp-breach-kit.png",
            ["kits.vip"] = "/assets/media/kits/vip-kit.png",
            ["kits.vip.diamond"] = "/assets/media/kits/vip-diamond-kit.png",
            ["kits.vipplus"] = "/assets/media/kits/vip-plus-kit.png",
            ["kits.vipplus.diamond"] = "/assets/media/kits/vip-plus-diamond-kit.png",
            ["kits.mvp"] = "/assets/media/kits/mvp-kit.png",
            ["kits.goldenvip"] = "/assets/media/kits/golden-vip-kit.png",
            ["kits.ultimatevip"] = "/assets/media/kits/ultimate-vip-kit.png",
            ["kits.titanvip"] = "/assets/media/kits/titan-vip-kit.png",
            ["kits.sentry.small"] = "/assets/media/kits/sentry-small-pack.png",
            ["kits.sentry.large"] = "/assets/media/kits/sentry-large-pack.png",
            ["kits.portafort"] = "/assets/media/kits/portafort-token.png",
            ["kits.vehicle"] = "/assets/media/kits/vehicle-pack.png",
            ["kits.claim.steam_name_rewards"] = "/assets/media/kits/steam-name-rewards-kit.png",
            ["kits.claim.steam_rewards"] = "/assets/media/kits/steam-rewards-kit.png",
            ["kits.claim.discord_booster"] = "/assets/media/kits/discord-booster-kit.png",
            ["kits.claim.discord_raid"] = "/assets/media/kits/discord-raid-kit.png",
            ["kits.claim.discord"] = "/assets/media/kits/discord-kit.png",
            ["kits.claim.556"] = "/assets/media/kits/556-kit.png",
            ["kits.claim.cards"] = "/assets/media/kits/cards-kit.png",
            ["kits.claim.scrap"] = "/assets/media/kits/scrap-kit.png",
            ["kits.claim.scuba"] = "/assets/media/kits/scuba-kit.png",
            ["kits.claim.components"] = "/assets/media/kits/comps-kit.png",
            ["kits.comp"] = "/assets/media/kits/comps-kit.png",
            ["kits.claim.build"] = "/assets/media/kits/build-kit.png",
            ["kits.build"] = "/assets/media/kits/build-kit.png",
            ["kits.claim.raid"] = "/assets/media/kits/raid-kit.png",
            ["kits.raid"] = "/assets/media/kits/raid-kit.png",
            ["kits.claim.medical"] = "/assets/media/kits/medical-kit.png",
            ["kits.medical"] = "/assets/media/kits/medical-kit.png",
            ["kits.claim.mp5"] = "/assets/media/kits/mp5-kit.png",
            ["kits.claim.lr300"] = "/assets/media/kits/lr300-kit.png",
            ["kits.claim.m16a2"] = "/assets/media/kits/m16a2-kit.png",
            ["kits.claim.ak"] = "/assets/media/kits/ak-kit.png"
        };

        private static Dictionary<string, StorefrontGroupBundle> DefaultStorefrontBundles()
        {
            var bundles = new Dictionary<string, StorefrontGroupBundle>(StringComparer.OrdinalIgnoreCase)
            {
                ["vip"] = Bundle("VIP", "rank_vip"),
                ["vip_plus"] = Bundle("VIP+", "rank_vip_plus"),
                ["mvp"] = Bundle("MVP", "rank_mvp"),
                ["golden_vip"] = Bundle("Golden VIP", "rank_golden_vip"),
                ["diamond_vip"] = Bundle("Diamond VIP", "rank_diamond_vip"),
                ["ultimate_vip"] = Bundle("Ultimate VIP", "rank_ultimate_vip"),
                ["titan_vip"] = Bundle("Titan VIP", "rank_titan_vip"),
                ["queue_priority"] = Bundle("Queue Priority", "perk_queue_priority"),
                ["teleport_instant"] = Bundle("Instant Teleport", "perk_teleport_instant"),
                ["home_5s"] = Bundle("5 Second Home Teleport", "perk_home_5s"),
                ["sign_art"] = Bundle("Custom Sign Art", "perk_sign_art"),
                ["chat_title"] = Bundle("Custom Chat Title", "perk_chat_title"),
                ["backpack_36"] = Bundle("Backpack 36 Slots", "perk_backpack_36"),
                ["backpack_42"] = Bundle("Backpack 42 Slots", "perk_backpack_42"),
                ["backpack_48"] = Bundle("Backpack 48 Slots", "perk_backpack_48"),
                ["backpack_keep_death"] = Bundle("Keep Backpack on Death", "perk_backpack_keep_death"),
                ["backpack_keep_wipe"] = Bundle("Keep Backpack on Wipe", "perk_backpack_keep_wipe"),
                ["vehicle_hp_125"] = Bundle("Shop Vehicle HP 1.25x", "perk_vehicle_hp_125"),
                ["vehicle_hp_150"] = Bundle("Shop Vehicle HP 1.5x", "perk_vehicle_hp_150"),
                ["tc_12"] = Bundle("TC Limit 12", "perk_tc_12"),
                ["minicopter_instant_takeoff"] = Bundle("Instant Minicopter Takeoff", "perk_minicopter_instant_takeoff"),
                ["shop_ranks"] = Bundle("All Storefront Ranks", StorefrontRankGroups),
                ["shop_perks"] = Bundle("All Storefront Perks", StorefrontPerkGroups),
                ["shop_all"] = Bundle("All Storefront Ranks and Perks", StorefrontRankGroups.Concat(StorefrontPerkGroups).ToArray()),
                ["all"] = Bundle("All Storefront Ranks and Perks", StorefrontRankGroups.Concat(StorefrontPerkGroups).ToArray())
            };

            return bundles;
        }

        private static StorefrontGroupBundle Bundle(string title, params string[] groups)
        {
            return new StorefrontGroupBundle
            {
                Title = title,
                Groups = groups?.Where(group => !string.IsNullOrWhiteSpace(group)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>()
            };
        }

        private class StorefrontGroupBundle
        {
            public string Title = "";
            public List<string> Groups = new List<string>();
        }

        private class Configuration
        {
            public string ApiBaseUrl = "https://raidlands.net";
            [JsonProperty("Website Asset Base Url")]
            public string WebsiteAssetBaseUrl = "https://raidlands.net";
            [JsonProperty("Assets")]
            public AssetPaths Assets = new AssetPaths();
            public string ServerId = "raidlands-main";
            public string SharedSecret = "";
            public int SyncIntervalSeconds = 120;
            public string FailMode = "log_only";
            public bool StatusHeartbeatEnabled = true;
            public int StatusHeartbeatIntervalSeconds = 30;
            public int StatusHeartbeatDebounceSeconds = 10;
            public int WebRequestTimeoutMilliseconds = 20000;
            public bool StatsEnabled = true;
            public int StatsSyncIntervalSeconds = 300;
            public int StatsDebounceSeconds = 30;
            public int StatsBotSnapshotLimit = 0;
            public bool HeatmapEnabled = false;
            public int HeatmapSyncIntervalSeconds = 300;
            public int HeatmapBucketSize = 100;
            public List<string> HeatmapMetrics = new List<string>
            {
                "player_deaths",
                "pvp_kills",
                "npc_deaths",
                "deaths_by_npc",
                "roambots_activity"
            };
            public int HeatmapRetentionHours = 72;
            public bool HeatmapIncludeOnlinePositions = false;
            public bool RpPurchasesEnabled = true;
            public int RpPurchasePollIntervalSeconds = 30;
            public int RpPurchasePollLimit = 10;
            public int RpPurchaseProcessedRetentionDays = 0;
            public bool RpPointRequestsEnabled = true;
            public int RpPointRequestPollIntervalSeconds = 30;
            public int RpPointRequestPollLimit = 10;
            public int RpPointRequestProcessedRetentionDays = 0;
            public bool KitSyncEnabled = true;
            public int KitSyncIntervalSeconds = 180;
            public bool PermissionSyncEnabled = true;
            public int PermissionSyncIntervalSeconds = 180;
            public int PermissionSnapshotDebounceSeconds = 10;
            public int PermissionSnapshotSettledDelaySeconds = 120;
            public int KitDataBackupCount = 8;
            public List<string> KitPermissionManagedGroups = new List<string>
            {
                "default",
                "discord",
                "rank_vip",
                "rank_vip_plus",
                "rank_mvp",
                "rank_golden_vip",
                "rank_diamond_vip",
                "rank_ultimate_vip",
                "rank_titan_vip",
                "vip_bronze",
                "vip_gold",
                "vip_elite",
                "claim_steam_name",
                "claim_steam_group",
                "claim_discord_member",
                "claim_discord_booster"
            };
            public List<string> KitPermissionPrefixes = new List<string>
            {
                "kits.",
                "serverrewards."
            };
            public string WipeKey = "";
            public string WipeStartedAt = "";
            public List<string> ManagedGroups = new List<string>
            {
                "rank_vip",
                "rank_vip_plus",
                "rank_mvp",
                "rank_golden_vip",
                "rank_diamond_vip",
                "rank_ultimate_vip",
                "rank_titan_vip",
                "perk_queue_priority",
                "perk_teleport_instant",
                "perk_home_5s",
                "perk_sign_art",
                "perk_chat_title",
                "perk_backpack_36",
                "perk_backpack_42",
                "perk_backpack_48",
                "perk_backpack_keep_death",
                "perk_backpack_keep_wipe",
                "perk_spawn_full",
                "perk_vehicle_hp_125",
                "perk_vehicle_hp_150",
                "perk_tc_12",
                "perk_minicopter_instant_takeoff",
                "perk_shop_sale_25",
                "perk_shop_sale_50",
                "perk_shop_sale_75",
                "vip_bronze",
                "vip_gold",
                "vip_elite",
                "claim_steam_name",
                "claim_steam_group",
                "claim_discord_member",
                "claim_discord_booster"
            };
            public Dictionary<string, StorefrontGroupBundle> StorefrontBundles = DefaultStorefrontBundles();
        }

        private class AssetPaths
        {
            public string Logo = "/assets/media/raidlands-logo.png";
            public string NavLogo = "/assets/media/nav-logo.png";
            public string SimpleLogo = "/assets/media/raidlands-logo.png";
            public string Hero = "/assets/media/website-hero-raid-overlook-v4.webp";
            public string Header = "/assets/media/header-bg-rust-v2.png";
            public string CommandMenu = "/assets/media/in-game/raidlands-command-menu-bg.png";
            public string WipePanel = "/assets/media/wipe-countdown-panel-v2.jpg";
            public string BackpacksIcon = "/assets/media/feature-icons/backpacks.png";
            public string KitsIcon = "/assets/media/feature-icons/kit.png";
            public string TeleportIcon = "/assets/media/feature-icons/teleport.png";
            public string ClanIcon = "/assets/media/feature-icons/clan.png";
            public string SkinboxIcon = "/assets/media/feature-icons/skinbox.png";
            public string FastRaidsIcon = "/assets/media/feature-icons/fast-raids.png";
            public string GatherIcon = "/assets/media/feature-icons/gather.png";
            public string StatsIcon = "/assets/media/feature-icons/stats.png";
            public string SearchIcon = "/assets/media/feature-icons/search.png";
        }

        private class PlayerResponse
        {
            public bool ok;
            public string error;
            public string steam_id64;
            public List<string> managed_groups;
            public List<string> groups;
            public long cursor;
        }

        private class ChangesResponse
        {
            public bool ok;
            public string error;
            public List<string> managed_groups;
            public List<PlayerState> players;
            public long cursor;
        }

        private class PlayerState
        {
            public string steam_id64;
            public List<string> groups;
        }

        private class StatsResponse
        {
            public bool ok;
            public string error;
        }

        private class StatusHeartbeatResponse
        {
            public bool ok;
            public string error;
        }

        private class KitSyncResponse
        {
            public bool ok;
            public string error;
            public bool has_update;
            public long revision;
            public List<JObject> kits;
            public List<JObject> server_rewards_kits;
            public JToken group_access;
        }

        private class KitResultResponse
        {
            public bool ok;
            public string error;
        }

        private class PermissionSyncResponse
        {
            public bool ok;
            public string error;
            public bool has_update;
            public long revision;
            public List<JObject> groups;
            public JToken group_permissions;
            public List<string> managed_groups;
            public List<string> read_only_groups;
            public List<string> deleted_groups;
        }

        private class PermissionSnapshotGroup
        {
            public string name;
            public string title;
            public int rank;
            public string parent;
        }

        private class RpPurchaseRequest
        {
            public string request_id;
            public string steam_id64;
            public int rp_cost;
            public bool auto_renew;
            public bool renewal;
            public string purchase_type;
            public string subscription_id;
        }

        private class RpPurchaseLedger
        {
            public Dictionary<string, RpPurchaseLedgerEntry> processed = new Dictionary<string, RpPurchaseLedgerEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private class RpPointRequest
        {
            public string request_id;
            public string steam_id64;
            public string source_type;
            public string source_id;
            public int debit_rp;
            public int credit_rp;
            public string reason;
        }

        private class RpPointLedger
        {
            public Dictionary<string, RpPointLedgerEntry> processed = new Dictionary<string, RpPointLedgerEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private class DeletedGroupState
        {
            public List<string> groups = new List<string>();
        }

        private class StorefrontOverrideState
        {
            public Dictionary<string, StorefrontOverrideEntry> players = new Dictionary<string, StorefrontOverrideEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private class StorefrontOverrideEntry
        {
            public List<string> groups = new List<string>();
            public string updated_at;
        }

        private class RpPurchaseLedgerEntry
        {
            public string request_id;
            public string steam_id64;
            public int rp_cost;
            public string status;
            public string reason;
            public string message;
            public int balance_before;
            public int balance_after;
            public bool auto_renew;
            public bool renewal;
            public string purchase_type;
            public string subscription_id;
            public string subscription_status;
            public string transaction_id;
            public string processed_at;
            public bool posted;
            public string posted_at;
            public int post_attempts;
            public string last_post_error;
        }

        private class RpPointLedgerEntry
        {
            public string request_id;
            public string steam_id64;
            public string source_type;
            public string source_id;
            public int debit_rp;
            public int credit_rp;
            public string status;
            public string reason;
            public string message;
            public int balance_before;
            public int balance_after;
            public string transaction_id;
            public string processed_at;
            public bool posted;
            public string posted_at;
            public int post_attempts;
            public string last_post_error;
        }

        private class StatsSnapshot
        {
            public string wipe_key;
            public string wipe_started_at;
            public string generated_at;
            public List<StatsPlayer> players = new List<StatsPlayer>();
            public List<StatsBot> bots = new List<StatsBot>();
        }

        private class StatusHeartbeat
        {
            public string server_id;
            public string generated_at;
            public bool online;
            public string status;
            public string status_label;
            public string name;
            public int players;
            public int max_players;
            public int queue;
            public int joining;
            public int sleepers;
            public string server_fps;
            public string server_fps_average;
            public int entity_count;
            public string map_name;
            public int world_size;
            public int seed;
            public string wipe_key;
            public string wipe_started_at;
            public JObject details;
        }

        private class QueueCounts
        {
            public int queued;
            public int joining;
        }

        private class StatsPlayer
        {
            public string steam_id64;
            public string display_name;
            public int kills;
            public int deaths;
            public int npc_kills;
            public int deaths_by_npc;
            public int playtime_seconds;
            public int afk_seconds;
            public int reward_points;
        }

        private class StatsBot
        {
            public string bot_key;
            public string display_name;
            public string kit_name;
            public string skill_tier;
            public string behavior_model_key;
            public string player_profile_key;
            public string clan_key;
            public string clan_tag;
            public string clan_name;
            public string squad_role;
            public int team_id;
            public int spawns;
            public int kills;
            public int deaths;
        }

        private class HeatmapSnapshot
        {
            public string server_id;
            public string wipe_key;
            public string wipe_started_at;
            public string generated_at;
            public string window_started_at;
            public string window_ended_at;
            public int bucket_size;
            public int retention_hours;
            public bool includes_online_positions;
            public List<HeatmapBucket> buckets = new List<HeatmapBucket>();
        }

        private class HeatmapBucket
        {
            public int bucket_x;
            public int bucket_z;
            public int center_x;
            public int center_z;
            public int player_deaths;
            public int pvp_kills;
            public int npc_deaths;
            public int deaths_by_npc;
            public int roambots_activity;
            public int online_positions;
        }

        private class KdrData
        {
            public ulong id;
            public string name;
            public int kills;
            public int deaths;
        }

        private class RoamBotStatsData
        {
            public Dictionary<string, RoamBotPlayerStats> players = new Dictionary<string, RoamBotPlayerStats>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, RoamBotBotStats> bots = new Dictionary<string, RoamBotBotStats>(StringComparer.OrdinalIgnoreCase);
        }

        private class RoamBotPlayerStats
        {
            public string steam_id64;
            public string display_name;
            public int npc_kills;
            public int deaths_by_npc;
        }

        private class RoamBotBotStats
        {
            public string bot_key;
            public string display_name;
            public string kit_name;
            public string skill_tier;
            public string behavior_model_key;
            public string player_profile_key;
            public string clan_key;
            public string clan_tag;
            public string clan_name;
            public string squad_role;
            public int team_id;
            public int spawns;
            public int kills;
            public int deaths;
        }

        private class PlaytimeData
        {
            public Dictionary<string, PlaytimeUser> _userData = new Dictionary<string, PlaytimeUser>();
        }

        private class PlaytimeUser
        {
            public double playtime;
            public double afkTime;
            public string displayName;
            public double PlayTime;
            public double AFKTime;
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
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
                PrintWarning("Configuration was invalid; writing defaults.");
                config = new Configuration();
            }

            var defaults = new Configuration();

            if (string.IsNullOrWhiteSpace(config.WebsiteAssetBaseUrl))
            {
                config.WebsiteAssetBaseUrl = defaults.WebsiteAssetBaseUrl;
            }

            if (config.Assets == null)
            {
                config.Assets = new AssetPaths();
            }

            ApplyAssetDefaults(config.Assets, defaults.Assets);

            if (config.ManagedGroups == null)
            {
                config.ManagedGroups = defaults.ManagedGroups;
            }

            if (config.StatsSyncIntervalSeconds <= 0)
            {
                config.StatsSyncIntervalSeconds = defaults.StatsSyncIntervalSeconds;
            }

            if (config.StatsDebounceSeconds <= 0)
            {
                config.StatsDebounceSeconds = defaults.StatsDebounceSeconds;
            }

            if (config.HeatmapSyncIntervalSeconds <= 0)
            {
                config.HeatmapSyncIntervalSeconds = defaults.HeatmapSyncIntervalSeconds;
            }

            if (config.HeatmapBucketSize <= 0)
            {
                config.HeatmapBucketSize = defaults.HeatmapBucketSize;
            }

            if (config.HeatmapMetrics == null || config.HeatmapMetrics.Count == 0)
            {
                config.HeatmapMetrics = defaults.HeatmapMetrics;
            }

            if (config.HeatmapRetentionHours <= 0)
            {
                config.HeatmapRetentionHours = defaults.HeatmapRetentionHours;
            }

            if (config.RpPurchasePollIntervalSeconds <= 0)
            {
                config.RpPurchasePollIntervalSeconds = defaults.RpPurchasePollIntervalSeconds;
            }

            if (config.RpPurchasePollLimit <= 0)
            {
                config.RpPurchasePollLimit = defaults.RpPurchasePollLimit;
            }

            if (config.RpPurchaseProcessedRetentionDays < 0)
            {
                config.RpPurchaseProcessedRetentionDays = defaults.RpPurchaseProcessedRetentionDays;
            }

            if (config.RpPointRequestPollIntervalSeconds <= 0)
            {
                config.RpPointRequestPollIntervalSeconds = defaults.RpPointRequestPollIntervalSeconds;
            }

            if (config.RpPointRequestPollLimit <= 0)
            {
                config.RpPointRequestPollLimit = defaults.RpPointRequestPollLimit;
            }

            if (config.RpPointRequestProcessedRetentionDays < 0)
            {
                config.RpPointRequestProcessedRetentionDays = defaults.RpPointRequestProcessedRetentionDays;
            }

            if (config.StatusHeartbeatIntervalSeconds <= 0)
            {
                config.StatusHeartbeatIntervalSeconds = defaults.StatusHeartbeatIntervalSeconds;
            }

            if (config.StatusHeartbeatDebounceSeconds <= 0)
            {
                config.StatusHeartbeatDebounceSeconds = defaults.StatusHeartbeatDebounceSeconds;
            }

            if (config.WebRequestTimeoutMilliseconds <= 0)
            {
                config.WebRequestTimeoutMilliseconds = defaults.WebRequestTimeoutMilliseconds;
            }

            if (config.KitSyncIntervalSeconds <= 0)
            {
                config.KitSyncIntervalSeconds = defaults.KitSyncIntervalSeconds;
            }

            if (config.PermissionSyncIntervalSeconds <= 0)
            {
                config.PermissionSyncIntervalSeconds = defaults.PermissionSyncIntervalSeconds;
            }

            if (config.PermissionSnapshotDebounceSeconds <= 0)
            {
                config.PermissionSnapshotDebounceSeconds = defaults.PermissionSnapshotDebounceSeconds;
            }

            if (config.PermissionSnapshotSettledDelaySeconds <= 0)
            {
                config.PermissionSnapshotSettledDelaySeconds = defaults.PermissionSnapshotSettledDelaySeconds;
            }

            if (config.KitDataBackupCount <= 0)
            {
                config.KitDataBackupCount = defaults.KitDataBackupCount;
            }

            if (config.KitPermissionManagedGroups == null || config.KitPermissionManagedGroups.Count == 0)
            {
                config.KitPermissionManagedGroups = defaults.KitPermissionManagedGroups;
            }

            if (config.KitPermissionPrefixes == null || config.KitPermissionPrefixes.Count == 0)
            {
                config.KitPermissionPrefixes = defaults.KitPermissionPrefixes;
            }

            if (config.StorefrontBundles == null)
            {
                config.StorefrontBundles = new Dictionary<string, StorefrontGroupBundle>(StringComparer.OrdinalIgnoreCase);
            }

            MergeStorefrontBundleDefaults(config.StorefrontBundles, defaults.StorefrontBundles);
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void OnServerInitialized()
        {
            LoadDeletedGroupState();
            LoadStorefrontOverrides();
            EnsureManagedGroups(config.ManagedGroups);
            SyncBrandConfigs();
            LogBridgeSecretDiagnostics();
            SyncChanges();
            StartKitSync();
            StartPermissionSync();
            StartStatusHeartbeat();
            StartRpPurchasePolling();
            StartRpPointPolling();
            StartHeatmapSync();

            var interval = Math.Max(30, config.SyncIntervalSeconds);
            syncTimer = timer.Every(interval, () => RunScheduled("VIP change sync timer", SyncChanges));

            if (config.StatsEnabled)
            {
                var statsInterval = Math.Max(60, config.StatsSyncIntervalSeconds);
                timer.Once(10f, () => RunScheduled("Initial stats sync", SyncStatsSnapshot));
                statsTimer = timer.Every(statsInterval, () => RunScheduled("Stats sync timer", SyncStatsSnapshot));
                Puts($"WebsiteVipBridge syncing VIP every {interval} seconds and stats every {statsInterval} seconds.");
                return;
            }

            Puts($"WebsiteVipBridge syncing VIP every {interval} seconds. Stats sync is disabled.");
        }

        private void Unload()
        {
            isUnloading = true;
            syncTimer?.Destroy();
            statsTimer?.Destroy();
            pendingStatsTimer?.Destroy();
            statusHeartbeatTimer?.Destroy();
            pendingStatusHeartbeatTimer?.Destroy();
            kitSyncTimer?.Destroy();
            pendingKitSnapshotTimer?.Destroy();
            permissionSyncTimer?.Destroy();
            pendingPermissionSnapshotTimer?.Destroy();
            rpPurchaseTimer?.Destroy();
            rpPointTimer?.Destroy();
            heatmapTimer?.Destroy();
            rpPurchasePollGeneration++;
            rpPointPollGeneration++;
            rpPurchasePollInFlight = false;
            rpPointPollInFlight = false;
            SaveRpPurchaseData();
            SaveRpPointData();
            SaveDeletedGroupState();
            SaveStorefrontOverrides();
        }

        private void OnUserConnected(IPlayer player)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.Id))
            {
                return;
            }

            SyncPlayer(player.Id);
            QueueStatsSync();
            QueueStatusHeartbeat();
        }

        private void OnUserDisconnected(IPlayer player)
        {
            QueueStatsSync();
            QueueStatusHeartbeat();
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || !IsHeatmapEnabled())
            {
                return;
            }

            var victimPlayer = entity as BasePlayer;
            var attackerPlayer = info?.InitiatorPlayer;

            if (victimPlayer != null && IsRealPlayerSteamId(victimPlayer.userID))
            {
                AddHeatmapMetric(victimPlayer.transform.position, "player_deaths");

                if (attackerPlayer != null && attackerPlayer != victimPlayer && IsRealPlayerSteamId(attackerPlayer.userID))
                {
                    AddHeatmapMetric(victimPlayer.transform.position, "pvp_kills");
                }

                if (attackerPlayer != null && !IsRealPlayerSteamId(attackerPlayer.userID))
                {
                    AddHeatmapMetric(victimPlayer.transform.position, "deaths_by_npc");
                }

                return;
            }

            if (victimPlayer != null && !IsRealPlayerSteamId(victimPlayer.userID))
            {
                AddHeatmapMetric(victimPlayer.transform.position, "npc_deaths");

                if (IsRaidlandsRoamBot(victimPlayer))
                {
                    AddHeatmapMetric(victimPlayer.transform.position, "roambots_activity");
                }
            }
        }

        private void OnPointsUpdated(ulong userId, int balance)
        {
            QueueStatsSync();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            QueuePermissionSnapshotForPluginChange("Plugin loaded", plugin);
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            QueuePermissionSnapshotForPluginChange("Plugin unloaded", plugin);
        }

        private bool CanRunBridgeCommand(IPlayer player)
        {
            if (player == null || player.IsServer || player.IsAdmin)
            {
                return true;
            }

            player.Reply("You must be a server admin to run this command.");
            return false;
        }

        [Command("websitevip.restoredefault")]
        private void RestoreDefaultCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            var restored = 0;

            foreach (var connectedPlayer in players.Connected)
            {
                if (connectedPlayer == null || !IsSteamId64(connectedPlayer.Id))
                {
                    continue;
                }

                if (EnsureDefaultUserGroup(connectedPlayer.Id))
                {
                    restored++;
                }
            }

            var message = $"Restored default group for {restored} connected player(s).";

            if (player != null)
            {
                player.Reply(message);
            }

            Puts(message);
        }

        [Command("websitevip.storefront.list")]
        private void StorefrontListCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            var bundles = StorefrontBundleMap()
                .OrderBy(entry => entry.Key)
                .Select(entry => $"{entry.Key}: {entry.Value.Title} [{string.Join(", ", entry.Value.Groups)}]");

            ReplyBridge(player, "Storefront bundles:\n" + string.Join("\n", bundles));
        }

        [Command("websitevip.storefront.show")]
        private void StorefrontShowCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            if (args == null || args.Length == 0 || !IsSteamId64(args[0]))
            {
                ReplyBridge(player, "Usage: websitevip.storefront.show <steamid64>");
                return;
            }

            var steamId = args[0].Trim();
            var manualGroups = ManualStorefrontGroupsFor(steamId).OrderBy(group => group).ToList();
            var currentGroups = KnownStorefrontGroups()
                .Where(group => permission.UserHasGroup(steamId, group))
                .OrderBy(group => group)
                .ToList();

            ReplyBridge(player, $"Storefront groups for {steamId}\nCurrent: {FormatGroupList(currentGroups)}\nManual bridge overrides: {FormatGroupList(manualGroups)}");
        }

        [Command("websitevip.storefront.add")]
        private void StorefrontAddCommand(IPlayer player, string command, string[] args)
        {
            StorefrontGrantCommand(player, command, args);
        }

        [Command("websitevip.storefront.grant")]
        private void StorefrontGrantCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            RunStorefrontBundleCommand(player, args, true);
        }

        [Command("websitevip.storefront.remove")]
        private void StorefrontRemoveCommand(IPlayer player, string command, string[] args)
        {
            StorefrontRevokeCommand(player, command, args);
        }

        [Command("websitevip.storefront.revoke")]
        private void StorefrontRevokeCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            RunStorefrontBundleCommand(player, args, false);
        }

        [Command("websitevip.storefront.clear")]
        private void StorefrontClearCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            if (args == null || args.Length == 0 || !IsSteamId64(args[0]))
            {
                ReplyBridge(player, "Usage: websitevip.storefront.clear <steamid64>");
                return;
            }

            var steamId = args[0].Trim();
            var groups = KnownStorefrontGroups().ToList();
            var changedOverrides = RemoveStorefrontOverrideGroups(steamId, groups, true);
            var changedGroups = SetUserStorefrontGroups(steamId, groups, false);
            ReplyBridge(player, $"Removed bridge storefront overrides and current storefront groups from {steamId}. Changed groups={changedGroups}, changed overrides={changedOverrides}. Website-owned entitlements may re-apply on the next sync.");
        }

        [Command("websitevip.kits.snapshot")]
        private void KitSnapshotCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            PostKitSnapshot();
            ReplyBridge(player, "Posted current kit snapshot to the website.");
        }

        [Command("websitevip.kits.sync")]
        private void KitSyncCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            SyncKits();
            ReplyBridge(player, "Requested kit sync from the website.");
        }

        [Command("websitevip.kits.status")]
        private void KitStatusCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            var message = $"Kit sync enabled={config.KitSyncEnabled}, interval={Math.Max(60, config.KitSyncIntervalSeconds)}s, last revision={kitRevision}, backups={KitBackupDirectory()}";
            ReplyBridge(player, message);
        }

        [Command("websitevip.kits.repairimages")]
        private void KitRepairImagesCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            RunScheduled("Kit image repair", () =>
            {
                int kitImagesChanged;
                int rewardIconsChanged;
                RepairCanonicalKitImages(out kitImagesChanged, out rewardIconsChanged);

                if (kitImagesChanged > 0 || rewardIconsChanged > 0)
                {
                    ReplyBridge(player, $"Updated {kitImagesChanged} Kits image URL(s) and {rewardIconsChanged} ServerRewards kit icon URL(s), then reloaded Kits and ServerRewards.");
                    return;
                }

                ReplyBridge(player, "Kit image URLs are already current.");
            });
        }

        [Command("websitevip.permissions.snapshot")]
        private void PermissionSnapshotCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            PostPermissionSnapshot();
            ReplyBridge(player, "Posted current permission snapshot to the website.");
        }

        [Command("websitevip.permissions.sync")]
        private void PermissionSyncCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            SyncPermissions();
            ReplyBridge(player, "Requested permission sync from the website.");
        }

        [Command("websitevip.permissions.status")]
        private void PermissionStatusCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            var message = $"Permission sync enabled={config.PermissionSyncEnabled}, interval={Math.Max(60, config.PermissionSyncIntervalSeconds)}s, last revision={permissionRevision}";
            ReplyBridge(player, message);
        }

        [Command("websitevip.status")]
        private void StatusHeartbeatCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            SyncStatusHeartbeat();
            var interval = Math.Max(15, config.StatusHeartbeatIntervalSeconds);
            var last = lastStatusHeartbeatAt == DateTime.MinValue
                ? "never"
                : lastStatusHeartbeatAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

            ReplyBridge(player, $"Status heartbeat enabled={config.StatusHeartbeatEnabled}, interval={interval}s, last success={last}. Current heartbeat requested.");
        }

        [Command("websitevip.heatmap.sync")]
        private void HeatmapSyncCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            heatmapEndpointMissing = false;
            SyncHeatmapSnapshot();
            ReplyBridge(player, "Requested heatmap aggregate sync.");
        }

        [Command("websitevip.heatmap.status")]
        private void HeatmapStatusCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            var interval = Math.Max(60, config.HeatmapSyncIntervalSeconds);
            var last = lastHeatmapSyncAt == DateTime.MinValue
                ? "never"
                : lastHeatmapSyncAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            var message = $"Heatmap enabled={config.HeatmapEnabled}, endpoint_missing={heatmapEndpointMissing}, interval={interval}s, bucket_size={Math.Max(1, config.HeatmapBucketSize)}m, retention={Math.Max(1, config.HeatmapRetentionHours)}h, include_online_positions={config.HeatmapIncludeOnlinePositions}, pending_buckets={heatmapBuckets.Count}, last_success={last}.";

            ReplyBridge(player, message);
        }

        [Command("websitevip.heatmap.clearwipe")]
        private void HeatmapClearWipeCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            ClearHeatmapWipe();
            ReplyBridge(player, "Cleared pending heatmap aggregates for the current wipe window.");
        }

        [Command("websitevip.rp.sync")]
        private void RpPurchaseSyncCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            SyncRpPurchases();
            ReplyBridge(player, "Requested RP purchase sync from the website.");
        }

        [Command("websitevip.rp.status")]
        private void RpPurchaseStatusCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            LoadRpPurchaseData();

            var processed = rpPurchaseData?.processed?.Count ?? 0;
            var unposted = rpPurchaseData?.processed?.Values.Count(entry => entry != null && !entry.posted) ?? 0;
            var interval = Math.Max(10, config.RpPurchasePollIntervalSeconds);
            var message = $"RP purchase polling enabled={config.RpPurchasesEnabled}, interval={interval}s, processed={processed}, unposted_results={unposted}.";

            ReplyBridge(player, message);
        }

        [Command("websitevip.rp.points.sync")]
        private void RpPointSyncCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            SyncRpPointRequests();
            ReplyBridge(player, "Requested RP point request sync from the website.");
        }

        [Command("websitevip.rp.points.status")]
        private void RpPointStatusCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            LoadRpPointData();

            var processed = rpPointData?.processed?.Count ?? 0;
            var unposted = rpPointData?.processed?.Values.Count(entry => entry != null && !entry.posted) ?? 0;
            var interval = Math.Max(10, config.RpPointRequestPollIntervalSeconds);
            var message = $"RP point request polling enabled={config.RpPointRequestsEnabled}, interval={interval}s, processed={processed}, unposted_results={unposted}.";

            ReplyBridge(player, message);
        }

        [Command("websitevip.kits.rollback")]
        private void KitRollbackCommand(IPlayer player, string command, string[] args)
        {
            if (!CanRunBridgeCommand(player))
            {
                return;
            }

            if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                ReplyBridge(player, "Usage: websitevip.kits.rollback <revision-or-backup-token>");
                return;
            }

            string message;

            if (RollbackKitBackup(args[0], out message))
            {
                ReloadKitPlugins();
            }

            ReplyBridge(player, message);
        }

        private void ReplyBridge(IPlayer player, string message)
        {
            if (player != null)
            {
                player.Reply(message);
            }

            Puts(message);
        }

        private void RunStorefrontBundleCommand(IPlayer player, string[] args, bool grant)
        {
            if (args == null || args.Length < 2 || !IsSteamId64(args[0]))
            {
                ReplyBridge(player, $"Usage: websitevip.storefront.{(grant ? "add" : "remove")} <steamid64> <bundle-or-group> [more-bundles]");
                return;
            }

            var steamId = args[0].Trim();
            var tokens = StorefrontCommandTokens(args.Skip(1)).ToList();

            if (tokens.Count == 0)
            {
                ReplyBridge(player, $"Usage: websitevip.storefront.{(grant ? "add" : "remove")} <steamid64> <bundle-or-group> [more-bundles]");
                return;
            }

            List<string> selected;
            List<string> unknown;
            var groups = ResolveStorefrontGroups(tokens, out selected, out unknown);

            if (unknown.Count > 0)
            {
                ReplyBridge(player, $"Unknown storefront bundle/group: {string.Join(", ", unknown)}. Run websitevip.storefront.list for available bundles.");
                return;
            }

            if (groups.Count == 0)
            {
                ReplyBridge(player, "No storefront groups matched that request.");
                return;
            }

            var changedOverrides = grant
                ? AddStorefrontOverrideGroups(steamId, groups)
                : RemoveStorefrontOverrideGroups(steamId, groups);
            var changedGroups = SetUserStorefrontGroups(steamId, groups, grant);
            var action = grant ? "Granted" : "Removed";
            var note = grant
                ? "Bridge sync will keep these manual overrides until you remove or clear them."
                : "Website-owned entitlements may re-apply on the next sync.";

            ReplyBridge(player, $"{action} storefront bundle(s) {string.Join(", ", selected.Distinct(StringComparer.OrdinalIgnoreCase))} for {steamId}. Groups: {FormatGroupList(groups)}. Changed groups={changedGroups}, changed overrides={changedOverrides}. {note}");
        }

        private IEnumerable<string> StorefrontCommandTokens(IEnumerable<string> args)
        {
            foreach (var arg in args ?? new string[0])
            {
                foreach (var token in (arg ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = token.Trim();

                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        yield return trimmed;
                    }
                }
            }
        }

        private Dictionary<string, StorefrontGroupBundle> StorefrontBundleMap()
        {
            var result = new Dictionary<string, StorefrontGroupBundle>(StringComparer.OrdinalIgnoreCase);
            var source = config?.StorefrontBundles ?? DefaultStorefrontBundles();

            foreach (var entry in source)
            {
                var key = NormalizeStorefrontKey(entry.Key);
                var groups = CleanStorefrontGroups(entry.Value?.Groups).ToList();

                if (string.IsNullOrWhiteSpace(key) || groups.Count == 0)
                {
                    continue;
                }

                result[key] = new StorefrontGroupBundle
                {
                    Title = string.IsNullOrWhiteSpace(entry.Value?.Title) ? key : entry.Value.Title.Trim(),
                    Groups = groups
                };
            }

            if (result.Count == 0)
            {
                foreach (var entry in DefaultStorefrontBundles())
                {
                    result[NormalizeStorefrontKey(entry.Key)] = entry.Value;
                }
            }

            return result;
        }

        private HashSet<string> ResolveStorefrontGroups(IEnumerable<string> tokens, out List<string> selected, out List<string> unknown)
        {
            var bundles = StorefrontBundleMap();
            var knownGroups = KnownStorefrontGroups();
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            selected = new List<string>();
            unknown = new List<string>();

            foreach (var token in tokens ?? new string[0])
            {
                var key = NormalizeStorefrontKey(token);
                StorefrontGroupBundle bundle;
                string bundleKey;

                if (TryResolveStorefrontBundle(bundles, key, out bundleKey, out bundle))
                {
                    selected.Add(bundleKey);

                    foreach (var group in bundle.Groups)
                    {
                        groups.Add(group);
                    }

                    continue;
                }

                var directGroup = knownGroups.FirstOrDefault(group =>
                    string.Equals(group, token, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(NormalizeStorefrontKey(group), key, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(directGroup))
                {
                    selected.Add(directGroup);
                    groups.Add(directGroup);
                    continue;
                }

                unknown.Add(token);
            }

            return groups;
        }

        private bool TryResolveStorefrontBundle(Dictionary<string, StorefrontGroupBundle> bundles, string key, out string bundleKey, out StorefrontGroupBundle bundle)
        {
            bundleKey = "";
            bundle = null;

            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (bundles.TryGetValue(key, out bundle))
            {
                bundleKey = key;
                return true;
            }

            var alias = StorefrontBundleAlias(key);

            if (!string.Equals(alias, key, StringComparison.OrdinalIgnoreCase) && bundles.TryGetValue(alias, out bundle))
            {
                bundleKey = alias;
                return true;
            }

            return false;
        }

        private string StorefrontBundleAlias(string key)
        {
            switch (NormalizeStorefrontKey(key))
            {
                case "ranks":
                case "all_ranks":
                    return "shop_ranks";
                case "perks":
                case "all_perks":
                    return "shop_perks";
                case "everything":
                case "all_shop":
                case "storefront_all":
                    return "shop_all";
                case "rank_vip":
                    return "vip";
                case "rank_vip_plus":
                    return "vip_plus";
                case "mvp":
                case "rank_mvp":
                    return "mvp";
                case "golden":
                case "golden_vip":
                case "rank_golden_vip":
                    return "golden_vip";
                case "diamond":
                case "diamond_vip":
                case "rank_diamond_vip":
                    return "diamond_vip";
                case "ultimate":
                case "ultimate_vip":
                case "rank_ultimate_vip":
                    return "ultimate_vip";
                case "titan":
                case "titan_vip":
                case "rank_titan_vip":
                    return "titan_vip";
                default:
                    return key;
            }
        }

        private HashSet<string> KnownStorefrontGroups()
        {
            return new HashSet<string>(StorefrontBundleMap().Values.SelectMany(bundle => bundle.Groups), StringComparer.OrdinalIgnoreCase);
        }

        private IEnumerable<string> CleanStorefrontGroups(IEnumerable<string> groups)
        {
            return (groups ?? new string[0])
                .Select(group => (group ?? "").Trim())
                .Where(group => IsManageableGroupName(group) && !IsDeletedManagedGroup(group))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group);
        }

        private int SetUserStorefrontGroups(string steamId, IEnumerable<string> groups, bool grant)
        {
            var cleanGroups = CleanStorefrontGroups(groups).ToList();
            var changed = 0;

            if (grant)
            {
                EnsureDefaultUserGroup(steamId);
                EnsureManagedGroups(cleanGroups);
            }

            foreach (var group in cleanGroups)
            {
                var hasGroup = permission.UserHasGroup(steamId, group);

                if (grant && !hasGroup)
                {
                    permission.AddUserGroup(steamId, group);
                    changed++;
                    continue;
                }

                if (!grant && hasGroup)
                {
                    permission.RemoveUserGroup(steamId, group);
                    changed++;
                }
            }

            return changed;
        }

        private int AddStorefrontOverrideGroups(string steamId, IEnumerable<string> groups)
        {
            LoadStorefrontOverrides();

            if (!storefrontOverrideState.players.TryGetValue(steamId, out var entry) || entry == null)
            {
                entry = new StorefrontOverrideEntry();
                storefrontOverrideState.players[steamId] = entry;
            }

            var current = new HashSet<string>(CleanStorefrontGroups(entry.groups), StringComparer.OrdinalIgnoreCase);
            var changed = 0;

            foreach (var group in CleanStorefrontGroups(groups))
            {
                if (current.Add(group))
                {
                    changed++;
                }
            }

            if (changed > 0)
            {
                entry.groups = current.OrderBy(group => group).ToList();
                entry.updated_at = DateTime.UtcNow.ToString("o");
                SaveStorefrontOverrides();
            }

            return changed;
        }

        private int RemoveStorefrontOverrideGroups(string steamId, IEnumerable<string> groups, bool clearAll = false)
        {
            LoadStorefrontOverrides();

            if (!storefrontOverrideState.players.TryGetValue(steamId, out var entry) || entry == null)
            {
                return 0;
            }

            var current = new HashSet<string>(CleanStorefrontGroups(entry.groups), StringComparer.OrdinalIgnoreCase);
            var before = current.Count;

            if (clearAll)
            {
                current.Clear();
            }
            else
            {
                foreach (var group in CleanStorefrontGroups(groups))
                {
                    current.Remove(group);
                }
            }

            var changed = before - current.Count;

            if (changed <= 0)
            {
                return 0;
            }

            if (current.Count == 0)
            {
                storefrontOverrideState.players.Remove(steamId);
            }
            else
            {
                entry.groups = current.OrderBy(group => group).ToList();
                entry.updated_at = DateTime.UtcNow.ToString("o");
            }

            SaveStorefrontOverrides();
            return changed;
        }

        private List<string> ManualStorefrontGroupsFor(string steamId)
        {
            LoadStorefrontOverrides();

            if (storefrontOverrideState.players.TryGetValue(steamId, out var entry) && entry != null)
            {
                return CleanStorefrontGroups(entry.groups).ToList();
            }

            return new List<string>();
        }

        private void LoadStorefrontOverrides()
        {
            if (storefrontOverrideState != null)
            {
                return;
            }

            storefrontOverrideState = ReadDataFile<StorefrontOverrideState>(StorefrontOverridesDataFile) ?? new StorefrontOverrideState();

            if (storefrontOverrideState.players == null)
            {
                storefrontOverrideState.players = new Dictionary<string, StorefrontOverrideEntry>(StringComparer.OrdinalIgnoreCase);
            }

            var normalized = new Dictionary<string, StorefrontOverrideEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in storefrontOverrideState.players)
            {
                if (!IsSteamId64(entry.Key) || entry.Value == null)
                {
                    continue;
                }

                var groups = CleanStorefrontGroups(entry.Value.groups).ToList();

                if (groups.Count == 0)
                {
                    continue;
                }

                normalized[entry.Key.Trim()] = new StorefrontOverrideEntry
                {
                    groups = groups,
                    updated_at = entry.Value.updated_at
                };
            }

            storefrontOverrideState.players = normalized;
        }

        private void SaveStorefrontOverrides()
        {
            if (storefrontOverrideState == null)
            {
                return;
            }

            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(StorefrontOverridesDataFile, storefrontOverrideState, true);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not write data file {StorefrontOverridesDataFile}: {ex.Message}");
            }
        }

        private static string FormatGroupList(IEnumerable<string> groups)
        {
            var list = (groups ?? new string[0])
                .Where(group => !string.IsNullOrWhiteSpace(group))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group)
                .ToList();

            return list.Count == 0 ? "(none)" : string.Join(", ", list);
        }

        private void SyncPlayer(string steamId)
        {
            if (!CanRequest())
            {
                return;
            }

            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/vip-player.php?steam_id64={Uri.EscapeDataString(steamId)}";
            SendGet(url, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"VIP player sync failed for {steamId}: {error}");
                    return;
                }

                var payload = JsonConvert.DeserializeObject<PlayerResponse>(response);

                if (payload == null || !payload.ok)
                {
                PrintWarning($"VIP player sync failed for {steamId}: {payload?.error ?? "invalid response"}");
                return;
            }

                ApplyDesiredGroups(steamId, payload.groups, payload.managed_groups);

                if (payload.cursor > cursor)
                {
                    cursor = payload.cursor;
                }
            });
        }

        private void SyncChanges()
        {
            if (!CanRequest())
            {
                return;
            }

            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/vip-changes.php?since={cursor}";
            SendGet(url, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"VIP change sync failed: {error}");
                    return;
                }

                var payload = JsonConvert.DeserializeObject<ChangesResponse>(response);

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"VIP change sync failed: {payload?.error ?? "invalid response"}");
                    return;
                }

                EnsureManagedGroups(payload.managed_groups);

                foreach (var player in payload.players ?? new List<PlayerState>())
                {
                    if (player == null || string.IsNullOrWhiteSpace(player.steam_id64))
                    {
                        continue;
                    }

                    ApplyDesiredGroups(player.steam_id64, player.groups, payload.managed_groups);
                }

                if (payload.cursor > cursor)
                {
                    cursor = payload.cursor;
                }
            });
        }

        private void StartRpPurchasePolling()
        {
            LoadRpPurchaseData();
            PruneRpPurchaseData();

            if (!config.RpPurchasesEnabled)
            {
                Puts("WebsiteVipBridge RP purchase polling is disabled.");
                return;
            }

            var interval = Math.Max(10, config.RpPurchasePollIntervalSeconds);
            timer.Once(20f, () => RunScheduled("Initial RP purchase sync", SyncRpPurchases));
            rpPurchaseTimer = timer.Every(interval, () => RunScheduled("RP purchase sync timer", SyncRpPurchases));
            Puts($"WebsiteVipBridge polling RP purchases every {interval} seconds.");
        }

        private void SyncRpPurchases()
        {
            if (!config.RpPurchasesEnabled || !CanRequest())
            {
                return;
            }

            if (rpPurchasePollInFlight)
            {
                var staleAfter = Math.Max(10f, WebRequestTimeoutSeconds() + 5f);

                if ((DateTime.UtcNow - rpPurchasePollStartedAt).TotalSeconds <= staleAfter)
                {
                    Puts("RP purchase sync skipped because a previous poll is still in flight.");
                    return;
                }

                PrintWarning($"RP purchase poll exceeded {staleAfter:0} seconds; releasing the stale in-flight guard.");
                rpPurchasePollInFlight = false;
                rpPurchasePollGeneration++;
            }

            LoadRpPurchaseData();
            PruneRpPurchaseData();
            PostPendingRpPurchaseResults();

            rpPurchasePollInFlight = true;
            rpPurchasePollStartedAt = DateTime.UtcNow;
            var pollGeneration = ++rpPurchasePollGeneration;
            var limit = Math.Max(1, config.RpPurchasePollLimit);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/rp-purchases.php?limit={limit}";

            SendGet(url, (code, response) =>
            {
                try
                {
                    if (!IsSuccess(code, response, out var error))
                    {
                        PrintWarning($"RP purchase sync failed: {error}");
                        return;
                    }

                    JObject payload;

                    try
                    {
                        payload = JObject.Parse(response);
                    }
                    catch (Exception ex)
                    {
                        PrintWarning($"RP purchase sync failed: invalid response ({ex.Message})");
                        return;
                    }

                    if (JsonToken(payload, "ok") != null && !JsonBool(payload, false, "ok"))
                    {
                        PrintWarning($"RP purchase sync failed: {JsonString(payload, "error", "message", "reason")}");
                        return;
                    }

                    var requests = ExtractRpPurchaseRequests(payload);
                    var handled = 0;

                    foreach (var request in requests)
                    {
                        if (HandleRpPurchaseRequest(request))
                        {
                            handled++;
                        }
                    }

                    if (handled > 0)
                    {
                        Puts($"Handled {handled} RP purchase request(s).");
                    }
                }
                finally
                {
                    if (pollGeneration == rpPurchasePollGeneration)
                    {
                        rpPurchasePollInFlight = false;
                    }
                }
            });
        }

        private List<JObject> ExtractRpPurchaseRequests(JObject payload)
        {
            var requests = new List<JObject>();
            var array = JsonToken(payload, "requests", "purchases", "rp_purchases", "rp_purchase_requests") as JArray;

            if (array == null)
            {
                return requests;
            }

            foreach (var item in array.OfType<JObject>())
            {
                requests.Add(item);
            }

            return requests;
        }

        private bool HandleRpPurchaseRequest(JObject item)
        {
            var request = ParseRpPurchaseRequest(item);

            if (string.IsNullOrWhiteSpace(request.request_id))
            {
                PrintWarning("Ignored RP purchase request without request_id.");
                return false;
            }

            LoadRpPurchaseData();

            RpPurchaseLedgerEntry existing;

            if (rpPurchaseData.processed.TryGetValue(request.request_id, out existing) && existing != null)
            {
                if (!string.Equals(existing.steam_id64, request.steam_id64, StringComparison.OrdinalIgnoreCase)
                    || (request.rp_cost > 0 && existing.rp_cost != request.rp_cost))
                {
                    PrintWarning($"Duplicate RP purchase {request.request_id} changed request details; returning saved {existing.status} result.");
                }

                PostRpPurchaseResult(existing, true);
                return true;
            }

            var entry = ProcessRpPurchaseRequest(request);
            rpPurchaseData.processed[entry.request_id] = entry;
            SaveRpPurchaseData();

            Puts($"RP purchase {entry.request_id} {entry.status}: {entry.message}");
            PostRpPurchaseResult(entry, true);

            if (string.Equals(entry.status, "confirmed", StringComparison.OrdinalIgnoreCase))
            {
                QueueStatsSync();
            }

            return true;
        }

        private RpPurchaseRequest ParseRpPurchaseRequest(JObject item)
        {
            var purchaseType = JsonString(item, "purchase_type", "type", "kind", "mode");
            var renewal = JsonBool(item, false, "renewal", "is_renewal", "isRenewal")
                || ContainsWord(purchaseType, "renewal");
            var autoRenew = JsonBool(item, false, "auto_renew", "autoRenew", "is_auto_renew", "isAutoRenew", "recurring")
                || ContainsWord(purchaseType, "subscription")
                || ContainsWord(purchaseType, "auto_renew");

            return new RpPurchaseRequest
            {
                request_id = JsonString(item, "request_id", "id", "purchase_request_id", "rp_purchase_request_id"),
                steam_id64 = JsonString(item, "steam_id64", "steam_id", "player_steam_id", "player_steam_id64"),
                rp_cost = JsonInt(item, 0, "rp_cost", "cost_rp", "reward_points", "amount_rp", "amount"),
                auto_renew = autoRenew,
                renewal = renewal,
                purchase_type = purchaseType,
                subscription_id = JsonString(item, "subscription_id", "rp_subscription_id")
            };
        }

        private RpPurchaseLedgerEntry ProcessRpPurchaseRequest(RpPurchaseRequest request)
        {
            if (!IsSteamId64(request.steam_id64))
            {
                return BuildRpPurchaseResult(request, "failed", "invalid_steam_id", "RP purchase request has an invalid SteamID64.", 0, 0);
            }

            if (request.rp_cost <= 0)
            {
                return BuildRpPurchaseResult(request, "failed", "invalid_rp_cost", "RP purchase request has an invalid RP cost.", 0, 0);
            }

            int balanceBefore;
            string balanceError;

            if (!TryGetServerRewardsBalance(request.steam_id64, out balanceBefore, out balanceError))
            {
                return BuildRpPurchaseResult(request, "failed", "server_rewards_unavailable", balanceError, 0, 0);
            }

            if (balanceBefore < request.rp_cost)
            {
                return BuildRpPurchaseResult(
                    request,
                    "rejected",
                    "insufficient_rp",
                    $"Insufficient RP: required {request.rp_cost}, available {balanceBefore}.",
                    balanceBefore,
                    balanceBefore);
            }

            string debitError;

            if (!TryTakeServerRewardsPoints(request.steam_id64, request.rp_cost, out debitError))
            {
                int retryBalance;

                if (TryGetServerRewardsBalance(request.steam_id64, out retryBalance, out balanceError) && retryBalance < request.rp_cost)
                {
                    return BuildRpPurchaseResult(
                        request,
                        "rejected",
                        "insufficient_rp",
                        $"Insufficient RP: required {request.rp_cost}, available {retryBalance}.",
                        retryBalance,
                        retryBalance);
                }

                return BuildRpPurchaseResult(request, "failed", "debit_failed", debitError, balanceBefore, balanceBefore);
            }

            int balanceAfter;

            if (!TryGetServerRewardsBalance(request.steam_id64, out balanceAfter, out balanceError))
            {
                balanceAfter = Math.Max(0, balanceBefore - request.rp_cost);
            }

            return BuildRpPurchaseResult(
                request,
                "confirmed",
                "confirmed",
                $"Debited {request.rp_cost} RP from {request.steam_id64}.",
                balanceBefore,
                balanceAfter);
        }

        private RpPurchaseLedgerEntry BuildRpPurchaseResult(
            RpPurchaseRequest request,
            string status,
            string reason,
            string message,
            int balanceBefore,
            int balanceAfter)
        {
            var processedAt = DateTime.UtcNow.ToString("o");
            var isPastDueRenewal = string.Equals(status, "rejected", StringComparison.OrdinalIgnoreCase)
                && string.Equals(reason, "insufficient_rp", StringComparison.OrdinalIgnoreCase)
                && request.renewal;

            return new RpPurchaseLedgerEntry
            {
                request_id = request.request_id,
                steam_id64 = request.steam_id64,
                rp_cost = Math.Max(0, request.rp_cost),
                status = status,
                reason = reason,
                message = message,
                balance_before = Math.Max(0, balanceBefore),
                balance_after = Math.Max(0, balanceAfter),
                auto_renew = request.auto_renew,
                renewal = request.renewal,
                purchase_type = request.purchase_type,
                subscription_id = request.subscription_id,
                subscription_status = isPastDueRenewal ? "past_due" : "",
                transaction_id = $"rp:{request.request_id}",
                processed_at = processedAt,
                posted = false,
                posted_at = "",
                post_attempts = 0,
                last_post_error = ""
            };
        }

        private void PostPendingRpPurchaseResults()
        {
            LoadRpPurchaseData();

            var limit = Math.Max(1, config.RpPurchasePollLimit);
            var pending = rpPurchaseData.processed.Values
                .Where(entry => entry != null && !entry.posted)
                .OrderBy(entry => entry.processed_at ?? "")
                .Take(limit)
                .ToList();

            foreach (var entry in pending)
            {
                PostRpPurchaseResult(entry, false);
            }
        }

        private void PostRpPurchaseResult(RpPurchaseLedgerEntry entry, bool force)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.request_id))
            {
                return;
            }

            if (entry.posted && !force)
            {
                return;
            }

            if (rpResultPostsInFlight.Contains(entry.request_id))
            {
                return;
            }

            rpResultPostsInFlight.Add(entry.request_id);

            var body = BuildRpPurchaseResultPayload(entry).ToString(Formatting.None);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/rp-purchase-result.php";

            SendPost(url, body, (code, response) =>
            {
                rpResultPostsInFlight.Remove(entry.request_id);
                entry.post_attempts++;

                if (!IsSuccess(code, response, out var error))
                {
                    entry.last_post_error = error;
                    SaveRpPurchaseData();
                    PrintWarning($"RP purchase result post failed for {entry.request_id}: {error}");
                    return;
                }

                JObject payload;

                try
                {
                    payload = JObject.Parse(response);
                }
                catch (Exception ex)
                {
                    entry.last_post_error = $"invalid response ({ex.Message})";
                    SaveRpPurchaseData();
                    PrintWarning($"RP purchase result post failed for {entry.request_id}: {entry.last_post_error}");
                    return;
                }

                if (JsonToken(payload, "ok") != null && !JsonBool(payload, false, "ok"))
                {
                    entry.last_post_error = JsonString(payload, "error", "message", "reason");
                    SaveRpPurchaseData();
                    PrintWarning($"RP purchase result post failed for {entry.request_id}: {entry.last_post_error}");
                    return;
                }

                entry.posted = true;
                entry.posted_at = DateTime.UtcNow.ToString("o");
                entry.last_post_error = "";
                SaveRpPurchaseData();
            });
        }

        private JObject BuildRpPurchaseResultPayload(RpPurchaseLedgerEntry entry)
        {
            return new JObject
            {
                ["server_id"] = config.ServerId,
                ["request_id"] = entry.request_id,
                ["steam_id64"] = entry.steam_id64,
                ["status"] = entry.status,
                ["result"] = entry.status,
                ["reason"] = entry.reason,
                ["message"] = entry.message,
                ["rp_cost"] = entry.rp_cost,
                ["balance_before"] = entry.balance_before,
                ["balance_after"] = entry.balance_after,
                ["auto_renew"] = entry.auto_renew,
                ["renewal"] = entry.renewal,
                ["purchase_type"] = NullIfEmpty(entry.purchase_type),
                ["subscription_id"] = NullIfEmpty(entry.subscription_id),
                ["subscription_status"] = NullIfEmpty(entry.subscription_status),
                ["transaction_id"] = entry.transaction_id,
                ["processed_at"] = entry.processed_at
            };
        }

        private bool TryGetServerRewardsBalance(string steamId, out int balance, out string error)
        {
            balance = 0;

            if (ServerRewards == null)
            {
                error = "ServerRewards plugin is not loaded.";
                return false;
            }

            try
            {
                var result = ServerRewards.Call("CheckPoints", steamId);

                if (result == null)
                {
                    error = "ServerRewards CheckPoints returned no result.";
                    return false;
                }

                balance = Math.Max(0, Convert.ToInt32(result));
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = $"ServerRewards CheckPoints failed: {ex.Message}";
                return false;
            }
        }

        private bool TryTakeServerRewardsPoints(string steamId, int amount, out string error)
        {
            if (ServerRewards == null)
            {
                error = "ServerRewards plugin is not loaded.";
                return false;
            }

            if (amount <= 0)
            {
                error = "RP amount must be positive.";
                return false;
            }

            try
            {
                var result = ServerRewards.Call("TakePoints", steamId, amount);

                if (result is bool && (bool)result)
                {
                    error = "";
                    return true;
                }

                error = "ServerRewards rejected the RP debit.";
                return false;
            }
            catch (Exception ex)
            {
                error = $"ServerRewards TakePoints failed: {ex.Message}";
                return false;
            }
        }

        private bool TryAddServerRewardsPoints(string steamId, int amount, out string error)
        {
            if (ServerRewards == null)
            {
                error = "ServerRewards plugin is not loaded.";
                return false;
            }

            if (amount <= 0)
            {
                error = "RP amount must be positive.";
                return false;
            }

            try
            {
                var result = ServerRewards.Call("AddPoints", steamId, amount);

                if (result is bool && (bool)result)
                {
                    error = "";
                    return true;
                }

                error = "ServerRewards rejected the RP credit.";
                return false;
            }
            catch (Exception ex)
            {
                error = $"ServerRewards AddPoints failed: {ex.Message}";
                return false;
            }
        }

        private void LoadRpPurchaseData()
        {
            if (rpPurchaseData != null)
            {
                return;
            }

            rpPurchaseData = ReadDataFile<RpPurchaseLedger>(RpPurchaseDataFile) ?? new RpPurchaseLedger();

            if (rpPurchaseData.processed == null)
            {
                rpPurchaseData.processed = new Dictionary<string, RpPurchaseLedgerEntry>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var processed = new Dictionary<string, RpPurchaseLedgerEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in rpPurchaseData.processed)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && entry.Value != null)
                {
                    processed[entry.Key] = entry.Value;
                }
            }

            rpPurchaseData.processed = processed;
        }

        private void SaveRpPurchaseData()
        {
            if (rpPurchaseData == null)
            {
                return;
            }

            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(RpPurchaseDataFile, rpPurchaseData, true);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not write data file {RpPurchaseDataFile}: {ex.Message}");
            }
        }

        private void LoadDeletedGroupState()
        {
            if (deletedGroupState != null)
            {
                return;
            }

            deletedGroupState = ReadDataFile<DeletedGroupState>(DeletedGroupsDataFile) ?? new DeletedGroupState();
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in deletedGroupState.groups ?? new List<string>())
            {
                if (IsGroupName(group))
                {
                    groups.Add(group.Trim());
                }
            }

            deletedGroupState.groups = groups.OrderBy(value => value).ToList();
        }

        private void SaveDeletedGroupState()
        {
            if (deletedGroupState == null)
            {
                return;
            }

            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(DeletedGroupsDataFile, deletedGroupState, true);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not write data file {DeletedGroupsDataFile}: {ex.Message}");
            }
        }

        private bool IsDeletedManagedGroup(string group)
        {
            LoadDeletedGroupState();
            return deletedGroupState.groups.Any(item => string.Equals(item, group, StringComparison.OrdinalIgnoreCase));
        }

        private bool RememberDeletedManagedGroup(string group)
        {
            if (!IsGroupName(group))
            {
                return false;
            }

            LoadDeletedGroupState();

            if (deletedGroupState.groups.Any(item => string.Equals(item, group, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            deletedGroupState.groups.Add(group.Trim());
            deletedGroupState.groups = deletedGroupState.groups.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList();
            return true;
        }

        private bool ForgetDeletedManagedGroup(string group)
        {
            if (!IsGroupName(group))
            {
                return false;
            }

            LoadDeletedGroupState();
            var removed = deletedGroupState.groups.RemoveAll(item => string.Equals(item, group, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                deletedGroupState.groups = deletedGroupState.groups.OrderBy(value => value).ToList();
            }

            return removed > 0;
        }

        private void PruneRpPurchaseData()
        {
            LoadRpPurchaseData();

            if (config.RpPurchaseProcessedRetentionDays <= 0)
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, config.RpPurchaseProcessedRetentionDays));
            var remove = new List<string>();

            foreach (var entry in rpPurchaseData.processed)
            {
                DateTime processedAt;

                if (entry.Value == null || !entry.Value.posted || !DateTime.TryParse(entry.Value.processed_at, out processedAt))
                {
                    continue;
                }

                if (processedAt.ToUniversalTime() < cutoff)
                {
                    remove.Add(entry.Key);
                }
            }

            if (remove.Count == 0)
            {
                return;
            }

            foreach (var requestId in remove)
            {
                rpPurchaseData.processed.Remove(requestId);
            }

            SaveRpPurchaseData();
        }

        private void StartRpPointPolling()
        {
            LoadRpPointData();
            PruneRpPointData();

            if (!config.RpPointRequestsEnabled)
            {
                Puts("WebsiteVipBridge RP point request polling is disabled.");
                return;
            }

            var interval = Math.Max(10, config.RpPointRequestPollIntervalSeconds);
            timer.Once(25f, () => RunScheduled("Initial RP point request sync", SyncRpPointRequests));
            rpPointTimer = timer.Every(interval, () => RunScheduled("RP point request sync timer", SyncRpPointRequests));
            Puts($"WebsiteVipBridge polling RP point requests every {interval} seconds.");
        }

        private void SyncRpPointRequests()
        {
            if (!config.RpPointRequestsEnabled || !CanRequest())
            {
                return;
            }

            if (rpPointPollInFlight)
            {
                var staleAfter = Math.Max(10f, WebRequestTimeoutSeconds() + 5f);

                if ((DateTime.UtcNow - rpPointPollStartedAt).TotalSeconds <= staleAfter)
                {
                    Puts("RP point request sync skipped because a previous poll is still in flight.");
                    return;
                }

                PrintWarning($"RP point request poll exceeded {staleAfter:0} seconds; releasing the stale in-flight guard.");
                rpPointPollInFlight = false;
                rpPointPollGeneration++;
            }

            LoadRpPointData();
            PruneRpPointData();
            PostPendingRpPointResults();

            rpPointPollInFlight = true;
            rpPointPollStartedAt = DateTime.UtcNow;
            var pollGeneration = ++rpPointPollGeneration;
            var limit = Math.Max(1, config.RpPointRequestPollLimit);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/rp-point-requests.php?limit={limit}";

            SendGet(url, (code, response) =>
            {
                try
                {
                    if (!IsSuccess(code, response, out var error))
                    {
                        PrintWarning($"RP point request sync failed: {error}");
                        return;
                    }

                    JObject payload;

                    try
                    {
                        payload = JObject.Parse(response);
                    }
                    catch (Exception ex)
                    {
                        PrintWarning($"RP point request sync failed: invalid response ({ex.Message})");
                        return;
                    }

                    if (JsonToken(payload, "ok") != null && !JsonBool(payload, false, "ok"))
                    {
                        PrintWarning($"RP point request sync failed: {JsonString(payload, "error", "message", "reason")}");
                        return;
                    }

                    var requests = ExtractRpPointRequests(payload);
                    var handled = 0;

                    foreach (var request in requests)
                    {
                        if (HandleRpPointRequest(request))
                        {
                            handled++;
                        }
                    }

                    if (handled > 0)
                    {
                        Puts($"Handled {handled} RP point request(s).");
                    }
                }
                finally
                {
                    if (pollGeneration == rpPointPollGeneration)
                    {
                        rpPointPollInFlight = false;
                    }
                }
            });
        }

        private List<JObject> ExtractRpPointRequests(JObject payload)
        {
            var requests = new List<JObject>();
            var array = JsonToken(payload, "requests", "point_requests", "rp_point_requests") as JArray;

            if (array == null)
            {
                return requests;
            }

            foreach (var item in array.OfType<JObject>())
            {
                requests.Add(item);
            }

            return requests;
        }

        private bool HandleRpPointRequest(JObject item)
        {
            var request = ParseRpPointRequest(item);

            if (string.IsNullOrWhiteSpace(request.request_id))
            {
                PrintWarning("Ignored RP point request without request_id.");
                return false;
            }

            LoadRpPointData();

            RpPointLedgerEntry existing;

            if (rpPointData.processed.TryGetValue(request.request_id, out existing) && existing != null)
            {
                if (!string.Equals(existing.steam_id64, request.steam_id64, StringComparison.OrdinalIgnoreCase)
                    || existing.debit_rp != request.debit_rp
                    || existing.credit_rp != request.credit_rp)
                {
                    PrintWarning($"Duplicate RP point request {request.request_id} changed details; returning saved {existing.status} result.");
                }

                PostRpPointResult(existing, true);
                return true;
            }

            var entry = ProcessRpPointRequest(request);
            rpPointData.processed[entry.request_id] = entry;
            SaveRpPointData();

            Puts($"RP point request {entry.request_id} {entry.status}: {entry.message}");
            PostRpPointResult(entry, true);

            if (string.Equals(entry.status, "confirmed", StringComparison.OrdinalIgnoreCase))
            {
                QueueStatsSync();
            }

            return true;
        }

        private RpPointRequest ParseRpPointRequest(JObject item)
        {
            return new RpPointRequest
            {
                request_id = JsonString(item, "request_id", "id", "point_request_id", "rp_point_request_id"),
                steam_id64 = JsonString(item, "steam_id64", "steam_id", "player_steam_id", "player_steam_id64"),
                source_type = JsonString(item, "source_type", "type", "kind"),
                source_id = JsonString(item, "source_id", "source", "round_id", "claim_id"),
                debit_rp = JsonInt(item, 0, "debit_rp", "debit", "cost_rp", "stake_rp"),
                credit_rp = JsonInt(item, 0, "credit_rp", "credit", "reward_rp", "payout_rp"),
                reason = JsonString(item, "reason", "message", "description")
            };
        }

        private RpPointLedgerEntry ProcessRpPointRequest(RpPointRequest request)
        {
            if (!IsSteamId64(request.steam_id64))
            {
                return BuildRpPointResult(request, "failed", "invalid_steam_id", "RP point request has an invalid SteamID64.", 0, 0);
            }

            request.debit_rp = Math.Max(0, request.debit_rp);
            request.credit_rp = Math.Max(0, request.credit_rp);

            if (request.debit_rp <= 0 && request.credit_rp <= 0)
            {
                return BuildRpPointResult(request, "failed", "invalid_point_amount", "RP point request has no debit or credit amount.", 0, 0);
            }

            int balanceBefore;
            string balanceError;

            if (!TryGetServerRewardsBalance(request.steam_id64, out balanceBefore, out balanceError))
            {
                return BuildRpPointResult(request, "failed", "server_rewards_unavailable", balanceError, 0, 0);
            }

            if (request.debit_rp > 0 && balanceBefore < request.debit_rp)
            {
                return BuildRpPointResult(
                    request,
                    "rejected",
                    "insufficient_rp",
                    $"Insufficient RP: required {request.debit_rp}, available {balanceBefore}.",
                    balanceBefore,
                    balanceBefore);
            }

            if (request.debit_rp > 0)
            {
                string debitError;

                if (!TryTakeServerRewardsPoints(request.steam_id64, request.debit_rp, out debitError))
                {
                    int retryBalance;

                    if (TryGetServerRewardsBalance(request.steam_id64, out retryBalance, out balanceError) && retryBalance < request.debit_rp)
                    {
                        return BuildRpPointResult(
                            request,
                            "rejected",
                            "insufficient_rp",
                            $"Insufficient RP: required {request.debit_rp}, available {retryBalance}.",
                            retryBalance,
                            retryBalance);
                    }

                    return BuildRpPointResult(request, "failed", "debit_failed", debitError, balanceBefore, balanceBefore);
                }
            }

            if (request.credit_rp > 0)
            {
                string creditError;

                if (!TryAddServerRewardsPoints(request.steam_id64, request.credit_rp, out creditError))
                {
                    if (request.debit_rp > 0)
                    {
                        string refundError;
                        TryAddServerRewardsPoints(request.steam_id64, request.debit_rp, out refundError);
                    }

                    int failedBalance;
                    if (!TryGetServerRewardsBalance(request.steam_id64, out failedBalance, out balanceError))
                    {
                        failedBalance = Math.Max(0, balanceBefore - request.debit_rp);
                    }

                    return BuildRpPointResult(request, "failed", "credit_failed", creditError, balanceBefore, failedBalance);
                }
            }

            int balanceAfter;

            if (!TryGetServerRewardsBalance(request.steam_id64, out balanceAfter, out balanceError))
            {
                balanceAfter = Math.Max(0, balanceBefore - request.debit_rp + request.credit_rp);
            }

            var action = request.debit_rp > 0 && request.credit_rp > 0
                ? $"Debited {request.debit_rp} RP and credited {request.credit_rp} RP"
                : (request.debit_rp > 0 ? $"Debited {request.debit_rp} RP" : $"Credited {request.credit_rp} RP");

            return BuildRpPointResult(
                request,
                "confirmed",
                "confirmed",
                $"{action} for {request.steam_id64}.",
                balanceBefore,
                balanceAfter);
        }

        private RpPointLedgerEntry BuildRpPointResult(
            RpPointRequest request,
            string status,
            string reason,
            string message,
            int balanceBefore,
            int balanceAfter)
        {
            return new RpPointLedgerEntry
            {
                request_id = request.request_id,
                steam_id64 = request.steam_id64,
                source_type = request.source_type,
                source_id = request.source_id,
                debit_rp = Math.Max(0, request.debit_rp),
                credit_rp = Math.Max(0, request.credit_rp),
                status = status,
                reason = reason,
                message = message,
                balance_before = Math.Max(0, balanceBefore),
                balance_after = Math.Max(0, balanceAfter),
                transaction_id = $"rp-point:{request.request_id}",
                processed_at = DateTime.UtcNow.ToString("o"),
                posted = false,
                posted_at = "",
                post_attempts = 0,
                last_post_error = ""
            };
        }

        private void PostPendingRpPointResults()
        {
            LoadRpPointData();

            var limit = Math.Max(1, config.RpPointRequestPollLimit);
            var pending = rpPointData.processed.Values
                .Where(entry => entry != null && !entry.posted)
                .OrderBy(entry => entry.processed_at ?? "")
                .Take(limit)
                .ToList();

            foreach (var entry in pending)
            {
                PostRpPointResult(entry, false);
            }
        }

        private void PostRpPointResult(RpPointLedgerEntry entry, bool force)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.request_id))
            {
                return;
            }

            if (entry.posted && !force)
            {
                return;
            }

            if (rpPointResultPostsInFlight.Contains(entry.request_id))
            {
                return;
            }

            rpPointResultPostsInFlight.Add(entry.request_id);

            var body = BuildRpPointResultPayload(entry).ToString(Formatting.None);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/rp-point-result.php";

            SendPost(url, body, (code, response) =>
            {
                rpPointResultPostsInFlight.Remove(entry.request_id);
                entry.post_attempts++;

                if (!IsSuccess(code, response, out var error))
                {
                    entry.last_post_error = error;
                    SaveRpPointData();
                    PrintWarning($"RP point result post failed for {entry.request_id}: {error}");
                    return;
                }

                JObject payload;

                try
                {
                    payload = JObject.Parse(response);
                }
                catch (Exception ex)
                {
                    entry.last_post_error = $"invalid response ({ex.Message})";
                    SaveRpPointData();
                    PrintWarning($"RP point result post failed for {entry.request_id}: {entry.last_post_error}");
                    return;
                }

                if (JsonToken(payload, "ok") != null && !JsonBool(payload, false, "ok"))
                {
                    entry.last_post_error = JsonString(payload, "error", "message", "reason");
                    SaveRpPointData();
                    PrintWarning($"RP point result post failed for {entry.request_id}: {entry.last_post_error}");
                    return;
                }

                entry.posted = true;
                entry.posted_at = DateTime.UtcNow.ToString("o");
                entry.last_post_error = "";
                SaveRpPointData();
            });
        }

        private JObject BuildRpPointResultPayload(RpPointLedgerEntry entry)
        {
            return new JObject
            {
                ["server_id"] = config.ServerId,
                ["request_id"] = entry.request_id,
                ["steam_id64"] = entry.steam_id64,
                ["source_type"] = entry.source_type,
                ["source_id"] = entry.source_id,
                ["status"] = entry.status,
                ["result"] = entry.status,
                ["reason"] = entry.reason,
                ["message"] = entry.message,
                ["debit_rp"] = entry.debit_rp,
                ["credit_rp"] = entry.credit_rp,
                ["balance_before"] = entry.balance_before,
                ["balance_after"] = entry.balance_after,
                ["transaction_id"] = entry.transaction_id,
                ["processed_at"] = entry.processed_at
            };
        }

        private void LoadRpPointData()
        {
            if (rpPointData != null)
            {
                return;
            }

            rpPointData = ReadDataFile<RpPointLedger>(RpPointDataFile) ?? new RpPointLedger();

            if (rpPointData.processed == null)
            {
                rpPointData.processed = new Dictionary<string, RpPointLedgerEntry>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var processed = new Dictionary<string, RpPointLedgerEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in rpPointData.processed)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && entry.Value != null)
                {
                    processed[entry.Key] = entry.Value;
                }
            }

            rpPointData.processed = processed;
        }

        private void SaveRpPointData()
        {
            if (rpPointData == null)
            {
                return;
            }

            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(RpPointDataFile, rpPointData, true);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not write data file {RpPointDataFile}: {ex.Message}");
            }
        }

        private void PruneRpPointData()
        {
            LoadRpPointData();

            if (config.RpPointRequestProcessedRetentionDays <= 0)
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, config.RpPointRequestProcessedRetentionDays));
            var remove = new List<string>();

            foreach (var entry in rpPointData.processed)
            {
                DateTime processedAt;

                if (entry.Value == null || !entry.Value.posted || !DateTime.TryParse(entry.Value.processed_at, out processedAt))
                {
                    continue;
                }

                if (processedAt.ToUniversalTime() < cutoff)
                {
                    remove.Add(entry.Key);
                }
            }

            if (remove.Count == 0)
            {
                return;
            }

            foreach (var requestId in remove)
            {
                rpPointData.processed.Remove(requestId);
            }

            SaveRpPointData();
        }

        private void StartStatusHeartbeat()
        {
            if (!config.StatusHeartbeatEnabled)
            {
                Puts("WebsiteVipBridge status heartbeat is disabled.");
                return;
            }

            var interval = Math.Max(15, config.StatusHeartbeatIntervalSeconds);
            timer.Once(5f, () => RunScheduled("Initial status heartbeat", SyncStatusHeartbeat));
            statusHeartbeatTimer = timer.Every(interval, () => RunScheduled("Status heartbeat timer", SyncStatusHeartbeat));
            Puts($"WebsiteVipBridge posting server status heartbeat every {interval} seconds.");
        }

        private void QueueStatusHeartbeat()
        {
            if (!config.StatusHeartbeatEnabled || !CanRequest())
            {
                return;
            }

            pendingStatusHeartbeatTimer?.Destroy();
            pendingStatusHeartbeatTimer = timer.Once(Math.Max(3, config.StatusHeartbeatDebounceSeconds), () => RunScheduled("Queued status heartbeat", SyncStatusHeartbeat));
        }

        private void SyncStatusHeartbeat()
        {
            pendingStatusHeartbeatTimer?.Destroy();
            pendingStatusHeartbeatTimer = null;

            if (!config.StatusHeartbeatEnabled || !CanRequest())
            {
                return;
            }

            var heartbeat = BuildStatusHeartbeat();
            var body = JsonConvert.SerializeObject(heartbeat);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/status-heartbeat.php";

            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"Status heartbeat failed: {error}");
                    return;
                }

                var payload = JsonConvert.DeserializeObject<StatusHeartbeatResponse>(response);

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"Status heartbeat failed: {payload?.error ?? "invalid response"}");
                    return;
                }

                lastStatusHeartbeatAt = DateTime.UtcNow;
            });
        }

        private StatusHeartbeat BuildStatusHeartbeat()
        {
            var queueCounts = GetQueueCounts();
            var wipeStartedAt = GetWipeStartedAt();

            return new StatusHeartbeat
            {
                server_id = config.ServerId,
                generated_at = DateTime.UtcNow.ToString("o"),
                online = true,
                status = "online",
                status_label = "Online",
                name = FirstNonEmpty(server.Name, ConVar.Server.hostname),
                players = SafeCountActivePlayers(),
                max_players = Math.Max(0, ConVar.Server.maxplayers),
                queue = queueCounts.queued,
                joining = queueCounts.joining,
                sleepers = SafeCountSleepers(),
                server_fps = ToInt(Performance.current.frameRate).ToString(),
                server_fps_average = ToInt(Performance.current.frameRateAverage).ToString(),
                entity_count = SafeCountEntities(),
                map_name = GetMapDisplayName(),
                world_size = Math.Max(0, ConVar.Server.worldsize),
                seed = Math.Max(0, ConVar.Server.seed),
                wipe_key = ResolveWipeKey(wipeStartedAt),
                wipe_started_at = wipeStartedAt == DateTime.MinValue ? null : wipeStartedAt.ToString("o"),
                details = new JObject
                {
                    ["server_port"] = ConVar.Server.port,
                    ["save_file"] = World.SaveFileName ?? "",
                    ["protocol"] = Rust.Protocol.network,
                    ["status_heartbeat_interval_seconds"] = Math.Max(15, config.StatusHeartbeatIntervalSeconds)
                }
            };
        }

        private QueueCounts GetQueueCounts()
        {
            try
            {
                var info = ConVar.Admin.ServerInfo();
                return new QueueCounts
                {
                    queued = Math.Max(0, info.Queued),
                    joining = Math.Max(0, info.Joining)
                };
            }
            catch
            {
                var queue = ServerMgr.Instance?.connectionQueue;
                return new QueueCounts
                {
                    queued = Math.Max(0, queue?.Queued ?? 0),
                    joining = Math.Max(0, queue?.Joining ?? 0)
                };
            }
        }

        private int SafeCountActivePlayers()
        {
            return Math.Max(0, BasePlayer.activePlayerList?.Count ?? players.Connected.Count());
        }

        private int SafeCountSleepers()
        {
            return Math.Max(0, BasePlayer.sleepingPlayerList?.Count ?? 0);
        }

        private int SafeCountEntities()
        {
            return Math.Max(0, BaseNetworkable.serverEntities?.Count ?? 0);
        }

        private string GetMapDisplayName()
        {
            var saveFile = World.SaveFileName ?? "";

            if (saveFile.StartsWith("proceduralmap", StringComparison.OrdinalIgnoreCase))
            {
                return "Procedural Map";
            }

            if (!string.IsNullOrWhiteSpace(saveFile))
            {
                var name = Path.GetFileNameWithoutExtension(saveFile).Replace('_', ' ').Replace('-', ' ').Trim();
                return string.IsNullOrWhiteSpace(name) ? "Procedural Map" : name;
            }

            return "Procedural Map";
        }

        private DateTime GetWipeStartedAt()
        {
            try
            {
                return SaveRestore.SaveCreatedTime.ToUniversalTime();
            }
            catch
            {
                var configured = ResolveSecretValue(config.WipeStartedAt);

                if (string.IsNullOrWhiteSpace(configured))
                {
                    return DateTime.MinValue;
                }

                DateTime parsed;
                return DateTime.TryParse(configured, out parsed) ? parsed.ToUniversalTime() : DateTime.MinValue;
            }
        }

        private void QueueStatsSync()
        {
            if (!config.StatsEnabled || !CanRequest())
            {
                return;
            }

            pendingStatsTimer?.Destroy();
            pendingStatsTimer = timer.Once(Math.Max(5, config.StatsDebounceSeconds), () => RunScheduled("Queued stats sync", SyncStatsSnapshot));
        }

        private void SyncStatsSnapshot()
        {
            pendingStatsTimer?.Destroy();
            pendingStatsTimer = null;

            if (!config.StatsEnabled || !CanRequest())
            {
                return;
            }

            var snapshot = BuildStatsSnapshot();
            var body = JsonConvert.SerializeObject(snapshot);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/stats-snapshot.php";

            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"Stats snapshot sync failed: {error}");
                    return;
                }

                var payload = JsonConvert.DeserializeObject<StatsResponse>(response);

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"Stats snapshot sync failed: {payload?.error ?? "invalid response"}");
                    return;
                }

                Puts($"Stats snapshot synced for {snapshot.players.Count} players and {snapshot.bots.Count} bots.");
            });
        }

        private StatsSnapshot BuildStatsSnapshot()
        {
            var playersById = new Dictionary<string, StatsPlayer>();
            var wipeStartedAt = GetWipeStartedAt();

            AddKdrStats(playersById);
            AddPlaytimeStats(playersById);
            AddRewardPoints(playersById);
            AddConnectedPlayers(playersById);
            var botStats = AddRoamBotStats(playersById);

            return new StatsSnapshot
            {
                wipe_key = ResolveWipeKey(wipeStartedAt),
                wipe_started_at = wipeStartedAt == DateTime.MinValue ? null : wipeStartedAt.ToUniversalTime().ToString("o"),
                generated_at = DateTime.UtcNow.ToString("o"),
                players = playersById.Values
                    .OrderByDescending(player => player.kills)
                    .ThenByDescending(player => player.playtime_seconds)
                    .ThenBy(player => player.steam_id64)
                    .ToList(),
                bots = botStats
            };
        }

        private void AddKdrStats(Dictionary<string, StatsPlayer> playersById)
        {
            var directory = Path.Combine(Interface.Oxide.DataFileSystem.Directory, "KDRScoreboard");

            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    var data = JsonConvert.DeserializeObject<KdrData>(File.ReadAllText(path));

                    if (data == null || data.id == 0)
                    {
                        continue;
                    }

                    var steamId = data.id.ToString();

                    if (!IsSteamId64(steamId))
                    {
                        continue;
                    }

                    var player = EnsureStatsPlayer(playersById, steamId);
                    player.display_name = FirstNonEmpty(player.display_name, data.name);
                    player.kills = Math.Max(0, data.kills);
                    player.deaths = Math.Max(0, data.deaths);
                }
                catch (Exception ex)
                {
                    PrintWarning($"Could not read KDR stats from {Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }

        private void AddPlaytimeStats(Dictionary<string, StatsPlayer> playersById)
        {
            var data = ReadDataFile<PlaytimeData>("PlaytimeTracker/user_data");

            if (data?._userData == null)
            {
                return;
            }

            foreach (var entry in data._userData)
            {
                if (!IsSteamId64(entry.Key) || entry.Value == null)
                {
                    continue;
                }

                var player = EnsureStatsPlayer(playersById, entry.Key);
                player.display_name = FirstNonEmpty(player.display_name, entry.Value.displayName);
                player.playtime_seconds = Math.Max(0, ToInt(Math.Max(entry.Value.PlayTime, entry.Value.playtime)));
                player.afk_seconds = Math.Max(0, ToInt(Math.Max(entry.Value.AFKTime, entry.Value.afkTime)));
            }
        }

        private void AddRewardPoints(Dictionary<string, StatsPlayer> playersById)
        {
            var balances = ReadDataFile<Dictionary<string, int>>("ServerRewards/player_balances");

            if (balances == null)
            {
                return;
            }

            var canCheckLiveBalances = ServerRewards != null;

            foreach (var entry in balances)
            {
                if (!IsSteamId64(entry.Key))
                {
                    continue;
                }

                var rewardPoints = Math.Max(0, entry.Value);
                int liveBalance;
                string balanceError;

                if (canCheckLiveBalances && TryGetServerRewardsBalance(entry.Key, out liveBalance, out balanceError))
                {
                    rewardPoints = Math.Max(0, liveBalance);
                }

                var player = EnsureStatsPlayer(playersById, entry.Key);
                player.reward_points = rewardPoints;
            }
        }

        private void AddConnectedPlayers(Dictionary<string, StatsPlayer> playersById)
        {
            foreach (var player in players.Connected)
            {
                if (player == null || !IsSteamId64(player.Id))
                {
                    continue;
                }

                var statsPlayer = EnsureStatsPlayer(playersById, player.Id);
                statsPlayer.display_name = FirstNonEmpty(statsPlayer.display_name, player.Name);
            }
        }

        private List<StatsBot> AddRoamBotStats(Dictionary<string, StatsPlayer> playersById)
        {
            var data = ReadDataFile<RoamBotStatsData>("RaidlandsRoamBots/stats");
            var bots = new List<StatsBot>();

            if (data == null)
            {
                return bots;
            }

            foreach (var entry in data.players ?? new Dictionary<string, RoamBotPlayerStats>())
            {
                var source = entry.Value;
                var steamId = source?.steam_id64;

                if (string.IsNullOrWhiteSpace(steamId))
                {
                    steamId = entry.Key;
                }

                if (!IsSteamId64(steamId))
                {
                    continue;
                }

                var player = EnsureStatsPlayer(playersById, steamId);
                player.display_name = FirstNonEmpty(player.display_name, source?.display_name);
                player.npc_kills = Math.Max(0, source?.npc_kills ?? 0);
                player.deaths_by_npc = Math.Max(0, source?.deaths_by_npc ?? 0);
            }

            foreach (var entry in data.bots ?? new Dictionary<string, RoamBotBotStats>())
            {
                var source = entry.Value;
                var botKey = source?.bot_key;

                if (string.IsNullOrWhiteSpace(botKey))
                {
                    botKey = entry.Key;
                }

                if (string.IsNullOrWhiteSpace(botKey))
                {
                    continue;
                }

                bots.Add(new StatsBot
                {
                    bot_key = botKey,
                    display_name = source?.display_name ?? botKey,
                    kit_name = source?.kit_name ?? "",
                    skill_tier = source?.skill_tier ?? "",
                    behavior_model_key = source?.behavior_model_key ?? "",
                    player_profile_key = source?.player_profile_key ?? "",
                    clan_key = source?.clan_key ?? "",
                    clan_tag = source?.clan_tag ?? "",
                    clan_name = source?.clan_name ?? "",
                    squad_role = source?.squad_role ?? "",
                    team_id = Math.Max(0, source?.team_id ?? 0),
                    spawns = Math.Max(0, source?.spawns ?? 0),
                    kills = Math.Max(0, source?.kills ?? 0),
                    deaths = Math.Max(0, source?.deaths ?? 0)
                });
            }

            var ordered = bots
                .OrderByDescending(bot => bot.kills)
                .ThenByDescending(bot => bot.spawns)
                .ThenBy(bot => bot.deaths)
                .ThenBy(bot => bot.bot_key)
                .ToList();

            var limit = Math.Max(0, config.StatsBotSnapshotLimit);

            return limit > 0 && ordered.Count > limit
                ? ordered.Take(limit).ToList()
                : ordered;
        }

        private void StartHeatmapSync()
        {
            if (!IsHeatmapEnabled())
            {
                Puts("WebsiteVipBridge heatmap sync is disabled.");
                return;
            }

            var interval = Math.Max(60, config.HeatmapSyncIntervalSeconds);
            heatmapTimer = timer.Every(interval, () => RunScheduled("Heatmap sync timer", SyncHeatmapSnapshot));
            Puts($"WebsiteVipBridge syncing heatmap aggregates every {interval} seconds with {Math.Max(1, config.HeatmapBucketSize)}m buckets.");
        }

        private bool IsHeatmapEnabled()
        {
            return config != null && config.HeatmapEnabled;
        }

        private bool HeatmapMetricEnabled(string metric)
        {
            return config?.HeatmapMetrics != null
                && config.HeatmapMetrics.Any(item => string.Equals(item, metric, StringComparison.OrdinalIgnoreCase));
        }

        private void AddHeatmapMetric(Vector3 position, string metric)
        {
            if (!IsHeatmapEnabled() || !HeatmapMetricEnabled(metric))
            {
                return;
            }

            var bucket = HeatmapBucketForPosition(position);

            switch (metric)
            {
                case "player_deaths":
                    bucket.player_deaths++;
                    break;
                case "pvp_kills":
                    bucket.pvp_kills++;
                    break;
                case "npc_deaths":
                    bucket.npc_deaths++;
                    break;
                case "deaths_by_npc":
                    bucket.deaths_by_npc++;
                    break;
                case "roambots_activity":
                    bucket.roambots_activity++;
                    break;
            }
        }

        private HeatmapBucket HeatmapBucketForPosition(Vector3 position)
        {
            var bucketSize = Math.Max(1, config.HeatmapBucketSize);
            var bucketX = Mathf.FloorToInt(position.x / bucketSize);
            var bucketZ = Mathf.FloorToInt(position.z / bucketSize);
            var key = $"{bucketX}:{bucketZ}";

            HeatmapBucket bucket;
            if (!heatmapBuckets.TryGetValue(key, out bucket))
            {
                bucket = new HeatmapBucket
                {
                    bucket_x = bucketX,
                    bucket_z = bucketZ,
                    center_x = Mathf.RoundToInt((bucketX + 0.5f) * bucketSize),
                    center_z = Mathf.RoundToInt((bucketZ + 0.5f) * bucketSize)
                };
                heatmapBuckets[key] = bucket;
            }

            return bucket;
        }

        private void AddConnectedPlayerHeatmapPositions()
        {
            if (!config.HeatmapIncludeOnlinePositions || !HeatmapMetricEnabled("online_positions"))
            {
                return;
            }

            foreach (var connected in BasePlayer.activePlayerList)
            {
                if (connected == null || !IsRealPlayerSteamId(connected.userID))
                {
                    continue;
                }

                HeatmapBucketForPosition(connected.transform.position).online_positions++;
            }
        }

        private bool IsRaidlandsRoamBot(BasePlayer player)
        {
            if (player == null)
            {
                return false;
            }

            return (player.UserIDString ?? "").StartsWith("90", StringComparison.Ordinal)
                || (player.displayName ?? "").IndexOf("Roam", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsRealPlayerSteamId(ulong userId)
        {
            return IsSteamId64(userId.ToString());
        }

        private void SyncHeatmapSnapshot()
        {
            if (!IsHeatmapEnabled() || !CanRequest() || heatmapEndpointMissing)
            {
                return;
            }

            AddConnectedPlayerHeatmapPositions();

            if (heatmapBuckets.Count == 0)
            {
                lastHeatmapSyncAt = DateTime.UtcNow;
                return;
            }

            var now = DateTime.UtcNow;
            var wipeStartedAt = GetWipeStartedAt();
            var snapshot = new HeatmapSnapshot
            {
                server_id = config.ServerId,
                wipe_key = ResolveWipeKey(wipeStartedAt),
                wipe_started_at = wipeStartedAt == DateTime.MinValue ? null : wipeStartedAt.ToUniversalTime().ToString("o"),
                generated_at = now.ToString("o"),
                window_started_at = heatmapWindowStartedAt.ToString("o"),
                window_ended_at = now.ToString("o"),
                bucket_size = Math.Max(1, config.HeatmapBucketSize),
                retention_hours = Math.Max(1, config.HeatmapRetentionHours),
                includes_online_positions = config.HeatmapIncludeOnlinePositions,
                buckets = heatmapBuckets.Values
                    .OrderBy(bucket => bucket.bucket_x)
                    .ThenBy(bucket => bucket.bucket_z)
                    .ToList()
            };

            var body = JsonConvert.SerializeObject(snapshot);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/heatmap-snapshot.php";

            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    if (code == 404)
                    {
                        heatmapEndpointMissing = true;
                        PrintWarning("Heatmap snapshot endpoint returned 404. Heatmap posting is paused until WebsiteVipBridge is reloaded or websitevip.heatmap.sync is run after the website endpoint is deployed.");
                        return;
                    }

                    PrintWarning($"Heatmap snapshot sync failed: {error}");
                    return;
                }

                var payload = JsonConvert.DeserializeObject<StatsResponse>(response);

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"Heatmap snapshot sync failed: {payload?.error ?? "invalid response"}");
                    return;
                }

                heatmapBuckets.Clear();
                heatmapWindowStartedAt = DateTime.UtcNow;
                lastHeatmapSyncAt = heatmapWindowStartedAt;
                Puts($"Heatmap snapshot synced for {snapshot.buckets.Count} bucket(s).");
            });
        }

        private void ClearHeatmapWipe()
        {
            heatmapBuckets.Clear();
            heatmapWindowStartedAt = DateTime.UtcNow;
            lastHeatmapSyncAt = DateTime.MinValue;
        }

        private StatsPlayer EnsureStatsPlayer(Dictionary<string, StatsPlayer> playersById, string steamId)
        {
            StatsPlayer player;

            if (!playersById.TryGetValue(steamId, out player))
            {
                player = new StatsPlayer
                {
                    steam_id64 = steamId,
                    display_name = ""
                };
                playersById[steamId] = player;
            }

            return player;
        }

        private T ReadDataFile<T>(string fileName)
        {
            try
            {
                if (!Interface.Oxide.DataFileSystem.ExistsDatafile(fileName))
                {
                    return default(T);
                }

                return Interface.Oxide.DataFileSystem.ReadObject<T>(fileName);
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read data file {fileName}: {ex.Message}");
                return default(T);
            }
        }

        private void StartKitSync()
        {
            if (!config.KitSyncEnabled)
            {
                Puts("WebsiteVipBridge kit sync is disabled.");
                return;
            }

            var interval = Math.Max(60, config.KitSyncIntervalSeconds);
            pendingKitSnapshotTimer = timer.Once(15f, () => RunScheduled("Initial kit snapshot", PostKitSnapshot));
            timer.Once(25f, () => RunScheduled("Initial kit sync", SyncKits));
            kitSyncTimer = timer.Every(interval, () => RunScheduled("Kit sync timer", SyncKits));
            Puts($"WebsiteVipBridge syncing kits every {interval} seconds.");
        }

        private void PostKitSnapshot()
        {
            if (!config.KitSyncEnabled || !CanRequest())
            {
                return;
            }

            try
            {
                var body = new JObject
                {
                    ["server_id"] = config.ServerId,
                    ["generated_at"] = DateTime.UtcNow.ToString("o"),
                    ["kits_data"] = ReadJsonFile(KitDataPath("Kits", "kits_data.json")),
                    ["server_rewards"] = ReadJsonFile(KitDataPath("ServerRewards", "products.json")),
                    ["groups"] = JObject.FromObject(CurrentKitGroupPermissions())
                }.ToString(Formatting.None);
                var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/kits-snapshot.php";

                SendPost(url, body, (code, response) =>
                {
                    if (!IsSuccess(code, response, out var error))
                    {
                        PrintWarning($"Kit snapshot post failed: {error}");
                        return;
                    }

                    var payload = JsonConvert.DeserializeObject<KitResultResponse>(response);

                    if (payload == null || !payload.ok)
                    {
                        PrintWarning($"Kit snapshot post failed: {payload?.error ?? "invalid response"}");
                        return;
                    }

                    Puts("Posted kit snapshot to website.");
                });
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not build kit snapshot: {ex.Message}");
            }
        }

        private void SyncKits()
        {
            if (!config.KitSyncEnabled || !CanRequest())
            {
                return;
            }

            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/kits-sync.php?since={kitRevision}";

            SendGet(url, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"Kit sync check failed: {error}");
                    return;
                }

                KitSyncResponse payload;

                try
                {
                    payload = JsonConvert.DeserializeObject<KitSyncResponse>(response);
                }
                catch (Exception ex)
                {
                    PrintWarning($"Kit sync check failed: invalid response ({ex.Message})");
                    return;
                }

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"Kit sync check failed: {payload?.error ?? "invalid response"}");
                    return;
                }

                if (!payload.has_update)
                {
                    if (payload.revision > kitRevision)
                    {
                        kitRevision = payload.revision;
                    }

                    Puts($"Kit sync has no update; website revision={payload.revision}, local revision={kitRevision}.");
                    return;
                }

                var validation = ValidateKitSyncPayload(payload);

                if (validation.Count > 0)
                {
                    var message = string.Join("; ", validation.Take(8).ToArray());
                    PrintWarning($"Kit sync revision {payload.revision} rejected: {message}");
                    PostKitSyncResult(payload.revision, false, message);
                    return;
                }

                try
                {
                    ApplyKitSyncPayload(payload);
                }
                catch (Exception ex)
                {
                    PrintWarning($"Kit sync revision {payload.revision} failed: {ex.Message}");
                    PostKitSyncResult(payload.revision, false, ex.Message);
                }
            });
        }

        private List<string> ValidateKitSyncPayload(KitSyncResponse payload)
        {
            var errors = new List<string>();

            if (payload.kits == null || payload.kits.Count == 0)
            {
                errors.Add("Published payload did not include any kits.");
                return errors;
            }

            foreach (var kit in payload.kits)
            {
                if (kit == null)
                {
                    errors.Add("Kit entry is empty.");
                    continue;
                }

                var name = KitString(kit, "Name");
                var active = KitBool(kit, "IsActive", true);

                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add("A kit is missing Name.");
                    continue;
                }

                if (!IsSafeKitName(name))
                {
                    errors.Add($"Kit {name} uses an unsafe name.");
                }

                foreach (var previousName in KitPreviousNames(kit))
                {
                    if (!IsSafeKitName(previousName))
                    {
                        errors.Add($"Kit {name} uses an unsafe previous name.");
                    }
                }

                var permissionName = KitString(kit, "RequiredPermission");

                if (!string.IsNullOrWhiteSpace(permissionName) && !IsSafeKitPermission(permissionName))
                {
                    errors.Add($"Kit {name} uses unsafe permission {permissionName}.");
                }

                var image = KitString(kit, "KitImage");

                if (!IsSafeKitImage(image))
                {
                    errors.Add($"Kit {name} has an unsafe image path.");
                }

                if (!active)
                {
                    continue;
                }

                ValidateKitItems(errors, name, kit["MainItems"] as JArray, 24);
                ValidateKitItems(errors, name, kit["WearItems"] as JArray, 8);
                ValidateKitItems(errors, name, kit["BeltItems"] as JArray, 6);
            }

            foreach (var item in payload.server_rewards_kits ?? new List<JObject>())
            {
                var kitName = KitString(item, "KitName");

                if (string.IsNullOrWhiteSpace(kitName) || !IsSafeKitName(kitName))
                {
                    errors.Add("ServerRewards kit row has an unsafe KitName.");
                }

                var icon = KitString(item, "IconURL");

                if (!IsSafeKitImage(icon))
                {
                    errors.Add($"ServerRewards kit {kitName} has an unsafe icon path.");
                }

                var permissionName = KitString(item, "Permission");

                if (!string.IsNullOrWhiteSpace(permissionName) && !IsSafeKitPermission(permissionName))
                {
                    errors.Add($"ServerRewards kit {kitName} has unsafe permission {permissionName}.");
                }
            }

            return errors;
        }

        private void ValidateKitItems(List<string> errors, string kitName, JArray items, int capacity)
        {
            if (items == null)
            {
                return;
            }

            foreach (var token in items)
            {
                var item = token as JObject;

                if (item == null)
                {
                    errors.Add($"Kit {kitName} has an invalid item row.");
                    continue;
                }

                var shortname = KitString(item, "Shortname");

                if (string.IsNullOrWhiteSpace(shortname) || !IsSafeShortname(shortname))
                {
                    errors.Add($"Kit {kitName} has unsafe item shortname {shortname}.");
                    continue;
                }

                if (ItemManager.FindItemDefinition(shortname) == null)
                {
                    errors.Add($"Kit {kitName} references unknown item {shortname}.");
                }

                var amount = KitInt(item, "Amount", 1);

                if (amount <= 0)
                {
                    errors.Add($"Kit {kitName} item {shortname} has invalid amount.");
                }

                var position = KitInt(item, "Position", 0);

                if (position < 0 || position >= capacity)
                {
                    errors.Add($"Kit {kitName} item {shortname} has slot {position} outside capacity {capacity}.");
                }
            }
        }

        private void ApplyKitSyncPayload(KitSyncResponse payload)
        {
            var backups = BackupKitData(payload.revision);
            var kitsPath = KitDataPath("Kits", "kits_data.json");
            var kitsData = ReadJsonFile(kitsPath);
            var kitsRoot = kitsData["_kits"] as JObject ?? new JObject();

            foreach (var kit in payload.kits ?? new List<JObject>())
            {
                var name = KitString(kit, "Name");
                foreach (var previousName in KitPreviousNames(kit))
                {
                    if (!string.IsNullOrWhiteSpace(previousName) && !previousName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        kitsRoot.Remove(previousName);
                    }
                }

                if (!KitBool(kit, "IsActive", true))
                {
                    kitsRoot.Remove(name);
                    continue;
                }

                kitsRoot[name] = NormalizeKitForData(kit);
            }

            kitsData["_kits"] = kitsRoot;
            WriteJsonAtomic(kitsPath, kitsData);
            ApplyServerRewardsKitRows(payload);
            kitRevision = payload.revision;
            ReloadKitPlugins();

            timer.Once(3f, () => RunScheduled("Post-kit permission sync", () =>
            {
                SyncPermissions();
                PostKitSyncResult(payload.revision, true, $"Applied kit revision {payload.revision}; backups: {string.Join(", ", backups.Select(Path.GetFileName).ToArray())}");
            }));
        }

        private JObject NormalizeKitForData(JObject source)
        {
            return new JObject
            {
                ["Name"] = KitString(source, "Name"),
                ["Description"] = KitString(source, "Description"),
                ["RequiredPermission"] = KitString(source, "RequiredPermission"),
                ["MaximumUses"] = KitInt(source, "MaximumUses", 0),
                ["RequiredAuth"] = Math.Max(0, Math.Min(2, KitInt(source, "RequiredAuth", 0))),
                ["Cooldown"] = Math.Max(0, KitInt(source, "Cooldown", 0)),
                ["Cost"] = Math.Max(0, KitInt(source, "Cost", 0)),
                ["IsHidden"] = KitBool(source, "IsHidden", false),
                ["CopyPasteFile"] = KitString(source, "CopyPasteFile"),
                ["KitImage"] = ResolveKitImageUrl(source),
                ["MainItems"] = NormalizeItemArray(source["MainItems"] as JArray),
                ["WearItems"] = NormalizeItemArray(source["WearItems"] as JArray),
                ["BeltItems"] = NormalizeItemArray(source["BeltItems"] as JArray)
            };
        }

        private void RepairCanonicalKitImages(out int kitImagesChanged, out int rewardIconsChanged)
        {
            kitImagesChanged = RepairKitsDataImages();
            rewardIconsChanged = RepairServerRewardsKitIcons();

            if (kitImagesChanged > 0 || rewardIconsChanged > 0)
            {
                ReloadKitPlugins();
            }
        }

        private int RepairKitsDataImages()
        {
            var path = KitDataPath("Kits", "kits_data.json");
            var kitsData = ReadJsonFile(path);
            var kitsRoot = kitsData["_kits"] as JObject;

            if (kitsRoot == null)
            {
                return 0;
            }

            var changed = 0;

            foreach (var property in kitsRoot.Properties().ToList())
            {
                var kit = property.Value as JObject;

                if (kit == null)
                {
                    continue;
                }

                var image = ResolveKitImageUrl(kit, property.Name);

                if (string.IsNullOrWhiteSpace(image))
                {
                    continue;
                }

                var current = KitString(kit, "KitImage").Trim();

                if (string.Equals(current, image, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                kit["KitImage"] = image;
                changed++;
            }

            if (changed > 0)
            {
                WriteJsonAtomic(path, kitsData);
            }

            return changed;
        }

        private int RepairServerRewardsKitIcons()
        {
            var path = KitDataPath("ServerRewards", "products.json");
            var products = ReadJsonFile(path);
            var kitRows = products["Kits"] as JArray;

            if (kitRows == null)
            {
                return 0;
            }

            var changed = 0;

            foreach (var token in kitRows)
            {
                var row = token as JObject;

                if (row == null)
                {
                    continue;
                }

                var image = ResolveKitImageUrl(row, KitString(row, "KitName"), KitString(row, "Permission"));

                if (string.IsNullOrWhiteSpace(image))
                {
                    continue;
                }

                var current = KitString(row, "IconURL").Trim();

                if (string.Equals(current, image, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                row["IconURL"] = image;
                changed++;
            }

            if (changed > 0)
            {
                WriteJsonAtomic(path, products);
            }

            return changed;
        }

        private string ResolveKitImageUrl(JObject source, string fallbackName = "", string fallbackPermission = "")
        {
            var name = FirstNonEmpty(KitString(source, "Name"), FirstNonEmpty(KitString(source, "KitName"), fallbackName));
            var permissionName = FirstNonEmpty(KitString(source, "RequiredPermission"), FirstNonEmpty(KitString(source, "Permission"), fallbackPermission));
            var canonical = CanonicalKitImagePath(name, permissionName);
            var configured = FirstNonEmpty(KitString(source, "KitImage"), KitString(source, "IconURL"));

            return AssetUrl(RustKitImagePath(FirstNonEmpty(canonical, configured)));
        }

        private string CanonicalKitImagePath(string kitName, string permissionName)
        {
            string image;
            var permissionKey = NormalizePermissionName(permissionName);

            if (!string.IsNullOrWhiteSpace(permissionKey) && CanonicalKitImagePathsByPermission.TryGetValue(permissionKey, out image))
            {
                return image;
            }

            var nameKey = CanonicalKitImageNameKey(kitName);

            if (!string.IsNullOrWhiteSpace(nameKey) && CanonicalKitImagePathsByName.TryGetValue(nameKey, out image))
            {
                return image;
            }

            return "";
        }

        private static string CanonicalKitImageNameKey(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant();
        }

        private static string RustKitImagePath(string value)
        {
            return (value ?? "").Trim();
        }

        private JArray NormalizeItemArray(JArray source)
        {
            var result = new JArray();

            foreach (var token in source ?? new JArray())
            {
                var item = token as JObject;

                if (item == null)
                {
                    continue;
                }

                result.Add(NormalizeItemForData(item));
            }

            return result;
        }

        private JObject NormalizeItemForData(JObject item)
        {
            var shortname = KitString(item, "Shortname");
            var displayName = NullIfEmpty(KitString(item, "DisplayName"));
            var skin = Math.Max(0L, KitLong(item, "Skin", KitLong(item, "SkinID", 0)));

            if (IsSuperSerumShortname(shortname))
            {
                shortname = "supertea";
                displayName = new JValue("Super Serum");
                skin = 0L;
            }

            var definition = ItemManager.FindItemDefinition(shortname);
            var condition = Math.Max(0f, KitFloat(item, "Condition", 0f));
            var maxCondition = Math.Max(0f, KitFloat(item, "MaxCondition", 0f));
            var amount = Math.Max(1, KitInt(item, "Amount", 1));

            if (definition != null && definition.condition.enabled)
            {
                var defaultMaxCondition = Math.Max(0f, definition.condition.max);
                var badMaxCondition = maxCondition <= 0f || maxCondition < defaultMaxCondition || maxCondition > defaultMaxCondition;

                if (badMaxCondition)
                {
                    maxCondition = defaultMaxCondition;
                }

                if (condition <= 0f || condition < maxCondition || condition > maxCondition || badMaxCondition)
                {
                    condition = maxCondition > 0f ? maxCondition : defaultMaxCondition;
                }
            }

            return new JObject
            {
                ["Shortname"] = shortname,
                ["DisplayName"] = displayName,
                ["Skin"] = skin,
                ["Amount"] = amount,
                ["Condition"] = condition,
                ["MaxCondition"] = maxCondition,
                ["Ammo"] = Math.Max(0, KitInt(item, "Ammo", 0)),
                ["Ammotype"] = NullIfEmpty(FirstKitString(item, "Ammotype", "AmmoType")),
                ["Position"] = Math.Max(0, KitInt(item, "Position", 0)),
                ["Frequency"] = KitInt(item, "Frequency", -1),
                ["BlueprintShortname"] = NullIfEmpty(KitString(item, "BlueprintShortname")),
                ["Text"] = NullIfEmpty(KitString(item, "Text")),
                ["Contents"] = item["Contents"] is JArray contents ? contents : JValue.CreateNull(),
                ["Container"] = item["Container"] is JObject container ? container : JValue.CreateNull()
            };
        }

        private bool IsSuperSerumShortname(string shortname)
        {
            return string.Equals(shortname, "supertea", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shortname, "maxhealthtea.pure", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyServerRewardsKitRows(KitSyncResponse payload)
        {
            var path = KitDataPath("ServerRewards", "products.json");
            var products = ReadJsonFile(path);
            var existing = products["Kits"] as JArray ?? new JArray();
            var managedKitNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kit in payload.kits ?? new List<JObject>())
            {
                AddManagedKitName(managedKitNames, KitString(kit, "Name"));

                foreach (var previousName in KitPreviousNames(kit))
                {
                    AddManagedKitName(managedKitNames, previousName);
                }
            }
            var merged = new JArray();

            foreach (var token in existing)
            {
                var kit = token as JObject;
                var name = kit == null ? "" : KitString(kit, "KitName");

                if (kit != null && !managedKitNames.Contains(name))
                {
                    merged.Add(kit);
                }
            }

            var productIndex = Math.Max(0, KitInt(products, "ProductIndex", 0));

            foreach (var row in payload.server_rewards_kits ?? new List<JObject>())
            {
                var copy = new JObject
                {
                    ["KitName"] = KitString(row, "KitName"),
                    ["Description"] = KitString(row, "Description"),
                    ["ID"] = KitInt(row, "ID", -1),
                    ["DisplayName"] = KitString(row, "DisplayName"),
                    ["Cost"] = Math.Max(0, KitInt(row, "Cost", 0)),
                    ["Cooldown"] = Math.Max(0, KitInt(row, "Cooldown", 0)),
                    ["IconURL"] = ResolveKitImageUrl(row),
                    ["Permission"] = KitString(row, "Permission")
                };

                if (KitInt(copy, "ID", -1) < 0)
                {
                    copy["ID"] = productIndex++;
                }

                merged.Add(copy);
            }

            products["ProductIndex"] = productIndex;
            products["Kits"] = merged;
            WriteJsonAtomic(path, products);
        }

        private void ApplyKitGroupAccess(Dictionary<string, List<string>> groupAccess)
        {
            var managedGroups = new HashSet<string>((config.KitPermissionManagedGroups ?? new List<string>())
                .Where(group => IsKitManageableGroupName(group) && !IsDeletedManagedGroup(group)), StringComparer.OrdinalIgnoreCase);
            var desiredByGroup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in managedGroups)
            {
                if (!permission.GroupExists(group))
                {
                    permission.CreateGroup(group, group, 0);
                }

                desiredByGroup[group] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var entry in groupAccess ?? new Dictionary<string, List<string>>())
            {
                if (!managedGroups.Contains(entry.Key))
                {
                    continue;
                }

                foreach (var permissionName in entry.Value ?? new List<string>())
                {
                    var normalized = NormalizePermissionName(permissionName);

                    if (IsSafeKitPermission(normalized))
                    {
                        desiredByGroup[entry.Key].Add(normalized);
                    }
                }
            }

            foreach (var group in managedGroups)
            {
                var desired = desiredByGroup[group];
                var current = new HashSet<string>((permission.GetGroupPermissions(group, false) ?? new string[0])
                    .Select(NormalizePermissionName)
                    .Where(IsSafeKitPermission), StringComparer.OrdinalIgnoreCase);

                foreach (var permissionName in desired)
                {
                    if (!current.Contains(permissionName))
                    {
                        GrantGroupPermissionVerified(group, permissionName);
                    }
                }

                foreach (var permissionName in current)
                {
                    if (!desired.Contains(permissionName))
                    {
                        RevokeGroupPermissionVerified(group, permissionName);
                    }
                }
            }
        }

        private Dictionary<string, List<string>> CurrentKitGroupPermissions()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in config.KitPermissionManagedGroups ?? new List<string>())
            {
                if (!IsKitManageableGroupName(group) || IsDeletedManagedGroup(group) || !permission.GroupExists(group))
                {
                    continue;
                }

                result[group] = (permission.GetGroupPermissions(group, false) ?? new string[0])
                    .Where(IsSafeKitPermission)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value)
                    .ToList();
            }

            return result;
        }

        private void PostKitSyncResult(long revision, bool success, string message)
        {
            if (!CanRequest())
            {
                return;
            }

            var body = new JObject
            {
                ["revision"] = revision,
                ["status"] = success ? "applied" : "failed",
                ["ok"] = success,
                ["message"] = message ?? "",
                ["payload_hash"] = ""
            }.ToString(Formatting.None);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/kits-sync-result.php";

            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"Kit sync result post failed: {error}");
                    return;
                }

                var payload = JsonConvert.DeserializeObject<KitResultResponse>(response);

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"Kit sync result post failed: {payload?.error ?? "invalid response"}");
                }
            });
        }

        private void StartPermissionSync()
        {
            if (!config.PermissionSyncEnabled)
            {
                Puts("WebsiteVipBridge permission sync is disabled.");
                return;
            }

            var interval = Math.Max(60, config.PermissionSyncIntervalSeconds);
            QueuePermissionSnapshot("Initial permission registry snapshot", 20f);
            timer.Once(Math.Max(45, config.PermissionSnapshotSettledDelaySeconds), () => QueuePermissionSnapshot("Settled permission registry snapshot", 1f));
            timer.Once(30f, () => RunScheduled("Initial permission sync", SyncPermissions));
            permissionSyncTimer = timer.Every(interval, () => RunScheduled("Permission sync timer", SyncPermissions));
            Puts($"WebsiteVipBridge syncing permissions every {interval} seconds.");
        }

        private void QueuePermissionSnapshotForPluginChange(string context, Plugin plugin)
        {
            if (plugin == null || string.Equals(plugin.Name, Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            QueuePermissionSnapshot($"{context} permission registry snapshot ({plugin.Name})");
        }

        private void QueuePermissionSnapshot(string context, float delaySeconds = -1f)
        {
            if (config == null || !config.PermissionSyncEnabled)
            {
                return;
            }

            pendingPermissionSnapshotTimer?.Destroy();
            var delay = delaySeconds > 0 ? delaySeconds : Math.Max(5, config.PermissionSnapshotDebounceSeconds);

            pendingPermissionSnapshotTimer = timer.Once(delay, () =>
            {
                pendingPermissionSnapshotTimer = null;
                RunScheduled(context, PostPermissionSnapshot);
            });
        }

        private void PostPermissionSnapshot()
        {
            if (!config.PermissionSyncEnabled || !CanRequest())
            {
                return;
            }

            try
            {
                var groups = CurrentPermissionGroups();
                var permissions = CurrentRegisteredPermissions();
                var groupPermissions = CurrentPermissionGroupMap();
                var body = new JObject
                {
                    ["server_id"] = config.ServerId,
                    ["generated_at"] = DateTime.UtcNow.ToString("o"),
                    ["groups"] = JArray.FromObject(groups),
                    ["permissions"] = JArray.FromObject(permissions),
                    ["group_permissions"] = JObject.FromObject(groupPermissions)
                }.ToString(Formatting.None);
                var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/permissions-snapshot.php";

                SendPost(url, body, (code, response) =>
                {
                    if (!IsSuccess(code, response, out var error))
                    {
                        PrintWarning($"Permission snapshot post failed: {error}");
                        return;
                    }

                    var payload = JsonConvert.DeserializeObject<KitResultResponse>(response);

                    if (payload == null || !payload.ok)
                    {
                        PrintWarning($"Permission snapshot post failed: {payload?.error ?? "invalid response"}");
                        return;
                    }

                    Puts($"Posted permission snapshot to website ({permissions.Count} permissions, {groups.Count} groups).");
                });
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not build permission snapshot: {ex.Message}");
            }
        }

        private void SyncPermissions()
        {
            if (!config.PermissionSyncEnabled || !CanRequest())
            {
                return;
            }

            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/permissions-sync.php?since={permissionRevision}";

            SendGet(url, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"Permission sync check failed: {error}");
                    return;
                }

                PermissionSyncResponse payload;

                try
                {
                    payload = JsonConvert.DeserializeObject<PermissionSyncResponse>(response);
                }
                catch (Exception ex)
                {
                    PrintWarning($"Permission sync check failed: invalid response ({ex.Message})");
                    return;
                }

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"Permission sync check failed: {payload?.error ?? "invalid response"}");
                    return;
                }

                if (!payload.has_update)
                {
                    if (payload.revision > permissionRevision)
                    {
                        permissionRevision = payload.revision;
                    }

                    Puts($"Permission sync has no update; website revision={payload.revision}, local revision={permissionRevision}.");
                    return;
                }

                var validation = ValidatePermissionSyncPayload(payload);

                if (validation.Count > 0)
                {
                    var message = string.Join("; ", validation.Take(8).ToArray());
                    PrintWarning($"Permission sync revision {payload.revision} rejected: {message}");
                    PostPermissionSyncResult(payload.revision, false, message);
                    return;
                }

                try
                {
                    var changeCount = ApplyPermissionSyncPayload(payload);
                    permissionRevision = payload.revision;
                    PostPermissionSyncResult(payload.revision, true, $"Applied permission revision {payload.revision}; changed {changeCount} grant(s).");
                }
                catch (Exception ex)
                {
                    PrintWarning($"Permission sync revision {payload.revision} failed: {ex.Message}");
                    PostPermissionSyncResult(payload.revision, false, ex.Message);
                }
            });
        }

        private List<string> ValidatePermissionSyncPayload(PermissionSyncResponse payload)
        {
            var errors = new List<string>();

            if (payload.revision <= 0)
            {
                errors.Add("Published payload did not include a valid revision.");
            }

            var groupPermissions = JsonStringListMap(payload.group_permissions);

            if (groupPermissions == null)
            {
                errors.Add("Published payload did not include group permissions.");
                return errors;
            }

            var readOnly = PermissionReadOnlyGroups(payload);
            var managedGroups = PermissionPayloadManagedGroups(payload);
            var deletedGroups = PermissionPayloadDeletedGroups(payload);

            if (managedGroups.Count == 0 && deletedGroups.Count == 0)
            {
                errors.Add("Published payload did not include managed or deleted groups.");
            }

            foreach (var group in managedGroups)
            {
                if (!IsPermissionManageableGroupName(group) || readOnly.Contains(group))
                {
                    errors.Add($"Group {group} is not editable by the website.");
                }
            }

            foreach (var group in deletedGroups)
            {
                if (!IsPermissionManageableGroupName(group) || readOnly.Contains(group) || IsProtectedPermissionGroupName(group))
                {
                    errors.Add($"Deleted group {group} is not removable by the website.");
                }
            }

            foreach (var group in payload.groups ?? new List<JObject>())
            {
                var groupName = PermissionString(group, "name");

                if (string.IsNullOrWhiteSpace(groupName))
                {
                    errors.Add("Published payload included a group without a name.");
                    continue;
                }

                if (!IsPermissionManageableGroupName(groupName) || readOnly.Contains(groupName))
                {
                    errors.Add($"Group {groupName} is not editable by the website.");
                }
            }

            foreach (var entry in groupPermissions)
            {
                if (!managedGroups.Contains(entry.Key))
                {
                    errors.Add($"Group {entry.Key} has permissions but is not marked managed.");
                    continue;
                }

                if (readOnly.Contains(entry.Key) || !IsPermissionManageableGroupName(entry.Key))
                {
                    errors.Add($"Group {entry.Key} is not editable by the website.");
                    continue;
                }

                foreach (var permissionName in entry.Value ?? new List<string>())
                {
                    if (!IsSafePermissionName(permissionName))
                    {
                        errors.Add($"Group {entry.Key} includes unsafe permission {permissionName}.");
                    }
                }
            }

            return errors;
        }

        private int ApplyPermissionSyncPayload(PermissionSyncResponse payload)
        {
            var readOnly = PermissionReadOnlyGroups(payload);
            var managedGroups = PermissionPayloadManagedGroups(payload)
                .Where(group => IsPermissionManageableGroupName(group) && !readOnly.Contains(group))
                .ToList();
            var managedSet = new HashSet<string>(managedGroups, StringComparer.OrdinalIgnoreCase);
            var deletedGroups = PermissionPayloadDeletedGroups(payload)
                .Where(group => IsPermissionManageableGroupName(group) && !readOnly.Contains(group) && !IsProtectedPermissionGroupName(group))
                .Where(group => !managedSet.Contains(group))
                .ToList();
            var groupDetails = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in payload.groups ?? new List<JObject>())
            {
                var groupName = PermissionString(group, "name");

                if (managedGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase))
                {
                    groupDetails[groupName] = group;
                }
            }

            var desiredByGroup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in managedGroups)
            {
                JObject details;
                groupDetails.TryGetValue(group, out details);
                EnsurePermissionGroup(group, details);
                desiredByGroup[group] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var groupPermissions = JsonStringListMap(payload.group_permissions) ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in groupPermissions)
            {
                HashSet<string> desired;

                if (!desiredByGroup.TryGetValue(entry.Key, out desired))
                {
                    continue;
                }

                foreach (var permissionName in entry.Value ?? new List<string>())
                {
                    var normalized = NormalizePermissionName(permissionName);

                    if (IsSafePermissionName(normalized))
                    {
                        desired.Add(normalized);
                    }
                }
            }

            var changes = ApplyDeletedPermissionGroups(deletedGroups);
            var deletedStateChanged = false;

            foreach (var group in managedGroups)
            {
                if (ForgetDeletedManagedGroup(group))
                {
                    deletedStateChanged = true;
                }
            }

            foreach (var group in managedGroups)
            {
                var desired = desiredByGroup[group];
                var current = new HashSet<string>((permission.GetGroupPermissions(group, false) ?? new string[0])
                    .Select(NormalizePermissionName)
                    .Where(IsSafePermissionName), StringComparer.OrdinalIgnoreCase);

                foreach (var permissionName in desired)
                {
                    if (!current.Contains(permissionName))
                    {
                        if (GrantGroupPermissionVerified(group, permissionName))
                        {
                            changes++;
                        }
                    }
                }

                foreach (var permissionName in current)
                {
                    if (!desired.Contains(permissionName))
                    {
                        if (RevokeGroupPermissionVerified(group, permissionName))
                        {
                            changes++;
                        }
                    }
                }
            }

            if (deletedStateChanged)
            {
                SaveDeletedGroupState();
            }

            return changes;
        }

        private int ApplyDeletedPermissionGroups(List<string> groups)
        {
            var changes = 0;
            var stateChanged = false;

            foreach (var group in groups ?? new List<string>())
            {
                if (!IsPermissionManageableGroupName(group) || IsProtectedPermissionGroupName(group))
                {
                    continue;
                }

                if (RememberDeletedManagedGroup(group))
                {
                    stateChanged = true;
                }

                if (!permission.GroupExists(group))
                {
                    continue;
                }

                var current = (permission.GetGroupPermissions(group, false) ?? new string[0])
                    .Select(NormalizePermissionName)
                    .Where(IsSafePermissionName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var permissionName in current)
                {
                    if (RevokeGroupPermissionVerified(group, permissionName))
                    {
                        changes++;
                    }
                }

                if (RemovePermissionGroup(group))
                {
                    changes++;
                }
            }

            if (stateChanged)
            {
                SaveDeletedGroupState();
            }

            return changes;
        }

        private bool RemovePermissionGroup(string group)
        {
            if (!permission.GroupExists(group))
            {
                return false;
            }

            InvokePermissionMethod("RemoveGroup", group);

            if (!permission.GroupExists(group))
            {
                Puts($"Removed Oxide group {group}.");
                return true;
            }

            PrintWarning($"Website requested deletion of {group}, but Oxide still reports that group exists.");
            return false;
        }

        private void EnsurePermissionGroup(string group, JObject details)
        {
            var title = PermissionString(details, "title", group);
            var rank = PermissionInt(details, "rank", 0);
            var parent = PermissionString(details, "parent");

            if (!permission.GroupExists(group))
            {
                permission.CreateGroup(group, title, rank);
                Puts($"Created Oxide group {group}.");
            }

            if (IsProtectedPermissionGroupName(group))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                InvokePermissionMethod("SetGroupTitle", group, title);
            }

            InvokePermissionMethod("SetGroupRank", group, rank);

            if (string.IsNullOrWhiteSpace(parent) || IsGroupName(parent))
            {
                InvokePermissionMethod("SetGroupParent", group, parent ?? "");
            }
        }

        private void PostPermissionSyncResult(long revision, bool success, string message)
        {
            if (!CanRequest())
            {
                return;
            }

            var body = new JObject
            {
                ["revision"] = revision,
                ["status"] = success ? "applied" : "failed",
                ["ok"] = success,
                ["message"] = message ?? "",
                ["payload_hash"] = ""
            }.ToString(Formatting.None);
            var url = $"{TrimSlash(config.ApiBaseUrl)}/api/server/permissions-sync-result.php";

            SendPost(url, body, (code, response) =>
            {
                if (!IsSuccess(code, response, out var error))
                {
                    PrintWarning($"Permission sync result post failed: {error}");
                    return;
                }

                var payload = JsonConvert.DeserializeObject<KitResultResponse>(response);

                if (payload == null || !payload.ok)
                {
                    PrintWarning($"Permission sync result post failed: {payload?.error ?? "invalid response"}");
                }
            });
        }

        private List<PermissionSnapshotGroup> CurrentPermissionGroups()
        {
            var result = new List<PermissionSnapshotGroup>();

            foreach (var group in CurrentPermissionGroupNames().OrderBy(value => value))
            {
                if (!permission.GroupExists(group))
                {
                    continue;
                }

                result.Add(new PermissionSnapshotGroup
                {
                    name = group,
                    title = PermissionGroupTitle(group),
                    rank = PermissionGroupRank(group),
                    parent = PermissionGroupParent(group)
                });
            }

            return result;
        }

        private List<string> CurrentRegisteredPermissions()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var permissionName in PermissionStringEnumerable("GetPermissions"))
            {
                var normalized = NormalizePermissionName(permissionName);

                if (IsSafePermissionName(normalized))
                {
                    result.Add(normalized);
                }
            }

            foreach (var permissions in CurrentPermissionGroupMap().Values)
            {
                foreach (var permissionName in permissions)
                {
                    result.Add(permissionName);
                }
            }

            return result.OrderBy(value => value).ToList();
        }

        private Dictionary<string, List<string>> CurrentPermissionGroupMap()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in CurrentPermissionGroupNames())
            {
                if (!permission.GroupExists(group))
                {
                    continue;
                }

                result[group] = (permission.GetGroupPermissions(group, false) ?? new string[0])
                    .Select(NormalizePermissionName)
                    .Where(IsSafePermissionName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value)
                    .ToList();
            }

            return result;
        }

        private HashSet<string> CurrentPermissionGroupNames()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in ProtectedGroups)
            {
                result.Add(group);
            }

            foreach (var group in config.ManagedGroups ?? new List<string>())
            {
                if (IsGroupName(group) && !IsDeletedManagedGroup(group))
                {
                    result.Add(group.Trim());
                }
            }

            foreach (var group in config.KitPermissionManagedGroups ?? new List<string>())
            {
                if (IsGroupName(group) && !IsDeletedManagedGroup(group))
                {
                    result.Add(group.Trim());
                }
            }

            foreach (var group in PermissionStringEnumerable("GetGroups"))
            {
                if (IsGroupName(group) && !IsDeletedManagedGroup(group))
                {
                    result.Add(group.Trim());
                }
            }

            return result;
        }

        private IEnumerable<string> PermissionStringEnumerable(string methodName)
        {
            var value = InvokePermissionMethod(methodName);

            if (value == null)
            {
                return new List<string>();
            }

            var strings = value as IEnumerable<string>;

            if (strings != null)
            {
                return strings.Where(item => !string.IsNullOrWhiteSpace(item));
            }

            var enumerable = value as System.Collections.IEnumerable;

            if (enumerable == null)
            {
                return new List<string>();
            }

            return enumerable.Cast<object>()
                .Where(item => item != null)
                .Select(item => item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        private object InvokePermissionMethod(string methodName, params object[] args)
        {
            Exception lastError = null;

            foreach (var method in permission.GetType().GetMethods().Where(item => item.Name == methodName && item.GetParameters().Length == args.Length))
            {
                try
                {
                    return method.Invoke(permission, args);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            if (lastError != null)
            {
                PrintWarning($"Permission method {methodName} failed: {lastError.Message}");
            }

            return null;
        }

        private string PermissionGroupTitle(string group)
        {
            return Convert.ToString(InvokePermissionMethod("GetGroupTitle", group)) ?? group;
        }

        private int PermissionGroupRank(string group)
        {
            var value = InvokePermissionMethod("GetGroupRank", group);
            int rank;

            return value != null && int.TryParse(value.ToString(), out rank) ? rank : 0;
        }

        private string PermissionGroupParent(string group)
        {
            return Convert.ToString(InvokePermissionMethod("GetGroupParent", group)) ?? "";
        }

        private HashSet<string> PermissionReadOnlyGroups(PermissionSyncResponse payload)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "authenticated"
            };

            foreach (var group in payload?.read_only_groups ?? new List<string>())
            {
                if (IsGroupName(group))
                {
                    result.Add(group.Trim());
                }
            }

            return result;
        }

        private HashSet<string> PermissionPayloadManagedGroups(PermissionSyncResponse payload)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in payload?.managed_groups ?? new List<string>())
            {
                if (IsGroupName(group))
                {
                    result.Add(group.Trim());
                }
            }

            if (result.Count == 0)
            {
                foreach (var group in (JsonStringListMap(payload?.group_permissions) ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)).Keys)
                {
                    if (IsGroupName(group))
                    {
                        result.Add(group.Trim());
                    }
                }
            }

            return result;
        }

        private HashSet<string> PermissionPayloadDeletedGroups(PermissionSyncResponse payload)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in payload?.deleted_groups ?? new List<string>())
            {
                if (IsGroupName(group))
                {
                    result.Add(group.Trim());
                }
            }

            return result;
        }

        private static Dictionary<string, List<string>> JsonStringListMap(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var obj = token as JObject;

            if (obj == null)
            {
                var array = token as JArray;
                return array != null && array.Count == 0 ? result : null;
            }

            foreach (var property in obj.Properties())
            {
                if (string.IsNullOrWhiteSpace(property.Name))
                {
                    continue;
                }

                var values = new List<string>();
                var valueArray = property.Value as JArray;

                if (valueArray != null)
                {
                    foreach (var value in valueArray)
                    {
                        var text = value == null ? "" : value.ToString().Trim();

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            values.Add(text);
                        }
                    }
                }
                else if (property.Value != null && property.Value.Type != JTokenType.Null && property.Value.Type != JTokenType.Undefined)
                {
                    var text = property.Value.ToString().Trim();

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        values.Add(text);
                    }
                }

                result[property.Name.Trim()] = values
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value)
                    .ToList();
            }

            return result;
        }

        private List<string> BackupKitData(long revision)
        {
            var backups = new List<string>();
            var directory = KitBackupDirectory();
            Directory.CreateDirectory(directory);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

            foreach (var entry in KitDataTargets())
            {
                if (!File.Exists(entry.Value))
                {
                    continue;
                }

                var target = Path.Combine(directory, $"{entry.Key}_rev{revision}_{stamp}.json");
                File.Copy(entry.Value, target, true);
                backups.Add(target);
            }

            PruneKitBackups(directory);
            return backups;
        }

        private bool RollbackKitBackup(string token, out string message)
        {
            token = (token ?? "").Trim();
            var directory = KitBackupDirectory();

            if (string.IsNullOrWhiteSpace(token) || !Directory.Exists(directory))
            {
                message = "No kit backup directory exists yet.";
                return false;
            }

            var restored = 0;

            foreach (var entry in KitDataTargets())
            {
                var file = Directory.GetFiles(directory, $"{entry.Key}_*{token}*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(entry.Value));
                File.Copy(file, entry.Value, true);
                restored++;
            }

            if (restored == 0)
            {
                message = $"No kit backups matched '{token}'.";
                return false;
            }

            message = $"Restored {restored} kit backup file(s) matching '{token}'. Reloaded Kits and ServerRewards.";
            return true;
        }

        private void PruneKitBackups(string directory)
        {
            var keep = Math.Max(1, config.KitDataBackupCount) * Math.Max(1, KitDataTargets().Count);
            var files = Directory.GetFiles(directory, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(keep);

            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }

        private Dictionary<string, string> KitDataTargets()
        {
            return new Dictionary<string, string>
            {
                ["kits_data"] = KitDataPath("Kits", "kits_data.json"),
                ["player_data"] = KitDataPath("Kits", "player_data.json"),
                ["serverrewards_products"] = KitDataPath("ServerRewards", "products.json")
            };
        }

        private string KitDataPath(string directory, string file)
        {
            return Path.Combine(Interface.Oxide.DataFileSystem.Directory, directory, file);
        }

        private string KitBackupDirectory()
        {
            return Path.Combine(Interface.Oxide.DataFileSystem.Directory, "RaidlandsKitBackups");
        }

        private JObject ReadJsonFile(string path)
        {
            if (!File.Exists(path))
            {
                return new JObject();
            }

            return JObject.Parse(File.ReadAllText(path));
        }

        private void WriteJsonAtomic(string path, JObject json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = $"{path}.tmp";
            File.WriteAllText(temp, json.ToString(Formatting.Indented));

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }

        private void ReloadKitPlugins()
        {
            server.Command("oxide.reload Kits");
            server.Command("oxide.reload ServerRewards");
        }

        private bool IsPermissionManageableGroupName(string group)
        {
            return IsGroupName(group) && !string.Equals(group, "authenticated", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsProtectedPermissionGroupName(string group)
        {
            return string.Equals(group, "default", StringComparison.OrdinalIgnoreCase)
                || string.Equals(group, "discord", StringComparison.OrdinalIgnoreCase)
                || string.Equals(group, "admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(group, "authenticated", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSafePermissionName(string permissionName)
        {
            var value = NormalizePermissionName(permissionName);

            return value.Length > 0
                && value.Length <= 190
                && value.IndexOf('.') >= 0
                && value.All(character => char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.');
        }

        private static string NormalizePermissionName(string permissionName)
        {
            return (permissionName ?? "").Trim().ToLowerInvariant();
        }

        private bool GroupHasPermission(string group, string permissionName)
        {
            var normalized = NormalizePermissionName(permissionName);

            return (permission.GetGroupPermissions(group, false) ?? new string[0])
                .Select(NormalizePermissionName)
                .Contains(normalized, StringComparer.OrdinalIgnoreCase);
        }

        private void EnsureBridgeManagedKitPermissionRegistered(string permissionName)
        {
            var normalized = NormalizePermissionName(permissionName);

            if (!IsSafeKitPermission(normalized) || permission.PermissionExists(normalized))
            {
                return;
            }

            permission.RegisterPermission(normalized, this);
            Puts($"Registered synced kit permission {normalized}.");
        }

        private bool GrantGroupPermissionVerified(string group, string permissionName)
        {
            var normalized = NormalizePermissionName(permissionName);
            EnsureBridgeManagedKitPermissionRegistered(normalized);

            try
            {
                permission.GrantGroupPermission(group, normalized, null);
            }
            catch (Exception ex)
            {
                PrintWarning($"Oxide grant call for {normalized} to group {group} failed before fallback repair: {ex.Message}");
            }

            if (GroupHasPermission(group, normalized))
            {
                Puts($"Granted {normalized} to group {group}.");
                return true;
            }

            if (ForceAddGroupPermission(group, normalized))
            {
                Puts($"Force-added {normalized} to group {group} after Oxide grant did not persist.");
                return true;
            }

            PrintWarning($"Grant for {normalized} to group {group} did not persist and fallback group-data repair failed.");
            return false;
        }

        private bool RevokeGroupPermissionVerified(string group, string permissionName)
        {
            var normalized = NormalizePermissionName(permissionName);
            permission.RevokeGroupPermission(group, normalized);

            if (!GroupHasPermission(group, normalized))
            {
                Puts($"Revoked {normalized} from group {group}.");
                return true;
            }

            if (ForceRemoveGroupPermission(group, normalized))
            {
                Puts($"Force-removed {normalized} from group {group} after Oxide revoke did not persist.");
                return true;
            }

            PrintWarning($"Revoke for {normalized} from group {group} did not persist and fallback group-data repair failed.");
            return false;
        }

        private bool ForceAddGroupPermission(string group, string permissionName)
        {
            var permissions = GetMutableGroupPermissions(group);

            if (permissions == null)
            {
                return false;
            }

            if (permissions.Any(item => string.Equals(item, permissionName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            permissions.Add(permissionName);
            return GroupHasPermission(group, permissionName);
        }

        private bool ForceRemoveGroupPermission(string group, string permissionName)
        {
            var permissions = GetMutableGroupPermissions(group);

            if (permissions == null)
            {
                return false;
            }

            var existing = permissions.FirstOrDefault(item => string.Equals(item, permissionName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                return true;
            }

            return permissions.Remove(existing) && !GroupHasPermission(group, permissionName);
        }

        private ICollection<string> GetMutableGroupPermissions(string group)
        {
            var getGroupData = permission.GetType().GetMethod(
                "GetGroupData",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);

            if (getGroupData == null)
            {
                getGroupData = permission.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(method =>
                        method.Name == "GetGroupData"
                        && method.GetParameters().Length == 1);
            }

            var groupData = getGroupData?.Invoke(permission, new object[] { group });

            if (groupData == null)
            {
                return null;
            }

            var permissionsField = groupData.GetType().GetField(
                "Perms",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (permissionsField != null)
            {
                return permissionsField.GetValue(groupData) as ICollection<string>;
            }

            var permissionsProperty = groupData.GetType().GetProperty(
                "Perms",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            return permissionsProperty != null
                ? permissionsProperty.GetValue(groupData, null) as ICollection<string>
                : null;
        }

        private static string PermissionString(JObject obj, string key, string fallback = "")
        {
            var value = (string)(obj?[key] ?? "") ?? "";
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int PermissionInt(JObject obj, string key, int fallback)
        {
            var token = obj?[key];
            int value;

            return token != null && int.TryParse(token.ToString(), out value) ? value : fallback;
        }

        private bool IsKitManageableGroupName(string group)
        {
            return IsGroupName(group) && !string.Equals(group, "admin", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(group, "authenticated", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSafeKitPermission(string permissionName)
        {
            if (string.IsNullOrWhiteSpace(permissionName))
            {
                return false;
            }

            var value = permissionName.Trim();

            if (!value.All(character => char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.'))
            {
                return false;
            }

            return (config.KitPermissionPrefixes ?? new List<string>()).Any(prefix =>
                !string.IsNullOrWhiteSpace(prefix) && value.StartsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private bool IsSafeKitImage(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            var trimmed = value.Trim();

            if (trimmed.IndexOf('\\') >= 0 || trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                || trimmed.IndexOf(".php", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (trimmed.Length > 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            {
                return false;
            }

            Uri uri;

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out uri))
            {
                return string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
            }

            return NormalizeAssetPath(trimmed).StartsWith("assets/media/kits/", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSafeKitName(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= 160
                && value.All(character => char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == ' ');
        }

        private bool IsSafeShortname(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= 160
                && value.All(character => char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.');
        }

        private static string KitString(JObject obj, string key)
        {
            return (string)(obj?[key] ?? "") ?? "";
        }

        private static string FirstKitString(JObject obj, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = KitString(obj, key);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }

        private static List<string> KitPreviousNames(JObject obj)
        {
            var names = new List<string>();

            AddKitPreviousName(names, KitString(obj, "PreviousName"));

            var token = obj?["PreviousNames"];

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    AddKitPreviousName(names, item?.ToString());
                }
            }
            else
            {
                AddKitPreviousName(names, token?.ToString());
            }

            return names;
        }

        private static void AddKitPreviousName(List<string> names, string value)
        {
            foreach (var chunk in (value ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = chunk.Trim();

                if (name.Length == 0 || names.Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                names.Add(name);
            }
        }

        private static void AddManagedKitName(HashSet<string> names, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                names.Add(value.Trim());
            }
        }

        private static int KitInt(JObject obj, string key, int fallback)
        {
            var token = obj?[key];
            int value;

            return token != null && int.TryParse(token.ToString(), out value) ? value : fallback;
        }

        private static long KitLong(JObject obj, string key, long fallback)
        {
            var token = obj?[key];
            long value;

            return token != null && long.TryParse(token.ToString(), out value) ? value : fallback;
        }

        private static float KitFloat(JObject obj, string key, float fallback)
        {
            var token = obj?[key];
            float value;

            return token != null && float.TryParse(token.ToString(), out value) ? value : fallback;
        }

        private static bool KitBool(JObject obj, string key, bool fallback)
        {
            var token = obj?[key];
            bool value;

            return token != null && bool.TryParse(token.ToString(), out value) ? value : fallback;
        }

        private static JToken JsonToken(JObject obj, params string[] keys)
        {
            if (obj == null || keys == null)
            {
                return null;
            }

            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var property = obj.Properties().FirstOrDefault(item =>
                    string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase));
                var token = property?.Value;

                if (token != null)
                {
                    return token;
                }
            }

            return null;
        }

        private static string JsonString(JObject obj, params string[] keys)
        {
            var token = JsonToken(obj, keys);
            return token == null || token.Type == JTokenType.Null ? "" : token.ToString().Trim();
        }

        private static int JsonInt(JObject obj, int fallback, params string[] keys)
        {
            var token = JsonToken(obj, keys);
            int value;

            return token != null && int.TryParse(token.ToString(), out value) ? value : fallback;
        }

        private static bool JsonBool(JObject obj, bool fallback, params string[] keys)
        {
            var token = JsonToken(obj, keys);

            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            var value = token.ToString().Trim();

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return fallback;
        }

        private static bool ContainsWord(string value, string word)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(word))
            {
                return false;
            }

            var normalizedValue = value.Replace('-', '_').ToLowerInvariant();
            var normalizedWord = word.Replace('-', '_').ToLowerInvariant();

            return normalizedValue.Contains(normalizedWord);
        }

        private static JValue NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? JValue.CreateNull() : new JValue(value);
        }

        private string ResolveWipeKey(DateTime wipeStartedAt)
        {
            var wipeKey = ResolveSecretValue(config.WipeKey);

            if (!string.IsNullOrWhiteSpace(wipeKey))
            {
                return wipeKey.Trim();
            }

            var serverId = CleanWipeKeySegment(config.ServerId, "raidlands-main");
            var startedAt = wipeStartedAt == DateTime.MinValue ? GetWipeStartedAt() : wipeStartedAt.ToUniversalTime();

            if (startedAt != DateTime.MinValue)
            {
                return $"{serverId}-{startedAt:yyyyMMdd'T'HHmmss'Z'}";
            }

            return $"{serverId}-current";
        }

        private static string CleanWipeKeySegment(string value, string fallback)
        {
            var raw = (value ?? "").Trim();
            var builder = new StringBuilder(raw.Length);

            foreach (var ch in raw)
            {
                builder.Append(IsWipeKeyCharacter(ch) ? ch : '-');
            }

            var cleaned = builder.ToString().Trim('-', '_', '.', ':');
            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }

        private static bool IsWipeKeyCharacter(char ch)
        {
            return (ch >= 'a' && ch <= 'z')
                || (ch >= 'A' && ch <= 'Z')
                || (ch >= '0' && ch <= '9')
                || ch == '_'
                || ch == '-'
                || ch == '.'
                || ch == ':';
        }

        public string GetRaidlandsBrandAssetUrl(string key)
        {
            return BrandAssetUrl(key);
        }

        public Dictionary<string, string> GetRaidlandsBrandAssets()
        {
            return BrandAssetUrls();
        }

        public string GetRaidlandsBrandValue(string key)
        {
            string value;
            return TryGetBrandValue(key, out value) ? value : "";
        }

        private void SyncBrandConfigs()
        {
            var updated = 0;

            updated += SyncJsonConfig("SimpleLogo", ApplySimpleLogoBrand);
            updated += SyncJsonConfig("ServerInfo", ApplyServerInfoBrand);
            updated += SyncJsonConfig("ServerPop", ApplyServerPopBrand);
            updated += SyncJsonConfig("SmartChatBot", ApplySmartChatBotBrand);
            updated += SyncJsonConfig("Kits", ApplyKitsBrand);
            updated += SyncJsonConfig("DiscordWipe", ApplyDiscordWipeBrand);
            updated += SyncJsonConfig("Scoreboards", ApplyScoreboardsBrand);

            if (updated > 0)
            {
                Puts($"Raidlands brand config sync updated {updated} config file(s).");
            }
        }

        private int SyncJsonConfig(string configName, Action<JObject> apply)
        {
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, $"{configName}.json");

            if (!File.Exists(path))
            {
                return 0;
            }

            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                var before = json.ToString(Formatting.None);

                apply(json);

                if (before == json.ToString(Formatting.None))
                {
                    return 0;
                }

                File.WriteAllText(path, json.ToString(Formatting.Indented));
                return 1;
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not sync Raidlands brand config for {configName}: {ex.Message}");
                return 0;
            }
        }

        private void ApplySimpleLogoBrand(JObject json)
        {
            var ui = EnsureObject(json, "UI");
            ui["BackgroundMainURL"] = new JArray(FirstNonEmpty(BrandAssetUrl("SimpleLogo"), FirstNonEmpty(BrandAssetUrl("NavLogo"), BrandAssetUrl("Logo"))));
            ui["GUIAnchorMin"] = "0.795 0.015";
            ui["GUIAnchorMax"] = "0.84 0.095";
            ui["AutoWidthFromImage"] = true;
            ui["ImageAspectRatio"] = 1.0;
            ui["ScreenAspectRatio"] = 1.77778;
        }

        private void ApplyServerInfoBrand(JObject json)
        {
            var settings = EnsureObject(json, "settings");
            var backgroundImage = EnsureObject(settings, "BackgroundImage");

            backgroundImage["Enabled"] = true;
            backgroundImage["Url"] = FirstNonEmpty(BrandAssetUrl("CommandMenu"), BrandAssetUrl("Header"));
            backgroundImage["TransparencyInPercent"] = 58;

            settings["ActiveButtonColor"] = BrandValue("PrimaryRed");
            settings["InactiveButtonColor"] = BrandValue("MutedButton");
            settings["CloseButtonColor"] = BrandValue("PrimaryRed");
            settings["NextPageButtonColor"] = BrandValue("PrimaryRed");
            settings["PrevPageButtonColor"] = BrandValue("PrimaryRed");
            settings["BackgroundColor"] = BrandValue("DarkPanel");

            var helpButton = EnsureObject(settings, "HelpButton");
            helpButton["Color"] = BrandValue("PrimaryRed");
            // ServerInfo.json owns tab/page content, including /commands SubTabs.
            // Brand sync must not rewrite it back to a static two-page command list.
        }

        private void ApplyServerPopBrand(JObject json)
        {
            var chatSettings = EnsureObject(json, "Chat Settings");
            var messageSettings = EnsureObject(json, "Messgae Settings");

            chatSettings["Chat Prefix"] = "<size=16><color=#ff3b3b>| Raidlands |</color></size>";
            messageSettings["Value Color (HEX)"] = BrandValue("AccentGold");
        }

        private void ApplySmartChatBotBrand(JObject json)
        {
            json["Chat Prefix"] = "<color=#ff3b3b>Raidlands</color> ";
            json["Show Chat Prefix"] = true;
            json["Auto Messages"] = new JArray(
                new JObject
                {
                    ["Permission"] = "smartchatbot.messages",
                    ["Message Frequency"] = "5m",
                    ["Auto Messages"] = new JArray(
                        new JObject
                        {
                            ["Is Enabled"] = true,
                            ["Message"] = "Visit https://raidlands.net/ for store perks, Discord, and live stats."
                        })
                });
        }

        private void ApplyKitsBrand(JObject json)
        {
            var uiOptions = EnsureObject(json, "UI Options");
            uiOptions["Default kit image URL"] = BrandAssetUrl("KitsIcon");
            uiOptions["View kit icon URL"] = FirstNonEmpty(BrandAssetUrl("SearchIcon"), BrandAssetUrl("KitsIcon"));
        }

        private void ApplyDiscordWipeBrand(JObject json)
        {
            ApplyDiscordMessageBrands(json["Wipe messages"] as JArray,
                "Raidlands has wiped. Drop in, build fast, and claim the map.");
            ApplyDiscordMessageBrands(json["Protocol messages"] as JArray,
                "Raidlands server protocol changed. Update Rust before reconnecting.");
        }

        private void ApplyScoreboardsBrand(JObject json)
        {
            json["Background Color"] = "0.105 0.118 0.122 0.82";
            json["Content Color"] = "0.067 0.067 0.067 0.88";
            json["Header Color"] = "1 0.231 0.231 1";
            json["Title Color"] = "1 0.82 0.4 1";
        }

        private void ApplyDiscordMessageBrands(JArray messages, string description)
        {
            if (messages == null)
            {
                return;
            }

            foreach (var item in messages.OfType<JObject>())
            {
                var embed = EnsureObject(item, "Embed");
                var footer = EnsureObject(embed, "Footer");

                embed["Description"] = description;
                embed["Url"] = BrandValue("WebsiteUrl");
                embed["Embed Color"] = BrandValue("PrimaryRed");
                embed["Thumbnail Url"] = BrandAssetUrl("Logo");
                footer["Icon Url"] = BrandAssetUrl("Logo");
                footer["Text"] = BrandValue("Name");
                footer["Enabled"] = true;
            }
        }

        private void SetServerInfoTab(JArray tabs, int index, string buttonText, string headerText, IEnumerable<string> lines)
        {
            if (tabs == null || index < 0 || index >= tabs.Count)
            {
                return;
            }

            var tab = tabs[index] as JObject;

            if (tab == null)
            {
                return;
            }

            tab["ButtonText"] = buttonText;
            tab["HeaderText"] = headerText;

            var pages = tab["Pages"] as JArray;

            if (pages == null)
            {
                pages = new JArray();
                tab["Pages"] = pages;
            }

            if (pages.Count == 0)
            {
                pages.Add(new JObject
                {
                    ["ImageSettings"] = new JArray()
                });
            }

            var page = pages[0] as JObject;

            if (page == null)
            {
                page = new JObject();
                pages[0] = page;
            }

            var textLines = new JArray();

            foreach (var line in lines ?? new string[0])
            {
                textLines.Add(line);
            }

            page["TextLines"] = textLines;

            if (page["ImageSettings"] == null)
            {
                page["ImageSettings"] = new JArray();
            }
        }

        private JObject EnsureObject(JObject parent, string key)
        {
            var current = parent[key] as JObject;

            if (current != null)
            {
                return current;
            }

            current = new JObject();
            parent[key] = current;
            return current;
        }

        private Dictionary<string, string> BrandAssetUrls()
        {
            var assets = config?.Assets ?? new AssetPaths();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            values["Logo"] = AssetUrl(assets.Logo);
            values["NavLogo"] = AssetUrl(assets.NavLogo);
            values["SimpleLogo"] = AssetUrl(assets.SimpleLogo);
            values["Hero"] = AssetUrl(assets.Hero);
            values["Header"] = AssetUrl(assets.Header);
            values["CommandMenu"] = AssetUrl(assets.CommandMenu);
            values["WipePanel"] = AssetUrl(assets.WipePanel);
            values["BackpacksIcon"] = AssetUrl(assets.BackpacksIcon);
            values["KitsIcon"] = AssetUrl(assets.KitsIcon);
            values["TeleportIcon"] = AssetUrl(assets.TeleportIcon);
            values["ClanIcon"] = AssetUrl(assets.ClanIcon);
            values["SkinboxIcon"] = AssetUrl(assets.SkinboxIcon);
            values["FastRaidsIcon"] = AssetUrl(assets.FastRaidsIcon);
            values["GatherIcon"] = AssetUrl(assets.GatherIcon);
            values["StatsIcon"] = AssetUrl(assets.StatsIcon);
            values["SearchIcon"] = AssetUrl(assets.SearchIcon);

            return values;
        }

        private string BrandAssetUrl(string key)
        {
            var normalized = NormalizeBrandKey(key);

            foreach (var asset in BrandAssetUrls())
            {
                if (NormalizeBrandKey(asset.Key) == normalized)
                {
                    return asset.Value;
                }
            }

            return "";
        }

        private Dictionary<string, string> BrandValues()
        {
            var websiteUrl = TrimSlash(config?.ApiBaseUrl);

            if (string.IsNullOrWhiteSpace(websiteUrl))
            {
                websiteUrl = "https://raidlands.net";
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            values["Name"] = "Raidlands";
            values["WebsiteUrl"] = websiteUrl;
            values["PrimaryRed"] = "#ff3b3b";
            values["AccentGold"] = "#ffd166";
            values["DarkPanel"] = "#151719";
            values["MutedButton"] = "#3a3d3f";

            return values;
        }

        private string BrandValue(string key)
        {
            string value;
            return TryGetBrandValue(key, out value) ? value : "";
        }

        private bool TryGetBrandValue(string key, out string value)
        {
            var normalized = NormalizeBrandKey(key);

            foreach (var item in BrandValues())
            {
                if (NormalizeBrandKey(item.Key) == normalized)
                {
                    value = item.Value;
                    return true;
                }
            }

            value = "";
            return false;
        }

        private string AssetUrl(string configuredAssetPath)
        {
            return ResolveAssetUrl(configuredAssetPath, config?.WebsiteAssetBaseUrl);
        }

        private static string ResolveAssetUrl(string configuredAssetPath, string websiteAssetBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(configuredAssetPath))
            {
                return "";
            }

            var assetPath = configuredAssetPath.Trim();

            if (assetPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || assetPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return assetPath;
            }

            var baseUrl = TrimSlash(websiteAssetBaseUrl);
            var normalizedPath = NormalizeAssetPath(assetPath).TrimStart('/');

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return normalizedPath;
            }

            return $"{baseUrl}/{normalizedPath}";
        }

        private bool CanRequest()
        {
            if (string.IsNullOrWhiteSpace(config.ApiBaseUrl))
            {
                PrintWarning("ApiBaseUrl is not configured.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(ResolveSecretValue(config.SharedSecret)))
            {
                PrintWarning("SharedSecret is not configured.");
                return false;
            }

            return true;
        }

        private void LogBridgeSecretDiagnostics()
        {
            var sharedSecret = ResolveSecretValue(config.SharedSecret);

            if (string.IsNullOrWhiteSpace(sharedSecret))
            {
                PrintWarning("Bridge SharedSecret is empty after resolving secrets.");
                return;
            }

            Puts($"Bridge SharedSecret source: {DescribeSecretSource(config.SharedSecret)}; length: {sharedSecret.Length}; fingerprint: {SecretFingerprint(sharedSecret)}");
        }

        private void SendGet(string url, Action<int, string> callback)
        {
            if (isUnloading)
            {
                return;
            }

            var headers = BuildHeaders("GET", url, "");
            webrequest.Enqueue(url, null, (code, response) => RunWebCallback($"GET {url}", callback, code, response), this, RequestMethod.GET, headers, WebRequestTimeoutSeconds());
        }

        private void SendPost(string url, string body, Action<int, string> callback)
        {
            if (isUnloading)
            {
                return;
            }

            var headers = BuildHeaders("POST", url, body);
            headers["Content-Type"] = "application/json";
            webrequest.Enqueue(url, body, (code, response) => RunWebCallback($"POST {url}", callback, code, response), this, RequestMethod.POST, headers, WebRequestTimeoutSeconds());
        }

        private float WebRequestTimeoutSeconds()
        {
            return Math.Max(5000, config.WebRequestTimeoutMilliseconds) / 1000f;
        }

        private void RunWebCallback(string context, Action<int, string> callback, int code, string response)
        {
            if (isUnloading)
            {
                return;
            }

            try
            {
                callback?.Invoke(code, response ?? "");
            }
            catch (Exception ex)
            {
                PrintWarning($"{context} callback failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void RunScheduled(string context, Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                PrintWarning($"{context} failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private Dictionary<string, string> BuildHeaders(string method, string url, string body)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var pathAndQuery = new Uri(url).PathAndQuery;
            var bodyHash = Sha256(body ?? "");
            var payload = $"{method.ToUpperInvariant()}\n{pathAndQuery}\n{timestamp}\n{bodyHash}";
            var signature = HmacSha256(payload, ResolveSecretValue(config.SharedSecret));

            return new Dictionary<string, string>
            {
                ["X-Raidlands-Server"] = config.ServerId,
                ["X-Raidlands-Timestamp"] = timestamp,
                ["X-Raidlands-Signature"] = signature,
                ["Accept"] = "application/json"
            };
        }

        private string ResolveSecretValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var trimmed = value.Trim();

            if (!trimmed.StartsWith("${", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return trimmed;
            }

            var key = trimmed.Substring(2, trimmed.Length - 3).Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            string secret;

            if (LoadSecrets().TryGetValue(key, out secret))
            {
                return (secret ?? "").Trim();
            }

            PrintWarning($"Secret variable {key} is not configured in oxide/config/{SecretsConfigName}.json.");
            return "";
        }

        private string DescribeSecretSource(string value)
        {
            var trimmed = (value ?? "").Trim();

            if (!trimmed.StartsWith("${", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                return "oxide/config/WebsiteVipBridge.json";
            }

            var key = trimmed.Substring(2, trimmed.Length - 3).Trim();
            var source = string.IsNullOrWhiteSpace(secretsConfigSource) ? $"oxide/config/{SecretsConfigName}.json" : secretsConfigSource;

            return $"{key} in {source}";
        }

        private Dictionary<string, string> LoadSecrets()
        {
            if (secrets != null)
            {
                return secrets;
            }

            secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, $"{SecretsConfigName}.json");
            secretsConfigSource = $"oxide/config/{SecretsConfigName}.json";

            if (!File.Exists(path))
            {
                PrintWarning($"Optional secrets file not found: oxide/config/{SecretsConfigName}.json.");
                return secrets;
            }

            try
            {
                var loadedSecrets = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));

                if (loadedSecrets != null)
                {
                    secrets = new Dictionary<string, string>(loadedSecrets, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Could not read oxide/config/{SecretsConfigName}.json: {ex.Message}");
            }

            return secrets;
        }

        private void ApplyDesiredGroups(string steamId, List<string> desiredGroups, List<string> apiManagedGroups)
        {
            EnsureDefaultUserGroup(steamId);

            var managed = new HashSet<string>((config.ManagedGroups ?? new List<string>())
                .Where(group => IsManageableGroupName(group) && !IsDeletedManagedGroup(group)), StringComparer.OrdinalIgnoreCase);

            foreach (var group in apiManagedGroups ?? new List<string>())
            {
                if (IsManageableGroupName(group) && !IsDeletedManagedGroup(group))
                {
                    managed.Add(group);
                }
            }

            var desired = new HashSet<string>((desiredGroups ?? new List<string>())
                .Where(group => IsManageableGroupName(group) && !IsDeletedManagedGroup(group)), StringComparer.OrdinalIgnoreCase);
            var manualStorefrontGroups = ManualStorefrontGroupsFor(steamId);

            foreach (var group in manualStorefrontGroups)
            {
                if (IsManageableGroupName(group) && !IsDeletedManagedGroup(group))
                {
                    managed.Add(group);
                    desired.Add(group);
                }
            }

            EnsureManagedGroups(managed.ToList());

            foreach (var group in managed)
            {
                var hasGroup = permission.UserHasGroup(steamId, group);
                var shouldHaveGroup = desired.Contains(group);

                if (shouldHaveGroup && !hasGroup)
                {
                    permission.AddUserGroup(steamId, group);
                    Puts($"Granted {group} to {steamId}.");
                    continue;
                }

                if (!shouldHaveGroup && hasGroup)
                {
                    permission.RemoveUserGroup(steamId, group);
                    Puts($"Removed {group} from {steamId}.");
                }
            }
        }

        private void EnsureManagedGroups(IEnumerable<string> groups)
        {
            foreach (var group in groups ?? new List<string>())
            {
                if (!IsManageableGroupName(group) || IsDeletedManagedGroup(group))
                {
                    continue;
                }

                if (!permission.GroupExists(group))
                {
                    permission.CreateGroup(group, group, 0);
                    Puts($"Created Oxide group {group}.");
                }
            }
        }

        private bool EnsureDefaultUserGroup(string steamId)
        {
            if (!IsSteamId64(steamId))
            {
                return false;
            }

            if (!permission.GroupExists("default"))
            {
                permission.CreateGroup("default", "default", 0);
            }

            if (!permission.UserHasGroup(steamId, "default"))
            {
                permission.AddUserGroup(steamId, "default");
                Puts($"Restored default to {steamId}.");
                return true;
            }

            return false;
        }

        private bool IsManageableGroupName(string group)
        {
            return IsGroupName(group) && !ProtectedGroups.Contains(group);
        }

        private bool IsGroupName(string group)
        {
            return !string.IsNullOrWhiteSpace(group) && group.All(character =>
                char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.');
        }

        private bool IsSteamId64(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length == 17
                && value.StartsWith("7656119")
                && value.All(char.IsDigit);
        }

        private string FirstNonEmpty(string current, string next)
        {
            return string.IsNullOrWhiteSpace(current) ? (next ?? "").Trim() : current;
        }

        private static string ConfiguredOrDefault(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static void ApplyAssetDefaults(AssetPaths assets, AssetPaths defaults)
        {
            assets.Logo = ConfiguredOrDefault(assets.Logo, defaults.Logo);
            assets.NavLogo = ConfiguredOrDefault(assets.NavLogo, defaults.NavLogo);
            assets.SimpleLogo = ConfiguredOrDefault(assets.SimpleLogo, defaults.SimpleLogo);
            assets.Hero = ConfiguredOrDefault(assets.Hero, defaults.Hero);
            assets.Header = ConfiguredOrDefault(assets.Header, defaults.Header);
            assets.CommandMenu = ConfiguredOrDefault(assets.CommandMenu, defaults.CommandMenu);
            assets.WipePanel = ConfiguredOrDefault(assets.WipePanel, defaults.WipePanel);
            assets.BackpacksIcon = ConfiguredOrDefault(assets.BackpacksIcon, defaults.BackpacksIcon);
            assets.KitsIcon = ConfiguredOrDefault(assets.KitsIcon, defaults.KitsIcon);
            assets.TeleportIcon = ConfiguredOrDefault(assets.TeleportIcon, defaults.TeleportIcon);
            assets.ClanIcon = ConfiguredOrDefault(assets.ClanIcon, defaults.ClanIcon);
            assets.SkinboxIcon = ConfiguredOrDefault(assets.SkinboxIcon, defaults.SkinboxIcon);
            assets.FastRaidsIcon = ConfiguredOrDefault(assets.FastRaidsIcon, defaults.FastRaidsIcon);
            assets.GatherIcon = ConfiguredOrDefault(assets.GatherIcon, defaults.GatherIcon);
            assets.StatsIcon = ConfiguredOrDefault(assets.StatsIcon, defaults.StatsIcon);
            assets.SearchIcon = ConfiguredOrDefault(assets.SearchIcon, defaults.SearchIcon);
        }

        private static void MergeStorefrontBundleDefaults(Dictionary<string, StorefrontGroupBundle> bundles, Dictionary<string, StorefrontGroupBundle> defaults)
        {
            if (bundles == null || defaults == null)
            {
                return;
            }

            var existingKeys = new HashSet<string>(bundles.Keys.Select(NormalizeStorefrontKey), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in defaults)
            {
                var key = NormalizeStorefrontKey(entry.Key);

                if (string.IsNullOrWhiteSpace(key) || existingKeys.Contains(key))
                {
                    continue;
                }

                bundles[entry.Key] = new StorefrontGroupBundle
                {
                    Title = entry.Value?.Title ?? "",
                    Groups = entry.Value?.Groups?.ToList() ?? new List<string>()
                };
                existingKeys.Add(key);
            }
        }

        private int ToInt(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                return 0;
            }

            return (int)Math.Min(int.MaxValue, Math.Round(value));
        }

        private bool IsSuccess(int code, string response, out string error)
        {
            if (code >= 200 && code < 300 && !string.IsNullOrWhiteSpace(response))
            {
                error = "";
                return true;
            }

            error = $"HTTP {code}: {response}";
            return false;
        }

        private static string TrimSlash(string value)
        {
            return (value ?? "").Trim().TrimEnd('/');
        }

        private static string NormalizeAssetPath(string value)
        {
            return string.Join("/", (value ?? "")
                .Replace('\\', '/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string NormalizeBrandKey(string value)
        {
            return string.Concat((value ?? "").Where(char.IsLetterOrDigit)).ToLowerInvariant();
        }

        private static string NormalizeStorefrontKey(string value)
        {
            var builder = new StringBuilder();
            var previousUnderscore = false;

            foreach (var character in (value ?? "").Trim().Replace("+", "_plus"))
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                    previousUnderscore = false;
                    continue;
                }

                if ((character == '_' || character == '-' || character == '.' || char.IsWhiteSpace(character)) && !previousUnderscore && builder.Length > 0)
                {
                    builder.Append('_');
                    previousUnderscore = true;
                }
            }

            return builder.ToString().Trim('_');
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }

        private static string HmacSha256(string value, string secret)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                return Hex(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }
        }

        private static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);

            foreach (var item in bytes)
            {
                builder.Append(item.ToString("x2"));
            }

            return builder.ToString();
        }

        private static string SecretFingerprint(string value)
        {
            var hash = Sha256(value ?? "");
            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }
    }
}
