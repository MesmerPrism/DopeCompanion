namespace DopeCompanion.Integration.Tests;

public sealed class DisplayCastOverlayWindowSourceTests
{
    [Fact]
    public async Task Render_view_window_enables_activation_and_taskbar_presence_when_created_as_standalone()
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
            "DisplayCastOverlayWindow.xaml.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));

        Assert.Contains("ShowActivated = createdForRenderViewMode;", source, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar = false;", source, StringComparison.Ordinal);
        Assert.Contains("public bool CreatedForRenderViewMode => _createdForRenderViewMode;", source, StringComparison.Ordinal);
    }
}
