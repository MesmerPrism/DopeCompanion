using System.Windows;
using DopeCompanion.App.ViewModels;

namespace DopeCompanion.App;

partial class FullDiagnosticHarnessWindow : Window
{
    private readonly FullDiagnosticHarnessWindowViewModel _viewModel;

    internal FullDiagnosticHarnessWindow(FullDiagnosticHarnessWindowViewModel viewModel)
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _viewModel.RequestClose += OnRequestClose;
        DataContext = _viewModel;
        Closed += OnClosed;
    }

    private void OnRequestClose(object? sender, EventArgs e)
        => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        Closed -= OnClosed;
    }
}
