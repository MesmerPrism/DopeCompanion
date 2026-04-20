using System.IO;

namespace DopeCompanion.Cli;

internal static class CliAssetLocator
{
    public static string? TryResolveRepoRoot()
    {
        foreach (var root in EnumerateSearchRoots())
        {
            if (File.Exists(Path.Combine(root, "DopeCompanion.sln")))
            {
                return root;
            }
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source",
            "repos",
            "DopeCompanion");
        return Directory.Exists(fallback) ? Path.GetFullPath(fallback) : null;
    }

    public static string ResolveQuestSessionKitRoot()
        => TryResolveQuestSessionKitRoot()
            ?? throw new DirectoryNotFoundException("Could not resolve the Quest Session Kit root for the CLI.");

    public static string ResolveStudyShellRoot()
        => TryResolveStudyShellRoot()
            ?? throw new DirectoryNotFoundException("Could not resolve the study-shell root for the CLI.");

    public static string? TryResolveQuestSessionKitRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("DOPE_QUEST_SESSION_KIT_ROOT"),
            TryResolveRepoRelativeDirectory("samples", "quest-session-kit"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "DopeCompanion",
                "samples",
                "quest-session-kit"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "AstralKarateDojo",
                "QuestSessionKit"));

    public static string? TryResolveStudyShellRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("DOPE_STUDY_SHELL_ROOT"),
            TryResolveRepoRelativeDirectory("samples", "study-shells"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "DopeCompanion",
                "samples",
                "study-shells"));

    public static string? TryResolveOscillatorConfigRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("DOPE_OSCILLATOR_CONFIG_ROOT"),
            TryResolveRepoRelativeDirectory("samples", "oscillator-config"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "DopeCompanion",
                "samples",
                "oscillator-config"));

    public static string? TryResolveDocsRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("DOPE_DOCS_ROOT"),
            TryResolveRepoRelativeDirectory("docs"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "DopeCompanion",
                "docs"));

    public static string? TryResolveVerificationHarnessProjectPath()
        => TryResolveExistingFile(
            Environment.GetEnvironmentVariable("DOPE_VERIFICATION_HARNESS_PROJECT"),
            TryResolveRepoRelativeFile("tools", "DopeCompanion.VerificationHarness", "DopeCompanion.VerificationHarness.csproj"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "DopeCompanion",
                "tools",
                "DopeCompanion.VerificationHarness",
                "DopeCompanion.VerificationHarness.csproj"));

    public static string? TryResolveVerificationHarnessExecutablePath()
    {
        var repoRoot = TryResolveRepoRoot();
        return TryResolveExistingFile(
            Environment.GetEnvironmentVariable("DOPE_VERIFICATION_HARNESS_EXE"),
            repoRoot is null
                ? null
                : Path.Combine(repoRoot, "tools", "DopeCompanion.VerificationHarness", "bin", "Debug", "net10.0-windows10.0.19041.0", "DopeCompanion.VerificationHarness.exe"),
            repoRoot is null
                ? null
                : Path.Combine(repoRoot, "tools", "DopeCompanion.VerificationHarness", "bin", "Debug", "net10.0-windows", "DopeCompanion.VerificationHarness.exe"),
            repoRoot is null
                ? null
                : Path.Combine(repoRoot, "tools", "DopeCompanion.VerificationHarness", "bin", "Release", "net10.0-windows10.0.19041.0", "DopeCompanion.VerificationHarness.exe"),
            repoRoot is null
                ? null
                : Path.Combine(repoRoot, "tools", "DopeCompanion.VerificationHarness", "bin", "Release", "net10.0-windows", "DopeCompanion.VerificationHarness.exe"));
    }

    private static string? TryResolveRepoRelativeDirectory(params string[] relativeSegments)
    {
        foreach (var root in EnumerateSearchRoots())
        {
            var candidate = Path.Combine([root, .. relativeSegments]);
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static string? TryResolveRepoRelativeFile(params string[] relativeSegments)
    {
        foreach (var root in EnumerateSearchRoots())
        {
            var candidate = Path.Combine([root, .. relativeSegments]);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(seed))
            {
                continue;
            }

            DirectoryInfo? current;
            try
            {
                current = new DirectoryInfo(Path.GetFullPath(seed));
            }
            catch
            {
                continue;
            }

            while (current is not null)
            {
                if (seen.Add(current.FullName))
                {
                    yield return current.FullName;
                }

                current = current.Parent;
            }
        }
    }

    private static string? TryResolveExistingDirectory(params string?[] candidates)
        => candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault();

    private static string? TryResolveExistingFile(params string?[] candidates)
        => candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault();
}
