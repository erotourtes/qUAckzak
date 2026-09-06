using System;
using System.IO;
using System.Linq;
using DuckGame;

namespace qUAckzak.Mod.QuackHat
{
    internal static class QuackHatRootHatRegistry
    {
        public static void Register(QuackHatDefinition hat)
        {
            if (Teams.core == null)
            {
                throw new InvalidOperationException(
                    "Duck Game's team registry is not initialized yet.");
            }

            Team team;
            try
            {
                team = Team.DeserializeFromPNG(
                    File.ReadAllBytes(hat.HatPath),
                    hat.Name,
                    null);
            }
            catch (Exception exception)
            {
                throw Error(hat, $"could not be loaded: {exception.Message}", exception);
            }

            if (team == null)
            {
                throw Error(hat, "is not a valid Duck Game custom-hat PNG.");
            }

            Teams.AddExtraTeam(team);
            HatSelector.remember = null;
            hat.RootTeam = team;
        }

        public static void Replace(
            QuackHatDefinition oldHat,
            QuackHatDefinition replacement)
        {
            Team oldTeam = oldHat.RootTeam;
            Team newTeam = replacement?.RootTeam;
            if (oldTeam == null)
            {
                return;
            }

            if (newTeam != null)
            {
                foreach (Profile profile in oldTeam.activeProfiles.ToArray())
                {
                    oldTeam.Leave(profile, false);
                    profile.team = newTeam;
                }

                if (Level.current != null)
                {
                    foreach (TeamHat teamHat in Level.current.things[typeof(TeamHat)]
                        .Cast<TeamHat>()
                        .ToArray())
                    {
                        if (ReferenceEquals(teamHat.team, oldTeam))
                        {
                            teamHat.team = newTeam;
                        }
                    }
                }
            }

            Teams.core.extraTeams.Remove(oldTeam);
            HatSelector.remember = null;
            oldHat.RootTeam = null;
        }

        private static InvalidDataException Error(
            QuackHatDefinition hat,
            string message,
            Exception innerException = null)
        {
            return new InvalidDataException(
                $"Root hat '{Path.GetFileName(hat.HatPath)}' {message}",
                innerException);
        }
    }
}
