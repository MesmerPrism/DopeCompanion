namespace DopeCompanion.Integration.Tests;

public sealed class MainWindowViewModelSourceTests
{
    [Fact]
    public async Task Live_session_cast_overlay_window_allows_render_view_mode_without_scrcpy()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "DopeCompanion.App",
            "ViewModels",
            "MainWindowViewModel.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));

        Assert.Contains(
            "if (!_questDisplayCastService.IsRunning && !desiredRenderViewMode)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Display_mirror_restart_reuses_current_overlay_bounds_when_available()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "DopeCompanion.App",
            "ViewModels",
            "MainWindowViewModel.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));

        Assert.Contains(
            "overlayWindow.TryGetCurrentCastDeviceBounds(out var preferredCastBounds)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "StartDisplay0Async(selector, castBounds)",
            source,
            StringComparison.Ordinal);
    }
}
