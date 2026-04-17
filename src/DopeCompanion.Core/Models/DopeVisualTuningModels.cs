using System.Globalization;

namespace DopeCompanion.Core.Models;

public sealed record DopeVisualTuningProfile(
    string Name,
    string? Notes);

public sealed record DopeVisualTuningInfo(
    string Effect,
    string IncreaseLooksLike,
    string DecreaseLooksLike,
    IReadOnlyList<string> Tradeoffs);

public sealed record DopeVisualTuningControl(
    string Id,
    string Label,
    bool Editable,
    double Value,
    double BaselineValue,
    string Type,
    string Units,
    double SafeMinimum,
    double SafeMaximum,
    string RuntimeJsonField,
    DopeVisualTuningInfo Info)
{
    public string BaselineLabel => Type switch
    {
        "bool" => BaselineValue >= 0.5d ? "On" : "Off",
        "int" => Math.Round(BaselineValue, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture),
        _ => BaselineValue.ToString("0.###", CultureInfo.InvariantCulture)
    };
}

public sealed record DopeVisualTuningDocument(
    string SchemaVersion,
    string DocumentKind,
    string PackageId,
    string BaselineHotloadProfileId,
    string HotloadTargetKey,
    DopeVisualTuningProfile Profile,
    IReadOnlyList<DopeVisualTuningControl> Controls)
{
    public IReadOnlyDictionary<string, double> ControlValues
        => Controls.ToDictionary(control => control.Id, control => control.Value, StringComparer.OrdinalIgnoreCase);
}

public sealed record DopeVisualTuningCompileResult(
    DopeVisualTuningDocument Document,
    string CompactRuntimeConfigJson,
    string PrettyRuntimeConfigJson,
    string HotloadTargetKey,
    IReadOnlyList<RuntimeConfigEntry> Entries);

public sealed record DopeVisualProfileRecord(
    string Id,
    string FilePath,
    string FileHash,
    DateTimeOffset ModifiedAtUtc,
    DopeVisualTuningDocument Document);

public sealed record DopeVisualProfileStartupState(
    string ProfileId,
    string ProfileName,
    DateTimeOffset UpdatedAtUtc,
    string? ProfileNotes = null,
    IReadOnlyDictionary<string, double>? ControlValues = null);

public sealed record DopeVisualProfileApplyRecord(
    string ProfileId,
    string ProfileName,
    string FileHash,
    string CompiledJsonHash,
    DateTimeOffset AppliedAtUtc,
    IReadOnlyDictionary<string, double> RequestedValues,
    IReadOnlyDictionary<string, double?>? PreviousReportedValues);

public enum DopeVisualConfirmationState
{
    Waiting = 0,
    Confirmed = 1,
    Mismatch = 2
}

public sealed record DopeVisualConfirmationRow(
    string Id,
    string Label,
    double RequestedValue,
    double? ReportedValue,
    DopeVisualConfirmationState State);

public sealed record DopeVisualConfirmationResult(
    string Summary,
    IReadOnlyList<DopeVisualConfirmationRow> Rows,
    int ConfirmedCount,
    int WaitingCount,
    int MismatchCount);

public sealed record DopeVisualComparisonRow(
    string Id,
    string Label,
    double BaselineValue,
    double SelectedValue,
    double? CompareValue,
    double DeltaFromBaseline,
    double? DeltaBetweenProfiles);

