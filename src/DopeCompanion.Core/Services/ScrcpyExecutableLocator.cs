namespace DopeCompanion.Core.Services;

public static class ScrcpyExecutableLocator
{
    public const string EnvironmentVariableName = "DOPE_SCRCPY_EXE";

    public static string? TryLocate(string? appBaseDirectory = null)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(appBaseDirectory)
            ? AppContext.BaseDirectory
            : appBaseDirectory;

        var questMultiStreamRepoRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source",
            "repos",
            "Quest Multi Stream");

        return new[]
        {
            Environment.GetEnvironmentVariable(EnvironmentVariableName),
            OfficialQuestToolingLayout.ScrcpyExecutablePath,
            Path.Combine(baseDirectory, "scrcpy.exe"),
            Path.Combine(baseDirectory, "scrcpy", "scrcpy.exe"),
            TryFindNewestExecutable(Path.Combine(questMultiStreamRepoRoot, "tools", "scrcpy"), "scrcpy.exe"),
            TryFindOnPath("scrcpy.exe")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
        .Select(path => Path.GetFullPath(path!))
        .FirstOrDefault();
    }

    internal static string? TryFindNewestExecutable(string root, string fileName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    internal static string? TryFindOnPath(string fileName)
    {
        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in pathEntries)
        {
            var candidate = Path.Combine(entry, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
