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

    [Fact]
    public void Idle_full_profile_has_opposing_visible_rotations()
    {
        var profile = VirgilMotionProfiles.For(VirgilCoreState.Idle, compact: false);

        Assert.True(profile.HasOpposingRotations);
        Assert.InRange(profile.OuterRotationSeconds, 14, 18);
        Assert.InRange(profile.InnerRotationSeconds, 8, 12);
        Assert.InRange(profile.SegmentRotationSeconds, 20, 24);
        Assert.True(profile.OuterRotationSeconds < 20);
    }

    [Fact]
    public void Fragment_motion_has_full_and_compact_amplitudes()
    {
        var full = VirgilMotionProfiles.For(VirgilCoreState.Idle, compact: false);
        var compact = VirgilMotionProfiles.For(VirgilCoreState.Idle, compact: true);

        Assert.True(full.FragmentTranslationPixels >= 5);
        Assert.InRange(full.FragmentCycleSeconds, 2.2, 3.2);
        Assert.InRange(compact.FragmentTranslationPixels, 2, 3);
    }

    [Fact]
    public void Scanning_profile_is_faster_than_idle()
    {
        var idle = VirgilMotionProfiles.For(VirgilCoreState.Idle, compact: false);
        var scanning = VirgilMotionProfiles.For(VirgilCoreState.Scanning, compact: false);

        Assert.True(scanning.OuterRotationSeconds < idle.OuterRotationSeconds);
        Assert.True(scanning.InnerRotationSeconds < idle.InnerRotationSeconds);
        Assert.True(scanning.SegmentRotationSeconds < idle.SegmentRotationSeconds);
        Assert.True(scanning.FragmentTranslationPixels > idle.FragmentTranslationPixels);
    }

    [Fact]
    public void Communication_profile_has_spatial_motion_without_changing_state()
    {
        var controller = new VirgilCoreAnimationController();
        controller.SetState(VirgilCoreState.Scanning, canAnimate: true);
        var profile = VirgilMotionProfiles.For(VirgilCoreState.Communicating, compact: false);

        var plan = controller.PulseCommunication(canAnimate: true);

        Assert.Equal(VirgilCoreState.Scanning, controller.State);
        Assert.True(plan.StartCommunication);
        Assert.InRange(profile.CommunicationRotationDegrees, 20, 35);
        Assert.InRange(profile.CommunicationTranslationPixels, 4, 6);
        Assert.True(profile.HasPhysicalMotion);
    }

    [Fact]
    public void Success_profile_contains_rotation_and_translation()
    {
        var profile = VirgilMotionProfiles.For(VirgilCoreState.Success, compact: false);

        Assert.InRange(profile.SuccessRotationDegrees, 70, 110);
        Assert.True(profile.SuccessTranslationPixels > 0);
        Assert.True(profile.HasRotation);
        Assert.True(profile.HasTranslation);
    }

    [Fact]
    public void Animated_profiles_are_not_opacity_only()
    {
        foreach (var state in new[]
        {
            VirgilCoreState.Idle,
            VirgilCoreState.Scanning,
            VirgilCoreState.Warning,
            VirgilCoreState.SensitiveAction,
            VirgilCoreState.Executing,
            VirgilCoreState.Success,
            VirgilCoreState.Error,
            VirgilCoreState.Communicating
        })
        {
            var profile = VirgilMotionProfiles.For(state, compact: false);

            Assert.False(profile.IsOpacityOnly);
            Assert.True(profile.HasPhysicalMotion);
        }
    }

    [Fact]
    public void Continuous_rotation_profiles_are_linear()
    {
        foreach (var state in new[]
        {
            VirgilCoreState.Idle,
            VirgilCoreState.Scanning,
            VirgilCoreState.Warning,
            VirgilCoreState.Executing
        })
        {
            var profile = VirgilMotionProfiles.For(state, compact: false);

            Assert.True(profile.UsesLinearContinuousRotation);
            Assert.Equal(360, profile.OuterRotationDegrees);
            Assert.Equal(-360, profile.InnerRotationDegrees);
        }
    }

    [Fact]
    public void Boot_profile_has_short_mechanical_startup_motion()
    {
        var profile = VirgilMotionProfiles.Boot(compact: false);

        Assert.InRange(profile.OuterRotationSeconds, 1.3, 1.8);
        Assert.InRange(profile.OuterRotationDegrees, 120, 180);
        Assert.InRange(Math.Abs(profile.InnerRotationDegrees), 90, 140);
        Assert.Equal(7, profile.FragmentTranslationPixels);
        Assert.True(profile.HasTacticalSweep);
    }
}
