using System.ComponentModel;
using DopeCompanion.Core.Models;

namespace DopeCompanion.App.ViewModels;

public sealed class LiveSessionSettingViewModel : ObservableObject, IDisposable
{
    private string _liveValue = "No live readback";
    private string _liveDetail = "Waiting for quest_twin_state runtime values.";
    private OperationOutcomeKind _liveLevel = OperationOutcomeKind.Preview;

    public LiveSessionSettingViewModel(ConfigSettingRowViewModel row)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        Row.PropertyChanged += OnRowPropertyChanged;
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

    public void ApplyLiveValue(string liveValue, string sourceLabel)
    {
        LiveValue = string.IsNullOrWhiteSpace(liveValue) ? "No live readback" : liveValue.Trim();
        var stagedValue = ReadRowValue(Row);
        var matches = ValuesEquivalent(stagedValue, LiveValue);
        LiveLevel = matches ? OperationOutcomeKind.Success : OperationOutcomeKind.Warning;
        LiveDetail = matches
            ? $"{sourceLabel}. Live and staged values match."
            : $"{sourceLabel}. Live value differs from the staged override.";
    }

    public void ClearLiveValue(string detail)
    {
        LiveValue = "No live readback";
        LiveLevel = OperationOutcomeKind.Preview;
        LiveDetail = detail;
    }

    public void Dispose()
    {
        Row.PropertyChanged -= OnRowPropertyChanged;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (LiveLevel is not OperationOutcomeKind.Success and not OperationOutcomeKind.Warning)
        {
            return;
        }

        if (e.PropertyName is nameof(SingleValueRowViewModel.ValueText)
            or nameof(ToggleRowViewModel.Value)
            or nameof(ChoiceRowViewModel.SelectedOption))
        {
            ApplyLiveValue(LiveValue, ExtractSourceLabel(LiveDetail));
        }
    }

    private static string ExtractSourceLabel(string detail)
    {
        var index = detail.IndexOf('.');
        return index > 0 ? detail[..index] : "Live runtime state";
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
