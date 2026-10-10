using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace ExaltHUD.Capture;

internal sealed class CaptureDevice
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Preferred { get; set; }
    public override string ToString() => Description;
}

internal static class NpcapNative
{
    private static IntPtr library;
    private static readonly object libraryLock = new object();

    public static void EnsureAvailable()
    {
        lock (libraryLock)
        {
            if (library != IntPtr.Zero) return;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Npcap", "wpcap.dll");
            if (!File.Exists(path)) throw new InvalidOperationException("Npcap is required. Install it from npcap.com, then restart ExaltHUD.");
            // Load from the Windows Npcap directory, with native dependencies beside it.
            library = LoadLibraryEx(path, IntPtr.Zero, 0x00000008);
            if (library == IntPtr.Zero) throw new InvalidOperationException("Npcap could not be loaded. Repair the 64-bit Npcap installation and restart ExaltHUD.");
        }
    }

    public static List<CaptureDevice> GetDevices()
    {
        EnsureAvailable();
        var error = new StringBuilder(256);
        if (pcap_findalldevs(out IntPtr first, error) != 0)
            throw new InvalidOperationException("Cannot list capture adapters: " + error);
        var result = new List<CaptureDevice>();
        try
        {
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            for (IntPtr item = first; item != IntPtr.Zero;)
            {
                var native = (PcapInterface)Marshal.PtrToStructure(item, typeof(PcapInterface));
                string name = Marshal.PtrToStringAnsi(native.Name);
                string description = Marshal.PtrToStringAnsi(native.Description) ?? name;
                bool preferred = false;
                var adapter = adapters.FirstOrDefault(a => name.IndexOf(a.Id, StringComparison.OrdinalIgnoreCase) >= 0);
                if (adapter != null)
                {
                    description = adapter.Name + " - " + adapter.Description;
                    preferred = adapter.OperationalStatus == OperationalStatus.Up &&
                        adapter.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any));
                }
                result.Add(new CaptureDevice { Name = name, Description = description, Preferred = preferred });
                item = native.Next;
            }
        }
        finally { pcap_freealldevs(first); }
        return result.OrderByDescending(d => d.Preferred).ThenBy(d => d.Description).ToList();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PcapInterface { public IntPtr Next, Name, Description, Addresses; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct PacketHeader { public int Seconds, Microseconds; public uint CapturedLength, OriginalLength; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct BpfProgram { public uint Length; public IntPtr Instructions; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int pcap_findalldevs(out IntPtr devices, StringBuilder error);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void pcap_freealldevs(IntPtr devices);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern IntPtr pcap_create(string device, StringBuilder error);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_set_snaplen(IntPtr handle, int length);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_set_promisc(IntPtr handle, int enabled);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_set_timeout(IntPtr handle, int milliseconds);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_set_buffer_size(IntPtr handle, int bytes);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_activate(IntPtr handle);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_setnonblock(IntPtr handle, int enabled, StringBuilder error);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_datalink(IntPtr handle);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int pcap_compile(IntPtr handle, out BpfProgram program, string filter, int optimize, uint netmask);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_setfilter(IntPtr handle, ref BpfProgram program);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void pcap_freecode(ref BpfProgram program);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_next_ex(IntPtr handle, out IntPtr header, out IntPtr bytes);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr pcap_geterr(IntPtr handle);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int pcap_stats(IntPtr handle, IntPtr statistics);
    [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void pcap_close(IntPtr handle);
}
