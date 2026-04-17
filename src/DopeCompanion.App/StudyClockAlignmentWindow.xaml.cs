using System.Windows;
using DopeCompanion.App.ViewModels;

namespace DopeCompanion.App;

public partial class StudyClockAlignmentWindow : Window
{
    public StudyClockAlignmentWindow(StudyShellViewModel viewModel)
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        DataContext = viewModel;
        Title = $"{viewModel.StudyLabel} Clock Alignment";
    }
}
