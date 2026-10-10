using System;
using ExaltHUD.Models;
using ExaltHUD.Tracking;

namespace ExaltHUD;

// Dashboard and overlay share snapshots, while keeping their own tabs and scroll positions.
internal static class DpsOverlayManager
{
    private static DpsOverlayForm[] views = Array.Empty<DpsOverlayForm>();
    public static void Attach(params DpsOverlayForm[] forms) => views = forms;
    public static void Detach() => views = Array.Empty<DpsOverlayForm>();

    private static void Apply(Action<DpsOverlayForm> action)
    {
        foreach (var view in views)
        {
            if (view.IsDisposed) continue;
            if (view.IsHandleCreated && view.InvokeRequired)
            {
                try { view.BeginInvoke((Action)(() => { if (!view.IsDisposed) action(view); })); }
                catch (InvalidOperationException) { }
            }
            else action(view);
        }
    }
    public static void UpdateSnapshot(DpsSnapshot snapshot) => Apply(view => view.UpdateData(snapshot));
    public static void UpdateMoonlightVillage(MoonlightVillageSnapshot snapshot) => Apply(view => view.UpdateMoonlightVillage(snapshot));
    public static void SwitchToMoonlightVillageTab() => Apply(view => view.SelectTab(3));
    public static void RequestRedraw() => Apply(view => view.Invalidate());
}
