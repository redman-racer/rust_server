// Requires: Clans

using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Clan Team", "rewritten", "2.0.0")]
    [Description("Modern clan-to-team sync system")]
    class ClanTeam : CovalencePlugin
    {
        [PluginReference]
        private Plugin Clans;

        // clanTag -> teamID
        private readonly Dictionary<string, ulong> clanTeams = new();

        // =========================
        // CORE HELPERS
        // =========================

        private string GetClanTag(ulong userId)
            => Clans.Call<string>("GetClanOf", userId);

        private List<ulong> GetClanMembers(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return new List<ulong>();

            JObject clan = Clans.Call<JObject>("GetClan", tag);
            if (clan == null || clan["members"] == null)
                return new List<ulong>();

            return clan["members"].ToObject<List<ulong>>() ?? new List<ulong>();
        }

        private BasePlayer FindPlayer(ulong id)
            => BasePlayer.FindByID(id);

        // =========================
        // TEAM CORE
        // =========================

        private RelationshipManager.PlayerTeam GetOrCreateTeam(string tag)
        {
            if (clanTeams.TryGetValue(tag, out ulong teamId))
            {
                var existing = RelationshipManager.ServerInstance.FindTeam(teamId);
                if (existing != null)
                    return existing;

                clanTeams.Remove(tag);
            }

            var newTeam = RelationshipManager.ServerInstance.CreateTeam();
            clanTeams[tag] = newTeam.teamID;
            return newTeam;
        }

        private void SyncClan(string tag)
        {
            var members = GetClanMembers(tag);
            if (members.Count == 0)
                return;

            var team = GetOrCreateTeam(tag);
            if (team == null)
                return;

            var validPlayers = new List<BasePlayer>();

            // Build valid player list
            foreach (var id in members)
            {
                var player = FindPlayer(id);
                if (player == null || !player.IsConnected)
                    continue;

                validPlayers.Add(player);
            }

            if (validPlayers.Count == 0)
                return;

            // Remove players not in clan anymore
            var currentMembers = team.members.ToList();
            foreach (var memberId in currentMembers)
            {
                if (!members.Contains(memberId))
                    team.RemovePlayer(memberId);
            }

            // Add missing players
            foreach (var player in validPlayers)
            {
                if (!team.members.Contains(player.userID))
                {
                    if (player.currentTeam != 0UL && player.currentTeam != team.teamID)
                    {
                        var oldTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                        oldTeam?.RemovePlayer(player.userID);
                    }

                    team.AddPlayer(player);
                    player.TeamUpdate();
                }
            }

            // Assign leader (first connected member)
            var leader = validPlayers.FirstOrDefault();
            if (leader != null)
                team.SetTeamLeader(leader.userID);
        }

        // =========================
        // HOOKS (EVENT DRIVEN)
        // =========================

        private void OnClanCreate(string tag)
        {
            timer.Once(1f, () => SyncClan(tag));
        }

        private void OnClanUpdate(string tag)
        {
            SyncClan(tag);
        }

        private void OnClanDestroy(string tag)
        {
            if (!clanTeams.TryGetValue(tag, out ulong teamId))
                return;

            var team = RelationshipManager.ServerInstance.FindTeam(teamId);
            if (team != null)
            {
                foreach (var member in team.members.ToList())
                    team.RemovePlayer(member);

                RelationshipManager.ServerInstance.DisbandTeam(team);
            }

            clanTeams.Remove(tag);
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            var tag = GetClanTag(player.userID);
            if (!string.IsNullOrEmpty(tag))
                SyncClan(tag);
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            var tag = GetClanTag(player.userID);
            if (!string.IsNullOrEmpty(tag))
                timer.Once(1f, () => SyncClan(tag));
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            var tag = GetClanTag(player.userID);
            if (!string.IsNullOrEmpty(tag))
                SyncClan(tag);
        }
    }
}