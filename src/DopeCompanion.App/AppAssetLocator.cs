using System.IO;

namespace DopeCompanion.App;

internal static class AppAssetLocator
{
    private static readonly string[] BundledCliEntryPoints =
    [
        "dope-companion.exe",
        "dope-companion.dll"
    ];

    public static string? TryResolveQuestSessionKitRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("DOPE_QUEST_SESSION_KIT_ROOT"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "quest-session-kit")),
            Path.Combine(AppContext.BaseDirectory, "samples", "quest-session-kit"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "AstralKarateDojo",
                "QuestSessionKit"));

    public static string ResolveQuestSessionKitRoot()
        => TryResolveQuestSessionKitRoot()
            ?? throw new DirectoryNotFoundException("Could not resolve the requested DOPE companion asset directory.");

    public static string? TryResolveStudyShellRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("DOPE_STUDY_SHELL_ROOT"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "study-shells")),
            Path.Combine(AppContext.BaseDirectory, "samples", "study-shells"),
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
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "oscillator-config")),
            Path.Combine(AppContext.BaseDirectory, "samples", "oscillator-config"),
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
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs")),
            Path.Combine(AppContext.BaseDirectory, "docs"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "DopeCompanion",
                "docs"));

    public static string? TryResolveBundledCliRoot()
        => TryResolveExistingDirectoryContainingAnyFile(
            BundledCliEntryPoints,
            Environment.GetEnvironmentVariable("DOPE_BUNDLED_CLI_ROOT"),
            Path.Combine(AppContext.BaseDirectory, "cli", "current"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "cli-win-x64")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DopeCompanion.Cli", "bin", "Release", "net10.0")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DopeCompanion.Cli", "bin", "Debug", "net10.0")),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "DopeCompanion",
                "artifacts",
                "cli-win-x64"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "DopeCompanion",
                "src",
                "DopeCompanion.Cli",
                "bin",
                "Release",
                "net10.0"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "DopeCompanion",
                "src",
                "DopeCompanion.Cli",
                "bin",
                "Debug",
                "net10.0"));

    public static string? TryResolveDopeParticleSizeTemplatePath()
        => TryResolveExistingFile(
            Environment.GetEnvironmentVariable("DOPE_PARTICLE_SIZE_TUNING_TEMPLATE"),
            Path.Combine(TryResolveOscillatorConfigRoot() ?? string.Empty, "llm-tuning", "dope-particle-size-v1.template.json"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "AstralKarateDojo",
                "QuestSessionKit",
                "LlmTuningProfiles",
                "dope-particle-size-v1.template.json"));

    public static string? TryResolveDopeVisualTuningTemplatePath()
        => TryResolveExistingFile(
            Environment.GetEnvironmentVariable("DOPE_VISUAL_TUNING_TEMPLATE"),
            Path.Combine(TryResolveOscillatorConfigRoot() ?? string.Empty, "llm-tuning", "dope-visual-tuning-v1.template.json"));

    public static string? TryResolveBundledDopeVisualProfilesRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("DOPE_VISUAL_PROFILE_BUNDLE_ROOT"),
            Path.Combine(TryResolveStudyShellRoot() ?? string.Empty, "dope-projected-feed-colorama", "visual-profiles"));

    public static string? TryResolveBundledDopeControllerBreathingProfilesRoot()
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("DOPE_CONTROLLER_BREATHING_PROFILE_BUNDLE_ROOT"),
            Path.Combine(TryResolveStudyShellRoot() ?? string.Empty, "dope-projected-feed-colorama", "dope-controller-breathing-profiles"),
            Path.Combine(TryResolveStudyShellRoot() ?? string.Empty, "dope-projected-feed-colorama", "controller-breathing-profiles"));

    public static string? TryResolveDopeControllerBreathingTuningTemplatePath()
        => TryResolveExistingFile(
            Environment.GetEnvironmentVariable("DOPE_CONTROLLER_BREATHING_TUNING_TEMPLATE"),
            Path.Combine(ResolveQuestSessionKitRoot(), "LlmTuningProfiles", "dope-controller-breathing-tuning-v1.template.json"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "AstralKarateDojo",
                "QuestSessionKit",
                "LlmTuningProfiles",
                "dope-controller-breathing-tuning-v1.template.json"));

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

    private static string? TryResolveExistingDirectoryContainingAnyFile(IEnumerable<string> fileNames, params string?[] candidates)
        => candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            .Select(candidate => Path.GetFullPath(candidate!))
            .FirstOrDefault(candidate => fileNames.Any(fileName => File.Exists(Path.Combine(candidate, fileName))));
}

