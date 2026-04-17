using DopeCompanion.Core.Models;

namespace DopeCompanion.App.ViewModels;

public sealed record DopeVisualSessionSnapshot(
    bool IsAvailable,
    OperationOutcomeKind ApplyLevel,
    string ApplySummary,
    string ApplyDetail,
    DopeVisualProfileRecord? CurrentProfile,
    DopeVisualProfileRecord? EffectiveProfile,
    DopeVisualProfileStartupState? StartupProfile,
    DopeVisualProfileApplyRecord? LastApplyRecord,
    IReadOnlyDictionary<string, double?> ReportedValues,
    bool SelectedMatchesLastApplied,
    bool HasUnappliedEdits);

public sealed record DopeControllerBreathingSessionSnapshot(
    bool IsAvailable,
    OperationOutcomeKind ApplyLevel,
    string ApplySummary,
    string ApplyDetail,
    DopeControllerBreathingProfileRecord? CurrentProfile,
    DopeControllerBreathingProfileRecord? EffectiveProfile,
    DopeControllerBreathingProfileStartupState? StartupProfile,
    DopeControllerBreathingProfileApplyRecord? LastApplyRecord,
    IReadOnlyDictionary<string, double?> ReportedValues,
    bool SelectedMatchesLastApplied,
    bool HasUnappliedEdits);

