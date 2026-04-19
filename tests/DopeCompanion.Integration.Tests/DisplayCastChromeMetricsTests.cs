using DopeCompanion.App;
using System.Windows;

namespace DopeCompanion.Integration.Tests;

public sealed class DisplayCastChromeMetricsTests
{
    [Fact]
    public void ExpandAndContractBounds_RoundTripViewportBounds()
    {
        var metrics = new DisplayCastChromeMetrics(
            LeftInset: 14,
            TopInset: 104,
            RightInset: 308,
            BottomInset: 14);
        var viewportBounds = new WindowLayoutBounds(320, 180, 1280, 720);

        var outerBounds = metrics.ExpandViewportBounds(viewportBounds);

        Assert.Equal(new WindowLayoutBounds(306, 76, 1602, 838), outerBounds);
        Assert.Equal(viewportBounds, metrics.ContractOuterBounds(outerBounds));
    }

    [Fact]
    public void FromViewportBounds_CapturesInsetsFromWindowSpace()
    {
        var metrics = DisplayCastChromeMetrics.FromViewportBounds(
            new Rect(14, 104, 1040, 620),
            new Size(1362, 738));

        Assert.Equal(14, metrics.LeftInset);
        Assert.Equal(104, metrics.TopInset);
        Assert.Equal(308, metrics.RightInset);
        Assert.Equal(14, metrics.BottomInset);
    }

    [Fact]
    public void MinimumOuterSize_IncludesReservedChromeInsets()
    {
        var metrics = new DisplayCastChromeMetrics(
            LeftInset: 14,
            TopInset: 104,
            RightInset: 308,
            BottomInset: 14);

        Assert.Equal(802, metrics.GetMinimumOuterWidth(480));
        Assert.Equal(388, metrics.GetMinimumOuterHeight(270));
    }
}
