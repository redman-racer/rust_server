// Requires: Clans

using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Clan Team", "rewritten", "2.1.0")]
    [Description("Modern clan-to-team sync system")]
    class ClanTeam : CovalencePlugin
    {
        [PluginReference]
        private Plugin Clans;

        [PluginReference]
        private Plugin AutomaticAuthorization;

        // clanTag -> teamID
        private readonly Dictionary<string, ulong> clanTeams = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Timer> pendingSyncs = new(StringComparer.OrdinalIgnoreCase);

        private string GetClanTag(ulong userId)
            => Clans?.Call<string>("GetClanOf", userId);

        private List<ulong> GetClanMembers(string tag)
        {
            if (Clans == null || string.IsNullOrWhiteSpace(tag))
                return new List<ulong>();

            JObject clan = Clans.Call<JObject>("GetClan", tag);
            JArray members = clan?["members"] as JArray;
            if (members == null)
                return new List<ulong>();

            var result = new List<ulong>();
            foreach (JToken member in members)
            {
                ulong userId;
                if (ulong.TryParse(member.ToString(), out userId))
                    result.Add(userId);
            }

            return result.Distinct().ToList();
        }

        private BasePlayer FindPlayer(ulong id)
            => BasePlayer.FindByID(id) ?? BasePlayer.FindSleeping(id);

        private bool AreClanMembers(ulong firstPlayerId, ulong secondPlayerId)
        {
            if (Clans == null || firstPlayerId == 0UL || secondPlayerId == 0UL)
                return false;

            object result = Clans.Call("IsClanMember", firstPlayerId.ToString(), secondPlayerId.ToString());
            return result is bool && (bool)result;
        }

        private RelationshipManager.PlayerTeam FindExistingClanTeam(List<ulong> members)
        {
            foreach (ulong memberId in members)
            {
                BasePlayer player = FindPlayer(memberId);
                if (player == null || player.currentTeam == 0UL)
                    continue;

                RelationshipManager.PlayerTeam existing = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (existing != null && !clanTeams.Values.Contains(existing.teamID))
                    return existing;
            }

            return null;
        }

        private RelationshipManager.PlayerTeam GetOrCreateTeam(string tag, List<ulong> members)
        {
            ulong teamId;
            if (clanTeams.TryGetValue(tag, out teamId))
            {
                RelationshipManager.PlayerTeam known = RelationshipManager.ServerInstance.FindTeam(teamId);
                if (known != null)
                    return known;

                clanTeams.Remove(tag);
            }

            RelationshipManager.PlayerTeam existing = FindExistingClanTeam(members);
            if (existing != null)
            {
                clanTeams[tag] = existing.teamID;
                return existing;
            }

            RelationshipManager.PlayerTeam created = RelationshipManager.ServerInstance.CreateTeam();
            if (created != null)
                clanTeams[tag] = created.teamID;
            return created;
        }

        private void QueueSync(string tag, float delay = 0.5f)
        {
            if (string.IsNullOrWhiteSpace(tag) || Clans == null)
                return;

            Timer pending;
            if (pendingSyncs.TryGetValue(tag, out pending))
                pending?.Destroy();

            pendingSyncs[tag] = timer.Once(delay, () =>
            {
                pendingSyncs.Remove(tag);
                SyncClan(tag);
            });
        }

        private void SyncClan(string tag)
        {
            List<ulong> members = GetClanMembers(tag);
            if (members.Count == 0)
                return;

            RelationshipManager.PlayerTeam team = GetOrCreateTeam(tag, members);
            if (team == null)
                return;

            int changes = 0;

            foreach (ulong memberId in team.members.ToList())
            {
                if (members.Contains(memberId))
                    continue;

                team.RemovePlayer(memberId);
                changes++;
            }

            foreach (ulong memberId in members)
            {
                if (team.members.Contains(memberId))
                    continue;

                BasePlayer player = FindPlayer(memberId);
                if (player == null)
                    continue;

                if (player.currentTeam != 0UL && player.currentTeam != team.teamID)
                {
                    RelationshipManager.PlayerTeam oldTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                    oldTeam?.RemovePlayer(player.userID);
                }

                team.AddPlayer(player);
                player.TeamUpdate();
                changes++;
            }

            BasePlayer leader = members.Select(FindPlayer).FirstOrDefault(player => player != null && team.members.Contains(player.userID));
            if (leader != null && team.teamLeader != leader.userID)
                team.SetTeamLeader(leader.userID);

            if (changes > 0)
                Puts($"Synchronized clan [{tag}] with Rust team {team.teamID}: members={members.Count}, changes={changes}.");

            // Run lock/TC/turret reconciliation after the Rust team is final.
            AutomaticAuthorization?.Call("RaidlandsRefreshClanAuthorization", tag);
        }

        private void SyncAllClans()
        {
            if (Clans == null)
                return;

            JArray clans = Clans.Call<JArray>("GetAllClans");
            if (clans == null)
                return;

            foreach (JToken clan in clans)
                QueueSync(clan.ToString(), 0.1f);
        }

        private void OnServerInitialized()
        {
            timer.Once(1f, SyncAllClans);
            timer.Every(300f, SyncAllClans);
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin?.Name == "Clans")
                timer.Once(1f, SyncAllClans);
        }

        private void OnClanCreate(string tag) => QueueSync(tag);

        private void OnClanUpdate(string tag) => QueueSync(tag);

        // Clans 0.2.10 calls the universal List<string> overload after disbanding.
        private void OnClanDisbanded(string tag, List<string> members) => DestroyClanTeam(tag);

        // Compatibility with older Clans builds.
        private void OnClanDestroy(string tag) => DestroyClanTeam(tag);

        private void DestroyClanTeam(string tag)
        {
            Timer pending;
            if (pendingSyncs.TryGetValue(tag, out pending))
            {
                pending?.Destroy();
                pendingSyncs.Remove(tag);
            }

            ulong teamId;
            if (!clanTeams.TryGetValue(tag, out teamId))
                return;

            RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindTeam(teamId);
            if (team != null)
            {
                foreach (ulong member in team.members.ToList())
                    team.RemovePlayer(member);

                RelationshipManager.ServerInstance.DisbandTeam(team);
            }

            clanTeams.Remove(tag);
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player != null)
                QueueSync(GetClanTag(player.userID));
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player != null)
                QueueSync(GetClanTag(player.userID));
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            BasePlayer victim = entity as BasePlayer;
            BasePlayer attacker = info?.InitiatorPlayer;
            if (attacker == null && info?.Initiator is BaseEntity initiator && initiator.OwnerID != 0UL)
                attacker = FindPlayer(initiator.OwnerID);

            if (victim == null || attacker == null || victim.userID == attacker.userID)
                return null;

            if (!AreClanMembers(attacker.userID, victim.userID))
                return null;

            info.damageTypes.ScaleAll(0f);
            info.DoHitEffects = false;
            return true;
        }

        private void Unload()
        {
            foreach (Timer pending in pendingSyncs.Values)
                pending?.Destroy();

            pendingSyncs.Clear();
            clanTeams.Clear();
        }
    }
}
