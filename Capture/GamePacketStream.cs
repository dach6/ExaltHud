using System;
using System.IO;
using ExaltHUD.Protocol;

namespace ExaltHUD.Capture;

internal sealed class GamePacketStream
{
    internal const string ServerKey = "c91d9eec420160730d825604e0";
    internal const string ClientKey = "5a4d2016bc16dc64883194ffd9";
    private const int MaximumPacketLength = 1024 * 1024;
    private readonly Rc4Cipher cipher;
    private readonly bool fromServer;
    private readonly Action<Packet> deliver;
    private byte[] buffer = new byte[8192];
    private int count;

    public GamePacketStream(bool fromServer, Action<Packet> deliver)
    {
        this.fromServer = fromServer;
        this.deliver = deliver;
        cipher = new Rc4Cipher(fromServer ? ServerKey : ClientKey);
    }

    public void Push(byte[] bytes)
    {
        if (count + bytes.Length > MaximumPacketLength * 2)
            throw new InvalidDataException("Game packet buffer exceeded its limit.");
        if (buffer.Length < count + bytes.Length)
            Array.Resize(ref buffer, Math.Max(count + bytes.Length, buffer.Length * 2));
        Buffer.BlockCopy(bytes, 0, buffer, count, bytes.Length);
        count += bytes.Length;
        int offset = 0;
        while (count - offset >= 5)
        {
            int length = (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
            if (length < 5 || length > MaximumPacketLength)
                throw new InvalidDataException("Game stream is out of sync. Reconnect the game.");
            if (count - offset < length) break;
            var data = new byte[length];
            Buffer.BlockCopy(buffer, offset, data, 0, length);
            offset += length;
            // Every payload advances RC4, including untracked packets such as the handshake.
            cipher.Crypt(data);
            if (Packet.IsTracked(data[4], fromServer))
            {
                try { deliver(Packet.Create(data)); }
                catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is IndexOutOfRangeException || ex is OverflowException)
                {
                    throw new InvalidDataException("Unable to read game packet " + data[4] + ". Reconnect; if this continues, the game protocol may have changed.", ex);
                }
            }
        }
        if (offset == 0) return;
        count -= offset;
        Buffer.BlockCopy(buffer, offset, buffer, 0, count);
        Array.Clear(buffer, count, offset);
    }
}
