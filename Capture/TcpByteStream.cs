using System;
using System.Collections.Generic;
using System.IO;

namespace ExaltHUD.Capture;

// Reassembles one direction of a TCP connection before any cipher bytes are consumed.
internal sealed class TcpByteStream
{
    private const int MaximumBufferedBytes = 4 * 1024 * 1024;
    private readonly Dictionary<uint, byte[]> pending = new Dictionary<uint, byte[]>();
    private readonly Action<byte[]> deliver;
    private uint nextSequence;
    private int bufferedBytes;
    private DateTime? gapSince;
    public bool Initialized { get; private set; }

    public TcpByteStream(Action<byte[]> deliver) => this.deliver = deliver;

    public void Start(uint firstSequence)
    {
        Initialized = true;
        nextSequence = firstSequence;
        pending.Clear();
        bufferedBytes = 0;
        gapSince = null;
    }

    public void Push(uint sequence, byte[] data, DateTime now)
    {
        if (!Initialized) throw new InvalidDataException("Connection handshake was missed. Reconnect the game.");
        if (data.Length == 0) return;
        int distance = unchecked((int)(sequence - nextSequence));
        if (distance > 0)
        {
            if (distance > MaximumBufferedBytes) throw new InvalidDataException("TCP gap is too large. Reconnect the game.");
            if (!pending.TryGetValue(sequence, out byte[] existing) || data.Length > existing.Length)
            {
                int newSize = bufferedBytes + data.Length - (existing?.Length ?? 0);
                if (newSize > MaximumBufferedBytes || pending.Count >= 2048)
                    throw new InvalidDataException("Too much missing TCP data. Reconnect the game.");
                pending[sequence] = data;
                bufferedBytes = newSize;
            }
            if (!gapSince.HasValue) gapSince = now;
            CheckGap(now);
            return;
        }

        DeliverUnseen(sequence, data);
        while (true)
        {
            uint readySequence = 0;
            byte[] ready = null;
            foreach (var pair in pending)
            {
                if (unchecked((int)(pair.Key - nextSequence)) <= 0)
                {
                    readySequence = pair.Key;
                    ready = pair.Value;
                    break;
                }
            }
            if (ready == null) break;
            pending.Remove(readySequence);
            bufferedBytes -= ready.Length;
            DeliverUnseen(readySequence, ready);
        }
        if (pending.Count == 0) gapSince = null;
        CheckGap(now);
    }

    private void DeliverUnseen(uint sequence, byte[] data)
    {
        long alreadyRead = unchecked((int)(nextSequence - sequence));
        if (alreadyRead >= data.Length) return; // Retransmission/duplicate capture.
        if (alreadyRead < 0) throw new InvalidDataException("Invalid TCP sequence.");
        int count = data.Length - (int)alreadyRead;
        byte[] remaining = data;
        if (alreadyRead > 0)
        {
            remaining = new byte[count];
            Buffer.BlockCopy(data, (int)alreadyRead, remaining, 0, count);
        }
        nextSequence = unchecked(nextSequence + (uint)count);
        deliver(remaining);
    }

    public void CheckGap(DateTime now)
    {
        if (gapSince.HasValue && now - gapSince.Value > TimeSpan.FromSeconds(10))
            throw new InvalidDataException("Game packets were missed. Reconnect the game to resume tracking.");
    }
}
