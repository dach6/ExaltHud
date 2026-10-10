using System;
using System.IO;
using ExaltHUD.Protocol;
using ExaltHUD.Protocol.Packets;

namespace ExaltHUD.Capture;

internal enum CaptureState { Stopped, Listening, Connecting, Tracking, NeedsReconnect, Error }

internal sealed class CaptureStatus
{
    public CaptureState State { get; }
    public string Message { get; }
    public CaptureStatus(CaptureState state, string message) { State = state; Message = message; }
}

internal sealed class CaptureEvent
{
    public bool BeginConnection;
    public Packet Packet;
}

internal sealed class GameConnection
{
    private readonly Func<CaptureEvent, bool> deliver;
    private readonly Action<CaptureStatus> status;
    private string key;
    private uint clientInitialSequence;
    private uint? serverInitialSequence;
    private TcpByteStream outgoing;
    private TcpByteStream incoming;
    private bool failed;
    private bool hasMap;
    private DateTime lastData;
    private DateTime connectedAt;

    public GameConnection(Func<CaptureEvent, bool> deliver, Action<CaptureStatus> status)
    {
        this.deliver = deliver;
        this.status = status;
    }

    public void Process(TcpSegment segment, DateTime now)
    {
        if (segment == null) return;
        if (!segment.FromServer && segment.Syn && !segment.Ack)
        {
            // A retransmitted SYN must never reset RC4 or the encounter a second time.
            if (key != segment.ConnectionKey || clientInitialSequence != segment.Sequence)
                Begin(segment, now);
        }
        if (key == null)
        {
            if (segment.Payload.Length > 0)
                status(new CaptureStatus(CaptureState.NeedsReconnect, "Game already connected. Reconnect or change servers to start tracking."));
            return;
        }
        if (segment.ConnectionKey != key || failed) return;
        try
        {
            if (segment.FromServer && segment.Syn && segment.Ack)
            {
                if (segment.Acknowledgment != unchecked(clientInitialSequence + 1)) return;
                if (!serverInitialSequence.HasValue)
                {
                    serverInitialSequence = segment.Sequence;
                    incoming.Start(unchecked(segment.Sequence + 1));
                }
                else if (serverInitialSequence.Value != segment.Sequence)
                    throw new InvalidDataException("Game connection changed. Reconnect to synchronize.");
            }
            if (segment.Payload.Length > 0)
            {
                lastData = now;
                (segment.FromServer ? incoming : outgoing).Push(unchecked(segment.Sequence + (segment.Syn ? 1u : 0u)), segment.Payload, now);
            }
            if (segment.Reset || segment.Fin)
            {
                failed = true;
                status(new CaptureStatus(CaptureState.Listening, "Game disconnected. Waiting for a new connection; last results are retained."));
            }
        }
        catch (InvalidDataException ex) { Fault(ex.Message); }
    }

    private void Begin(TcpSegment segment, DateTime now)
    {
        key = segment.ConnectionKey;
        clientInitialSequence = segment.Sequence;
        serverInitialSequence = null;
        failed = false;
        hasMap = false;
        connectedAt = lastData = now;
        var clientPackets = new GamePacketStream(false, OnPacket);
        var serverPackets = new GamePacketStream(true, OnPacket);
        outgoing = new TcpByteStream(clientPackets.Push);
        incoming = new TcpByteStream(serverPackets.Push);
        outgoing.Start(unchecked(segment.Sequence + 1));
        if (!deliver(new CaptureEvent { BeginConnection = true })) { Fault("Capture queue filled. Reconnect the game."); return; }
        status(new CaptureStatus(CaptureState.Connecting, "Game connection found. Waiting for map data..."));
    }

    private void OnPacket(Packet packet)
    {
        if (packet is MapInfoPacket map)
        {
            if (map.Width <= 0 || map.Height <= 0 || map.Width > 16384 || map.Height > 16384 || string.IsNullOrWhiteSpace(map.MapName))
                throw new InvalidDataException("Map data is invalid. Reconnect; the game protocol may have changed.");
            hasMap = true;
            status(new CaptureStatus(CaptureState.Tracking, "Tracking live game data."));
        }
        if (!deliver(new CaptureEvent { Packet = packet })) throw new InvalidDataException("Capture queue filled. Reconnect the game to resume tracking.");
    }

    public void CheckTimeouts(DateTime now)
    {
        if (key == null || failed) return;
        try
        {
            incoming.CheckGap(now);
            outgoing.CheckGap(now);
            if (!hasMap && now - connectedAt > TimeSpan.FromSeconds(30))
                Fault("No map data received. Reconnect and check the selected network adapter.");
            else if (hasMap && now - lastData > TimeSpan.FromSeconds(30))
                Fault("No recent game data. Reconnect to resume tracking; last results are retained.");
        }
        catch (InvalidDataException ex) { Fault(ex.Message); }
    }

    public void Fault(string message)
    {
        failed = true;
        status(new CaptureStatus(CaptureState.NeedsReconnect, message));
    }
}
