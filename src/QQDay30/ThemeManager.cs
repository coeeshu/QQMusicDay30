using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace QQDay30;

public enum ThemeMode { Dark, Light, System }

/// <summary>
/// Owns the app-wide color brushes (referenced everywhere via DynamicResource) and swaps them per theme.
/// </summary>
public static class ThemeManager
{
    // key -> (dark, light)
    private static readonly Dictionary<string, (string Dark, string Light)> Palette = new()
    {
        ["Bg"] = ("#121212", "#F6F7F9"),
        ["Panel"] = ("#1A1A1A", "#ECEEF1"),
        ["Fg"] = ("#E6E6E6", "#1E1F22"),
        ["Sub"] = ("#8A8A8A", "#6A6E75"),
        ["Muted"] = ("#6E6E6E", "#9A9EA5"),
        ["Card"] = ("#2A2A2A", "#E2E4E8"),
        ["Card2"] = ("#262626", "#FFFFFF"),
        ["Placeholder"] = ("#666666", "#A3A7AE"),
        ["Overlay"] = ("#FFFFFF", "#000000"),
        ["ScrollThumb"] = ("#3A3A3A", "#C5C8CE"),
        ["Track"] = ("#3A3A3A", "#C9CCD2"),
        ["StatusBg"] = ("#161616", "#E6E8EC"),
        ["ToastBg"] = ("#2B2B2B", "#FFFFFF"),
        ["CaptionFg"] = ("#B0B0B0", "#55595F"),
        ["CaptionHover"] = ("#2E2E2E", "#DADDE2"),
        ["SwitchBg"] = ("#242424", "#E0E3E7"),
        ["SwitchThumb"] = ("#3A3A3A", "#FFFFFF"),
    };

    public static ThemeMode Mode { get; private set; } = ThemeMode.System;
    public static bool IsDark { get; private set; } = true;

    /// <summary>Raised after the colors changed (windows refresh their DWM frame).</summary>
    public static event Action? Changed;

    /// <summary>
    /// Optional hook an open window installs to wrap the color swap in a transition.
    /// Called with the swap action and the point (window coordinates) the transition should start from.
    /// </summary>
    public static Action<Action, Point?>? Transition { get; set; }

    public static void Init(ThemeMode mode)
    {
        Mode = mode;
        Swap(Resolve(mode));
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General || Mode != ThemeMode.System) return;
            Application.Current.Dispatcher.BeginInvoke(() => SetMode(ThemeMode.System, null));
        };
    }

    public static void SetMode(ThemeMode mode, Point? origin)
    {
        Mode = mode;
        var dark = Resolve(mode);
        if (dark == IsDark) return;
        if (Transition != null) Transition(() => Swap(dark), origin);
        else Swap(dark);
    }

    private static bool Resolve(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => SystemIsDark(),
    };

    private static bool SystemIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
    }

    private static void Swap(bool dark)
    {
        IsDark = dark;
        // application resources get frozen, so swap in new brushes; DynamicResource picks them up
        var res = Application.Current.Resources;
        foreach (var (key, (d, l)) in Palette)
            res[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? d : l));
        Changed?.Invoke();
    }
}
