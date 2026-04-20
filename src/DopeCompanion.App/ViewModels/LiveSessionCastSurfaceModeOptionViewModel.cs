namespace DopeCompanion.App.ViewModels;

public sealed class LiveSessionCastSurfaceModeOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public LiveSessionCastSurfaceModeOptionViewModel(string value, string label, string description)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    public string Value { get; }

    public string Label { get; }

    public string Description { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
