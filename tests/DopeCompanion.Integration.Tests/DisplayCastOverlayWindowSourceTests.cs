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

    [Fact]
    public async Task Stop_button_detaches_and_hides_overlay_before_running_stop_command()
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

        var xaml = await File.ReadAllTextAsync(Path.GetFullPath(xamlPath));
        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));
        var normalizedXaml = xaml.Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("x:Name=\"StopButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"StopButton\"\n                                Content=\"Stop\"\n                                Style=\"{StaticResource OverlayHeaderButtonStyle}\"\n                                PreviewMouseLeftButtonDown=\"OnInteractiveControlPreviewMouseLeftButtonDown\"",
            normalizedXaml,
            StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment(ref _foregroundActivationGeneration);", source, StringComparison.Ordinal);
        Assert.Contains("ClearOwner();", source, StringComparison.Ordinal);
        Assert.Contains("Hide();", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private void OnStopWindowClicked(object sender, RoutedEventArgs e)\n    {\n        ActivateOverlayWindow();",
            normalizedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cast_overlay_dismisses_click_to_do_when_loading_showing_and_activating()
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

        Assert.Contains("CompanionWindowActivationHelper.DismissKnownBlockingShellOverlays();", source, StringComparison.Ordinal);
        Assert.Contains("private void OnLoaded(object sender, RoutedEventArgs e)", source, StringComparison.Ordinal);
        Assert.Contains("private void ActivateOverlayWindow()", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Minimize_button_respects_minimized_state_until_an_explicit_restore_is_requested()
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
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("public void RefreshFromCastWindow(bool requestRestore = false)", source, StringComparison.Ordinal);
        Assert.Contains("_restoreFromMinimizedStateRequested = true;", source, StringComparison.Ordinal);
        Assert.Contains("if (_castService.IsWindowMinimized)", source, StringComparison.Ordinal);
        Assert.Contains("_castService.TryRestoreWindow()", source, StringComparison.Ordinal);
        Assert.Contains(
            "if (WindowState == WindowState.Minimized)\n        {\n            if (!restoreRequested)\n            {\n                return;\n            }\n",
            normalizedSource,
            StringComparison.Ordinal);
    }
}
