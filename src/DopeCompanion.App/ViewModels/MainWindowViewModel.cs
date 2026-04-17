using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;

namespace DopeCompanion.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly TwinInspectorScope[] TwinInspectorScopeCatalog =
    [
        new("routing", "Routing + Inputs", "Future public routing and scene-selection keys reported by a DOPE runtime."),
        new("headset", "Headset Policy", "Quest CPU/GPU, display refresh, and foveation values that shape device-side behavior."),
        new("runtime", "APK Runtime", "Projected-feed scene parameters and other runtime values currently reported by the APK."),
        new("twin", "Twin + Timing", "Twin apply policy and future DOPE live-sync bridge values."),
        new("state", "App State", "Foreground package, runtime state, and session metadata coming back from quest_twin_state."),
        new("all", "All Public Keys", "Every requested or reported key currently visible to the public twin bridge.")
    ];

    private readonly QuestSessionKitCatalogLoader _catalogLoader = new();
    private readonly StudyShellCatalogLoader _studyShellCatalogLoader = new();
    private AppSessionState _sessionState;
    private readonly IQuestControlService _questService;
    private readonly IHzdbService _hzdbService = HzdbServiceFactory.CreateDefault();
    private readonly ILslMonitorService _monitorService = LslMonitorServiceFactory.CreateDefault();
    private readonly ILslStreamDiscoveryService _lslStreamDiscoveryService = LslStreamDiscoveryServiceFactory.CreateDefault();
    private readonly IStudyClockAlignmentService _clockAlignmentService = StudyClockAlignmentServiceFactory.CreateDefault();
    private readonly ITestLslSignalService _testLslSignalService = TestLslSignalServiceFactory.CreateDefault();
    private readonly ITwinModeBridge _twinBridge = TwinModeBridgeFactory.CreateShared();
    private readonly WindowsEnvironmentAnalysisService _windowsEnvironmentAnalysisService;
    private readonly QuestWifiTransportDiagnosticsService _questWifiTransportDiagnosticsService = new();
    private readonly SessionManifestWriter _manifestWriter = new();
    private readonly RuntimeConfigWorkspaceViewModel _runtimeConfig = new();
    private readonly RuntimeConfigWriter _runtimeConfigWriter = new();
    private readonly DopeParticleSizeTuningCompiler _dopeParticleSizeTuningCompiler = new();
    private readonly LocalAgentWorkspaceService _localAgentWorkspaceService = new();
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, string> _apkOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TwinModeCommand> _twinCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["twin-start"] = new TwinModeCommand("twin-start", "Start twin session"),
        ["twin-pause"] = new TwinModeCommand("twin-pause", "Pause twin session"),
        ["twin-resume"] = new TwinModeCommand("twin-resume", "Resume twin session"),
        ["twin-marker"] = new TwinModeCommand("twin-marker", "Send marker"),
        ["twin-reset"] = new TwinModeCommand("twin-reset", "Reset twin state")
    };

    private CancellationTokenSource? _monitorCts;
    private IReadOnlyList<HotloadProfile> _hotloadProfiles = Array.Empty<HotloadProfile>();
    private bool _initialized;
    private string _catalogStatus = "Loading Quest Session Kit...";
    private string _catalogSourcePath = string.Empty;
    private string _studyShellCatalogStatus = "Loading scene workspaces...";
    private string _studyShellCatalogSourcePath = string.Empty;
    private StudyShellLaunchOptions _studyShellLaunchOptions = new(string.Empty, false);
    private bool _startupStudyAutoOpened;
    private StudyShellViewModel? _activeStudyShell;
    private bool _isStudyBannerExpanded = true;
    private string _endpointDraft = string.Empty;
    private string _browserUrlDraft = "https://mesmerprism.com/DopeCompanion/";
    private string _connectionSummary = "No Quest endpoint action has run yet.";
    private string _usbSummary = "USB ADB probe has not run yet.";
    private string _foregroundSummary = "Active app query has not run yet.";
    private string _selectedAppApkPath = string.Empty;
    private string _cpuLevelText = "2";
    private string _gpuLevelText = "2";
    private bool _remoteOnlyControlEnabled = true;
    private string _headsetStatusSummary = "Headset status not queried yet.";
    private string _headsetStatusDetail = "Connect to Quest to start live status polling.";
    private string _headsetModel = "Unknown";
    private string _headsetBatteryLabel = "Battery n/a";
    private string _headsetSoftwareVersionLabel = "Headset OS n/a";
    private string _headsetPerformanceLabel = "CPU n/a / GPU n/a";
    private string _headsetForegroundPackage = "Foreground n/a";
    private string _headsetVisibleActivities = "Visible activities n/a";
    private string _headsetActivityLabel = "Headset activity unknown";
    private string _headsetActivityDetail = "No live headset activity is available yet.";
    private string _headsetTargetStatusLabel = "Waiting for headset check.";
    private string _deviceSnapshotAgeLabel = "Device snapshot pending.";
    private string _hzdbStatusSummary = "hzdb has not been queried yet.";
    private string _hzdbStatusDetail = "Use Refresh Device Snapshot to collect extra device details when hzdb is available.";
    private string _monitorSummary = "LSL monitor idle.";
    private string _monitorDetail = "Use quest_monitor / quest.telemetry to follow the live Quest telemetry outlet.";
    private string _lslRuntimeSummary = "liblsl runtime not checked yet.";
    private string _lslRuntimeDetail = "The packaged Windows runtime path will appear here once the desktop monitor initializes.";
    private float _monitorValue;
    private float _monitorSampleRateHz;
    private string _monitorStreamName = "quest_monitor";
    private string _monitorStreamType = "quest.telemetry";
    private string _monitorChannelIndexText = "0";
    private string _twinBridgeSummary;
    private string _twinBridgeDetail;
    private string _twinAppStateSummary = "No live app-state frames received yet.";
    private string _twinAppStateDetail = "Once the APK publishes quest_twin_state, reported values and raw frames will appear here.";
    private string _liveTwinPublisherLabel = "No live twin publisher detected.";
    private string _liveTwinPublisherDetail = "Once an APK publishes quest_twin_state, its package and runtime state will appear here.";
    private string _lastTwinStateTimestampLabel = "No LSL app-state timestamp yet.";
    private string _lastActionLabel = "None";
    private string _lastActionDetail = "No operator action has run yet.";
    private string _latestManifestPath = "No manifest written yet.";
    private DopeParticleSizeTuningDocument? _dopeParticleSizeTuningDocument;
    private string _dopeParticleTuningSummary = "Import a Dope particle-size V1 JSON file to compile it onto the live Dope runtime baseline.";
    private string _dopeParticleTuningDetail = "The companion validates the partial file, patches only ParticleSizeEnvelopeLimits.x and .y, stages the full compiled runtime JSON, and then uploads it through the existing hotload path.";
    private string _dopeParticleTuningSourcePath = "No Dope particle-size tuning file imported yet.";
    private string _dopeParticleTuningValuesLabel = "No Dope particle-size values imported yet.";
    private string _dopeParticleTuningBaselineSummary = "Live Dope runtime JSON baseline unavailable.";
    private string _dopeParticleTuningTemplatePath = "Bundled Dope particle-size template not found.";
    private string _dopeParticleTuningCompiledPath = "No compiled Dope particle-size hotload CSV written yet.";
    private OperationOutcomeKind _dopeParticleTuningLevel = OperationOutcomeKind.Preview;
    private bool _isQuestConnected;
    private int _batteryPercent;
    private OperationOutcomeKind _lastActionKind = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _targetStatusLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _foregroundStatusLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _headsetActivityLevel = OperationOutcomeKind.Preview;
    private bool _isForegroundMismatch;
    private string _foregroundMismatchLabel = string.Empty;
    private string _selectedAppPipelineLabel = string.Empty;
    private bool _isSelectedAppIncompatible;
    private string _incompatibleBannerLabel = string.Empty;
    private string? _activeForegroundPackageId;
    private string? _activeForegroundComponent;
    private string? _liveTwinPublisherPackageId;
    private IReadOnlyList<TwinSettingsDelta> _latestTwinDeltas = Array.Empty<TwinSettingsDelta>();
    private TwinInspectorScope? _selectedTwinInspectorScope;
    private TwinInspectorRow? _selectedTwinInspectorRow;
    private QuestAppTarget? _selectedApp;
    private QuestBundle? _selectedBundle;
    private HotloadProfile? _selectedHotloadProfile;
    private DeviceProfile? _selectedDeviceProfile;
    private readonly DispatcherTimer? _twinRefreshTimer;
    private bool _twinRefreshPending;
    private OperationOutcomeKind _twinBridgeLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _twinPublisherLevel = OperationOutcomeKind.Preview;
    private OperationOutcomeKind _twinAppStateLevel = OperationOutcomeKind.Preview;
    private int _selectedTabIndex;
    private HeadsetAppStatus? _latestHeadsetStatus;
    private OperationOutcomeKind _windowsEnvironmentAnalysisLevel = OperationOutcomeKind.Preview;
    private string _windowsEnvironmentAnalysisSummary = "No Windows environment analysis has been run yet.";
    private string _windowsEnvironmentAnalysisDetail = "Run Analyze Windows Environment to check local tooling, liblsl, twin transport, and expected upstream sender visibility on this PC.";
    private string _windowsEnvironmentAnalysisTimestampLabel = "Not run yet.";
    private OperationOutcomeKind _deviceProfileDiagnosticsLevel = OperationOutcomeKind.Preview;
    private string _deviceProfileDiagnosticsSummary = "Quest profile diagnostics have not been run yet.";
    private string _deviceProfileDiagnosticsDetail = "Refresh the selected device profile status to compare the intended Quest properties against the current headset values.";
    private string _deviceProfileDiagnosticsTimestampLabel = "Not checked yet.";
    private OperationOutcomeKind _wifiRouterDiagnosticsLevel = OperationOutcomeKind.Preview;
    private string _wifiRouterDiagnosticsSummary = "Wi-Fi router path not checked yet.";
    private string _wifiRouterDiagnosticsDetail = "Refresh the Wi-Fi router diagnostics after connecting the Quest over Wi-Fi ADB to verify the active TCP path and SSID topology.";
    private string _wifiRouterDiagnosticsTimestampLabel = "Not checked yet.";
    private OperationOutcomeKind _diagnosticsReportLevel = OperationOutcomeKind.Preview;
    private string _diagnosticsReportSummary = "No DOPE diagnostics report has been generated yet.";
    private string _diagnosticsReportDetail = "Generate a shareable report when Windows LSL, quest_twin_state, or command acceptance needs to be diagnosed without relying on screenshots.";
    private string _diagnosticsReportTimestampLabel = "Not generated yet.";
    private string _diagnosticsReportFolderPath = string.Empty;
    private string _diagnosticsReportPdfPath = string.Empty;

    public MainWindowViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _sessionState = AppSessionState.Load();
        _questService = QuestControlServiceFactory.CreateDefault(_sessionState.ActiveEndpoint);
        _windowsEnvironmentAnalysisService = new WindowsEnvironmentAnalysisService(
            _monitorService,
            _lslStreamDiscoveryService,
            _clockAlignmentService,
            _testLslSignalService,
            _twinBridge);
        _endpointDraft = _sessionState.ActiveEndpoint ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_sessionState.ActiveEndpoint))
        {
            _connectionSummary = $"Last saved Quest endpoint: {_sessionState.ActiveEndpoint}. Use Find Wi-Fi Quest or Connect Quest to resume.";
        }

        _twinBridgeSummary = _twinBridge.Status.Summary;
        _twinBridgeDetail = _twinBridge.Status.Detail;
        UpdateLslRuntimeState();
        _runtimeConfig.PropertyChanged += OnRuntimeConfigPropertyChanged;
        foreach (var scope in TwinInspectorScopeCatalog)
        {
            TwinInspectorScopes.Add(scope);
        }

        _selectedTwinInspectorScope = TwinInspectorScopes.FirstOrDefault();
        _twinRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _twinRefreshTimer.Tick += OnTwinRefreshTimerTick;

        if (_twinBridge is LslTwinModeBridge lslBridge)
        {
            lslBridge.StateChanged += OnTwinBridgeStateChanged;
            RefreshLiveTwinState();
        }

        RefreshCatalogCommand = new AsyncRelayCommand(RefreshCatalogAsync);
        OpenStudyShellCommand = new AsyncRelayCommand(OpenStudyShellAsync);
        ExitStudyModeCommand = new AsyncRelayCommand(ExitStudyModeAsync);
        ToggleStudyBannerCommand = new AsyncRelayCommand(ToggleStudyBannerAsync);
        ProbeUsbCommand = new AsyncRelayCommand(ProbeUsbAsync);
        DiscoverWifiCommand = new AsyncRelayCommand(DiscoverWifiAsync);
        EnableWifiCommand = new AsyncRelayCommand(EnableWifiAsync);
        ConnectQuestCommand = new AsyncRelayCommand(ConnectQuestAsync);
        InstallAppCommand = new AsyncRelayCommand(InstallSelectedAppAsync);
        InstallBundleCommand = new AsyncRelayCommand(InstallSelectedBundleAsync);
        BrowseApkCommand = new AsyncRelayCommand(BrowseSelectedApkAsync);
        ApplyHotloadCommand = new AsyncRelayCommand(ApplyHotloadAsync);
        ApplyDeviceProfileCommand = new AsyncRelayCommand(ApplyDeviceProfileAsync);
        ApplyPerformanceLevelsCommand = new AsyncRelayCommand(ApplyPerformanceLevelsAsync);
        LaunchAppCommand = new AsyncRelayCommand(LaunchSelectedAppAsync);
        OpenBrowserCommand = new AsyncRelayCommand(OpenBrowserAsync);
        RefreshForegroundCommand = new AsyncRelayCommand(RefreshForegroundAsync);
        RefreshHeadsetStatusCommand = new AsyncRelayCommand(RefreshHeadsetStatusAsync);
        RestartMonitorCommand = new AsyncRelayCommand(RestartMonitorAsync);
        ExportManifestCommand = new AsyncRelayCommand(ExportManifestAsync);
        ExportRuntimeConfigCommand = new AsyncRelayCommand(ExportRuntimeConfigAsync);
        ResetRuntimeConfigCommand = new AsyncRelayCommand(ResetRuntimeConfigAsync);
        ImportDopeParticleTuningCommand = new AsyncRelayCommand(ImportDopeParticleTuningAsync);
        ApplyDopeParticleTuningCommand = new AsyncRelayCommand(ApplyDopeParticleTuningAsync);
        ApplyTwinPresetCommand = new AsyncRelayCommand(ApplyTwinPresetAsync);
        PublishRuntimeConfigCommand = new AsyncRelayCommand(PublishRuntimeConfigAsync);
        SendTwinCommandCommand = new AsyncRelayCommand(SendTwinCommandAsync);
        RunUtilityCommand = new AsyncRelayCommand(RunUtilityAsync);
        AnalyzeWindowsEnvironmentCommand = new AsyncRelayCommand(AnalyzeWindowsEnvironmentAsync);
        RefreshDeviceProfileDiagnosticsCommand = new AsyncRelayCommand(RefreshDeviceProfileDiagnosticsAsync);
        RefreshWifiRouterDiagnosticsCommand = new AsyncRelayCommand(RefreshWifiRouterDiagnosticsAsync);
        GenerateDiagnosticsReportCommand = new AsyncRelayCommand(GenerateDiagnosticsReportAsync);
        OpenDiagnosticsReportFolderCommand = new AsyncRelayCommand(OpenDiagnosticsReportFolderAsync);
        OpenDiagnosticsReportPdfCommand = new AsyncRelayCommand(OpenDiagnosticsReportPdfAsync);
        OpenLocalAgentWorkspaceCommand = new AsyncRelayCommand(OpenLocalAgentWorkspaceAsync);
        RefreshDopeParticleTuningState();
    }

    public ObservableCollection<QuestAppTarget> Apps { get; } = new();

    public ObservableCollection<QuestBundle> Bundles { get; } = new();

    public ObservableCollection<StudyShellDefinition> StudyShells { get; } = new();

    public StudyShellViewModel? ActiveStudyShell
    {
        get => _activeStudyShell;
        private set
        {
            var previous = _activeStudyShell;
            if (SetProperty(ref _activeStudyShell, value))
            {
                if (previous is not null)
                {
                    previous.PropertyChanged -= OnActiveStudyShellPropertyChanged;
                }

                if (value is not null)
                {
                    value.PropertyChanged += OnActiveStudyShellPropertyChanged;
                }

                NotifyStudyModeStateChanged();
            }
        }
    }

    public bool HasActiveStudyShell => ActiveStudyShell is not null;

    public StudyShellDefinition? StartupStudyShell
        => _studyShellLaunchOptions.HasStartupStudy
            ? StudyShells.FirstOrDefault(
                study => string.Equals(study.Id, _studyShellLaunchOptions.StartupStudyId, StringComparison.OrdinalIgnoreCase))
            : null;

    public StudyShellDefinition? FeaturedStudyShell
        => StartupStudyShell ?? (StudyShells.Count > 0 ? StudyShells[0] : null);

    public bool HasFeaturedStudyShell => FeaturedStudyShell is not null;

    public bool HasLockedStartupStudy => _studyShellLaunchOptions.LockToStartupStudy && StartupStudyShell is not null;

    public bool IsStudyModeLocked => HasLockedStartupStudy && ActiveStudyShell is not null;

    public bool CanExitStudyMode => ActiveStudyShell is not null && !HasLockedStartupStudy;

    public bool ShowOperatorHeaderUtilities => !HasLockedStartupStudy;

    public bool ShowStandardOperatorSurface => ActiveStudyShell is null && !HasLockedStartupStudy;

    public bool ShowStudyShellStartupLoading => ActiveStudyShell is null && HasLockedStartupStudy;

    public bool ShowStudyModeAction => ActiveStudyShell is null && FeaturedStudyShell is not null && !HasLockedStartupStudy;

    public bool IsStudyBannerExpanded
    {
        get => _isStudyBannerExpanded;
        set
        {
            if (SetProperty(ref _isStudyBannerExpanded, value))
            {
                OnPropertyChanged(nameof(ShowExpandedStudyBanner));
                OnPropertyChanged(nameof(StudyBannerToggleLabel));
            }
        }
    }

    public bool ShowExpandedStudyBanner => HasActiveStudyShell && IsStudyBannerExpanded;

    public string StudyBannerToggleLabel => IsStudyBannerExpanded ? "Collapse Banner" : "Expand Banner";

    public string FeaturedStudyShellLabel
        => FeaturedStudyShell?.Label ?? "No pinned scene workspace is available.";

    public string FeaturedStudyShellSummary
        => FeaturedStudyShell is null
            ? "This public repo currently uses the standard DOPE operator surface only. No separate locked study shell is configured."
            : $"{FeaturedStudyShell.Partner}. {FeaturedStudyShell.Description}";

    public string FeaturedStudyShellTargetLabel
        => FeaturedStudyShell is null
            ? StudyShellCatalogStatus
            : $"Pinned runtime {FeaturedStudyShell.App.PackageId}. Device profile {FeaturedStudyShell.DeviceProfile.Label}.";

    public string StudyModeHeaderButtonLabel
        => FeaturedStudyShell is null
            ? "Open Scene Workspace"
            : $"Open {FeaturedStudyShell.Label} Workspace";

    public string FeaturedStudyShellActionLabel
        => FeaturedStudyShell is null ? "No workspace configured" : "Open Workspace";

    public string LockedStudyShellStartupLabel
        => StartupStudyShell is null
            ? "Opening dedicated workspace..."
            : $"Opening {StartupStudyShell.Label} workspace...";

    public string LockedStudyShellStartupDetail
        => string.IsNullOrWhiteSpace(StudyShellCatalogStatus)
            ? "The dedicated workspace is loading."
            : $"{StudyShellCatalogStatus} The full operator workspace stays hidden for this package.";

    public ObservableCollection<HotloadProfile> AvailableHotloadProfiles { get; } = new();

    public ObservableCollection<DeviceProfile> DeviceProfiles { get; } = new();

    public ObservableCollection<OperatorLogEntry> Logs { get; } = new();

    public ObservableCollection<TwinSettingsDelta> SettingsDelta { get; } = new();

    public ObservableCollection<KeyValueStatusRow> TwinReportedState { get; } = new();

    public ObservableCollection<TwinStateEvent> TwinLiveEvents { get; } = new();

    public ObservableCollection<TwinInspectorScope> TwinInspectorScopes { get; } = new();

    public ObservableCollection<TwinInspectorRow> TwinInspectorRows { get; } = new();

    public ObservableCollection<WorkflowGuideCheckItem> WindowsEnvironmentChecks { get; } = new();

    public ObservableCollection<StudyStatusRowViewModel> DeviceProfileDiagnosticsRows { get; } = new();

    public ObservableCollection<ActionChoice<QuestUtilityAction>> UtilityActions { get; } = new(
    [
        new ActionChoice<QuestUtilityAction>("Home", "Return to the Quest launcher.", QuestUtilityAction.Home),
        new ActionChoice<QuestUtilityAction>("Back", "Send a back event to the active Quest app.", QuestUtilityAction.Back),
        new ActionChoice<QuestUtilityAction>("List Apps", "Read the Quest package list over ADB.", QuestUtilityAction.ListInstalledPackages),
        new ActionChoice<QuestUtilityAction>("Reboot", "Reboot the Quest from the active ADB session.", QuestUtilityAction.Reboot)
    ]);

    public RuntimeConfigWorkspaceViewModel RuntimeConfig => _runtimeConfig;

    public AsyncRelayCommand RefreshCatalogCommand { get; }

    public AsyncRelayCommand OpenStudyShellCommand { get; }

    public AsyncRelayCommand ExitStudyModeCommand { get; }

    public AsyncRelayCommand ToggleStudyBannerCommand { get; }

    public AsyncRelayCommand ProbeUsbCommand { get; }

    public AsyncRelayCommand DiscoverWifiCommand { get; }

    public AsyncRelayCommand EnableWifiCommand { get; }

    public AsyncRelayCommand ConnectQuestCommand { get; }

    public AsyncRelayCommand InstallAppCommand { get; }

    public AsyncRelayCommand InstallBundleCommand { get; }

    public AsyncRelayCommand BrowseApkCommand { get; }

    public AsyncRelayCommand ApplyHotloadCommand { get; }

    public AsyncRelayCommand ApplyDeviceProfileCommand { get; }

    public AsyncRelayCommand ApplyPerformanceLevelsCommand { get; }

    public AsyncRelayCommand LaunchAppCommand { get; }

    public AsyncRelayCommand OpenBrowserCommand { get; }

    public AsyncRelayCommand RefreshForegroundCommand { get; }

    public AsyncRelayCommand RefreshHeadsetStatusCommand { get; }

    public AsyncRelayCommand RestartMonitorCommand { get; }

    public AsyncRelayCommand ExportManifestCommand { get; }

    public AsyncRelayCommand ExportRuntimeConfigCommand { get; }

    public AsyncRelayCommand ResetRuntimeConfigCommand { get; }

    public AsyncRelayCommand ImportDopeParticleTuningCommand { get; }

    public AsyncRelayCommand ApplyDopeParticleTuningCommand { get; }

    public AsyncRelayCommand ApplyTwinPresetCommand { get; }

    public AsyncRelayCommand PublishRuntimeConfigCommand { get; }

    public AsyncRelayCommand SendTwinCommandCommand { get; }

    public AsyncRelayCommand RunUtilityCommand { get; }

    public AsyncRelayCommand AnalyzeWindowsEnvironmentCommand { get; }

    public AsyncRelayCommand RefreshDeviceProfileDiagnosticsCommand { get; }

    public AsyncRelayCommand RefreshWifiRouterDiagnosticsCommand { get; }

    public AsyncRelayCommand GenerateDiagnosticsReportCommand { get; }

    public AsyncRelayCommand OpenDiagnosticsReportFolderCommand { get; }

    public AsyncRelayCommand OpenDiagnosticsReportPdfCommand { get; }

    public AsyncRelayCommand OpenLocalAgentWorkspaceCommand { get; }

    public string CatalogStatus
    {
        get => _catalogStatus;
        private set => SetProperty(ref _catalogStatus, value);
    }

    public string CatalogSourcePath
    {
        get => _catalogSourcePath;
        private set => SetProperty(ref _catalogSourcePath, value);
    }

    public string StudyShellCatalogStatus
    {
        get => _studyShellCatalogStatus;
        private set
        {
            if (SetProperty(ref _studyShellCatalogStatus, value))
            {
                OnPropertyChanged(nameof(LockedStudyShellStartupDetail));
            }
        }
    }

    public string StudyShellCatalogSourcePath
    {
        get => _studyShellCatalogSourcePath;
        private set
        {
            if (SetProperty(ref _studyShellCatalogSourcePath, value))
            {
                OnPropertyChanged(nameof(LockedStudyShellStartupDetail));
            }
        }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public string EndpointDraft
    {
        get => _endpointDraft;
        set => SetProperty(ref _endpointDraft, value);
    }

    public string BrowserUrlDraft
    {
        get => _browserUrlDraft;
        set => SetProperty(ref _browserUrlDraft, value);
    }

    public string ConnectionSummary
    {
        get => _connectionSummary;
        private set => SetProperty(ref _connectionSummary, value);
    }

    public string UsbSummary
    {
        get => _usbSummary;
        private set => SetProperty(ref _usbSummary, value);
    }

    public string ForegroundSummary
    {
        get => _foregroundSummary;
        private set => SetProperty(ref _foregroundSummary, value);
    }

    public string SelectedAppApkPath
    {
        get => _selectedAppApkPath;
        set
        {
            if (SetProperty(ref _selectedAppApkPath, value) && SelectedApp is not null)
            {
                _apkOverrides[SelectedApp.Id] = value;
            }
        }
    }

    public string CpuLevelText
    {
        get => _cpuLevelText;
        set => SetProperty(ref _cpuLevelText, value);
    }

    public string GpuLevelText
    {
        get => _gpuLevelText;
        set => SetProperty(ref _gpuLevelText, value);
    }

    public bool RemoteOnlyControlEnabled
    {
        get => _remoteOnlyControlEnabled;
        set
        {
            if (SetProperty(ref _remoteOnlyControlEnabled, value))
            {
                OnPropertyChanged(nameof(RemoteControlModeSummary));
            }
        }
    }

    public string HeadsetStatusSummary
    {
        get => _headsetStatusSummary;
        private set => SetProperty(ref _headsetStatusSummary, value);
    }

    public string HeadsetStatusDetail
    {
        get => _headsetStatusDetail;
        private set => SetProperty(ref _headsetStatusDetail, value);
    }

    public string HeadsetModel
    {
        get => _headsetModel;
        private set => SetProperty(ref _headsetModel, value);
    }

    public string HeadsetBatteryLabel
    {
        get => _headsetBatteryLabel;
        private set => SetProperty(ref _headsetBatteryLabel, value);
    }

    public string HeadsetSoftwareVersionLabel
    {
        get => _headsetSoftwareVersionLabel;
        private set => SetProperty(ref _headsetSoftwareVersionLabel, value);
    }

    public string HeadsetPerformanceLabel
    {
        get => _headsetPerformanceLabel;
        private set => SetProperty(ref _headsetPerformanceLabel, value);
    }

    public string HeadsetForegroundPackage
    {
        get => _headsetForegroundPackage;
        private set => SetProperty(ref _headsetForegroundPackage, value);
    }

    public string HeadsetVisibleActivities
    {
        get => _headsetVisibleActivities;
        private set => SetProperty(ref _headsetVisibleActivities, value);
    }

    public string HeadsetActivityLabel
    {
        get => _headsetActivityLabel;
        private set => SetProperty(ref _headsetActivityLabel, value);
    }

    public string HeadsetActivityDetail
    {
        get => _headsetActivityDetail;
        private set => SetProperty(ref _headsetActivityDetail, value);
    }

    public string HeadsetTargetStatusLabel
    {
        get => _headsetTargetStatusLabel;
        private set => SetProperty(ref _headsetTargetStatusLabel, value);
    }

    public string DeviceSnapshotAgeLabel
    {
        get => _deviceSnapshotAgeLabel;
        private set => SetProperty(ref _deviceSnapshotAgeLabel, value);
    }

    public string HzdbStatusSummary
    {
        get => _hzdbStatusSummary;
        private set => SetProperty(ref _hzdbStatusSummary, value);
    }

    public string HzdbStatusDetail
    {
        get => _hzdbStatusDetail;
        private set => SetProperty(ref _hzdbStatusDetail, value);
    }

    public string MonitorSummary
    {
        get => _monitorSummary;
        private set => SetProperty(ref _monitorSummary, value);
    }

    public string MonitorDetail
    {
        get => _monitorDetail;
        private set => SetProperty(ref _monitorDetail, value);
    }

    public string LslRuntimeSummary
    {
        get => _lslRuntimeSummary;
        private set => SetProperty(ref _lslRuntimeSummary, value);
    }

    public string LslRuntimeDetail
    {
        get => _lslRuntimeDetail;
        private set => SetProperty(ref _lslRuntimeDetail, value);
    }

    public float MonitorValue
    {
        get => _monitorValue;
        private set
        {
            if (SetProperty(ref _monitorValue, value))
            {
                OnPropertyChanged(nameof(MonitorValueLabel));
            }
        }
    }

    public float MonitorSampleRateHz
    {
        get => _monitorSampleRateHz;
        private set
        {
            if (SetProperty(ref _monitorSampleRateHz, value))
            {
                OnPropertyChanged(nameof(MonitorRateLabel));
            }
        }
    }

    public string MonitorStreamName
    {
        get => _monitorStreamName;
        set => SetProperty(ref _monitorStreamName, value);
    }

    public string MonitorStreamType
    {
        get => _monitorStreamType;
        set => SetProperty(ref _monitorStreamType, value);
    }

    public string MonitorChannelIndexText
    {
        get => _monitorChannelIndexText;
        set => SetProperty(ref _monitorChannelIndexText, value);
    }

    public string TwinBridgeSummary
    {
        get => _twinBridgeSummary;
        private set => SetProperty(ref _twinBridgeSummary, value);
    }

    public string TwinBridgeDetail
    {
        get => _twinBridgeDetail;
        private set => SetProperty(ref _twinBridgeDetail, value);
    }

    public string TwinAppStateSummary
    {
        get => _twinAppStateSummary;
        private set => SetProperty(ref _twinAppStateSummary, value);
    }

    public string TwinAppStateDetail
    {
        get => _twinAppStateDetail;
        private set => SetProperty(ref _twinAppStateDetail, value);
    }

    public string LiveTwinPublisherLabel
    {
        get => _liveTwinPublisherLabel;
        private set => SetProperty(ref _liveTwinPublisherLabel, value);
    }

    public string LiveTwinPublisherDetail
    {
        get => _liveTwinPublisherDetail;
        private set => SetProperty(ref _liveTwinPublisherDetail, value);
    }

    public string LastTwinStateTimestampLabel
    {
        get => _lastTwinStateTimestampLabel;
        private set => SetProperty(ref _lastTwinStateTimestampLabel, value);
    }

    public string LastActionLabel
    {
        get => _lastActionLabel;
        private set => SetProperty(ref _lastActionLabel, value);
    }

    public string LastActionDetail
    {
        get => _lastActionDetail;
        private set => SetProperty(ref _lastActionDetail, value);
    }

    public string LatestManifestPath
    {
        get => _latestManifestPath;
        private set => SetProperty(ref _latestManifestPath, value);
    }

    public bool IsQuestConnected
    {
        get => _isQuestConnected;
        private set => SetProperty(ref _isQuestConnected, value);
    }

    public string ConnectionStatusLabel
        => IsQuestConnected ? "Quest connected" : "Quest disconnected";

    public int BatteryPercent
    {
        get => _batteryPercent;
        private set => SetProperty(ref _batteryPercent, value);
    }

    public OperationOutcomeKind LastActionKind
    {
        get => _lastActionKind;
        private set => SetProperty(ref _lastActionKind, value);
    }

    public OperationOutcomeKind TargetStatusLevel
    {
        get => _targetStatusLevel;
        private set => SetProperty(ref _targetStatusLevel, value);
    }

    public OperationOutcomeKind ForegroundStatusLevel
    {
        get => _foregroundStatusLevel;
        private set => SetProperty(ref _foregroundStatusLevel, value);
    }

    public OperationOutcomeKind HeadsetActivityLevel
    {
        get => _headsetActivityLevel;
        private set => SetProperty(ref _headsetActivityLevel, value);
    }

    public bool IsForegroundMismatch
    {
        get => _isForegroundMismatch;
        private set => SetProperty(ref _isForegroundMismatch, value);
    }

    public string ForegroundMismatchLabel
    {
        get => _foregroundMismatchLabel;
        private set => SetProperty(ref _foregroundMismatchLabel, value);
    }

    public string SelectedAppPipelineLabel
    {
        get => _selectedAppPipelineLabel;
        private set => SetProperty(ref _selectedAppPipelineLabel, value);
    }

    public bool IsSelectedAppIncompatible
    {
        get => _isSelectedAppIncompatible;
        private set => SetProperty(ref _isSelectedAppIncompatible, value);
    }

    public string IncompatibleBannerLabel
    {
        get => _incompatibleBannerLabel;
        private set => SetProperty(ref _incompatibleBannerLabel, value);
    }

    public QuestAppTarget? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (SetProperty(ref _selectedApp, value))
            {
                ConfigureTwinStateSourceForSelectedApp();
                RefreshAvailableHotloadProfiles();
                RefreshRuntimeConfigProfileSelection();
                RefreshSelectedAppApkPath();
                RefreshPipelineLabel();
                RefreshRuntimeContextLabels();
                RefreshDopeParticleTuningState();
                OnPropertyChanged(nameof(SelectedAppSummary));
                OnPropertyChanged(nameof(SelectedAppCapabilitySummary));
                OnPropertyChanged(nameof(SelectedAppCommunicationSummary));
                OnPropertyChanged(nameof(TargetSelectionHeadline));
                OnPropertyChanged(nameof(TargetSelectionDetail));
                OnPropertyChanged(nameof(RuntimeConfigPublishChannelSummary));
            }
        }
    }

    public QuestBundle? SelectedBundle
    {
        get => _selectedBundle;
        set => SetProperty(ref _selectedBundle, value);
    }

    public HotloadProfile? SelectedHotloadProfile
    {
        get => _selectedHotloadProfile;
        set
        {
            if (SetProperty(ref _selectedHotloadProfile, value))
            {
                _runtimeConfig.SelectProfile(value?.Id);
                OnPropertyChanged(nameof(SelectedHotloadProfileSummary));
            }
        }
    }

    public DeviceProfile? SelectedDeviceProfile
    {
        get => _selectedDeviceProfile;
        set
        {
            if (SetProperty(ref _selectedDeviceProfile, value))
            {
                OnPropertyChanged(nameof(DeviceProfileDiagnosticsLabel));
            }
        }
    }

    public string OperatorDataRootPath => CompanionOperatorDataLayout.RootPath;

    public string ManagedToolingRootPath => OfficialQuestToolingLayout.RootPath;

    public string LocalAgentWorkspacePath => _localAgentWorkspaceService.RootPath;

    public string LocalAgentWorkspaceDetail
        => "Open this host-visible workspace to mirror the bundled CLI, docs, and sample catalogs out of WindowsApps for local-agent or terminal use.";

    public string BundledCliExportPath => LocalAgentWorkspaceLayout.BundledCliExecutablePath;

    public string DeviceProfileDiagnosticsLabel
        => ResolveDiagnosticsDeviceProfile()?.Label ?? "No Quest profile selected.";

    public OperationOutcomeKind WindowsEnvironmentAnalysisLevel
    {
        get => _windowsEnvironmentAnalysisLevel;
        private set => SetProperty(ref _windowsEnvironmentAnalysisLevel, value);
    }

    public string WindowsEnvironmentAnalysisSummary
    {
        get => _windowsEnvironmentAnalysisSummary;
        private set => SetProperty(ref _windowsEnvironmentAnalysisSummary, value);
    }

    public string WindowsEnvironmentAnalysisDetail
    {
        get => _windowsEnvironmentAnalysisDetail;
        private set => SetProperty(ref _windowsEnvironmentAnalysisDetail, value);
    }

    public string WindowsEnvironmentAnalysisTimestampLabel
    {
        get => _windowsEnvironmentAnalysisTimestampLabel;
        private set => SetProperty(ref _windowsEnvironmentAnalysisTimestampLabel, value);
    }

    public OperationOutcomeKind DeviceProfileDiagnosticsLevel
    {
        get => _deviceProfileDiagnosticsLevel;
        private set => SetProperty(ref _deviceProfileDiagnosticsLevel, value);
    }

    public string DeviceProfileDiagnosticsSummary
    {
        get => _deviceProfileDiagnosticsSummary;
        private set => SetProperty(ref _deviceProfileDiagnosticsSummary, value);
    }

    public string DeviceProfileDiagnosticsDetail
    {
        get => _deviceProfileDiagnosticsDetail;
        private set => SetProperty(ref _deviceProfileDiagnosticsDetail, value);
    }

    public string DeviceProfileDiagnosticsTimestampLabel
    {
        get => _deviceProfileDiagnosticsTimestampLabel;
        private set => SetProperty(ref _deviceProfileDiagnosticsTimestampLabel, value);
    }

    public OperationOutcomeKind WifiRouterDiagnosticsLevel
    {
        get => _wifiRouterDiagnosticsLevel;
        private set => SetProperty(ref _wifiRouterDiagnosticsLevel, value);
    }

    public string WifiRouterDiagnosticsSummary
    {
        get => _wifiRouterDiagnosticsSummary;
        private set => SetProperty(ref _wifiRouterDiagnosticsSummary, value);
    }

    public string WifiRouterDiagnosticsDetail
    {
        get => _wifiRouterDiagnosticsDetail;
        private set => SetProperty(ref _wifiRouterDiagnosticsDetail, value);
    }

    public string WifiRouterDiagnosticsTimestampLabel
    {
        get => _wifiRouterDiagnosticsTimestampLabel;
        private set => SetProperty(ref _wifiRouterDiagnosticsTimestampLabel, value);
    }

    public OperationOutcomeKind DiagnosticsReportLevel
    {
        get => _diagnosticsReportLevel;
        private set => SetProperty(ref _diagnosticsReportLevel, value);
    }

    public string DiagnosticsReportSummary
    {
        get => _diagnosticsReportSummary;
        private set => SetProperty(ref _diagnosticsReportSummary, value);
    }

    public string DiagnosticsReportDetail
    {
        get => _diagnosticsReportDetail;
        private set => SetProperty(ref _diagnosticsReportDetail, value);
    }

    public string DiagnosticsReportTimestampLabel
    {
        get => _diagnosticsReportTimestampLabel;
        private set => SetProperty(ref _diagnosticsReportTimestampLabel, value);
    }

    public string DiagnosticsReportFolderPath
    {
        get => _diagnosticsReportFolderPath;
        private set
        {
            if (SetProperty(ref _diagnosticsReportFolderPath, NormalizeHostVisibleOperatorPath(value)))
            {
                OnPropertyChanged(nameof(CanOpenDiagnosticsReportFolder));
            }
        }
    }

    public string DiagnosticsReportPdfPath
    {
        get => _diagnosticsReportPdfPath;
        private set
        {
            if (SetProperty(ref _diagnosticsReportPdfPath, NormalizeHostVisibleOperatorPath(value)))
            {
                OnPropertyChanged(nameof(CanOpenDiagnosticsReportPdf));
            }
        }
    }

    public bool CanOpenDiagnosticsReportFolder
        => CompanionOperatorDataLayout.TryResolveExistingDirectory(DiagnosticsReportFolderPath, out _);

    public bool CanOpenDiagnosticsReportPdf
        => CompanionOperatorDataLayout.TryResolveExistingFile(DiagnosticsReportPdfPath, out _);

    public string MonitorValueLabel => $"{MonitorValue:0.000}";

    public string MonitorRateLabel => $"{MonitorSampleRateHz:0} Hz";

    public OperationOutcomeKind HeaderTargetLevel
        => ActiveStudyShell?.PinnedBuildLevel ?? TargetStatusLevel;

    public OperationOutcomeKind HeaderConnectionLevel
        => ActiveStudyShell?.QuestStatusLevel ?? HeadsetActivityLevel;

    public OperationOutcomeKind HeaderLiveLevel
        => ActiveStudyShell?.LiveRuntimeLevel ?? LiveSignalLevel;

    public OperationOutcomeKind LiveSignalLevel
    {
        get
        {
            if (MonitorSummary.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                return OperationOutcomeKind.Failure;
            }

            if (!string.IsNullOrWhiteSpace(_liveTwinPublisherPackageId) || MonitorSampleRateHz > 0.1f)
            {
                return OperationOutcomeKind.Success;
            }

            return IsQuestConnected ? OperationOutcomeKind.Warning : OperationOutcomeKind.Preview;
        }
    }

    public string LiveSignalHeadline
        => ActiveStudyShell is not null
            ? ActiveStudyShell.LiveRuntimeSummary
            : !string.IsNullOrWhiteSpace(_liveTwinPublisherPackageId)
            ? "Twin-state reporting live."
            : MonitorSampleRateHz > 0.1f
                ? $"Monitor stream live at {MonitorSampleRateHz:0} Hz."
                : "Waiting for live runtime signal.";

    public string LiveSignalDetail
        => ActiveStudyShell is not null
            ? $"{ActiveStudyShell.LiveRuntimeDetail} {ActiveStudyShell.LastTwinStateTimestampLabel}".Trim()
            : !string.IsNullOrWhiteSpace(_liveTwinPublisherPackageId)
            ? $"{LiveTwinPublisherLabel} {LastTwinStateTimestampLabel}"
            : MonitorSampleRateHz > 0.1f
                ? $"{MonitorSummary} {MonitorDetail}"
                : $"{TwinBridgeSummary} {MonitorSummary}";

    public string OnDeviceStatusDetail
        => ActiveStudyShell is not null
            ? $"{ActiveStudyShell.ConnectionSummary}. {ActiveStudyShell.QuestStatusDetail}".Trim()
            : IsForegroundMismatch
            ? ForegroundMismatchLabel
            : string.IsNullOrWhiteSpace(_activeForegroundPackageId)
                ? HeadsetActivityDetail
                : $"{HeadsetForegroundPackage}. {HeadsetActivityDetail}";

    public string SessionHealthSummary
        => ActiveStudyShell is not null
            ? ActiveStudyShell.QuestStatusSummary
            : IsQuestConnected
            ? $"{HeadsetActivityLabel}. {DeviceSnapshotAgeLabel}"
            : "Reconnect the Quest before install, launch, or snapshot actions.";

    public string TargetSelectionHeadline
        => ActiveStudyShell is not null
            ? $"Selected workspace target: {ActiveStudyShell.StudyLabel}"
            : SelectedApp is null
            ? ActiveStudyShell is null
                ? "Selected target: waiting for headset check."
                : $"Selected workspace target: {ActiveStudyShell.StudyLabel}"
            : $"Selected target: {SelectedApp.Label}";

    public string TargetSelectionDetail
        => ActiveStudyShell is not null
            ? $"{ActiveStudyShell.PinnedPackageId}. {ActiveStudyShell.PinnedBuildSummary}"
            : SelectedApp is null
            ? "Refresh Device Snapshot to adopt the current headset app automatically, or choose one manually in Quest Library."
            : $"{SelectedApp.PackageId}. Install, launch, preset staging, and publish actions use this target until you change it.";

    public string PrimaryWorkflowTabLabel
        => ActiveStudyShell is not null
            ? $"{ActiveStudyShell.StudyLabel} Workspace"
            : StartupStudyShell is not null && HasLockedStartupStudy
                ? $"{StartupStudyShell.Label} Workspace"
                : "Start Here";

    public string CurrentModeLabel
        => ActiveStudyShell is not null
            ? $"{ActiveStudyShell.StudyLabel} workspace"
            : ShowStudyShellStartupLoading
                ? "Dedicated scene workspace"
                : "Standard operator mode";

    public string CurrentModeDetail
        => ActiveStudyShell is not null
            ? IsStudyModeLocked
                ? $"{ActiveStudyShell.StudyPartner}. This package stays inside one pinned scene workspace. The full operator tabs and the exit path are hidden for this build."
                : $"{ActiveStudyShell.StudyPartner}. The active workspace is pinned, while the generic tabs stay hidden until you exit the workspace."
            : ShowStudyShellStartupLoading
                ? "This package is configured to open directly into a pinned workspace instead of the general operator surface."
                : "Use Start Here for the public DOPE operator path. This build does not currently split into a second public study-shell package.";

    public string PublishedBuildSummary => AppBuildIdentity.Current.Summary;

    public string PublishedBuildDetail => AppBuildIdentity.Current.Detail;

    public string WindowTitle => $"DOPE Companion ({AppBuildIdentity.Current.ShortId})";

    public string HeaderModeSummary
        => ActiveStudyShell is not null
            ? IsStudyModeLocked
                ? $"{ActiveStudyShell.StudyPartner}. Dedicated workspace pinned to {ActiveStudyShell.PinnedPackageId}."
                : $"{ActiveStudyShell.StudyPartner}. Pinned target {ActiveStudyShell.PinnedPackageId}."
            : ShowStudyShellStartupLoading
                ? $"{FeaturedStudyShellLabel}. Opening the pinned workspace and keeping the general operator surfaces hidden for this package."
                : "Standard DOPE operator workflow. Install the bundled projected-feed Colorama APK, apply a device profile, and stage scene profiles from the runtime-config surface.";

    public string SelectedAppSummary
        => SelectedApp is null
            ? "No target selected yet. Waiting for a headset snapshot or manual selection."
            : $"{SelectedApp.Label} ({SelectedApp.PackageId}) — {SelectedApp.Description}";

    public string SelectedAppCapabilitySummary
        => SelectedApp is null
            ? "Refresh Device Snapshot to adopt the current headset app automatically, or choose one manually from Quest Library."
            : BuildSelectedAppCapabilitySummary(SelectedApp);

    public string SelectedAppCommunicationSummary
        => SelectedApp is null
            ? "No communication path is selected yet because there is no target APK."
            : BuildSelectedAppCommunicationSummary(SelectedApp);

    public string RemoteControlModeSummary
        => RemoteOnlyControlEnabled
            ? "Remote-only mode is enabled. Device snapshots run only on request over ADB/hzdb, while real-time app state is expected to arrive on quest_twin_state."
            : "Remote-only mode is disabled. Device probes are still on-demand only; live APK state should come from the twin LSL bridge rather than repeated ADB polling.";

    public string SelectedHotloadProfileSummary
        => SelectedHotloadProfile is null
            ? SelectedApp is null
                ? "No runtime preset selected because no target app is selected yet."
                : "No runtime preset selected."
            : $"{SelectedHotloadProfile.Label} ({SelectedHotloadProfile.Channel}/{SelectedHotloadProfile.Version}) — {SelectedHotloadProfile.Description}";

    public string RuntimeConfigPublishSummary
        => SelectedApp is null
            ? "No target selected yet. Refresh Device Snapshot to adopt the current headset app automatically, or choose one manually before install, launch, preset staging, or Publish over Twin."
            : SelectedApp.Tags.Any(tag => tag.Equals("runtime", StringComparison.OrdinalIgnoreCase))
                ? $"Selected target: {SelectedApp.Label} ({SelectedApp.PackageId}). Install, launch, preset staging, and Publish over Twin actions use this target even when another app is currently active on the headset."
                : $"Selected target: {SelectedApp.Label} ({SelectedApp.PackageId}). This public bundle is currently staged for install, launch, and CSV export. Live Publish over Twin depends on a future DOPE build that exposes Astral-style runtime-config wiring.";

    public string RuntimeConfigDeviceModeSummary
        => IsQuestConnected
            ? $"ADB/hzdb is on-demand only. Current device snapshot shows {HeadsetActivityLabel}. Use it for install, launch, battery, CPU/GPU, and active-app checks, not for live runtime values."
            : "ADB/hzdb is the on-demand device path. Connect to the headset to read the active app, install state, battery, and performance hints.";

    public string RuntimeConfigPublishChannelSummary
        => SelectedApp is null
            ? "Operator publish is idle until you choose a target app. Runtime config publishes use quest_hotload_config; twin commands use quest_twin_commands."
            : !SelectedApp.Tags.Any(tag => tag.Equals("runtime", StringComparison.OrdinalIgnoreCase))
                ? $"Selected target is {SelectedApp.Label}, but this bundle does not currently advertise live runtime-config publish support. You can still export the projected-feed Colorama CSVs and keep them as the public scene contract."
            : _runtimeConfig.SelectedProfile is null
                ? $"Selected target is {SelectedApp.Label}, but no runtime config profile is selected for it. Choose a profile before publishing on quest_hotload_config."
                : _runtimeConfig.SelectedProfile.MatchesPackage(SelectedApp.PackageId)
                    ? $"Publishing is configured for {SelectedApp.Label}. The selected profile targets this app and is sent on quest_hotload_config; no live values come back on this publish channel."
                    : $"Selected profile targets {FormatPackageTargets(_runtimeConfig.SelectedProfile.PackageIds)}, but the selected target app is {SelectedApp.Label}. In Astral twin mode, keep the publish target on the runtime app whose profile you are editing; the live twin publisher can still be a different active APK.";

    public string RuntimeConfigLiveSummary
        => "Live runtime state is passive. Values arrive on quest_twin_state only. They appear below in Live Runtime Values and in Twin Monitor > Live Twin Monitor, without polling ADB.";

    public OperationOutcomeKind DopeParticleTuningLevel
    {
        get => _dopeParticleTuningLevel;
        private set => SetProperty(ref _dopeParticleTuningLevel, value);
    }

    public string DopeParticleTuningSummary
    {
        get => _dopeParticleTuningSummary;
        private set => SetProperty(ref _dopeParticleTuningSummary, value);
    }

    public string DopeParticleTuningDetail
    {
        get => _dopeParticleTuningDetail;
        private set => SetProperty(ref _dopeParticleTuningDetail, value);
    }

    public string DopeParticleTuningSourcePath
    {
        get => _dopeParticleTuningSourcePath;
        private set => SetProperty(ref _dopeParticleTuningSourcePath, value);
    }

    public string DopeParticleTuningValuesLabel
    {
        get => _dopeParticleTuningValuesLabel;
        private set => SetProperty(ref _dopeParticleTuningValuesLabel, value);
    }

    public string DopeParticleTuningBaselineSummary
    {
        get => _dopeParticleTuningBaselineSummary;
        private set => SetProperty(ref _dopeParticleTuningBaselineSummary, value);
    }

    public string DopeParticleTuningTemplatePath
    {
        get => _dopeParticleTuningTemplatePath;
        private set => SetProperty(ref _dopeParticleTuningTemplatePath, value);
    }

    public string DopeParticleTuningCompiledPath
    {
        get => _dopeParticleTuningCompiledPath;
        private set => SetProperty(ref _dopeParticleTuningCompiledPath, value);
    }

    public string TwinTrackingCoverageSummary
    {
        get
        {
            if (_twinBridge is not LslTwinModeBridge lslBridge)
            {
                return _twinBridge.Status.Detail;
            }

            var deltas = lslBridge.ComputeSettingsDelta();
            var requestedCount = lslBridge.RequestedSettings.Count;
            var reportedCount = lslBridge.ReportedSettings.Count;
            var comparableCount = deltas.Count(delta => delta.Requested is not null && delta.Reported is not null);
            var matchedCount = deltas.Count(delta => delta.Matches);
            var unreportedCount = deltas.Count(delta => delta.Requested is not null && delta.Reported is null);

            if (requestedCount == 0 && reportedCount == 0)
            {
                return "No publish snapshot or live runtime values yet.";
            }

            if (requestedCount == 0)
            {
                return $"Live runtime is reporting {reportedCount} value(s), but no operator publish snapshot is staged for comparison yet.";
            }

            return $"Requested {requestedCount} key(s). Live runtime reported {reportedCount} key(s). Comparable {comparableCount}, matched {matchedCount}, not reported by this runtime {unreportedCount}.";
        }
    }

    public string RemoteControlSelectionSummary
        => SelectedApp is null
            ? "No selected target yet. Monitoring can still query the headset; Refresh Device Snapshot will adopt the current headset app automatically, or you can choose one manually in Quest Library."
            : $"Selected target: {SelectedApp.Label}. Device Snapshot shows on-demand headset state; In-App Twin State shows live LSL state from the active publisher, which may be a different APK.";

    public TwinInspectorScope? SelectedTwinInspectorScope
    {
        get => _selectedTwinInspectorScope;
        set
        {
            if (SetProperty(ref _selectedTwinInspectorScope, value))
            {
                RefreshTwinInspectorRows();
            }
        }
    }

    public TwinInspectorRow? SelectedTwinInspectorRow
    {
        get => _selectedTwinInspectorRow;
        set
        {
            if (SetProperty(ref _selectedTwinInspectorRow, value))
            {
                OnPropertyChanged(nameof(TwinInspectorSelectionHeadline));
                OnPropertyChanged(nameof(TwinInspectorSelectionRequested));
                OnPropertyChanged(nameof(TwinInspectorSelectionReported));
                OnPropertyChanged(nameof(TwinInspectorSelectionSource));
                OnPropertyChanged(nameof(TwinInspectorSelectionDetail));
            }
        }
    }

    public OperationOutcomeKind TwinBridgeLevel
    {
        get => _twinBridgeLevel;
        private set => SetProperty(ref _twinBridgeLevel, value);
    }

    public OperationOutcomeKind TwinPublisherLevel
    {
        get => _twinPublisherLevel;
        private set => SetProperty(ref _twinPublisherLevel, value);
    }

    public OperationOutcomeKind TwinAppStateLevel
    {
        get => _twinAppStateLevel;
        private set => SetProperty(ref _twinAppStateLevel, value);
    }

    public string TwinInspectorScopeSummary
    {
        get
        {
            var scopeLabel = SelectedTwinInspectorScope?.Label ?? "Focused";
            if (TwinInspectorRows.Count == 0)
            {
                return $"No {scopeLabel.ToLowerInvariant()} values are visible yet. Wait for quest_twin_state or publish a runtime snapshot first.";
            }

            var reportedCount = TwinInspectorRows.Count(row => row.HasReported);
            var comparableCount = TwinInspectorRows.Count(row => row.HasRequested && row.HasReported);
            var matchedCount = TwinInspectorRows.Count(row => row.Matches);
            return $"{scopeLabel}: {TwinInspectorRows.Count} key(s), {reportedCount} reported, {comparableCount} comparable, {matchedCount} matched.";
        }
    }

    public string TwinInspectorMatchLabel
    {
        get
        {
            var comparableCount = TwinInspectorRows.Count(row => row.HasRequested && row.HasReported);
            if (comparableCount == 0)
            {
                return "No comparable keys yet.";
            }

            var matchedCount = TwinInspectorRows.Count(row => row.Matches);
            return $"{matchedCount} / {comparableCount} matched";
        }
    }

    public double TwinInspectorMatchPercent
    {
        get
        {
            var comparableCount = TwinInspectorRows.Count(row => row.HasRequested && row.HasReported);
            if (comparableCount == 0)
            {
                return 0;
            }

            var matchedCount = TwinInspectorRows.Count(row => row.Matches);
            return (matchedCount * 100d) / comparableCount;
        }
    }

    public string TwinInspectorSelectionHeadline
        => SelectedTwinInspectorRow?.Key ?? "Select a tracked value.";

    public string TwinInspectorSelectionRequested
        => SelectedTwinInspectorRow?.Requested ?? "No requested value staged for the selected row.";

    public string TwinInspectorSelectionReported
        => SelectedTwinInspectorRow?.Reported ?? "The APK has not reported a value for the selected row yet.";

    public string TwinInspectorSelectionSource
        => SelectedTwinInspectorRow is null
            ? "Choose a row from the focused list to inspect requested, reported, and source context."
            : $"{SelectedTwinInspectorRow.ScopeLabel} • {SelectedTwinInspectorRow.Source}";

    public string TwinInspectorSelectionDetail
        => SelectedTwinInspectorRow is null
            ? "The focused list is intentionally trimmed to one inspector section at a time so the page stays readable even when the APK reports hundreds of values."
            : SelectedTwinInspectorRow.StatusLabel;

    public string RemoteControlLiveContextSummary
        => string.IsNullOrWhiteSpace(_liveTwinPublisherPackageId)
            ? "No live quest_twin_state publisher detected yet. When one appears, this pane will identify it separately from the selected target and the ADB active app."
            : $"Live publisher: {DescribeAppIdentity(_liveTwinPublisherPackageId, null)}. This pane reflects that APK's reported state, not necessarily the selected target.";

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RefreshCatalogAsync().ConfigureAwait(false);
        EnsureTwinBridgeMonitoringStarted();
        await RestartMonitorAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _testLslSignalService.Dispose();
        _runtimeConfig.PropertyChanged -= OnRuntimeConfigPropertyChanged;
        ActiveStudyShell?.Dispose();

        if (_twinBridge is LslTwinModeBridge lslBridge)
        {
            lslBridge.StateChanged -= OnTwinBridgeStateChanged;
        }

        if (_twinRefreshTimer is not null)
        {
            _twinRefreshTimer.Tick -= OnTwinRefreshTimerTick;
            _twinRefreshTimer.Stop();
        }

    }

    private async Task RefreshCatalogAsync()
    {
        try
        {
            var rootPath = ResolveCatalogRoot();
            var catalog = await _catalogLoader.LoadAsync(rootPath).ConfigureAwait(false);
            await _runtimeConfig.LoadAsync(rootPath, catalog.HotloadProfiles).ConfigureAwait(false);
            var previousPackageId = await DispatchAsync(() => SelectedApp?.PackageId).ConfigureAwait(false);
            var previousBundleId = await DispatchAsync(() => SelectedBundle?.Id).ConfigureAwait(false);
            var previousDeviceProfileId = await DispatchAsync(() => SelectedDeviceProfile?.Id).ConfigureAwait(false);

            await DispatchAsync(() =>
            {
                Apps.Clear();
                Bundles.Clear();
                DeviceProfiles.Clear();
                AvailableHotloadProfiles.Clear();

                foreach (var app in catalog.Apps)
                {
                    Apps.Add(app);
                }

                foreach (var bundle in catalog.Bundles)
                {
                    Bundles.Add(bundle);
                }

                foreach (var profile in catalog.DeviceProfiles)
                {
                    DeviceProfiles.Add(profile);
                }

                CatalogStatus = $"Loaded {catalog.Source.Label}: {catalog.Apps.Count} app(s), {catalog.Bundles.Count} bundle(s), {catalog.HotloadProfiles.Count} runtime preset(s), and {catalog.DeviceProfiles.Count} device profile(s).";
                CatalogSourcePath = catalog.Source.RootPath;

                _hotloadProfiles = catalog.HotloadProfiles;
                SelectedApp = Apps.FirstOrDefault(app => string.Equals(app.PackageId, previousPackageId, StringComparison.OrdinalIgnoreCase));
                SelectedBundle = Bundles.FirstOrDefault(bundle => string.Equals(bundle.Id, previousBundleId, StringComparison.OrdinalIgnoreCase));
                SelectedDeviceProfile = DeviceProfiles.FirstOrDefault(profile => string.Equals(profile.Id, previousDeviceProfileId, StringComparison.OrdinalIgnoreCase))
                    ?? DeviceProfiles.FirstOrDefault();
                RefreshAvailableHotloadProfiles();
                RefreshRuntimeConfigProfileSelection();

                AppendLog(OperatorLogLevel.Info, "Catalog refreshed.", $"{CatalogStatus} {_runtimeConfig.CatalogStatus}");
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await DispatchAsync(() =>
            {
                CatalogStatus = "Catalog refresh failed.";
                CatalogSourcePath = ex.Message;
                AppendLog(OperatorLogLevel.Failure, "Catalog refresh failed.", ex.Message);
            }).ConfigureAwait(false);
        }

        await RefreshStudyShellCatalogAsync().ConfigureAwait(false);
    }

    private async Task RefreshStudyShellCatalogAsync()
    {
        try
        {
            var activeStudyId = await DispatchAsync(() => ActiveStudyShell?.StudyId).ConfigureAwait(false);
            var rootPath = AppAssetLocator.TryResolveStudyShellRoot();
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                await DispatchAsync(() =>
                {
                    StudyShells.Clear();
                    _studyShellLaunchOptions = new StudyShellLaunchOptions(string.Empty, false);
                    StudyShellCatalogStatus = "No scene-workspace catalog found.";
                    StudyShellCatalogSourcePath = "Keep samples/study-shells available next to the app if you want to experiment with a locked workspace later.";
                    NotifyStudyShellEntryPointsChanged();
                }).ConfigureAwait(false);
                return;
            }

            var catalog = await _studyShellCatalogLoader.LoadAsync(rootPath).ConfigureAwait(false);
            StudyShellDefinition? startupStudy = catalog.LaunchOptions.HasStartupStudy
                ? catalog.Studies.FirstOrDefault(
                    study => string.Equals(study.Id, catalog.LaunchOptions.StartupStudyId, StringComparison.OrdinalIgnoreCase))
                : null;

            await DispatchAsync(() =>
            {
                StudyShells.Clear();
                foreach (var study in catalog.Studies)
                {
                    StudyShells.Add(study);
                }

                _studyShellLaunchOptions = catalog.LaunchOptions;
                StudyShellCatalogStatus = $"Loaded {catalog.Studies.Count} scene workspace(s) from {catalog.Source.Label}.";
                StudyShellCatalogSourcePath = catalog.Source.RootPath;
                NotifyStudyShellEntryPointsChanged();
            }).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(activeStudyId))
            {
                var refreshedStudy = catalog.Studies.FirstOrDefault(
                    study => string.Equals(study.Id, activeStudyId, StringComparison.OrdinalIgnoreCase));
                if (refreshedStudy is not null)
                {
                    await ActivateStudyModeAsync(refreshedStudy).ConfigureAwait(false);
                }
                return;
            }

            if (catalog.LaunchOptions.LockToStartupStudy && startupStudy is not null)
            {
                ResetPersistedStudyShellStartupState();
                await ActivateStudyModeAsync(startupStudy).ConfigureAwait(false);
                _startupStudyAutoOpened = true;
                return;
            }

            if (startupStudy is not null && !_startupStudyAutoOpened)
            {
                ResetPersistedStudyShellStartupState();
                await ActivateStudyModeAsync(startupStudy).ConfigureAwait(false);
                _startupStudyAutoOpened = true;
                return;
            }

            if (catalog.LaunchOptions.HasStartupStudy)
            {
                await DispatchAsync(() =>
                {
                    AppendLog(
                        OperatorLogLevel.Warning,
                        "Startup workspace not found.",
                        $"Configured startup workspace id `{catalog.LaunchOptions.StartupStudyId}` was not found in {catalog.Source.Label}.");
                }).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await DispatchAsync(() =>
            {
                StudyShells.Clear();
                _studyShellLaunchOptions = new StudyShellLaunchOptions(string.Empty, false);
                StudyShellCatalogStatus = "Scene-workspace catalog refresh failed.";
                StudyShellCatalogSourcePath = ex.Message;
                AppendLog(OperatorLogLevel.Warning, "Scene-workspace catalog refresh failed.", ex.Message);
                NotifyStudyShellEntryPointsChanged();
            }).ConfigureAwait(false);
        }
    }

    private void NotifyStudyShellEntryPointsChanged()
    {
        OnPropertyChanged(nameof(StartupStudyShell));
        OnPropertyChanged(nameof(FeaturedStudyShell));
        OnPropertyChanged(nameof(HasFeaturedStudyShell));
        OnPropertyChanged(nameof(ShowStudyModeAction));
        OnPropertyChanged(nameof(FeaturedStudyShellLabel));
        OnPropertyChanged(nameof(FeaturedStudyShellSummary));
        OnPropertyChanged(nameof(FeaturedStudyShellTargetLabel));
        OnPropertyChanged(nameof(StudyModeHeaderButtonLabel));
        OnPropertyChanged(nameof(FeaturedStudyShellActionLabel));
        OnPropertyChanged(nameof(LockedStudyShellStartupLabel));
        OnPropertyChanged(nameof(LockedStudyShellStartupDetail));
        NotifyStudyModeStateChanged();
    }

    private async Task OpenStudyShellAsync(object? parameter)
    {
        if (parameter is not StudyShellDefinition study)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Study shell blocked.", "Select a study shell before opening it.")).ConfigureAwait(false);
            return;
        }

        await ActivateStudyModeAsync(study).ConfigureAwait(false);
    }

    public async Task ActivateStudyModeAsync(StudyShellDefinition study)
    {
        ArgumentNullException.ThrowIfNull(study);

        StudyShellViewModel nextStudyShell = await DispatchAsync(() => new StudyShellViewModel(study)).ConfigureAwait(false);
        StudyShellViewModel? previousStudyShell = null;

        await DispatchAsync(() =>
        {
            previousStudyShell = ActiveStudyShell;
            ActiveStudyShell = nextStudyShell;
            SelectedTabIndex = 0;
            PinStudyShellSelection(study);
            AppendLog(OperatorLogLevel.Info, "Study mode activated.", $"{study.Label} is now embedded in the main operator window.");
        }).ConfigureAwait(false);

        previousStudyShell?.Dispose();
        await nextStudyShell.InitializeAsync().ConfigureAwait(false);
    }

    private Task ExitStudyModeAsync()
    {
        return DispatchAsync(() =>
        {
            if (HasLockedStartupStudy)
            {
                AppendLog(
                    OperatorLogLevel.Warning,
                    "Study mode exit blocked.",
                    "This package is pinned to its startup study shell and does not expose the full operator workspace.");
                return;
            }

            if (ActiveStudyShell is null)
            {
                return;
            }

            var previousStudyShell = ActiveStudyShell;
            ActiveStudyShell = null;
            AppendLog(OperatorLogLevel.Info, "Study mode cleared.", $"{previousStudyShell.StudyLabel} is no longer pinned in the main operator window.");
            previousStudyShell.Dispose();
        });
    }

    private Task ToggleStudyBannerAsync()
    {
        IsStudyBannerExpanded = !IsStudyBannerExpanded;
        return Task.CompletedTask;
    }

    private void PinStudyShellSelection(StudyShellDefinition study)
    {
        var matchingApp = Apps.FirstOrDefault(app => string.Equals(app.PackageId, study.App.PackageId, StringComparison.OrdinalIgnoreCase));
        if (matchingApp is not null)
        {
            SelectedApp = matchingApp;
        }

        if (DeviceProfiles.FirstOrDefault(profile => string.Equals(profile.Id, study.DeviceProfile.Id, StringComparison.OrdinalIgnoreCase)) is { } matchingDeviceProfile)
        {
            SelectedDeviceProfile = matchingDeviceProfile;
        }

        if (study.DeviceProfile.Properties.TryGetValue("debug.oculus.cpuLevel", out var cpuLevel) && !string.IsNullOrWhiteSpace(cpuLevel))
        {
            CpuLevelText = cpuLevel;
        }

        if (study.DeviceProfile.Properties.TryGetValue("debug.oculus.gpuLevel", out var gpuLevel) && !string.IsNullOrWhiteSpace(gpuLevel))
        {
            GpuLevelText = gpuLevel;
        }
    }

    private void ConfigureTwinStateSourceForSelectedApp()
    {
        if (ActiveStudyShell is not null)
        {
            return;
        }

        if (_twinBridge is not LslTwinModeBridge lslBridge)
        {
            return;
        }

        lslBridge.ConfigureExpectedQuestStateSource(SelectedApp?.PackageId);
    }

    private void RefreshAvailableHotloadProfiles()
    {
        if (SelectedApp is null)
        {
            AvailableHotloadProfiles.Clear();
            SelectedHotloadProfile = null;
            return;
        }

        var matching = _hotloadProfiles
            .Where(profile => profile.MatchesPackage(SelectedApp.PackageId))
            .ToArray();

        AvailableHotloadProfiles.Clear();
        foreach (var profile in matching)
        {
            AvailableHotloadProfiles.Add(profile);
        }

        SelectedHotloadProfile = AvailableHotloadProfiles.FirstOrDefault();
    }

    private void RefreshRuntimeConfigProfileSelection()
        => _runtimeConfig.SelectProfileForPackage(SelectedApp?.PackageId);

    private void RefreshDopeParticleTuningState()
    {
        DopeParticleTuningTemplatePath = AppAssetLocator.TryResolveDopeParticleSizeTemplatePath()
            ?? "Bundled Dope particle-size template not found.";
        DopeParticleTuningValuesLabel = _dopeParticleSizeTuningDocument is null
            ? "No Dope particle-size values imported yet."
            : $"Imported min {_dopeParticleSizeTuningDocument.ParticleSizeMinimum.Value:0.###} | max {_dopeParticleSizeTuningDocument.ParticleSizeMaximum.Value:0.###} ({_dopeParticleSizeTuningDocument.PackageId}).";
        DopeParticleTuningBaselineSummary = TryGetLiveDopeRuntimeConfigJson(out _, out var baselineDetail)
            ? "Live Dope runtime JSON baseline is available on quest_twin_state."
            : baselineDetail;
    }

    private bool TryGetLiveDopeRuntimeConfigJson(out string runtimeConfigJson, out string detail)
    {
        runtimeConfigJson = string.Empty;

        if (_dopeParticleSizeTuningDocument is null)
        {
            detail = "Import a Dope particle-size tuning V1 JSON file first.";
            return false;
        }

        if (SelectedApp is null)
        {
            detail = "Select the Dope Experiment target app before compiling a particle-size tuning file.";
            return false;
        }

        if (!string.Equals(SelectedApp.PackageId, _dopeParticleSizeTuningDocument.PackageId, StringComparison.OrdinalIgnoreCase))
        {
            detail = $"Selected app {SelectedApp.PackageId} does not match the tuning file target {_dopeParticleSizeTuningDocument.PackageId}.";
            return false;
        }

        if (_twinBridge is not LslTwinModeBridge lslBridge)
        {
            detail = _twinBridge.Status.Detail;
            return false;
        }

        if (lslBridge.ReportedSettings.TryGetValue("showcase_active_runtime_config_json", out var liveRuntimeConfigJson) &&
            !string.IsNullOrWhiteSpace(liveRuntimeConfigJson))
        {
            runtimeConfigJson = liveRuntimeConfigJson;
            detail = "Live Dope runtime JSON baseline is available on quest_twin_state.";
            return true;
        }

        if (lslBridge.ReportedSettings.TryGetValue("hotload.showcase_active_runtime_config_json", out liveRuntimeConfigJson) &&
            !string.IsNullOrWhiteSpace(liveRuntimeConfigJson))
        {
            runtimeConfigJson = liveRuntimeConfigJson;
            detail = "Live Dope runtime JSON baseline is available on quest_twin_state.";
            return true;
        }

        detail = "Connect the live Dope runtime and let it publish showcase_active_runtime_config_json on quest_twin_state before applying a particle-size tuning file.";
        return false;
    }

    private void RefreshSelectedAppApkPath()
    {
        if (SelectedApp is null)
        {
            SelectedAppApkPath = string.Empty;
            return;
        }

        if (_apkOverrides.TryGetValue(SelectedApp.Id, out var overridePath) && !string.IsNullOrWhiteSpace(overridePath))
        {
            SelectedAppApkPath = overridePath;
            return;
        }

        SelectedAppApkPath = ResolveApkPathForTarget(SelectedApp) ?? string.Empty;
    }

    private void RefreshPipelineLabel()
    {
        if (SelectedApp is null)
        {
            SelectedAppPipelineLabel = string.Empty;
            IsSelectedAppIncompatible = false;
            IncompatibleBannerLabel = string.Empty;
            return;
        }

        var tags = SelectedApp.Tags;
        var shortHash = string.IsNullOrWhiteSpace(SelectedApp.ApkSha256)
            ? string.Empty
            : SelectedApp.ApkSha256[..Math.Min(12, SelectedApp.ApkSha256.Length)];

        if (SelectedApp.CompatibilityStatus == ApkCompatibilityStatus.Incompatible)
        {
            SelectedAppPipelineLabel = string.IsNullOrWhiteSpace(SelectedApp.CompatibilityNotes)
                ? $"APK hash {shortHash} is marked incompatible with this Windows app build."
                : $"APK hash {shortHash} is marked incompatible: {SelectedApp.CompatibilityNotes}";
            IsSelectedAppIncompatible = true;
            IncompatibleBannerLabel = $"{SelectedApp.Label} is marked incompatible for this Windows app version. Update the hash compatibility manifest or switch to a supported build.";
            return;
        }

        IsSelectedAppIncompatible = false;
        IncompatibleBannerLabel = string.Empty;

        if (!IsCompanionCompatibleTarget(tags))
        {
            SelectedAppPipelineLabel = "This app is not tagged as a DOPE companion target. Remote control, twin bridge, and runtime config features are not guaranteed for it.";
            IsSelectedAppIncompatible = true;
            IncompatibleBannerLabel = $"{SelectedApp.Label} is not tagged as a companion-compatible target. Twin bridge, runtime config, and remote control features require a tagged DOPE target.";
            return;
        }

        var parts = new List<string>();
        var supportsTwin = tags.Any(t => t.Equals("twin", StringComparison.OrdinalIgnoreCase));
        var supportsRuntime = tags.Any(t => t.Equals("runtime", StringComparison.OrdinalIgnoreCase));
        var supportsLsl = tags.Any(t => t.Equals("lsl", StringComparison.OrdinalIgnoreCase));

        if (supportsTwin)
            parts.Add("twin bridge");
        if (supportsRuntime)
            parts.Add("runtime config hotload");
        if (supportsLsl)
            parts.Add("LSL telemetry relay");

        var capabilitySummary = parts.Count > 0
            ? $"This app supports: {string.Join(", ", parts)}."
            : "Companion-compatible target. No live runtime or twin capability is advertised by the current catalog metadata.";

        var hashSummary = string.IsNullOrWhiteSpace(SelectedApp.ApkSha256)
            ? string.Empty
            : SelectedApp.CompatibilityStatus == ApkCompatibilityStatus.Compatible
                ? $" Verified build hash: {shortHash}."
                : $" Build hash {shortHash} is not classified yet for this Windows app version; update the compatibility manifest to confirm support.";

        var compatibilityNotes = string.IsNullOrWhiteSpace(SelectedApp.CompatibilityNotes)
            ? string.Empty
            : $" {SelectedApp.CompatibilityNotes}";

        SelectedAppPipelineLabel = supportsRuntime && !supportsTwin
            ? $"{capabilitySummary} Live requested/reported config tracking requires a twin-state publisher on `quest_twin_state` such as LslTwin.{hashSummary}{compatibilityNotes}"
            : $"{capabilitySummary}{hashSummary}{compatibilityNotes}";

        OnPropertyChanged(nameof(RuntimeConfigPublishSummary));
        OnPropertyChanged(nameof(RuntimeConfigPublishChannelSummary));
        OnPropertyChanged(nameof(SelectedAppCapabilitySummary));
        OnPropertyChanged(nameof(SelectedAppCommunicationSummary));
        OnPropertyChanged(nameof(RemoteControlSelectionSummary));
    }

    private async Task ProbeUsbAsync()
    {
        var outcome = await _questService.ProbeUsbAsync().ConfigureAwait(false);
        SaveSession(usbSerial: outcome.Endpoint);
        await ApplyOutcomeAsync("Probe USB", outcome, summary => UsbSummary = summary).ConfigureAwait(false);
    }

    private async Task DiscoverWifiAsync()
    {
        var outcome = await _questService.DiscoverWifiAsync().ConfigureAwait(false);
        await DispatchAsync(() =>
        {
            if (!string.IsNullOrWhiteSpace(outcome.Endpoint))
            {
                EndpointDraft = outcome.Endpoint;
            }
        }).ConfigureAwait(false);

        SaveSession(endpoint: outcome.Endpoint);
        await ApplyOutcomeAsync("Find Wi-Fi Quest", outcome, summary => ConnectionSummary = summary).ConfigureAwait(false);

        if (outcome.Kind is OperationOutcomeKind.Success or OperationOutcomeKind.Warning)
        {
            await RefreshHeadsetStatusAsync().ConfigureAwait(false);
        }
    }

    private async Task EnableWifiAsync()
    {
        var outcome = await _questService.EnableWifiFromUsbAsync().ConfigureAwait(false);
        await DispatchAsync(() =>
        {
            if (!string.IsNullOrWhiteSpace(outcome.Endpoint))
            {
                EndpointDraft = outcome.Endpoint;
            }
        }).ConfigureAwait(false);

        SaveSession(endpoint: outcome.Endpoint);
        await ApplyOutcomeAsync("Enable Wi-Fi ADB", outcome, summary => UsbSummary = summary).ConfigureAwait(false);
    }

    private async Task ConnectQuestAsync()
    {
        var outcome = await _questService.ConnectAsync(EndpointDraft).ConfigureAwait(false);
        SaveSession(endpoint: outcome.Endpoint);
        await ApplyOutcomeAsync("Connect Quest", outcome, summary => ConnectionSummary = summary).ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
    }

    private async Task InstallSelectedAppAsync()
    {
        if (SelectedApp is null)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Install app blocked.", "Select an app target first.")).ConfigureAwait(false);
            return;
        }

        var installTarget = WithResolvedApkPath(SelectedApp);
        if (string.IsNullOrWhiteSpace(installTarget.ApkFile))
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Install app blocked.", "Choose an APK file for the selected app first.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _questService.InstallAppAsync(installTarget).ConfigureAwait(false);
        await ApplyOutcomeAsync("Install Selected App", outcome).ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
    }

    private async Task InstallSelectedBundleAsync()
    {
        if (SelectedBundle is null)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Install bundle blocked.", "Select an install bundle first.")).ConfigureAwait(false);
            return;
        }

        var targets = SelectedBundle.AppIds
            .Select(id => Apps.FirstOrDefault(app => string.Equals(app.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(app => app is not null)
            .Cast<QuestAppTarget>()
            .Select(WithResolvedApkPath)
            .ToArray();

        var outcome = await _questService.InstallBundleAsync(SelectedBundle, targets).ConfigureAwait(false);
        await ApplyOutcomeAsync("Install Bundle", outcome).ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
    }

    private async Task BrowseSelectedApkAsync()
    {
        if (SelectedApp is null)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Browse APK blocked.", "Select an app target first.")).ConfigureAwait(false);
            return;
        }

        var selectedPath = await DispatchAsync(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = $"Choose APK for {SelectedApp.Label}",
                Filter = "Android packages (*.apk)|*.apk|All files (*.*)|*.*",
                FileName = Path.GetFileName(SelectedAppApkPath)
            };

            return dialog.ShowDialog() == true ? dialog.FileName : string.Empty;
        }).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            await DispatchAsync(() =>
            {
                SelectedAppApkPath = selectedPath;
                AppendLog(OperatorLogLevel.Info, "APK selected.", selectedPath);
            }).ConfigureAwait(false);
        }
    }

    private async Task ApplyHotloadAsync()
    {
        if (SelectedApp is null || SelectedHotloadProfile is null)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Runtime preset blocked.", "Select both an app target and a runtime preset first.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _questService.ApplyHotloadProfileAsync(SelectedHotloadProfile, SelectedApp).ConfigureAwait(false);
        await ApplyOutcomeAsync("Track Runtime Preset", outcome).ConfigureAwait(false);
    }

    private async Task ApplyDeviceProfileAsync()
    {
        if (SelectedDeviceProfile is null)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Device profile blocked.", "Select a device profile first.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _questService.ApplyDeviceProfileAsync(SelectedDeviceProfile).ConfigureAwait(false);
        await ApplyOutcomeAsync("Apply Device Profile", outcome).ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
        await RefreshDeviceProfileDiagnosticsAsync().ConfigureAwait(false);
    }

    private async Task ApplyPerformanceLevelsAsync()
    {
        if (!int.TryParse(CpuLevelText, out var cpuLevel) || !int.TryParse(GpuLevelText, out var gpuLevel))
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Performance update blocked.", "CPU and GPU levels must be integers.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _questService.ApplyPerformanceLevelsAsync(cpuLevel, gpuLevel).ConfigureAwait(false);
        await ApplyOutcomeAsync("Apply Performance Levels", outcome).ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
    }

    private async Task LaunchSelectedAppAsync()
    {
        if (SelectedApp is null)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Launch blocked.", "Select an app target first.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _questService.LaunchAppAsync(SelectedApp).ConfigureAwait(false);
        await ApplyOutcomeAsync("Launch Selected App", outcome).ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
    }

    private async Task OpenBrowserAsync()
    {
        var browserTarget = Apps.FirstOrDefault(app =>
            app.Tags.Contains("browser", StringComparer.OrdinalIgnoreCase) ||
            string.Equals(app.PackageId, "com.oculus.browser", StringComparison.OrdinalIgnoreCase));

        if (browserTarget is null)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Browser action blocked.", "No browser target is available in the staged Quest library.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _questService.OpenBrowserAsync(BrowserUrlDraft, browserTarget).ConfigureAwait(false);
        await ApplyOutcomeAsync("Open Browser on Quest", outcome).ConfigureAwait(false);
    }

    private async Task RefreshForegroundAsync()
    {
        var outcome = await _questService.QueryForegroundAsync().ConfigureAwait(false);
        await ApplyOutcomeAsync("Refresh Foreground App", outcome, summary => ForegroundSummary = summary).ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
    }

    private async Task RefreshHeadsetStatusAsync()
    {
        EnsureTwinBridgeMonitoringStarted();
        var status = await _questService.QueryHeadsetStatusAsync(SelectedApp, RemoteOnlyControlEnabled).ConfigureAwait(false);
        var hzdbOutcome = await QueryHzdbSnapshotAsync(cancellationToken: default).ConfigureAwait(false);

        await DispatchAsync(() =>
        {
            HeadsetStatusSummary = status.Summary;
            HeadsetStatusDetail = status.Detail;
            IsQuestConnected = status.IsConnected;
            BatteryPercent = status.BatteryLevel ?? 0;
            HeadsetModel = status.IsConnected ? $"Model {status.DeviceModel}" : "Not connected";
            HeadsetBatteryLabel = BuildHeadsetBatteryLabel(status);
            HeadsetSoftwareVersionLabel = BuildHeadsetSoftwareVersionLabel(status);
            HeadsetPerformanceLabel = $"CPU {(status.CpuLevel?.ToString() ?? "n/a")} / GPU {(status.GpuLevel?.ToString() ?? "n/a")}";
            _activeForegroundPackageId = status.ForegroundPackageId;
            _activeForegroundComponent = status.ForegroundComponent;
            HeadsetForegroundPackage = string.IsNullOrWhiteSpace(status.ForegroundPackageId)
                ? "Foreground n/a"
                : $"Foreground {(string.IsNullOrWhiteSpace(status.ForegroundComponent) ? status.ForegroundPackageId : status.ForegroundComponent)}";
            HeadsetVisibleActivities = status.VisibleActivityComponents is { Count: > 0 }
                ? string.Join(" | ", status.VisibleActivityComponents)
                : "Visible activities n/a";
            DeviceSnapshotAgeLabel = $"Last device snapshot {DateTimeOffset.Now:HH:mm:ss}. Device probes run only on request.";
            HzdbStatusSummary = hzdbOutcome.Summary;
            HzdbStatusDetail = hzdbOutcome.Detail;
            var adoptedDetectedTarget = TryAdoptDetectedTarget(status.ForegroundPackageId);

            var activityStatus = DescribeHeadsetActivity(status);
            HeadsetActivityLabel = activityStatus.Label;
            HeadsetActivityDetail = activityStatus.Detail;
            HeadsetActivityLevel = activityStatus.Level;
            HeadsetTargetStatusLabel = SelectedApp is null
                ? !status.IsConnected
                    ? "Waiting for headset check."
                    : string.IsNullOrWhiteSpace(status.ForegroundPackageId)
                        ? "Waiting for current headset app."
                        : $"{status.ForegroundPackageId} is active, but not in the Quest library yet."
                : adoptedDetectedTarget
                    ? $"{SelectedApp.Label} is active."
                : status.IsTargetForeground
                    ? $"{SelectedApp.Label} is active."
                    : status.IsTargetRunning
                        ? $"{SelectedApp.Label} is running."
                        : status.IsTargetInstalled
                            ? $"{SelectedApp.Label} is installed."
                            : $"{SelectedApp.Label} is not installed.";

            TargetStatusLevel = SelectedApp is null
                ? OperationOutcomeKind.Preview
                : adoptedDetectedTarget
                    ? OperationOutcomeKind.Success
                : status.IsTargetForeground
                    ? OperationOutcomeKind.Success
                    : status.IsTargetRunning
                        ? OperationOutcomeKind.Warning
                        : status.IsTargetInstalled
                            ? OperationOutcomeKind.Warning
                            : OperationOutcomeKind.Failure;

            RefreshRuntimeContextLabels();
            RefreshLiveTwinState();
        }).ConfigureAwait(false);

        _latestHeadsetStatus = status;
    }

    private async Task AnalyzeWindowsEnvironmentAsync()
    {
        WindowsEnvironmentAnalysisResult result;
        try
        {
            result = await _windowsEnvironmentAnalysisService
                .AnalyzeAsync(BuildWindowsEnvironmentAnalysisRequest())
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await ApplyOutcomeAsync(
                "Analyze Windows Environment",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Windows environment analysis failed.",
                    exception.Message)).ConfigureAwait(false);
            return;
        }

        await DispatchAsync(() => ApplyWindowsEnvironmentAnalysisResult(result)).ConfigureAwait(false);
        await ApplyOutcomeAsync(
            "Analyze Windows Environment",
            new OperationOutcome(
                result.Level,
                result.Summary,
                BuildWindowsEnvironmentActionDetail(result))).ConfigureAwait(false);
    }

    private async Task RefreshDeviceProfileDiagnosticsAsync()
    {
        var profile = ResolveDiagnosticsDeviceProfile();
        if (profile is null)
        {
            await ApplyOutcomeAsync(
                "Refresh Quest Profile Diagnostics",
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Quest profile diagnostics are blocked.",
                    "Select a device profile first, or keep the bundled projected-feed Colorama profile available in the diagnostics catalog.")).ConfigureAwait(false);
            return;
        }

        DeviceProfileStatus status;
        try
        {
            status = await _questService.QueryDeviceProfileStatusAsync(profile).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchAsync(() =>
            {
                DeviceProfileDiagnosticsLevel = OperationOutcomeKind.Failure;
                DeviceProfileDiagnosticsSummary = "Quest profile diagnostics failed.";
                DeviceProfileDiagnosticsDetail = exception.Message;
                DeviceProfileDiagnosticsTimestampLabel = $"Failed {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
                ReplaceStudyStatusRows(DeviceProfileDiagnosticsRows, []);
            }).ConfigureAwait(false);

            await ApplyOutcomeAsync(
                "Refresh Quest Profile Diagnostics",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Quest profile diagnostics failed.",
                    exception.Message)).ConfigureAwait(false);
            return;
        }

        await DispatchAsync(() => ApplyDeviceProfileDiagnosticsResult(status)).ConfigureAwait(false);
        await ApplyOutcomeAsync(
            "Refresh Quest Profile Diagnostics",
            new OperationOutcome(
                status.IsActive ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning,
                status.Summary,
                status.Detail)).ConfigureAwait(false);
    }

    private async Task RefreshWifiRouterDiagnosticsAsync()
    {
        if (_latestHeadsetStatus?.IsConnected != true)
        {
            await RefreshHeadsetStatusAsync().ConfigureAwait(false);
        }

        if (_latestHeadsetStatus?.IsConnected != true)
        {
            await DispatchAsync(() =>
            {
                WifiRouterDiagnosticsLevel = OperationOutcomeKind.Preview;
                WifiRouterDiagnosticsSummary = "Wi-Fi router path not checked yet.";
                WifiRouterDiagnosticsDetail = "Connect the headset first. Once the public flow is on Wi-Fi ADB, this page can verify the current router path.";
                WifiRouterDiagnosticsTimestampLabel = "Not checked yet.";
            }).ConfigureAwait(false);

            await ApplyOutcomeAsync(
                "Refresh Wi-Fi Router Diagnostics",
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Wi-Fi router diagnostics are blocked.",
                    "Connect the headset first so the current Wi-Fi ADB selector and SSID topology are available.")).ConfigureAwait(false);
            return;
        }

        QuestWifiTransportDiagnosticsResult result;
        try
        {
            result = await _questWifiTransportDiagnosticsService
                .AnalyzeAsync(_latestHeadsetStatus, ResolveHeadsetActionSelector())
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchAsync(() =>
            {
                WifiRouterDiagnosticsLevel = OperationOutcomeKind.Failure;
                WifiRouterDiagnosticsSummary = "Wi-Fi router diagnostics failed.";
                WifiRouterDiagnosticsDetail = exception.Message;
                WifiRouterDiagnosticsTimestampLabel = $"Failed {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
            }).ConfigureAwait(false);

            await ApplyOutcomeAsync(
                "Refresh Wi-Fi Router Diagnostics",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Wi-Fi router diagnostics failed.",
                    exception.Message)).ConfigureAwait(false);
            return;
        }

        await DispatchAsync(() => ApplyWifiRouterDiagnosticsResult(result)).ConfigureAwait(false);
        await ApplyOutcomeAsync(
            "Refresh Wi-Fi Router Diagnostics",
            new OperationOutcome(
                result.Level,
                result.Summary,
                result.Detail)).ConfigureAwait(false);
    }

    private async Task GenerateDiagnosticsReportAsync()
    {
        var study = ResolveDiagnosticsStudyDefinition();
        if (study is null)
        {
            await ApplyOutcomeAsync(
                "Generate Diagnostics Report",
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "Diagnostics report generation is blocked.",
                    "The public diagnostics catalog did not expose the projected-feed Colorama scene definition needed to build the report.")).ConfigureAwait(false);
            return;
        }

        await DispatchAsync(() =>
        {
            DiagnosticsReportLevel = OperationOutcomeKind.Preview;
            DiagnosticsReportSummary = "Generating DOPE diagnostics report...";
            DiagnosticsReportDetail = "Collecting Windows analysis, Quest setup, Wi-Fi transport, quest_twin_state return-path, and command-acceptance evidence.";
            DiagnosticsReportTimestampLabel = $"Started {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
        }).ConfigureAwait(false);

        DopeDiagnosticsReportResult result;
        OperationOutcome pdfOutcome;
        try
        {
            if (_latestHeadsetStatus?.IsConnected != true)
            {
                await RefreshHeadsetStatusAsync().ConfigureAwait(false);
            }

            var reportService = new DopeDiagnosticsReportService(
                _questService,
                _windowsEnvironmentAnalysisService,
                _lslStreamDiscoveryService,
                _testLslSignalService,
                _twinBridge);
            result = await reportService
                .GenerateAsync(
                    new DopeDiagnosticsReportRequest(
                        study,
                        DeviceSelector: ResolveHeadsetActionSelector(),
                        ProbeWaitDuration: TimeSpan.FromSeconds(12)))
                .ConfigureAwait(false);
            pdfOutcome = await GenerateDiagnosticsReportPdfAsync(result.JsonPath, result.PdfPath).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await DispatchAsync(() =>
            {
                DiagnosticsReportLevel = OperationOutcomeKind.Failure;
                DiagnosticsReportSummary = "DOPE diagnostics report failed.";
                DiagnosticsReportDetail = exception.Message;
                DiagnosticsReportTimestampLabel = $"Failed {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
            }).ConfigureAwait(false);

            await ApplyOutcomeAsync(
                "Generate Diagnostics Report",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "DOPE diagnostics report failed.",
                    exception.Message)).ConfigureAwait(false);
            return;
        }

        var displayLevel = result.Level == OperationOutcomeKind.Success && pdfOutcome.Kind == OperationOutcomeKind.Warning
            ? OperationOutcomeKind.Warning
            : result.Level;
        var pdfDetail = pdfOutcome.Kind == OperationOutcomeKind.Success
            ? $"PDF ready: {NormalizeHostVisibleOperatorPath(result.PdfPath)}"
            : $"PDF issue: {pdfOutcome.Summary} {pdfOutcome.Detail}".Trim();

        await DispatchAsync(() =>
        {
            ApplyWindowsEnvironmentAnalysisResult(result.Report.WindowsEnvironment);
            ApplyWifiRouterDiagnosticsResult(result.Report.QuestWifiTransport);
            DiagnosticsReportLevel = displayLevel;
            DiagnosticsReportSummary = result.Summary;
            DiagnosticsReportDetail = $"{result.Detail} {pdfDetail} Folder: {NormalizeHostVisibleOperatorPath(result.ReportDirectory)}";
            DiagnosticsReportTimestampLabel = $"Generated {result.CompletedAtUtc.ToLocalTime():HH:mm:ss}.";
            DiagnosticsReportFolderPath = result.ReportDirectory;
            DiagnosticsReportPdfPath = result.PdfPath;
        }).ConfigureAwait(false);

        await ApplyOutcomeAsync(
            "Generate Diagnostics Report",
            new OperationOutcome(
                displayLevel,
                result.Summary,
                DiagnosticsReportDetail,
                Items: [result.PdfPath, result.TexPath, result.JsonPath, result.ReportDirectory])).ConfigureAwait(false);
    }

    private WindowsEnvironmentAnalysisRequest BuildWindowsEnvironmentAnalysisRequest()
    {
        var diagnosticsStudy = ResolveDiagnosticsStudyDefinition();
        var expectedStreamName = string.IsNullOrWhiteSpace(diagnosticsStudy?.Monitoring.ExpectedLslStreamName)
            ? HrvBiofeedbackStreamContract.StreamName
            : diagnosticsStudy.Monitoring.ExpectedLslStreamName;
        var expectedStreamType = string.IsNullOrWhiteSpace(diagnosticsStudy?.Monitoring.ExpectedLslStreamType)
            ? HrvBiofeedbackStreamContract.StreamType
            : diagnosticsStudy.Monitoring.ExpectedLslStreamType;

        return new WindowsEnvironmentAnalysisRequest(
            expectedStreamName,
            expectedStreamType,
            QuestWifiTransport: _latestHeadsetStatus?.IsConnected == true
                ? new QuestWifiTransportDiagnosticsContext(_latestHeadsetStatus, ResolveHeadsetActionSelector())
                : null);
    }

    private void ApplyWindowsEnvironmentAnalysisResult(WindowsEnvironmentAnalysisResult result)
    {
        WindowsEnvironmentAnalysisLevel = result.Level;
        WindowsEnvironmentAnalysisSummary = result.Summary;
        WindowsEnvironmentAnalysisDetail = BuildWindowsEnvironmentSummaryDetail(result);
        WindowsEnvironmentAnalysisTimestampLabel = $"Last analyzed {result.CompletedAtUtc.ToLocalTime():HH:mm:ss}.";
        ReplaceWorkflowGuideCheckItems(
            WindowsEnvironmentChecks,
            result.Checks
                .Select(check => new WorkflowGuideCheckItem(check.Label, check.Summary, check.Detail, check.Level))
                .ToArray());
    }

    private static string BuildWindowsEnvironmentSummaryDetail(WindowsEnvironmentAnalysisResult result)
    {
        var attentionItems = result.Checks
            .Where(check => check.Level is OperationOutcomeKind.Warning or OperationOutcomeKind.Failure)
            .Select(check => $"{check.Label}: {check.Summary}")
            .ToArray();

        return attentionItems.Length == 0
            ? $"{result.Detail} All Windows-side prerequisites and the current Quest Wi-Fi transport path that the public app can verify are present."
            : $"{result.Detail} {string.Join(" ", attentionItems)}";
    }

    private static string BuildWindowsEnvironmentActionDetail(WindowsEnvironmentAnalysisResult result)
        => string.Join(
            Environment.NewLine,
            new[] { result.Detail }
                .Concat(result.Checks.Select(check => $"{check.Label}: {check.Summary}")));

    private void ApplyWifiRouterDiagnosticsResult(QuestWifiTransportDiagnosticsResult result)
    {
        WifiRouterDiagnosticsLevel = result.Level;
        WifiRouterDiagnosticsSummary = result.Summary;
        WifiRouterDiagnosticsDetail = result.Detail;
        WifiRouterDiagnosticsTimestampLabel = $"Last checked {result.CheckedAtUtc.ToLocalTime():HH:mm:ss}.";
    }

    private void ApplyDeviceProfileDiagnosticsResult(DeviceProfileStatus status)
    {
        DeviceProfileDiagnosticsLevel = status.IsActive ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning;
        DeviceProfileDiagnosticsSummary = status.Summary;
        DeviceProfileDiagnosticsDetail = status.Detail;
        DeviceProfileDiagnosticsTimestampLabel = $"Last checked {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
        ReplaceStudyStatusRows(
            DeviceProfileDiagnosticsRows,
            status.Properties.Select(property => new StudyStatusRowViewModel(
                new StudyStatusRow(
                    Label: FormatDeviceProfilePropertyLabel(property.Key),
                    Key: property.Key,
                    Value: string.IsNullOrWhiteSpace(property.ReportedValue) ? "Not reported" : property.ReportedValue,
                    Expected: property.ExpectedValue,
                    Detail: BuildDeviceProfilePropertyDetail(property),
                    Level: property.Matches ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning)))
            .ToArray());
    }

    private async Task OpenDiagnosticsReportFolderAsync()
    {
        await OpenOperatorPathAsync(
            DiagnosticsReportFolderPath,
            isDirectory: true,
            actionLabel: "Open Diagnostics Report Folder",
            unavailableSummary: "Diagnostics report folder is not available yet.",
            unavailableDetail: "Generate the diagnostics report first so the JSON, LaTeX, and PDF artifacts exist.").ConfigureAwait(false);
    }

    private async Task OpenDiagnosticsReportPdfAsync()
    {
        await OpenOperatorPathAsync(
            DiagnosticsReportPdfPath,
            isDirectory: false,
            actionLabel: "Open Diagnostics Report PDF",
            unavailableSummary: "Diagnostics report PDF is not available yet.",
            unavailableDetail: "Generate the diagnostics report first so the shareable PDF exists.").ConfigureAwait(false);
    }

    private async Task OpenLocalAgentWorkspaceAsync()
    {
        LocalAgentWorkspaceSnapshot snapshot;
        try
        {
            snapshot = await Task.Run(() => _localAgentWorkspaceService.EnsureWorkspace()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ApplyOutcomeAsync(
                "Open Local Agent Workspace",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Local agent workspace could not be prepared.",
                    ex.Message)).ConfigureAwait(false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = snapshot.RootPath,
                UseShellExecute = true
            });

            await ApplyOutcomeAsync(
                "Open Local Agent Workspace",
                new OperationOutcome(
                    snapshot.HasBundledCli ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning,
                    snapshot.HasBundledCli
                        ? "Local agent workspace ready."
                        : "Local agent workspace opened, but the bundled CLI mirror is missing.",
                    snapshot.HasBundledCli
                        ? $"Open your local agent in {snapshot.RootPath}. This workspace mirrors the bundled CLI, CLI docs, and public DOPE sample catalogs outside WindowsApps."
                        : $"Open your local agent in {snapshot.RootPath}. The mirrored docs and sample catalogs are ready, but the bundled CLI payload was not mirrored on this machine.",
                    Items: [snapshot.RootPath])).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ApplyOutcomeAsync(
                "Open Local Agent Workspace",
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Local agent workspace could not be opened.",
                    ex.Message,
                    Items: [snapshot.RootPath])).ConfigureAwait(false);
        }
    }

    private async Task RestartMonitorAsync()
    {
        UpdateLslRuntimeState();
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = new CancellationTokenSource();

        if (!int.TryParse(MonitorChannelIndexText, out var channelIndex))
        {
            channelIndex = 0;
            await DispatchAsync(() => MonitorChannelIndexText = "0").ConfigureAwait(false);
        }

        var subscription = new LslMonitorSubscription(
            MonitorStreamName.Trim(),
            MonitorStreamType.Trim(),
            Math.Max(0, channelIndex));

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var reading in _monitorService.MonitorAsync(subscription, _monitorCts.Token).ConfigureAwait(false))
                {
                    await DispatchAsync(() =>
                    {
                        MonitorSummary = reading.Status;
                        MonitorDetail = reading.Detail;
                        MonitorValue = reading.Value ?? 0f;
                        MonitorSampleRateHz = reading.SampleRateHz;
                        NotifyOverviewStateChanged();
                    }).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await DispatchAsync(() =>
                {
                    MonitorSummary = "LSL monitor failed.";
                    MonitorDetail = ex.Message;
                    NotifyOverviewStateChanged();
                    AppendLog(OperatorLogLevel.Failure, "LSL monitor failed.", ex.Message);
                }).ConfigureAwait(false);
            }
        });

        await DispatchAsync(() => AppendLog(
            OperatorLogLevel.Info,
            "LSL monitor restarted.",
            $"{subscription.StreamName} / {subscription.StreamType} channel {subscription.ChannelIndex}")).ConfigureAwait(false);
    }

    private void UpdateLslRuntimeState()
    {
        var runtimeState = _monitorService.RuntimeState;
        LslRuntimeSummary = runtimeState.Available
            ? "liblsl runtime ready."
            : "liblsl runtime unavailable.";
        LslRuntimeDetail = runtimeState.Detail;
    }

    private async Task ExportManifestAsync()
    {
        var runtimeConfigId = await DispatchAsync(() => _runtimeConfig.SelectedProfile?.Id).ConfigureAwait(false);
        var runtimeConfigSummary = await DispatchAsync(() => _runtimeConfig.SelectedProfileSummary).ConfigureAwait(false);

        var snapshot = new SessionManifestSnapshot(
            CatalogStatus,
            CatalogSourcePath,
            EndpointDraft,
            EndpointDraft,
            SelectedApp?.Id,
            SelectedBundle?.Id,
            SelectedHotloadProfile?.Id,
            runtimeConfigId,
            SelectedDeviceProfile?.Id,
            ConnectionSummary,
            runtimeConfigSummary,
            RemoteOnlyControlEnabled,
            MonitorSummary,
            MonitorDetail,
            MonitorValue,
            MonitorSampleRateHz,
            TwinBridgeSummary,
            TwinBridgeDetail,
            LastActionLabel,
            LastActionDetail,
            BrowserUrlDraft,
            Logs.Take(20).ToArray());

        var outputPath = await _manifestWriter.WriteAsync(snapshot).ConfigureAwait(false);
        await DispatchAsync(() =>
        {
            LatestManifestPath = outputPath;
            AppendLog(OperatorLogLevel.Info, "Session manifest written.", outputPath);
        }).ConfigureAwait(false);
    }

    private async Task ExportRuntimeConfigAsync()
    {
        try
        {
            var outputPath = await _runtimeConfig.ExportAsync().ConfigureAwait(false);
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Info, "Runtime config exported.", outputPath)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Failure, "Runtime config export failed.", ex.Message)).ConfigureAwait(false);
        }
    }

    private async Task ResetRuntimeConfigAsync()
    {
        await DispatchAsync(() =>
        {
            _runtimeConfig.ResetSelectedProfile();
            AppendLog(OperatorLogLevel.Info, "Runtime config restored.", _runtimeConfig.SelectedProfileSummary);
        }).ConfigureAwait(false);
    }

    private async Task ImportDopeParticleTuningAsync()
    {
        var initialDirectory = Path.GetDirectoryName(AppAssetLocator.TryResolveDopeParticleSizeTemplatePath() ?? string.Empty) ?? string.Empty;
        var selectedPath = await DispatchAsync(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Dope Particle Size Tuning V1 JSON",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : string.Empty
            };

            return dialog.ShowDialog() == true ? dialog.FileName : string.Empty;
        }).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(selectedPath).ConfigureAwait(false);
            var document = _dopeParticleSizeTuningCompiler.Parse(json);
            await DispatchAsync(() =>
            {
                _dopeParticleSizeTuningDocument = document;
                DopeParticleTuningSourcePath = selectedPath;
                DopeParticleTuningCompiledPath = "No compiled Dope particle-size hotload CSV written yet.";
                DopeParticleTuningLevel = OperationOutcomeKind.Success;
                DopeParticleTuningSummary = "Imported Dope particle-size tuning V1 file.";
                DopeParticleTuningDetail = $"Loaded {Path.GetFileName(selectedPath)}. The companion will compile it onto the live Dope runtime JSON baseline and upload the full payload through the normal hotload CSV path.";
                RefreshDopeParticleTuningState();
                AppendLog(
                    OperatorLogLevel.Info,
                    "Dope particle-size tuning imported.",
                    $"{Path.GetFileName(selectedPath)} | min {document.ParticleSizeMinimum.Value:0.###} | max {document.ParticleSizeMaximum.Value:0.###}");
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await DispatchAsync(() =>
            {
                DopeParticleTuningLevel = OperationOutcomeKind.Failure;
                DopeParticleTuningSummary = "Dope particle-size tuning import failed.";
                DopeParticleTuningDetail = ex.Message;
                AppendLog(OperatorLogLevel.Failure, "Dope particle-size tuning import failed.", ex.Message);
            }).ConfigureAwait(false);
        }
    }

    private async Task ApplyDopeParticleTuningAsync()
    {
        if (_dopeParticleSizeTuningDocument is null)
        {
            await DispatchAsync(() => AppendLog(
                    OperatorLogLevel.Warning,
                    "Dope particle-size apply blocked.",
                    "Import a Dope particle-size tuning V1 JSON file first."))
                .ConfigureAwait(false);
            return;
        }

        if (SelectedApp is null)
        {
            await DispatchAsync(() => AppendLog(
                    OperatorLogLevel.Warning,
                    "Dope particle-size apply blocked.",
                    "Select the Dope Experiment target app first."))
                .ConfigureAwait(false);
            return;
        }

        if (!string.Equals(SelectedApp.PackageId, _dopeParticleSizeTuningDocument.PackageId, StringComparison.OrdinalIgnoreCase))
        {
            await DispatchAsync(() => AppendLog(
                    OperatorLogLevel.Warning,
                    "Dope particle-size apply blocked.",
                    $"The imported tuning file targets {_dopeParticleSizeTuningDocument.PackageId}, but the current app target is {SelectedApp.PackageId}."))
                .ConfigureAwait(false);
            return;
        }

        if (!TryGetLiveDopeRuntimeConfigJson(out var baselineRuntimeConfigJson, out var baselineDetail))
        {
            await DispatchAsync(() =>
            {
                DopeParticleTuningLevel = OperationOutcomeKind.Warning;
                DopeParticleTuningSummary = "Dope particle-size apply blocked.";
                DopeParticleTuningDetail = baselineDetail;
                AppendLog(OperatorLogLevel.Warning, "Dope particle-size apply blocked.", baselineDetail);
            }).ConfigureAwait(false);
            return;
        }

        try
        {
            var compiled = _dopeParticleSizeTuningCompiler.Compile(_dopeParticleSizeTuningDocument, baselineRuntimeConfigJson);
            var stagedIntoInspector = await DispatchAsync(() => _runtimeConfig.TrySetValue(compiled.HotloadTargetKey, compiled.PrettyRuntimeConfigJson)).ConfigureAwait(false);

            var version = DateTimeOffset.UtcNow.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture);
            var profileId = $"dope_particle_size_v1_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}";
            var description = $"Compiled from {Path.GetFileName(DopeParticleTuningSourcePath)}. Only ParticleSizeEnvelopeLimits.x/y were changed.";
            var runtimeProfile = new RuntimeConfigProfile(
                profileId,
                "Dope Particle Size V1",
                string.Empty,
                version,
                "study",
                false,
                description,
                [SelectedApp.PackageId],
                [new RuntimeConfigEntry(compiled.HotloadTargetKey, compiled.CompactRuntimeConfigJson)]);

            var csvPath = await _runtimeConfigWriter.WriteAsync(runtimeProfile).ConfigureAwait(false);
            var hotloadProfile = new HotloadProfile(
                runtimeProfile.Id,
                runtimeProfile.Label,
                csvPath,
                runtimeProfile.Version,
                runtimeProfile.Channel,
                runtimeProfile.StudyLock,
                runtimeProfile.Description,
                runtimeProfile.PackageIds);

            var uploadOutcome = await _questService.ApplyHotloadProfileAsync(hotloadProfile, SelectedApp).ConfigureAwait(false);
            var detail = stagedIntoInspector
                ? $"{uploadOutcome.Detail} The compiled full runtime JSON was also staged into the Runtime Config inspector under {compiled.HotloadTargetKey}."
                : $"{uploadOutcome.Detail} The compiled full runtime JSON was uploaded, but the Runtime Config inspector could not be updated locally.";
            var combinedOutcome = new OperationOutcome(
                uploadOutcome.Kind,
                uploadOutcome.Summary,
                detail,
                uploadOutcome.Endpoint,
                uploadOutcome.PackageId,
                [csvPath]);

            await DispatchAsync(() =>
            {
                DopeParticleTuningCompiledPath = csvPath;
                DopeParticleTuningLevel = combinedOutcome.Kind;
                DopeParticleTuningSummary = combinedOutcome.Summary;
                DopeParticleTuningDetail = combinedOutcome.Detail;
                RefreshDopeParticleTuningState();
            }).ConfigureAwait(false);

            await ApplyOutcomeAsync("Apply Dope Particle Size Tuning", combinedOutcome).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await DispatchAsync(() =>
            {
                DopeParticleTuningLevel = OperationOutcomeKind.Failure;
                DopeParticleTuningSummary = "Dope particle-size apply failed.";
                DopeParticleTuningDetail = ex.Message;
                AppendLog(OperatorLogLevel.Failure, "Dope particle-size apply failed.", ex.Message);
            }).ConfigureAwait(false);
        }
    }

    private async Task ApplyTwinPresetAsync()
    {
        if (SelectedApp is null || SelectedHotloadProfile is null)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Twin preset blocked.", "Select both an app target and a runtime preset first.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _twinBridge.ApplyConfigAsync(SelectedHotloadProfile, SelectedApp).ConfigureAwait(false);
        await ApplyOutcomeAsync("Track Runtime Preset", outcome).ConfigureAwait(false);
    }

    private async Task PublishRuntimeConfigAsync()
    {
        if (SelectedApp is null)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Runtime config publish blocked.", "Select an app target first.")).ConfigureAwait(false);
            return;
        }

        var selectedProfile = await DispatchAsync(() => _runtimeConfig.SelectedProfile).ConfigureAwait(false);
        if (selectedProfile is null)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Runtime config publish blocked.", "Select a runtime config profile for the current target app first.")).ConfigureAwait(false);
            return;
        }

        if (!selectedProfile.MatchesPackage(SelectedApp.PackageId))
        {
            await DispatchAsync(() => AppendLog(
                OperatorLogLevel.Warning,
                "Runtime config publish blocked.",
                $"The selected runtime config profile targets {FormatPackageTargets(selectedProfile.PackageIds)}, but the current selected app is {SelectedApp.PackageId}. Select the runtime app whose profile you want to publish.")).ConfigureAwait(false);
            return;
        }

        try
        {
            var profile = await DispatchAsync(() => _runtimeConfig.BuildEditedProfile()).ConfigureAwait(false);
            var outcome = await _twinBridge.PublishRuntimeConfigAsync(profile, SelectedApp).ConfigureAwait(false);
            await ApplyOutcomeAsync("Publish Runtime Config", outcome).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Failure, "Runtime config publish failed.", ex.Message)).ConfigureAwait(false);
        }
    }

    private async Task SendTwinCommandAsync(object? parameter)
    {
        var key = parameter as string;
        if (string.IsNullOrWhiteSpace(key) || !_twinCommands.TryGetValue(key, out var command))
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Twin command blocked.", "Unknown twin command id.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _twinBridge.SendCommandAsync(command).ConfigureAwait(false);
        await ApplyOutcomeAsync(command.DisplayName, outcome).ConfigureAwait(false);
    }

    private async Task RunUtilityAsync(object? parameter)
    {
        if (parameter is not QuestUtilityAction action)
        {
            await DispatchAsync(() => AppendLog(OperatorLogLevel.Warning, "Utility action blocked.", "Unknown Quest utility action.")).ConfigureAwait(false);
            return;
        }

        var outcome = await _questService.RunUtilityAsync(action).ConfigureAwait(false);
        await ApplyOutcomeAsync(action.ToString(), outcome).ConfigureAwait(false);
        await RefreshHeadsetStatusAsync().ConfigureAwait(false);
    }

    private void RefreshLiveTwinState()
    {
        if (_twinBridge is not LslTwinModeBridge lslBridge)
        {
            _latestTwinDeltas = Array.Empty<TwinSettingsDelta>();
            TwinReportedState.Clear();
            TwinLiveEvents.Clear();
            TwinInspectorRows.Clear();
            SelectedTwinInspectorRow = null;
            TwinAppStateSummary = _twinBridge.Status.Summary;
            TwinAppStateDetail = _twinBridge.Status.Detail;
            TwinAppStateLevel = OperationOutcomeKind.Preview;
            LiveTwinPublisherLabel = "Twin publisher unavailable.";
            LiveTwinPublisherDetail = _twinBridge.Status.Detail;
            TwinPublisherLevel = OperationOutcomeKind.Preview;
            _liveTwinPublisherPackageId = null;
            LastTwinStateTimestampLabel = "No live app-state timestamp yet.";
            SettingsDelta.Clear();
            RefreshDopeParticleTuningState();
            OnPropertyChanged(nameof(TwinInspectorScopeSummary));
            OnPropertyChanged(nameof(TwinInspectorMatchLabel));
            OnPropertyChanged(nameof(TwinInspectorMatchPercent));
            RefreshRuntimeContextLabels();
            NotifyOverviewStateChanged();
            return;
        }

        var reportedSettings = lslBridge.ReportedSettings
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var deltas = lslBridge.ComputeSettingsDelta();
        var liveEvents = lslBridge.StateEvents
            .OrderByDescending(stateEvent => stateEvent.Timestamp)
            .Take(24)
            .ToArray();
        _latestTwinDeltas = deltas;

        TwinReportedState.Clear();
        foreach (var entry in reportedSettings)
        {
            TwinReportedState.Add(new KeyValueStatusRow(entry.Key, entry.Value, ClassifyTwinStateSource(entry.Key)));
        }

        TwinLiveEvents.Clear();
        foreach (var stateEvent in liveEvents)
        {
            TwinLiveEvents.Add(stateEvent);
        }

        SettingsDelta.Clear();
        foreach (var delta in deltas)
        {
            SettingsDelta.Add(delta);
        }

        _runtimeConfig.ApplyTwinDelta(deltas);
        RefreshTwinInspectorRows();
        TwinAppStateSummary = DescribeTwinAppState(reportedSettings);
        TwinAppStateDetail = reportedSettings.Length == 0
            ? "No live quest_twin_state values have been reported yet from the APK."
            : $"Reported {reportedSettings.Length} value(s); requested/reported matches {deltas.Count(delta => delta.Matches)}/{deltas.Count}.";
        TwinAppStateLevel = reportedSettings.Length == 0
            ? OperationOutcomeKind.Preview
            : OperationOutcomeKind.Success;
        var publisherContext = DescribeTwinPublisher(reportedSettings);
        var hasLiveStateActivity = lslBridge.LastStateReceivedAt is not null || liveEvents.Length > 0;
        if (hasLiveStateActivity && publisherContext.PackageId is null && !string.IsNullOrWhiteSpace(_activeForegroundPackageId))
        {
            publisherContext = (
                $"{DescribeAppIdentity(_activeForegroundPackageId, _activeForegroundComponent)} publishing over LSL",
                $"quest_twin_state is active, but the APK did not include its package id in the state frame. Falling back to the current ADB active app {_activeForegroundPackageId}.",
                _activeForegroundPackageId);
        }

        _liveTwinPublisherPackageId = publisherContext.PackageId;
        LiveTwinPublisherLabel = publisherContext.Label;
        LiveTwinPublisherDetail = publisherContext.Detail;
        TwinPublisherLevel = hasLiveStateActivity
            ? string.IsNullOrWhiteSpace(publisherContext.PackageId)
                ? OperationOutcomeKind.Warning
                : OperationOutcomeKind.Success
            : OperationOutcomeKind.Preview;
        LastTwinStateTimestampLabel = lslBridge.LastStateReceivedAt is null
            ? "No live LSL app-state timestamp yet."
            : $"Last app-state frame {lslBridge.LastStateReceivedAt.Value.ToLocalTime():HH:mm:ss}.";
        RefreshDopeParticleTuningState();
        RefreshRuntimeContextLabels();
        RefreshTwinBridgeStatus(deltas);
        NotifyOverviewStateChanged();
    }

    private async Task ApplyOutcomeAsync(
        string actionLabel,
        OperationOutcome outcome,
        Action<string>? summaryTarget = null)
    {
        await DispatchAsync(() =>
        {
            LastActionLabel = actionLabel;
            LastActionDetail = outcome.Detail;
            LastActionKind = outcome.Kind;
            summaryTarget?.Invoke(outcome.Summary);
            AppendLog(MapLevel(outcome.Kind), outcome.Summary, outcome.Detail);
        }).ConfigureAwait(false);
    }

    private StudyShellDefinition? ResolveDiagnosticsStudyDefinition()
    {
        if (StudyShells.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(SelectedApp?.PackageId))
        {
            var selectedAppMatch = StudyShells.FirstOrDefault(
                study => string.Equals(study.App.PackageId, SelectedApp.PackageId, StringComparison.OrdinalIgnoreCase));
            if (selectedAppMatch is not null)
            {
                return selectedAppMatch;
            }
        }

        return StudyShells.FirstOrDefault(study => string.Equals(study.Id, "dope-projected-feed-colorama", StringComparison.OrdinalIgnoreCase))
            ?? StudyShells.FirstOrDefault(study => string.Equals(study.App.PackageId, "com.tillh.dynamicoscillatorypatternentrainment", StringComparison.OrdinalIgnoreCase))
            ?? StudyShells[0];
    }

    private DeviceProfile? ResolveDiagnosticsDeviceProfile()
        => SelectedDeviceProfile ?? (ResolveDiagnosticsStudyDefinition() is { } study ? StudyShellOperatorBindings.CreateDeviceProfile(study) : null);

    private string ResolveHeadsetActionSelector()
    {
        foreach (var candidate in new[]
                 {
                     _latestHeadsetStatus?.IsConnected == true ? _latestHeadsetStatus.ConnectionLabel : null,
                     string.IsNullOrWhiteSpace(EndpointDraft) ? null : EndpointDraft.Trim(),
                     _sessionState.ActiveEndpoint
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static string FormatDeviceProfilePropertyLabel(string key)
        => key switch
        {
            "debug.oculus.cpuLevel" => "CPU level",
            "debug.oculus.gpuLevel" => "GPU level",
            "dope.screen_brightness_percent" => "Screen brightness (%)",
            "dope.media_volume_music" => "Media volume",
            "dope.minimum_headset_battery_percent" => "Minimum headset battery (%)",
            "dope.minimum_right_controller_battery_percent" => "Minimum right controller battery (%)",
            _ => key
        };

    private static string BuildDeviceProfilePropertyDetail(DevicePropertyStatus property)
        => property.Key switch
        {
            "dope.media_volume_music" when property.Matches
                => "Pinned Quest media volume matches.",
            "dope.media_volume_music"
                => "Pinned Quest media volume differs from the current headset value. If Apply Device Profile does not fix it, set volume manually on the headset and refresh.",
            _ => property.Matches
                ? "Pinned Quest property matches."
                : "Pinned Quest property differs from the current headset value."
        };

    private static string NormalizeHostVisibleOperatorPath(string? path)
        => CompanionOperatorDataLayout.NormalizeHostVisiblePath(path);

    private async Task OpenOperatorPathAsync(
        string path,
        bool isDirectory,
        string actionLabel,
        string unavailableSummary,
        string unavailableDetail)
    {
        var resolvedPath = isDirectory
            ? CompanionOperatorDataLayout.TryResolveExistingDirectory(path, out var directoryPath) ? directoryPath : null
            : CompanionOperatorDataLayout.TryResolveExistingFile(path, out var filePath) ? filePath : null;

        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            await ApplyOutcomeAsync(
                actionLabel,
                new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    unavailableSummary,
                    unavailableDetail,
                    Items: string.IsNullOrWhiteSpace(path) ? [] : [NormalizeHostVisibleOperatorPath(path)])).ConfigureAwait(false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = resolvedPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ApplyOutcomeAsync(
                actionLabel,
                new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    $"{actionLabel} failed.",
                    ex.Message,
                    Items: [resolvedPath])).ConfigureAwait(false);
        }
    }

    private async Task<OperationOutcome> GenerateDiagnosticsReportPdfAsync(string jsonPath, string outputPdfPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Diagnostics PDF skipped.",
                "The diagnostics JSON report was not written.");
        }

        var scriptPath = TryResolveDiagnosticsPdfScriptPath();
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Diagnostics PDF generator was not found.",
                "The JSON and LaTeX diagnostics reports were written, but the bundled PDF script could not be resolved.");
        }

        foreach (var (fileName, prefixArgs) in new[]
                 {
                     ("py", new[] { "-3", scriptPath }),
                     ("python", new[] { scriptPath })
                 })
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                foreach (var arg in prefixArgs)
                {
                    process.StartInfo.ArgumentList.Add(arg);
                }

                process.StartInfo.ArgumentList.Add("--input-json");
                process.StartInfo.ArgumentList.Add(jsonPath);
                process.StartInfo.ArgumentList.Add("--output-pdf");
                process.StartInfo.ArgumentList.Add(outputPdfPath);

                process.Start();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                var completed = await Task.Run(() => process.WaitForExit((int)TimeSpan.FromSeconds(45).TotalMilliseconds)).ConfigureAwait(false);
                if (!completed)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    return new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Diagnostics PDF generation timed out.",
                        "The JSON and LaTeX diagnostics reports were written; open the report folder to share those files.");
                }

                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                if (process.ExitCode == 0 && File.Exists(outputPdfPath))
                {
                    return new OperationOutcome(
                        OperationOutcomeKind.Success,
                        "Diagnostics PDF generated.",
                        string.IsNullOrWhiteSpace(stdout) ? outputPdfPath : stdout.Trim(),
                        Items: [outputPdfPath]);
                }

                if (fileName == "python")
                {
                    return new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Diagnostics PDF generation failed.",
                        string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text))).Trim());
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                if (fileName == "python")
                {
                    return new OperationOutcome(
                        OperationOutcomeKind.Warning,
                        "Python was not available for diagnostics PDF generation.",
                        "The JSON and LaTeX diagnostics reports were written. Install Python with matplotlib to generate the PDF locally.");
                }
            }
        }

        return new OperationOutcome(
            OperationOutcomeKind.Warning,
            "Diagnostics PDF was not generated.",
            "The JSON and LaTeX diagnostics reports were written, but no Python runtime completed the bundled PDF script.");
    }

    private static string? TryResolveDiagnosticsPdfScriptPath()
        => new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "reports", "generate_dope_diagnostics_pdf.py")),
            Path.Combine(AppContext.BaseDirectory, "tools", "reports", "generate_dope_diagnostics_pdf.py"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source",
                "repos",
                "DopeCompanion",
                "tools",
                "reports",
                "generate_dope_diagnostics_pdf.py")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
        .Select(Path.GetFullPath)
        .FirstOrDefault();

    private static void ReplaceWorkflowGuideCheckItems(
        ObservableCollection<WorkflowGuideCheckItem> target,
        IReadOnlyList<WorkflowGuideCheckItem> source)
    {
        var canUpdateInPlace = target.Count == source.Count
            && target.Zip(source, (existing, incoming) => string.Equals(existing.Label, incoming.Label, StringComparison.Ordinal))
                .All(matches => matches);

        if (!canUpdateInPlace)
        {
            ReplaceItems(target, source);
            return;
        }

        for (var index = 0; index < source.Count; index++)
        {
            target[index].UpdateFrom(source[index]);
        }
    }

    private static void ReplaceStudyStatusRows(
        ObservableCollection<StudyStatusRowViewModel> target,
        IReadOnlyList<StudyStatusRowViewModel> source)
    {
        var canUpdateInPlace = target.Count == source.Count
            && target.Zip(source, (existing, incoming) => string.Equals(existing.Key, incoming.Key, StringComparison.Ordinal))
                .All(matches => matches);

        if (!canUpdateInPlace)
        {
            ReplaceItems(target, source);
            return;
        }

        for (var index = 0; index < source.Count; index++)
        {
            target[index].Apply(new StudyStatusRow(
                source[index].Label,
                source[index].Key,
                source[index].Value,
                source[index].Expected,
                source[index].Detail,
                source[index].Level));
        }
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void RefreshTwinBridgeStatus(IReadOnlyList<TwinSettingsDelta> deltas)
    {
        if (_twinBridge is not LslTwinModeBridge lslBridge)
        {
            TwinBridgeSummary = _twinBridge.Status.Summary;
            TwinBridgeDetail = _twinBridge.Status.Detail;
            TwinBridgeLevel = OperationOutcomeKind.Preview;
            NotifyOverviewStateChanged();
            return;
        }

        var requestedCount = lslBridge.RequestedSettings.Count;
        var reportedCount = lslBridge.ReportedSettings.Count;
        var selectedSupportsTwin = SelectedApp?.Tags.Any(tag => tag.Equals("twin", StringComparison.OrdinalIgnoreCase)) == true;

        if (requestedCount == 0 && reportedCount == 0)
        {
            TwinBridgeSummary = selectedSupportsTwin
                ? "Twin bridge idle."
                : "Runtime tracking unavailable for the current app.";
            TwinBridgeDetail = selectedSupportsTwin
                ? "No runtime-config snapshot or twin command has been sent yet."
                : "The selected app supports runtime editing, but requested/reported setting diffs only appear when a twin-enabled headset app publishes `quest_twin_state`.";
            TwinBridgeLevel = selectedSupportsTwin ? OperationOutcomeKind.Preview : OperationOutcomeKind.Warning;
            NotifyOverviewStateChanged();
            return;
        }

        if (requestedCount > 0 && reportedCount == 0)
        {
            TwinBridgeSummary = "Runtime config published; waiting for headset state.";
            TwinBridgeDetail = selectedSupportsTwin
                ? $"{requestedCount} requested setting(s) are staged locally, but the headset has not reported any `quest_twin_state` values yet. Active headset state: {HeadsetActivityLabel}."
                : $"{requestedCount} requested setting(s) are staged locally, but the selected app is not marked as twin-enabled. Switch to a twin-capable app such as LslTwin to get requested/reported tracking.";
            TwinBridgeLevel = OperationOutcomeKind.Warning;
            NotifyOverviewStateChanged();
            return;
        }

        var matchedCount = deltas.Count(delta => delta.Matches);
        TwinBridgeSummary = $"Tracking {reportedCount} headset setting(s).";
        TwinBridgeDetail = $"Requested {requestedCount}, reported {reportedCount}, matched {matchedCount}. Active headset state: {HeadsetActivityLabel}.";
        TwinBridgeLevel = matchedCount == deltas.Count && deltas.Count > 0
            ? OperationOutcomeKind.Success
            : OperationOutcomeKind.Warning;
        NotifyOverviewStateChanged();
    }

    private async Task<OperationOutcome> QueryHzdbSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!_hzdbService.IsAvailable)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Preview,
                "hzdb not available.",
                "Run guided setup or install the official Quest tooling cache before collecting extra device details on request.");
        }

        var selectors = ResolveHzdbSelectorCandidates();
        if (selectors.Count == 0)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "hzdb selector unavailable.",
                "No live Wi-Fi endpoint or USB serial is available for an hzdb query yet.");
        }

        try
        {
            OperationOutcome? fallback = null;
            foreach (var selector in selectors)
            {
                var outcome = await _hzdbService.GetDeviceInfoAsync(selector, cancellationToken).ConfigureAwait(false);
                if (fallback is null)
                {
                    fallback = outcome with { Detail = TrimStatusDetail(outcome.Detail) };
                }

                if (outcome.Kind != OperationOutcomeKind.Failure)
                {
                    return outcome with { Detail = TrimStatusDetail(outcome.Detail) };
                }
            }

            return fallback
                ?? new OperationOutcome(
                    OperationOutcomeKind.Warning,
                    "hzdb query failed.",
                    "The companion could not find a working Quest selector for hzdb.");
        }
        catch (Exception ex)
        {
            return new OperationOutcome(
                OperationOutcomeKind.Warning,
                "hzdb query failed.",
                TrimStatusDetail(ex.Message));
        }
    }

    private void OnTwinBridgeStateChanged(object? sender, EventArgs e)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            _twinRefreshPending = true;
            if (_twinRefreshTimer is null)
            {
                RefreshLiveTwinState();
                _twinRefreshPending = false;
                return;
            }

            if (!_twinRefreshTimer.IsEnabled)
            {
                _twinRefreshTimer.Start();
            }
        });
    }

    private void OnTwinRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!_twinRefreshPending)
        {
            _twinRefreshTimer?.Stop();
            return;
        }

        _twinRefreshPending = false;
        RefreshLiveTwinState();
        if (!_twinRefreshPending)
        {
            _twinRefreshTimer?.Stop();
        }
    }

    private void EnsureTwinBridgeMonitoringStarted()
    {
        if (_twinBridge is not LslTwinModeBridge lslBridge)
        {
            return;
        }

        var outcome = lslBridge.Open();
        if (outcome.Kind == OperationOutcomeKind.Failure)
        {
            TwinBridgeSummary = outcome.Summary;
            TwinBridgeDetail = outcome.Detail;
        }
    }

    private string ResolveHzdbSelector()
        => ResolveHzdbSelectorCandidates().FirstOrDefault() ?? string.Empty;

    private IReadOnlyList<string> ResolveHzdbSelectorCandidates()
    {
        var candidates = new List<string>(3);
        AddHzdbSelectorCandidate(candidates, _sessionState.ActiveEndpoint);
        AddHzdbSelectorCandidate(candidates, string.IsNullOrWhiteSpace(EndpointDraft) ? null : EndpointDraft.Trim());
        AddHzdbSelectorCandidate(candidates, _sessionState.LastUsbSerial);
        return candidates;
    }

    private static string DescribeTwinAppState(IReadOnlyList<KeyValuePair<string, string>> reportedSettings)
    {
        if (reportedSettings.Count == 0)
        {
            return "Waiting for quest_twin_state.";
        }

        static string? FindValue(IReadOnlyList<KeyValuePair<string, string>> entries, params string[] keys)
            => entries.FirstOrDefault(entry => keys.Any(key => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))).Value;

        var component = FindValue(reportedSettings, "app.component", "foreground.component", "active.component");
        if (!string.IsNullOrWhiteSpace(component))
        {
            return $"APK reports {component}.";
        }

        var packageId = FindValue(reportedSettings, "app.package", "app.packageId", "foreground.package", "active.package", "package");
        var sessionState = FindValue(reportedSettings, "session.state", "app.state", "runtime.state", "status", "state");
        var profileId = FindValue(reportedSettings, "hotload_profile_id", "profile.id", "runtime.profile");

        if (!string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(sessionState))
        {
            return $"{packageId} reports {sessionState}.";
        }

        if (!string.IsNullOrWhiteSpace(packageId))
        {
            return $"{packageId} is publishing live app state.";
        }

        if (!string.IsNullOrWhiteSpace(sessionState))
        {
            return $"APK state: {sessionState}.";
        }

        if (!string.IsNullOrWhiteSpace(profileId))
        {
            return $"Live profile {profileId} reported by the APK.";
        }

        return "Live quest_twin_state frames received.";
    }

    private static void AddHzdbSelectorCandidate(ICollection<string> candidates, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return;
        }

        var normalized = selector.Trim();
        if (candidates.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidates.Add(normalized);
    }

    private void RefreshRuntimeContextLabels()
    {
        OnPropertyChanged(nameof(RuntimeConfigPublishSummary));
        OnPropertyChanged(nameof(RuntimeConfigDeviceModeSummary));
        OnPropertyChanged(nameof(RuntimeConfigPublishChannelSummary));
        OnPropertyChanged(nameof(RuntimeConfigLiveSummary));
        OnPropertyChanged(nameof(TwinTrackingCoverageSummary));
        OnPropertyChanged(nameof(RemoteControlSelectionSummary));
        OnPropertyChanged(nameof(RemoteControlLiveContextSummary));
        NotifyOverviewStateChanged();

        if (!IsQuestConnected)
        {
            IsForegroundMismatch = false;
            ForegroundMismatchLabel = string.Empty;
            ForegroundStatusLevel = OperationOutcomeKind.Preview;
            return;
        }

        if (string.IsNullOrWhiteSpace(_activeForegroundPackageId))
        {
            IsForegroundMismatch = false;
            ForegroundMismatchLabel = "Foreground package could not be detected. The headset may be on the home screen or showing an OS overlay.";
            ForegroundStatusLevel = OperationOutcomeKind.Warning;
            return;
        }

        if (SelectedApp is null)
        {
            IsForegroundMismatch = false;
            ForegroundMismatchLabel = string.Empty;
            ForegroundStatusLevel = OperationOutcomeKind.Preview;
            return;
        }

        var selectedMatchesForeground = string.Equals(_activeForegroundPackageId, SelectedApp.PackageId, StringComparison.OrdinalIgnoreCase);
        if (selectedMatchesForeground)
        {
            IsForegroundMismatch = false;
            ForegroundMismatchLabel = string.Empty;
            ForegroundStatusLevel = OperationOutcomeKind.Success;
            return;
        }

        IsForegroundMismatch = true;
        ForegroundStatusLevel = OperationOutcomeKind.Warning;

        var foregroundLabel = DescribeAppIdentity(_activeForegroundPackageId, _activeForegroundComponent);
        var livePublisherLabel = DescribeAppIdentity(_liveTwinPublisherPackageId, null);
        var livePublisherMatchesSelected = !string.IsNullOrWhiteSpace(_liveTwinPublisherPackageId)
            && string.Equals(_liveTwinPublisherPackageId, SelectedApp.PackageId, StringComparison.OrdinalIgnoreCase);
        var livePublisherMatchesForeground = !string.IsNullOrWhiteSpace(_liveTwinPublisherPackageId)
            && string.Equals(_liveTwinPublisherPackageId, _activeForegroundPackageId, StringComparison.OrdinalIgnoreCase);

        ForegroundMismatchLabel = livePublisherMatchesSelected
            ? $"Selected target is {SelectedApp.Label}, and live LSL state also matches that target, but the current active app is {foregroundLabel}. This usually means the headset is showing a launcher transition or overlay while the selected runtime is still the one publishing quest_twin_state."
            : livePublisherMatchesForeground
                ? $"Selected target is {SelectedApp.Label}, while the headset active app and live LSL publisher are {livePublisherLabel}. Install, launch, preset staging, and Publish over Twin actions still target {SelectedApp.Label}; live runtime state reflects {livePublisherLabel} until you switch apps or change the selected target."
                : $"Selected target is {SelectedApp.Label}, but the current active app is {foregroundLabel}. Install, launch, preset staging, and Publish over Twin actions still target {SelectedApp.Label}. Live runtime state follows whichever APK is publishing quest_twin_state.";
    }

    private void NotifyOverviewStateChanged()
    {
        OnPropertyChanged(nameof(ConnectionStatusLabel));
        OnPropertyChanged(nameof(LiveSignalLevel));
        OnPropertyChanged(nameof(LiveSignalHeadline));
        OnPropertyChanged(nameof(LiveSignalDetail));
        OnPropertyChanged(nameof(OnDeviceStatusDetail));
        OnPropertyChanged(nameof(SessionHealthSummary));
    }

    private (string Label, string Detail, string? PackageId) DescribeTwinPublisher(IReadOnlyList<KeyValuePair<string, string>> reportedSettings)
    {
        if (reportedSettings.Count == 0)
        {
            return (
                "No live twin publisher detected.",
                "Once an APK publishes quest_twin_state, its package and runtime state will appear here.",
                null);
        }

        var component = FindReportedValue(reportedSettings, "app.component", "foreground.component", "active.component");
        var packageId = FindReportedValue(reportedSettings, "app.package", "app.packageId", "foreground.package", "active.package", "package");
        var sessionState = FindReportedValue(reportedSettings, "session.state", "app.state", "runtime.state", "status", "state");
        var profileId = FindReportedValue(reportedSettings, "hotload_profile_id", "profile.id", "runtime.profile");

        if (!string.IsNullOrWhiteSpace(component))
        {
            var slashIndex = component.IndexOf('/');
            if (slashIndex > 0)
            {
                packageId ??= component[..slashIndex];
            }

            return (
                $"{DescribeAppIdentity(packageId, component)} publishing over LSL",
                $"Live quest_twin_state reports active component {component}.",
                packageId);
        }

        if (!string.IsNullOrWhiteSpace(packageId) && !string.IsNullOrWhiteSpace(sessionState))
        {
            return (
                $"{DescribeAppIdentity(packageId, null)} publishing over LSL",
                $"Live quest_twin_state reports runtime state {sessionState}.",
                packageId);
        }

        if (!string.IsNullOrWhiteSpace(packageId))
        {
            return (
                $"{DescribeAppIdentity(packageId, null)} publishing over LSL",
                "Live quest_twin_state frames are arriving from this package.",
                packageId);
        }

        return (
            "Unknown APK publishing over LSL",
            string.IsNullOrWhiteSpace(profileId)
                ? "Live quest_twin_state frames arrived, but the APK did not report its package id."
                : $"Live quest_twin_state frames arrived for runtime profile {profileId}, but the APK did not report its package id.",
            null);
    }

    private static string? FindReportedValue(IReadOnlyList<KeyValuePair<string, string>> entries, params string[] keys)
        => entries.FirstOrDefault(entry => keys.Any(key => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))).Value;

    private bool TryAdoptDetectedTarget(string? packageId)
    {
        if (SelectedApp is not null || string.IsNullOrWhiteSpace(packageId))
        {
            return false;
        }

        var knownApp = Apps.FirstOrDefault(app => string.Equals(app.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        if (knownApp is null)
        {
            return false;
        }

        SelectedApp = knownApp;
        AppendLog(
            OperatorLogLevel.Info,
            "Selected target adopted from headset state.",
            $"{knownApp.Label} became the selected target because it is the current active app on the headset.");
        return true;
    }

    private string DescribeAppIdentity(string? packageId, string? component)
    {
        if (!string.IsNullOrWhiteSpace(component))
        {
            var slashIndex = component.IndexOf('/');
            if (slashIndex > 0)
            {
                packageId ??= component[..slashIndex];
            }
        }

        if (!string.IsNullOrWhiteSpace(packageId))
        {
            var knownApp = Apps.FirstOrDefault(app => string.Equals(app.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            if (knownApp is not null)
            {
                return knownApp.Label;
            }

            return packageId;
        }

        return string.IsNullOrWhiteSpace(component) ? "unknown app" : component;
    }

    private static string FormatPackageTargets(IReadOnlyList<string> packageIds)
        => packageIds.Count == 0
            ? "no declared package targets"
            : string.Join(", ", packageIds);

    private void OnRuntimeConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RuntimeConfigWorkspaceViewModel.SelectedProfile)
            or nameof(RuntimeConfigWorkspaceViewModel.SelectedProfileSummary)
            or nameof(RuntimeConfigWorkspaceViewModel.SelectedProfileLabel))
        {
            RefreshRuntimeContextLabels();
        }
    }

    private void OnActiveStudyShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StudyShellViewModel.ConnectionSummary):
            case nameof(StudyShellViewModel.QuestStatusSummary):
            case nameof(StudyShellViewModel.QuestStatusDetail):
            case nameof(StudyShellViewModel.PinnedBuildSummary):
            case nameof(StudyShellViewModel.PinnedBuildLevel):
            case nameof(StudyShellViewModel.LiveRuntimeSummary):
            case nameof(StudyShellViewModel.LiveRuntimeDetail):
            case nameof(StudyShellViewModel.LiveRuntimeLevel):
            case nameof(StudyShellViewModel.LastTwinStateTimestampLabel):
                OnPropertyChanged(nameof(TargetSelectionHeadline));
                OnPropertyChanged(nameof(TargetSelectionDetail));
                OnPropertyChanged(nameof(SessionHealthSummary));
                OnPropertyChanged(nameof(OnDeviceStatusDetail));
                OnPropertyChanged(nameof(LiveSignalHeadline));
                OnPropertyChanged(nameof(LiveSignalDetail));
                OnPropertyChanged(nameof(HeaderTargetLevel));
                OnPropertyChanged(nameof(HeaderConnectionLevel));
                OnPropertyChanged(nameof(HeaderLiveLevel));
                break;
        }
    }

    private void NotifyStudyModeStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveStudyShell));
        OnPropertyChanged(nameof(HasLockedStartupStudy));
        OnPropertyChanged(nameof(IsStudyModeLocked));
        OnPropertyChanged(nameof(CanExitStudyMode));
        OnPropertyChanged(nameof(ShowOperatorHeaderUtilities));
        OnPropertyChanged(nameof(ShowStandardOperatorSurface));
        OnPropertyChanged(nameof(ShowStudyShellStartupLoading));
        OnPropertyChanged(nameof(PrimaryWorkflowTabLabel));
        OnPropertyChanged(nameof(CurrentModeLabel));
        OnPropertyChanged(nameof(CurrentModeDetail));
        OnPropertyChanged(nameof(ShowExpandedStudyBanner));
        OnPropertyChanged(nameof(StudyBannerToggleLabel));
        OnPropertyChanged(nameof(HeaderModeSummary));
        OnPropertyChanged(nameof(TargetSelectionHeadline));
        OnPropertyChanged(nameof(TargetSelectionDetail));
        OnPropertyChanged(nameof(SessionHealthSummary));
        OnPropertyChanged(nameof(OnDeviceStatusDetail));
        OnPropertyChanged(nameof(LiveSignalHeadline));
        OnPropertyChanged(nameof(LiveSignalDetail));
        OnPropertyChanged(nameof(HeaderTargetLevel));
        OnPropertyChanged(nameof(HeaderConnectionLevel));
        OnPropertyChanged(nameof(HeaderLiveLevel));
        OnPropertyChanged(nameof(ShowStudyModeAction));
    }

    private static string BuildSelectedAppCapabilitySummary(QuestAppTarget app)
    {
        if (app.CompatibilityStatus == ApkCompatibilityStatus.Incompatible)
        {
            return "Capability classification blocked: this APK hash is marked incompatible with the current Windows operator build.";
        }

        var tags = app.Tags;
        var capabilities = new List<string>();
        if (tags.Any(tag => tag.Equals("runtime", StringComparison.OrdinalIgnoreCase)))
        {
            capabilities.Add("runtime-config publish");
        }

        if (tags.Any(tag => tag.Equals("twin", StringComparison.OrdinalIgnoreCase)))
        {
            capabilities.Add("live twin requested/reported tracking");
        }

        if (tags.Any(tag => tag.Equals("lsl", StringComparison.OrdinalIgnoreCase)))
        {
            capabilities.Add("LSL telemetry or bridge transport");
        }

        if (capabilities.Count == 0)
        {
            return "This target is staged for install and launch, but no runtime/twin communication capability is advertised by the catalog metadata.";
        }

        return $"Advertised capabilities: {string.Join(", ", capabilities)}.";
    }

    private static string BuildSelectedAppCommunicationSummary(QuestAppTarget app)
    {
        var tags = app.Tags;
        var supportsRuntime = tags.Any(tag => tag.Equals("runtime", StringComparison.OrdinalIgnoreCase));
        var supportsTwin = tags.Any(tag => tag.Equals("twin", StringComparison.OrdinalIgnoreCase));
        var supportsLsl = tags.Any(tag => tag.Equals("lsl", StringComparison.OrdinalIgnoreCase));

        if (!IsCompanionCompatibleTarget(tags))
        {
            return "Communication path: outside the tagged DOPE companion set. The Windows app can still install or launch it over ADB, but runtime publish, twin tracking, and app-state interpretation are not supported.";
        }

        if (supportsRuntime && supportsTwin)
        {
            return "Communication path: direct operator publish plus live requested/reported tracking. This APK can receive runtime-config publishes and also report live app state over quest_twin_state.";
        }

        if (supportsRuntime && !supportsTwin)
        {
            return "Communication path: operator publish only. This APK can receive runtime-config or preset publishes, but live requested/reported tracking depends on a separate twin publisher such as LslTwin.";
        }

        if (supportsTwin || supportsLsl)
        {
            return "Communication path: live LSL publisher/bridge. This APK can contribute live state to the operator view, but staged runtime edits may target another selected runtime app.";
        }

        return "Communication path: ADB-only from the operator shell. Install, launch, and device-level actions work, but no live runtime communication channel is declared.";
    }

    private static bool IsCompanionCompatibleTarget(IReadOnlyList<string> tags)
        => tags.Any(tag => tag.Equals("dope", StringComparison.OrdinalIgnoreCase));

    private static string ClassifyTwinStateSource(string key)
        => key switch
        {
            "hotload_profile_id" => "App state",
            "hotload_profile_version" => "App state",
            "hotload_profile_channel" => "App state",
            var value when value.StartsWith("internal_", StringComparison.OrdinalIgnoreCase) => "App config",
            var value when value.StartsWith("performance_", StringComparison.OrdinalIgnoreCase) => "App config",
            var value when value.StartsWith("render_", StringComparison.OrdinalIgnoreCase) => "App config",
            var value when value.StartsWith("display_", StringComparison.OrdinalIgnoreCase) => "App config",
            var value when value.StartsWith("projected_feed_", StringComparison.OrdinalIgnoreCase) => "App config",
            var value when value.StartsWith("quest_foveation_", StringComparison.OrdinalIgnoreCase) => "Headset policy",
            var value when value.StartsWith("unity_", StringComparison.OrdinalIgnoreCase) => "App config",
            var value when value.StartsWith("study_", StringComparison.OrdinalIgnoreCase) => "App config",
            var value when value.StartsWith("showcase_", StringComparison.OrdinalIgnoreCase) => "App config",
            var value when value.StartsWith("twin_", StringComparison.OrdinalIgnoreCase) => "App config",
            var value when value.StartsWith("app.", StringComparison.OrdinalIgnoreCase) => "App state",
            var value when value.StartsWith("session.", StringComparison.OrdinalIgnoreCase) => "App state",
            var value when value.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase) => "App state",
            _ => "APK telemetry"
        };

    private void RefreshTwinInspectorRows()
    {
        var scopeId = SelectedTwinInspectorScope?.Id ?? "routing";
        var previousKey = SelectedTwinInspectorRow?.Key;
        var rows = _latestTwinDeltas
            .Where(delta => TwinScopeMatches(scopeId, delta.Key))
            .OrderBy(delta => GetTwinScopeSortWeight(delta.Key))
            .ThenBy(delta => delta.Key, StringComparer.OrdinalIgnoreCase)
            .Select(BuildTwinInspectorRow)
            .ToArray();

        TwinInspectorRows.Clear();
        foreach (var row in rows)
        {
            TwinInspectorRows.Add(row);
        }

        SelectedTwinInspectorRow = rows.FirstOrDefault(row => string.Equals(row.Key, previousKey, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault();

        OnPropertyChanged(nameof(TwinInspectorScopeSummary));
        OnPropertyChanged(nameof(TwinInspectorMatchLabel));
        OnPropertyChanged(nameof(TwinInspectorMatchPercent));
        OnPropertyChanged(nameof(TwinInspectorSelectionHeadline));
        OnPropertyChanged(nameof(TwinInspectorSelectionRequested));
        OnPropertyChanged(nameof(TwinInspectorSelectionReported));
        OnPropertyChanged(nameof(TwinInspectorSelectionSource));
        OnPropertyChanged(nameof(TwinInspectorSelectionDetail));
    }

    private TwinInspectorRow BuildTwinInspectorRow(TwinSettingsDelta delta)
    {
        var scopeId = GetTwinScopeId(delta.Key);
        var scopeLabel = TwinInspectorScopeCatalog.FirstOrDefault(scope => string.Equals(scope.Id, scopeId, StringComparison.OrdinalIgnoreCase))?.Label
            ?? "App State";
        var hasRequested = delta.Requested is not null;
        var hasReported = delta.Reported is not null;
        var requested = delta.Requested ?? "Not staged";
        var reported = delta.Reported ?? "Not reported";
        var (statusLabel, statusLevel) = hasRequested switch
        {
            true when !hasReported => ("Requested by Windows, but not yet reported back by the APK.", OperationOutcomeKind.Warning),
            false when hasReported => ("Reported live by the APK. No operator-side requested value is currently staged for comparison.", OperationOutcomeKind.Preview),
            true when delta.Matches => ("Requested and reported values match.", OperationOutcomeKind.Success),
            true when hasReported => ("Requested and reported values differ. Review before publishing another change.", OperationOutcomeKind.Warning),
            _ => ("No requested or reported value is available for this key yet.", OperationOutcomeKind.Preview)
        };

        return new TwinInspectorRow(
            delta.Key,
            scopeId,
            scopeLabel,
            ClassifyTwinStateSource(delta.Key),
            requested,
            reported,
            hasRequested,
            hasReported,
            delta.Matches,
            statusLabel,
            statusLevel);
    }

    private static bool TwinScopeMatches(string scopeId, string key)
        => scopeId switch
        {
            "routing" => string.Equals(GetTwinScopeId(key), "routing", StringComparison.OrdinalIgnoreCase),
            "headset" => string.Equals(GetTwinScopeId(key), "headset", StringComparison.OrdinalIgnoreCase),
            "runtime" => string.Equals(GetTwinScopeId(key), "runtime", StringComparison.OrdinalIgnoreCase),
            "twin" => string.Equals(GetTwinScopeId(key), "twin", StringComparison.OrdinalIgnoreCase),
            "state" => string.Equals(GetTwinScopeId(key), "state", StringComparison.OrdinalIgnoreCase),
            _ => true
        };

    private static string GetTwinScopeId(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "state";
        }

        if (string.Equals(key, "showcase_active_runtime_config_json", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("internal_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("twin_", StringComparison.OrdinalIgnoreCase))
        {
            return "twin";
        }

        if (key.StartsWith("performance_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("display_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("quest_foveation_", StringComparison.OrdinalIgnoreCase))
        {
            return "headset";
        }

        if (key.StartsWith("render_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("projected_feed_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("unity_", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("study_", StringComparison.OrdinalIgnoreCase))
        {
            return "runtime";
        }

        if (key.StartsWith("showcase_", StringComparison.OrdinalIgnoreCase))
        {
            return "routing";
        }

        if (key.StartsWith("app.", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("session.", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("hotload_profile_", StringComparison.OrdinalIgnoreCase))
        {
            return "state";
        }

        return "state";
    }

    private static int GetTwinScopeSortWeight(string key)
        => GetTwinScopeId(key) switch
        {
            "routing" => 0,
            "headset" => 1,
            "runtime" => 2,
            "twin" => 3,
            "state" => 4,
            _ => 5
        };

    private static string TrimStatusDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        var compact = string.Join(" ", detail.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= 520 ? compact : $"{compact[..520]}...";
    }

    private (string Label, string Detail, OperationOutcomeKind Level) DescribeHeadsetActivity(HeadsetAppStatus status)
    {
        if (!status.IsConnected)
        {
            return ("Headset offline", "Connect to a Quest to read the current in-headset activity.", OperationOutcomeKind.Failure);
        }

        if (string.IsNullOrWhiteSpace(status.ForegroundPackageId))
        {
            return ("Active app unknown", "Quest is connected, but Android did not expose a resumed activity. This often happens on the home shell or during transient OS overlays.", OperationOutcomeKind.Warning);
        }

        var visibleComponents = status.VisibleActivityComponents ?? Array.Empty<string>();
        var primary = DescribeComponent(status.ForegroundPackageId, status.ForegroundComponent);
        var overlays = visibleComponents
            .Skip(1)
            .Select(DescribeComponent)
            .Where(component => component.IsOverlay)
            .Select(component => component.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var detail = overlays.Length > 0
            ? $"Also visible: {string.Join(", ", overlays)}."
            : primary.Detail;

        return (primary.Label, detail, primary.Level);
    }

    private (string Label, string Detail, OperationOutcomeKind Level, bool IsOverlay) DescribeComponent(string component)
    {
        var slashIndex = component.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= component.Length - 1)
        {
            return (component, "Live headset component reported.", OperationOutcomeKind.Preview, false);
        }

        return DescribeComponent(component[..slashIndex], component);
    }

    private (string Label, string Detail, OperationOutcomeKind Level, bool IsOverlay) DescribeComponent(string packageId, string? component)
    {
        if (QuestShellOverlayClassifier.TryClassify(packageId, component) is { } overlayClassification)
        {
            return (overlayClassification.Label, overlayClassification.Detail, overlayClassification.Level, overlayClassification.IsOverlay);
        }

        var knownApp = Apps.FirstOrDefault(app => string.Equals(app.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        if (knownApp is not null)
        {
            return ($"{knownApp.Label} active", $"Foreground package: {packageId}.", OperationOutcomeKind.Success, false);
        }

        return ($"{packageId} active", $"Foreground package: {packageId}.", OperationOutcomeKind.Preview, false);
    }

    private static string BuildHeadsetBatteryLabel(HeadsetAppStatus status)
    {
        var headsetLabel = status.BatteryLevel is null ? "Battery n/a" : $"Battery {status.BatteryLevel}%";
        var controllerLabel = BuildControllerBatteryLabel(status.Controllers);
        return string.IsNullOrWhiteSpace(controllerLabel)
            ? headsetLabel
            : $"{headsetLabel} | {controllerLabel}";
    }

    private static string BuildHeadsetSoftwareVersionLabel(HeadsetAppStatus status)
        => string.IsNullOrWhiteSpace(status.SoftwareVersion)
            ? "Headset OS n/a"
            : $"Headset OS {status.SoftwareVersion}";

    private static string BuildControllerBatteryLabel(IReadOnlyList<QuestControllerStatus>? controllers)
    {
        if (controllers is not { Count: > 0 })
        {
            return string.Empty;
        }

        var left = controllers.FirstOrDefault(controller => string.Equals(controller.HandLabel, "Left", StringComparison.OrdinalIgnoreCase));
        var right = controllers.FirstOrDefault(controller => string.Equals(controller.HandLabel, "Right", StringComparison.OrdinalIgnoreCase));
        return $"Controllers {FormatControllerBatteryLabel("L", left)} / {FormatControllerBatteryLabel("R", right)}";
    }

    private static string FormatControllerBatteryLabel(string shortHandLabel, QuestControllerStatus? controller)
    {
        if (controller is null)
        {
            return $"{shortHandLabel} n/a";
        }

        var batteryLabel = controller.BatteryLevel is null ? "n/a" : $"{controller.BatteryLevel}%";
        var connectionLabel = controller.ConnectionState switch
        {
            "CONNECTED_ACTIVE" => "active",
            "CONNECTED_INACTIVE" => "idle",
            "DISCONNECTED" => "off",
            _ => string.IsNullOrWhiteSpace(controller.ConnectionState)
                ? string.Empty
                : controller.ConnectionState.Replace('_', ' ').ToLowerInvariant()
        };

        return string.IsNullOrWhiteSpace(connectionLabel)
            ? $"{shortHandLabel} {batteryLabel}"
            : $"{shortHandLabel} {batteryLabel} {connectionLabel}";
    }

    private void AppendLog(OperatorLogLevel level, string message, string detail)
    {
        Logs.Insert(0, new OperatorLogEntry(DateTimeOffset.Now, level, message, detail));
        while (Logs.Count > 80)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private void SaveSession(string? endpoint = null, string? usbSerial = null)
    {
        _sessionState = _sessionState
            .WithEndpoint(endpoint)
            .WithUsbSerial(usbSerial);
        _sessionState.Save();
    }

    private static OperatorLogLevel MapLevel(OperationOutcomeKind kind)
        => kind switch
        {
            OperationOutcomeKind.Success => OperatorLogLevel.Info,
            OperationOutcomeKind.Warning => OperatorLogLevel.Warning,
            OperationOutcomeKind.Failure => OperatorLogLevel.Failure,
            _ => OperatorLogLevel.Info
        };

    private static string ResolveCatalogRoot()
        => AppAssetLocator.ResolveQuestSessionKitRoot();

    private static void ResetPersistedStudyShellStartupState()
    {
        var loadedSessionState = AppSessionState.Load();
        var normalizedSessionState = StudyShellViewModel.NormalizeStartupSessionState(
            loadedSessionState,
            out var clearedPersistedRegularSnapshots);

        if (clearedPersistedRegularSnapshots)
        {
            normalizedSessionState.Save();
        }
    }

    private string? ResolveApkPathForTarget(QuestAppTarget target)
    {
        if (_apkOverrides.TryGetValue(target.Id, out var overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        if (Path.IsPathRooted(target.ApkFile) && File.Exists(target.ApkFile))
        {
            return Path.GetFullPath(target.ApkFile);
        }

        if (!string.IsNullOrWhiteSpace(CatalogSourcePath))
        {
            var candidate = Path.Combine(CatalogSourcePath, "APKs", target.ApkFile);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private QuestAppTarget WithResolvedApkPath(QuestAppTarget target)
        => target with { ApkFile = ResolveApkPathForTarget(target) ?? string.Empty };

    private Task DispatchAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action).Task;
    }

    private Task<T> DispatchAsync<T>(Func<T> action)
    {
        if (_dispatcher.CheckAccess())
        {
            return Task.FromResult(action());
        }

        return _dispatcher.InvokeAsync(action).Task;
    }

    public sealed record ActionChoice<T>(string Label, string Description, T Value);

    public sealed record KeyValueStatusRow(string Key, string Value, string Source);

    public sealed record TwinInspectorScope(string Id, string Label, string Description);

    public sealed record TwinInspectorRow(
        string Key,
        string ScopeId,
        string ScopeLabel,
        string Source,
        string Requested,
        string Reported,
        bool HasRequested,
        bool HasReported,
        bool Matches,
        string StatusLabel,
        OperationOutcomeKind StatusLevel);
}

