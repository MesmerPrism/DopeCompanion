using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;

namespace DopeCompanion.Cli;

internal static class DopeCliSupport
{
    internal const string DefaultStudyId = "dope-projected-feed-colorama";
    internal const string VisualBundledBaselineId = "__bundled_dope_visual_baseline__";
    internal const string VisualBundledProfileIdPrefix = "__bundled_dope_visual_profile__::";
    internal const string ControllerBundledProfileIdPrefix = "__bundled_dope_controller_breathing_profile__::";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    internal sealed record VisualResolvedProfile(
        DopeVisualProfileRecord Record,
        bool IsBundledBaseline,
        bool IsBundledProfile)
    {
        public string DisplayLabel => Record.Document.Profile.Name;
        public bool IsWritableLocalProfile => !IsBundledBaseline && !IsBundledProfile;
    }

    internal sealed record ControllerResolvedProfile(
        DopeControllerBreathingProfileRecord Record,
        bool IsBaselineTemplate,
        bool IsBundledProfile = false)
    {
        public string DisplayLabel => Record.Document.Profile.Name;
        public bool IsWritableLocalProfile => !IsBaselineTemplate && !IsBundledProfile;
    }

    internal sealed record StartupSyncResult(
        bool AppliedToDevice,
        bool DeferredWhileStudyRunning,
        string Summary,
        string Detail,
        string? CsvPath,
        DopeVisualProfileApplyRecord? VisualApplyRecord,
        DopeControllerBreathingProfileApplyRecord? ControllerApplyRecord);

    internal sealed record SurfaceFieldSpec(
        string Id,
        string Group,
        string Label,
        string Type,
        string Units,
        string Baseline,
        string Minimum,
        string Maximum,
        string? PairGroup,
        string? PairPartner,
        string RuntimeField,
        string Effect,
        string IncreaseLooksLike,
        string DecreaseLooksLike,
        IReadOnlyList<string> Tradeoffs,
        string ToolTipText);

    internal static async Task<IReadOnlyList<VisualResolvedProfile>> LoadVisualProfilesAsync(string studyId, string? studyRoot = null)
    {
        var compiler = CreateVisualCompiler();
        var localStore = new DopeVisualProfileStore(compiler);
        var profiles = new List<VisualResolvedProfile>
        {
            new(CreateVisualBaselineRecord(compiler), IsBundledBaseline: true, IsBundledProfile: false)
        };

        profiles.AddRange((await LoadBundledVisualProfilesAsync(compiler, studyRoot).ConfigureAwait(false))
            .Select(record => new VisualResolvedProfile(record, IsBundledBaseline: false, IsBundledProfile: true)));
        profiles.AddRange((await localStore.LoadAllAsync().ConfigureAwait(false))
            .Select(record => new VisualResolvedProfile(record, IsBundledBaseline: false, IsBundledProfile: false)));

        return profiles;
    }

    internal static async Task<IReadOnlyList<ControllerResolvedProfile>> LoadControllerProfilesAsync(string studyId, string? studyRoot = null)
    {
        var compiler = CreateControllerCompiler();
        var store = new DopeControllerBreathingProfileStore(compiler);
        var profiles = new List<ControllerResolvedProfile>();
        profiles.AddRange((await LoadBundledControllerProfilesAsync(compiler, studyRoot).ConfigureAwait(false))
            .Select(record => new ControllerResolvedProfile(record, IsBaselineTemplate: false, IsBundledProfile: true)));
        profiles.AddRange((await store.LoadAllAsync().ConfigureAwait(false))
            .Select(record => new ControllerResolvedProfile(record, IsBaselineTemplate: false, IsBundledProfile: false)));
        return profiles;
    }

    internal static DopeVisualTuningCompiler CreateVisualCompiler()
    {
        var path = ResolveDopeVisualTuningTemplatePath();
        return new DopeVisualTuningCompiler(File.ReadAllText(path));
    }

    internal static DopeControllerBreathingTuningCompiler CreateControllerCompiler()
    {
        var path = ResolveDopeControllerBreathingTuningTemplatePath();
        return new DopeControllerBreathingTuningCompiler(File.ReadAllText(path));
    }

    internal static DopeVisualProfileStore CreateVisualStore(DopeVisualTuningCompiler compiler) => new(compiler);

    internal static DopeControllerBreathingProfileStore CreateControllerStore(DopeControllerBreathingTuningCompiler compiler) => new(compiler);

    internal static VisualResolvedProfile ResolveVisualProfile(
        IReadOnlyList<VisualResolvedProfile> profiles,
        string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("A visual profile id or name is required.");
        }

        var normalized = token.Trim();
        if (string.Equals(normalized, "bundled-baseline", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "baseline", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, VisualBundledBaselineId, StringComparison.OrdinalIgnoreCase))
        {
            var baseline = profiles.FirstOrDefault(profile => profile.IsBundledBaseline);
            if (baseline is null)
            {
                throw new InvalidOperationException("The bundled Dope visual baseline could not be resolved.");
            }

            return baseline;
        }

        return ResolveUniqueProfile(
            profiles,
            normalized,
            profile => profile.Record.Id,
            profile => profile.Record.Document.Profile.Name,
            "visual profile");
    }

    internal static ControllerResolvedProfile ResolveControllerProfile(
        IReadOnlyList<ControllerResolvedProfile> profiles,
        string token,
        bool allowBaselineTemplate = true)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("A controller-breathing profile id or name is required.");
        }

        var normalized = token.Trim();
        if (allowBaselineTemplate &&
            (string.Equals(normalized, "bundled-baseline", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(normalized, "baseline", StringComparison.OrdinalIgnoreCase)))
        {
            var compiler = CreateControllerCompiler();
            return new ControllerResolvedProfile(CreateControllerBaselineRecord(compiler), IsBaselineTemplate: true);
        }

        return ResolveUniqueProfile(
            profiles,
            normalized,
            profile => profile.Record.Id,
            profile => profile.Record.Document.Profile.Name,
            "controller-breathing profile");
    }

    internal static IReadOnlyList<SurfaceFieldSpec> BuildVisualFieldSpecs()
    {
        var compiler = CreateVisualCompiler();
        return compiler.TemplateDocument.Controls
            .Select(control =>
            {
                var (pairGroup, pairPartner) = ResolvePairMetadata(control.Id);
                var format = ResolveNumericFormat(control.Type);
                return new SurfaceFieldSpec(
                    control.Id,
                    ResolveVisualGroupTitle(control.Id),
                    control.Label,
                    control.Type,
                    control.Units,
                    FormatControlValue(control.Type, control.BaselineValue),
                    control.SafeMinimum.ToString(format, CultureInfo.InvariantCulture),
                    control.SafeMaximum.ToString(format, CultureInfo.InvariantCulture),
                    pairGroup,
                    pairPartner,
                    control.RuntimeJsonField,
                    control.Info.Effect,
                    control.Info.IncreaseLooksLike,
                    control.Info.DecreaseLooksLike,
                    control.Info.Tradeoffs,
                    BuildVisualToolTip(control));
            })
            .ToArray();
    }

    internal static IReadOnlyList<SurfaceFieldSpec> BuildControllerFieldSpecs()
    {
        var compiler = CreateControllerCompiler();
        return compiler.TemplateDocument.Controls
            .Select(control =>
            {
                var (pairGroup, pairPartner) = ResolvePairMetadata(control.Id);
                var format = ResolveNumericFormat(control.Type);
                return new SurfaceFieldSpec(
                    control.Id,
                    control.Group,
                    control.Label,
                    control.Type,
                    control.Units,
                    FormatControlValue(control.Type, control.BaselineValue),
                    control.SafeMinimum.ToString(format, CultureInfo.InvariantCulture),
                    control.SafeMaximum.ToString(format, CultureInfo.InvariantCulture),
                    pairGroup,
                    pairPartner,
                    control.RuntimeKey,
                    control.Info.Effect,
                    control.Info.IncreaseLooksLike,
                    control.Info.DecreaseLooksLike,
                    control.Info.Tradeoffs,
                    BuildControllerToolTip(control));
            })
            .ToArray();
    }

    internal static DopeVisualTuningDocument BuildVisualDocument(
        DopeVisualTuningCompiler compiler,
        DopeVisualTuningDocument source,
        string? nameOverride,
        string? notesOverride,
        IReadOnlyList<string>? setSpecs,
        IReadOnlyList<string>? scaleSpecs)
    {
        var values = new Dictionary<string, double>(source.ControlValues, StringComparer.OrdinalIgnoreCase);
        ApplyVisualMutations(compiler, values, setSpecs, scaleSpecs);
        return compiler.CreateDocument(
            nameOverride ?? source.Profile.Name,
            notesOverride ?? source.Profile.Notes,
            values);
    }

    internal static DopeControllerBreathingTuningDocument BuildControllerDocument(
        DopeControllerBreathingTuningCompiler compiler,
        DopeControllerBreathingTuningDocument source,
        string? nameOverride,
        string? notesOverride,
        IReadOnlyList<string>? setSpecs,
        IReadOnlyList<string>? scaleSpecs)
    {
        var values = new Dictionary<string, double>(source.ControlValues, StringComparer.OrdinalIgnoreCase);
        ApplyControllerMutations(compiler, values, setSpecs, scaleSpecs);
        return compiler.CreateDocument(
            nameOverride ?? source.Profile.Name,
            notesOverride ?? source.Profile.Notes,
            values);
    }

    internal static async Task<DopeVisualProfileRecord> SaveVisualAsNewAsync(
        DopeVisualProfileStore store,
        DopeVisualTuningDocument document)
        => await store.SaveAsync(
            existingPath: null,
            document.Profile.Name,
            document.Profile.Notes,
            document.ControlValues).ConfigureAwait(false);

    internal static async Task<DopeVisualProfileRecord> SaveVisualExistingAsync(
        DopeVisualProfileStore store,
        VisualResolvedProfile existing,
        DopeVisualTuningDocument document)
    {
        if (!existing.IsWritableLocalProfile)
        {
            throw new InvalidOperationException("Bundled Dope visual profiles are read-only. Create a new local profile instead.");
        }

        return await store.SaveAsync(
            existing.Record.FilePath,
            document.Profile.Name,
            document.Profile.Notes,
            document.ControlValues).ConfigureAwait(false);
    }

    internal static async Task<DopeControllerBreathingProfileRecord> SaveControllerAsNewAsync(
        DopeControllerBreathingProfileStore store,
        DopeControllerBreathingTuningDocument document)
        => await store.SaveAsync(
            existingPath: null,
            document.Profile.Name,
            document.Profile.Notes,
            document.ControlValues).ConfigureAwait(false);

    internal static async Task<DopeControllerBreathingProfileRecord> SaveControllerExistingAsync(
        DopeControllerBreathingProfileStore store,
        ControllerResolvedProfile existing,
        DopeControllerBreathingTuningDocument document)
    {
        if (!existing.IsWritableLocalProfile)
        {
            throw new InvalidOperationException("Bundled controller-breathing profiles are read-only. Create a new local profile instead.");
        }

        return await store.SaveAsync(
            existing.Record.FilePath,
            document.Profile.Name,
            document.Profile.Notes,
            document.ControlValues).ConfigureAwait(false);
    }

    internal static void SaveVisualStartupState(string studyId, DopeVisualProfileRecord? record)
    {
        var startupStore = new DopeVisualProfileStartupStateStore(studyId);
        if (record is null)
        {
            startupStore.Save(null);
            return;
        }

        startupStore.Save(new DopeVisualProfileStartupState(
            record.Id,
            record.Document.Profile.Name,
            DateTimeOffset.UtcNow,
            record.Document.Profile.Notes,
            new Dictionary<string, double>(record.Document.ControlValues, StringComparer.OrdinalIgnoreCase)));
    }

    internal static void SaveControllerStartupState(string studyId, DopeControllerBreathingProfileRecord? record)
    {
        var startupStore = new DopeControllerBreathingProfileStartupStateStore(studyId);
        if (record is null)
        {
            startupStore.Save(null);
            return;
        }

        startupStore.Save(new DopeControllerBreathingProfileStartupState(
            record.Id,
            record.Document.Profile.Name,
            DateTimeOffset.UtcNow,
            record.Document.Profile.Notes,
            new Dictionary<string, double>(record.Document.ControlValues, StringComparer.OrdinalIgnoreCase)));
    }

    internal static void RefreshVisualStateAfterSave(
        string studyId,
        string previousProfileId,
        DopeVisualProfileRecord saved,
        bool forceSetStartup)
    {
        var startupStore = new DopeVisualProfileStartupStateStore(studyId);
        var startup = startupStore.Load();
        if (forceSetStartup ||
            string.Equals(startup?.ProfileId, previousProfileId, StringComparison.OrdinalIgnoreCase))
        {
            SaveVisualStartupState(studyId, saved);
        }

        var applyStore = new DopeVisualProfileApplyStateStore(studyId);
        var apply = applyStore.Load();
        if (apply is null || !string.Equals(apply.ProfileId, previousProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        applyStore.Save(new DopeVisualProfileApplyRecord(
            saved.Id,
            saved.Document.Profile.Name,
            saved.FileHash,
            apply.CompiledJsonHash,
            apply.AppliedAtUtc,
            apply.RequestedValues,
            apply.PreviousReportedValues));
    }

    internal static void RefreshControllerStateAfterSave(
        string studyId,
        string previousProfileId,
        DopeControllerBreathingProfileRecord saved,
        bool forceSetStartup)
    {
        var startupStore = new DopeControllerBreathingProfileStartupStateStore(studyId);
        var startup = startupStore.Load();
        if (forceSetStartup ||
            string.Equals(startup?.ProfileId, previousProfileId, StringComparison.OrdinalIgnoreCase))
        {
            SaveControllerStartupState(studyId, saved);
        }

        var applyStore = new DopeControllerBreathingProfileApplyStateStore(studyId);
        var apply = applyStore.Load();
        if (apply is null || !string.Equals(apply.ProfileId, previousProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        applyStore.Save(new DopeControllerBreathingProfileApplyRecord(
            saved.Id,
            saved.Document.Profile.Name,
            saved.FileHash,
            apply.CompiledValuesHash,
            apply.AppliedAtUtc,
            apply.RequestedValues,
            apply.PreviousReportedValues));
    }

    internal static void ClearVisualStateForDeletedProfile(string studyId, string profileId)
    {
        var startupStore = new DopeVisualProfileStartupStateStore(studyId);
        if (string.Equals(startupStore.Load()?.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            startupStore.Save(null);
        }

        var applyStore = new DopeVisualProfileApplyStateStore(studyId);
        if (string.Equals(applyStore.Load()?.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            applyStore.Save(null);
        }
    }

    internal static void ClearControllerStateForDeletedProfile(string studyId, string profileId)
    {
        var startupStore = new DopeControllerBreathingProfileStartupStateStore(studyId);
        if (string.Equals(startupStore.Load()?.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            startupStore.Save(null);
        }

        var applyStore = new DopeControllerBreathingProfileApplyStateStore(studyId);
        if (string.Equals(applyStore.Load()?.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
        {
            applyStore.Save(null);
        }
    }

    internal static async Task<StartupSyncResult> SyncPinnedStartupProfilesAsync(
        StudyShellDefinition study,
        IQuestControlService questService,
        string studyId,
        string? studyRoot,
        bool forceWhenStudyNotForeground)
    {
        var visualCompiler = CreateVisualCompiler();
        var controllerCompiler = CreateControllerCompiler();
        var target = StudyShellOperatorBindings.CreateQuestTarget(study);
        var headset = await questService.QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false).ConfigureAwait(false);

        var visualProfiles = await LoadVisualProfilesAsync(studyId, studyRoot).ConfigureAwait(false);
        var controllerProfiles = await LoadControllerProfilesAsync(studyId, studyRoot).ConfigureAwait(false);

        var visualStartup = TryCreateVisualStartupRecord(studyId, visualCompiler, visualProfiles);
        var controllerStartup = TryCreateControllerStartupRecord(studyId, controllerCompiler, controllerProfiles);

        if (!forceWhenStudyNotForeground && headset.IsTargetForeground == true)
        {
            return new StartupSyncResult(
                AppliedToDevice: false,
                DeferredWhileStudyRunning: true,
                Summary: "Pinned Dope startup profiles saved locally.",
                Detail: $"The Dope runtime is active in the foreground, so the device-side launch override will wait until the next study stop or launch. Package: {target.PackageId}.",
                CsvPath: null,
                VisualApplyRecord: null,
                ControllerApplyRecord: null);
        }

        if (!headset.IsConnected)
        {
            return new StartupSyncResult(
                AppliedToDevice: false,
                DeferredWhileStudyRunning: false,
                Summary: "Pinned Dope startup profiles saved locally.",
                Detail: $"{headset.Detail} The next successful Dope stop/launch or a later sync command will restage the device-side launch override.",
                CsvPath: null,
                VisualApplyRecord: null,
                ControllerApplyRecord: null);
        }

        if (visualStartup is null && controllerStartup is null)
        {
            var clearOutcome = await questService.ClearHotloadOverrideAsync(target).ConfigureAwait(false);
            return new StartupSyncResult(
                AppliedToDevice: clearOutcome.Kind != OperationOutcomeKind.Failure,
                DeferredWhileStudyRunning: false,
                Summary: clearOutcome.Summary,
                Detail: string.IsNullOrWhiteSpace(clearOutcome.Detail)
                    ? "Cleared the Dope device-side startup override; the next launch will use the bundled baseline."
                    : clearOutcome.Detail,
                CsvPath: null,
                VisualApplyRecord: null,
                ControllerApplyRecord: null);
        }

        var visualCompiled = visualStartup is null ? null : visualCompiler.Compile(visualStartup.Document);
        var controllerCompiled = controllerStartup is null ? null : controllerCompiler.Compile(controllerStartup.Document);
        var mergedEntries = MergeRuntimeConfigEntries(
            visualCompiled?.Entries,
            controllerCompiled?.Entries);
        var runtimeProfile = new RuntimeConfigProfile(
            $"dope_pinned_startup_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}",
            "Dope Pinned Startup Profile",
            string.Empty,
            DateTimeOffset.UtcNow.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture),
            "study",
            false,
            "Pinned Dope launch profile payload staged for the next runtime start.",
            [target.PackageId],
            mergedEntries);
        var writer = new RuntimeConfigWriter();
        var csvPath = await writer.WriteAsync(runtimeProfile).ConfigureAwait(false);
        var hotloadProfile = new HotloadProfile(
            runtimeProfile.Id,
            runtimeProfile.Label,
            csvPath,
            runtimeProfile.Version,
            runtimeProfile.Channel,
            runtimeProfile.StudyLock,
            runtimeProfile.Description,
            runtimeProfile.PackageIds);
        var outcome = await questService.ApplyHotloadProfileAsync(hotloadProfile, target).ConfigureAwait(false);

        DopeVisualProfileApplyRecord? visualApplyRecord = null;
        DopeControllerBreathingProfileApplyRecord? controllerApplyRecord = null;
        if (outcome.Kind is not (OperationOutcomeKind.Warning or OperationOutcomeKind.Failure))
        {
            if (visualStartup is not null)
            {
                visualApplyRecord = new DopeVisualProfileApplyRecord(
                    visualStartup.Id,
                    visualStartup.Document.Profile.Name,
                    visualStartup.FileHash,
                    ComputeTextHash(string.Join("\n", visualCompiled!.Entries.Select(entry => $"{entry.Key}={entry.Value}"))),
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, double>(visualStartup.Document.ControlValues, StringComparer.OrdinalIgnoreCase),
                    PreviousReportedValues: null);
                new DopeVisualProfileApplyStateStore(studyId).Save(visualApplyRecord);
            }

            if (controllerStartup is not null)
            {
                controllerApplyRecord = new DopeControllerBreathingProfileApplyRecord(
                    controllerStartup.Id,
                    controllerStartup.Document.Profile.Name,
                    controllerStartup.FileHash,
                    ComputeTextHash(string.Join("\n", controllerCompiled!.Entries.Select(entry => $"{entry.Key}={entry.Value}"))),
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, double>(controllerStartup.Document.ControlValues, StringComparer.OrdinalIgnoreCase),
                    PreviousReportedValues: null);
                new DopeControllerBreathingProfileApplyStateStore(studyId).Save(controllerApplyRecord);
            }
        }

        return new StartupSyncResult(
            AppliedToDevice: outcome.Kind is not (OperationOutcomeKind.Warning or OperationOutcomeKind.Failure),
            DeferredWhileStudyRunning: false,
            Summary: outcome.Summary,
            Detail: string.IsNullOrWhiteSpace(outcome.Detail)
                ? $"Compiled CSV: {csvPath}"
                : $"{outcome.Detail} Compiled CSV: {csvPath}",
            CsvPath: csvPath,
            VisualApplyRecord: visualApplyRecord,
            ControllerApplyRecord: controllerApplyRecord);
    }

    internal static async Task<(OperationOutcome Outcome, string CsvPath)> ApplyVisualLiveAsync(
        StudyShellDefinition study,
        IQuestControlService questService,
        ITwinModeBridge twinBridge,
        string studyId,
        DopeVisualProfileRecord record)
    {
        var compiler = CreateVisualCompiler();
        var compiled = compiler.Compile(record.Document);
        var target = StudyShellOperatorBindings.CreateQuestTarget(study);
        var runtimeProfile = new RuntimeConfigProfile(
            $"dope_visual_tuning_v1_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}",
            $"Dope Visual Profile - {record.Document.Profile.Name}",
            string.Empty,
            DateTimeOffset.UtcNow.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture),
            "study",
            false,
            $"Compiled from {record.Document.Profile.Name}. Only the Dope-approved visual envelope fields plus the simplified tracer wrapper were changed.",
            [record.Document.PackageId],
            compiled.Entries);
        var writer = new RuntimeConfigWriter();
        var csvPath = await writer.WriteAsync(runtimeProfile).ConfigureAwait(false);

        var headset = await questService.QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false).ConfigureAwait(false);
        if (!headset.IsConnected)
        {
            return (new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Live Dope visual apply blocked.",
                $"{headset.Detail} The runtime payload was compiled locally at {csvPath}, but it was not published."), csvPath);
        }

        if (headset.IsAwake != true || headset.IsInWakeLimbo)
        {
            var wake = await questService.RunUtilityAsync(QuestUtilityAction.Wake, allowWakeResumeTarget: false).ConfigureAwait(false);
            if (wake.Kind == OperationOutcomeKind.Failure)
            {
                return (new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Live Dope visual apply blocked by wake failure.",
                    $"{wake.Detail} Compiled CSV: {csvPath}"), csvPath);
            }

            headset = await questService.QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false).ConfigureAwait(false);
        }

        if (headset.IsTargetForeground != true)
        {
            return (new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Live Dope visual apply requires an active Dope session.",
                $"Bring {target.Label} to the foreground, then apply the profile again. Compiled CSV: {csvPath}"), csvPath);
        }

        if (!twinBridge.Status.IsAvailable)
        {
            return (new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Live Dope visual apply requires the twin bridge.",
                $"The CLI mirrors the GUI and publishes current-session visual applies over the live quest_hotload_config channel. Compiled CSV: {csvPath}"), csvPath);
        }

        var outcome = await twinBridge.PublishRuntimeConfigAsync(runtimeProfile, target).ConfigureAwait(false);
        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            new DopeVisualProfileApplyStateStore(studyId).Save(new DopeVisualProfileApplyRecord(
                record.Id,
                record.Document.Profile.Name,
                record.FileHash,
                ComputeTextHash(string.Join("\n", compiled.Entries.Select(entry => $"{entry.Key}={entry.Value}"))),
                DateTimeOffset.UtcNow,
                new Dictionary<string, double>(record.Document.ControlValues, StringComparer.OrdinalIgnoreCase),
                PreviousReportedValues: null));
        }

        return (new OperationOutcome(
            outcome.Kind,
            outcome.Summary,
            string.IsNullOrWhiteSpace(outcome.Detail)
                ? $"Compiled CSV: {csvPath}"
                : $"{outcome.Detail} Compiled CSV: {csvPath}"), csvPath);
    }

    internal static async Task<(OperationOutcome Outcome, string CsvPath)> ApplyControllerLiveAsync(
        StudyShellDefinition study,
        IQuestControlService questService,
        ITwinModeBridge twinBridge,
        string studyId,
        DopeControllerBreathingProfileRecord record)
    {
        var compiler = CreateControllerCompiler();
        var compiled = compiler.Compile(record.Document);
        var target = StudyShellOperatorBindings.CreateQuestTarget(study);
        var runtimeProfile = new RuntimeConfigProfile(
            $"dope_controller_breathing_tuning_v1_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}",
            $"Dope Controller Breathing Profile - {record.Document.Profile.Name}",
            string.Empty,
            DateTimeOffset.UtcNow.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture),
            "study",
            false,
            $"Compiled from {record.Document.Profile.Name}. Only the Dope-approved controller-breathing and vibration fields were changed.",
            [record.Document.PackageId],
            compiled.Entries);
        var writer = new RuntimeConfigWriter();
        var csvPath = await writer.WriteAsync(runtimeProfile).ConfigureAwait(false);

        var headset = await questService.QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false).ConfigureAwait(false);
        if (!headset.IsConnected)
        {
            return (new OperationOutcome(
                OperationOutcomeKind.Failure,
                "Live Dope controller-breathing apply blocked.",
                $"{headset.Detail} The runtime payload was compiled locally at {csvPath}, but it was not published."), csvPath);
        }

        if (headset.IsAwake != true || headset.IsInWakeLimbo)
        {
            var wake = await questService.RunUtilityAsync(QuestUtilityAction.Wake, allowWakeResumeTarget: false).ConfigureAwait(false);
            if (wake.Kind == OperationOutcomeKind.Failure)
            {
                return (new OperationOutcome(
                    OperationOutcomeKind.Failure,
                    "Live Dope controller-breathing apply blocked by wake failure.",
                    $"{wake.Detail} Compiled CSV: {csvPath}"), csvPath);
            }

            headset = await questService.QueryHeadsetStatusAsync(target, remoteOnlyControlEnabled: false).ConfigureAwait(false);
        }

        if (headset.IsTargetForeground != true)
        {
            return (new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Live Dope controller-breathing apply requires an active Dope session.",
                $"Bring {target.Label} to the foreground, then apply the profile again. Compiled CSV: {csvPath}"), csvPath);
        }

        if (!twinBridge.Status.IsAvailable)
        {
            return (new OperationOutcome(
                OperationOutcomeKind.Warning,
                "Live Dope controller-breathing apply requires the twin bridge.",
                $"The CLI mirrors the GUI and publishes current-session controller-breathing applies over the live quest_hotload_config channel. Compiled CSV: {csvPath}"), csvPath);
        }

        var outcome = await twinBridge.PublishRuntimeConfigAsync(runtimeProfile, target).ConfigureAwait(false);
        if (outcome.Kind != OperationOutcomeKind.Failure)
        {
            new DopeControllerBreathingProfileApplyStateStore(studyId).Save(new DopeControllerBreathingProfileApplyRecord(
                record.Id,
                record.Document.Profile.Name,
                record.FileHash,
                ComputeTextHash(string.Join("\n", compiled.Entries.Select(entry => $"{entry.Key}={entry.Value}"))),
                DateTimeOffset.UtcNow,
                new Dictionary<string, double>(record.Document.ControlValues, StringComparer.OrdinalIgnoreCase),
                PreviousReportedValues: null));
        }

        return (new OperationOutcome(
            outcome.Kind,
            outcome.Summary,
            string.IsNullOrWhiteSpace(outcome.Detail)
                ? $"Compiled CSV: {csvPath}"
                : $"{outcome.Detail} Compiled CSV: {csvPath}"), csvPath);
    }

    internal static object BuildVisualProfileView(
        VisualResolvedProfile profile,
        string studyId)
    {
        var startup = new DopeVisualProfileStartupStateStore(studyId).Load();
        var apply = new DopeVisualProfileApplyStateStore(studyId).Load();
        return new
        {
            profile_kind = profile.IsBundledBaseline ? "bundled-baseline" : profile.IsBundledProfile ? "bundled-profile" : "local-profile",
            id = profile.Record.Id,
            name = profile.Record.Document.Profile.Name,
            notes = profile.Record.Document.Profile.Notes,
            path = string.IsNullOrWhiteSpace(profile.Record.FilePath) ? null : profile.Record.FilePath,
            startup_selected = string.Equals(startup?.ProfileId, profile.Record.Id, StringComparison.OrdinalIgnoreCase),
            last_applied = apply is not null &&
                           string.Equals(apply.ProfileId, profile.Record.Id, StringComparison.OrdinalIgnoreCase)
                ? apply.AppliedAtUtc
                : (DateTimeOffset?)null,
            controls = profile.Record.Document.Controls.Select(control =>
            {
                var (pairGroup, pairPartner) = ResolvePairMetadata(control.Id);
                return new
                {
                    id = control.Id,
                    group = ResolveVisualGroupTitle(control.Id),
                    label = control.Label,
                    type = control.Type,
                    units = control.Units,
                    value = FormatControlValue(control.Type, control.Value),
                    baseline = FormatControlValue(control.Type, control.BaselineValue),
                    minimum = FormatControlValue(control.Type, control.SafeMinimum),
                    maximum = FormatControlValue(control.Type, control.SafeMaximum),
                    pair_group = pairGroup,
                    pair_partner = pairPartner,
                    runtime_json_field = control.RuntimeJsonField,
                    effect = control.Info.Effect,
                    increase_looks_like = control.Info.IncreaseLooksLike,
                    decrease_looks_like = control.Info.DecreaseLooksLike,
                    tradeoffs = control.Info.Tradeoffs,
                    tooltip = BuildVisualToolTip(control)
                };
            }).ToArray()
        };
    }

    internal static object BuildControllerProfileView(
        ControllerResolvedProfile profile,
        string studyId)
    {
        var startup = new DopeControllerBreathingProfileStartupStateStore(studyId).Load();
        var apply = new DopeControllerBreathingProfileApplyStateStore(studyId).Load();
        return new
        {
            profile_kind = profile.IsBaselineTemplate ? "bundled-baseline" : profile.IsBundledProfile ? "bundled-profile" : "local-profile",
            id = profile.Record.Id,
            name = profile.Record.Document.Profile.Name,
            notes = profile.Record.Document.Profile.Notes,
            path = string.IsNullOrWhiteSpace(profile.Record.FilePath) ? null : profile.Record.FilePath,
            startup_selected = string.Equals(startup?.ProfileId, profile.Record.Id, StringComparison.OrdinalIgnoreCase),
            last_applied = apply is not null &&
                           string.Equals(apply.ProfileId, profile.Record.Id, StringComparison.OrdinalIgnoreCase)
                ? apply.AppliedAtUtc
                : (DateTimeOffset?)null,
            controls = profile.Record.Document.Controls.Select(control =>
            {
                var (pairGroup, pairPartner) = ResolvePairMetadata(control.Id);
                return new
                {
                    id = control.Id,
                    group = control.Group,
                    label = control.Label,
                    type = control.Type,
                    units = control.Units,
                    value = FormatControlValue(control.Type, control.Value),
                    baseline = FormatControlValue(control.Type, control.BaselineValue),
                    minimum = FormatControlValue(control.Type, control.SafeMinimum),
                    maximum = FormatControlValue(control.Type, control.SafeMaximum),
                    pair_group = pairGroup,
                    pair_partner = pairPartner,
                    runtime_key = control.RuntimeKey,
                    effect = control.Info.Effect,
                    increase_looks_like = control.Info.IncreaseLooksLike,
                    decrease_looks_like = control.Info.DecreaseLooksLike,
                    tradeoffs = control.Info.Tradeoffs,
                    tooltip = BuildControllerToolTip(control)
                };
            }).ToArray()
        };
    }

    internal static void WriteJson(object value)
        => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    internal static void PrintVisualProfiles(IReadOnlyList<VisualResolvedProfile> profiles, string studyId)
    {
        var startup = new DopeVisualProfileStartupStateStore(studyId).Load();
        Console.WriteLine("Dope Visual Profiles:");
        foreach (var profile in profiles)
        {
            var kind = profile.IsBundledBaseline ? "bundled-baseline" : profile.IsBundledProfile ? "bundled" : "local";
            var startupMark = string.Equals(startup?.ProfileId, profile.Record.Id, StringComparison.OrdinalIgnoreCase) ? " [startup]" : string.Empty;
            Console.WriteLine($"  {profile.Record.Id,-44} {kind,-16} {profile.DisplayLabel}{startupMark}");
        }
    }

    internal static void PrintControllerProfiles(IReadOnlyList<ControllerResolvedProfile> profiles, string studyId)
    {
        var startup = new DopeControllerBreathingProfileStartupStateStore(studyId).Load();
        Console.WriteLine("Dope Controller-Breathing Profiles:");
        Console.WriteLine("  bundled-baseline                               baseline         Bundled Dope controller-breathing baseline");
        if (startup is null)
        {
            Console.WriteLine("    current startup: bundled baseline");
        }

        foreach (var profile in profiles)
        {
            var startupMark = string.Equals(startup?.ProfileId, profile.Record.Id, StringComparison.OrdinalIgnoreCase) ? " [startup]" : string.Empty;
            var kind = profile.IsBundledProfile ? "bundled" : "local";
            Console.WriteLine($"  {profile.Record.Id,-44} {kind,-16} {profile.DisplayLabel}{startupMark}");
        }
    }

    internal static void PrintFieldSpecs(string title, IReadOnlyList<SurfaceFieldSpec> fields)
    {
        Console.WriteLine(title);
        foreach (var field in fields)
        {
            Console.WriteLine($"  {field.Id} [{field.Group}]");
            Console.WriteLine($"    Label:    {field.Label}");
            Console.WriteLine($"    Type:     {field.Type}");
            Console.WriteLine($"    Baseline: {field.Baseline}");
            Console.WriteLine($"    Range:    {field.Minimum} .. {field.Maximum}");
            if (!string.IsNullOrWhiteSpace(field.PairGroup))
            {
                Console.WriteLine($"    Pair:     {field.PairGroup} ({field.PairPartner})");
            }
            Console.WriteLine($"    Runtime:  {field.RuntimeField}");
            Console.WriteLine($"    Effect:   {field.Effect}");
        }
    }

    private static async Task<IReadOnlyList<DopeVisualProfileRecord>> LoadBundledVisualProfilesAsync(
        DopeVisualTuningCompiler compiler,
        string? studyRoot)
    {
        var bundledRoot = TryResolveBundledDopeVisualProfilesRoot(studyRoot);
        if (string.IsNullOrWhiteSpace(bundledRoot) || !Directory.Exists(bundledRoot))
        {
            return Array.Empty<DopeVisualProfileRecord>();
        }

        var records = new List<DopeVisualProfileRecord>();
        foreach (var path in Directory.EnumerateFiles(bundledRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var document = compiler.Parse(json);
                records.Add(new DopeVisualProfileRecord(
                    VisualBundledProfileIdPrefix + Path.GetFileNameWithoutExtension(path),
                    Path.GetFullPath(path),
                    ComputeTextHash(json),
                    File.GetLastWriteTimeUtc(path),
                    document));
            }
            catch (InvalidDataException)
            {
                // Ignore broken bundled files so agent flows remain usable.
            }
        }

        return records
            .OrderBy(record => record.Document.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<DopeControllerBreathingProfileRecord>> LoadBundledControllerProfilesAsync(
        DopeControllerBreathingTuningCompiler compiler,
        string? studyRoot)
    {
        var bundledRoot = TryResolveBundledDopeControllerBreathingProfilesRoot(studyRoot);
        if (string.IsNullOrWhiteSpace(bundledRoot) || !Directory.Exists(bundledRoot))
        {
            return Array.Empty<DopeControllerBreathingProfileRecord>();
        }

        var records = new List<DopeControllerBreathingProfileRecord>();
        foreach (var path in Directory.EnumerateFiles(bundledRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var document = compiler.Parse(json);
                records.Add(new DopeControllerBreathingProfileRecord(
                    ControllerBundledProfileIdPrefix + Path.GetFileNameWithoutExtension(path),
                    Path.GetFullPath(path),
                    ComputeTextHash(json),
                    File.GetLastWriteTimeUtc(path),
                    document));
            }
            catch (InvalidDataException)
            {
                // Ignore broken bundled files so agent flows remain usable.
            }
        }

        return records
            .OrderBy(record => record.Document.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DopeVisualProfileRecord CreateVisualBaselineRecord(DopeVisualTuningCompiler compiler)
    {
        var payload = compiler.Serialize(compiler.TemplateDocument);
        return new DopeVisualProfileRecord(
            VisualBundledBaselineId,
            string.Empty,
            ComputeTextHash(payload),
            DateTimeOffset.MinValue,
            compiler.TemplateDocument);
    }

    private static DopeControllerBreathingProfileRecord CreateControllerBaselineRecord(DopeControllerBreathingTuningCompiler compiler)
    {
        var payload = compiler.Serialize(compiler.TemplateDocument);
        return new DopeControllerBreathingProfileRecord(
            "bundled-baseline",
            string.Empty,
            ComputeTextHash(payload),
            DateTimeOffset.MinValue,
            compiler.TemplateDocument);
    }

    private static DopeVisualProfileRecord? TryCreateVisualStartupRecord(
        string studyId,
        DopeVisualTuningCompiler compiler,
        IReadOnlyList<VisualResolvedProfile> profiles)
    {
        var startup = new DopeVisualProfileStartupStateStore(studyId).Load();
        if (startup is null)
        {
            return null;
        }

        DopeVisualTuningDocument? legacyPinnedDocument = profiles
            .FirstOrDefault(profile => string.Equals(profile.Record.Id, startup.ProfileId, StringComparison.OrdinalIgnoreCase))
            ?.Record.Document;

        DopeVisualTuningDocument document;
        if (startup.ControlValues is { Count: > 0 })
        {
            try
            {
                document = compiler.CreateDocument(
                    startup.ProfileName,
                    startup.ProfileNotes,
                    startup.ControlValues);
            }
            catch (InvalidDataException)
            {
                document = legacyPinnedDocument ?? compiler.TemplateDocument;
            }
        }
        else
        {
            document = legacyPinnedDocument ?? compiler.TemplateDocument;
        }

        var payload = compiler.Serialize(document);
        return new DopeVisualProfileRecord(
            startup.ProfileId,
            string.Empty,
            ComputeTextHash(payload),
            startup.UpdatedAtUtc,
            document);
    }

    private static DopeControllerBreathingProfileRecord? TryCreateControllerStartupRecord(
        string studyId,
        DopeControllerBreathingTuningCompiler compiler,
        IReadOnlyList<ControllerResolvedProfile> profiles)
    {
        var startup = new DopeControllerBreathingProfileStartupStateStore(studyId).Load();
        if (startup is null)
        {
            return null;
        }

        DopeControllerBreathingTuningDocument? legacyPinnedDocument = profiles
            .FirstOrDefault(profile => string.Equals(profile.Record.Id, startup.ProfileId, StringComparison.OrdinalIgnoreCase))
            ?.Record.Document;

        DopeControllerBreathingTuningDocument document;
        if (startup.ControlValues is { Count: > 0 })
        {
            try
            {
                document = compiler.CreateDocument(
                    startup.ProfileName,
                    startup.ProfileNotes,
                    startup.ControlValues);
            }
            catch (InvalidDataException)
            {
                document = legacyPinnedDocument ?? compiler.TemplateDocument;
            }
        }
        else
        {
            document = legacyPinnedDocument ?? compiler.TemplateDocument;
        }

        var payload = compiler.Serialize(document);
        return new DopeControllerBreathingProfileRecord(
            startup.ProfileId,
            string.Empty,
            ComputeTextHash(payload),
            startup.UpdatedAtUtc,
            document);
    }

    private static IReadOnlyList<RuntimeConfigEntry> MergeRuntimeConfigEntries(
        IReadOnlyList<RuntimeConfigEntry>? visualEntries,
        IReadOnlyList<RuntimeConfigEntry>? controllerEntries)
    {
        var merged = new List<RuntimeConfigEntry>();
        var indexesByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Append(IEnumerable<RuntimeConfigEntry>? entries)
        {
            if (entries is null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                if (indexesByKey.TryGetValue(entry.Key, out var existingIndex))
                {
                    merged[existingIndex] = entry;
                    continue;
                }

                indexesByKey[entry.Key] = merged.Count;
                merged.Add(entry);
            }
        }

        Append(visualEntries);
        Append(controllerEntries);
        return merged;
    }

    private static void ApplyVisualMutations(
        DopeVisualTuningCompiler compiler,
        Dictionary<string, double> values,
        IReadOnlyList<string>? setSpecs,
        IReadOnlyList<string>? scaleSpecs)
    {
        var controls = compiler.TemplateDocument.Controls.ToDictionary(control => control.Id, StringComparer.OrdinalIgnoreCase);
        ApplyScaleMutations(scaleSpecs, values, controls.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.OrdinalIgnoreCase));
        ApplySetMutations(setSpecs, values, controls.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.OrdinalIgnoreCase));
    }

    private static void ApplyControllerMutations(
        DopeControllerBreathingTuningCompiler compiler,
        Dictionary<string, double> values,
        IReadOnlyList<string>? setSpecs,
        IReadOnlyList<string>? scaleSpecs)
    {
        var controls = compiler.TemplateDocument.Controls.ToDictionary(control => control.Id, StringComparer.OrdinalIgnoreCase);
        ApplyScaleMutations(scaleSpecs, values, controls.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.OrdinalIgnoreCase));
        ApplySetMutations(setSpecs, values, controls.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.OrdinalIgnoreCase));
    }

    private static void ApplyScaleMutations(
        IReadOnlyList<string>? scaleSpecs,
        IDictionary<string, double> values,
        IReadOnlyDictionary<string, string> types)
    {
        if (scaleSpecs is null)
        {
            return;
        }

        foreach (var spec in scaleSpecs.Where(spec => !string.IsNullOrWhiteSpace(spec)))
        {
            var (id, value) = ParseAssignment(spec, "scale");
            if (!types.TryGetValue(id, out var type))
            {
                throw new InvalidOperationException($"Unknown control id '{id}'. Use the fields command to list valid ids.");
            }

            if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Boolean control '{id}' does not support scaling. Use --set {id}=on/off instead.");
            }

            var factor = ParseDoubleInvariant(value, $"Invalid scale factor for '{id}'.");
            values[id] = values[id] * factor;
        }
    }

    private static void ApplySetMutations(
        IReadOnlyList<string>? setSpecs,
        IDictionary<string, double> values,
        IReadOnlyDictionary<string, string> types)
    {
        if (setSpecs is null)
        {
            return;
        }

        foreach (var spec in setSpecs.Where(spec => !string.IsNullOrWhiteSpace(spec)))
        {
            var (id, rawValue) = ParseAssignment(spec, "set");
            if (!types.TryGetValue(id, out var type))
            {
                throw new InvalidOperationException($"Unknown control id '{id}'. Use the fields command to list valid ids.");
            }

            values[id] = ParseControlValue(type, rawValue, id);
        }
    }

    private static (string Id, string Value) ParseAssignment(string spec, string action)
    {
        var separatorIndex = spec.IndexOf('=');
        if (separatorIndex <= 0 || separatorIndex == spec.Length - 1)
        {
            throw new InvalidOperationException($"Invalid --{action} value '{spec}'. Expected field=value.");
        }

        return (spec[..separatorIndex].Trim(), spec[(separatorIndex + 1)..].Trim());
    }

    private static double ParseControlValue(string type, string rawValue, string id)
    {
        if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return rawValue.Trim().ToLowerInvariant() switch
            {
                "1" or "true" or "on" or "yes" or "enabled" => 1d,
                "0" or "false" or "off" or "no" or "disabled" => 0d,
                _ => throw new InvalidOperationException($"Invalid boolean value '{rawValue}' for '{id}'. Use on/off, true/false, or 1/0.")
            };
        }

        return ParseDoubleInvariant(rawValue, $"Invalid numeric value '{rawValue}' for '{id}'.");
    }

    private static double ParseDoubleInvariant(string value, string message)
    {
        if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException(message);
        }

        return parsed;
    }

    private static string ResolveDopeVisualTuningTemplatePath()
        => ResolveExistingFile(
            Environment.GetEnvironmentVariable("DOPE_VISUAL_TUNING_TEMPLATE"),
            Path.Combine(CliAssetLocator.TryResolveOscillatorConfigRoot() ?? string.Empty, "llm-tuning", "dope-visual-tuning-v1.template.json"));

    private static string ResolveDopeControllerBreathingTuningTemplatePath()
        => ResolveExistingFile(
            Environment.GetEnvironmentVariable("DOPE_CONTROLLER_BREATHING_TUNING_TEMPLATE"),
            Path.Combine(CliAssetLocator.ResolveQuestSessionKitRoot(), "LlmTuningProfiles", "dope-controller-breathing-tuning-v1.template.json"),
            Path.Combine(
                Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty,
                "source",
                "repos",
                "AstralKarateDojo",
                "QuestSessionKit",
                "LlmTuningProfiles",
                "dope-controller-breathing-tuning-v1.template.json"));

    private static string? TryResolveBundledDopeVisualProfilesRoot(string? studyRoot)
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("DOPE_VISUAL_PROFILE_BUNDLE_ROOT"),
            Path.Combine(studyRoot ?? CliAssetLocator.TryResolveStudyShellRoot() ?? string.Empty, "dope-projected-feed-colorama", "visual-profiles"));

    private static string? TryResolveBundledDopeControllerBreathingProfilesRoot(string? studyRoot)
        => TryResolveExistingDirectory(
            Environment.GetEnvironmentVariable("DOPE_CONTROLLER_BREATHING_PROFILE_BUNDLE_ROOT"),
            Path.Combine(studyRoot ?? CliAssetLocator.TryResolveStudyShellRoot() ?? string.Empty, "dope-projected-feed-colorama", "dope-controller-breathing-profiles"),
            Path.Combine(studyRoot ?? CliAssetLocator.TryResolveStudyShellRoot() ?? string.Empty, "dope-projected-feed-colorama", "controller-breathing-profiles"));

    private static string ResolveExistingDirectory(params string?[] candidates)
        => TryResolveExistingDirectory(candidates)
            ?? throw new DirectoryNotFoundException("Could not resolve the requested Dope asset directory.");

    private static string ResolveExistingFile(params string?[] candidates)
        => TryResolveExistingFile(candidates)
            ?? throw new FileNotFoundException("Could not resolve the requested Dope asset file.");

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

    private static T ResolveUniqueProfile<T>(
        IReadOnlyList<T> profiles,
        string token,
        Func<T, string> resolveId,
        Func<T, string> resolveName,
        string label)
    {
        var byId = profiles
            .Where(profile => string.Equals(resolveId(profile), token, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (byId.Length == 1)
        {
            return byId[0];
        }

        var byName = profiles
            .Where(profile => string.Equals(resolveName(profile), token, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (byName.Length == 1)
        {
            return byName[0];
        }

        if (byName.Length > 1)
        {
            throw new InvalidOperationException($"Multiple {label}s share the name '{token}'. Use the profile id instead.");
        }

        throw new InvalidOperationException($"Could not resolve {label} '{token}'.");
    }

    private static string FormatControlValue(string type, double value)
        => string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase)
            ? value >= 0.5d ? "On" : "Off"
            : string.Equals(type, "int", StringComparison.OrdinalIgnoreCase)
                ? Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string ResolveNumericFormat(string type)
        => string.Equals(type, "int", StringComparison.OrdinalIgnoreCase) ? "0" : "0.######";

    private static (string? PairGroup, string? PairPartner) ResolvePairMetadata(string controlId)
    {
        if (controlId.EndsWith("_min", StringComparison.OrdinalIgnoreCase))
        {
            var group = controlId[..^4];
            return (group, group + "_max");
        }

        if (controlId.EndsWith("_max", StringComparison.OrdinalIgnoreCase))
        {
            var group = controlId[..^4];
            return (group, group + "_min");
        }

        return (null, null);
    }

    private static string ResolveVisualGroupTitle(string controlId)
        => controlId switch
        {
            "sphere_deformation_enabled" => "Shape",
            string id when id.StartsWith("oblateness_by_radius", StringComparison.Ordinal) => "Shape",
            string id when id.StartsWith("sphere_radius", StringComparison.Ordinal) => "Size",
            "particle_size_relative_to_radius" => "Size",
            string id when id.StartsWith("particle_size", StringComparison.Ordinal) => "Size",
            string id when id.StartsWith("depth_wave", StringComparison.Ordinal) => "Depth Wave",
            string id when id.StartsWith("transparency", StringComparison.Ordinal) => "Transparency",
            string id when id.StartsWith("saturation", StringComparison.Ordinal) => "Saturation",
            string id when id.StartsWith("brightness", StringComparison.Ordinal) => "Brightness",
            string id when id.StartsWith("orbit_distance", StringComparison.Ordinal) => "Orbit",
            string id when id.StartsWith("tracers_", StringComparison.Ordinal) => "Tracers",
            _ => "Other"
        };

    private static string BuildVisualToolTip(DopeVisualTuningControl control)
    {
        var tradeoffs = string.Join(Environment.NewLine, control.Info.Tradeoffs.Select(tradeoff => "- " + tradeoff));
        if (string.Equals(control.Type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return $"{control.Info.Effect}{Environment.NewLine}{Environment.NewLine}" +
                   $"Enable: {control.Info.IncreaseLooksLike}{Environment.NewLine}" +
                   $"Disable: {control.Info.DecreaseLooksLike}{Environment.NewLine}{Environment.NewLine}" +
                   $"Baseline: {FormatControlValue(control.Type, control.BaselineValue)}{Environment.NewLine}" +
                   "Values: Off or On" +
                   $"{Environment.NewLine}{Environment.NewLine}{tradeoffs}";
        }

        var numericFormat = ResolveNumericFormat(control.Type);
        return $"{control.Info.Effect}{Environment.NewLine}{Environment.NewLine}" +
               $"Increase: {control.Info.IncreaseLooksLike}{Environment.NewLine}" +
               $"Decrease: {control.Info.DecreaseLooksLike}{Environment.NewLine}{Environment.NewLine}" +
               $"Baseline: {control.BaselineValue.ToString(numericFormat, CultureInfo.InvariantCulture)}{Environment.NewLine}" +
               $"Range: {control.SafeMinimum.ToString(numericFormat, CultureInfo.InvariantCulture)} .. {control.SafeMaximum.ToString(numericFormat, CultureInfo.InvariantCulture)}{Environment.NewLine}{Environment.NewLine}" +
               $"{tradeoffs}";
    }

    private static string BuildControllerToolTip(DopeControllerBreathingTuningControl control)
    {
        var tradeoffs = string.Join(Environment.NewLine, control.Info.Tradeoffs.Select(tradeoff => "- " + tradeoff));
        if (string.Equals(control.Type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return $"{control.Info.Effect}{Environment.NewLine}{Environment.NewLine}" +
                   $"Enable: {control.Info.IncreaseLooksLike}{Environment.NewLine}" +
                   $"Disable: {control.Info.DecreaseLooksLike}{Environment.NewLine}{Environment.NewLine}" +
                   $"Baseline: {FormatControlValue(control.Type, control.BaselineValue)}{Environment.NewLine}" +
                   "Values: Off or On" +
                   $"{Environment.NewLine}{Environment.NewLine}{tradeoffs}";
        }

        var numericFormat = ResolveNumericFormat(control.Type);
        return $"{control.Info.Effect}{Environment.NewLine}{Environment.NewLine}" +
               $"Increase: {control.Info.IncreaseLooksLike}{Environment.NewLine}" +
               $"Decrease: {control.Info.DecreaseLooksLike}{Environment.NewLine}{Environment.NewLine}" +
               $"Baseline: {control.BaselineValue.ToString(numericFormat, CultureInfo.InvariantCulture)}{Environment.NewLine}" +
               $"Range: {control.SafeMinimum.ToString(numericFormat, CultureInfo.InvariantCulture)} .. {control.SafeMaximum.ToString(numericFormat, CultureInfo.InvariantCulture)}{Environment.NewLine}{Environment.NewLine}" +
               $"{tradeoffs}";
    }

    private static string ComputeTextHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}

