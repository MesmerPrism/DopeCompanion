using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace DopeCompanion.Integration.Tests;

public sealed class WpfUiFixture : IDisposable
{
    private readonly Thread uiThread;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Dispatcher? dispatcher;
    private DopeCompanion.App.App? application;

    public WpfUiFixture()
    {
        uiThread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "WpfUiFixtureThread"
        };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        ready.Task.GetAwaiter().GetResult();
    }

    public Dispatcher Dispatcher
        => dispatcher ?? throw new InvalidOperationException("The WPF test dispatcher has not been initialized.");

    public DopeCompanion.App.App Application
        => application ?? throw new InvalidOperationException("The WPF test application has not been initialized.");

    public Task InvokeAsync(Func<Task> action)
        => Dispatcher.InvokeAsync(action).Task.Unwrap();

    public Task<T> InvokeAsync<T>(Func<Task<T>> action)
        => Dispatcher.InvokeAsync(action).Task.Unwrap();

    public void Dispose()
    {
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.Invoke(() =>
        {
            if (application is not null)
            {
                application.Shutdown();
            }
        });

        uiThread.Join(TimeSpan.FromSeconds(10));
    }

    private void ThreadMain()
    {
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

            var app = new DopeCompanion.App.App
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            app.InitializeComponent();

            application = app;
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.SetResult();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
        }
    }
}
