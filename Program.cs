using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace ExaltHUD;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    // Diagnostics go to a debugger only. The standalone app does not create packet/chat logs.
    public static void LogInfo(string area, string message) => Debug.WriteLine(area + ": " + message);
    public static void LogWarning(string area, string message) => Debug.WriteLine(area + ": " + message);
    public static void LogError(string area, string message) => Debug.WriteLine(area + ": " + message);
}
