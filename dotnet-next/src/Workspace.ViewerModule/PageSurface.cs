using System.Windows;
using System.Windows.Controls;
namespace Workspace.ViewerModule;

// Only the viewport's page controls exist. The extent is geometry, not a huge bitmap.
internal sealed class PageSurface : Panel
{
    public double ExtentWidth { get; set; }
    public double ExtentHeight { get; set; }
    public Dictionary<UIElement, Rect> Positions { get; } = new();
    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(Positions.TryGetValue(child, out var rect) ? rect.Size : new Size());
        return new Size(ExtentWidth, ExtentHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
            if (Positions.TryGetValue(child, out var rect)) child.Arrange(rect);
        return finalSize;
    }
}
