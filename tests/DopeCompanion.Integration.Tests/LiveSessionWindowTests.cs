namespace DopeCompanion.Integration.Tests;

public sealed class LiveSessionWindowTests
{
    [Fact]
    public async Task Live_session_window_declares_render_view_main_surface_selector_and_dynamic_cast_actions()
    {
        var xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "DopeCompanion.App",
            "LiveSessionWindow.xaml");

        var xaml = await File.ReadAllTextAsync(Path.GetFullPath(xamlPath));

        Assert.Contains("Main view", xaml, StringComparison.Ordinal);
        Assert.Contains("LiveSessionCastSurfaceOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectLiveSessionCastSurfaceModeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("LiveSessionCastStartActionLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("LiveSessionCastRestartActionLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("LiveSessionCastStopActionLabel", xaml, StringComparison.Ordinal);
    }
}
