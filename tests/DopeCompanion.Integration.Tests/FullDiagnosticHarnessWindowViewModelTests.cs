using DopeCompanion.App.ViewModels;
using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;

namespace DopeCompanion.Integration.Tests;

public sealed class FullDiagnosticHarnessWindowViewModelTests
{
    [Fact]
    public void Prepare_for_run_seeds_pending_steps_and_running_summary()
    {
        var request = new DopeFullDiagnosticHarnessRequest(BuildStudyDefinition());
        var plan = DopeFullDiagnosticHarnessService.BuildExecutionPlan(request);
        var viewModel = new FullDiagnosticHarnessWindowViewModel();

        viewModel.PrepareForRun(plan);

        Assert.Equal(plan.Count, viewModel.Steps.Count);
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.IsCompleted);
        Assert.Equal("Running the full DOPE diagnostic harness...", viewModel.Summary);
        Assert.All(
            viewModel.Steps,
            step => Assert.Equal(DopeFullDiagnosticHarnessProgressState.Pending, step.State));
    }

    [Fact]
    public void Apply_progress_updates_current_step_and_completion_state()
    {
        var request = new DopeFullDiagnosticHarnessRequest(BuildStudyDefinition());
        var plan = DopeFullDiagnosticHarnessService.BuildExecutionPlan(request);
        var viewModel = new FullDiagnosticHarnessWindowViewModel();

        viewModel.PrepareForRun(plan);
        viewModel.ApplyProgress(new DopeFullDiagnosticHarnessProgress(
            "managed-tooling",
            "Managed Quest Tooling",
            0,
            plan.Count,
            DopeFullDiagnosticHarnessProgressState.Running,
            OperationOutcomeKind.Preview,
            "Checking managed tooling.",
            "The harness is verifying that hzdb, Android platform-tools, and scrcpy are ready."));

        Assert.Equal("Managed Quest Tooling", viewModel.CurrentStepLabel);
        Assert.Equal("Running", viewModel.Steps[0].StatusLabel);
        Assert.True(viewModel.ProgressPercent > 0);

        viewModel.ApplyProgress(new DopeFullDiagnosticHarnessProgress(
            "managed-tooling",
            "Managed Quest Tooling",
            0,
            plan.Count,
            DopeFullDiagnosticHarnessProgressState.Completed,
            OperationOutcomeKind.Success,
            "Managed Quest tooling is already present.",
            "hzdb n/a | Android platform-tools n/a | scrcpy n/a"));

        Assert.Equal("Done", viewModel.Steps[0].StatusLabel);
        Assert.Equal(DopeFullDiagnosticHarnessProgressState.Completed, viewModel.Steps[0].State);
    }

    private static StudyShellDefinition BuildStudyDefinition()
    {
        IReadOnlyList<string> none = Array.Empty<string>();

        return new StudyShellDefinition(
            "test-study",
            "Test Study",
            "OpenAI",
            "Test harness study.",
            new StudyPinnedApp(
                "Test App",
                "com.example.test",
                "test.apk",
                "com.example.test/.MainActivity",
                "sha256",
                "1.0.0",
                "notes",
                AllowManualSelection: false,
                LaunchInKioskMode: false),
            new StudyPinnedDeviceProfile(
                "device-profile",
                "Device Profile",
                "Pinned device profile.",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new StudyMonitoringProfile(
                "breathing",
                "heartbeat",
                "coherence",
                "quest_monitor",
                "quest.telemetry",
                0.5d,
                none,
                none,
                none,
                none,
                none,
                none,
                none,
                none,
                none,
                none,
                none,
                none,
                none,
                none,
                none,
                none,
                none,
                none),
            new StudyControlProfile(
                "recenter",
                "particles-on",
                "particles-off"));
    }
}
