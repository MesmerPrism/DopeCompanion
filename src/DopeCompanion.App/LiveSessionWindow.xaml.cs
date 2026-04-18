using System.Windows;
using DopeCompanion.App.ViewModels;

namespace DopeCompanion.App;

public partial class LiveSessionWindow : Window
{
    public LiveSessionWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        DataContext = viewModel;
    }
}
