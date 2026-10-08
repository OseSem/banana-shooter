using System.Collections.Generic;
using System.Linq;

namespace Multiplayer
{
    public class ClientAuthentication
    {
        private readonly Dictionary<ulong, ushort> _clientBySteamId = new();
        private readonly Dictionary<ushort, ulong> _steamIdByClient = new();
        private readonly HashSet<ulong> _authorized = new();

        public IReadOnlyCollection<ulong> SteamIds => _clientBySteamId.Keys.ToList();
        
        public bool TryBind(ushort client, ulong transportSteamId, ulong claimedSteamId)
        {
            if (transportSteamId == 0 || transportSteamId != claimedSteamId) return false;

            if (_clientBySteamId.TryGetValue(claimedSteamId, out var previousClient) && previousClient != client)
                _steamIdByClient.Remove(previousClient);

            _authorized.Remove(claimedSteamId);
            _clientBySteamId[claimedSteamId] = client;
            _steamIdByClient[client] = claimedSteamId;
            return true;
        }

        public bool TryAuthorize(ulong steamId, out ushort client)
        {
            if (!_clientBySteamId.TryGetValue(steamId, out client)) return false;

            _authorized.Add(steamId);
            return true;
        }

        public void Revoke(ulong steamId) => _authorized.Remove(steamId);

        public bool TryGetClient(ulong steamId, out ushort client) => _clientBySteamId.TryGetValue(steamId, out client);

        public bool TryGetAuthorizedSteamId(ushort client, out ulong steamId)
        {
            return _steamIdByClient.TryGetValue(client, out steamId) && _authorized.Contains(steamId);
        }

        public bool Remove(ushort client, out ulong steamId)
        {
            if (!_steamIdByClient.Remove(client, out steamId)) return false;

            _clientBySteamId.Remove(steamId);
            _authorized.Remove(steamId);
            return true;
        }

        public void Clear()
        {
            _clientBySteamId.Clear();
            _steamIdByClient.Clear();
            _authorized.Clear();
        }
    }
}