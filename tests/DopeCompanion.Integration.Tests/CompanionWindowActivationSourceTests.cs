namespace DopeCompanion.Integration.Tests;

public sealed class CompanionWindowActivationSourceTests
{
    [Fact]
    public async Task Main_window_view_model_queues_deferred_companion_activation_retries_without_global_focus_redirection()
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

        Assert.Contains("RequestPreferredCompanionWindowActivation()", source, StringComparison.Ordinal);
        Assert.Contains("SchedulePreferredCompanionWindowActivation(generation, TimeSpan.FromMilliseconds(120));", source, StringComparison.Ordinal);
        Assert.Contains("SchedulePreferredCompanionWindowActivation(generation, TimeSpan.FromMilliseconds(360));", source, StringComparison.Ordinal);
        Assert.DoesNotContain("internal void NotifyCompanionWindowActivated(Window activatedWindow)", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activation_helper_dismisses_click_to_do_before_promoting_the_companion_window()
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
            "CompanionWindowActivationHelper.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));

        Assert.Contains("private const string ClickToDoWindowTitle = \"Click to Do\";", source, StringComparison.Ordinal);
        Assert.Contains("DismissKnownBlockingShellOverlays();", source, StringComparison.Ordinal);
        Assert.Contains("TryCloseVisibleTopLevelWindowByExactTitle(ClickToDoWindowTitle);", source, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.EnumWindows", source, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.PostMessage(blockingWindowHandle, NativeMethods.WmClose, 0, 0);", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Main_and_live_session_windows_do_not_force_focus_back_to_the_view_model()
    {
        var mainWindowSourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "DopeCompanion.App",
            "MainWindow.xaml.cs");
        var liveSessionSourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "DopeCompanion.App",
            "LiveSessionWindow.xaml.cs");

        var mainWindowSource = await File.ReadAllTextAsync(Path.GetFullPath(mainWindowSourcePath));
        var liveSessionSource = await File.ReadAllTextAsync(Path.GetFullPath(liveSessionSourcePath));

        Assert.DoesNotContain("Activated += OnActivated;", mainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Deactivated += OnDeactivated;", mainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CompanionWindowActivationHelper.HoldWindowAboveNoActivateOverlays(this);", mainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CompanionWindowActivationHelper.ReleaseWindowAboveNoActivateOverlays(this);", mainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.NotifyCompanionWindowActivated(this);", mainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Activated += OnActivated;", liveSessionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Deactivated += OnDeactivated;", liveSessionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CompanionWindowActivationHelper.HoldWindowAboveNoActivateOverlays(this);", liveSessionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CompanionWindowActivationHelper.ReleaseWindowAboveNoActivateOverlays(this);", liveSessionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.NotifyCompanionWindowActivated(this);", liveSessionSource, StringComparison.Ordinal);
    }
}
