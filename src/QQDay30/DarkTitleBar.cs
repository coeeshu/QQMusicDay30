using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QQDay30;

internal static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void Apply(Window w)
    {
        var on = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(w).Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
    }
}
