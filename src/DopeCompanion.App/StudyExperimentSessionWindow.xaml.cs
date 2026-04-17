using System.Windows;
using DopeCompanion.App.ViewModels;

namespace DopeCompanion.App;

public partial class StudyExperimentSessionWindow : Window
{
    public StudyExperimentSessionWindow(StudyShellViewModel viewModel)
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        DataContext = viewModel;
        Title = $"{viewModel.StudyLabel} Experiment Session";
    }
}
