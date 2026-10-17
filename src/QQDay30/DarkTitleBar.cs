using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QQDay30;

/// <summary>DWM tweaks for the borderless (WindowChrome) windows: themed frame, Win11 rounded corners, subtle border.</summary>
internal static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;

    public static void Apply(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        Set(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        void Frame()
        {
            Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ThemeManager.IsDark ? 1 : 0);
            Set(hwnd, DWMWA_BORDER_COLOR, ThemeManager.IsDark ? 0x00333333 : 0x00D6D3D0); // COLORREF 0x00BBGGRR
        }
        Frame();
        ThemeManager.Changed += Frame;
        w.Closed += (_, _) => ThemeManager.Changed -= Frame;

        // WindowChrome lets a maximized window spill past the screen edge; pad it back in
        w.StateChanged += (_, _) => Pad(w);
        Pad(w);
    }

    private static void Pad(Window w)
    {
        if (w.Content is FrameworkElement root)
        {
            var b = SystemParameters.WindowResizeBorderThickness;
            root.Margin = w.WindowState == WindowState.Maximized
                ? new Thickness(b.Left + 4, b.Top + 4, b.Right + 4, b.Bottom + 4)
                : new Thickness(0);
        }
    }

    private static void Set(IntPtr hwnd, int attr, int value) =>
        DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int));
}
