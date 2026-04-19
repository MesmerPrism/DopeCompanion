using System.Windows;

namespace DopeCompanion.App;

internal sealed record DisplayCastChromeMetrics(
    int LeftInset,
    int TopInset,
    int RightInset,
    int BottomInset)
{
    public WindowLayoutBounds ExpandViewportBounds(WindowLayoutBounds viewportBounds)
        => new(
            viewportBounds.X - LeftInset,
            viewportBounds.Y - TopInset,
            Math.Max(1, viewportBounds.Width + LeftInset + RightInset),
            Math.Max(1, viewportBounds.Height + TopInset + BottomInset));

    public WindowLayoutBounds ContractOuterBounds(WindowLayoutBounds outerBounds)
        => new(
            outerBounds.X + LeftInset,
            outerBounds.Y + TopInset,
            Math.Max(1, outerBounds.Width - LeftInset - RightInset),
            Math.Max(1, outerBounds.Height - TopInset - BottomInset));

    public int GetMinimumOuterWidth(int minimumViewportWidth)
        => Math.Max(1, LeftInset + RightInset + minimumViewportWidth);

    public int GetMinimumOuterHeight(int minimumViewportHeight)
        => Math.Max(1, TopInset + BottomInset + minimumViewportHeight);

    public static DisplayCastChromeMetrics FromViewportBounds(Rect viewportBounds, Size windowSize)
    {
        var leftInset = Math.Max(0, (int)Math.Round(viewportBounds.Left));
        var topInset = Math.Max(0, (int)Math.Round(viewportBounds.Top));
        var rightInset = Math.Max(0, (int)Math.Round(windowSize.Width - viewportBounds.Right));
        var bottomInset = Math.Max(0, (int)Math.Round(windowSize.Height - viewportBounds.Bottom));

        return new DisplayCastChromeMetrics(leftInset, topInset, rightInset, bottomInset);
    }
}
