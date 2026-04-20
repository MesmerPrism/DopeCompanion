namespace DopeCompanion.Integration.Tests;

public sealed class DisplayCastOverlayWindowTests
{
    [Fact]
    public async Task Display_cast_overlay_window_declares_render_view_main_surface_selector_sidebar_tweak_list_apply_button_focus_layer_selector_and_audio_trigger_toggle()
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
            "DisplayCastOverlayWindow.xaml");

        var xaml = await File.ReadAllTextAsync(Path.GetFullPath(xamlPath));

        Assert.Contains("Render View", xaml, StringComparison.Ordinal);
        Assert.Contains("Main view", xaml, StringComparison.Ordinal);
        Assert.Contains("LiveSessionCastSurfaceOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectLiveSessionCastSurfaceModeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("IsLiveSessionCastRenderViewMode", xaml, StringComparison.Ordinal);
        Assert.Contains("LiveSessionCastFocusedLayerPreviewSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("LiveSessionCastFocusedLayerPreviewImage", xaml, StringComparison.Ordinal);
        Assert.Contains("Live tweak values", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding LiveSessionCastSidebarSettings}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Layer focus", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding LiveSessionCastFocusLayerOptions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectLiveSessionCastFocusLayerCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Audio trigger", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleLiveSessionCastAudioTriggerCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ApplyLiveSessionRuntimeConfigCommand}\"", xaml, StringComparison.Ordinal);
    }
}
