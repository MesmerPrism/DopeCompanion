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
    public async Task GlobalComboBoxTemplate_OpensPopupFromDropdownToggle()
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
                ItemsSource = new[] { "Display mirror", "Render view" },
                SelectedIndex = 0,
                Width = 240,
                MaxDropDownHeight = 120
            };
            var window = new Window
            {
                Content = comboBox,
                Width = 360,
                Height = 180,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            try
            {
                window.Show();
                await WaitForConditionAsync(() => window.IsLoaded && window.IsVisible, TimeSpan.FromSeconds(5));
                comboBox.ApplyTemplate();
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var selectedContent = Assert.IsType<ContentPresenter>(FindDescendant<ContentPresenter>(comboBox));
                Assert.False(string.IsNullOrWhiteSpace(selectedContent.Content?.ToString()));
                Assert.Same(Application.Current.Resources["InkBrush"], TextElement.GetForeground(selectedContent));
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

    private static T? FindDescendant<T>(DependencyObject? node)
        where T : DependencyObject
    {
        if (node is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var child = VisualTreeHelper.GetChild(node, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
