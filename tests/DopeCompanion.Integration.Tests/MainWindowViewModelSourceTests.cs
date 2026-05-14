namespace DopeCompanion.Integration.Tests;

public sealed class MainWindowViewModelSourceTests
{
    [Fact]
    public async Task Live_session_cast_overlay_window_allows_render_view_mode_without_scrcpy()
    {
        var sourcePath = ResolveRepoFile("src", "DopeCompanion.App", "ViewModels", "MainWindowViewModel.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));

        Assert.Contains(
            "if (!_questDisplayCastService.IsRunning && !desiredRenderViewMode)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Display_mirror_restart_reuses_current_overlay_bounds_when_available()
    {
        var sourcePath = ResolveRepoFile("src", "DopeCompanion.App", "ViewModels", "MainWindowViewModel.cs");

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

    [Fact]
    public async Task Live_session_cast_declares_media_projection_as_a_direct_frame_surface()
    {
        var sourcePath = ResolveRepoFile("src", "DopeCompanion.App", "ViewModels", "MainWindowViewModel.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));

        Assert.Contains("LiveSessionCastSurfaceModeMediaProjection", source, StringComparison.Ordinal);
        Assert.Contains("MediaProjectionStreamService", source, StringComparison.Ordinal);
        Assert.Contains("IsLiveSessionCastDirectFrameMode", source, StringComparison.Ordinal);
        Assert.Contains("Start MediaProjection", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cast_overlay_refresh_requests_restore_when_the_window_is_reopened_from_the_companion()
    {
        var sourcePath = ResolveRepoFile("src", "DopeCompanion.App", "ViewModels", "MainWindowViewModel.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));

        Assert.Contains(
            "_liveSessionCastOverlayWindow.RefreshFromCastWindow(requestRestore: true);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "window.RefreshFromCastWindow(requestRestore: true);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Live_session_cast_sync_does_not_repeatedly_stop_or_restart_preview_from_state_changed_feedback()
    {
        var sourcePath = ResolveRepoFile("src", "DopeCompanion.App", "ViewModels", "MainWindowViewModel.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));

        Assert.Contains("if (_liveSessionCastStopInProgress)", source, StringComparison.Ordinal);
        Assert.Contains("CloseLiveSessionCastOverlayWindow(requestActivation: false);", source, StringComparison.Ordinal);
        Assert.Contains("if (!previewRunning)", source, StringComparison.Ordinal);
        Assert.Contains("_ = EnsureLiveSessionCastFocusedLayerPreviewAsync();", source, StringComparison.Ordinal);
        Assert.Contains("if (previewRunning)", source, StringComparison.Ordinal);
        Assert.Contains("_ = StopLiveSessionCastFocusedLayerPreviewAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stop_live_session_cast_marks_stop_in_progress_before_stopping_services()
    {
        var sourcePath = ResolveRepoFile("src", "DopeCompanion.App", "ViewModels", "MainWindowViewModel.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));

        Assert.Contains("_liveSessionCastStopInProgress = true;", source, StringComparison.Ordinal);
        Assert.Contains("CloseLiveSessionCastOverlayWindow(requestActivation: false);", source, StringComparison.Ordinal);
        Assert.Contains("var castOutcome = await _questDisplayCastService.StopAsync().ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("var previewOutcome = await _focusedLayerPreviewService.StopAsync().ConfigureAwait(false);", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Focused_layer_preview_updates_do_not_run_full_surface_sync_or_relayout_the_render_view_shell_each_frame()
    {
        var sourcePath = ResolveRepoFile("src", "DopeCompanion.App", "ViewModels", "MainWindowViewModel.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));
        var handlerStart = source.IndexOf(
            "private void OnFocusedLayerPreviewStateChanged(object? sender, EventArgs e)",
            StringComparison.Ordinal);
        var nextMethodStart = source.IndexOf(
            "private bool NeedsRenderViewSurfaceStateRefresh()",
            handlerStart,
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && nextMethodStart > handlerStart, "Could not isolate OnFocusedLayerPreviewStateChanged in MainWindowViewModel.cs.");
        var handlerSource = source[handlerStart..nextMethodStart];

        Assert.Contains("private void OnFocusedLayerPreviewStateChanged(object? sender, EventArgs e)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_dispatcher.InvokeAsync(SyncLiveSessionCastSurface);", handlerSource, StringComparison.Ordinal);
        Assert.Contains("RefreshLiveSessionCastFocusedLayerPreviewState();", handlerSource, StringComparison.Ordinal);
        Assert.Contains("NeedsRenderViewSurfaceStateRefresh()", handlerSource, StringComparison.Ordinal);
        Assert.Contains("BuildLiveSessionCastFocusedLayerPreviewSummary()", source, StringComparison.Ordinal);
        Assert.Contains("BuildLiveSessionCastFocusedLayerPreviewDetail()", source, StringComparison.Ordinal);
    }

    private static string ResolveRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not resolve repo file {Path.Combine(segments)} from {AppContext.BaseDirectory}.");
    }
}
