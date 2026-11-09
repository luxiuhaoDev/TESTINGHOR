using System;
using System.Drawing;

namespace McPetOverlay;

internal static class EpsilonTheme
{
    internal static Color Accent(AppConfig config) => Opaque(config.UiAccentColorArgb, Color.FromArgb(208, 188, 255));
    internal static Color Background(AppConfig config) => Opaque(config.UiBackgroundColorArgb, Color.FromArgb(20, 18, 24));
    internal static Color Panel(AppConfig config) => Opaque(config.UiPanelColorArgb, Color.FromArgb(33, 31, 38));
    internal static Color Text(AppConfig config) => Opaque(config.UiTextColorArgb, Color.FromArgb(230, 224, 233));
    internal static Color TextSecondary(AppConfig config) => Mix(Text(config), Background(config), 0.26f);
    internal static Color TextMuted(AppConfig config) => Mix(Text(config), Background(config), 0.52f);
    internal static Color PanelHigh(AppConfig config) => Mix(Panel(config), Text(config), 0.08f);
    internal static Color PanelHighest(AppConfig config) => Mix(Panel(config), Text(config), 0.16f);
    internal static Color Secondary(AppConfig config) => Mix(Panel(config), Accent(config), 0.28f);
    internal static Color Outline(AppConfig config) => Mix(Text(config), Background(config), 0.45f);
    internal static Color OnAccent(AppConfig config) => Contrast(Accent(config));
    internal static float Scale(AppConfig config) => Math.Clamp(config.UiScalePercent / 100f, 0.6f, 1.8f);

    internal static Color Mix(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            255,
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static Color Opaque(int argb, Color fallback)
    {
        Color value = Color.FromArgb(argb);
        return value.A == 0 ? fallback : Color.FromArgb(255, value.R, value.G, value.B);
    }

    private static Color Contrast(Color color)
    {
        double luminance = (0.299d * color.R + 0.587d * color.G + 0.114d * color.B) / 255d;
        return luminance > 0.56d ? Color.FromArgb(28, 24, 34) : Color.White;
    }
}
