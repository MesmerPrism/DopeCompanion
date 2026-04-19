using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using DopeCompanion.App;
using DopeCompanion.Core.Models;
using DopeCompanion.Core.Services;

namespace DopeCompanion.App.ViewModels;

internal sealed class FullDiagnosticHarnessWindowViewModel : ObservableObject
{
    private readonly Func<string, bool, Task> _openPathAsync;
    private OperationOutcomeKind _level = OperationOutcomeKind.Preview;
    private bool _isBusy;
    private bool _isCompleted;
    private string _summary = "Run the full harness from Windows Environment to open live progress here.";
    private string _detail = "This window stays focused on the public harness path: managed tooling, Quest connection, APK reinstall, scene staging, launch, and the resulting report bundle.";
    private string _statusLine = "Waiting to start.";
    private string _currentStepProgressLabel = "Pending";
    private string _currentStepLabel = "Harness not started";
    private string _currentStepSummary = "Use Run Full Harness to start the public end-to-end acceptance path.";
    private string _currentStepDetail = "The ordered step list will light up here as the harness progresses.";
    private string _harnessFolderPath = string.Empty;
    private string _harnessBundlePath = string.Empty;
    private string _reportPdfPath = string.Empty;

    public FullDiagnosticHarnessWindowViewModel()
        : this(OpenResolvedPathAsync)
    {
    }

    internal FullDiagnosticHarnessWindowViewModel(Func<string, bool, Task> openPathAsync)
    {
        _openPathAsync = openPathAsync ?? throw new ArgumentNullException(nameof(openPathAsync));

        OpenHarnessFolderCommand = new AsyncRelayCommand(() => OpenPathAsync(HarnessFolderPath, true), () => CanOpenHarnessFolder);
        OpenHarnessBundleCommand = new AsyncRelayCommand(() => OpenPathAsync(HarnessBundlePath, false), () => CanOpenHarnessBundle);
        OpenReportPdfCommand = new AsyncRelayCommand(() => OpenPathAsync(ReportPdfPath, false), () => CanOpenReportPdf);
        CloseCommand = new AsyncRelayCommand(CloseAsync);
    }

    public event EventHandler? RequestClose;

    public ObservableCollection<FullDiagnosticHarnessStepViewModel> Steps { get; } = new();

    public AsyncRelayCommand OpenHarnessFolderCommand { get; }
    public AsyncRelayCommand OpenHarnessBundleCommand { get; }
    public AsyncRelayCommand OpenReportPdfCommand { get; }
    public AsyncRelayCommand CloseCommand { get; }

    public string WindowTitle => "Full Diagnostic Harness";

    public OperationOutcomeKind Level
    {
        get => _level;
        private set => SetProperty(ref _level, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        private set => SetProperty(ref _isCompleted, value);
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public string StatusLine
    {
        get => _statusLine;
        private set => SetProperty(ref _statusLine, value);
    }

    public string CurrentStepProgressLabel
    {
        get => _currentStepProgressLabel;
        private set => SetProperty(ref _currentStepProgressLabel, value);
    }

    public string CurrentStepLabel
    {
        get => _currentStepLabel;
        private set => SetProperty(ref _currentStepLabel, value);
    }

    public string CurrentStepSummary
    {
        get => _currentStepSummary;
        private set => SetProperty(ref _currentStepSummary, value);
    }

    public string CurrentStepDetail
    {
        get => _currentStepDetail;
        private set => SetProperty(ref _currentStepDetail, value);
    }

    public string HarnessFolderPath
    {
        get => _harnessFolderPath;
        private set
        {
            if (SetProperty(ref _harnessFolderPath, NormalizeOperatorPath(value)))
            {
                OnPropertyChanged(nameof(CanOpenHarnessFolder));
                OpenHarnessFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string HarnessBundlePath
    {
        get => _harnessBundlePath;
        private set
        {
            if (SetProperty(ref _harnessBundlePath, NormalizeOperatorPath(value)))
            {
                OnPropertyChanged(nameof(CanOpenHarnessBundle));
                OpenHarnessBundleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ReportPdfPath
    {
        get => _reportPdfPath;
        private set
        {
            if (SetProperty(ref _reportPdfPath, NormalizeOperatorPath(value)))
            {
                OnPropertyChanged(nameof(CanOpenReportPdf));
                OpenReportPdfCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanOpenHarnessFolder
        => CompanionOperatorDataLayout.TryResolveExistingDirectory(HarnessFolderPath, out _);

    public bool CanOpenHarnessBundle
        => CompanionOperatorDataLayout.TryResolveExistingFile(HarnessBundlePath, out _);

    public bool CanOpenReportPdf
        => CompanionOperatorDataLayout.TryResolveExistingFile(ReportPdfPath, out _);

    public int ProgressPercent
    {
        get
        {
            if (Steps.Count == 0)
            {
                return 0;
            }

            var completedSteps = Steps.Count(step => step.State == DopeFullDiagnosticHarnessProgressState.Completed);
            var runningSteps = Steps.Any(step => step.State == DopeFullDiagnosticHarnessProgressState.Running) ? 0.5d : 0d;
            var fraction = (completedSteps + runningSteps) / Steps.Count;
            return (int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero);
        }
    }

    public void PrepareForRun(IReadOnlyList<DopeFullDiagnosticHarnessPlannedStep> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Steps.Clear();
        for (var index = 0; index < plan.Count; index++)
        {
            Steps.Add(new FullDiagnosticHarnessStepViewModel(index + 1, plan[index]));
        }

        Level = OperationOutcomeKind.Preview;
        IsBusy = true;
        IsCompleted = false;
        Summary = "Running the full DOPE diagnostic harness...";
        Detail = "A separate progress window now tracks the full public acceptance path live so the harness does not look idle while it works.";
        StatusLine = $"Started {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
        CurrentStepProgressLabel = plan.Count == 0 ? "Preparing harness" : $"Step 1 of {plan.Count}";
        CurrentStepLabel = "Preparing harness";
        CurrentStepSummary = "The ordered step list is staged and waiting for the first live update.";
        CurrentStepDetail = "Managed tooling, Quest connection, install, scene staging, launch, and report generation will update in place as each stage advances.";
        HarnessFolderPath = string.Empty;
        HarnessBundlePath = string.Empty;
        ReportPdfPath = string.Empty;
        OnPropertyChanged(nameof(ProgressPercent));
    }

    public void ApplyProgress(DopeFullDiagnosticHarnessProgress progress)
    {
        var step = Steps.FirstOrDefault(candidate => string.Equals(candidate.Id, progress.StepId, StringComparison.Ordinal));
        if (step is null)
        {
            return;
        }

        step.Apply(progress);

        if (progress.State == DopeFullDiagnosticHarnessProgressState.Running)
        {
            CurrentStepProgressLabel = $"Step {progress.StepIndex + 1} of {progress.TotalSteps}";
            CurrentStepLabel = step.Label;
            CurrentStepSummary = progress.Summary;
            CurrentStepDetail = progress.Detail;
        }

        OnPropertyChanged(nameof(ProgressPercent));
    }

    public void ApplyResult(DopeFullDiagnosticHarnessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (var lingeringStep in Steps.Where(step => step.State == DopeFullDiagnosticHarnessProgressState.Running))
        {
            lingeringStep.ApplyCompletion(result.Level, result.Summary, result.Detail);
        }

        foreach (var resultStep in result.Steps)
        {
            var step = Steps.FirstOrDefault(candidate => string.Equals(candidate.Id, resultStep.Id, StringComparison.Ordinal));
            step?.ApplyCompletion(resultStep.Level, resultStep.Summary, resultStep.Detail);
        }

        Level = result.Level;
        IsBusy = false;
        IsCompleted = true;
        Summary = result.Summary;
        Detail = result.Detail;
        StatusLine = $"Completed {result.CompletedAtUtc.ToLocalTime():HH:mm:ss}.";
        CurrentStepProgressLabel = $"{Steps.Count(step => step.State == DopeFullDiagnosticHarnessProgressState.Completed)} of {Steps.Count} steps reviewed";
        CurrentStepLabel = result.Level == OperationOutcomeKind.Success
            ? "Harness complete"
            : "Harness complete with advisories";
        CurrentStepSummary = result.Summary;
        CurrentStepDetail = result.Detail;
        HarnessFolderPath = result.ReportDirectory;
        HarnessBundlePath = result.BundlePath;
        ReportPdfPath = result.DiagnosticsPdfPath;
        OnPropertyChanged(nameof(ProgressPercent));
    }

    public void ApplyFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        foreach (var lingeringStep in Steps.Where(step => step.State == DopeFullDiagnosticHarnessProgressState.Running))
        {
            lingeringStep.ApplyCompletion(
                OperationOutcomeKind.Failure,
                "Harness step failed.",
                exception.Message);
        }

        Level = OperationOutcomeKind.Failure;
        IsBusy = false;
        IsCompleted = true;
        Summary = "Full DOPE diagnostic harness failed.";
        Detail = exception.Message;
        StatusLine = $"Failed {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
        CurrentStepLabel = "Harness failed";
        CurrentStepSummary = "The harness stopped before it could finish the full public acceptance path.";
        CurrentStepDetail = exception.Message;
        OnPropertyChanged(nameof(ProgressPercent));
    }

    public void ApplyBlocked(string summary, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        Level = OperationOutcomeKind.Warning;
        IsBusy = false;
        IsCompleted = true;
        Summary = summary;
        Detail = detail;
        StatusLine = $"Blocked {DateTimeOffset.UtcNow.ToLocalTime():HH:mm:ss}.";
        CurrentStepProgressLabel = $"{Steps.Count(step => step.State == DopeFullDiagnosticHarnessProgressState.Completed)} of {Steps.Count} steps started";
        CurrentStepLabel = "Harness blocked";
        CurrentStepSummary = summary;
        CurrentStepDetail = detail;
        OnPropertyChanged(nameof(ProgressPercent));
    }

    private async Task OpenPathAsync(string path, bool isDirectory)
    {
        var resolvedPath = isDirectory
            ? CompanionOperatorDataLayout.TryResolveExistingDirectory(path, out var directoryPath) ? directoryPath : null
            : CompanionOperatorDataLayout.TryResolveExistingFile(path, out var filePath) ? filePath : null;

        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return;
        }

        await _openPathAsync(resolvedPath, isDirectory).ConfigureAwait(false);
    }

    private Task CloseAsync()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private static string NormalizeOperatorPath(string? path)
        => CompanionOperatorDataLayout.NormalizeHostVisiblePath(path);

    private static Task OpenResolvedPathAsync(string path, bool _)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }
}

internal sealed class FullDiagnosticHarnessStepViewModel : ObservableObject
{
    private readonly string _description;
    private DopeFullDiagnosticHarnessProgressState _state = DopeFullDiagnosticHarnessProgressState.Pending;
    private OperationOutcomeKind _level = OperationOutcomeKind.Preview;
    private string _summary;
    private string _detail = string.Empty;

    public FullDiagnosticHarnessStepViewModel(int ordinal, DopeFullDiagnosticHarnessPlannedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        Ordinal = ordinal;
        Id = step.Id;
        Label = step.Label;
        _description = step.Description;
        _summary = step.Description;
    }

    public int Ordinal { get; }
    public string Id { get; }
    public string Label { get; }

    public DopeFullDiagnosticHarnessProgressState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                RaiseDerivedStateChanged();
            }
        }
    }

    public OperationOutcomeKind Level
    {
        get => _level;
        private set
        {
            if (SetProperty(ref _level, value))
            {
                RaiseDerivedStateChanged();
            }
        }
    }

    public string Summary
    {
        get => _summary;
        private set
        {
            if (SetProperty(ref _summary, value))
            {
                OnPropertyChanged(nameof(SupportingText));
            }
        }
    }

    public string Detail
    {
        get => _detail;
        private set
        {
            if (SetProperty(ref _detail, value))
            {
                OnPropertyChanged(nameof(IsDetailVisible));
            }
        }
    }

    public string OrdinalLabel => Ordinal.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    public string SupportingText
        => State == DopeFullDiagnosticHarnessProgressState.Pending ? _description : Summary;

    public string StatusLabel
        => State switch
        {
            DopeFullDiagnosticHarnessProgressState.Running => "Running",
            DopeFullDiagnosticHarnessProgressState.Completed when Level == OperationOutcomeKind.Success => "Done",
            DopeFullDiagnosticHarnessProgressState.Completed when Level == OperationOutcomeKind.Warning => "Warning",
            DopeFullDiagnosticHarnessProgressState.Completed when Level == OperationOutcomeKind.Failure => "Failed",
            DopeFullDiagnosticHarnessProgressState.Completed => "Skipped",
            _ => "Pending"
        };

    public Brush AccentBrush => ResolveBrush(
        State switch
        {
            DopeFullDiagnosticHarnessProgressState.Running => BrushResourceLookup.Info(),
            DopeFullDiagnosticHarnessProgressState.Completed when Level == OperationOutcomeKind.Success => BrushResourceLookup.Success(),
            DopeFullDiagnosticHarnessProgressState.Completed when Level == OperationOutcomeKind.Warning => BrushResourceLookup.Warning(),
            DopeFullDiagnosticHarnessProgressState.Completed when Level == OperationOutcomeKind.Failure => BrushResourceLookup.Failure(),
            _ => BrushResourceLookup.Muted()
        },
        Brushes.Gray);

    public Brush SoftBrush => ResolveBrush(
        State switch
        {
            DopeFullDiagnosticHarnessProgressState.Running => BrushResourceLookup.InfoSoft(),
            DopeFullDiagnosticHarnessProgressState.Completed when Level == OperationOutcomeKind.Success => BrushResourceLookup.SuccessSoft(),
            DopeFullDiagnosticHarnessProgressState.Completed when Level == OperationOutcomeKind.Warning => BrushResourceLookup.WarningSoft(),
            DopeFullDiagnosticHarnessProgressState.Completed when Level == OperationOutcomeKind.Failure => BrushResourceLookup.FailureSoft(),
            _ => BrushResourceLookup.PanelAlt()
        },
        Brushes.Transparent);

    public bool IsDetailVisible => !string.IsNullOrWhiteSpace(Detail);

    public void Apply(DopeFullDiagnosticHarnessProgress progress)
    {
        State = progress.State;
        Level = progress.Level;

        if (progress.State != DopeFullDiagnosticHarnessProgressState.Pending)
        {
            Summary = progress.Summary;
            Detail = progress.Detail;
        }
    }

    public void ApplyCompletion(OperationOutcomeKind level, string summary, string detail)
    {
        State = DopeFullDiagnosticHarnessProgressState.Completed;
        Level = level;
        Summary = summary;
        Detail = detail;
    }

    private void RaiseDerivedStateChanged()
    {
        OnPropertyChanged(nameof(SupportingText));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(AccentBrush));
        OnPropertyChanged(nameof(SoftBrush));
    }

    private static Brush ResolveBrush(object resource, Brush fallback)
        => resource as Brush ?? fallback;
}
