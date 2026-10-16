using Riptide;

namespace Multiplayer
{
    // Decides which builds can play together. Application.version is only the label shown in the menu.
    // Bump Version when message IDs, field order or types, or the meaning of synced data change.
    public static class NetworkProtocol
    {
        public const int Version = 1;

        public const string LobbyKey = "ProtocolVersion";

        public static Message ConnectMessage() => Message.Create().AddInt(Version);

        public static bool IsCompatible(Message connectMessage) =>
            connectMessage.UnreadLength >= sizeof(int) && connectMessage.GetInt() == Version;
    }
}
