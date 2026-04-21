using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace DopeCompanion.App;

internal static class CompanionWindowActivationHelper
{
    private const string ClickToDoWindowTitle = "Click to Do";

    public static bool ActivateWindow(Window? targetWindow)
    {
        if (targetWindow is null)
        {
            return false;
        }

        try
        {
            if (!targetWindow.IsVisible)
            {
                targetWindow.Show();
            }

            if (targetWindow.WindowState == WindowState.Minimized)
            {
                targetWindow.WindowState = WindowState.Normal;
            }

            targetWindow.Activate();
            targetWindow.Focus();
        }
        catch (InvalidOperationException)
        {
        }

        var windowHandle = new WindowInteropHelper(targetWindow).Handle;
        DismissKnownBlockingShellOverlays();
        return PromoteHandleToForeground(windowHandle);
    }

    public static void DismissKnownBlockingShellOverlays()
    {
        TryCloseVisibleTopLevelWindowByExactTitle(ClickToDoWindowTitle);
    }

    public static bool PromoteHandleToForeground(nint windowHandle)
    {
        if (windowHandle == 0 || !NativeMethods.IsWindow(windowHandle))
        {
            return false;
        }

        var currentThreadId = NativeMethods.GetCurrentThreadId();
        var targetThreadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out _);
        var foregroundHandle = NativeMethods.GetForegroundWindow();
        var foregroundThreadId = foregroundHandle == 0
            ? 0u
            : NativeMethods.GetWindowThreadProcessId(foregroundHandle, out _);

        var attachedToForeground = false;
        var attachedToTarget = false;
        try
        {
            if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
            {
                attachedToForeground = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            if (targetThreadId != 0 &&
                targetThreadId != currentThreadId &&
                targetThreadId != foregroundThreadId)
            {
                attachedToTarget = NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            if (NativeMethods.IsIconic(windowHandle))
            {
                NativeMethods.ShowWindow(windowHandle, NativeMethods.SwRestore);
            }
            else
            {
                NativeMethods.ShowWindow(windowHandle, NativeMethods.SwShow);
            }

            NativeMethods.BringWindowToTop(windowHandle);
            NativeMethods.SetActiveWindow(windowHandle);
            NativeMethods.SetFocus(windowHandle);

            var activated = NativeMethods.SetForegroundWindow(windowHandle);
            if (!activated)
            {
                NativeMethods.ShowWindow(windowHandle, NativeMethods.SwShow);
                NativeMethods.BringWindowToTop(windowHandle);
                activated = NativeMethods.SetForegroundWindow(windowHandle);
            }

            return activated || NativeMethods.GetForegroundWindow() == windowHandle;
        }
        finally
        {
            if (attachedToTarget)
            {
                _ = NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, false);
            }

            if (attachedToForeground)
            {
                _ = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }
    }

    private static void TryCloseVisibleTopLevelWindowByExactTitle(string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return;
        }

        nint blockingWindowHandle = 0;
        _ = NativeMethods.EnumWindows((windowHandle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(windowHandle))
            {
                return true;
            }

            var titleLength = NativeMethods.GetWindowTextLength(windowHandle);
            if (titleLength <= 0)
            {
                return true;
            }

            var titleBuilder = new StringBuilder(titleLength + 1);
            _ = NativeMethods.GetWindowText(windowHandle, titleBuilder, titleBuilder.Capacity);
            if (!string.Equals(titleBuilder.ToString(), windowTitle, StringComparison.Ordinal))
            {
                return true;
            }

            blockingWindowHandle = windowHandle;
            return false;
        }, 0);

        if (blockingWindowHandle != 0)
        {
            _ = NativeMethods.PostMessage(blockingWindowHandle, NativeMethods.WmClose, 0, 0);
        }
    }

    private static class NativeMethods
    {
        public delegate bool EnumWindowsProc(nint windowHandle, nint lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(nint windowHandle);

        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(nint windowHandle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(nint windowHandle, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(nint windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(nint windowHandle, uint message, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(nint windowHandle);

        [DllImport("user32.dll")]
        public static extern nint SetActiveWindow(nint windowHandle);

        [DllImport("user32.dll")]
        public static extern nint SetFocus(nint windowHandle);

        public const int SwShow = 5;
        public const int SwRestore = 9;
        public const uint WmClose = 0x0010;
    }
}
