using System.Collections.Generic;
using System.Linq;
using DuckGame;

namespace qUAckzak.Mod.QuackHat
{
    internal sealed class QuackHatNetworkTransport
    {
        private readonly Dictionary<NetworkConnection, HashSet<QuackHatDefinition>> _sent =
            new();

        public void EnsureSent(QuackHatDefinition hat, Profile profile)
        {
            if (!Network.isActive || profile == null)
            {
                return;
            }

            HashSet<NetworkConnection> activeConnections = new(Network.connections);
            foreach (NetworkConnection oldConnection in _sent.Keys
                .Where(connection => !activeConnections.Contains(connection))
                .ToArray())
            {
                _sent.Remove(oldConnection);
            }

            foreach (NetworkConnection connection in Network.connections)
            {
                if (connection == DuckNetwork.localConnection)
                {
                    continue;
                }

                if (!_sent.TryGetValue(connection, out HashSet<QuackHatDefinition> hats))
                {
                    hats = new HashSet<QuackHatDefinition>();
                    _sent.Add(connection, hats);
                }

                if (!hats.Add(hat))
                {
                    continue;
                }

                bool filtered = connection.profile?.muteHat == true;
                foreach (QuackHatComponentDefinition component in hat.Components)
                {
                    foreach (QuackHatNetworkTileDefinition tile in component.NetworkTiles)
                    {
                        foreach (Team frameTeam in tile.FrameTeams)
                        {
                            Send.Message(
                                new NMSpecialHat(frameTeam, profile, filtered),
                                connection);
                        }
                    }
                }
            }
        }

        public void Reset()
        {
            _sent.Clear();
        }
    }
}
