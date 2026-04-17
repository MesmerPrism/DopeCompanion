using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;

namespace DopeCompanion.App.ViewModels;

internal sealed record DopeVisualProfileSnapshot(
    DopeVisualProfileListItemViewModel Profile,
    string ProfileName,
    string? ProfileNotes,
    IReadOnlyDictionary<string, double> ControlValues);

internal sealed record DopeVisualStartupHotloadPlan(
    DopeVisualProfileRecord Profile,
    IReadOnlyList<RuntimeConfigEntry> Entries,
    IReadOnlyDictionary<string, double?> PreviousReportedValues);

public readonly record struct DopeVisualRowConfirmationState(
    string Label,
    OperationOutcomeKind Level);

internal sealed record DopeVisualRowConfirmationComputationResult(
    IReadOnlyDictionary<string, DopeVisualRowConfirmationState> States,
    int ChangedSinceApplyCount,
    int UnchangedSinceApplyCount);

internal static class DopeVisualRowConfirmationResolver
{
    public static DopeVisualRowConfirmationComputationResult Compute(
        IEnumerable<DopeVisualProfileFieldViewModel> fields,
        DopeVisualProfileApplyRecord applyRecord,
        IReadOnlyDictionary<string, DopeVisualConfirmationRow> confirmationRows)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(applyRecord);
        ArgumentNullException.ThrowIfNull(confirmationRows);

        var states = new Dictionary<string, DopeVisualRowConfirmationState>(StringComparer.OrdinalIgnoreCase);
        var changedSinceApplyCount = 0;
        var unchangedSinceApplyCount = 0;

        foreach (var field in fields)
        {
            if (applyRecord.RequestedValues.TryGetValue(field.Id, out var requestedValue) &&
                ValuesMatch(field, field.Value, requestedValue))
            {
                unchangedSinceApplyCount++;
                states[field.Id] = confirmationRows.TryGetValue(field.Id, out var confirmationRow)
                    ? BuildConfirmationState(confirmationRow)
                    : DefaultConfirmationState;
            }
            else
            {
                changedSinceApplyCount++;
                states[field.Id] = EditedConfirmationState;
            }
        }

        return new DopeVisualRowConfirmationComputationResult(states, changedSinceApplyCount, unchangedSinceApplyCount);
    }

    public static DopeVisualRowConfirmationState BuildConfirmationState(DopeVisualConfirmationRow confirmationRow)
        => new(
            confirmationRow.State switch
            {
                DopeVisualConfirmationState.Confirmed => "Confirmed",
                DopeVisualConfirmationState.Mismatch => "Mismatch",
                _ => "Waiting"
            },
            confirmationRow.State switch
            {
                DopeVisualConfirmationState.Confirmed => OperationOutcomeKind.Success,
                DopeVisualConfirmationState.Mismatch => OperationOutcomeKind.Warning,
                _ => OperationOutcomeKind.Warning
            });

    public static DopeVisualRowConfirmationState DefaultConfirmationState
        => new("Not applied", OperationOutcomeKind.Preview);

    public static DopeVisualRowConfirmationState EditedConfirmationState
        => new("Edited", OperationOutcomeKind.Warning);

    public static DopeVisualRowConfirmationState MissingTelemetryConfirmationState
        => new("No telemetry", OperationOutcomeKind.Warning);

    private static bool ValuesMatch(
        DopeVisualProfileFieldViewModel field,
        double currentValue,
        double requestedValue)
        => field.IsBoolean
            ? (currentValue >= 0.5d) == (requestedValue >= 0.5d)
            : Math.Abs(currentValue - requestedValue) <= 0.000001d;
}

internal static class DopeVisualStartupSnapshotResolver
{
    public static DopeVisualTuningDocument ResolveDocument(
        DopeVisualTuningCompiler compiler,
        DopeVisualProfileStartupState? startupState,
        DopeVisualTuningDocument? legacyPinnedDocument)
    {
        ArgumentNullException.ThrowIfNull(compiler);

        if (startupState?.ControlValues is { Count: > 0 } snapshotValues)
        {
            try
            {
                return compiler.CreateDocument(
                    startupState.ProfileName,
                    startupState.ProfileNotes,
                    snapshotValues);
            }
            catch (InvalidDataException)
            {
                // Fall through to the legacy/profile-backed path when an older or corrupt snapshot is on disk.
            }
        }

        return legacyPinnedDocument ?? compiler.TemplateDocument;
    }

    public static bool MatchesCurrentSelection(
        DopeVisualTuningCompiler compiler,
        DopeVisualProfileStartupState? startupState,
        string? selectedProfileId,
        DopeVisualTuningDocument currentSelectedDocument,
        DopeVisualTuningDocument? legacyPinnedDocument)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(currentSelectedDocument);

        if (startupState is null ||
            string.IsNullOrWhiteSpace(selectedProfileId) ||
            !string.Equals(selectedProfileId, startupState.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var startupDocument = ResolveDocument(compiler, startupState, legacyPinnedDocument);
        foreach (var control in compiler.TemplateDocument.Controls)
        {
            if (!currentSelectedDocument.ControlValues.TryGetValue(control.Id, out var currentValue))
            {
                return false;
            }

            if (!startupDocument.ControlValues.TryGetValue(control.Id, out var startupValue))
            {
                return false;
            }

            if (string.Equals(control.Type, "bool", StringComparison.OrdinalIgnoreCase))
            {
                if ((currentValue >= 0.5d) != (startupValue >= 0.5d))
                {
                    return false;
                }
            }
            else if (Math.Abs(currentValue - startupValue) > 0.000001d)
            {
                return false;
            }
        }

        return true;
    }
}

internal static class DopeVisualCurrentDocumentResolver
{
    public static bool TryCreate(
        DopeVisualTuningCompiler compiler,
        string profileName,
        string profileNotes,
        IEnumerable<DopeVisualProfileFieldViewModel> fields,
        out DopeVisualTuningDocument? document,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(fields);

        try
        {
            document = compiler.CreateDocument(
                profileName,
                profileNotes,
                fields.ToDictionary(field => field.Id, field => field.Value, StringComparer.OrdinalIgnoreCase));
            error = null;
            return true;
        }
        catch (InvalidDataException ex)
        {
            document = null;
            error = ex.Message;
            return false;
        }
    }
}

internal static class DopeVisualDocumentComparer
{
    public static bool Matches(
        DopeVisualTuningCompiler compiler,
        DopeVisualTuningDocument left,
        DopeVisualTuningDocument right)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (!string.Equals(left.Profile.Name.Trim(), right.Profile.Name.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(
                left.Profile.Notes?.Trim() ?? string.Empty,
                right.Profile.Notes?.Trim() ?? string.Empty,
                StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var control in compiler.TemplateDocument.Controls)
        {
            if (!left.ControlValues.TryGetValue(control.Id, out var leftValue) ||
                !right.ControlValues.TryGetValue(control.Id, out var rightValue))
            {
                return false;
            }

            if (string.Equals(control.Type, "bool", StringComparison.OrdinalIgnoreCase))
            {
                if ((leftValue >= 0.5d) != (rightValue >= 0.5d))
                {
                    return false;
                }

                continue;
            }

            if (Math.Abs(leftValue - rightValue) > 0.000001d)
            {
                return false;
            }
        }

        return true;
    }

    public static bool MatchesControlValues(
        DopeVisualTuningCompiler compiler,
        DopeVisualTuningDocument document,
        IReadOnlyDictionary<string, double> controlValues)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(controlValues);

        foreach (var control in compiler.TemplateDocument.Controls)
        {
            if (!document.ControlValues.TryGetValue(control.Id, out var currentValue) ||
                !controlValues.TryGetValue(control.Id, out var expectedValue))
            {
                return false;
            }

            if (string.Equals(control.Type, "bool", StringComparison.OrdinalIgnoreCase))
            {
                if ((currentValue >= 0.5d) != (expectedValue >= 0.5d))
                {
                    return false;
                }

                continue;
            }

            if (Math.Abs(currentValue - expectedValue) > 0.000001d)
            {
                return false;
            }
        }

        return true;
    }
}

internal static class DopeVisualComparisonRowBuilder
{
    public static IReadOnlyList<DopeVisualComparisonRow> Build(
        DopeVisualTuningCompiler compiler,
        IEnumerable<DopeVisualProfileFieldViewModel> fields,
        DopeVisualTuningDocument? compareDocument = null)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(fields);

        var selectedValues = fields.ToDictionary(field => field.Id, field => field.Value, StringComparer.OrdinalIgnoreCase);
        var compareValues = compareDocument?.ControlValues;
        var rows = new List<DopeVisualComparisonRow>(compiler.TemplateDocument.Controls.Count);

        foreach (var templateControl in compiler.TemplateDocument.Controls)
        {
            var selectedValue = selectedValues.TryGetValue(templateControl.Id, out var currentValue)
                ? currentValue
                : templateControl.Value;
            double? compareValue = null;
            if (compareValues is not null &&
                compareValues.TryGetValue(templateControl.Id, out var startupValue))
            {
                compareValue = startupValue;
            }

            rows.Add(new DopeVisualComparisonRow(
                templateControl.Id,
                templateControl.Label,
                templateControl.BaselineValue,
                selectedValue,
                compareValue,
                selectedValue - templateControl.BaselineValue,
                compareValue is null ? null : selectedValue - compareValue.Value));
        }

        return rows;
    }
}

public sealed class DopeVisualProfileListItemViewModel : ObservableObject
{
    private DopeVisualProfileRecord _record;
    private readonly string? _displayLabelOverride;
    private readonly string? _modifiedLabelOverride;

    public DopeVisualProfileListItemViewModel(
        DopeVisualProfileRecord record,
        bool isBundledBaseline = false,
        bool isBundledProfile = false,
        string? displayLabelOverride = null,
        string? modifiedLabelOverride = null)
    {
        _record = record;
        IsBundledBaseline = isBundledBaseline;
        IsBundledProfile = isBundledProfile;
        _displayLabelOverride = displayLabelOverride;
        _modifiedLabelOverride = modifiedLabelOverride;
    }

    public bool IsBundledBaseline { get; }
    public bool IsBundledProfile { get; }
    public bool IsWritableLocalProfile => !IsBundledBaseline && !IsBundledProfile;
    public string Id => _record.Id;
    public string FilePath => _record.FilePath;
    public string FileHash => _record.FileHash;
    public DopeVisualTuningDocument Document => _record.Document;
    public string ModifiedLabel
        => IsBundledBaseline
            ? _modifiedLabelOverride ?? "Bundled with the APK"
            : IsBundledProfile
                ? _modifiedLabelOverride ?? "Included in this release"
            : _record.ModifiedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string DisplayLabel => _displayLabelOverride ?? _record.Document.Profile.Name;
    public string OriginLabel
        => IsBundledBaseline
            ? "Bundled baseline"
            : IsBundledProfile
                ? "Bundled profile"
                : "Local profile";
    public string SecondaryLabel
        => IsBundledBaseline || IsBundledProfile
            ? $"{OriginLabel} | {ModifiedLabel}"
            : $"{OriginLabel} | Updated {ModifiedLabel}";

    public void Apply(DopeVisualProfileRecord record)
    {
        _record = record;
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(FileHash));
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(ModifiedLabel));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(OriginLabel));
        OnPropertyChanged(nameof(SecondaryLabel));
    }

    public DopeVisualProfileRecord ToRecord() => _record;

    public override string ToString() => DisplayLabel;
}

public sealed class DopeVisualProfileGroupViewModel
{
    public DopeVisualProfileGroupViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }
    public ObservableCollection<DopeVisualProfileFieldViewModel> Fields { get; } = new();
}

public sealed class DopeVisualProfileFieldViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private double _value;
    private string _confirmationLabel = "Not applied";
    private OperationOutcomeKind _confirmationLevel = OperationOutcomeKind.Preview;

    public DopeVisualProfileFieldViewModel(
        DopeVisualTuningControl control,
        Action onChanged)
    {
        Id = control.Id;
        Label = control.Label;
        Type = control.Type;
        ToolTipText = BuildToolTip(control);
        Minimum = control.SafeMinimum;
        Maximum = control.SafeMaximum;
        BaselineValue = control.BaselineValue;
        Units = control.Units;
        _value = control.Value;
        _onChanged = onChanged;
    }

    public string Id { get; }
    public string Label { get; }
    public string Type { get; }
    public bool IsBoolean => string.Equals(Type, "bool", StringComparison.OrdinalIgnoreCase);
    public bool IsInteger => string.Equals(Type, "int", StringComparison.OrdinalIgnoreCase);
    public string ToolTipText { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double BaselineValue { get; }
    public string Units { get; }
    public string BaselineLabel => FormatDisplayValue(BaselineValue);

    public double Value
    {
        get => _value;
        set => SetValue(value, notify: true);
    }

    public string ConfirmationLabel
    {
        get => _confirmationLabel;
        private set => SetProperty(ref _confirmationLabel, value);
    }

    public OperationOutcomeKind ConfirmationLevel
    {
        get => _confirmationLevel;
        private set => SetProperty(ref _confirmationLevel, value);
    }

    public void SetValue(double value, bool notify)
    {
        var normalized = NormalizeValue(value);

        if (SetProperty(ref _value, normalized, nameof(Value)) && notify)
        {
            _onChanged();
        }
    }

    public void ResetToBaseline(bool notify)
        => SetValue(BaselineValue, notify);

    public void SetConfirmation(string label, OperationOutcomeKind level)
    {
        ConfirmationLabel = label;
        ConfirmationLevel = level;
    }

    public double NormalizeValue(double value)
    {
        if (IsBoolean)
        {
            return value >= 0.5d ? 1d : 0d;
        }

        if (IsInteger)
        {
            var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            return Math.Clamp(rounded, Minimum, Maximum);
        }

        return Math.Clamp(value, Minimum, Maximum);
    }

    private string FormatDisplayValue(double value)
    {
        if (IsBoolean)
        {
            return FormatBooleanValue(value);
        }

        if (IsInteger)
        {
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string BuildToolTip(DopeVisualTuningControl control)
    {
        var tradeoffs = string.Join(Environment.NewLine, control.Info.Tradeoffs.Select(tradeoff => "- " + tradeoff));
        if (string.Equals(control.Type, "bool", StringComparison.OrdinalIgnoreCase))
        {
            return $"{control.Info.Effect}{Environment.NewLine}{Environment.NewLine}" +
                   $"Enable: {control.Info.IncreaseLooksLike}{Environment.NewLine}" +
                   $"Disable: {control.Info.DecreaseLooksLike}{Environment.NewLine}{Environment.NewLine}" +
                   $"Baseline: {FormatBooleanValue(control.BaselineValue)}{Environment.NewLine}" +
                   "Values: Off or On" +
                   $"{Environment.NewLine}{Environment.NewLine}{tradeoffs}";
        }

        var numericFormat = string.Equals(control.Type, "int", StringComparison.OrdinalIgnoreCase) ? "0" : "0.###";
        return $"{control.Info.Effect}{Environment.NewLine}{Environment.NewLine}" +
               $"Increase: {control.Info.IncreaseLooksLike}{Environment.NewLine}" +
               $"Decrease: {control.Info.DecreaseLooksLike}{Environment.NewLine}{Environment.NewLine}" +
               $"Baseline: {control.BaselineValue.ToString(numericFormat, CultureInfo.InvariantCulture)}{Environment.NewLine}" +
               $"Range: {control.SafeMinimum.ToString(numericFormat, CultureInfo.InvariantCulture)} .. {control.SafeMaximum.ToString(numericFormat, CultureInfo.InvariantCulture)}{Environment.NewLine}{Environment.NewLine}" +
               $"{tradeoffs}";
    }

    private static string FormatBooleanValue(double value)
        => value >= 0.5d ? "On" : "Off";
}

public sealed class DopeVisualComparisonRowViewModel : ObservableObject
{
    private readonly DopeVisualProfileFieldViewModel _field;
    private string _currentValueText;
    private string _baseline;
    private string _compare;
    private string _deltaFromBaseline;
    private string _deltaBetweenProfiles;
    private string _range;
    private string _confirmation;
    private OperationOutcomeKind _confirmationLevel;

    public DopeVisualComparisonRowViewModel(
        string groupTitle,
        DopeVisualProfileFieldViewModel field,
        DopeVisualComparisonRow comparison,
        DopeVisualRowConfirmationState confirmation)
    {
        _field = field;
        Group = groupTitle;
        Label = comparison.Label;
        ToolTipText = field.ToolTipText;
        _currentValueText = FormatEditableValue(field.Value);
        _baseline = string.Empty;
        _compare = string.Empty;
        _deltaFromBaseline = string.Empty;
        _deltaBetweenProfiles = string.Empty;
        _range = string.Empty;
        _confirmation = string.Empty;
        _confirmationLevel = OperationOutcomeKind.Preview;
        Apply(comparison, confirmation, syncCurrentValueText: true);
    }

    public DopeVisualProfileFieldViewModel Field => _field;
    public string Group { get; }
    public string Label { get; }
    public string ToolTipText { get; }
    public bool IsBooleanEditor => _field.IsBoolean;
    public string Selected => FormatValue(_field.Value);

    public string Baseline
    {
        get => _baseline;
        private set => SetProperty(ref _baseline, value);
    }

    public string Compare
    {
        get => _compare;
        private set
        {
            if (SetProperty(ref _compare, value))
            {
                OnPropertyChanged(nameof(Startup));
            }
        }
    }

    public string Startup
    {
        get => Compare;
        private set => Compare = value;
    }

    public string DeltaFromBaseline
    {
        get => _deltaFromBaseline;
        private set => SetProperty(ref _deltaFromBaseline, value);
    }

    public string DeltaBetweenProfiles
    {
        get => _deltaBetweenProfiles;
        private set
        {
            if (SetProperty(ref _deltaBetweenProfiles, value))
            {
                OnPropertyChanged(nameof(DeltaFromStartup));
            }
        }
    }

    public string DeltaFromStartup
    {
        get => DeltaBetweenProfiles;
        private set => DeltaBetweenProfiles = value;
    }

    public string Range
    {
        get => _range;
        private set => SetProperty(ref _range, value);
    }

    public string Confirmation
    {
        get => _confirmation;
        private set => SetProperty(ref _confirmation, value);
    }

    public OperationOutcomeKind ConfirmationLevel
    {
        get => _confirmationLevel;
        private set => SetProperty(ref _confirmationLevel, value);
    }

    public string CurrentValueText
    {
        get => _currentValueText;
        set
        {
            if (_field.IsBoolean)
            {
                var normalizedBoolean = FormatEditableValue(_field.Value);
                if (!string.Equals(_currentValueText, normalizedBoolean, StringComparison.Ordinal))
                {
                    _currentValueText = normalizedBoolean;
                    OnPropertyChanged(nameof(CurrentValueText));
                }

                return;
            }

            if (!SetProperty(ref _currentValueText, value))
            {
                return;
            }

            if (TryParseEditableValue(value, _field.IsInteger, out var parsed))
            {
                _field.SetValue(parsed, notify: true);
            }

            var normalized = FormatEditableValue(_field.Value);
            if (!string.Equals(_currentValueText, normalized, StringComparison.Ordinal))
            {
                _currentValueText = normalized;
                OnPropertyChanged(nameof(CurrentValueText));
            }
        }
    }

    public bool CurrentBoolValue
    {
        get => _field.Value >= 0.5d;
        set
        {
            if (!_field.IsBoolean)
            {
                return;
            }

            if (CurrentBoolValue == value)
            {
                return;
            }

            _field.SetValue(value ? 1d : 0d, notify: true);
            OnPropertyChanged(nameof(CurrentBoolValue));
            OnPropertyChanged(nameof(CurrentValueText));
            OnPropertyChanged(nameof(Selected));
        }
    }

    public void Apply(
        DopeVisualComparisonRow comparison,
        DopeVisualRowConfirmationState confirmation,
        bool syncCurrentValueText)
    {
        Baseline = FormatValue(comparison.BaselineValue);
        Startup = comparison.CompareValue is null ? string.Empty : FormatValue(comparison.CompareValue.Value);
        DeltaFromBaseline = FormatDelta(comparison.SelectedValue, comparison.BaselineValue);
        DeltaFromStartup = comparison.CompareValue is null ? string.Empty : FormatDelta(comparison.SelectedValue, comparison.CompareValue.Value);
        Range = _field.IsBoolean
            ? "Off / On"
            : $"{FormatValue(_field.Minimum)} .. {FormatValue(_field.Maximum)}";
        Confirmation = confirmation.Label;
        ConfirmationLevel = confirmation.Level;

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(CurrentBoolValue));

        if (syncCurrentValueText)
        {
            var normalized = FormatEditableValue(_field.Value);
            if (!string.Equals(_currentValueText, normalized, StringComparison.Ordinal))
            {
                _currentValueText = normalized;
                OnPropertyChanged(nameof(CurrentValueText));
            }
        }
    }

    private string FormatValue(double value)
    {
        if (_field.IsBoolean)
        {
            return FormatBooleanValue(value);
        }

        if (_field.IsInteger)
        {
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private string FormatEditableValue(double value)
    {
        if (_field.IsBoolean)
        {
            return FormatBooleanValue(value);
        }

        if (_field.IsInteger)
        {
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.CurrentCulture);
        }

        return value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private string FormatDelta(double selected, double reference)
    {
        if (_field.IsBoolean)
        {
            return NearlyEqual(selected, reference) ? "Same" : "Changed";
        }

        if (_field.IsInteger)
        {
            var delta = (int)Math.Round(selected - reference, MidpointRounding.AwayFromZero);
            return delta.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        }

        return (selected - reference).ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture);
    }

    private static bool TryParseEditableValue(string text, bool integer, out double value)
    {
        if (integer)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var currentCultureInt))
            {
                value = currentCultureInt;
                return true;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var invariantInt))
            {
                value = invariantInt;
                return true;
            }
        }

        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value) ||
               double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    private static bool NearlyEqual(double left, double right)
        => Math.Abs(left - right) <= 0.000001d;

    private static string FormatBooleanValue(double value)
        => value >= 0.5d ? "On" : "Off";
}

