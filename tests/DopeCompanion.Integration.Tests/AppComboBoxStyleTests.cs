using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace DopeCompanion.Integration.Tests;

[Collection("WpfUi")]
public sealed class AppComboBoxStyleTests
{
    private readonly WpfUiFixture fixture;

    public AppComboBoxStyleTests(WpfUiFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Global_combo_box_template_opens_dropdown_from_template_toggle()
    {
        await fixture.InvokeAsync(async () =>
        {
            var app = fixture.Application;
            if (app.Dispatcher.CheckAccess())
            {
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            var comboBox = new ComboBox
            {
                Width = 280,
                ItemsSource = new[] { "Baseline", "Soft" },
                SelectedIndex = 0
            };
            var window = new Window
            {
                Content = comboBox,
                Width = 420,
                Height = 220,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            try
            {
                window.Show();
                await WaitForConditionAsync(() => comboBox.IsLoaded && window.IsVisible, TimeSpan.FromSeconds(5));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                comboBox.ApplyTemplate();
                comboBox.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var selectedContent = Assert.IsType<ContentPresenter>(FindDescendant<ContentPresenter>(comboBox));
                Assert.Equal("Baseline", selectedContent.Content?.ToString());
                Assert.Same(app.Resources["InkBrush"], TextElement.GetForeground(selectedContent));

                var dropDownToggle = Assert.IsType<ToggleButton>(FindDescendant<ToggleButton>(comboBox));
                Assert.NotNull(BindingOperations.GetBindingExpression(dropDownToggle, ToggleButton.IsCheckedProperty));

                dropDownToggle.IsChecked = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                Assert.True(comboBox.IsDropDownOpen);
            }
            finally
            {
                comboBox.IsDropDownOpen = false;
                window.Close();
            }
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

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T typedRoot)
        {
            return typedRoot;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var match = FindDescendant<T>(VisualTreeHelper.GetChild(root, index));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
