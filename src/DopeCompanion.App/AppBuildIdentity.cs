using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DopeCompanion.App;

internal static class AppBuildIdentity
{
    internal const string LaunchKindEnvironmentVariable = "DOPE_COMPANION_LAUNCH_KIND";
    internal const string DevLaunchKind = "Dev";
    private static readonly Regex WindowsAppsPackageDirectoryPattern = new(
        @"^(?<name>.+?)_(?<version>\d+\.\d+\.\d+\.\d+)_[^_]+__.+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Lazy<AppBuildStamp> CurrentLazy = new(ResolveCurrent);

    public static AppBuildStamp Current => CurrentLazy.Value;

    private static AppBuildStamp ResolveCurrent()
    {
        var processPath = Environment.ProcessPath ?? string.Empty;
        var launchKind = Environment.GetEnvironmentVariable(LaunchKindEnvironmentVariable) ?? string.Empty;
        var packagedIdentity = TryReadPackagedIdentity() ?? TryReadPackagedIdentityFromProcessPath(processPath);
        if (packagedIdentity is not null)
        {
            var isPreviewPackage = packagedIdentity.Name.Contains("Preview", StringComparison.OrdinalIgnoreCase);
            return new AppBuildStamp(
                isPreviewPackage
                    ? $"Installed preview {packagedIdentity.Version}"
                    : $"Installed app {packagedIdentity.Version}",
                isPreviewPackage
                    ? "DOPE Companion is running from the installed preview package. App Installer updates should target this copy."
                    : "DOPE Companion is running from the installed Windows package. App Installer updates should target this copy.",
                packagedIdentity.Version,
                IsPackaged: true,
                Variant: isPreviewPackage ? AppBuildVariant.Preview : AppBuildVariant.Installed);
        }

        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        var assemblyVersion = assembly.GetName().Version?.ToString() ?? string.Empty;
        var fileVersion = string.IsNullOrWhiteSpace(processPath)
            ? string.Empty
            : FileVersionInfo.GetVersionInfo(processPath).FileVersion ?? string.Empty;

        var bestVersion = FirstNonEmpty(informationalVersion, fileVersion, assemblyVersion);
        if (LooksLikePlaceholderVersion(bestVersion))
        {
            bestVersion = string.Empty;
        }

        var isDevLaunch = string.Equals(launchKind, DevLaunchKind, StringComparison.OrdinalIgnoreCase);

        return new AppBuildStamp(
            isDevLaunch
                ? string.IsNullOrWhiteSpace(bestVersion)
                    ? "Repo dev build"
                    : $"Repo dev build {bestVersion}"
                : string.IsNullOrWhiteSpace(bestVersion)
                    ? "Unpackaged build"
                    : $"Unpackaged build {bestVersion}",
            isDevLaunch
                ? string.IsNullOrWhiteSpace(processPath)
                    ? "Running from the repo-local dev launcher. The packaged Windows update flow does not apply to this copy."
                    : $"Running from the repo-local dev launcher at {processPath}. The packaged Windows update flow does not apply to this copy."
                : string.IsNullOrWhiteSpace(processPath)
                    ? "This copy is not running from an installed MSIX package, so the packaged Windows update flow does not apply to it."
                    : $"Running unpackaged from {processPath}. The packaged Windows update flow does not apply to this copy.",
            string.IsNullOrWhiteSpace(bestVersion) ? "unpackaged" : bestVersion,
            IsPackaged: false,
            Variant: isDevLaunch ? AppBuildVariant.Dev : AppBuildVariant.Unpackaged);
    }

    internal static string? TryReadPackagedVersionFromProcessPath(string? processPath)
        => TryReadPackagedIdentityFromProcessPath(processPath)?.Version;

    private static PackagedIdentity? TryReadPackagedIdentity()
    {
        try
        {
            var packageType = Type.GetType("Windows.ApplicationModel.Package, Windows, ContentType=WindowsRuntime");
            if (packageType is null)
            {
                return null;
            }

            var currentPackage = packageType.GetProperty("Current")?.GetValue(null);
            if (currentPackage is null)
            {
                return null;
            }

            var packageId = currentPackage.GetType().GetProperty("Id")?.GetValue(currentPackage);
            if (packageId is null)
            {
                return null;
            }

            var name = packageId.GetType().GetProperty("Name")?.GetValue(packageId)?.ToString() ?? "Package";
            var version = packageId.GetType().GetProperty("Version")?.GetValue(packageId);
            if (version is null)
            {
                return null;
            }

            var major = Convert.ToUInt16(version.GetType().GetProperty("Major")?.GetValue(version) ?? 0);
            var minor = Convert.ToUInt16(version.GetType().GetProperty("Minor")?.GetValue(version) ?? 0);
            var build = Convert.ToUInt16(version.GetType().GetProperty("Build")?.GetValue(version) ?? 0);
            var revision = Convert.ToUInt16(version.GetType().GetProperty("Revision")?.GetValue(version) ?? 0);
            return new PackagedIdentity(name, $"{major}.{minor}.{build}.{revision}");
        }
        catch
        {
            return null;
        }
    }

    private static PackagedIdentity? TryReadPackagedIdentityFromProcessPath(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath) ||
            processPath.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        DirectoryInfo? directory;
        try
        {
            directory = new FileInfo(processPath).Directory;
        }
        catch
        {
            return null;
        }

        while (directory is not null)
        {
            var match = WindowsAppsPackageDirectoryPattern.Match(directory.Name);
            if (match.Success)
            {
                return new PackagedIdentity(
                    match.Groups["name"].Value,
                    match.Groups["version"].Value);
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool LooksLikePlaceholderVersion(string version)
        => string.IsNullOrWhiteSpace(version)
           || string.Equals(version, "1.0.0.0", StringComparison.OrdinalIgnoreCase)
           || string.Equals(version, "1.0.0", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    internal enum AppBuildVariant
    {
        Installed,
        Preview,
        Dev,
        Unpackaged
    }

    internal sealed record AppBuildStamp(
        string Summary,
        string Detail,
        string ShortId,
        bool IsPackaged,
        AppBuildVariant Variant)
    {
        public string CompanionWindowLabel => Variant switch
        {
            AppBuildVariant.Preview => "DOPE Companion Preview",
            AppBuildVariant.Dev => "DOPE Companion Dev",
            _ => "DOPE Companion"
        };

        public string LiveSessionWindowLabel => Variant switch
        {
            AppBuildVariant.Preview => "DOPE Live Session Preview",
            AppBuildVariant.Dev => "DOPE Live Session Dev",
            _ => "DOPE Live Session"
        };

        public string ExperimentSessionWindowLabel => Variant switch
        {
            AppBuildVariant.Preview => "Dope Experiment Session Preview",
            AppBuildVariant.Dev => "Dope Experiment Session Dev",
            _ => "Dope Experiment Session"
        };

        public string? ExplicitAppUserModelId => Variant switch
        {
            AppBuildVariant.Dev => "MesmerPrism.DopeCompanion.Dev",
            _ => null
        };
    }

    private sealed record PackagedIdentity(string Name, string Version);
}
