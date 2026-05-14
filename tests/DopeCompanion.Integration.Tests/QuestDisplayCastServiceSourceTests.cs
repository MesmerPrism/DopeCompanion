namespace DopeCompanion.Integration.Tests;

public sealed class QuestDisplayCastServiceSourceTests
{
    [Fact]
    public async Task Cast_service_exposes_the_native_minimized_state_of_the_scrcpy_window()
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
            "QuestDisplayCastService.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(sourcePath));

        Assert.Contains("public bool IsWindowMinimized", source, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.IsIconic(_windowHandle)", source, StringComparison.Ordinal);
    }
}
