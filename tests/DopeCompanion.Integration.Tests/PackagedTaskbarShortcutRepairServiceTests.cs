using DopeCompanion.App;

namespace DopeCompanion.Integration.Tests;

public sealed class PackagedTaskbarShortcutRepairServiceTests
{
    [Fact]
    public void IsPreviewPackageFamilyName_matches_only_rotated_preview_family()
    {
        Assert.True(PackagedTaskbarShortcutRepairService.IsPreviewPackageFamilyName(
            "MesmerPrism.DopeCompanionPreview_zncnfcs118r0y"));
        Assert.False(PackagedTaskbarShortcutRepairService.IsPreviewPackageFamilyName(
            "MesmerPrism.DopeCompanion_zncnfcs118r0y"));
        Assert.False(PackagedTaskbarShortcutRepairService.IsPreviewPackageFamilyName(null));
    }

    [Fact]
    public void BuildAppsFolderArguments_targets_preview_package_family()
    {
        var arguments = PackagedTaskbarShortcutRepairService.BuildAppsFolderArguments(
            "MesmerPrism.DopeCompanionPreview_zncnfcs118r0y");

        Assert.Equal(
            @"shell:AppsFolder\MesmerPrism.DopeCompanionPreview_zncnfcs118r0y!App",
            arguments);
    }

    [Fact]
    public void ShouldRepairShortcut_matches_legacy_packaged_taskbar_pin()
    {
        var shouldRepair = PackagedTaskbarShortcutRepairService.ShouldRepairShortcut(
            "DOPE Companion.lnk",
            targetPath: @"C:\Windows\explorer.exe",
            arguments: @"shell:AppsFolder\MesmerPrism.DopeCompanion_zncnfcs118r0y!App");

        Assert.True(shouldRepair);
    }

    [Fact]
    public void ShouldRepairShortcut_ignores_repo_local_launcher_pin()
    {
        var shouldRepair = PackagedTaskbarShortcutRepairService.ShouldRepairShortcut(
            "DOPE Companion.lnk",
            targetPath: @"C:\Windows\System32\wscript.exe",
            arguments: @"//B //nologo ""C:\Users\tillh\source\repos\DopeCompanion\tools\app\Start-Desktop-App.vbs""");

        Assert.False(shouldRepair);
    }

    [Fact]
    public void ShouldRepairShortcut_ignores_preview_package_pin()
    {
        var shouldRepair = PackagedTaskbarShortcutRepairService.ShouldRepairShortcut(
            "DOPE Companion Preview.lnk",
            targetPath: @"C:\Windows\explorer.exe",
            arguments: @"shell:AppsFolder\MesmerPrism.DopeCompanionPreview_zncnfcs118r0y!App");

        Assert.True(shouldRepair);
    }

    [Fact]
    public void ShouldInspectShortcutName_ignores_dev_pin()
    {
        var shouldInspect = PackagedTaskbarShortcutRepairService.ShouldInspectShortcutName(
            "DOPE Companion Dev.lnk");

        Assert.False(shouldInspect);
    }
}
