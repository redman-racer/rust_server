using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Oxide.Plugins
{
    [Info("RaidlandsPermissionReset", "Raidlands", "1.1.0")]
    [Description("Retired legacy permission reset. WebsiteVipBridge now owns VIP/perk group wiring.")]
    public class RaidlandsPermissionReset : RustPlugin
    {
        private bool applied;
        private int changed;
        private readonly List<string> warnings = new List<string>();

        private readonly string[] defaultRevokes =
        {
            "lockedcratetimer.conf.use",
            "autolock.use",
            "autolock.item.bypass",
            "friendlyfire.changestate",
            "targetabledrones.untargetable",
            "toolcupboardturrets.ignore"
        };

        private readonly string[] adminPermissions =
        {
            "arkan.allowed",
            "arkan.nr.draw",
            "arkan.aim.draw",
            "arkan.ir.draw",
            "arkan.nr.reportchat",
            "arkan.nr.reportconsole",
            "arkan.aim.reportchat",
            "arkan.aim.reportconsole",
            "arkan.ir.reportchat",
            "arkan.ir.reportconsole",
            "backpacks.admin",
            "backpacks.admin.view",
            "backpacks.admin.edit",
            "backpacks.admin.resize",
            "backpacks.admin.debug",
            "backpacks.admin.protected",
            "betterchat.admin",
            "betterloot.admin",
            "blueprintmanager.admin",
            "buildingskins.admin",
            "customvendingsetup.use",
            "discordauth.deauth",
            "guardian.admin",
            "guardian.all",
            "guardian.command.config",
            "guardian.command.ip",
            "guardian.command.server",
            "guardian.command.teleport",
            "guardian.command.vpn",
            "godmode.admin",
            "godmode.autoenable",
            "godmode.invulnerable",
            "godmode.lootprotection",
            "godmode.noattacking",
            "godmode.toggle",
            "godmode.untiring",
            "kits.admin",
            "lockedcratetimer.conf.use",
            "nteleportation.admin",
            "nteleportation.tp",
            "nteleportation.tpn",
            "nteleportation.tpl",
            "nteleportation.tpsave",
            "nteleportation.tpremove",
            "nteleportation.tpconsole",
            "nteleportation.importhomes",
            "nteleportation.radiushome",
            "nteleportation.deletehome",
            "nteleportation.homehomes",
            "nteleportation.wipehomes",
            "placeholderapi.list",
            "placeholderapi.test",
            "removertool.admin",
            "removertool.all",
            "removertool.target",
            "removertool.external",
            "removertool.override",
            "removertool.structure",
            "serverarmour.ban",
            "serverarmour.unban",
            "serverarmour.radar",
            "serverarmour.website.admin",
            "serverrewards.admin",
            "serverrewardswipe.admin",
            "scoreboards.admin",
            "skins.admin",
            "stacksizecontroller.setstack",
            "stacksizecontroller.setstackcat",
            "stacksizecontroller.setallstacks",
            "stacksizecontroller.itemsearch",
            "stacksizecontroller.listcategories",
            "stacksizecontroller.listcategoryitems",
            "stacksizecontroller.vd"
        };

        private readonly string[] miniPermissions =
        {
            "spawnheli.minicopter.spawn",
            "spawnheli.minicopter.fetch",
            "spawnheli.minicopter.despawn"
        };

        private readonly string[] skinboxPermissions =
        {
            "skins.use",
            "buildingskins.use",
            "buildingskins.build",
            "buildingskins.tc",
            "buildingskins.all"
        };

        private readonly string[] bronzeExtraPermissions =
        {
            "serverrewards.paidpvpkit",
            "backpacks.size.12",
            "backpacks.fetch"
        };

        private readonly string[] goldExtraPermissions =
        {
            "backpacks.size.24",
            "backpacks.gather",
            "backpacks.retrieve"
        };

        private readonly string[] eliteExtraPermissions =
        {
            "backpacks.size.48",
            "backpacks.keepondeath",
            "backpacks.nofoodspoiling"
        };

        private readonly string[] defaultPublicPromoPermissions =
        {
            "backpacks.use",
            "backpacks.gui",
            "backpacks.size.6",
            "backpacks.size.12",
            "backpacks.fetch",
            "blueprintmanager.all",
            "serverrewards.paidpvpkit"
        };

        private void Loaded()
        {
            Puts("Legacy Raidlands permission reset is retired. Use WebsiteVipBridge permission sync for VIP/perk groups.");
        }

        private void OnServerInitialized()
        {
            Puts("RaidlandsPermissionReset did not apply legacy groups on server initialization.");
        }

        [ConsoleCommand("raidlands.permissions.apply")]
        private void ApplyCommand(ConsoleSystem.Arg arg)
        {
            if (arg != null && arg.Connection != null && arg.Connection.authLevel < 2)
            {
                SendReply(arg, "You must be a server admin to run this command.");
                return;
            }

            SendReply(arg, "RaidlandsPermissionReset is retired. Run websitevip.permissions.sync after applying the website workbook migration.");
        }

        private void ApplyReset(string source)
        {
            if (applied)
            {
                return;
            }

            applied = true;
            changed = 0;
            warnings.Clear();

            Puts("Starting Raidlands permission reset from " + source + ".");

            EnsureGroup("authenticated", "authenticated", 0, "");
            EnsureGroup("default", "default", 0, "");
            EnsureGroup("discord", "discord", 0, "");
            EnsureGroup("admin", "admin", 1, "");
            EnsureGroup("vip_bronze", "vip_bronze", 10, "");
            EnsureGroup("vip_gold", "vip_gold", 20, "vip_bronze");
            EnsureGroup("vip_elite", "vip_elite", 30, "vip_gold");
            EnsureGroup("perk_personal_mini", "perk_personal_mini", 0, "");
            EnsureGroup("perk_skinbox", "perk_skinbox", 0, "");
            EnsureGroup("perk_raid_kit", "perk_raid_kit", 0, "");
            EnsureGroup("perk_queue_priority", "perk_queue_priority", 0, "");
            EnsureGroup("perk_supporter_badge", "perk_supporter_badge", 0, "");

            RevokeGroupPermissions("default", defaultRevokes);
            GrantGroupPermissions("default", miniPermissions);
            GrantGroupPermissions("default", skinboxPermissions);
            GrantGroupPermissions("default", defaultPublicPromoPermissions);

            GrantGroupPermissions("discord", new[] { "kits.discord" });
            GrantGroupPermissions("admin", adminPermissions);
            GrantGroupPermissions("perk_personal_mini", miniPermissions);
            GrantGroupPermissions("perk_skinbox", skinboxPermissions);
            GrantGroupPermissions("perk_raid_kit", new[] { "serverrewards.paidpvpkit" });
            GrantGroupPermissions("vip_bronze", miniPermissions);
            GrantGroupPermissions("vip_bronze", skinboxPermissions);
            GrantGroupPermissions("vip_bronze", bronzeExtraPermissions);
            GrantGroupPermissions("vip_gold", goldExtraPermissions);
            GrantGroupPermissions("vip_elite", miniPermissions);
            GrantGroupPermissions("vip_elite", skinboxPermissions);
            GrantGroupPermissions("vip_elite", bronzeExtraPermissions);
            GrantGroupPermissions("vip_elite", goldExtraPermissions);
            GrantGroupPermissions("vip_elite", eliteExtraPermissions);

            permission.SaveData();

            Puts("Raidlands permission reset complete. Changed entries: " + changed + ".");
            PrintGroupSummary("default");
            PrintGroupSummary("discord");
            PrintGroupSummary("admin");
            PrintGroupSummary("vip_bronze");
            PrintGroupSummary("vip_gold");
            PrintGroupSummary("vip_elite");
            PrintGroupSummary("perk_personal_mini");
            PrintGroupSummary("perk_skinbox");
            PrintGroupSummary("perk_raid_kit");
            PrintGroupSummary("perk_queue_priority");
            PrintGroupSummary("perk_supporter_badge");

            if (warnings.Count > 0)
            {
                PrintWarning("Permission reset warnings:");
                foreach (var warning in warnings)
                {
                    PrintWarning("- " + warning);
                }
            }
        }

        private void EnsureGroup(string name, string title, int rank, string parent)
        {
            if (!permission.GroupExists(name))
            {
                if (permission.CreateGroup(name, title, rank))
                {
                    changed++;
                    Puts("Created group " + name + ".");
                }
                else
                {
                    warnings.Add("Could not create group " + name + ".");
                    return;
                }
            }

            if (permission.GetGroupTitle(name) != title && permission.SetGroupTitle(name, title))
            {
                changed++;
            }

            if (permission.GetGroupRank(name) != rank && permission.SetGroupRank(name, rank))
            {
                changed++;
            }

            var currentParent = permission.GetGroupParent(name) ?? "";
            var desiredParent = parent ?? "";

            if (!string.Equals(currentParent, desiredParent, StringComparison.OrdinalIgnoreCase))
            {
                if (permission.SetGroupParent(name, desiredParent))
                {
                    changed++;
                    Puts("Set parent for " + name + " to '" + desiredParent + "'.");
                }
                else
                {
                    if (name == "default" && desiredParent == "" && permission.GroupExists("authenticated")
                        && permission.SetGroupParent(name, "authenticated"))
                    {
                        changed++;
                        warnings.Add("Could not clear default parent, so default now inherits empty authenticated group instead.");
                    }
                    else
                    {
                        warnings.Add("Could not set parent for " + name + " to '" + desiredParent + "'.");
                    }
                }
            }
        }

        private void GrantGroupPermissions(string groupName, IEnumerable<string> permissions)
        {
            foreach (var perm in permissions)
            {
                if (GroupHasDirectPermission(groupName, perm))
                {
                    continue;
                }

                permission.GrantGroupPermission(groupName, perm, this);

                if (GroupHasDirectPermission(groupName, perm) || ForceAddGroupPermission(groupName, perm))
                {
                    changed++;
                }
                else
                {
                    warnings.Add("Grant did not stick: " + groupName + " -> " + perm + ".");
                }
            }
        }

        private void RevokeGroupPermissions(string groupName, IEnumerable<string> permissions)
        {
            foreach (var perm in permissions)
            {
                if (!GroupHasDirectPermission(groupName, perm))
                {
                    continue;
                }

                permission.RevokeGroupPermission(groupName, perm);

                if (!GroupHasDirectPermission(groupName, perm) || ForceRemoveGroupPermission(groupName, perm))
                {
                    changed++;
                }
                else
                {
                    warnings.Add("Revoke did not stick: " + groupName + " -> " + perm + ".");
                }
            }
        }

        private bool ForceAddGroupPermission(string groupName, string perm)
        {
            var perms = GetMutableGroupPermissions(groupName);

            if (perms == null)
            {
                warnings.Add("Could not inspect group data for " + groupName + " while granting " + perm + ".");
                return false;
            }

            if (perms.Any(item => string.Equals(item, perm, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            perms.Add(perm);
            Puts("Force-added group permission " + groupName + " -> " + perm + ".");
            return true;
        }

        private bool ForceRemoveGroupPermission(string groupName, string perm)
        {
            var perms = GetMutableGroupPermissions(groupName);

            if (perms == null)
            {
                warnings.Add("Could not inspect group data for " + groupName + " while revoking " + perm + ".");
                return false;
            }

            var existingPerm = perms.FirstOrDefault(item => string.Equals(item, perm, StringComparison.OrdinalIgnoreCase));

            if (existingPerm == null)
            {
                return true;
            }

            if (perms.Remove(existingPerm))
            {
                Puts("Force-removed group permission " + groupName + " -> " + existingPerm + ".");
                return true;
            }

            return false;
        }

        private ICollection<string> GetMutableGroupPermissions(string groupName)
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

            if (getGroupData == null)
            {
                return null;
            }

            var groupData = getGroupData.Invoke(permission, new object[] { groupName });

            if (groupData == null)
            {
                return null;
            }

            var permsField = groupData.GetType().GetField(
                "Perms",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (permsField != null)
            {
                return permsField.GetValue(groupData) as ICollection<string>;
            }

            var permsProperty = groupData.GetType().GetProperty(
                "Perms",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            return permsProperty != null ? permsProperty.GetValue(groupData, null) as ICollection<string> : null;
        }

        private bool GroupHasDirectPermission(string groupName, string perm)
        {
            return permission.GetGroupPermissions(groupName, false)
                .Any(item => string.Equals(item, perm, StringComparison.OrdinalIgnoreCase));
        }

        private void PrintGroupSummary(string groupName)
        {
            var directPermissions = permission.GetGroupPermissions(groupName, false);
            var inheritedPermissions = permission.GetGroupPermissions(groupName, true);
            var parent = permission.GetGroupParent(groupName) ?? "";

            Puts(string.Format(
                "Group {0}: parent='{1}', direct permissions={2}, with inherited={3}",
                groupName,
                parent,
                directPermissions.Length,
                inheritedPermissions.Length));
        }
    }
}
