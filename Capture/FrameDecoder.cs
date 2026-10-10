using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace ExaltHUD.Capture;

internal sealed class TcpSegment
{
    public string ConnectionKey;
    public bool FromServer;
    public uint Sequence;
    public uint Acknowledgment;
    public bool Syn;
    public bool Ack;
    public bool Fin;
    public bool Reset;
    public byte[] Payload;
}

internal sealed class FrameDecoder
{
    private sealed class Fragments
    {
        public readonly byte[] Bytes = new byte[65535];
        public readonly bool[] Present = new bool[65535];
        public DateTime Created;
        public int Count;
        public int Length = -1;
    }
    private readonly Dictionary<string, Fragments> fragments = new Dictionary<string, Fragments>();

    public static bool SupportsLinkType(int type) => type == 1 || type == 0 || type == 108 || type == 12 || type == 101;

    public TcpSegment Decode(byte[] frame, int linkType, DateTime now)
    {
        int offset;
        int protocol;
        if (linkType == 1)
        {
            if (frame.Length < 14) return null;
            protocol = U16(frame, 12);
            offset = 14;
            for (int tags = 0; (protocol == 0x8100 || protocol == 0x88a8) && tags < 2; tags++)
            {
                if (frame.Length < offset + 4) return null;
                protocol = U16(frame, offset + 2);
                offset += 4;
            }
        }
        else if (linkType == 0 || linkType == 108)
        {
            offset = 4;
            if (frame.Length <= offset) return null;
            protocol = (frame[offset] >> 4) == 6 ? 0x86dd : 0x0800;
        }
        else if (linkType == 12 || linkType == 101)
        {
            offset = 0;
            if (frame.Length == 0) return null;
            protocol = (frame[0] >> 4) == 6 ? 0x86dd : 0x0800;
        }
        else return null;

        if (protocol == 0x0800) return DecodeV4(frame, offset, now);
        if (protocol == 0x86dd) return DecodeV6(frame, offset);
        return null;
    }

    private TcpSegment DecodeV4(byte[] frame, int offset, DateTime now)
    {
        if (frame.Length < offset + 20 || frame[offset] >> 4 != 4 || frame[offset + 9] != 6) return null;
        int headerLength = (frame[offset] & 15) * 4;
        int totalLength = U16(frame, offset + 2);
        if (headerLength < 20 || totalLength < headerLength || frame.Length < offset + totalLength) return null;
        string source = Address(frame, offset + 12, 4);
        string destination = Address(frame, offset + 16, 4);
        int fragmentFlags = U16(frame, offset + 6);
        int fragmentOffset = (fragmentFlags & 0x1fff) * 8;
        bool moreFragments = (fragmentFlags & 0x2000) != 0;
        int payloadLength = totalLength - headerLength;
        if (!moreFragments && fragmentOffset == 0)
            return DecodeTcp(frame, offset + headerLength, payloadLength, source, destination);

        foreach (string key in fragments.Where(pair => now - pair.Value.Created > TimeSpan.FromSeconds(10)).Select(pair => pair.Key).ToArray())
            fragments.Remove(key);
        string fragmentKey = source + ">" + destination + ":" + U16(frame, offset + 4);
        if (fragmentOffset + payloadLength > 65535 || (moreFragments && payloadLength % 8 != 0)) return null;
        if (!fragments.TryGetValue(fragmentKey, out var parts))
        {
            if (fragments.Count >= 32) fragments.Remove(fragments.OrderBy(pair => pair.Value.Created).First().Key);
            parts = new Fragments { Created = now };
            fragments.Add(fragmentKey, parts);
        }
        if (!moreFragments)
        {
            int length = fragmentOffset + payloadLength;
            if (parts.Length >= 0 && parts.Length != length) { fragments.Remove(fragmentKey); return null; }
            parts.Length = length;
        }
        for (int i = 0; i < payloadLength; i++)
        {
            int position = fragmentOffset + i;
            byte value = frame[offset + headerLength + i];
            if (parts.Present[position])
            {
                if (parts.Bytes[position] != value) { fragments.Remove(fragmentKey); return null; }
            }
            else
            {
                parts.Present[position] = true;
                parts.Bytes[position] = value;
                parts.Count++;
            }
        }
        if (parts.Length < 0 || parts.Count != parts.Length) return null;
        for (int i = 0; i < parts.Length; i++) if (!parts.Present[i]) return null;
        fragments.Remove(fragmentKey);
        return DecodeTcp(parts.Bytes, 0, parts.Length, source, destination);
    }

    private static TcpSegment DecodeV6(byte[] frame, int offset)
    {
        if (frame.Length < offset + 40 || frame[offset] >> 4 != 6) return null;
        int end = offset + 40 + U16(frame, offset + 4);
        if (end > frame.Length) return null;
        string source = Address(frame, offset + 8, 16);
        string destination = Address(frame, offset + 24, 16);
        int next = frame[offset + 6];
        int cursor = offset + 40;
        for (int headers = 0; next != 6 && headers < 8; headers++)
        {
            if (next != 0 && next != 43 && next != 60) return null; // Fragmented IPv6 is not supported.
            if (cursor + 2 > end) return null;
            int length = (frame[cursor + 1] + 1) * 8;
            next = frame[cursor];
            cursor += length;
            if (cursor > end) return null;
        }
        return next == 6 ? DecodeTcp(frame, cursor, end - cursor, source, destination) : null;
    }

    private static TcpSegment DecodeTcp(byte[] data, int offset, int length, string source, string destination)
    {
        if (length < 20) return null;
        int sourcePort = U16(data, offset), destinationPort = U16(data, offset + 2);
        if (sourcePort != 2050 && destinationPort != 2050) return null;
        if (sourcePort == 2050 && destinationPort == 2050) return null;
        int headerLength = (data[offset + 12] >> 4) * 4;
        if (headerLength < 20 || headerLength > length) return null;
        bool fromServer = sourcePort == 2050;
        byte flags = data[offset + 13];
        byte[] payload = new byte[length - headerLength];
        Buffer.BlockCopy(data, offset + headerLength, payload, 0, payload.Length);
        return new TcpSegment
        {
            ConnectionKey = fromServer ? destination + ":" + destinationPort + ">" + source : source + ":" + sourcePort + ">" + destination,
            FromServer = fromServer,
            Sequence = U32(data, offset + 4),
            Acknowledgment = U32(data, offset + 8),
            Syn = (flags & 2) != 0, Ack = (flags & 16) != 0,
            Fin = (flags & 1) != 0, Reset = (flags & 4) != 0,
            Payload = payload
        };
    }

    private static string Address(byte[] data, int offset, int count)
    {
        byte[] bytes = new byte[count];
        Buffer.BlockCopy(data, offset, bytes, 0, count);
        return new IPAddress(bytes).ToString();
    }
    private static int U16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];
    private static uint U32(byte[] data, int offset) => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
}
