using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IpGuard.Controls;

/// <summary>Border that clips its children to the rounded corner radius.</summary>
public sealed class RoundedClipBorder : Border
{
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = base.ArrangeOverride(arrangeSize);
        var radius = CornerRadius.TopLeft;
        if (radius > 0 && size.Width > 0 && size.Height > 0)
        {
            Clip = new RectangleGeometry(new Rect(0, 0, size.Width, size.Height), radius, radius);
        }

        return size;
    }
}
