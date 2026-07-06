using Avalonia.Media;

namespace BindProxy.Avalonia;

/// <summary>Small, hand-picked color set for the two theme variants, kept out of the FluentTheme resource
/// dictionary so exact tokens don't depend on the installed theme version.</summary>
internal sealed record Palette(
    IBrush CardBackground,
    IBrush CardBorder,
    IBrush MutedText,
    IBrush Accent,
    IBrush RunningBackground,
    IBrush RunningForeground,
    IBrush StoppedBackground,
    IBrush StoppedForeground,
    IBrush ErrorBackground,
    IBrush ErrorForeground)
{
    public static Palette Resolve(bool isDark) => isDark ? Dark : Light;

    private static readonly Palette Light = new(
        CardBackground: Brush("#FFFFFF"),
        CardBorder: Brush("#E2E8F0"),
        MutedText: Brush("#64748B"),
        Accent: Brush("#2563EB"),
        RunningBackground: Brush("#DCFCE7"),
        RunningForeground: Brush("#15803D"),
        StoppedBackground: Brush("#F1F5F9"),
        StoppedForeground: Brush("#475569"),
        ErrorBackground: Brush("#FEE2E2"),
        ErrorForeground: Brush("#B91C1C"));

    private static readonly Palette Dark = new(
        CardBackground: Brush("#1E293B"),
        CardBorder: Brush("#334155"),
        MutedText: Brush("#94A3B8"),
        Accent: Brush("#60A5FA"),
        RunningBackground: Brush("#14532D"),
        RunningForeground: Brush("#86EFAC"),
        StoppedBackground: Brush("#334155"),
        StoppedForeground: Brush("#CBD5E1"),
        ErrorBackground: Brush("#7F1D1D"),
        ErrorForeground: Brush("#FCA5A5"));

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
