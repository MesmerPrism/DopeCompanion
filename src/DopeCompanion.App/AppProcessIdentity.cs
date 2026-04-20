using System.Runtime.InteropServices;

namespace DopeCompanion.App;

internal static class AppProcessIdentity
{
    public static void ApplyCurrentIdentity()
    {
        var appUserModelId = AppBuildIdentity.Current.ExplicitAppUserModelId;
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return;
        }

        _ = SetCurrentProcessExplicitAppUserModelID(appUserModelId);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
