using DopeCompanion.Core.Models;

namespace DopeCompanion.Core.Services;

public static class PublicQuestSessionKitStudyFactory
{
    public const string DefaultStudyId = "dope-projected-feed-colorama";
    private const string DefaultPartnerLabel = "Public operator surface";
    private const string DefaultExpectedStreamName = "quest_monitor";
    private const string DefaultExpectedStreamType = "quest.telemetry";
    private const string DefaultBalancedDeviceProfileId = "dope-projected-feed-balanced";

    public static bool MatchesStudyToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        return string.Equals(token.Trim(), DefaultStudyId, StringComparison.OrdinalIgnoreCase)
               || string.Equals(token.Trim(), "dope_projected_feed_colorama", StringComparison.OrdinalIgnoreCase)
               || string.Equals(token.Trim(), "com.tillh.dynamicoscillatorypatternentrainment", StringComparison.OrdinalIgnoreCase)
               || string.Equals(token.Trim(), "dope-projected-feed-colorama", StringComparison.OrdinalIgnoreCase);
    }

    public static StudyShellDefinition CreateFromCatalog(
        QuestSessionKitCatalog catalog,
        string? studyToken = null,
        string? preferredDeviceProfileId = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var app = ResolveApp(catalog, studyToken);
        var deviceProfile = ResolveDeviceProfile(catalog, app, preferredDeviceProfileId);
        var apkPath = ResolveApkPath(catalog.Source.RootPath, app);
        return Create(app, deviceProfile, apkPath);
    }

    public static StudyShellDefinition Create(
        QuestAppTarget app,
        DeviceProfile? deviceProfile = null,
        string? apkPath = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var resolvedDeviceProfile = deviceProfile ?? new DeviceProfile(
            DefaultBalancedDeviceProfileId,
            "DOPE Projected Feed Balanced",
            "Balanced Quest defaults for the public projected-feed Colorama runtime.",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var resolvedApkPath = string.IsNullOrWhiteSpace(apkPath)
            ? app.ApkFile
            : apkPath;

        return new StudyShellDefinition(
            Id: DefaultStudyId,
            Label: app.Label,
            Partner: DefaultPartnerLabel,
            Description: app.Description,
            App: new StudyPinnedApp(
                Label: app.Label,
                PackageId: app.PackageId,
                ApkPath: resolvedApkPath ?? string.Empty,
                LaunchComponent: app.LaunchComponent,
                Sha256: app.ApkSha256 ?? string.Empty,
                VersionName: string.Empty,
                Notes: app.CompatibilityNotes ?? app.Description,
                AllowManualSelection: true,
                LaunchInKioskMode: false,
                VerificationBaseline: app.VerificationBaseline),
            DeviceProfile: new StudyPinnedDeviceProfile(
                resolvedDeviceProfile.Id,
                resolvedDeviceProfile.Label,
                resolvedDeviceProfile.Description,
                new Dictionary<string, string>(resolvedDeviceProfile.Properties, StringComparer.OrdinalIgnoreCase)),
            Monitoring: new StudyMonitoringProfile(
                ExpectedBreathingLabel: string.Empty,
                ExpectedHeartbeatLabel: string.Empty,
                ExpectedCoherenceLabel: string.Empty,
                ExpectedLslStreamName: DefaultExpectedStreamName,
                ExpectedLslStreamType: DefaultExpectedStreamType,
                RecenterDistanceThresholdUnits: 0.2d,
                LslConnectivityKeys: [],
                LslStreamNameKeys: [],
                LslStreamTypeKeys: [],
                LslValueKeys: [],
                ControllerValueKeys: [],
                ControllerStateKeys: [],
                ControllerTrackingKeys: [],
                AutomaticBreathingValueKeys: [],
                HeartbeatValueKeys: [],
                HeartbeatStateKeys: [],
                CoherenceValueKeys: [],
                CoherenceStateKeys: [],
                PerformanceFpsKeys: [],
                PerformanceFrameTimeKeys: [],
                PerformanceTargetFpsKeys: [],
                PerformanceRefreshRateKeys: [],
                RecenterDistanceKeys: [],
                ParticleVisibilityKeys: []),
            Controls: new StudyControlProfile(
                RecenterCommandActionId: string.Empty,
                ParticleVisibleOnActionId: string.Empty,
                ParticleVisibleOffActionId: string.Empty));
    }

    private static QuestAppTarget ResolveApp(QuestSessionKitCatalog catalog, string? studyToken)
    {
        var token = studyToken?.Trim();
        var apps = catalog.Apps
            .Where(app => !string.Equals(app.PackageId, "com.oculus.browser", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (apps.Length == 0)
        {
            throw new InvalidOperationException("The public quest-session-kit catalog did not expose any non-browser Quest apps.");
        }

        if (string.IsNullOrWhiteSpace(token) || MatchesStudyToken(token))
        {
            return apps.FirstOrDefault(app => string.Equals(app.Id, DefaultStudyId, StringComparison.OrdinalIgnoreCase))
                   ?? apps.FirstOrDefault(app => string.Equals(app.PackageId, "com.tillh.dynamicoscillatorypatternentrainment", StringComparison.OrdinalIgnoreCase))
                   ?? apps[0];
        }

        return apps.FirstOrDefault(app =>
                   string.Equals(app.Id, token, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(app.PackageId, token, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(app.Label, token, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"The public quest-session-kit catalog did not contain an app matching '{studyToken}'.");
    }

    private static DeviceProfile ResolveDeviceProfile(
        QuestSessionKitCatalog catalog,
        QuestAppTarget app,
        string? preferredDeviceProfileId)
    {
        if (!string.IsNullOrWhiteSpace(preferredDeviceProfileId))
        {
            var explicitMatch = catalog.DeviceProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, preferredDeviceProfileId, StringComparison.OrdinalIgnoreCase));
            if (explicitMatch is not null)
            {
                return explicitMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(app.VerificationBaseline?.DeviceProfileId))
        {
            var verificationMatch = catalog.DeviceProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, app.VerificationBaseline.DeviceProfileId, StringComparison.OrdinalIgnoreCase));
            if (verificationMatch is not null)
            {
                return verificationMatch;
            }
        }

        return catalog.DeviceProfiles.FirstOrDefault(profile =>
                   string.Equals(profile.Id, DefaultBalancedDeviceProfileId, StringComparison.OrdinalIgnoreCase))
               ?? catalog.DeviceProfiles.FirstOrDefault()
               ?? new DeviceProfile(
                   DefaultBalancedDeviceProfileId,
                   "DOPE Projected Feed Balanced",
                   "Balanced Quest defaults for the public projected-feed Colorama runtime.",
                   new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static string ResolveApkPath(string rootPath, QuestAppTarget app)
    {
        if (string.IsNullOrWhiteSpace(app.ApkFile))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(app.ApkFile))
        {
            return Path.GetFullPath(app.ApkFile);
        }

        return Path.GetFullPath(Path.Combine(rootPath, "APKs", app.ApkFile));
    }
}
