using DopeCompanion.Core.Services;

namespace DopeCompanion.Core.Tests;

public sealed class CompanionOperatorDataLayoutTests
{
    [Fact]
    public void ResolveRootPath_UsesHostVisiblePackagedRoot_WhenPackageFamilyIsKnown()
    {
        var rootPath = CompanionOperatorDataLayout.ResolveRootPath(
            @"C:\Users\joelp\AppData\Local",
            "MesmerPrism.DopeCompanion_8wekyb3d8bbwe",
            overrideRoot: null);

        Assert.Equal(
            @"C:\Users\joelp\AppData\Local\Packages\MesmerPrism.DopeCompanion_8wekyb3d8bbwe\LocalCache\Local\DopeCompanion",
            rootPath);
    }

    [Fact]
    public void ResolveRootPath_PrefersExplicitOverrideRoot()
    {
        var rootPath = CompanionOperatorDataLayout.ResolveRootPath(
            @"C:\Users\joelp\AppData\Local",
            "MesmerPrism.DopeCompanion_8wekyb3d8bbwe",
            @"D:\DopeData");

        Assert.Equal(@"D:\DopeData", rootPath);
    }

    [Fact]
    public void RemapToResolvedRootPath_RemapsLegacyBareLocalAppDataPath()
    {
        const string localAppDataPath = @"C:\Users\joelp\AppData\Local";
        const string resolvedRootPath = @"C:\Users\joelp\AppData\Local\Packages\MesmerPrism.DopeCompanion_8wekyb3d8bbwe\LocalCache\Local\DopeCompanion";
        const string legacySessionPath = @"C:\Users\joelp\AppData\Local\DopeCompanion\study-data\dope-projected-feed-colorama\participant-P001\session-20260413T132014Z";

        var remappedPath = CompanionOperatorDataLayout.RemapToResolvedRootPath(
            legacySessionPath,
            localAppDataPath,
            resolvedRootPath);

        Assert.Equal(
            @"C:\Users\joelp\AppData\Local\Packages\MesmerPrism.DopeCompanion_8wekyb3d8bbwe\LocalCache\Local\DopeCompanion\study-data\dope-projected-feed-colorama\participant-P001\session-20260413T132014Z",
            remappedPath);
    }

    [Fact]
    public void TryReadPackagedFamilyNameFromProcessPath_ParsesWindowsAppsDirectory()
    {
        var familyName = CompanionOperatorDataLayout.TryReadPackagedFamilyNameFromProcessPath(
            @"C:\Program Files\WindowsApps\MesmerPrism.DopeCompanion_0.1.46.0_x64__8wekyb3d8bbwe\DopeCompanion.App\DopeCompanion.exe");

        Assert.Equal("MesmerPrism.DopeCompanion_8wekyb3d8bbwe", familyName);
    }

    [Fact]
    public void TryResolveLegacyPackagedRoot_UsesLegacyFamilyRoot_ForRotatedPreviewPackage()
    {
        var legacyRoot = CompanionOperatorDataLayout.TryResolveLegacyPackagedRoot(
            @"C:\Users\joelp\AppData\Local",
            "MesmerPrism.DopeCompanionPreview_8wekyb3d8bbwe",
            path => string.Equals(
                path,
                @"C:\Users\joelp\AppData\Local\Packages\MesmerPrism.DopeCompanion_8wekyb3d8bbwe\LocalCache\Local\DopeCompanion",
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(
            @"C:\Users\joelp\AppData\Local\Packages\MesmerPrism.DopeCompanion_8wekyb3d8bbwe\LocalCache\Local\DopeCompanion",
            legacyRoot);
    }
}

