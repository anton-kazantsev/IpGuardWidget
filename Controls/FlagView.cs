using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace IpGuard.Controls;

/// <summary>Draws a small country flag from its ISO code; unknown codes become a neutral badge.</summary>
public sealed class FlagView : FrameworkElement
{
    public static readonly DependencyProperty CountryCodeProperty = DependencyProperty.Register(
        nameof(CountryCode),
        typeof(string),
        typeof(FlagView),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public string CountryCode
    {
        get => (string)GetValue(CountryCodeProperty);
        set => SetValue(CountryCodeProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0.5 || height <= 0.5)
        {
            return;
        }

        var bounds = new Rect(0, 0, width, height);
        const double radius = 5;
        dc.PushClip(new RectangleGeometry(bounds, radius, radius));

        var code = (CountryCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length > 0 && Flags.TryGetValue(code, out var draw))
        {
            draw(dc, bounds);
        }
        else
        {
            DrawFallback(dc, bounds, code);
        }

        dc.Pop();

        var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), 1);
        dc.DrawRoundedRectangle(null, borderPen,
            new Rect(0.5, 0.5, Math.Max(0, width - 1), Math.Max(0, height - 1)),
            radius - 0.5, radius - 0.5);
    }

    private void DrawFallback(DrawingContext dc, Rect rect, string code)
    {
        dc.DrawRectangle(Brush("#0FFFFFFF"), null, rect);
        if (code.Length == 0)
        {
            return;
        }

        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var text = new FormattedText(
            code,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            10,
            Brush("#A3A3AC"),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(text, new Point(
            rect.X + (rect.Width - text.Width) / 2,
            rect.Y + (rect.Height - text.Height) / 2));
    }

    private static Action<DrawingContext, Rect> Horizontal(params string[] colors) => (dc, rect) =>
    {
        var stripe = rect.Height / colors.Length;
        for (var i = 0; i < colors.Length; i++)
        {
            dc.DrawRectangle(Brush(colors[i]), null,
                new Rect(rect.X, rect.Y + i * stripe, rect.Width, stripe + 0.6));
        }
    };

    private static Action<DrawingContext, Rect> Vertical(params string[] colors) => (dc, rect) =>
    {
        var stripe = rect.Width / colors.Length;
        for (var i = 0; i < colors.Length; i++)
        {
            dc.DrawRectangle(Brush(colors[i]), null,
                new Rect(rect.X + i * stripe, rect.Y, stripe + 0.6, rect.Height));
        }
    };

    private static Action<DrawingContext, Rect> Nordic(string background, params (string Color, double Thickness)[] layers)
        => (dc, rect) =>
        {
            dc.DrawRectangle(Brush(background), null, rect);
            var crossX = rect.X + rect.Width * 0.36;
            var crossY = rect.Y + rect.Height * 0.5;

            foreach (var (color, thickness) in layers)
            {
                var size = rect.Height * thickness;
                var brush = Brush(color);
                dc.DrawRectangle(brush, null, new Rect(rect.X, crossY - size / 2, rect.Width, size));
                dc.DrawRectangle(brush, null, new Rect(crossX - size / 2, rect.Y, size, rect.Height));
            }
        };

    private static void Japan(DrawingContext dc, Rect rect)
    {
        dc.DrawRectangle(Brush("#FFFFFF"), null, rect);
        var radius = rect.Height * 0.29;
        dc.DrawEllipse(Brush("#BC002D"), null,
            new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), radius, radius);
    }

    private static void Switzerland(DrawingContext dc, Rect rect)
    {
        dc.DrawRectangle(Brush("#D52B1E"), null, rect);
        var arm = rect.Height * 0.62;
        var thickness = rect.Height * 0.16;
        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        dc.DrawRectangle(Brush("#FFFFFF"), null, new Rect(cx - arm / 2, cy - thickness / 2, arm, thickness));
        dc.DrawRectangle(Brush("#FFFFFF"), null, new Rect(cx - thickness / 2, cy - arm / 2, thickness, arm));
    }

    private static void UnitedStates(DrawingContext dc, Rect rect)
    {
        for (var i = 0; i < 13; i++)
        {
            var color = i % 2 == 0 ? "#B22234" : "#FFFFFF";
            var stripe = rect.Height / 13;
            dc.DrawRectangle(Brush(color), null,
                new Rect(rect.X, rect.Y + i * stripe, rect.Width, stripe + 0.6));
        }

        dc.DrawRectangle(Brush("#3C3B6E"), null,
            new Rect(rect.X, rect.Y, rect.Width * 0.42, rect.Height * 7 / 13));
    }

    private static void UnitedKingdom(DrawingContext dc, Rect rect)
    {
        dc.DrawRectangle(Brush("#012169"), null, rect);
        var white = Brush("#FFFFFF");
        var red = Brush("#C8102E");

        var whitePen = new Pen(white, rect.Height * 0.20);
        var redPen = new Pen(red, rect.Height * 0.08);

        dc.DrawLine(whitePen, rect.TopLeft, rect.BottomRight);
        dc.DrawLine(whitePen, rect.TopRight, rect.BottomLeft);
        dc.DrawLine(redPen, rect.TopLeft, rect.BottomRight);
        dc.DrawLine(redPen, rect.TopRight, rect.BottomLeft);

        dc.DrawRectangle(white, null,
            new Rect(rect.X, rect.Y + rect.Height * 0.32, rect.Width, rect.Height * 0.36));
        dc.DrawRectangle(white, null,
            new Rect(rect.X + rect.Width * 0.36, rect.Y, rect.Height * 0.28, rect.Height));
        dc.DrawRectangle(red, null,
            new Rect(rect.X, rect.Y + rect.Height * 0.40, rect.Width, rect.Height * 0.20));
        dc.DrawRectangle(red, null,
            new Rect(rect.X + rect.Width * 0.43, rect.Y, rect.Height * 0.14, rect.Height));
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    private static readonly Dictionary<string, Action<DrawingContext, Rect>> Flags =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // horizontal tricolors / bicolors
            ["NL"] = Horizontal("#AE1C28", "#F5F5F5", "#21468B"),
            ["RU"] = Horizontal("#FFFFFF", "#0039A6", "#D52B1E"),
            ["DE"] = Horizontal("#000000", "#DD0000", "#FFCE00"),
            ["AT"] = Horizontal("#ED2939", "#FFFFFF", "#ED2939"),
            ["HU"] = Horizontal("#CE2939", "#FFFFFF", "#477050"),
            ["BG"] = Horizontal("#FFFFFF", "#00966E", "#D62612"),
            ["EE"] = Horizontal("#0072CE", "#000000", "#FFFFFF"),
            ["LT"] = Horizontal("#FDB913", "#006A44", "#C1272D"),
            ["LV"] = Horizontal("#9E3039", "#FFFFFF", "#9E3039"),
            ["RS"] = Horizontal("#C6363C", "#0C4076", "#FFFFFF"),
            ["SK"] = Horizontal("#FFFFFF", "#0B4EA2", "#EE1C25"),
            ["SI"] = Horizontal("#FFFFFF", "#005DA4", "#ED1C24"),
            ["HR"] = Horizontal("#FF0000", "#FFFFFF", "#171796"),
            ["RO"] = Horizontal("#002B7F", "#FCD116", "#CE1126"),
            ["MD"] = Horizontal("#003DA5", "#FFD200", "#CC092F"),
            ["AM"] = Horizontal("#D90012", "#0033A0", "#F2A800"),
            ["CO"] = Horizontal("#FCD116", "#003893", "#CE1126"),
            ["VE"] = Horizontal("#FFCC00", "#00247D", "#CF142B"),
            ["EC"] = Horizontal("#FFDD00", "#034EA2", "#ED1C24"),
            ["EG"] = Horizontal("#CE1126", "#FFFFFF", "#000000"),
            ["IN"] = Horizontal("#FF9933", "#FFFFFF", "#138808"),
            ["TH"] = Horizontal("#A51931", "#F4F5F8", "#2D2A4A", "#F4F5F8", "#A51931"),
            ["PL"] = Horizontal("#FFFFFF", "#DC143C"),
            ["ID"] = Horizontal("#CE1126", "#FFFFFF"),
            ["MC"] = Horizontal("#CE1126", "#FFFFFF"),
            ["SG"] = Horizontal("#CE1126", "#FFFFFF"),
            ["UA"] = Horizontal("#0057B7", "#FFD700"),
            ["BY"] = Horizontal("#C8313E", "#4AA657"),
            ["KZ"] = Horizontal("#00AFCA", "#FEC50C"),
            ["GE"] = Horizontal("#FFFFFF", "#FF0000"),
            ["IL"] = Horizontal("#0038B8", "#FFFFFF", "#0038B8"),

            // vertical tricolors
            ["FR"] = Vertical("#002395", "#FFFFFF", "#ED2939"),
            ["IT"] = Vertical("#009246", "#FFFFFF", "#CE2B37"),
            ["IE"] = Vertical("#169B62", "#FFFFFF", "#FF883E"),
            ["BE"] = Vertical("#000000", "#FAE042", "#ED2939"),
            ["TD"] = Vertical("#002664", "#FECB00", "#C60C30"),
            ["GN"] = Vertical("#CE1126", "#FCD116", "#009460"),
            ["ML"] = Vertical("#14B53A", "#FCD116", "#CE1126"),
            ["SN"] = Vertical("#00853F", "#FDEF42", "#E31B23"),
            ["CI"] = Vertical("#F77F00", "#FFFFFF", "#009E60"),
            ["NG"] = Vertical("#008751", "#FFFFFF", "#008751"),
            ["PE"] = Vertical("#D91023", "#FFFFFF", "#D91023"),
            ["MX"] = Vertical("#006847", "#FFFFFF", "#CE1126"),
            ["CA"] = Vertical("#D80621", "#FFFFFF", "#D80621"),

            // Nordic crosses
            ["SE"] = Nordic("#006AA7", ("#FECC00", 0.16)),
            ["NO"] = Nordic("#BA0C2F", ("#FFFFFF", 0.22), ("#00205B", 0.12)),
            ["DK"] = Nordic("#C8102E", ("#FFFFFF", 0.16)),
            ["FI"] = Nordic("#FFFFFF", ("#002F6C", 0.16)),
            ["IS"] = Nordic("#02529C", ("#FFFFFF", 0.22), ("#DC1E35", 0.12)),

            // special
            ["JP"] = Japan,
            ["CH"] = Switzerland,
            ["US"] = UnitedStates,
            ["GB"] = UnitedKingdom
        };
}
