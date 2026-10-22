using System.Windows.Media;
using QQDay30.Core;

namespace QQDay30;

/// <summary>Brand color of each service; the UI accent follows the service being shown.</summary>
internal static class Accents
{
    public static Color Of(Platform p) => p == Platform.QQ ? Color.FromRgb(0x31, 0xC2, 0x7C) : Color.FromRgb(0xEC, 0x41, 0x41);
}
