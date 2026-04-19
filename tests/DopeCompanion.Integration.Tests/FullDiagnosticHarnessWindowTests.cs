using System.Windows;
using System.Windows.Threading;
using DopeCompanion.App;
using DopeCompanion.App.ViewModels;

namespace DopeCompanion.Integration.Tests;

[Collection("WpfUi")]
public sealed class FullDiagnosticHarnessWindowTests
{
    private readonly WpfUiFixture fixture;

    public FullDiagnosticHarnessWindowTests(WpfUiFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Full_diagnostic_harness_window_declares_step_list_and_output_buttons()
    {
        var xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "DopeCompanion.App",
            "FullDiagnosticHarnessWindow.xaml");

        var xaml = await File.ReadAllTextAsync(Path.GetFullPath(xamlPath));

        Assert.Contains("ItemsSource=\"{Binding Steps}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentStepLabel}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open Harness Folder\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open Harness Bundle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open Report PDF\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Full_diagnostic_harness_window_loads_without_missing_resource_failures()
    {
        await fixture.InvokeAsync(async () =>
        {
            var app = fixture.Application;
            if (app.Dispatcher.CheckAccess())
            {
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            var viewModel = new FullDiagnosticHarnessWindowViewModel();
            var window = new FullDiagnosticHarnessWindow(viewModel)
            {
                Width = 1180,
                Height = 820,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            window.Show();
            await WaitForConditionAsync(() => window.IsLoaded && window.IsVisible, TimeSpan.FromSeconds(5));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            Assert.True(window.IsVisible);

            window.Close();
        });
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(condition(), "Timed out waiting for the expected WPF state.");
    }
}
