using System.Windows;
using Size = System.Windows.Size;
using Point = System.Windows.Point;

namespace HarnessPortable.Windows.Services;

public enum TabDropZone { Center, Left, Right, Up, Down }

public static class TabDropGeometry
{
    public static Rect CardBounds(Rect pane, Size viewport, Size card)
    {
        var width = Math.Min(card.Width, Math.Max(0, viewport.Width - 16));
        var height = Math.Min(card.Height, Math.Max(0, viewport.Height - 16));
        var left = Math.Clamp(pane.Left + (pane.Width - width) / 2, 8, Math.Max(8, viewport.Width - width - 8));
        var top = Math.Clamp(pane.Top + (pane.Height - height) / 2, 8, Math.Max(8, viewport.Height - height - 8));
        return new Rect(left, top, width, height);
    }

    public static TabDropZone ZoneAt(Rect pane, Point point, IReadOnlyDictionary<TabDropZone, Rect> buttons)
    {
        foreach (var button in buttons)
            if (button.Value.Contains(point)) return button.Key;
        var edge = Math.Max(48, Math.Min(pane.Width, pane.Height) * 0.22);
        var horizontalEdge = Math.Min(edge, pane.Width / 3);
        var verticalEdge = Math.Min(edge, pane.Height / 3);
        if (point.X <= pane.Left + horizontalEdge) return TabDropZone.Left;
        if (point.X >= pane.Right - horizontalEdge) return TabDropZone.Right;
        if (point.Y <= pane.Top + verticalEdge) return TabDropZone.Up;
        if (point.Y >= pane.Bottom - verticalEdge) return TabDropZone.Down;
        return TabDropZone.Center;
    }
}
