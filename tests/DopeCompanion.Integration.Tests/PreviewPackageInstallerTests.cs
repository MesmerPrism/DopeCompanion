using System.Diagnostics;
using DopeCompanion.PreviewInstaller;

namespace DopeCompanion.Integration.Tests;

public sealed class PreviewPackageInstallerTests
{
    [Fact]
    public void ParseAppInstallerManifest_reads_main_package_identity()
    {
        var appInstallerPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.appinstaller");

        try
        {
            File.WriteAllText(
                appInstallerPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2018" Version="0.1.46.0">
                  <MainPackage Name="MesmerPrism.DopeCompanionPreview"
                               Version="0.1.46.0"
                               Publisher="CN=MesmerPrism"
                               ProcessorArchitecture="x64"
                               Uri="https://example.invalid/DopeCompanion.msix" />
                </AppInstaller>
                """);

            var identity = PreviewPackageInstaller.ParseAppInstallerManifest(appInstallerPath);

            Assert.Equal("MesmerPrism.DopeCompanionPreview", identity.Name);
            Assert.Equal("CN=MesmerPrism", identity.Publisher);
            Assert.Equal("0.1.46.0", identity.Version);
            Assert.Equal(new Uri(appInstallerPath, UriKind.Absolute), identity.AppInstallerUri);
        }
        finally
        {
            File.Delete(appInstallerPath);
        }
    }

    [Fact]
    public void FindExistingPackage_matches_name_and_publisher_and_prefers_newest_match()
    {
        var packageIdentity = new PreviewPackageIdentity(
            "MesmerPrism.DopeCompanionPreview",
            "CN=MesmerPrism",
            "0.1.46.0",
            new Uri("file:///C:/Temp/DopeCompanion.appinstaller"));

        var package = PreviewPackageInstaller.FindExistingPackage(
            packageIdentity,
            new[]
            {
                new PreviewPackageCandidate(
                    "MesmerPrism.DopeCompanionPreview_0.1.36.0_x64__zcnfcs118r0y",
                    "MesmerPrism.DopeCompanionPreview",
                    "CN=MesmerPrism",
                    "0.1.36.0",
                    "MesmerPrism.DopeCompanionPreview_zcnfcs118r0y"),
                new PreviewPackageCandidate(
                    "MesmerPrism.DopeCompanionPreview_0.1.37.0_x64__zcnfcs118r0y",
                    "MesmerPrism.DopeCompanionPreview",
                    "CN=MesmerPrism",
                    "0.1.37.0",
                    "MesmerPrism.DopeCompanionPreview_zcnfcs118r0y"),
                new PreviewPackageCandidate(
                    "MesmerPrism.DopeCompanionPreview_0.1.99.0_x64__otherpublisher",
                    "MesmerPrism.DopeCompanionPreview",
                    "CN=OtherPublisher",
                    "0.1.99.0",
                    "MesmerPrism.DopeCompanionPreview_otherpublisher")
            });

        Assert.NotNull(package);
        Assert.Equal("MesmerPrism.DopeCompanionPreview_0.1.37.0_x64__zcnfcs118r0y", package!.FullName);
        Assert.Equal("0.1.37.0", package.Version);
        Assert.Equal("MesmerPrism.DopeCompanionPreview_zcnfcs118r0y", package.FamilyName);
    }

    [Fact]
    public void FindLegacyPackageToRetire_matches_legacy_family_for_rotated_preview_package()
    {
        var packageIdentity = new PreviewPackageIdentity(
            "MesmerPrism.DopeCompanionPreview",
            "CN=MesmerPrism",
            "0.1.58.0",
            new Uri("file:///C:/Temp/DopeCompanion.appinstaller"));

        var package = PreviewPackageInstaller.FindLegacyPackageToRetire(
            packageIdentity,
            new[]
            {
                new PreviewPackageCandidate(
                    "MesmerPrism.DopeCompanion_0.1.56.0_x64__zncnfcs118r0y",
                    "MesmerPrism.DopeCompanion",
                    "CN=MesmerPrism",
                    "0.1.56.0",
                    "MesmerPrism.DopeCompanion_zncnfcs118r0y"),
                new PreviewPackageCandidate(
                    "MesmerPrism.DopeCompanion_0.1.55.0_x64__zncnfcs118r0y",
                    "MesmerPrism.DopeCompanion",
                    "CN=MesmerPrism",
                    "0.1.55.0",
                    "MesmerPrism.DopeCompanion_zncnfcs118r0y")
            });

        Assert.NotNull(package);
        Assert.Equal("MesmerPrism.DopeCompanion_0.1.56.0_x64__zncnfcs118r0y", package!.FullName);
        Assert.Equal("0.1.56.0", package.Version);
        Assert.Equal("MesmerPrism.DopeCompanion_zncnfcs118r0y", package.FamilyName);
    }

    [Fact]
    public void FindLegacyPackageToRetire_returns_null_for_non_preview_identity()
    {
        var packageIdentity = new PreviewPackageIdentity(
            "MesmerPrism.DopeCompanion",
            "CN=MesmerPrism",
            "0.1.56.0",
            new Uri("file:///C:/Temp/DopeCompanion.appinstaller"));

        var package = PreviewPackageInstaller.FindLegacyPackageToRetire(
            packageIdentity,
            new[]
            {
                new PreviewPackageCandidate(
                    "MesmerPrism.DopeCompanion_0.1.56.0_x64__zncnfcs118r0y",
                    "MesmerPrism.DopeCompanion",
                    "CN=MesmerPrism",
                    "0.1.56.0",
                    "MesmerPrism.DopeCompanion_zncnfcs118r0y")
            });

        Assert.Null(package);
    }

    [Fact]
    public void TryLaunchInstalledPackage_uses_apps_folder_target()
    {
        ProcessStartInfo? capturedStartInfo = null;

        var launched = PreviewPackageInstaller.TryLaunchInstalledPackage(
            new ExistingPreviewPackage(
                "MesmerPrism.DopeCompanionPreview_0.1.46.0_x64__zcnfcs118r0y",
                "0.1.46.0",
                "MesmerPrism.DopeCompanionPreview_zncnfcs118r0y"),
            out var detail,
            startInfo => capturedStartInfo = startInfo);

        Assert.True(launched);
        Assert.Contains("open the installed app automatically", detail, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(capturedStartInfo);
        Assert.Equal("explorer.exe", capturedStartInfo!.FileName);
        Assert.Equal(
            @"shell:AppsFolder\MesmerPrism.DopeCompanionPreview_zncnfcs118r0y!App",
            capturedStartInfo.Arguments);
        Assert.True(capturedStartInfo.UseShellExecute);
    }
}
