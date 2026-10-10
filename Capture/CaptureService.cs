using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ExaltHUD.Capture;

internal sealed class CaptureService : IDisposable
{
    private readonly ConcurrentQueue<CaptureEvent> events = new ConcurrentQueue<CaptureEvent>();
    private readonly ManualResetEvent stop = new ManualResetEvent(false);
    private Thread worker;
    private int eventCount;
    private int overflowed;
    private volatile CaptureStatus status = new CaptureStatus(CaptureState.Stopped, "Ready. Start listening, then connect to the game.");
    public CaptureStatus Status => status;
    public bool IsRunning => worker?.IsAlive == true;

    public void Start(CaptureDevice device)
    {
        if (IsRunning) throw new InvalidOperationException("Capture is already running.");
        ClearEvents();
        Interlocked.Exchange(ref overflowed, 0);
        stop.Reset();
        status = new CaptureStatus(CaptureState.Listening, "Opening the selected network adapter...");
        worker = new Thread(() => CaptureLoop(device.Name)) { IsBackground = true, Name = "ExaltHUD packet capture" };
        worker.Start();
    }

    private bool Enqueue(CaptureEvent item)
    {
        if (Volatile.Read(ref overflowed) != 0) return false;
        if (Interlocked.Increment(ref eventCount) > 4096)
        {
            Interlocked.Decrement(ref eventCount);
            Interlocked.Exchange(ref overflowed, 1);
            return false;
        }
        events.Enqueue(item);
        return true;
    }

    public bool TryDequeue(out CaptureEvent item)
    {
        if (!events.TryDequeue(out item)) return false;
        Interlocked.Decrement(ref eventCount);
        return true;
    }

    public bool ConsumeOverflow()
    {
        if (Volatile.Read(ref overflowed) == 0) return false;
        // Keep the producer blocked until the old queue is empty, so a new session's
        // handshake/map cannot be discarded while later packets survive.
        ClearEvents();
        Interlocked.Exchange(ref overflowed, 0);
        return true;
    }
    public void ClearEvents() { while (TryDequeue(out _)) { } }

    private void CaptureLoop(string device)
    {
        IntPtr handle = IntPtr.Zero;
        IntPtr statistics = IntPtr.Zero;
        try
        {
            NpcapNative.EnsureAvailable();
            var error = new StringBuilder(256);
            handle = NpcapNative.pcap_create(device, error);
            if (handle == IntPtr.Zero) throw new InvalidOperationException(error.ToString());
            Check(NpcapNative.pcap_set_snaplen(handle, 262144), handle);
            Check(NpcapNative.pcap_set_promisc(handle, 0), handle);
            Check(NpcapNative.pcap_set_timeout(handle, 50), handle);
            Check(NpcapNative.pcap_set_buffer_size(handle, 16 * 1024 * 1024), handle);
            Check(NpcapNative.pcap_activate(handle), handle);
            int linkType = NpcapNative.pcap_datalink(handle);
            if (!FrameDecoder.SupportsLinkType(linkType))
                throw new InvalidOperationException("This adapter's capture format is unsupported. Select your Ethernet, Wi-Fi, or VPN adapter.");
            // Include IPv4 TCP fragments so later fragments can be joined to their port-2050 header.
            string filter = "tcp port 2050 or (ip proto 6 and (ip[6:2] & 0x3fff != 0))";
            Check(NpcapNative.pcap_compile(handle, out var program, filter, 1, uint.MaxValue), handle);
            try { Check(NpcapNative.pcap_setfilter(handle, ref program), handle); }
            finally { NpcapNative.pcap_freecode(ref program); }
            if (NpcapNative.pcap_setnonblock(handle, 1, error) != 0) throw new InvalidOperationException(error.ToString());
            // Nonblocking reads plus an interruptible wait make Stop and application exit reliable.
            var frames = new FrameDecoder();
            var connection = new GameConnection(Enqueue, value => status = value);
            status = new CaptureStatus(CaptureState.Listening, "Listening. Connect, reconnect, or change servers in the game.");
            statistics = Marshal.AllocHGlobal(128);
            uint previousDrops = 0;
            DateTime lastStats = DateTime.UtcNow;
            while (!stop.WaitOne(0))
            {
                int result = NpcapNative.pcap_next_ex(handle, out IntPtr headerPointer, out IntPtr dataPointer);
                if (result == -1) throw new InvalidOperationException(Marshal.PtrToStringAnsi(NpcapNative.pcap_geterr(handle)));
                if (result == -2) break;
                DateTime now = DateTime.UtcNow;
                if (result == 1)
                {
                    var header = (NpcapNative.PacketHeader)Marshal.PtrToStructure(headerPointer, typeof(NpcapNative.PacketHeader));
                    if (header.CapturedLength > 262144 || header.CapturedLength < header.OriginalLength)
                        connection.Fault("A captured packet was truncated. Reconnect the game to resume tracking.");
                    else
                    {
                        var bytes = new byte[(int)header.CapturedLength];
                        Marshal.Copy(dataPointer, bytes, 0, bytes.Length);
                        connection.Process(frames.Decode(bytes, linkType, now), now);
                    }
                }
                else stop.WaitOne(10);
                connection.CheckTimeouts(now);
                if (now - lastStats >= TimeSpan.FromSeconds(1))
                {
                    if (NpcapNative.pcap_stats(handle, statistics) == 0)
                    {
                        uint drops = unchecked((uint)Marshal.ReadInt32(statistics, 4));
                        if (drops != previousDrops) connection.Fault("Npcap dropped game traffic. Reconnect to resume tracking.");
                        previousDrops = drops;
                    }
                    lastStats = now;
                }
            }
        }
        catch (Exception ex)
        {
            status = new CaptureStatus(CaptureState.Error, "Capture could not continue: " + ex.Message + " Check Npcap and the selected adapter.");
        }
        finally
        {
            if (statistics != IntPtr.Zero) Marshal.FreeHGlobal(statistics);
            if (handle != IntPtr.Zero) NpcapNative.pcap_close(handle);
        }
    }

    private static void Check(int result, IntPtr handle)
    {
        if (result < 0) throw new InvalidOperationException(Marshal.PtrToStringAnsi(NpcapNative.pcap_geterr(handle)));
    }

    public void Stop()
    {
        stop.Set();
        if (worker != null && !worker.Join(3000))
            throw new InvalidOperationException("Npcap has not stopped yet. Please wait before restarting capture.");
        worker = null;
        ClearEvents();
        status = new CaptureStatus(CaptureState.Stopped, "Listening stopped. Last results are retained.");
    }

    public void Dispose()
    {
        stop.Set();
        if (worker == null || worker.Join(3000)) stop.Dispose();
    }
}
