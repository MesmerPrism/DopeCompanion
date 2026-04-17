using DopeCompanion.App;

namespace DopeCompanion.Integration.Tests;

public sealed class AppBuildIdentityTests
{
    [Fact]
    public void TryReadPackagedVersionFromProcessPath_ParsesWindowsAppsVersion()
    {
        var processPath = @"C:\Program Files\WindowsApps\MesmerPrism.DopeCompanion_0.1.46.0_x64__8wekyb3d8bbwe\DopeCompanion.App\DopeCompanion.exe";

        var version = AppBuildIdentity.TryReadPackagedVersionFromProcessPath(processPath);

        Assert.Equal("0.1.46.0", version);
    }

    [Fact]
    public void TryReadPackagedVersionFromProcessPath_ReturnsNullOutsideWindowsApps()
    {
        var processPath = @"C:\Users\tillh\source\repos\DopeCompanion\src\DopeCompanion.App\bin\Release\net10.0-windows\DopeCompanion.exe";

        var version = AppBuildIdentity.TryReadPackagedVersionFromProcessPath(processPath);

        Assert.Null(version);
    }
}
