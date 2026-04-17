using System.Collections.ObjectModel;
using System.Windows;
using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;

namespace DopeCompanion.App.ViewModels;

public sealed class RuntimeConfigWorkspaceViewModel : ObservableObject
{
    private static readonly RuntimeConfigSectionDefinition[] SectionDefinitions =
    [
        new(
            "metadata",
            "Profile Metadata",
            "Keep public scene profiles traceable when you export or publish staged DOPE runtime overrides.",
            RuntimeConfigInspectorPane.SessionRouting,
            [
                Text("hotload_profile_id", "Profile Id", "Stable profile identifier stored in the hotload file.", string.Empty),
                Text("hotload_profile_version", "Profile Version", "Human-readable profile version tag.", string.Empty),
                Text("hotload_profile_channel", "Profile Channel", "Channel marker such as dev, study, or showcase.", string.Empty),
                Toggle("hotload_profile_lock", "Profile Lock", "When enabled, the runtime rejects later hash drift for the active study session.", false)
            ]),
        new(
            "projected-feed",
            "Projected Feed Colorama",
            "Primary projected-feed stylizer controls for the multi-layer Colorama quad scene.",
            RuntimeConfigInspectorPane.ApkRuntime,
            [
                Text("projected_feed_brightness", "Brightness", "Projected-feed filter brightness.", "0.04"),
                Text("projected_feed_contrast", "Contrast", "Projected-feed filter contrast.", "1.10"),
                Text("projected_feed_saturation", "Saturation", "Projected-feed filter saturation.", "1.00"),
                Text("projected_feed_posterize", "Posterize", "Posterization amount; keep at 0 for the authored baseline.", "0.00"),
                Text("projected_feed_stereo_blend", "Stereo Blend", "Cross-eye blend amount for the projected feed.", "0.00"),
                Text("projected_feed_gradient_span", "Gradient Span", "Width of the active brightness-gradient window.", "0.92"),
                Text("projected_feed_gradient_blend", "Gradient Blend", "Blend amount for the brightness gradient stylizer.", "1.00"),
                Text("projected_feed_gradient_phase_offset", "Gradient Phase Offset", "Phase offset applied before oscillator and audio phase contribution.", "0.00"),
                Text("projected_feed_gradient_oscillator_amount", "Gradient Oscillator Amount", "Oscillator contribution to gradient-window motion.", "1.00"),
                Text("projected_feed_gradient_audio_speed_boost", "Gradient Audio Speed Boost", "Extra gradient motion derived from audio amplitude.", "0.65"),
                Text("projected_feed_max_horizontal_displacement", "Horizontal Displacement", "Maximum horizontal displacement amplitude.", "0.018"),
                Text("projected_feed_max_vertical_displacement", "Vertical Displacement", "Maximum vertical displacement amplitude.", "0.018"),
                Text("projected_feed_displacement_audio_range_boost", "Displacement Audio Boost", "Audio-driven multiplier for displacement amplitude.", "1.20"),
                Toggle("projected_feed_pre_strength_brightness_blur", "Pre-Strength Brightness Blur", "Blur the brightness field before strength extraction.", true),
                Choice("projected_feed_displacement_blur_kernel", "Displacement Blur Kernel", "Projected-feed blur kernel enum.", "1", ["0", "1", "2"]),
                Text("projected_feed_displacement_blur_radius_texels", "Blur Radius Texels", "Radius for projected-feed displacement blur.", "4.0"),
                Text("projected_feed_displacement_blur_sigma", "Blur Sigma", "Sigma for projected-feed displacement blur.", "1.0"),
                Text("projected_feed_displacement_blend", "Displacement Blend", "Final displacement blend amount.", "1.00"),
                Text("projected_feed_displacement_gradient_influence", "Gradient Influence", "How strongly the gradient shapes displacement.", "1.00")
            ]),
        new(
            "overlay",
            "Overlay Surface",
            "Quad geometry, depth-visualization, and overlay presentation controls for the projected-feed scene.",
            RuntimeConfigInspectorPane.ApkRuntime,
            [
                Toggle("projected_feed_depth_visualization_enabled", "Depth Visualization", "Enable the projected-feed depth-visualization stack.", true),
                Text("projected_feed_depth_visualization_near_meters", "Depth Near Meters", "Near range for projected-feed depth visualization.", "0.20"),
                Text("projected_feed_depth_visualization_far_meters", "Depth Far Meters", "Far range for projected-feed depth visualization.", "4.00"),
                Toggle("projected_feed_depth_visualization_confidence_saturation", "Confidence Saturation", "Use confidence saturation in the projected-feed depth visualization.", true),
                Text("projected_feed_quad_width_meters", "Quad Width Meters", "Projected-feed quad width.", "0.72"),
                Text("projected_feed_quad_height_meters", "Quad Height Meters", "Projected-feed quad height.", "0.54"),
                Choice("projected_feed_display_surface_mode", "Display Surface Mode", "Projected-feed display-surface mode enum.", "1", ["0", "1"]),
                Text("projected_feed_full_view_overlay_overscan", "Overlay Overscan", "Full-view overlay overscan for the quad surface.", "1.06"),
                Text("projected_feed_projection_edge_fade", "Projection Edge Fade", "Edge fade applied to the projected-feed overlay.", "0.06"),
                Toggle("projected_feed_show_mode_indicators", "Show Mode Indicators", "Display the projected-feed mode indicators row.", true),
                Toggle("projected_feed_show_hero_row", "Show Hero Row", "Display the projected-feed hero row.", false),
                Toggle("projected_feed_enable_guided_layer_smoothing", "Guided Layer Smoothing", "Enable the optional guided layer smoothing path.", false)
            ]),
        new(
            "performance",
            "Quest Performance",
            "Quest-side performance hints that are safe to stage from the public operator app.",
            RuntimeConfigInspectorPane.Headset,
            [
                Toggle("performance_hints_enabled", "Performance Hints Enabled", "Allow the app runtime to reapply Quest CPU/GPU hint levels.", true),
                Choice("performance_hint_cpu_level", "CPU Hint Level", "Quest CPU hint level.", "4", ["0", "1", "2", "3", "4", "5"]),
                Choice("performance_hint_gpu_level", "GPU Hint Level", "Quest GPU hint level.", "4", ["0", "1", "2", "3", "4", "5"]),
                Toggle("performance_hint_write_direct_levels", "Write Direct Levels", "Also write direct OVRManager cpuLevel and gpuLevel after suggested hints.", true),
                Text("performance_hint_reapply_seconds", "Hint Reapply Seconds", "Reapply cadence for Quest performance hints.", "2.0")
            ]),
        new(
            "display",
            "Display + Foveation",
            "Display refresh and Quest foveation policy for projected-feed scene runs.",
            RuntimeConfigInspectorPane.Headset,
            [
                Toggle("display_refresh_request_enabled", "Display Refresh Request", "Allow the runtime to request a specific display refresh rate.", false),
                Text("display_refresh_request_hz", "Display Refresh Hz", "Requested display refresh rate.", "72.0"),
                Text("display_refresh_request_reapply_seconds", "Refresh Reapply Seconds", "How often the request is re-applied.", "5.0"),
                Toggle("quest_foveation_enabled", "Foveation Enabled", "Allow the runtime to manage Quest foveation settings.", false),
                Choice("quest_foveation_level", "Foveation Level", "Quest foveation level (0..4).", "1", ["0", "1", "2", "3", "4"]),
                Toggle("quest_foveation_dynamic", "Dynamic Foveation", "Enable dynamic foveation in runtime policy.", false),
                Text("quest_foveation_reapply_seconds", "Foveation Reapply Seconds", "How often the foveation request is refreshed.", "2.0")
            ]),
        new(
            "twin",
            "Twin Policy",
            "Public desktop-side twin sync defaults kept in the operator app so future Astral-style DOPE builds can opt into the same bridge contract.",
            RuntimeConfigInspectorPane.TwinTiming,
            [
                Choice("twin_sync_mode", "Twin Sync Mode", "0 APK -> Playmode, 1 Auto, 2 Playmode -> APK.", "2", ["0", "1", "2"]),
                Toggle("twin_auto_first_sync_apk_priority", "Prefer Remote First Sync", "In Auto mode, prefer the Quest snapshot on first sync.", true),
                Toggle("twin_parameter_apply_enabled", "Twin Parameter Apply", "Allow incoming twin parameter and routing application.", false),
                Toggle("twin_signal_mirror_enabled", "Twin Signal Mirror", "Allow incoming twin signal mirroring into the local registry.", true)
            ])
    ];

    private readonly RuntimeConfigCatalogLoader _catalogLoader = new();
    private readonly RuntimeConfigWriter _writer = new();
    private string _catalogStatus = "Loading DOPE runtime config profiles...";
    private string _catalogSourcePath = string.Empty;
    private string _selectedProfileSummary = "No runtime config selected.";
    private string _lastExportPath = "No runtime config export written yet.";
    private RuntimeConfigProfile? _selectedProfile;
    private readonly ObservableCollection<RuntimeConfigSectionViewModel> _sessionRoutingSections = new();
    private readonly ObservableCollection<RuntimeConfigSectionViewModel> _headsetSections = new();
    private readonly ObservableCollection<RuntimeConfigSectionViewModel> _apkRuntimeSections = new();
    private readonly ObservableCollection<RuntimeConfigSectionViewModel> _twinTimingSections = new();
    private readonly ObservableCollection<RuntimeConfigSectionViewModel> _allSections = new();

    public ObservableCollection<RuntimeConfigProfile> Profiles { get; } = new();

    public ObservableCollection<RuntimeConfigSectionViewModel> Sections { get; } = new();

    public ObservableCollection<RuntimeConfigSectionViewModel> SessionRoutingSections => _sessionRoutingSections;

    public ObservableCollection<RuntimeConfigSectionViewModel> HeadsetSections => _headsetSections;

    public ObservableCollection<RuntimeConfigSectionViewModel> ApkRuntimeSections => _apkRuntimeSections;

    public ObservableCollection<RuntimeConfigSectionViewModel> TwinTimingSections => _twinTimingSections;

    public ObservableCollection<RuntimeConfigSectionViewModel> AllSections => _allSections;

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

    public string SelectedProfileSummary
    {
        get => _selectedProfileSummary;
        private set => SetProperty(ref _selectedProfileSummary, value);
    }

    public string LastExportPath
    {
        get => _lastExportPath;
        private set => SetProperty(ref _lastExportPath, value);
    }

    public RuntimeConfigProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                RebuildSections();
                OnPropertyChanged(nameof(SelectedProfileLabel));
            }
        }
    }

    public string SelectedProfileLabel => SelectedProfile?.Label ?? "No runtime config selected.";

    public async Task LoadAsync(
        string questSessionKitRoot,
        IReadOnlyList<HotloadProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        var catalog = await _catalogLoader.LoadAsync(questSessionKitRoot, profiles, cancellationToken).ConfigureAwait(false);

        await DispatchAsync(() =>
        {
            var previousId = SelectedProfile?.Id;

            Profiles.Clear();
            foreach (var profile in catalog.Profiles)
            {
                Profiles.Add(profile);
            }

            CatalogStatus = $"Loaded {catalog.Source.Label}: {catalog.Profiles.Count} runtime config profile(s).";
            CatalogSourcePath = catalog.Source.RootPath;
            SelectedProfile = Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, previousId, StringComparison.OrdinalIgnoreCase))
                ?? Profiles.FirstOrDefault();

            if (SelectedProfile is null)
            {
                Sections.Clear();
                _sessionRoutingSections.Clear();
                _headsetSections.Clear();
                _apkRuntimeSections.Clear();
                _twinTimingSections.Clear();
                _allSections.Clear();
                SelectedProfileSummary = "No runtime config profiles are available.";
            }
        }).ConfigureAwait(false);
    }

    public void SelectProfile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        SelectedProfile = Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
            ?? SelectedProfile;
    }

    public void SelectProfileForPackage(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            SelectedProfile = null;
            return;
        }

        if (SelectedProfile?.MatchesPackage(packageId) == true)
        {
            return;
        }

        SelectedProfile = Profiles.FirstOrDefault(profile => profile.MatchesPackage(packageId));
    }

    public void ResetSelectedProfile()
    {
        RebuildSections();
    }

    public RuntimeConfigProfile BuildEditedProfile()
    {
        var selectedProfile = SelectedProfile ?? throw new InvalidOperationException("Select a runtime config profile first.");
        var rowLookup = Sections
            .SelectMany(section => section.Rows)
            .ToDictionary(row => row.Key, StringComparer.OrdinalIgnoreCase);

        var entries = new List<RuntimeConfigEntry>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in SectionDefinitions.SelectMany(section => section.Settings))
        {
            if (!rowLookup.TryGetValue(definition.Key, out var row))
            {
                continue;
            }

            var value = GetRowValue(row);
            if (string.IsNullOrWhiteSpace(value) && definition.EditorKind is RuntimeSettingEditorKind.Text or RuntimeSettingEditorKind.Multiline)
            {
                continue;
            }

            entries.Add(new RuntimeConfigEntry(definition.Key, value));
            seenKeys.Add(definition.Key);
        }

        foreach (var row in Sections
                     .Where(section => string.Equals(section.SectionId, "additional", StringComparison.OrdinalIgnoreCase))
                     .SelectMany(section => section.Rows))
        {
            var value = GetRowValue(row);
            if (string.IsNullOrWhiteSpace(value) || !seenKeys.Add(row.Key))
            {
                continue;
            }

            entries.Add(new RuntimeConfigEntry(row.Key, value));
        }

        return new RuntimeConfigProfile(
            GetEntryValue(entries, "hotload_profile_id", selectedProfile.Id),
            selectedProfile.Label,
            selectedProfile.File,
            GetEntryValue(entries, "hotload_profile_version", selectedProfile.Version),
            GetEntryValue(entries, "hotload_profile_channel", selectedProfile.Channel),
            bool.TryParse(GetEntryValue(entries, "hotload_profile_lock", selectedProfile.StudyLock ? "true" : "false"), out var studyLock) && studyLock,
            selectedProfile.Description,
            selectedProfile.PackageIds,
            entries);
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        var profile = await DispatchAsync(BuildEditedProfile).ConfigureAwait(false);
        var path = await _writer.WriteAsync(profile, cancellationToken).ConfigureAwait(false);
        await DispatchAsync(() => LastExportPath = path).ConfigureAwait(false);
        return path;
    }

    public bool TrySetValue(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var row = Sections
            .SelectMany(section => section.Rows)
            .FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return false;
        }

        switch (row)
        {
            case SingleValueRowViewModel single:
                single.ValueText = value;
                return true;
            case MultilineRowViewModel multiline:
                multiline.ValueText = value;
                return true;
            case ChoiceRowViewModel choice:
                choice.SelectedOption = value;
                return true;
            case ToggleRowViewModel toggle when bool.TryParse(value, out var boolValue):
                toggle.Value = boolValue;
                return true;
            default:
                return false;
        }
    }

    public bool TryGetValue(string key, out string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var row = Sections
            .SelectMany(section => section.Rows)
            .FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            value = string.Empty;
            return false;
        }

        value = GetRowValue(row);
        return true;
    }

    private void RebuildSections()
    {
        Sections.Clear();
        _sessionRoutingSections.Clear();
        _headsetSections.Clear();
        _apkRuntimeSections.Clear();
        _twinTimingSections.Clear();
        _allSections.Clear();

        if (SelectedProfile is null)
        {
            SelectedProfileSummary = "No runtime config selected.";
            return;
        }

        var entryLookup = new Dictionary<string, RuntimeConfigEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in SelectedProfile.Entries)
        {
            entryLookup[entry.Key] = entry;
        }

        var builtSections = new List<(RuntimeConfigInspectorPane Pane, RuntimeConfigSectionViewModel Section)>();

        foreach (var section in SectionDefinitions)
        {
            var rows = new List<ConfigSettingRowViewModel>();
            foreach (var setting in section.Settings)
            {
                entryLookup.TryGetValue(setting.Key, out var existingEntry);
                var rawValue = existingEntry?.Value ?? setting.DefaultValue;
                rows.Add(BuildRow(setting, rawValue));
                entryLookup.Remove(setting.Key);
            }

            var sectionViewModel = new RuntimeConfigSectionViewModel(section.Id, section.Title, section.Description, rows);
            Sections.Add(sectionViewModel);
            builtSections.Add((section.Pane, sectionViewModel));
        }

        if (entryLookup.Count > 0)
        {
            var additionalRows = entryLookup.Values
                .Select(entry => new SingleValueRowViewModel(
                    entry.Key,
                    entry.Key,
                    "Additional key preserved from the source profile.",
                    entry.Value))
                .Cast<ConfigSettingRowViewModel>()
                .ToArray();

            var additionalSection = new RuntimeConfigSectionViewModel(
                "additional",
                "Additional Keys",
                "Keys outside the current public schema stay editable and are preserved on export.",
                additionalRows);

            Sections.Add(additionalSection);
            builtSections.Add((RuntimeConfigInspectorPane.TwinTiming, additionalSection));
        }

        foreach (var entry in builtSections)
        {
            _allSections.Add(entry.Section);
            switch (entry.Pane)
            {
                case RuntimeConfigInspectorPane.SessionRouting:
                    _sessionRoutingSections.Add(entry.Section);
                    break;
                case RuntimeConfigInspectorPane.Headset:
                    _headsetSections.Add(entry.Section);
                    break;
                case RuntimeConfigInspectorPane.ApkRuntime:
                    _apkRuntimeSections.Add(entry.Section);
                    break;
                case RuntimeConfigInspectorPane.TwinTiming:
                    _twinTimingSections.Add(entry.Section);
                    break;
            }
        }

        SelectedProfileSummary =
            $"{SelectedProfile.Label} ({SelectedProfile.Channel}/{SelectedProfile.Version}) — {SelectedProfile.Description} {SelectedProfile.Entries.Count} staged key(s).";
    }

    private static ConfigSettingRowViewModel BuildRow(RuntimeSettingDefinition definition, string rawValue)
        => definition.EditorKind switch
        {
            RuntimeSettingEditorKind.Toggle => new ToggleRowViewModel(
                definition.Key,
                definition.Label,
                definition.Description,
                bool.TryParse(rawValue, out var boolValue) && boolValue),
            RuntimeSettingEditorKind.Choice => new ChoiceRowViewModel(
                definition.Key,
                definition.Label,
                definition.Description,
                definition.Options ?? Array.Empty<string>(),
                string.IsNullOrWhiteSpace(rawValue) ? definition.DefaultValue : rawValue),
            RuntimeSettingEditorKind.Multiline => new MultilineRowViewModel(
                definition.Key,
                definition.Label,
                definition.Description,
                rawValue,
                minimumLines: 6),
            _ => new SingleValueRowViewModel(
                definition.Key,
                definition.Label,
                definition.Description,
                rawValue)
        };

    private static string GetRowValue(ConfigSettingRowViewModel row)
        => row switch
        {
            SingleValueRowViewModel single => single.ValueText.Trim(),
            ToggleRowViewModel toggle => toggle.Value ? "true" : "false",
            ChoiceRowViewModel choice => choice.SelectedOption.Trim(),
            MultilineRowViewModel multiline => string.Join(
                " ",
                multiline.ValueText
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            _ => string.Empty
        };

    private static string GetEntryValue(
        IEnumerable<RuntimeConfigEntry> entries,
        string key,
        string fallback)
        => entries.LastOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))?.Value ?? fallback;

    private static RuntimeSettingDefinition Text(string key, string label, string description, string defaultValue)
        => new(key, label, description, RuntimeSettingEditorKind.Text, defaultValue, null);

    private static RuntimeSettingDefinition Toggle(string key, string label, string description, bool defaultValue)
        => new(key, label, description, RuntimeSettingEditorKind.Toggle, defaultValue ? "true" : "false", null);

    private static RuntimeSettingDefinition Choice(
        string key,
        string label,
        string description,
        string defaultValue,
        string[] options)
        => new(key, label, description, RuntimeSettingEditorKind.Choice, defaultValue, options);

    private static RuntimeSettingDefinition Multiline(string key, string label, string description, string defaultValue)
        => new(key, label, description, RuntimeSettingEditorKind.Multiline, defaultValue, null);

    public void ApplyTwinDelta(IReadOnlyList<TwinSettingsDelta> deltas)
    {
        var deltaLookup = new Dictionary<string, TwinSettingsDelta>(StringComparer.OrdinalIgnoreCase);
        foreach (var delta in deltas)
        {
            deltaLookup[delta.Key] = delta;
        }

        foreach (var row in Sections.SelectMany(section => section.Rows))
        {
            if (deltaLookup.Count == 0)
            {
                row.SyncState = SettingSyncState.Inactive;
                continue;
            }

            if (!deltaLookup.TryGetValue(row.Key, out var delta))
            {
                row.SyncState = SettingSyncState.Unknown;
                continue;
            }

            if (delta.Reported is null)
            {
                row.SyncState = SettingSyncState.Unknown;
                continue;
            }

            var currentValue = GetRowValue(row);
            row.SyncState = string.Equals(currentValue, delta.Reported, StringComparison.Ordinal)
                ? SettingSyncState.Verified
                : SettingSyncState.Pending;
        }
    }

    private static Task DispatchAsync(Action action)
        => Application.Current.Dispatcher.InvokeAsync(action).Task;

    private static Task<T> DispatchAsync<T>(Func<T> action)
        => Application.Current.Dispatcher.InvokeAsync(action).Task;

    private sealed record RuntimeConfigSectionDefinition(
        string Id,
        string Title,
        string Description,
        RuntimeConfigInspectorPane Pane,
        IReadOnlyList<RuntimeSettingDefinition> Settings);

    private sealed record RuntimeSettingDefinition(
        string Key,
        string Label,
        string Description,
        RuntimeSettingEditorKind EditorKind,
        string DefaultValue,
        IReadOnlyList<string>? Options);

    private enum RuntimeSettingEditorKind
    {
        Text,
        Toggle,
        Choice,
        Multiline
    }

    private enum RuntimeConfigInspectorPane
    {
        SessionRouting,
        Headset,
        ApkRuntime,
        TwinTiming
    }
}

public sealed class RuntimeConfigSectionViewModel
{
    public RuntimeConfigSectionViewModel(
        string sectionId,
        string title,
        string description,
        IReadOnlyList<ConfigSettingRowViewModel> rows)
    {
        SectionId = sectionId;
        Title = title;
        Description = description;
        Rows = new ObservableCollection<ConfigSettingRowViewModel>(rows);
    }

    public string SectionId { get; }

    public string Title { get; }

    public string Description { get; }

    public ObservableCollection<ConfigSettingRowViewModel> Rows { get; }
}

public enum SettingSyncState
{
    Inactive,
    Unknown,
    Pending,
    Verified
}

public abstract class ConfigSettingRowViewModel : ObservableObject
{
    private SettingSyncState _syncState = SettingSyncState.Inactive;

    protected ConfigSettingRowViewModel(string key, string label, string description)
    {
        Key = key;
        Label = label;
        Description = description;
    }

    public string Key { get; }

    public string Label { get; }

    public string Description { get; }

    public SettingSyncState SyncState
    {
        get => _syncState;
        set => SetProperty(ref _syncState, value);
    }
}

public sealed class SingleValueRowViewModel : ConfigSettingRowViewModel
{
    private string _valueText;

    public SingleValueRowViewModel(string key, string label, string description, string valueText)
        : base(key, label, description)
    {
        _valueText = valueText;
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }
}

public sealed class ToggleRowViewModel : ConfigSettingRowViewModel
{
    private bool _value;

    public ToggleRowViewModel(string key, string label, string description, bool value)
        : base(key, label, description)
    {
        _value = value;
    }

    public bool Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

public sealed class ChoiceRowViewModel : ConfigSettingRowViewModel
{
    private string _selectedOption;

    public ChoiceRowViewModel(
        string key,
        string label,
        string description,
        IEnumerable<string> options,
        string selectedOption)
        : base(key, label, description)
    {
        Options = new ObservableCollection<string>(options);
        _selectedOption = selectedOption;
    }

    public ObservableCollection<string> Options { get; }

    public string SelectedOption
    {
        get => _selectedOption;
        set => SetProperty(ref _selectedOption, value);
    }
}

public sealed class MultilineRowViewModel : ConfigSettingRowViewModel
{
    private string _valueText;

    public MultilineRowViewModel(
        string key,
        string label,
        string description,
        string valueText,
        int minimumLines)
        : base(key, label, description)
    {
        _valueText = valueText;
        MinimumLines = minimumLines;
    }

    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    public int MinimumLines { get; }
}

public sealed class RangeRowViewModel : ConfigSettingRowViewModel
{
    private string _minimumText;
    private string _maximumText;

    public RangeRowViewModel(
        string key,
        string label,
        string description,
        string minimumText,
        string maximumText)
        : base(key, label, description)
    {
        _minimumText = minimumText;
        _maximumText = maximumText;
    }

    public string MinimumText
    {
        get => _minimumText;
        set => SetProperty(ref _minimumText, value);
    }

    public string MaximumText
    {
        get => _maximumText;
        set => SetProperty(ref _maximumText, value);
    }
}

public sealed class Vector3RowViewModel : ConfigSettingRowViewModel
{
    private string _xText;
    private string _yText;
    private string _zText;

    public Vector3RowViewModel(
        string key,
        string label,
        string description,
        string xText,
        string yText,
        string zText)
        : base(key, label, description)
    {
        _xText = xText;
        _yText = yText;
        _zText = zText;
    }

    public string XText
    {
        get => _xText;
        set => SetProperty(ref _xText, value);
    }

    public string YText
    {
        get => _yText;
        set => SetProperty(ref _yText, value);
    }

    public string ZText
    {
        get => _zText;
        set => SetProperty(ref _zText, value);
    }
}
