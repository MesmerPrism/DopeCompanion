using System.Windows;
using DopeCompanion.App.ViewModels;

namespace DopeCompanion.App;

public partial class StudyTwinEventsWindow : Window
{
    public StudyTwinEventsWindow(StudyShellViewModel viewModel)
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        DataContext = viewModel;
        Title = $"{viewModel.StudyLabel} Twin Event Log";
    }
}
