namespace DopeCompanion.App.ViewModels;

public sealed class LiveSessionCastFocusLayerOptionViewModel : ObservableObject
{
    private bool _isSelected;
    private LiveSessionSettingSidebarState _state = LiveSessionSettingSidebarState.Staged;
    private string _stateDetail = string.Empty;

    public LiveSessionCastFocusLayerOptionViewModel(string value, string label, string description)
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

    public LiveSessionSettingSidebarState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string StateDetail
    {
        get => _stateDetail;
        set => SetProperty(ref _stateDetail, value);
    }
}
