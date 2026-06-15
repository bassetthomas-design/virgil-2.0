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
