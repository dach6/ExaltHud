using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExaltHUD.Protocol.Packets;

namespace ExaltHUD.Protocol;

internal class Packet
{
    private static readonly Dictionary<byte, Type> Types = new Dictionary<byte, Type>
    {
        { 10, typeof(NewTickPacket) }, { 11, typeof(ShowEffectPacket) },
        { 12, typeof(PlayerShootServerPacket) }, { 13, typeof(UseItemPacket) },
        { 25, typeof(EnemyHitPacket) }, { 30, typeof(PlayerShootPacket) },
        { 35, typeof(ServerPlayerShootPacket) }, { 42, typeof(UpdatePacket) },
        { 44, typeof(TextPacket) }, { 62, typeof(MovePacket) },
        { 67, typeof(NotificationPacket) }, { 75, typeof(EnemyShootPacket) },
        { 92, typeof(MapInfoPacket) }, { 101, typeof(GotoPacket) },
        { 148, typeof(DamageBoostPacket) },
        { 166, typeof(StasisPacket) }, { 183, typeof(CrucibleResponsePacket) },
        { 204, typeof(PartyActionResultPacket) }, { 210, typeof(IncomingPartyMemberInfoPacket) },
        { 212, typeof(PartyMemberAddedPacket) }
    };
    private static readonly Dictionary<Type, byte> Ids = Types.ToDictionary(pair => pair.Value, pair => pair.Key);
    private static readonly HashSet<byte> OutgoingIds = new HashSet<byte> { 13, 25, 30, 62 };

    public byte Id;
    public byte[] ExtraBytes = Array.Empty<byte>();

    protected Packet() => Id = Ids.TryGetValue(GetType(), out byte id) ? id : byte.MaxValue;
    public virtual void Read(PacketReader reader) { }
    // Serialization is retained for offline protocol tests; there is no network send path.
    public virtual void Write(PacketWriter writer) { }

    public static bool IsTracked(byte id, bool fromServer) => Types.ContainsKey(id) && fromServer != OutgoingIds.Contains(id);

    public static Packet Create(byte[] data)
    {
        if (data == null || data.Length < 5) throw new InvalidDataException("Incomplete game packet.");
        using var reader = new PacketReader(new MemoryStream(data, false));
        if (reader.ReadInt32() != data.Length) throw new InvalidDataException("Invalid game packet length.");
        byte id = reader.ReadByte();
        if (!Types.TryGetValue(id, out Type type)) return new Packet { Id = id };
        var packet = (Packet)Activator.CreateInstance(type);
        packet.Read(reader);
        packet.ExtraBytes = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
        return packet;
    }
}
