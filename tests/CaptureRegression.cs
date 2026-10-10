using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ExaltHUD;
using ExaltHUD.Capture;
using ExaltHUD.Data;
using ExaltHUD.Models;
using ExaltHUD.Protocol;
using ExaltHUD.Protocol.DataObjects;
using ExaltHUD.Protocol.Packets;
using ExaltHUD.Tracking;

internal static class CaptureRegression
{
    private static int checks;
    private static readonly DateTime Now = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is IOException || ex is InvalidDataException) { checks++; return; }
        throw new Exception(message);
    }
    private static byte[] B(string value) => Encoding.ASCII.GetBytes(value);
    private static void U16(byte[] data, int offset, int value) { data[offset] = (byte)(value >> 8); data[offset + 1] = (byte)value; }
    private static void U32(byte[] data, int offset, uint value) { data[offset] = (byte)(value >> 24); data[offset + 1] = (byte)(value >> 16); data[offset + 2] = (byte)(value >> 8); data[offset + 3] = (byte)value; }
    private static byte[] Join(params byte[][] bytes) => bytes.SelectMany(value => value).ToArray();
    private static byte[] Slice(byte[] bytes, int start, int length) => bytes.Skip(start).Take(length).ToArray();

    private static byte[] Raw(byte id, byte[] payload)
    {
        byte[] result = new byte[5 + payload.Length];
        U32(result, 0, (uint)result.Length);
        result[4] = id;
        Buffer.BlockCopy(payload, 0, result, 5, payload.Length);
        return result;
    }
    private static byte[] Wire(Packet packet, Rc4Cipher cipher)
    {
        using var stream = new MemoryStream();
        using var writer = new PacketWriter(stream);
        writer.Write(0);
        writer.Write(packet.Id);
        packet.Write(writer);
        byte[] result = stream.ToArray();
        U32(result, 0, (uint)result.Length);
        cipher.Crypt(result);
        return result;
    }
    private static MapInfoPacket Map(string name = "Test dungeon") => new MapInfoPacket
    {
        Width = 100, Height = 100, MapName = name, DisplayName = name,
        RealmName = "", VersionNumber = "fixture", DungeonModifiers = "", Fp = 0x12345678
    };

    private static byte[] Frame(bool fromServer, uint sequence, byte flags, byte[] payload = null, uint acknowledgment = 0)
    {
        payload = payload ?? Array.Empty<byte>();
        byte[] frame = new byte[54 + payload.Length];
        U16(frame, 12, 0x0800);
        frame[14] = 0x45; U16(frame, 16, frame.Length - 14); U16(frame, 18, 37); frame[22] = 64; frame[23] = 6;
        byte[] client = { 192, 0, 2, 1 }, server = { 198, 51, 100, 1 };
        Buffer.BlockCopy(fromServer ? server : client, 0, frame, 26, 4);
        Buffer.BlockCopy(fromServer ? client : server, 0, frame, 30, 4);
        U16(frame, 34, fromServer ? 2050 : 45000); U16(frame, 36, fromServer ? 45000 : 2050);
        U32(frame, 38, sequence); U32(frame, 42, acknowledgment); frame[46] = 0x50; frame[47] = flags;
        Buffer.BlockCopy(payload, 0, frame, 54, payload.Length);
        return frame;
    }
    private static byte[] Fragment(byte[] frame, int offset, int length, bool more)
    {
        var fragment = new byte[34 + length];
        Buffer.BlockCopy(frame, 0, fragment, 0, 34);
        U16(fragment, 16, 20 + length); U16(fragment, 20, (offset / 8) | (more ? 0x2000 : 0));
        Buffer.BlockCopy(frame, 34 + offset, fragment, 34, length);
        return fragment;
    }

    private static void TestStreams()
    {
        var received = new List<byte>();
        var tcp = new TcpByteStream(bytes => received.AddRange(bytes));
        tcp.Start(100);
        tcp.Push(104, B("efgh"), Now);
        Check(received.Count == 0, "TCP delivered a segment before its gap was filled");
        tcp.Push(100, B("abcd"), Now);
        Check(Encoding.ASCII.GetString(received.ToArray()) == "abcdefgh", "TCP reordering failed");
        tcp.Push(100, B("abcdefgh"), Now);
        Check(received.Count == 8, "TCP retransmission was delivered twice");
        tcp.Push(106, B("ghij"), Now);
        Check(Encoding.ASCII.GetString(received.ToArray()) == "abcdefghij", "Overlapping retransmission lost its new suffix");
        tcp.Start(uint.MaxValue - 3); received.Clear();
        tcp.Push(uint.MaxValue - 3, B("abcd"), Now); tcp.Push(0, B("ef"), Now);
        Check(Encoding.ASCII.GetString(received.ToArray()) == "abcdef", "TCP sequence wraparound failed");
        tcp.Push(4, B("ij"), Now);
        Reject(() => tcp.CheckGap(Now.AddSeconds(11)), "A missing segment did not time out");
        tcp.Start(0);
        Reject(() => tcp.Push(5000000, B("x"), Now), "Unbounded TCP gap was accepted");

        // Published RC4 vector (Key / Plaintext), independent of our packet serializer.
        var vector = Raw(0, B("Plaintext"));
        new Rc4Cipher(B("Key")).Crypt(vector);
        Check(BitConverter.ToString(vector.Skip(5).ToArray()).Replace("-", "") == "BBF316E8D940AF0AD3", "RC4 vector mismatch");
        Check(vector[4] == 0 && vector[3] == 14, "RC4 changed the plaintext game header");

        var packets = new List<Packet>();
        var framer = new GamePacketStream(true, packets.Add);
        var encryption = new Rc4Cipher(GamePacketStream.ServerKey);
        var untracked = Raw(254, B("ignored packet")); encryption.Crypt(untracked);
        byte[] map = Wire(Map(), encryption);
        byte[] identity = Wire(new GotoPacket { ObjectId = 100, CharId = 2, Xml = "" }, encryption);
        byte[] stream = Join(untracked, map, identity);
        for (int i = 0; i < stream.Length; i++) framer.Push(new[] { stream[i] });
        Check(packets.Count == 2 && packets[0] is MapInfoPacket && packets[1] is GotoPacket, "Split headers or unknown-packet RC4 alignment failed");
        Check(((MapInfoPacket)packets[0]).Fp == 0x12345678, "Map seed endian decoding failed");
        Check(((GotoPacket)packets[1]).ObjectId == 100, "Stateful cipher failed on the second tracked packet");
        Reject(() => new GamePacketStream(true, _ => { }).Push(new byte[] { 0, 0, 0, 4, 12 }), "Invalid frame length accepted");
        Reject(() => new GamePacketStream(true, _ => { }).Push(new byte[] { 0x7f, 0xff, 0xff, 0xff, 12 }), "Oversized frame length accepted");
        using (var reader = new PacketReader(new MemoryStream(new byte[] { 1, 2, 3, 4 })))
            Check(reader.ReadUInt32() == 0x01020304, "Unsigned network integer is not big endian");
        using (var reader = new PacketReader(new MemoryStream(new byte[] { 0, 20 })))
            Reject(() => reader.ReadString(), "Truncated string was silently accepted");
        using (var reader = new PacketReader(new MemoryStream(new byte[] { 0xbf, 0xff, 0xff, 0xff, 0x7f })))
            Reject(() => reader.ReadCompressedInt(), "Overflowing compressed integer was accepted");
        foreach (int value in new[] { 0, 63, 64, -63, -64, int.MaxValue, int.MinValue })
        {
            using var encoded = new MemoryStream();
            using var writer = new PacketWriter(encoded);
            writer.WriteCompressedInt(value);
            encoded.Position = 0;
            using var reader = new PacketReader(encoded);
            Check(reader.ReadCompressedInt() == value, "Compressed integer boundary failed: " + value);
        }
    }

    private static void TestFrames()
    {
        var decoder = new FrameDecoder();
        var frame = Frame(true, 42, 0x18, B("abcdefghijklmnop"));
        var segment = decoder.Decode(frame, 1, Now);
        Check(segment.FromServer && segment.Sequence == 42 && Encoding.ASCII.GetString(segment.Payload) == "abcdefghijklmnop", "Ethernet IPv4 decode failed");
        var reverse = decoder.Decode(Frame(false, 22, 2), 1, Now);
        Check(reverse.ConnectionKey == segment.ConnectionKey && !reverse.FromServer, "Bidirectional connection key mismatch");
        Check(decoder.Decode(Slice(frame, 0, 40), 1, Now) == null, "Truncated IPv4 packet accepted");
        var unrelated = (byte[])frame.Clone(); U16(unrelated, 34, 443);
        Check(decoder.Decode(unrelated, 1, Now) == null, "Unrelated traffic was decoded");
        var vlan = new byte[frame.Length + 4];
        Buffer.BlockCopy(frame, 0, vlan, 0, 12); U16(vlan, 12, 0x8100); U16(vlan, 16, 0x0800);
        Buffer.BlockCopy(frame, 14, vlan, 18, frame.Length - 14);
        Check(decoder.Decode(vlan, 1, Now)?.Payload.Length == 16, "VLAN decoding failed");
        var loopback = Join(new byte[] { 2, 0, 0, 0 }, Slice(frame, 14, frame.Length - 14));
        Check(decoder.Decode(loopback, 0, Now)?.Payload.Length == 16, "Loopback link header failed");
        Check(decoder.Decode(Slice(frame, 14, frame.Length - 14), 101, Now)?.Payload.Length == 16, "Raw IP decoding failed");
        Check(decoder.Decode(frame, 999, Now) == null, "Unsupported link type accepted");
        byte[] first = Fragment(frame, 0, 24, true), last = Fragment(frame, 24, 12, false);
        Check(decoder.Decode(last, 1, Now) == null, "IP fragment delivered before reassembly");
        Check(decoder.Decode(last, 1, Now) == null, "Duplicate fragment completed a partial datagram");
        Check(decoder.Decode(first, 1, Now)?.Payload.SequenceEqual(B("abcdefghijklmnop")) == true, "Out-of-order IPv4 fragments failed");
        decoder = new FrameDecoder();
        decoder.Decode(first, 1, Now);
        Check(decoder.Decode(last, 1, Now.AddSeconds(11)) == null, "Expired IP fragments were reused");
        decoder = new FrameDecoder();
        decoder.Decode(first, 1, Now);
        var conflicting = (byte[])first.Clone(); conflicting[34] ^= 1;
        Check(decoder.Decode(conflicting, 1, Now) == null && decoder.Decode(last, 1, Now) == null, "Conflicting IP fragment overlap accepted");

        // Basic IPv6, with addresses from the documentation prefix.
        byte[] v6 = new byte[14 + 40 + 20 + 16]; U16(v6, 12, 0x86dd); v6[14] = 0x60; U16(v6, 18, 36); v6[20] = 6;
        v6[22] = v6[38] = 0x20; v6[23] = v6[39] = 1; v6[37] = 1; v6[53] = 2;
        Buffer.BlockCopy(frame, 34, v6, 54, 36);
        Check(decoder.Decode(v6, 1, Now)?.Payload.Length == 16, "IPv6 TCP decoding failed");
    }

    private static void TestConnectionAndTracking()
    {
        if (GameData.Objects == null) GameData.LoadGameData();
        AbilityScalingManager.EnsureLoaded(); RealmSharkData.EnsureLoaded();
        ItemSpriteManager.InitializeAssets();
        Check(ItemSpriteManager.GetItemMetadata(17842).Name.Contains("Fractal"), "Bundled item names require external sprite assets");
        using var session = new TrackingSession();
        using var view = new DpsOverlayForm(true);
        DpsOverlayManager.Attach(view);
        CaptureStatus status = null;
        int starts = 0, packets = 0;
        var connection = new GameConnection(item =>
        {
            if (item.BeginConnection) { starts++; session.BeginConnection(); }
            else { packets++; session.Process(item.Packet); }
            return true;
        }, value => status = value);
        var decoder = new FrameDecoder();
        Action<byte[]> feed = bytes => connection.Process(decoder.Decode(bytes, 1, Now), Now);
        feed(Frame(true, 800, 0x18, B("already playing")));
        Check(status.State == CaptureState.NeedsReconnect && packets == 0, "Mid-session capture must request a reconnect");
        feed(Frame(false, 100, 2)); feed(Frame(true, 500, 0x12, acknowledgment: 101));
        Check(starts == 1 && status.State == CaptureState.Connecting, "Connection handshake failed");

        var serverCipher = new Rc4Cipher(GamePacketStream.ServerKey);
        uint serverSequence = 501;
        Action<Packet> server = packet => { var bytes = Wire(packet, serverCipher); feed(Frame(true, serverSequence, 0x18, bytes)); serverSequence += (uint)bytes.Length; };
        server(Map());
        Check(status.State == CaptureState.Tracking, "Validated map did not enable tracking");
        feed(Frame(false, 100, 2)); feed(Frame(true, 500, 0x12, acknowledgment: 101));
        Check(starts == 1, "Retransmitted SYN reset the active session");
        server(new GotoPacket { ObjectId = 100, CharId = 1, Xml = "" });
        ushort playerType = GameData.Objects.Map.First(pair => pair.Value.Player).Key;
        ushort enemyType = GameData.Objects.Map.First(pair => pair.Value.Enemy && pair.Value.MaxHP > 0).Key;
        var player = new ObjectData { ObjectType = playerType, Stats = new ObjectStatsData { ObjectId = 100, Position = new WorldPosData(10, 10), StatList = new List<StatData>
        {
            new StatData { Type = StatType.Name, StringValue = "FixturePlayer" }, new StatData { Type = StatType.HP, Value = 700 },
            new StatData { Type = StatType.MaximumHP, Value = 800 }, new StatData { Type = StatType.Inventory0, Value = 17842 },
            new StatData { Type = StatType.Inventory1, Value = 21228 }, new StatData { Type = StatType.Attack, Value = 50 }
        } } };
        var enemy = new ObjectData { ObjectType = enemyType, Stats = new ObjectStatsData { ObjectId = 900, Position = new WorldPosData(12, 10), StatList = new List<StatData>
        {
            new StatData { Type = StatType.HP, Value = 1000 }, new StatData { Type = StatType.MaximumHP, Value = 1000 },
            new StatData { Type = StatType.Defense, Value = 0 }
        } } };
        server(new UpdatePacket { PlayerPosition = new WorldPosData(10, 10), Tiles = Array.Empty<TileData>(), NewObjects = new[] { player, enemy }, Drops = Array.Empty<int>() });
        Check(session.ClientId == 100 && session.Player.PlayerName == "FixturePlayer", "Local identity or player state was lost");
        Check(session.Enemies.ContainsKey(900) && session.Portals.Count == 2, "Passive entity classification failed");

        // Coalesced shots/damage in reordered TCP segments, including a retransmission.
        byte[] combat = Join(Wire(new PlayerShootServerPacket { OwnerId = 100, BulletId = 256, ContainerType = 17842, Damage = 100 }, serverCipher),
            Wire(new EnemyShootPacket { ObjectId = 100, OwnerId = 900, _bulletId = 256, _damageAmount = 100, _effects = Array.Empty<byte>() }, serverCipher));
        feed(Frame(true, serverSequence + 7, 0x18, Slice(combat, 7, combat.Length - 7)));
        feed(Frame(true, serverSequence, 0x18, Slice(combat, 0, 7)));
        feed(Frame(true, serverSequence, 0x18, combat)); serverSequence += (uint)combat.Length;
        var tick = new NewTickPacket { TickId = 1, TickTime = 200, Statuses = new List<ObjectStatsData> { new ObjectStatsData
        {
            ObjectId = 100, Position = new WorldPosData(11, 10), StatList = new List<StatData> { new StatData { Type = StatType.HP, Value = 600 } }
        } } };
        server(tick); session.Dps.PushSnapshotToOverlay();
        var snapshot = (DpsSnapshot)typeof(DpsOverlayForm).GetField("currentSnapshot", Fields).GetValue(view);
        Check(snapshot.TotalDungeonDamage == 100, "Encrypted capture did not produce exactly one combat hit");
        Check(snapshot.Players.Any(p => p.Name == "FixturePlayer") && session.Player.Hp == 600, "Tick/roster state was not delivered to the HUD");

        var clientCipher = new Rc4Cipher(GamePacketStream.ClientKey);
        var handshake = Raw(74, B("synthetic untracked handshake")); clientCipher.Crypt(handshake);
        var move = Wire(new MovePacket { TickId = 1, Positions = new[] { new MoveRecord(1, 20, 25) } }, clientCipher);
        feed(Frame(false, 101, 0x18, Join(handshake, move)));
        Check(session.Player.Position.X == 20 && session.Player.Position.Y == 25, "Independent outgoing cipher or move tracking failed");

        server(Map("Next dungeon"));
        Check(DpsTracker.GetDungeonHistoryList().Any(item => !item.IsCurrent), "Map transition did not preserve encounter history");
        Check(session.Entities.Count == 0 && session.Enemies.Count == 0, "Old entities leaked into the next map");
        feed(Frame(true, serverSequence + 10, 0x18, B("missing gap")));
        connection.CheckTimeouts(Now.AddSeconds(11));
        Check(status.State == CaptureState.NeedsReconnect, "Packet loss did not suspend live tracking");
        int oldPackets = packets;
        feed(Frame(true, serverSequence, 0x18, new byte[10]));
        Check(packets == oldPackets, "Desynchronized packets were still delivered");
        feed(Frame(false, 9000, 2)); feed(Frame(true, 7000, 0x12, acknowledgment: 9001));
        serverCipher = new Rc4Cipher(GamePacketStream.ServerKey); serverSequence = 7001; server(Map("Recovered"));
        Check(starts == 2 && status.State == CaptureState.Tracking, "Reconnect did not recover capture");
        feed(Frame(true, serverSequence, 0x14));
        Check(status.State == CaptureState.Listening, "TCP reset left the HUD marked live");
        DpsOverlayManager.Detach();

        CaptureStatus overflowStatus = null;
        var overflow = new GameConnection(_ => false, value => overflowStatus = value);
        overflow.Process(decoder.Decode(Frame(false, 1, 2), 1, Now), Now);
        Check(overflowStatus.State == CaptureState.NeedsReconnect, "Queue overflow did not stop tracking");
    }

    private static void TestViews()
    {
        using var host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };
        using var dashboard = new DpsOverlayForm(true);
        using var overlay = new DpsOverlayForm(false);
        host.Controls.Add(dashboard); host.Show(); dashboard.Show();
        DpsOverlayManager.Attach(dashboard, overlay);
        var snapshot = new DpsSnapshot { DungeonName = "Shared encounter" };
        DpsOverlayManager.UpdateSnapshot(snapshot);
        Check(ReferenceEquals(typeof(DpsOverlayForm).GetField("currentSnapshot", Fields).GetValue(overlay), snapshot), "Hidden overlay did not receive snapshots");
        Check(ReferenceEquals(typeof(DpsOverlayForm).GetField("currentSnapshot", Fields).GetValue(dashboard), snapshot), "Dashboard did not receive snapshots");
        Check(!dashboard.TopMost && !dashboard.TopLevel && dashboard.Visible, "Dashboard still behaves like an overlay");
        Check(!overlay.OverlayEnabled, "Overlay must start disabled");
        int toggles = 0; overlay.OverlayPreferenceChanged += (_, __) => toggles++;
        overlay.SetOverlayEnabled(true); overlay.SetClickThrough(true);
        Check(overlay.Visible && overlay.ClickThroughEnabled, "Overlay enable/lock failed");
        dashboard.SetClickThrough(true);
        Check(!dashboard.ClickThroughEnabled && dashboard.Visible, "Locking the overlay made the dashboard unusable");
        overlay.ToggleVisibility();
        Check(!overlay.Visible && !overlay.OverlayEnabled && dashboard.Visible && toggles == 2, "Overlay visibility affected the main dashboard");
        dashboard.ToggleVisibility();
        Check(dashboard.Visible, "An overlay hide action hid the embedded dashboard");
        DpsOverlayManager.Detach();
    }

    private static void TestNpcap()
    {
        var devices = NpcapNative.GetDevices();
        Check(devices.Count > 0, "Npcap did not list adapters");
        using var capture = new CaptureService();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            capture.Start(devices[0]);
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 5000 && capture.Status.Message.StartsWith("Opening")) Thread.Sleep(10);
            Check(capture.IsRunning && capture.Status.State != CaptureState.Error, capture.Status.Message);
            clock.Restart(); capture.Stop();
            Check(!capture.IsRunning && capture.Status.State == CaptureState.Stopped && clock.ElapsedMilliseconds < 1000, "Capture did not stop cleanly");
        }
    }

    private static void TestMainWindow(string screenshot)
    {
        using var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };
        form.Show();
        Check(form.ClientSize.Height >= 500, "Main window height was collapsed by its size constraints");
        var clock = Stopwatch.StartNew();
        var button = (Button)typeof(MainForm).GetField("listen", Fields).GetValue(form);
        while (!button.Enabled && clock.ElapsedMilliseconds < 15000) { Application.DoEvents(); Thread.Sleep(10); }
        Check(button.Enabled, "Main window did not finish loading data/adapters: " + ((Label)typeof(MainForm).GetField("status", Fields).GetValue(form)).Text);
        var dashboard = (DpsOverlayForm)typeof(MainForm).GetField("dashboard", Fields).GetValue(form);
        Check(dashboard.Visible && dashboard.Width == 380 && dashboard.Height >= 250, "Dashboard is clipped or has the wrong layout width");
        var toggle = (CheckBox)typeof(MainForm).GetField("overlayToggle", Fields).GetValue(form);
        var overlay = (DpsOverlayForm)typeof(MainForm).GetField("overlay", Fields).GetValue(form);
        toggle.Checked = true; Check(overlay.OverlayEnabled, "Main window checkbox did not enable overlay");
        overlay.ToggleVisibility(); Check(!toggle.Checked, "Overlay close/hotkey did not synchronize the checkbox");
        button.PerformClick();
        clock.Restart();
        var capture = (CaptureService)typeof(MainForm).GetField("capture", Fields).GetValue(form);
        while (clock.ElapsedMilliseconds < 5000 && (!capture.IsRunning || capture.Status.Message.StartsWith("Opening"))) { Application.DoEvents(); Thread.Sleep(10); }
        Check(capture.IsRunning && capture.Status.State != CaptureState.Error, "Main window could not start listening");
        button.PerformClick(); clock.Restart();
        while (clock.ElapsedMilliseconds < 5000 && capture.IsRunning) { Application.DoEvents(); Thread.Sleep(10); }
        // Pump the UI once more so the async stop continuation and timer can update the label.
        clock.Restart(); while (clock.ElapsedMilliseconds < 100) { Application.DoEvents(); Thread.Sleep(10); }
        Check(!capture.IsRunning && button.Text == "Start listening", "Main window stop action did not reset its button");
        var statusLabel = (Label)typeof(MainForm).GetField("status", Fields).GetValue(form);
        Check(statusLabel.Text.Contains("stopped"), "Main window retained a live status after stopping");
        if (!string.IsNullOrEmpty(screenshot))
        {
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(screenshot);
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            int index = Array.IndexOf(args, "--ui-smoke");
            if (index >= 0) TestMainWindow(index + 1 < args.Length ? args[index + 1] : null);
            TestStreams(); TestFrames(); TestConnectionAndTracking(); TestViews();
            if (args.Contains("--npcap")) TestNpcap();
            Console.WriteLine("PASS: " + checks + " standalone capture, tracking, and window assertions.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
