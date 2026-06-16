using Virgil.Domain;
using Xunit;

namespace Virgil.Tests;

public sealed class VirgilCoreAnimationControllerTests
{
    [Fact]
    public void SetState_changes_current_state_and_starts_animation()
    {
        var controller = new VirgilCoreAnimationController();

        var plan = controller.SetState(VirgilCoreState.Scanning, canAnimate: true);

        Assert.Equal(VirgilCoreState.Scanning, controller.State);
        Assert.True(plan.StartPermanent);
        Assert.True(controller.IsPermanentAnimationRunning);
        Assert.Equal(1, controller.PermanentAnimationStarts);
    }

    [Fact]
    public void Success_returns_to_idle_after_transient_completion()
    {
        var controller = new VirgilCoreAnimationController();
        controller.SetState(VirgilCoreState.Success, canAnimate: true);

        var plan = controller.CompleteTransient(VirgilCoreState.Success, canAnimate: true);

        Assert.Equal(VirgilCoreState.Idle, controller.State);
        Assert.True(plan.StartPermanent);
        Assert.True(controller.IsPermanentAnimationRunning);
    }

    [Fact]
    public void Error_returns_to_idle_after_transient_completion()
    {
        var controller = new VirgilCoreAnimationController();
        controller.SetState(VirgilCoreState.Error, canAnimate: true);

        var plan = controller.CompleteTransient(VirgilCoreState.Error, canAnimate: true);

        Assert.Equal(VirgilCoreState.Idle, controller.State);
        Assert.True(plan.StartPermanent);
    }

    [Fact]
    public void PulseCommunication_does_not_change_main_state()
    {
        var controller = new VirgilCoreAnimationController();
        controller.SetState(VirgilCoreState.Warning, canAnimate: true);

        var plan = controller.PulseCommunication(canAnimate: true);

        Assert.Equal(VirgilCoreState.Warning, controller.State);
        Assert.True(plan.StartCommunication);
        Assert.Equal(VirgilCoreState.Warning, controller.LastCommunicationBaseState);
    }

    [Fact]
    public void Communicating_state_is_transient_alias_for_pulse()
    {
        var controller = new VirgilCoreAnimationController();
        controller.SetState(VirgilCoreState.Scanning, canAnimate: true);

        var plan = controller.SetState(VirgilCoreState.Communicating, canAnimate: true);

        Assert.Equal(VirgilCoreState.Scanning, controller.State);
        Assert.True(plan.StartCommunication);
        Assert.Equal(1, controller.CommunicationPulses);
    }

    [Fact]
    public void Unloaded_host_stops_permanent_animation()
    {
        var controller = new VirgilCoreAnimationController();
        controller.SetState(VirgilCoreState.Idle, canAnimate: true);

        var plan = controller.SetHostState(isLoaded: false, isVisible: false, animationsAllowed: true);

        Assert.False(controller.IsPermanentAnimationRunning);
        Assert.True(plan.StopPermanent);
        Assert.True(plan.ApplyStatic);
        Assert.Equal(1, controller.AnimationStops);
    }

    [Fact]
    public void Repeating_same_state_does_not_start_duplicate_animation()
    {
        var controller = new VirgilCoreAnimationController();
        controller.SetState(VirgilCoreState.Idle, canAnimate: true);

        var plan = controller.SetState(VirgilCoreState.Idle, canAnimate: true);

        Assert.False(plan.StartPermanent);
        Assert.Equal(1, controller.PermanentAnimationStarts);
    }

    [Fact]
    public void Disabled_windows_animations_apply_visible_static_state()
    {
        var controller = new VirgilCoreAnimationController();

        var plan = controller.SetState(VirgilCoreState.Scanning, canAnimate: false);

        Assert.Equal(VirgilCoreState.Scanning, controller.State);
        Assert.True(plan.ApplyStatic);
        Assert.False(controller.IsPermanentAnimationRunning);
        Assert.False(plan.StartPermanent);
    }

    [Fact]
    public void Success_with_disabled_animations_returns_immediately_to_idle()
    {
        var controller = new VirgilCoreAnimationController();

        var plan = controller.SetState(VirgilCoreState.Success, canAnimate: false);

        Assert.Equal(VirgilCoreState.Idle, controller.State);
        Assert.True(plan.ApplyStatic);
        Assert.False(plan.StartTransient);
    }

    [Fact]
    public void Hidden_host_clears_transient_state_to_idle()
    {
        var controller = new VirgilCoreAnimationController();
        controller.SetState(VirgilCoreState.Success, canAnimate: true);

        var plan = controller.SetHostState(isLoaded: true, isVisible: false, animationsAllowed: true);

        Assert.Equal(VirgilCoreState.Idle, controller.State);
        Assert.Equal(VirgilCoreState.Idle, plan.RenderState);
        Assert.True(plan.ApplyStatic);
    }

    [Fact]
    public void Visible_host_restarts_idle_after_interrupted_transient_state()
    {
        var controller = new VirgilCoreAnimationController();
        controller.SetState(VirgilCoreState.Error, canAnimate: true);

        var plan = controller.SetHostState(isLoaded: true, isVisible: true, animationsAllowed: true);

        Assert.Equal(VirgilCoreState.Idle, controller.State);
        Assert.Equal(VirgilCoreState.Idle, plan.RenderState);
        Assert.True(plan.StartPermanent);
    }
}
