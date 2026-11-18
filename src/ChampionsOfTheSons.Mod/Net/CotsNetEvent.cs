using Bolt;
using ChampionsOfTheSons.Core.Networking;
using RedLoader;
using SonsSdk.Networking;
using UdpKit;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// The mod's single SonsSdk NetEvent. Payload = one <see cref="MessageCodec"/> message,
/// length-prefixed. Handlers subscribe to <see cref="Received"/>.
/// </summary>
public sealed class CotsNetEvent : Packets.NetEvent
{
    public static readonly CotsNetEvent Instance = new();

    public override string Id => "ChampionsOfTheSons";

    /// <summary>A decoded message and the Bolt connection it arrived on (on a client this is the host's connection).</summary>
    public static event Action<IMessage, BoltConnection>? Received;

    public void Send(IMessage message, GlobalTargets targets)
    {
        var data = MessageCodec.Encode(message);
        var packet = NewPacket(4 + data.Length, targets);
        WritePayload(packet.Packet, data);
        Send(packet);
    }

    public void SendTo(IMessage message, BoltConnection connection)
    {
        var data = MessageCodec.Encode(message);
        var packet = NewPacket(4 + data.Length, connection);
        WritePayload(packet.Packet, data);
        Send(packet);
    }

    public override void Read(UdpPacket packet, BoltConnection fromConnection)
    {
        IMessage message;
        try
        {
            int length = packet.ReadInt();
            if (length < 1 || length > 64 * 1024) throw new PacketException($"Bad payload length {length}");
            message = MessageCodec.Decode(packet.ReadByteArray(length));
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: dropped malformed packet: {e.Message}");
            return;
        }
        Received?.Invoke(message, fromConnection);
    }

    private static void WritePayload(UdpPacket packet, byte[] data)
    {
        packet.WriteInt(data.Length);
        packet.WriteByteArray(data);
    }
}
