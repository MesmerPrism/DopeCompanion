using DopeCompanion.App.ViewModels;
using DopeCompanion.Core.Models;

namespace DopeCompanion.Integration.Tests;

public sealed class LiveSessionSettingViewModelTests
{
    [Fact]
    public void Requested_value_marks_setting_as_pending()
    {
        var row = new SingleValueRowViewModel("projected_feed_brightness", "Brightness", "Test setting.", "1.10");
        var viewModel = new LiveSessionSettingViewModel(row);

        viewModel.ApplyRequestedValue("1.10");

        Assert.Equal(LiveSessionSettingSidebarState.Pending, viewModel.SidebarState);
    }

    [Fact]
    public void Matching_live_value_marks_setting_as_verified()
    {
        var row = new SingleValueRowViewModel("projected_feed_brightness", "Brightness", "Test setting.", "1.10");
        var viewModel = new LiveSessionSettingViewModel(row);

        viewModel.ApplyRequestedValue("1.10");
        viewModel.ApplyLiveValue("1.10", "Reported directly on quest_twin_state");

        Assert.Equal(LiveSessionSettingSidebarState.Verified, viewModel.SidebarState);
        Assert.Equal(OperationOutcomeKind.Success, viewModel.LiveLevel);
    }

    [Fact]
    public void Failed_apply_marks_setting_as_failed_until_editor_changes()
    {
        var row = new SingleValueRowViewModel("projected_feed_brightness", "Brightness", "Test setting.", "1.10");
        var viewModel = new LiveSessionSettingViewModel(row);

        viewModel.ApplyRequestedValue("1.10");
        viewModel.ApplyFailedValue("1.10", "LSL publish failed.");

        Assert.Equal(LiveSessionSettingSidebarState.Failed, viewModel.SidebarState);

        row.ValueText = "1.25";

        Assert.Equal(LiveSessionSettingSidebarState.Staged, viewModel.SidebarState);
    }

    [Fact]
    public void Verified_live_state_wins_over_failed_snapshot()
    {
        var row = new SingleValueRowViewModel("projected_feed_brightness", "Brightness", "Test setting.", "1.10");
        var viewModel = new LiveSessionSettingViewModel(row);

        viewModel.ApplyFailedValue("1.10", "LSL publish failed.");
        viewModel.ApplyLiveValue("1.10", "Pulled from device runtime_overrides.csv");

        Assert.Equal(LiveSessionSettingSidebarState.Verified, viewModel.SidebarState);
        Assert.Equal(OperationOutcomeKind.Success, viewModel.LiveLevel);
    }
}
