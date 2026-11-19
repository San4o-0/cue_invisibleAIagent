using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace LiveTranscriber;

public static class CaptureProtection
{
    private const uint AffinityNone = 0x00;
    private const uint AffinityMonitor = 0x01;
    private const uint AffinityExcludeFromCapture = 0x11;
    private const int CbtHook = 5;
    private const int CbtActivate = 5;

    private static readonly HookProcedure Procedure = OnCbt;
    private static IntPtr hook;

    public static bool Enabled { get; private set; }

    public static void Install(bool enabled)
    {
        Enabled = enabled;
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnPopupOpened));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnPopupOpened));
        hook = SetWindowsHookEx(CbtHook, Procedure, IntPtr.Zero, GetCurrentThreadId());
    }

    public static bool Apply(Window window, bool enabled)
    {
        Enabled = enabled;
        return Protect(new WindowInteropHelper(window).Handle);
    }

    private static bool Protect(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }

        if (!Enabled)
        {
            return SetWindowDisplayAffinity(window, AffinityNone);
        }

        return SetWindowDisplayAffinity(window, AffinityExcludeFromCapture) || SetWindowDisplayAffinity(window, AffinityMonitor);
    }

    private static void OnPopupOpened(object sender, RoutedEventArgs e)
    {
        if (PresentationSource.FromVisual((Visual)sender) is HwndSource source)
        {
            Protect(source.Handle);
        }
    }

    private static IntPtr OnCbt(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == CbtActivate && Enabled)
        {
            Protect(wParam);
        }

        return CallNextHookEx(hook, code, wParam, lParam);
    }

    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, HookProcedure procedure, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
