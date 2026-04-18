using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DopeCompanion.Core.Models;

namespace DopeCompanion.Core.Services;

public sealed record DopeFullDiagnosticHarnessRequest(
    StudyShellDefinition Study,
    HotloadProfile? SceneProfile = null,
    string? DeviceSelector = null,
    string? OutputDirectory = null,
    string? OperatorBuildSummary = null,
    string? PdfScriptPath = null,
    TimeSpan? ProbeWaitDuration = null,
    bool EnsureManagedTooling = true,
    bool AttemptWifiBootstrap = true,
    bool GeneratePdf = true,
    bool CaptureQuestScreenshot = true,
    bool RunCommandAcceptanceCheck = true,
    bool IncludeLslTwinChecks = true);

public sealed record DopeFullDiagnosticHarnessStep(
    string Id,
    string Label,
    OperationOutcomeKind Level,
    string Summary,
    string Detail);

public sealed record DopeFullDiagnosticHarnessManifest(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string StudyId,
    string StudyLabel,
    string PackageId,
    string OperatorBuildSummary,
    string OperatorDataRoot,
    string ManagedToolingRoot,
    string DeviceSelector,
    string? SceneProfileId,
    string? SceneProfileLabel,
    OfficialQuestToolingStatus ToolingStatus,
    string DiagnosticsReportDirectory,
    string DiagnosticsJsonPath,
    string DiagnosticsTexPath,
    string DiagnosticsPdfPath,
    OperationOutcomeKind DiagnosticsPdfLevel,
    string DiagnosticsPdfSummary,
    string DiagnosticsPdfDetail,
    string? QuestScreenshotPath,
    IReadOnlyList<DopeFullDiagnosticHarnessStep> Steps,
    DopeDiagnosticsReport? DiagnosticsReport,
    OperationOutcomeKind Level,
    string Summary,
    string Detail);

public sealed record DopeFullDiagnosticHarnessResult(
    OperationOutcomeKind Level,
    string Summary,
    string Detail,
    string ReportDirectory,
    string SummaryTextPath,
    string SummaryJsonPath,
    string BundlePath,
    string DiagnosticsJsonPath,
    string DiagnosticsTexPath,
    string DiagnosticsPdfPath,
    string? QuestScreenshotPath,
    string DeviceSelector,
    OfficialQuestToolingStatus ToolingStatus,
    OperationOutcomeKind DiagnosticsPdfLevel,
    string DiagnosticsPdfSummary,
    string DiagnosticsPdfDetail,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<DopeFullDiagnosticHarnessStep> Steps,
    DopeDiagnosticsReportResult? DiagnosticsReportResult,
    DopeFullDiagnosticHarnessManifest Manifest);

public sealed class DopeFullDiagnosticHarnessService
{
    public const string DefaultBaselineSceneProfileId = "dope_projected_feed_colorama_baseline";
    private const string HarnessSchemaVersion = "2026-04-18.dope-full-diagnostic-harness.v1";
    private static readonly JsonSerializerOptions HarnessJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IQuestControlService _questService;
    private readonly IHzdbService _hzdbService;
    private readonly WindowsEnvironmentAnalysisService _windowsEnvironmentAnalysisService;
    private readonly ILslStreamDiscoveryService _streamDiscoveryService;
    private readonly ITestLslSignalService _testSignalService;
    private readonly ITwinModeBridge _twinBridge;
    private readonly Func<OfficialQuestToolingService> _toolingServiceFactory;

    public DopeFullDiagnosticHarnessService(
        IQuestControlService questService,
        IHzdbService hzdbService,
        WindowsEnvironmentAnalysisService windowsEnvironmentAnalysisService,
        ILslStreamDiscoveryService streamDiscoveryService,
        ITestLslSignalService testSignalService,
        ITwinModeBridge twinBridge,
        Func<OfficialQuestToolingService>? toolingServiceFactory = null)
    {
        _questService = questService ?? throw new ArgumentNullException(nameof(questService));
        _hzdbService = hzdbService ?? throw new ArgumentNullException(nameof(hzdbService));
        _windowsEnvironmentAnalysisService = windowsEnvironmentAnalysisService ?? throw new ArgumentNullException(nameof(windowsEnvironmentAnalysisService));
        _streamDiscoveryService = streamDiscoveryService ?? throw new ArgumentNullException(nameof(streamDiscoveryService));
        _testSignalService = testSignalService ?? throw new ArgumentNullException(nameof(testSignalService));
        _twinBridge = twinBridge ?? throw new ArgumentNullException(nameof(twinBridge));
        _toolingServiceFactory = toolingServiceFactory ?? (() => new OfficialQuestToolingService());
    }

    public async Task<DopeFullDiagnosticHarnessResult> GenerateAsync(
        DopeFullDiagnosticHarnessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Study);

        var reportDirectory = ResolveReportDirectory(request.Study, request.OutputDirectory);
        Directory.CreateDirectory(reportDirectory);

        var summaryTextPath = Path.Combine(reportDirectory, "dope_full_diagnostic_harness.txt");
        var summaryJsonPath = Path.Combine(reportDirectory, "dope_full_diagnostic_harness.json");
        var bundlePath = Path.Combine(reportDirectory, "dope_full_diagnostic_harness_bundle.zip");

        var target = StudyShellOperatorBindings.CreateQuestTarget(request.Study);
        var deviceProfile = StudyShellOperatorBindings.CreateDeviceProfile(request.Study);
        var steps = new List<DopeFullDiagnosticHarnessStep>();
        var currentSelector = request.DeviceSelector?.Trim() ?? string.Empty;
        var toolingStatus = BuildDefaultToolingStatus();
        var pdfOutcome = new OperationOutcome(
            OperationOutcomeKind.Preview,
            "Diagnostics PDF not generated yet.",
            "The full diagnostic harness has not reached the diagnostics PDF stage.");
        DopeDiagnosticsReportResult? diagnosticsReportResult = null;
        string? questScreenshotPath = null;
        HeadsetAppStatus? latestHeadsetStatus = null;
        var completedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            toolingStatus = await EnsureManagedToolingAsync(request, steps, cancellationToken).ConfigureAwait(false);
            currentSelector = await EnsureConnectedSelectorAsync(target, request, steps, currentSelector, cancellationToken).ConfigureAwait(false);
            latestHeadsetStatus = await QueryHeadsetStatusSafeAsync(target, cancellationToken).ConfigureAwait(false);

            var connectedOutcome = BuildConnectionVerificationOutcome(latestHeadsetStatus, currentSelector);
            steps.Add(ToStep("quest-connection", "Quest Connection", connectedOutcome));
            if (!string.IsNullOrWhiteSpace(connectedOutcome.Endpoint))
            {
                currentSelector = connectedOutcome.Endpoint!;
            }
            else if (latestHeadsetStatus?.IsConnected == true && !string.IsNullOrWhiteSpace(latestHeadsetStatus.ConnectionLabel))
            {
                currentSelector = latestHeadsetStatus.ConnectionLabel;
            }

            if (latestHeadsetStatus?.IsConnected == true)
            {
                var wakeOutcome = await _questService
                    .RunUtilityAsync(QuestUtilityAction.Wake, allowWakeResumeTarget: true, cancellationToken)
                    .ConfigureAwait(false);
                steps.Add(ToStep("wake-headset", "Wake Headset", wakeOutcome));

                var installOutcome = await _questService.InstallAppAsync(target, cancellationToken).ConfigureAwait(false);
                steps.Add(ToStep("install-apk", "Install Pinned APK", installOutcome));

                var profileOutcome = await _questService.ApplyDeviceProfileAsync(deviceProfile, cancellationToken).ConfigureAwait(false);
                steps.Add(ToStep("apply-device-profile", "Apply Device Profile", profileOutcome));

                if (request.SceneProfile is not null)
                {
                    var hotloadOutcome = await _questService
                        .ApplyHotloadProfileAsync(request.SceneProfile, target, cancellationToken)
                        .ConfigureAwait(false);
                    steps.Add(ToStep("apply-scene-profile", "Stage Scene Profile", hotloadOutcome));
                }
                else
                {
                    steps.Add(new DopeFullDiagnosticHarnessStep(
                        "apply-scene-profile",
                        "Stage Scene Profile",
                        OperationOutcomeKind.Preview,
                        "No scene profile was requested for the harness.",
                        "The full harness can stage a bundled projected-feed Colorama CSV before launch, but this run skipped that step."));
                }

                var launchOutcome = await _questService
                    .LaunchAppAsync(target, request.Study.App.LaunchInKioskMode, cancellationToken)
                    .ConfigureAwait(false);
                steps.Add(ToStep("launch-pinned-app", "Launch Pinned App", launchOutcome));

                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                latestHeadsetStatus = await QueryHeadsetStatusSafeAsync(target, cancellationToken).ConfigureAwait(false);
                steps.Add(ToStep(
                    "verify-foreground",
                    "Verify Foreground Runtime",
                    BuildForegroundVerificationOutcome(target, latestHeadsetStatus)));

                if (request.CaptureQuestScreenshot)
                {
                    var screenshotSelector = ResolvePreferredSelector(currentSelector, latestHeadsetStatus?.ConnectionLabel);
                    if (string.IsNullOrWhiteSpace(screenshotSelector))
                    {
                        steps.Add(new DopeFullDiagnosticHarnessStep(
                            "quest-screenshot",
                            "Capture Quest Screenshot",
                            OperationOutcomeKind.Warning,
                            "Quest screenshot capture skipped.",
                            "No usable Quest selector was available after the launch step."));
                    }
                    else
                    {
                        questScreenshotPath = Path.Combine(reportDirectory, "quest_launch_proof.png");
                        var screenshotOutcome = await _hzdbService
                            .CaptureScreenshotAsync(screenshotSelector, questScreenshotPath, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                        if (screenshotOutcome.Kind == OperationOutcomeKind.Failure && !File.Exists(questScreenshotPath))
                        {
                            questScreenshotPath = null;
                        }

                        steps.Add(ToStep("quest-screenshot", "Capture Quest Screenshot", screenshotOutcome));
                    }
                }
                else
                {
                    steps.Add(new DopeFullDiagnosticHarnessStep(
                        "quest-screenshot",
                        "Capture Quest Screenshot",
                        OperationOutcomeKind.Preview,
                        "Quest screenshot capture skipped by request.",
                        "This run did not request a Quest proof screenshot."));
                }

                try
                {
                    var diagnosticsService = new DopeDiagnosticsReportService(
                        _questService,
                        _windowsEnvironmentAnalysisService,
                        _streamDiscoveryService,
                        _testSignalService,
                        _twinBridge);
                    diagnosticsReportResult = await diagnosticsService
                        .GenerateAsync(
                            new DopeDiagnosticsReportRequest(
                                request.Study,
                                DeviceSelector: ResolvePreferredSelector(currentSelector, latestHeadsetStatus?.ConnectionLabel),
                                OutputDirectory: reportDirectory,
                                ProbeWaitDuration: request.ProbeWaitDuration,
                                RunCommandAcceptanceCheck: request.RunCommandAcceptanceCheck,
                                IncludeLslTwinChecks: request.IncludeLslTwinChecks),
                            cancellationToken)
                        .ConfigureAwait(false);

                    steps.Add(new DopeFullDiagnosticHarnessStep(
                        "diagnostics-report",
                        "Generate Diagnostics Report",
                        diagnosticsReportResult.Level,
                        diagnosticsReportResult.Summary,
                        diagnosticsReportResult.Detail));
                }
                catch (Exception ex)
                {
                    steps.Add(new DopeFullDiagnosticHarnessStep(
                        "diagnostics-report",
                        "Generate Diagnostics Report",
                        OperationOutcomeKind.Failure,
                        "Diagnostics report generation failed.",
                        ex.Message));
                }

                pdfOutcome = diagnosticsReportResult is null
                    ? new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Diagnostics PDF generation skipped.",
                        "The structured diagnostics report did not complete, so the PDF step could not run.")
                    : await GenerateDiagnosticsPdfAsync(
                            diagnosticsReportResult.JsonPath,
                            diagnosticsReportResult.PdfPath,
                            request.GeneratePdf,
                            request.PdfScriptPath)
                        .ConfigureAwait(false);

                steps.Add(ToStep("diagnostics-pdf", "Generate Diagnostics PDF", pdfOutcome));
            }
            else
            {
                steps.AddRange(BuildSkippedQuestExecutionSteps(request.CaptureQuestScreenshot));
                pdfOutcome = new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Diagnostics PDF generation skipped.",
                    "The headset never reached a connected state, so install, launch, and report generation were skipped.");
            }
        }
        catch (Exception ex)
        {
            steps.Add(new DopeFullDiagnosticHarnessStep(
                "fatal",
                "Harness Execution",
                OperationOutcomeKind.Failure,
                "The full diagnostic harness hit an unexpected failure.",
                ex.Message));
        }
        finally
        {
            completedAtUtc = DateTimeOffset.UtcNow;
        }

        var diagnosticsJsonPath = diagnosticsReportResult?.JsonPath ?? Path.Combine(reportDirectory, "dope_lsl_twin_diagnostics.json");
        var diagnosticsTexPath = diagnosticsReportResult?.TexPath ?? Path.Combine(reportDirectory, "dope_lsl_twin_diagnostics.tex");
        var diagnosticsPdfPath = diagnosticsReportResult?.PdfPath ?? Path.Combine(reportDirectory, "dope_lsl_twin_diagnostics.pdf");
        var finalLevel = CombineLevels(steps.Select(step => step.Level));
        var summary = BuildHarnessSummary(finalLevel);
        var detail = BuildHarnessDetail(steps, diagnosticsReportResult, pdfOutcome);
        var manifest = new DopeFullDiagnosticHarnessManifest(
            HarnessSchemaVersion,
            completedAtUtc,
            request.Study.Id,
            request.Study.Label,
            request.Study.App.PackageId,
            request.OperatorBuildSummary ?? "unknown",
            CompanionOperatorDataLayout.RootPath,
            OfficialQuestToolingLayout.RootPath,
            ResolvePreferredSelector(currentSelector, latestHeadsetStatus?.ConnectionLabel),
            request.SceneProfile?.Id,
            request.SceneProfile?.Label,
            toolingStatus,
            reportDirectory,
            diagnosticsJsonPath,
            diagnosticsTexPath,
            diagnosticsPdfPath,
            pdfOutcome.Kind,
            pdfOutcome.Summary,
            pdfOutcome.Detail,
            questScreenshotPath,
            steps,
            diagnosticsReportResult?.Report,
            finalLevel,
            summary,
            detail);

        await File.WriteAllTextAsync(
                summaryJsonPath,
                JsonSerializer.Serialize(manifest, HarnessJsonOptions),
                Encoding.UTF8,
                cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                summaryTextPath,
                RenderHarnessSummary(manifest),
                Encoding.UTF8,
                cancellationToken)
            .ConfigureAwait(false);
        CreateBundle(reportDirectory, bundlePath);

        return new DopeFullDiagnosticHarnessResult(
            finalLevel,
            summary,
            detail,
            reportDirectory,
            summaryTextPath,
            summaryJsonPath,
            bundlePath,
            diagnosticsJsonPath,
            diagnosticsTexPath,
            diagnosticsPdfPath,
            questScreenshotPath,
            manifest.DeviceSelector,
            toolingStatus,
            pdfOutcome.Kind,
            pdfOutcome.Summary,
            pdfOutcome.Detail,
            completedAtUtc,
            steps,
            diagnosticsReportResult,
            manifest);
    }

    private async Task<OfficialQuestToolingStatus> EnsureManagedToolingAsync(
        DopeFullDiagnosticHarnessRequest request,
        ICollection<DopeFullDiagnosticHarnessStep> steps,
        CancellationToken cancellationToken)
    {
        using var tooling = _toolingServiceFactory();
        var localStatus = tooling.GetLocalStatus();
        if (!request.EnsureManagedTooling)
        {
            steps.Add(new DopeFullDiagnosticHarnessStep(
                "managed-tooling",
                "Managed Quest Tooling",
                localStatus.IsReady ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning,
                localStatus.IsReady
                    ? "Managed Quest tooling check skipped because the local tool cache is already present."
                    : "Managed Quest tooling refresh skipped by request.",
                $"Quest control ready: {localStatus.IsReady}. Display cast ready: {localStatus.IsDisplayCastReady}. Root: {OfficialQuestToolingLayout.RootPath}"));
            return localStatus;
        }

        var toolingNeedsRefresh = !localStatus.IsReady || !localStatus.IsDisplayCastReady;
        if (!toolingNeedsRefresh)
        {
            steps.Add(new DopeFullDiagnosticHarnessStep(
                "managed-tooling",
                "Managed Quest Tooling",
                OperationOutcomeKind.Success,
                "Managed Quest tooling is already present.",
                $"hzdb {localStatus.Hzdb.InstalledVersion ?? "n/a"} | Android platform-tools {localStatus.PlatformTools.InstalledVersion ?? "n/a"} | scrcpy {localStatus.Scrcpy.InstalledVersion ?? "n/a"}"));
            return localStatus;
        }

        try
        {
            var installResult = await tooling.InstallOrUpdateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var level = installResult.Status.IsReady
                ? installResult.Status.IsDisplayCastReady
                    ? OperationOutcomeKind.Success
                    : OperationOutcomeKind.Warning
                : OperationOutcomeKind.Failure;
            steps.Add(new DopeFullDiagnosticHarnessStep(
                "managed-tooling",
                "Managed Quest Tooling",
                level,
                installResult.Summary,
                $"{installResult.Detail} Quest control ready: {installResult.Status.IsReady}. Display cast ready: {installResult.Status.IsDisplayCastReady}."));
            return installResult.Status;
        }
        catch (Exception ex)
        {
            var level = localStatus.IsReady ? OperationOutcomeKind.Warning : OperationOutcomeKind.Failure;
            steps.Add(new DopeFullDiagnosticHarnessStep(
                "managed-tooling",
                "Managed Quest Tooling",
                level,
                localStatus.IsReady
                    ? "Managed Quest tooling refresh failed, but existing Quest control tools remain available."
                    : "Managed Quest tooling refresh failed.",
                $"{ex.Message} Quest control ready: {localStatus.IsReady}. Display cast ready: {localStatus.IsDisplayCastReady}."));
            return localStatus;
        }
    }

    private async Task<string> EnsureConnectedSelectorAsync(
        QuestAppTarget target,
        DopeFullDiagnosticHarnessRequest request,
        ICollection<DopeFullDiagnosticHarnessStep> steps,
        string currentSelector,
        CancellationToken cancellationToken)
    {
        var latestHeadsetStatus = await QueryHeadsetStatusSafeAsync(target, cancellationToken).ConfigureAwait(false);
        if (latestHeadsetStatus?.IsConnected == true)
        {
            return ResolvePreferredSelector(currentSelector, latestHeadsetStatus.ConnectionLabel);
        }

        if (LooksLikeTcpSelector(currentSelector))
        {
            var connectOutcome = await _questService.ConnectAsync(currentSelector, cancellationToken).ConfigureAwait(false);
            steps.Add(ToStep("connect-remembered-endpoint", "Connect Saved Endpoint", connectOutcome));
            currentSelector = ResolvePreferredSelector(currentSelector, connectOutcome.Endpoint);
            latestHeadsetStatus = await QueryHeadsetStatusSafeAsync(target, cancellationToken).ConfigureAwait(false);
            if (latestHeadsetStatus?.IsConnected == true)
            {
                return ResolvePreferredSelector(currentSelector, latestHeadsetStatus.ConnectionLabel);
            }
        }

        var discoverOutcome = await _questService.DiscoverWifiAsync(cancellationToken).ConfigureAwait(false);
        steps.Add(ToStep("discover-wifi", "Find Wi-Fi Quest", discoverOutcome));
        currentSelector = ResolvePreferredSelector(currentSelector, discoverOutcome.Endpoint);
        latestHeadsetStatus = await QueryHeadsetStatusSafeAsync(target, cancellationToken).ConfigureAwait(false);
        if (latestHeadsetStatus?.IsConnected == true)
        {
            return ResolvePreferredSelector(currentSelector, latestHeadsetStatus.ConnectionLabel);
        }

        var probeOutcome = await _questService.ProbeUsbAsync(cancellationToken).ConfigureAwait(false);
        steps.Add(ToStep("probe-usb", "Probe USB", probeOutcome));
        currentSelector = ResolvePreferredSelector(currentSelector, probeOutcome.Endpoint);

        if (request.AttemptWifiBootstrap && probeOutcome.Kind != OperationOutcomeKind.Failure)
        {
            var wifiBootstrapOutcome = await _questService.EnableWifiFromUsbAsync(cancellationToken).ConfigureAwait(false);
            steps.Add(ToStep("enable-wifi-adb", "Enable Wi-Fi ADB", wifiBootstrapOutcome));
            currentSelector = ResolvePreferredSelector(currentSelector, wifiBootstrapOutcome.Endpoint);
        }

        latestHeadsetStatus = await QueryHeadsetStatusSafeAsync(target, cancellationToken).ConfigureAwait(false);
        return ResolvePreferredSelector(currentSelector, latestHeadsetStatus?.ConnectionLabel);
    }

    private static IEnumerable<DopeFullDiagnosticHarnessStep> BuildSkippedQuestExecutionSteps(bool captureQuestScreenshot)
    {
        yield return new DopeFullDiagnosticHarnessStep(
            "wake-headset",
            "Wake Headset",
            OperationOutcomeKind.Preview,
            "Wake step skipped.",
            "The headset never reached a connected state, so the wake normalization step did not run.");
        yield return new DopeFullDiagnosticHarnessStep(
            "install-apk",
            "Install Pinned APK",
            OperationOutcomeKind.Preview,
            "APK install step skipped.",
            "The headset never reached a connected state, so the pinned APK was not reinstalled.");
        yield return new DopeFullDiagnosticHarnessStep(
            "apply-device-profile",
            "Apply Device Profile",
            OperationOutcomeKind.Preview,
            "Device profile step skipped.",
            "The headset never reached a connected state, so the curated Quest device profile was not applied.");
        yield return new DopeFullDiagnosticHarnessStep(
            "apply-scene-profile",
            "Stage Scene Profile",
            OperationOutcomeKind.Preview,
            "Scene profile step skipped.",
            "The headset never reached a connected state, so the public projected-feed Colorama scene profile was not staged.");
        yield return new DopeFullDiagnosticHarnessStep(
            "launch-pinned-app",
            "Launch Pinned App",
            OperationOutcomeKind.Preview,
            "Launch step skipped.",
            "The headset never reached a connected state, so the public DOPE runtime was not launched.");
        yield return new DopeFullDiagnosticHarnessStep(
            "verify-foreground",
            "Verify Foreground Runtime",
            OperationOutcomeKind.Preview,
            "Foreground verification skipped.",
            "The headset never reached a connected state, so no post-launch foreground verification was available.");

        yield return new DopeFullDiagnosticHarnessStep(
            "quest-screenshot",
            "Capture Quest Screenshot",
            OperationOutcomeKind.Preview,
            captureQuestScreenshot
                ? "Quest screenshot capture skipped."
                : "Quest screenshot capture skipped by request.",
            "The headset never reached a connected state, so the Quest launch proof image was not collected.");

        yield return new DopeFullDiagnosticHarnessStep(
            "diagnostics-report",
            "Generate Diagnostics Report",
            OperationOutcomeKind.Preview,
            "Diagnostics report generation skipped.",
            "The headset never reached a connected state, so the structured diagnostics report could not run.");
        yield return new DopeFullDiagnosticHarnessStep(
            "diagnostics-pdf",
            "Generate Diagnostics PDF",
            OperationOutcomeKind.Preview,
            "Diagnostics PDF generation skipped.",
            "The structured diagnostics report did not run, so the PDF step could not run either.");
    }

    private static OperationOutcome BuildConnectionVerificationOutcome(HeadsetAppStatus? status, string selector)
    {
        if (status is null)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Quest connection could not be verified.",
                "The companion could not read live headset status. Connect the headset over USB, accept the developer-mode debugging prompt, and rerun the harness.",
                Endpoint: selector);
        }

        if (!status.IsConnected)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Quest connection could not be established.",
                $"{status.Summary} {status.Detail}".Trim(),
                Endpoint: ResolvePreferredSelector(selector, status.ConnectionLabel));
        }

        return new OperationOutcome(
            OperationOutcomeKind.Success,
            $"Connected to Quest at {ResolvePreferredSelector(selector, status.ConnectionLabel)}.",
            $"{status.Summary} {status.Detail}".Trim(),
            Endpoint: ResolvePreferredSelector(selector, status.ConnectionLabel));
    }

    private static OperationOutcome BuildForegroundVerificationOutcome(QuestAppTarget target, HeadsetAppStatus? status)
    {
        if (status is null)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Foreground verification could not read live headset status.",
                "The install and launch steps ran, but the post-launch foreground query did not return a status snapshot.");
        }

        var summary = status.IsTargetForeground
            ? $"{target.Label} is foregrounded."
            : status.IsTargetRunning
                ? $"{target.Label} is running, but not foregrounded."
                : status.IsTargetInstalled
                    ? $"{target.Label} is installed, but not running."
                    : $"{target.Label} is not installed on the headset.";
        var level = status.IsTargetForeground
            ? OperationOutcomeKind.Success
            : status.IsTargetRunning || status.IsTargetInstalled
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Failure;
        var detail = $"{status.Summary} Foreground: {status.ForegroundPackageId}. Component: {status.ForegroundComponent}.";
        return new OperationOutcome(level, summary, detail, Endpoint: status.ConnectionLabel, PackageId: target.PackageId);
    }

    private async Task<HeadsetAppStatus?> QueryHeadsetStatusSafeAsync(
        QuestAppTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _questService
                .QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static OfficialQuestToolingStatus BuildDefaultToolingStatus()
        => new(
            new OfficialQuestToolStatus(
                "hzdb",
                "Meta hzdb",
                false,
                null,
                null,
                false,
                OfficialQuestToolingLayout.HzdbExecutablePath,
                OfficialQuestToolingService.MetaHzdbMetadataUri,
                OfficialQuestToolingService.MetaHzdbLicenseSummary,
                OfficialQuestToolingService.MetaHzdbLicenseUri),
            new OfficialQuestToolStatus(
                "platform-tools",
                "Android platform-tools",
                false,
                null,
                null,
                false,
                OfficialQuestToolingLayout.AdbExecutablePath,
                OfficialQuestToolingService.AndroidPlatformToolsRepositoryUri,
                OfficialQuestToolingService.AndroidPlatformToolsLicenseSummary,
                OfficialQuestToolingService.AndroidPlatformToolsLicenseUri),
            new OfficialQuestToolStatus(
                "scrcpy",
                "scrcpy",
                false,
                null,
                null,
                false,
                OfficialQuestToolingLayout.ScrcpyExecutablePath,
                OfficialQuestToolingService.ScrcpyProjectUri,
                OfficialQuestToolingService.ScrcpyLicenseSummary,
                OfficialQuestToolingService.ScrcpyLicenseUri));

    private static string ResolveReportDirectory(StudyShellDefinition study, string? outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Path.GetFullPath(outputDirectory);
        }

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        return Path.Combine(CompanionOperatorDataLayout.DiagnosticsRootPath, study.Id, $"full-harness-{stamp}");
    }

    private static string ResolvePreferredSelector(params string?[] candidates)
        => candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim() ?? string.Empty;

    private static bool LooksLikeTcpSelector(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(':', StringComparison.Ordinal)
           && value.Any(char.IsDigit);

    private static DopeFullDiagnosticHarnessStep ToStep(string id, string label, OperationOutcome outcome)
        => new(
            id,
            label,
            outcome.Kind,
            outcome.Summary,
            outcome.Detail);

    private static OperationOutcomeKind CombineLevels(IEnumerable<OperationOutcomeKind> levels)
    {
        var anySuccess = false;
        var hasPreview = false;
        foreach (var level in levels)
        {
            switch (level)
            {
                case OperationOutcomeKind.Failure:
                    return OperationOutcomeKind.Failure;
                case OperationOutcomeKind.Warning:
                    return OperationOutcomeKind.Warning;
                case OperationOutcomeKind.Success:
                    anySuccess = true;
                    break;
                case OperationOutcomeKind.Preview:
                    hasPreview = true;
                    break;
            }
        }

        return anySuccess
            ? OperationOutcomeKind.Success
            : hasPreview
                ? OperationOutcomeKind.Preview
                : OperationOutcomeKind.Success;
    }

    private static string BuildHarnessSummary(OperationOutcomeKind level)
        => level switch
        {
            OperationOutcomeKind.Failure => "DOPE full diagnostic harness found blocking issues.",
            OperationOutcomeKind.Warning => "DOPE full diagnostic harness completed with issues.",
            OperationOutcomeKind.Preview => "DOPE full diagnostic harness ran in preview mode.",
            _ => "DOPE full diagnostic harness passed."
        };

    private static string BuildHarnessDetail(
        IReadOnlyList<DopeFullDiagnosticHarnessStep> steps,
        DopeDiagnosticsReportResult? diagnosticsReportResult,
        OperationOutcome pdfOutcome)
    {
        var attention = steps
            .Where(step => step.Level is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
            .Select(step => $"{step.Label}: {step.Summary}")
            .ToArray();

        if (attention.Length > 0)
        {
            return string.Join(" ", attention);
        }

        if (diagnosticsReportResult is not null &&
            pdfOutcome.Kind == OperationOutcomeKind.Success)
        {
            return "Managed tooling, Quest connection, APK install, device profile, scene profile, launch, and the shareable diagnostics bundle all completed cleanly.";
        }

        if (diagnosticsReportResult is not null)
        {
            return $"{diagnosticsReportResult.Detail} {pdfOutcome.Summary} {pdfOutcome.Detail}".Trim();
        }

        return "The harness completed without structured diagnostics output, but no blocking issues were recorded in the executed steps.";
    }

    private static string RenderHarnessSummary(DopeFullDiagnosticHarnessManifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine("DOPE Full Diagnostic Harness");
        builder.AppendLine();
        builder.AppendLine($"Generated: {manifest.GeneratedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Overall:   [{RenderLevel(manifest.Level)}] {manifest.Summary}");
        builder.AppendLine($"Detail:    {manifest.Detail}");
        builder.AppendLine();
        builder.AppendLine($"Study:     {manifest.StudyLabel} ({manifest.StudyId})");
        builder.AppendLine($"Package:   {manifest.PackageId}");
        builder.AppendLine($"Selector:  {manifest.DeviceSelector}");
        builder.AppendLine($"Build:     {manifest.OperatorBuildSummary}");
        builder.AppendLine($"Scene:     {(string.IsNullOrWhiteSpace(manifest.SceneProfileLabel) ? "none" : $"{manifest.SceneProfileLabel} ({manifest.SceneProfileId})")}");
        builder.AppendLine();
        builder.AppendLine("Managed Tooling");
        builder.AppendLine($"  hzdb:            {manifest.ToolingStatus.Hzdb.InstalledVersion ?? "n/a"}");
        builder.AppendLine($"  platform-tools:  {manifest.ToolingStatus.PlatformTools.InstalledVersion ?? "n/a"}");
        builder.AppendLine($"  scrcpy:          {manifest.ToolingStatus.Scrcpy.InstalledVersion ?? "n/a"}");
        builder.AppendLine($"  quest control:   {manifest.ToolingStatus.IsReady}");
        builder.AppendLine($"  display cast:    {manifest.ToolingStatus.IsDisplayCastReady}");
        builder.AppendLine();
        builder.AppendLine("Artifacts");
        builder.AppendLine($"  report dir:      {manifest.DiagnosticsReportDirectory}");
        builder.AppendLine($"  json:            {manifest.DiagnosticsJsonPath}");
        builder.AppendLine($"  tex:             {manifest.DiagnosticsTexPath}");
        builder.AppendLine($"  pdf:             {manifest.DiagnosticsPdfPath}");
        builder.AppendLine($"  pdf status:      [{RenderLevel(manifest.DiagnosticsPdfLevel)}] {manifest.DiagnosticsPdfSummary}");
        if (!string.IsNullOrWhiteSpace(manifest.DiagnosticsPdfDetail))
        {
            builder.AppendLine($"                   {manifest.DiagnosticsPdfDetail}");
        }

        if (!string.IsNullOrWhiteSpace(manifest.QuestScreenshotPath))
        {
            builder.AppendLine($"  screenshot:      {manifest.QuestScreenshotPath}");
        }

        builder.AppendLine();
        builder.AppendLine("Steps");
        foreach (var step in manifest.Steps)
        {
            builder.AppendLine($"  [{RenderLevel(step.Level)}] {step.Label}: {step.Summary}");
            if (!string.IsNullOrWhiteSpace(step.Detail))
            {
                builder.AppendLine($"      {step.Detail}");
            }
        }

        return builder.ToString();
    }

    private static string RenderLevel(OperationOutcomeKind level)
        => level switch
        {
            OperationOutcomeKind.Success => "OK",
            OperationOutcomeKind.Warning => "WARN",
            OperationOutcomeKind.Failure => "FAIL",
            _ => "INFO"
        };

    private static void CreateBundle(string reportDirectory, string bundlePath)
    {
        if (File.Exists(bundlePath))
        {
            File.Delete(bundlePath);
        }

        using var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(reportDirectory, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(bundlePath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(reportDirectory, file);
            archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
        }
    }

    private static async Task<OperationOutcome> GenerateDiagnosticsPdfAsync(
        string inputJsonPath,
        string outputPdfPath,
        bool generatePdf,
        string? scriptPath)
    {
        if (!generatePdf)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Preview,
                "Diagnostics PDF generation skipped by request.",
                "The harness still wrote the JSON and LaTeX artifacts, but this run did not request PDF generation.");
        }

        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Diagnostics PDF generator was not found.",
                "JSON and LaTeX reports were written, but the bundled PDF script could not be resolved.");
        }

        var attempts = new[]
        {
            ("py", new[] { "-3", scriptPath }),
            ("python", new[] { scriptPath })
        };

        foreach (var (exe, prefixArgs) in attempts)
        {
            try
            {
                var startInfo = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                foreach (var arg in prefixArgs)
                {
                    startInfo.ArgumentList.Add(arg);
                }

                startInfo.ArgumentList.Add("--input-json");
                startInfo.ArgumentList.Add(inputJsonPath);
                startInfo.ArgumentList.Add("--output-pdf");
                startInfo.ArgumentList.Add(outputPdfPath);

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    continue;
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var completed = await Task.Run(() => process.WaitForExit(45000)).ConfigureAwait(false);
                if (!completed)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    continue;
                }

                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);
                if (process.ExitCode == 0 && File.Exists(outputPdfPath))
                {
                    return new OperationOutcome(
                        OperationOutcomeKind.Success,
                        "Diagnostics PDF generated.",
                        string.IsNullOrWhiteSpace(output)
                            ? outputPdfPath
                            : $"{outputPdfPath} {output}".Trim());
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    return new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Diagnostics PDF generation failed.",
                        error.Trim());
                }
            }
            catch
            {
            }
        }

        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            "Diagnostics PDF generation failed.",
            "The JSON and LaTeX diagnostics reports were written, but no Python runtime completed the bundled PDF script.");
    }
}
