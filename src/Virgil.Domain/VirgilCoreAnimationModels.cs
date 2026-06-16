using System;

namespace Virgil.Domain;

public enum VirgilCoreState
{
    Idle,
    Scanning,
    Success,
    Warning,
    Error,
    Communicating,
    SensitiveAction,
    Executing
}

public sealed record VirgilCoreAnimationPlan(
    VirgilCoreState RenderState,
    bool StopPermanent = false,
    bool StartPermanent = false,
    bool StartTransient = false,
    bool StartCommunication = false,
    bool ApplyStatic = false)
{
    public static VirgilCoreAnimationPlan None(VirgilCoreState state)
    {
        return new VirgilCoreAnimationPlan(state);
    }
}

public sealed record VirgilMotionProfile(
    VirgilCoreState State,
    bool IsCompact,
    double OuterRotationDegrees,
    double OuterRotationSeconds,
    double InnerRotationDegrees,
    double InnerRotationSeconds,
    double SegmentRotationDegrees,
    double SegmentRotationSeconds,
    double FragmentTranslationPixels,
    double FragmentCycleSeconds,
    double CommunicationRotationDegrees,
    double CommunicationTranslationPixels,
    double SuccessRotationDegrees,
    double SuccessTranslationPixels,
    bool UsesLinearContinuousRotation,
    bool HasTacticalSweep,
    bool HasPhysicalMotion)
{
    public bool HasOpposingRotations => OuterRotationDegrees * InnerRotationDegrees < 0;

    public bool HasRotation => Math.Abs(OuterRotationDegrees) > 0 ||
        Math.Abs(InnerRotationDegrees) > 0 ||
        Math.Abs(SegmentRotationDegrees) > 0 ||
        Math.Abs(CommunicationRotationDegrees) > 0 ||
        Math.Abs(SuccessRotationDegrees) > 0;

    public bool HasTranslation => FragmentTranslationPixels > 0 ||
        CommunicationTranslationPixels > 0 ||
        SuccessTranslationPixels > 0;

    public bool IsOpacityOnly => !HasRotation && !HasTranslation && !HasTacticalSweep;
}

public static class VirgilMotionProfiles
{
    public static VirgilMotionProfile For(VirgilCoreState state, bool compact)
    {
        return state switch
        {
            VirgilCoreState.Scanning => Create(
                state,
                compact,
                outerSeconds: 4.8,
                innerSeconds: 3.0,
                segmentSeconds: 5.8,
                fragmentPixels: compact ? 3 : 8,
                fragmentSeconds: compact ? 2.5 : 2.2,
                communicationDegrees: 30,
                communicationPixels: compact ? 3 : 5,
                successDegrees: 0,
                successPixels: 0,
                hasSweep: true),
            VirgilCoreState.Warning => Create(
                state,
                compact,
                outerSeconds: 18,
                innerSeconds: 12,
                segmentSeconds: 24,
                fragmentPixels: compact ? 2.5 : 5,
                fragmentSeconds: compact ? 3.2 : 3.0,
                communicationDegrees: 24,
                communicationPixels: compact ? 3 : 5,
                successDegrees: 0,
                successPixels: 0,
                hasSweep: false),
            VirgilCoreState.SensitiveAction => Create(
                state,
                compact,
                outerDegrees: 10,
                outerSeconds: 3.2,
                innerDegrees: -360,
                innerSeconds: 28,
                segmentDegrees: 0,
                segmentSeconds: 0,
                fragmentPixels: compact ? 2 : 4,
                fragmentSeconds: 3.4,
                communicationDegrees: 20,
                communicationPixels: compact ? 3 : 4,
                successDegrees: 0,
                successPixels: 0,
                hasSweep: false,
                linear: false),
            VirgilCoreState.Executing => Create(
                state,
                compact,
                outerSeconds: 4.2,
                innerSeconds: 2.6,
                segmentSeconds: 5.2,
                fragmentPixels: compact ? 3 : 8,
                fragmentSeconds: compact ? 2.4 : 2.0,
                communicationDegrees: 32,
                communicationPixels: compact ? 3 : 5,
                successDegrees: 0,
                successPixels: 0,
                hasSweep: true),
            VirgilCoreState.Success => Create(
                state,
                compact,
                outerDegrees: 0,
                outerSeconds: 0,
                innerDegrees: 0,
                innerSeconds: 0,
                segmentDegrees: 0,
                segmentSeconds: 0,
                fragmentPixels: 0,
                fragmentSeconds: 0,
                communicationDegrees: 0,
                communicationPixels: 0,
                successDegrees: 92,
                successPixels: compact ? 3 : 6,
                hasSweep: true,
                linear: false),
            VirgilCoreState.Error => Create(
                state,
                compact,
                outerDegrees: -28,
                outerSeconds: 0.32,
                innerDegrees: 18,
                innerSeconds: 0.32,
                segmentDegrees: -16,
                segmentSeconds: 0.42,
                fragmentPixels: compact ? 2 : 4,
                fragmentSeconds: 0.48,
                communicationDegrees: 0,
                communicationPixels: 0,
                successDegrees: 0,
                successPixels: 0,
                hasSweep: false,
                linear: false),
            VirgilCoreState.Communicating => Create(
                state,
                compact,
                outerDegrees: 0,
                outerSeconds: 0,
                innerDegrees: 0,
                innerSeconds: 0,
                segmentDegrees: 0,
                segmentSeconds: 0,
                fragmentPixels: 0,
                fragmentSeconds: 0,
                communicationDegrees: 28,
                communicationPixels: compact ? 3 : 5,
                successDegrees: 0,
                successPixels: 0,
                hasSweep: true,
                linear: false),
            _ => Create(
                VirgilCoreState.Idle,
                compact,
                outerSeconds: 16,
                innerSeconds: 10,
                segmentSeconds: 22,
                fragmentPixels: compact ? 2.5 : 6,
                fragmentSeconds: compact ? 3.0 : 2.6,
                communicationDegrees: 28,
                communicationPixels: compact ? 3 : 5,
                successDegrees: 0,
                successPixels: 0,
                hasSweep: true)
        };
    }

    public static VirgilMotionProfile Boot(bool compact)
    {
        return Create(
            VirgilCoreState.Idle,
            compact,
            outerDegrees: 160,
            outerSeconds: 1.55,
            innerDegrees: -120,
            innerSeconds: 1.55,
            segmentDegrees: 80,
            segmentSeconds: 1.55,
            fragmentPixels: compact ? 0 : 7,
            fragmentSeconds: 1.55,
            communicationDegrees: 0,
            communicationPixels: 0,
            successDegrees: 0,
            successPixels: 0,
            hasSweep: true,
            linear: false);
    }

    private static VirgilMotionProfile Create(
        VirgilCoreState state,
        bool compact,
        double outerSeconds,
        double innerSeconds,
        double segmentSeconds,
        double fragmentPixels,
        double fragmentSeconds,
        double communicationDegrees,
        double communicationPixels,
        double successDegrees,
        double successPixels,
        bool hasSweep,
        double outerDegrees = 360,
        double innerDegrees = -360,
        double segmentDegrees = 360,
        bool linear = true)
    {
        return new VirgilMotionProfile(
            state,
            compact,
            outerDegrees,
            outerSeconds,
            innerDegrees,
            innerSeconds,
            segmentDegrees,
            segmentSeconds,
            fragmentPixels,
            fragmentSeconds,
            communicationDegrees,
            communicationPixels,
            successDegrees,
            successPixels,
            linear,
            hasSweep,
            HasPhysicalMotion(
                outerDegrees,
                innerDegrees,
                segmentDegrees,
                fragmentPixels,
                communicationDegrees,
                communicationPixels,
                successDegrees,
                successPixels,
                hasSweep));
    }

    private static bool HasPhysicalMotion(
        double outerDegrees,
        double innerDegrees,
        double segmentDegrees,
        double fragmentPixels,
        double communicationDegrees,
        double communicationPixels,
        double successDegrees,
        double successPixels,
        bool hasSweep)
    {
        return Math.Abs(outerDegrees) > 0 ||
            Math.Abs(innerDegrees) > 0 ||
            Math.Abs(segmentDegrees) > 0 ||
            fragmentPixels > 0 ||
            Math.Abs(communicationDegrees) > 0 ||
            communicationPixels > 0 ||
            Math.Abs(successDegrees) > 0 ||
            successPixels > 0 ||
            hasSweep;
    }
}

public sealed class VirgilCoreAnimationController
{
    public VirgilCoreState State { get; private set; } = VirgilCoreState.Idle;

    public bool IsPermanentAnimationRunning { get; private set; }

    public int PermanentAnimationStarts { get; private set; }

    public int AnimationStops { get; private set; }

    public int CommunicationPulses { get; private set; }

    public VirgilCoreState LastCommunicationBaseState { get; private set; } = VirgilCoreState.Idle;

    public VirgilCoreAnimationPlan SetState(VirgilCoreState requestedState, bool canAnimate)
    {
        if (requestedState == VirgilCoreState.Communicating)
        {
            return PulseCommunication(canAnimate);
        }

        var previousState = State;
        var wasRunning = IsPermanentAnimationRunning;

        if (IsTransient(requestedState) && !canAnimate)
        {
            State = VirgilCoreState.Idle;
            StopPermanentIfNeeded(wasRunning);
            return new VirgilCoreAnimationPlan(State, wasRunning, ApplyStatic: true);
        }

        State = requestedState;

        if (!canAnimate)
        {
            IsPermanentAnimationRunning = false;
            StopPermanentIfNeeded(wasRunning);
            return new VirgilCoreAnimationPlan(State, wasRunning, ApplyStatic: true);
        }

        if (IsTransient(requestedState))
        {
            IsPermanentAnimationRunning = false;
            StopPermanentIfNeeded(wasRunning);
            return new VirgilCoreAnimationPlan(requestedState, wasRunning, StartTransient: true);
        }

        if (previousState == requestedState && wasRunning)
        {
            return VirgilCoreAnimationPlan.None(State);
        }

        IsPermanentAnimationRunning = true;
        PermanentAnimationStarts++;
        return new VirgilCoreAnimationPlan(State, wasRunning, StartPermanent: true);
    }

    public VirgilCoreAnimationPlan CompleteTransient(VirgilCoreState transientState, bool canAnimate)
    {
        if (!IsTransient(transientState) || State != transientState)
        {
            return VirgilCoreAnimationPlan.None(State);
        }

        State = VirgilCoreState.Idle;

        if (!canAnimate)
        {
            IsPermanentAnimationRunning = false;
            return new VirgilCoreAnimationPlan(State, ApplyStatic: true);
        }

        IsPermanentAnimationRunning = true;
        PermanentAnimationStarts++;
        return new VirgilCoreAnimationPlan(State, StartPermanent: true);
    }

    public VirgilCoreAnimationPlan PulseCommunication(bool canAnimate)
    {
        LastCommunicationBaseState = State;

        if (!canAnimate)
        {
            return VirgilCoreAnimationPlan.None(State);
        }

        CommunicationPulses++;
        return new VirgilCoreAnimationPlan(State, StartCommunication: true);
    }

    public VirgilCoreAnimationPlan SetHostState(
        bool isLoaded,
        bool isVisible,
        bool animationsAllowed)
    {
        var canAnimate = CanAnimate(isLoaded, isVisible, animationsAllowed);
        var wasRunning = IsPermanentAnimationRunning;

        if (IsTransient(State))
        {
            State = VirgilCoreState.Idle;
        }

        if (!canAnimate)
        {
            IsPermanentAnimationRunning = false;
            StopPermanentIfNeeded(wasRunning);
            return new VirgilCoreAnimationPlan(State, wasRunning, ApplyStatic: true);
        }

        if (IsTransient(State) || wasRunning)
        {
            return VirgilCoreAnimationPlan.None(State);
        }

        IsPermanentAnimationRunning = true;
        PermanentAnimationStarts++;
        return new VirgilCoreAnimationPlan(State, StartPermanent: true);
    }

    public static bool CanAnimate(
        bool isLoaded,
        bool isVisible,
        bool animationsAllowed)
    {
        return isLoaded && isVisible && animationsAllowed;
    }

    private static bool IsTransient(VirgilCoreState state)
    {
        return state is VirgilCoreState.Success or VirgilCoreState.Error;
    }

    private void StopPermanentIfNeeded(bool wasRunning)
    {
        if (wasRunning)
        {
            AnimationStops++;
        }
    }
}
