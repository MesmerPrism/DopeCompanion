using System.Threading;

namespace DopeCompanion.Integration.Tests;

public sealed class StudyHarnessTests
{
    [Fact]
    public async Task DopeStudyHarnessRunsWhenEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("DOPE_RUN_MANUAL_HARNESS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        var thread = new Thread(() =>
        {
            var previousDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(repoRoot);
                HarnessScenarioRunner.RunOnceFromCurrentDirectory();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
            finally
            {
                Directory.SetCurrentDirectory(previousDirectory);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await completion.Task.WaitAsync(TimeSpan.FromMinutes(20));
    }
}

