using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DopeCompanion.App.ViewModels;

namespace DopeCompanion.App;

internal partial class DisplayCastOverlayWindow : Window
{
    private const int MinimumViewportWidth = 480;
    private const int MinimumViewportHeight = 270;
    private const int WmNcHitTest = 0x0084;
    private const int GwlpHwndParent = -8;
    private static readonly IntPtr HtTransparent = new(-1);

    private readonly QuestDisplayCastService _castService;
    private readonly ICommand _stopCastCommand;
    private readonly DispatcherTimer _followTimer;
    private readonly double _baseMinWidth;
    private readonly double _baseMinHeight;
    private readonly bool _createdForRenderViewMode;
    private bool _syncingFromCastWindow;
    private bool _applyingOverlayBounds;
    private bool _isFullscreen;
    private bool _isResizingWithGrip;
    private bool _refreshChromeAfterReload;
    private bool _chromeRefreshQueued;
    private int _foregroundActivationGeneration;
    private WindowLayoutBounds? _restoreBounds;
    private nint _windowHandle;
    private nint _ownedCastHandle;

    internal DisplayCastOverlayWindow(
        QuestDisplayCastService castService,
        ICommand stopCastCommand,
        bool createdForRenderViewMode,
        object? dataContext = null,
        WindowLayoutBounds? initialOverlayBounds = null)
    {
        _castService = castService ?? throw new ArgumentNullException(nameof(castService));
        _stopCastCommand = stopCastCommand ?? throw new ArgumentNullException(nameof(stopCastCommand));
        _createdForRenderViewMode = createdForRenderViewMode;

        InitializeComponent();
        WindowThemeHelper.Attach(this);
        DataContext = dataContext;
        ShowActivated = createdForRenderViewMode;
        ShowInTaskbar = false;

        _baseMinWidth = MinWidth;
        _baseMinHeight = MinHeight;
        if (initialOverlayBounds is { } bounds)
        {
            Left = bounds.X;
            Top = bounds.Y;
            Width = Math.Max(MinWidth, bounds.Width);
            Height = Math.Max(MinHeight, bounds.Height);
        }
        else
        {
            Left = -10000;
            Top = -10000;
        }

        WindowSizeText.Text = "Waiting for cast";

        _followTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background,
            OnFollowTimerTick,
            Dispatcher);

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        LocationChanged += OnOverlayBoundsChanged;
        SizeChanged += OnOverlayBoundsChanged;
        Closed += OnClosed;
    }

    public void RefreshFromCastWindow()
        => SyncToCastWindow();

    public bool CreatedForRenderViewMode => _createdForRenderViewMode;

    public WindowLayoutBounds CurrentOverlayBounds => GetCurrentOverlayBounds();

    public bool TryGetCurrentCastDeviceBounds(out WindowLayoutBounds bounds)
    {
        if (!TryGetChromeMetrics(out var metrics))
        {
            bounds = new WindowLayoutBounds(0, 0, 0, 0);
            return false;
        }

        var castDipBounds = metrics.ContractOuterBounds(GetCurrentOverlayBounds());
        bounds = ConvertDipBoundsToDevicePixels(castDipBounds);
        return true;
    }

    private bool IsRenderViewMode
        => DataContext is MainWindowViewModel viewModel && viewModel.IsLiveSessionCastRenderViewMode;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(_windowHandle) is { } source)
        {
            source.AddHook(WndProc);
        }

        TrySetOwner();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshMinimumWindowSize();
        UpdateWindowState();
        SyncToCastWindow();
        _followTimer.Start();
        CompanionWindowActivationHelper.DismissKnownBlockingShellOverlays();
        if (IsRenderViewMode || _createdForRenderViewMode)
        {
            QueueOverlayActivation();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _foregroundActivationGeneration);
        _followTimer.Stop();
        SourceInitialized -= OnSourceInitialized;
        Loaded -= OnLoaded;
        LocationChanged -= OnOverlayBoundsChanged;
        SizeChanged -= OnOverlayBoundsChanged;
        Closed -= OnClosed;
    }

    private void OnFollowTimerTick(object? sender, EventArgs e)
        => SyncToCastWindow();

    private void SyncToCastWindow()
    {
        if (!IsLoaded || _isResizingWithGrip)
        {
            return;
        }

        if (IsRenderViewMode)
        {
            SyncToRenderViewWindow();
            return;
        }

        if (_castService.IsRestarting)
        {
            _refreshChromeAfterReload = true;
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (!_castService.IsRunning ||
            !_castService.TryGetWindowHandle(out var castHandle) ||
            castHandle == 0 ||
            !_castService.TryGetWindowBounds(out var castBounds))
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        var ownerChanged = castHandle != _ownedCastHandle;
        TrySetOwner(castHandle);
        RefreshMinimumWindowSize();
        var overlayBounds = ExpandCastBoundsToOverlayBounds(ConvertDevicePixelsToDipBounds(castBounds));

        _syncingFromCastWindow = true;
        try
        {
            Left = overlayBounds.X;
            Top = overlayBounds.Y;
            Width = Math.Max(MinWidth, overlayBounds.Width);
            Height = Math.Max(MinHeight, overlayBounds.Height);
        }
        finally
        {
            _syncingFromCastWindow = false;
        }

        UpdateWindowState(ConvertDipBoundsToDevicePixels(overlayBounds));
        if (!IsVisible)
        {
            CompanionWindowActivationHelper.DismissKnownBlockingShellOverlays();
            Show();
        }

        if (ownerChanged || _refreshChromeAfterReload)
        {
            _refreshChromeAfterReload = false;
            QueueChromeRefresh();
        }
    }

    private void SyncToRenderViewWindow()
    {
        ClearOwner();
        RefreshMinimumWindowSize();
        var restoredFromMinimized = false;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            restoredFromMinimized = true;
        }

        var recenteredToVisibleDesktop = EnsureStandaloneBoundsVisible();
        var shouldActivate = restoredFromMinimized || recenteredToVisibleDesktop;

        if (!IsVisible)
        {
            shouldActivate = true;
            Show();
            QueueChromeRefresh();
        }

        UpdateWindowState(ConvertDipBoundsToDevicePixels(GetCurrentOverlayBounds()));
        if (shouldActivate)
        {
            QueueOverlayActivation();
        }
    }

    private bool EnsureStandaloneBoundsVisible()
    {
        var currentBounds = new Rect(Left, Top, Math.Max(MinWidth, Width), Math.Max(MinHeight, Height));
        var virtualDesktopBounds = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        if (Left > -9000 &&
            Top > -9000 &&
            Width >= MinWidth &&
            Height >= MinHeight &&
            currentBounds.IntersectsWith(virtualDesktopBounds))
        {
            return false;
        }

        var workArea = SystemParameters.WorkArea;
        var width = Math.Max(MinWidth, Width > 0 ? Width : 1120);
        var height = Math.Max(MinHeight, Height > 0 ? Height : 720);
        var left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        var top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);

        _syncingFromCastWindow = true;
        try
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
        finally
        {
            _syncingFromCastWindow = false;
        }

        return true;
    }

    private void OnOverlayBoundsChanged(object? sender, EventArgs e)
    {
        if (_syncingFromCastWindow || _applyingOverlayBounds || !IsLoaded || _isResizingWithGrip || _castService.IsRestarting)
        {
            return;
        }

        _isFullscreen = false;
        if (IsRenderViewMode)
        {
            _restoreBounds = ConvertDipBoundsToDevicePixels(GetCurrentOverlayBounds());
            UpdateWindowState();
            return;
        }

        ApplyOverlayBoundsToCastWindow();
        UpdateWindowState();
    }

    private void ApplyOverlayBoundsToCastWindow()
    {
        if (!_castService.IsRunning)
        {
            return;
        }

        RefreshMinimumWindowSize();
        var overlayBounds = GetCurrentOverlayBounds();
        var castBounds = ContractOverlayBoundsToCastBounds(overlayBounds);
        var deviceBounds = ConvertDipBoundsToDevicePixels(castBounds);

        if (!_isFullscreen)
        {
            _restoreBounds = ConvertDipBoundsToDevicePixels(overlayBounds);
        }

        _applyingOverlayBounds = true;
        try
        {
            _castService.TryMoveWindow(deviceBounds);
        }
        finally
        {
            _applyingOverlayBounds = false;
        }
    }

    private void UpdateWindowState(WindowLayoutBounds? bounds = null)
    {
        if (!IsRenderViewMode && _castService.IsRestarting)
        {
            WindowSizeText.Text = "Reloading…";
            FullscreenButton.Content = _isFullscreen ? "Restore" : "Full Size";
            FullscreenButton.ToolTip = _isFullscreen ? "Restore the prior cast size" : "Expand the cast to the current desktop work area";
            return;
        }

        if (bounds is null)
        {
            if (IsLoaded && IsVisible)
            {
                bounds = ConvertDipBoundsToDevicePixels(GetCurrentOverlayBounds());
            }
            else if (!IsRenderViewMode && _castService.TryGetWindowBounds(out var currentCastBounds))
            {
                var overlayDipBounds = ExpandCastBoundsToOverlayBounds(ConvertDevicePixelsToDipBounds(currentCastBounds));
                bounds = ConvertDipBoundsToDevicePixels(overlayDipBounds);
            }
        }

        Title = IsRenderViewMode
            ? "DOPE Companion Render View"
            : $"{_castService.WindowTitle} Controls";
        if (DataContext is MainWindowViewModel viewModel)
        {
            CaptureSourceText.Text = viewModel.LiveSessionCastSourceLabel;
        }
        else
        {
            CaptureSourceText.Text = _castService.CaptureSourceLabel;
        }

        if (bounds is { Width: > 0, Height: > 0 })
        {
            WindowSizeText.Text = $"{bounds.Width} × {bounds.Height}";
        }
        else
        {
            WindowSizeText.Text = IsRenderViewMode ? "Waiting for Render View" : "Waiting for cast";
        }

        FullscreenButton.Content = _isFullscreen ? "Restore" : "Full Size";
        FullscreenButton.ToolTip = _isFullscreen
            ? "Restore the prior surface size"
            : "Expand the current main surface to the desktop work area";
    }

    private void QueueChromeRefresh()
    {
        if (_chromeRefreshQueued || !IsLoaded)
        {
            return;
        }

        _chromeRefreshQueued = true;
        _ = Dispatcher.InvokeAsync(() =>
        {
            _chromeRefreshQueued = false;
            RefreshChromeAfterReload();
        }, DispatcherPriority.Render);
    }

    private void RefreshChromeAfterReload()
    {
        if (!IsLoaded || !IsVisible)
        {
            return;
        }

        InvalidateChrome();
        UpdateLayout();

        if (_windowHandle == 0)
        {
            return;
        }

        NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwShowNoActivate);
        NativeMethods.SetWindowPos(
            _windowHandle,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpFrameChanged |
            NativeMethods.SwpShowWindow);
        NativeMethods.RedrawWindow(
            _windowHandle,
            0,
            0,
            NativeMethods.RdwInvalidate |
            NativeMethods.RdwErase |
            NativeMethods.RdwFrame |
            NativeMethods.RdwAllChildren |
            NativeMethods.RdwUpdateNow);
    }

    private void InvalidateChrome()
    {
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
        RootChrome.InvalidateMeasure();
        RootChrome.InvalidateArrange();
        RootChrome.InvalidateVisual();
        HeaderChrome.InvalidateMeasure();
        HeaderChrome.InvalidateArrange();
        HeaderChrome.InvalidateVisual();
        VideoViewportSlot.InvalidateMeasure();
        VideoViewportSlot.InvalidateArrange();
        VideoViewportSlot.InvalidateVisual();
        SidebarChrome.InvalidateMeasure();
        SidebarChrome.InvalidateArrange();
        SidebarChrome.InvalidateVisual();
    }

    private void OnMinimizeWindowClicked(object sender, RoutedEventArgs e)
    {
        if (IsRenderViewMode)
        {
            WindowState = WindowState.Minimized;
            return;
        }

        _castService.TryMinimizeWindow();
        Hide();
    }

    private async void OnToggleFullscreenWindowClicked(object sender, RoutedEventArgs e)
    {
        ActivateOverlayWindow();

        if (_isFullscreen)
        {
            if (_restoreBounds is { } restoreBounds)
            {
                _isFullscreen = false;
                await ApplyResizedBoundsAsync(restoreBounds).ConfigureAwait(true);
                return;
            }
        }

        if (IsRenderViewMode || _castService.TryGetWindowBounds(out _))
        {
            _restoreBounds = ConvertDipBoundsToDevicePixels(GetCurrentOverlayBounds());
        }

        var workArea = SystemParameters.WorkArea;
        var deviceWorkArea = ConvertDipBoundsToDevicePixels(new WindowLayoutBounds(
            (int)Math.Round(workArea.Left),
            (int)Math.Round(workArea.Top),
            Math.Max((int)MinWidth, (int)Math.Round(workArea.Width)),
            Math.Max((int)MinHeight, (int)Math.Round(workArea.Height))));
        _isFullscreen = true;
        await ApplyResizedBoundsAsync(deviceWorkArea).ConfigureAwait(true);
    }

    private void OnStopWindowClicked(object sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref _foregroundActivationGeneration);
        ClearOwner();

        if (IsVisible)
        {
            Hide();
        }

        if (_stopCastCommand.CanExecute(null))
        {
            _stopCastCommand.Execute(null);
        }
    }

    private void OnInteractiveControlPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => ActivateOverlayWindow();

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInteractiveHeaderSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        ActivateOverlayWindow();
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ActivateOverlayWindow()
    {
        try
        {
            CompanionWindowActivationHelper.DismissKnownBlockingShellOverlays();

            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Focus();
            _ = CompanionWindowActivationHelper.PromoteHandleToForeground(_windowHandle);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void QueueOverlayActivation()
    {
        var generation = Interlocked.Increment(ref _foregroundActivationGeneration);
        ScheduleOverlayActivation(generation, TimeSpan.Zero);
        ScheduleOverlayActivation(generation, TimeSpan.FromMilliseconds(120));
        ScheduleOverlayActivation(generation, TimeSpan.FromMilliseconds(360));
    }

    private void ScheduleOverlayActivation(int generation, TimeSpan delay)
        => _ = ActivateOverlayWindowAsync(generation, delay);

    private async Task ActivateOverlayWindowAsync(int generation, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay).ConfigureAwait(true);
            }

            if (generation != Volatile.Read(ref _foregroundActivationGeneration) ||
                !IsLoaded ||
                Dispatcher.HasShutdownStarted)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (generation != Volatile.Read(ref _foregroundActivationGeneration) || !IsLoaded)
                {
                    return;
                }

                ActivateOverlayWindow();
            }, DispatcherPriority.ApplicationIdle);
        }
        catch (TaskCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool IsInteractiveHeaderSource(DependencyObject? source)
        => FindAncestor<Button>(source) is not null ||
           FindAncestor<ComboBox>(source) is not null ||
           FindAncestor<TextBox>(source) is not null;

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject? source)
        where TAncestor : DependencyObject
    {
        while (source is not null)
        {
            if (source is TAncestor matched)
            {
                return matched;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void OnResizeGripDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb)
        {
            return;
        }

        var left = Left;
        var top = Top;
        var width = Width;
        var height = Height;

        switch (thumb.Name)
        {
            case nameof(LeftGrip):
                left += e.HorizontalChange;
                width -= e.HorizontalChange;
                break;
            case nameof(RightGrip):
                width += e.HorizontalChange;
                break;
            case nameof(TopGrip):
                top += e.VerticalChange;
                height -= e.VerticalChange;
                break;
            case nameof(BottomGrip):
                height += e.VerticalChange;
                break;
            case nameof(TopLeftGrip):
                left += e.HorizontalChange;
                width -= e.HorizontalChange;
                top += e.VerticalChange;
                height -= e.VerticalChange;
                break;
            case nameof(TopRightGrip):
                width += e.HorizontalChange;
                top += e.VerticalChange;
                height -= e.VerticalChange;
                break;
            case nameof(BottomLeftGrip):
                left += e.HorizontalChange;
                width -= e.HorizontalChange;
                height += e.VerticalChange;
                break;
            case nameof(BottomRightGrip):
                width += e.HorizontalChange;
                height += e.VerticalChange;
                break;
        }

        if (width < MinWidth)
        {
            if (thumb.Name.Contains("Left", StringComparison.Ordinal))
            {
                left -= MinWidth - width;
            }

            width = MinWidth;
        }

        if (height < MinHeight)
        {
            if (thumb.Name.Contains("Top", StringComparison.Ordinal))
            {
                top -= MinHeight - height;
            }

            height = MinHeight;
        }

        _syncingFromCastWindow = true;
        try
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
        finally
        {
            _syncingFromCastWindow = false;
        }
    }

    private void OnResizeGripDragStarted(object sender, DragStartedEventArgs e)
    {
        _isResizingWithGrip = true;
        _restoreBounds = ConvertDipBoundsToDevicePixels(GetCurrentOverlayBounds());
    }

    private async void OnResizeGripDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isResizingWithGrip = false;
        _isFullscreen = false;
        var bounds = ConvertDipBoundsToDevicePixels(GetCurrentOverlayBounds());
        _restoreBounds = bounds;
        await ApplyResizedBoundsAsync(bounds).ConfigureAwait(true);
    }

    private async Task ApplyResizedBoundsAsync(WindowLayoutBounds overlayBounds)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (IsRenderViewMode)
        {
            var renderViewDipBounds = ConvertDevicePixelsToDipBounds(overlayBounds);
            _syncingFromCastWindow = true;
            try
            {
                Left = renderViewDipBounds.X;
                Top = renderViewDipBounds.Y;
                Width = Math.Max(MinWidth, renderViewDipBounds.Width);
                Height = Math.Max(MinHeight, renderViewDipBounds.Height);
            }
            finally
            {
                _syncingFromCastWindow = false;
            }

            UpdateWindowState(overlayBounds);
            return;
        }

        RefreshMinimumWindowSize();
        var overlayDipBounds = ConvertDevicePixelsToDipBounds(overlayBounds);
        var castDeviceBounds = ConvertDipBoundsToDevicePixels(ContractOverlayBoundsToCastBounds(overlayDipBounds));

        UpdateWindowState(overlayBounds);
        var outcome = await _castService.RestartDisplay0Async(castDeviceBounds).ConfigureAwait(true);
        if (outcome.Kind == DopeCompanion.Core.Models.OperationOutcomeKind.Success)
        {
            return;
        }

        if (IsLoaded)
        {
            UpdateWindowState();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest)
        {
            return IntPtr.Zero;
        }

        var x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        var y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
        var point = PointFromScreen(new Point(x, y));

        if (IsOverlayInteractivePoint(point))
        {
            return IntPtr.Zero;
        }

        handled = true;
        return HtTransparent;
    }

    private bool IsOverlayInteractivePoint(Point point)
    {
        const double edgeBand = 14;

        if (point.X <= edgeBand ||
            point.Y <= edgeBand ||
            point.X >= ActualWidth - edgeBand ||
            point.Y >= ActualHeight - edgeBand)
        {
            return true;
        }

        var headerBounds = HeaderChrome.TransformToAncestor(this)
            .TransformBounds(new Rect(new Point(0, 0), HeaderChrome.RenderSize));
        if (headerBounds.Contains(point))
        {
            return true;
        }

        var viewportBounds = VideoViewportSlot.TransformToAncestor(this)
            .TransformBounds(new Rect(new Point(0, 0), VideoViewportSlot.RenderSize));
        if (IsRenderViewMode && viewportBounds.Contains(point))
        {
            return true;
        }

        var sidebarBounds = SidebarChrome.TransformToAncestor(this)
            .TransformBounds(new Rect(new Point(0, 0), SidebarChrome.RenderSize));

        return sidebarBounds.Contains(point);
    }

    private WindowLayoutBounds ConvertDevicePixelsToDipBounds(WindowLayoutBounds bounds)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source || source.CompositionTarget is null)
        {
            return bounds;
        }

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(bounds.X, bounds.Y));
        var bottomRight = transform.Transform(new Point(bounds.X + bounds.Width, bounds.Y + bounds.Height));

        return new WindowLayoutBounds(
            (int)Math.Round(topLeft.X),
            (int)Math.Round(topLeft.Y),
            Math.Max(1, (int)Math.Round(bottomRight.X - topLeft.X)),
            Math.Max(1, (int)Math.Round(bottomRight.Y - topLeft.Y)));
    }

    private WindowLayoutBounds ConvertDipBoundsToDevicePixels(WindowLayoutBounds bounds)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source || source.CompositionTarget is null)
        {
            return bounds;
        }

        var transform = source.CompositionTarget.TransformToDevice;
        var topLeft = transform.Transform(new Point(bounds.X, bounds.Y));
        var bottomRight = transform.Transform(new Point(bounds.X + bounds.Width, bounds.Y + bounds.Height));

        return new WindowLayoutBounds(
            (int)Math.Round(topLeft.X),
            (int)Math.Round(topLeft.Y),
            Math.Max(1, (int)Math.Round(bottomRight.X - topLeft.X)),
            Math.Max(1, (int)Math.Round(bottomRight.Y - topLeft.Y)));
    }

    private void TrySetOwner()
    {
        if (_castService.TryGetWindowHandle(out var castHandle))
        {
            TrySetOwner(castHandle);
        }
    }

    private void TrySetOwner(nint castHandle)
    {
        if (_windowHandle == 0 || castHandle == 0 || castHandle == _ownedCastHandle)
        {
            return;
        }

        SetWindowLongPtr(_windowHandle, GwlpHwndParent, castHandle);
        _ownedCastHandle = castHandle;
    }

    private void ClearOwner()
    {
        if (_windowHandle == 0 || _ownedCastHandle == 0)
        {
            return;
        }

        SetWindowLongPtr(_windowHandle, GwlpHwndParent, 0);
        _ownedCastHandle = 0;
    }

    private WindowLayoutBounds GetCurrentOverlayBounds()
        => new(
            (int)Math.Round(Left),
            (int)Math.Round(Top),
            Math.Max(1, (int)Math.Round(Width)),
            Math.Max(1, (int)Math.Round(Height)));

    private void RefreshMinimumWindowSize()
    {
        if (!TryGetChromeMetrics(out var metrics))
        {
            return;
        }

        MinWidth = Math.Max(_baseMinWidth, metrics.GetMinimumOuterWidth(MinimumViewportWidth));
        MinHeight = Math.Max(_baseMinHeight, metrics.GetMinimumOuterHeight(MinimumViewportHeight));
    }

    private WindowLayoutBounds ExpandCastBoundsToOverlayBounds(WindowLayoutBounds castBounds)
        => TryGetChromeMetrics(out var metrics)
            ? metrics.ExpandViewportBounds(castBounds)
            : castBounds;

    private WindowLayoutBounds ContractOverlayBoundsToCastBounds(WindowLayoutBounds overlayBounds)
        => TryGetChromeMetrics(out var metrics)
            ? metrics.ContractOuterBounds(overlayBounds)
            : overlayBounds;

    private bool TryGetChromeMetrics(out DisplayCastChromeMetrics metrics)
    {
        if (!IsLoaded ||
            ActualWidth <= 0 ||
            ActualHeight <= 0 ||
            VideoViewportSlot.RenderSize.Width <= 0 ||
            VideoViewportSlot.RenderSize.Height <= 0)
        {
            metrics = default!;
            return false;
        }

        UpdateLayout();
        var viewportBounds = VideoViewportSlot.TransformToAncestor(this)
            .TransformBounds(new Rect(new Point(0, 0), VideoViewportSlot.RenderSize));
        metrics = DisplayCastChromeMetrics.FromViewportBounds(viewportBounds, new Size(ActualWidth, ActualHeight));
        return true;
    }

    private static nint SetWindowLongPtr(IntPtr windowHandle, int index, nint newValue)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, newValue)
            : SetWindowLong32(windowHandle, index, newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(IntPtr windowHandle, int index, nint newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern nint SetWindowLong32(IntPtr windowHandle, int index, nint newValue);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(nint windowHandle, int command);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            nint windowHandle,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RedrawWindow(
            nint windowHandle,
            nint updateRect,
            nint updateRegion,
            uint flags);

        public const int SwShowNoActivate = 4;
        public const int SwShow = 5;
        public const int SwRestore = 9;

        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoZOrder = 0x0004;
        public const uint SwpFrameChanged = 0x0020;
        public const uint SwpShowWindow = 0x0040;
        public const uint SwpNoActivate = 0x0010;

        public const uint RdwInvalidate = 0x0001;
        public const uint RdwErase = 0x0004;
        public const uint RdwAllChildren = 0x0080;
        public const uint RdwUpdateNow = 0x0100;
        public const uint RdwFrame = 0x0400;
    }
}
