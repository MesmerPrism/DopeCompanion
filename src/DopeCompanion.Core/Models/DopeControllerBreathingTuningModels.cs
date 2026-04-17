using System.Globalization;

namespace DopeCompanion.Core.Models;

public sealed record DopeControllerBreathingTuningProfile(
    string Name,
    string? Notes);

public sealed record DopeControllerBreathingTuningInfo(
    string Effect,
    string IncreaseLooksLike,
    string DecreaseLooksLike,
    IReadOnlyList<string> Tradeoffs);

public sealed record DopeControllerBreathingTuningControl(
    string Id,
    string Group,
    string Label,
    bool Editable,
    double Value,
    double BaselineValue,
    string Type,
    string Units,
    double SafeMinimum,
    double SafeMaximum,
    string RuntimeKey,
    DopeControllerBreathingTuningInfo Info)
{
    public string BaselineLabel => Type switch
    {
        "bool" => BaselineValue >= 0.5d ? "On" : "Off",
        "int" => Math.Round(BaselineValue).ToString(CultureInfo.InvariantCulture),
        _ => BaselineValue.ToString("0.###", CultureInfo.InvariantCulture)
    };
}

public sealed record DopeControllerBreathingTuningDocument(
    string SchemaVersion,
    string DocumentKind,
    string PackageId,
    string BaselineHotloadProfileId,
    DopeControllerBreathingTuningProfile Profile,
    IReadOnlyList<DopeControllerBreathingTuningControl> Controls)
{
    public IReadOnlyDictionary<string, double> ControlValues
        => Controls.ToDictionary(control => control.Id, control => control.Value, StringComparer.OrdinalIgnoreCase);
}

public sealed record DopeControllerBreathingTuningCompileResult(
    DopeControllerBreathingTuningDocument Document,
    IReadOnlyList<RuntimeConfigEntry> Entries);

public sealed record DopeControllerBreathingProfileRecord(
    string Id,
    string FilePath,
    string FileHash,
    DateTimeOffset ModifiedAtUtc,
    DopeControllerBreathingTuningDocument Document);

public sealed record DopeControllerBreathingProfileStartupState(
    string ProfileId,
    string ProfileName,
    DateTimeOffset UpdatedAtUtc,
    string? ProfileNotes = null,
    IReadOnlyDictionary<string, double>? ControlValues = null);

public sealed record DopeControllerBreathingProfileApplyRecord(
    string ProfileId,
    string ProfileName,
    string FileHash,
    string CompiledValuesHash,
    DateTimeOffset AppliedAtUtc,
    IReadOnlyDictionary<string, double> RequestedValues,
    IReadOnlyDictionary<string, double?>? PreviousReportedValues);

public enum DopeControllerBreathingConfirmationState
{
    Waiting = 0,
    Confirmed = 1,
    Mismatch = 2
}

public sealed record DopeControllerBreathingConfirmationRow(
    string Id,
    string Label,
    double RequestedValue,
    double? ReportedValue,
    DopeControllerBreathingConfirmationState State);

public sealed record DopeControllerBreathingConfirmationResult(
    string Summary,
    IReadOnlyList<DopeControllerBreathingConfirmationRow> Rows,
    int ConfirmedCount,
    int WaitingCount,
    int MismatchCount);

public sealed record DopeControllerBreathingComparisonRow(
    string Id,
    string Group,
    string Label,
    string Type,
    double BaselineValue,
    double SelectedValue,
    double? CompareValue,
    double DeltaFromBaseline,
    double? DeltaBetweenProfiles);

