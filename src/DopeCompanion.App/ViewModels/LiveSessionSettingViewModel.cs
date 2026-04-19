using System.ComponentModel;
using DopeCompanion.Core.Models;

namespace DopeCompanion.App.ViewModels;

public sealed class LiveSessionSettingViewModel : ObservableObject, IDisposable
{
    private string _liveValue = "No live readback";
    private string _liveDetail = "Waiting for quest_twin_state runtime values.";
    private OperationOutcomeKind _liveLevel = OperationOutcomeKind.Preview;
    private string? _reportedValue;
    private string _reportedSourceLabel = "Live runtime state";
    private string? _requestedValue;
    private string? _failedValue;
    private string _failedDetail = string.Empty;
    private LiveSessionSettingSidebarState _sidebarState = LiveSessionSettingSidebarState.Staged;
    private string _sidebarStateDetail = "Current editor value is staged locally and has not been applied yet.";

    public LiveSessionSettingViewModel(ConfigSettingRowViewModel row)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        Row.PropertyChanged += OnRowPropertyChanged;
        UpdateSidebarState();
    }

    public ConfigSettingRowViewModel Row { get; }

    public string Key => Row.Key;

    public string Label => Row.Label;

    public string Description => Row.Description;

    public string LiveValue
    {
        get => _liveValue;
        private set => SetProperty(ref _liveValue, value);
    }

    public string LiveDetail
    {
        get => _liveDetail;
        private set => SetProperty(ref _liveDetail, value);
    }

    public OperationOutcomeKind LiveLevel
    {
        get => _liveLevel;
        private set => SetProperty(ref _liveLevel, value);
    }

    public LiveSessionSettingSidebarState SidebarState
    {
        get => _sidebarState;
        private set => SetProperty(ref _sidebarState, value);
    }

    public string SidebarStateDetail
    {
        get => _sidebarStateDetail;
        private set => SetProperty(ref _sidebarStateDetail, value);
    }

    public void ApplyLiveValue(string liveValue, string sourceLabel)
    {
        _reportedValue = NormalizeValue(liveValue);
        _reportedSourceLabel = string.IsNullOrWhiteSpace(sourceLabel) ? "Live runtime state" : sourceLabel.Trim();
        LiveValue = string.IsNullOrWhiteSpace(_reportedValue) ? "No live readback" : _reportedValue;
        UpdateLiveComparison();
        UpdateSidebarState();
    }

    public void ClearLiveValue(string detail)
    {
        _reportedValue = null;
        _reportedSourceLabel = "Live runtime state";
        LiveValue = "No live readback";
        LiveLevel = OperationOutcomeKind.Preview;
        LiveDetail = detail;
        UpdateSidebarState();
    }

    public void ApplyRequestedValue(string? requestedValue)
    {
        _requestedValue = NormalizeValue(requestedValue);
        UpdateSidebarState();
    }

    public void ApplyFailedValue(string attemptedValue, string detail)
    {
        _failedValue = NormalizeValue(attemptedValue);
        _failedDetail = string.IsNullOrWhiteSpace(detail)
            ? "The last apply attempt failed for this value."
            : detail.Trim();
        UpdateSidebarState();
    }

    public void ClearFailureState()
    {
        _failedValue = null;
        _failedDetail = string.Empty;
        UpdateSidebarState();
    }

    public void Dispose()
    {
        Row.PropertyChanged -= OnRowPropertyChanged;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SingleValueRowViewModel.ValueText)
            or nameof(ToggleRowViewModel.Value)
            or nameof(ChoiceRowViewModel.SelectedOption))
        {
            UpdateLiveComparison();
            UpdateSidebarState();
        }
    }

    private static string ReadRowValue(ConfigSettingRowViewModel row)
        => row switch
        {
            SingleValueRowViewModel single => single.ValueText.Trim(),
            ToggleRowViewModel toggle => toggle.Value ? "true" : "false",
            ChoiceRowViewModel choice => choice.SelectedOption.Trim(),
            MultilineRowViewModel multiline => multiline.ValueText.Trim(),
            _ => string.Empty
        };

    private void UpdateLiveComparison()
    {
        if (string.IsNullOrWhiteSpace(_reportedValue))
        {
            if (LiveLevel is OperationOutcomeKind.Success or OperationOutcomeKind.Warning)
            {
                LiveValue = "No live readback";
                LiveLevel = OperationOutcomeKind.Preview;
                LiveDetail = "Waiting for quest_twin_state runtime values.";
            }

            return;
        }

        var stagedValue = ReadRowValue(Row);
        var matches = ValuesEquivalent(stagedValue, _reportedValue);
        LiveValue = _reportedValue;
        LiveLevel = matches ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning;
        LiveDetail = matches
            ? $"{_reportedSourceLabel}. Live and staged values match."
            : $"{_reportedSourceLabel}. Live value differs from the staged override.";
    }

    private void UpdateSidebarState()
    {
        var currentValue = NormalizeValue(ReadRowValue(Row)) ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(_reportedValue) && ValuesEquivalent(currentValue, _reportedValue))
        {
            SidebarState = LiveSessionSettingSidebarState.Verified;
            SidebarStateDetail = $"{_reportedSourceLabel}. Current value verified by the headset.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_failedValue) && ValuesEquivalent(currentValue, _failedValue))
        {
            SidebarState = LiveSessionSettingSidebarState.Failed;
            SidebarStateDetail = _failedDetail;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_requestedValue) && ValuesEquivalent(currentValue, _requestedValue))
        {
            SidebarState = LiveSessionSettingSidebarState.Pending;
            SidebarStateDetail = "This value was requested and is waiting for live verification from the headset.";
            return;
        }

        SidebarState = LiveSessionSettingSidebarState.Staged;
        SidebarStateDetail = string.IsNullOrWhiteSpace(_reportedValue)
            ? "Current editor value is staged locally and has not been applied yet."
            : "Current editor value differs from the live headset state and has not been requested yet.";
    }

    private static string? NormalizeValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ValuesEquivalent(string left, string right)
    {
        if (string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (bool.TryParse(left, out var leftBool) && bool.TryParse(right, out var rightBool))
        {
            return leftBool == rightBool;
        }

        return double.TryParse(left, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var leftNumber)
            && double.TryParse(right, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rightNumber)
            && Math.Abs(leftNumber - rightNumber) < 0.0001d;
    }
}

public enum LiveSessionSettingSidebarState
{
    Staged,
    Pending,
    Verified,
    Failed
}
