namespace DopeCompanion.Integration.Tests;

public sealed class DisplayCastOverlayWindowTests
{
    [Fact]
    public async Task Display_cast_overlay_window_declares_sidebar_tweak_list_apply_button_focus_layer_selector_and_audio_trigger_toggle()
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
