using HighwaySpeedEnforcement.Client.Application;
using HighwaySpeedEnforcement.Client.ViewModels;

namespace HighwaySpeedEnforcement.Unit.Tests.Client.ViewModels;

/// <summary>The refresh period the user can choose (an extension of BR-UI-02; the default stays 3 s).</summary>
public sealed class MainViewModelPollIntervalTests
{
    private static readonly DisplayFormatter Formatter = new(EntryViewModelTests.PlusTwo);

    private readonly FakeStateSource _source = new();

    [Fact]
    [Trait("BR", "BR-UI-02")]
    public void The_selection_starts_at_the_current_period_and_offers_the_standard_ones()
    {
        var control = new FakePollIntervalControl(TimeSpan.FromSeconds(3));

        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter, control);

        Assert.True(vm.CanChangePollInterval);
        Assert.Equal(TimeSpan.FromSeconds(3), vm.SelectedPollInterval);
        Assert.Equal([1, 2, 3, 5], vm.PollIntervalOptions.Select(option => (int)option.TotalSeconds));
        Assert.Empty(control.Requests);
    }

    [Fact]
    [Trait("BR", "BR-UI-02")]
    public void Choosing_a_period_passes_it_to_the_poller()
    {
        var control = new FakePollIntervalControl(TimeSpan.FromSeconds(3));
        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter, control);

        vm.SelectedPollInterval = TimeSpan.FromSeconds(1);

        Assert.Equal([TimeSpan.FromSeconds(1)], control.Requests);
        Assert.Equal(TimeSpan.FromSeconds(1), control.Interval);
    }

    [Fact]
    public void A_configured_period_outside_the_standard_ones_is_offered_too()
    {
        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter, new FakePollIntervalControl(TimeSpan.FromSeconds(4)));

        Assert.Equal([1, 2, 3, 4, 5], vm.PollIntervalOptions.Select(option => (int)option.TotalSeconds));
        Assert.Equal(TimeSpan.FromSeconds(4), vm.SelectedPollInterval);
    }

    [Fact]
    public void Without_a_period_control_nothing_can_be_chosen()
    {
        using var vm = new MainViewModel(_source, new InlineDispatcher(), Formatter);

        Assert.False(vm.CanChangePollInterval);
        Assert.Empty(vm.PollIntervalOptions);
    }
}
