using System;
using System.IO;
using System.Net;
using System.Text;

namespace ExaltHUD.Protocol;

internal class PacketReader : BinaryReader
{
	public PacketReader(MemoryStream stream)
		: base(stream, Encoding.UTF8)
	{
	}

	public override short ReadInt16()
	{
		return IPAddress.NetworkToHostOrder(base.ReadInt16());
	}

	public override ushort ReadUInt16()
	{
		return (ushort)IPAddress.NetworkToHostOrder((short)base.ReadUInt16());
	}

	public override int ReadInt32()
	{
		return IPAddress.NetworkToHostOrder(base.ReadInt32());
	}

	public override uint ReadUInt32() => unchecked((uint)ReadInt32());

	public override byte[] ReadBytes(int count)
	{
		if (count < 0 || count > BaseStream.Length - BaseStream.Position)
			throw new EndOfStreamException("Incomplete packet field.");
		return base.ReadBytes(count);
	}

	public int ReadCollectionCount(bool compressed, int minimumElementBytes = 1)
	{
		int count = compressed ? ReadCompressedInt() : ReadInt16();
		if (count < 0 || count > (BaseStream.Length - BaseStream.Position) / minimumElementBytes)
			throw new InvalidDataException("Invalid packet collection length.");
		return count;
	}

	public override float ReadSingle()
	{
		byte[] array = ReadBytes(4);
		Array.Reverse(array);
		return BitConverter.ToSingle(array, 0);
	}

	public override string ReadString()
	{
		return Encoding.UTF8.GetString(ReadBytes(ReadUInt16()));
	}

	public string ReadUTF()
	{
		return Encoding.UTF8.GetString(ReadBytes(ReadInt32()));
	}

	public int ReadCompressedInt()
	{
		byte current = base.ReadByte();
		bool negative = (current & 0x40) != 0;
		int shift = 6;
		uint magnitude = (uint)(current & 0x3F);
		while ((current & 0x80) != 0)
		{
			if (shift >= 32) throw new InvalidDataException("Invalid compressed integer.");
			current = base.ReadByte();
			if (shift == 27 && (current & 0x7F) > 16) throw new InvalidDataException("Compressed integer overflow.");
			magnitude |= (uint)(current & 0x7F) << shift;
			shift += 7;
		}
		if (magnitude > (negative ? 0x80000000u : 0x7fffffffu)) throw new InvalidDataException("Compressed integer overflow.");
		return negative ? unchecked(-(int)magnitude) : (int)magnitude;
	}
}
