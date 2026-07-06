using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BindProxy.Core.Nics;
using Path = Avalonia.Controls.Shapes.Path;

namespace BindProxy.Avalonia;

/// <summary>
/// Small, dependency-free glyphs used to tell connection kinds apart at a glance. Built from primitive
/// shapes rather than an icon font/library so the app stays self-contained and AOT-friendly.
/// </summary>
internal static class ConnectionIcon
{
    public static Control Create(NicKind kind, IBrush stroke, double size = 26)
    {
        Control glyph = kind switch
        {
            NicKind.Ethernet => CreateEthernet(stroke),
            NicKind.Wireless => CreateWireless(stroke),
            _ => CreateOther(stroke),
        };

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = glyph,
        };
    }

    private static Control CreateEthernet(IBrush stroke)
    {
        // A simplified network-plug silhouette: a body with a small tab, plus two contact pins.
        return new Path
        {
            // A network jack: a rounded body with two contact pins protruding from the bottom edge.
            Data = Geometry.Parse(
                "M6,4 L18,4 A2,2 0 0 1 20,6 L20,12 A2,2 0 0 1 18,14 " +
                "L6,14 A2,2 0 0 1 4,12 L4,6 A2,2 0 0 1 6,4 Z " +
                "M9,14 L9,17.5 L11,17.5 L11,14 Z " +
                "M13,14 L13,17.5 L15,17.5 L15,14 Z"),
            Stroke = stroke,
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
        };
    }

    private static Control CreateWireless(IBrush stroke)
    {
        // The universal three-wave-plus-dot Wi-Fi glyph, drawn with beziers instead of arcs.
        return new Path
        {
            Data = Geometry.Parse(
                "M12,18.5 A1.6,1.6 0 1 1 11.99,18.5 Z " +
                "M8.1,15.1 C9.2,14.1 10.55,13.6 12,13.6 C13.45,13.6 14.8,14.1 15.9,15.1 " +
                "M5.1,12 C7.05,10.15 9.45,9.2 12,9.2 C14.55,9.2 16.95,10.15 18.9,12 " +
                "M2.1,8.9 C4.85,6.25 8.3,4.8 12,4.8 C15.7,4.8 19.15,6.25 21.9,8.9"),
            Stroke = stroke,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
        };
    }

    private static Control CreateOther(IBrush stroke)
    {
        // A generic "link" glyph (two joined nodes) for connection types that aren't wired or Wi-Fi.
        return new Path
        {
            Data = Geometry.Parse(
                "M7.5,16.5 A4,4 0 0 1 7.5,7.5 L10,7.5 " +
                "M16.5,7.5 A4,4 0 0 1 16.5,16.5 L14,16.5 " +
                "M8.5,12 L15.5,12"),
            Stroke = stroke,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
        };
    }
}
