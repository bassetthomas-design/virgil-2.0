using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Virgil.Domain;

namespace Virgil.App.Controls;

public enum VirgilCoreAnimationDetailLevel
{
    Auto,
    Full,
    Compact
}

public partial class VirgilCoreControl : UserControl
{
    public static readonly DependencyProperty AnimationDetailLevelProperty =
        DependencyProperty.Register(
            nameof(AnimationDetailLevel),
            typeof(VirgilCoreAnimationDetailLevel),
            typeof(VirgilCoreControl),
            new PropertyMetadata(VirgilCoreAnimationDetailLevel.Auto));

    public static readonly DependencyProperty PlayBootSequenceProperty =
        DependencyProperty.Register(
            nameof(PlayBootSequence),
            typeof(bool),
            typeof(VirgilCoreControl),
            new PropertyMetadata(false));

    private readonly VirgilCoreAnimationController _animationController = new();
    private Storyboard? _permanentStoryboard;
    private Storyboard? _transientStoryboard;
    private Storyboard? _communicationStoryboard;
    private Storyboard? _bootStoryboard;
    private EventHandler? _transientCompletedHandler;
    private EventHandler? _communicationCompletedHandler;
    private EventHandler? _bootCompletedHandler;
    private VirgilCoreState? _permanentStoryboardState;
    private bool _bootSequencePlayed;
    private bool _isLoaded;

    public VirgilCoreControl()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public VirgilCoreState State => _animationController.State;

    public VirgilCoreAnimationDetailLevel AnimationDetailLevel
    {
        get => (VirgilCoreAnimationDetailLevel)GetValue(AnimationDetailLevelProperty);
        set => SetValue(AnimationDetailLevelProperty, value);
    }

    public bool PlayBootSequence
    {
        get => (bool)GetValue(PlayBootSequenceProperty);
        set => SetValue(PlayBootSequenceProperty, value);
    }

    public void SetState(VirgilCoreState state)
    {
        ApplyAnimationPlan(_animationController.SetState(state, CanAnimate()));
    }

    public void NotifyCommunication()
    {
        ApplyAnimationPlan(_animationController.PulseCommunication(CanAnimate()));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;

        if (ShouldPlayBootSequence())
        {
            _bootSequencePlayed = true;
            StartBootSequence();
            return;
        }

        ApplyAnimationPlan(_animationController.SetHostState(_isLoaded, IsVisible, UserAnimationsEnabled()));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        ApplyAnimationPlan(_animationController.SetHostState(false, false, UserAnimationsEnabled()));
        StopAllStoryboards();
        ClearMotionAnimations();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        ApplyAnimationPlan(_animationController.SetHostState(_isLoaded, IsVisible, UserAnimationsEnabled()));
    }

    private void ApplyAnimationPlan(VirgilCoreAnimationPlan plan)
    {
        if (plan.StopPermanent)
        {
            StopPermanentStoryboard();
        }

        if (plan.ApplyStatic)
        {
            StopAllStoryboards();
            ResetVisuals();
            ApplyStaticState(plan.RenderState);
            return;
        }

        if (plan.StartPermanent)
        {
            StartPermanentState(plan.RenderState);
        }

        if (plan.StartTransient)
        {
            StartTransientState(plan.RenderState);
        }

        if (plan.StartCommunication)
        {
            StartCommunicationPulse();
        }
    }

    private void StartPermanentState(VirgilCoreState state)
    {
        if (!CanAnimate())
        {
            ResetVisuals();
            ApplyStaticState(state);
            return;
        }

        if (_permanentStoryboardState == state && _permanentStoryboard is not null)
        {
            return;
        }

        StopBootStoryboard();
        StopPermanentStoryboard();
        StopTransientStoryboard();
        ResetVisuals();
        ApplyStaticState(state);

        var storyboard = new Storyboard();
        var profile = MotionProfile(state);

        switch (state)
        {
            case VirgilCoreState.Scanning:
                BuildScanning(storyboard, profile);
                break;
            case VirgilCoreState.Warning:
                BuildWarning(storyboard, profile);
                break;
            case VirgilCoreState.SensitiveAction:
                BuildSensitiveAction(storyboard, profile);
                break;
            case VirgilCoreState.Executing:
                BuildExecuting(storyboard, profile);
                break;
            default:
                BuildIdle(storyboard, profile);
                break;
        }

        _permanentStoryboard = storyboard;
        _permanentStoryboardState = state;
        storyboard.Begin(this, true);
    }

    private void StartTransientState(VirgilCoreState state)
    {
        StopBootStoryboard();
        StopTransientStoryboard();
        StopPermanentStoryboard();
        ResetVisuals();
        ApplyStaticState(state);

        var storyboard = new Storyboard();
        var profile = MotionProfile(state);

        if (state == VirgilCoreState.Error)
        {
            BuildError(storyboard, profile);
        }
        else
        {
            BuildSuccess(storyboard, profile);
        }

        EventHandler completed = (_, _) =>
        {
            DetachTransientHandler();
            var nextPlan = _animationController.CompleteTransient(state, CanAnimate());
            ApplyAnimationPlan(nextPlan);
        };

        _transientCompletedHandler = completed;
        storyboard.Completed += completed;
        _transientStoryboard = storyboard;
        storyboard.Begin(this, true);
    }

    private void StartBootSequence()
    {
        StopAllStoryboards();
        ResetVisuals();
        ApplyStaticState(VirgilCoreState.Idle);

        var storyboard = new Storyboard { FillBehavior = FillBehavior.Stop };
        var profile = VirgilMotionProfiles.Boot(IsCompact());

        CenterGlow.Opacity = 0.8;
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.82, 0.42, 1.55);
        AddDouble(storyboard, CommunicationWave, UIElement.OpacityProperty, 0.42, 0.0, 1.2, false, null, 0.22);
        AddDouble(storyboard, CommunicationWaveScale, ScaleTransform.ScaleXProperty, 0.42, 1.9, 1.2, false, null, 0.22);
        AddDouble(storyboard, CommunicationWaveScale, ScaleTransform.ScaleYProperty, 0.42, 1.9, 1.2, false, null, 0.22);
        AddDouble(storyboard, DataSweepAssembly, UIElement.OpacityProperty, 0.0, 0.7, 0.18, false, null, 0.28);
        AddDouble(storyboard, DataSweepAssembly, UIElement.OpacityProperty, 0.7, 0.0, 0.46, false, null, 0.92);

        AnimateRotate(OuterMotionAssemblyRotate, 0, profile.OuterRotationDegrees, profile.OuterRotationSeconds);
        AnimateRotate(InnerMotionAssemblyRotate, 0, profile.InnerRotationDegrees, profile.InnerRotationSeconds);
        AnimateRotate(SegmentRingRotate, 0, profile.SegmentRotationDegrees, profile.SegmentRotationSeconds);
        AnimateRotate(DataSweepAssemblyRotate, -65, 74, 0.95, beginSeconds: 0.3);
        AnimateFragmentBurst(profile.FragmentTranslationPixels, 0.72, beginSeconds: 0.16);

        EventHandler completed = (_, _) =>
        {
            DetachBootHandler();
            ResetVisuals();
            ApplyAnimationPlan(_animationController.SetHostState(_isLoaded, IsVisible, UserAnimationsEnabled()));
        };

        _bootCompletedHandler = completed;
        storyboard.Completed += completed;
        _bootStoryboard = storyboard;
        storyboard.Begin(this, true);
    }

    private void StartCommunicationPulse()
    {
        if (!CanAnimate())
        {
            return;
        }

        StopCommunicationStoryboard();
        ResetCommunicationMotion();

        var profile = VirgilMotionProfiles.For(VirgilCoreState.Communicating, IsCompact());
        var storyboard = new Storyboard { FillBehavior = FillBehavior.Stop };

        AddDouble(storyboard, CommunicationFlash, UIElement.OpacityProperty, 0.0, 0.58, 0.16);
        AddDouble(storyboard, CommunicationFlash, UIElement.OpacityProperty, 0.58, 0.0, 0.34, false, null, 0.16);
        AddDouble(storyboard, CommunicationWave, UIElement.OpacityProperty, 0.48, 0.0, 0.56);
        AddDouble(storyboard, CommunicationWaveScale, ScaleTransform.ScaleXProperty, 0.42, 1.72, 0.56);
        AddDouble(storyboard, CommunicationWaveScale, ScaleTransform.ScaleYProperty, 0.42, 1.72, 0.56);
        AddDouble(storyboard, CommunicationFragments, UIElement.OpacityProperty, 0.0, 0.92, 0.12, false, null, 0.04);
        AddDouble(storyboard, CommunicationFragments, UIElement.OpacityProperty, 0.92, 0.0, 0.28, false, null, 0.32);

        AnimateRotate(OuterMotionAssemblyKickRotate, 0, profile.CommunicationRotationDegrees, 0.56);
        AnimateFragmentBurst(profile.CommunicationTranslationPixels, 0.56);
        AnimateCommunicationFragments(profile.CommunicationTranslationPixels, 0.56);

        EventHandler completed = (_, _) =>
        {
            DetachCommunicationHandler();
            ResetCommunicationMotion();
        };

        _communicationCompletedHandler = completed;
        storyboard.Completed += completed;
        _communicationStoryboard = storyboard;
        storyboard.Begin(this, true);
    }

    private void BuildIdle(Storyboard storyboard, VirgilMotionProfile profile)
    {
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.35, 0.45, 3.1, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterHalo, UIElement.OpacityProperty, 0.16, 0.25, 3.4, true, RepeatBehavior.Forever);
        AddDouble(storyboard, VerticalMarks, UIElement.OpacityProperty, 0.72, 0.96, 4.0, true, RepeatBehavior.Forever);
        AddDouble(storyboard, HorizontalMarks, UIElement.OpacityProperty, 0.70, 0.94, 4.2, true, RepeatBehavior.Forever, 0.7);
        AddDouble(storyboard, CornerMarks, UIElement.OpacityProperty, 0.72, 0.94, 4.5, true, RepeatBehavior.Forever, 1.2);

        AddContinuousRotation(OuterMotionAssemblyRotate, 0, profile.OuterRotationDegrees, profile.OuterRotationSeconds);
        AddContinuousRotation(InnerMotionAssemblyRotate, 0, profile.InnerRotationDegrees, profile.InnerRotationSeconds);
        AddContinuousRotation(SegmentRingRotate, 0, profile.SegmentRotationDegrees, profile.SegmentRotationSeconds);
        AddContinuousRotation(OuterGuideRingRotate, 0, 360, 30);
        AddContinuousRotation(InnerGuideRingRotate, 0, -360, 24);
        AnimateScalePulse(CenterGlowScale, 0.97, 1.04, 3.1);
        AnimateScalePulse(HaloScale, 0.98, 1.035, 3.4);
        AnimateFragments(profile);
        AnimateIdleSweep();

        if (!IsCompact())
        {
            AddAmbientActivity(storyboard);
        }
    }

    private void BuildScanning(Storyboard storyboard, VirgilMotionProfile profile)
    {
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.48, 0.74, 1.05, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterHalo, UIElement.OpacityProperty, 0.28, 0.44, 1.25, true, RepeatBehavior.Forever);
        AddDouble(storyboard, ScanLine, UIElement.OpacityProperty, 0.22, 0.86, 0.68, true, RepeatBehavior.Forever);
        AddDouble(storyboard, SegmentA, UIElement.OpacityProperty, 0.62, 1.0, 0.72, true, RepeatBehavior.Forever, 0.0);
        AddDouble(storyboard, SegmentB, UIElement.OpacityProperty, 0.54, 0.96, 0.72, true, RepeatBehavior.Forever, 0.18);
        AddDouble(storyboard, SegmentC, UIElement.OpacityProperty, 0.58, 1.0, 0.72, true, RepeatBehavior.Forever, 0.36);
        AddDouble(storyboard, SegmentD, UIElement.OpacityProperty, 0.52, 0.92, 0.72, true, RepeatBehavior.Forever, 0.54);

        AddContinuousRotation(OuterMotionAssemblyRotate, 0, profile.OuterRotationDegrees, profile.OuterRotationSeconds);
        AddContinuousRotation(InnerMotionAssemblyRotate, 0, profile.InnerRotationDegrees, profile.InnerRotationSeconds);
        AddContinuousRotation(SegmentRingRotate, 0, profile.SegmentRotationDegrees, profile.SegmentRotationSeconds);
        AddContinuousRotation(OuterGuideRingRotate, 0, 360, 7.2);
        AddContinuousRotation(InnerGuideRingRotate, 0, -360, 3.8);
        AddContinuousRotation(DataSweepAssemblyRotate, 0, 360, 3.2);
        AnimateTranslation(ScanLineTransform, TranslateTransform.YProperty, -72, 72, 1.45, true, RepeatBehavior.Forever);
        AnimateFragments(profile);
    }

    private void BuildWarning(Storyboard storyboard, VirgilMotionProfile profile)
    {
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.56, 0.72, 1.8, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterHalo, UIElement.OpacityProperty, 0.28, 0.38, 2.2, true, RepeatBehavior.Forever);
        AddDouble(storyboard, StatusFlash, UIElement.OpacityProperty, 0.10, 0.18, 2.4, true, RepeatBehavior.Forever);

        AddContinuousRotation(OuterMotionAssemblyRotate, 0, profile.OuterRotationDegrees, profile.OuterRotationSeconds);
        AddContinuousRotation(InnerMotionAssemblyRotate, 0, profile.InnerRotationDegrees, profile.InnerRotationSeconds);
        AddContinuousRotation(SegmentRingRotate, 0, profile.SegmentRotationDegrees, profile.SegmentRotationSeconds);
        AnimateRotate(OuterMotionAssemblyKickRotate, -4, 4, 2.8, true, RepeatBehavior.Forever);
        AnimateRotate(InnerMotionAssemblyKickRotate, 3, -3, 2.6, true, RepeatBehavior.Forever);
        AnimateFragments(profile);
    }

    private void BuildSensitiveAction(Storyboard storyboard, VirgilMotionProfile profile)
    {
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.44, 0.64, 2.8, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterHalo, UIElement.OpacityProperty, 0.20, 0.30, 2.8, true, RepeatBehavior.Forever);
        AddDouble(storyboard, StatusFlash, UIElement.OpacityProperty, 0.05, 0.14, 2.8, true, RepeatBehavior.Forever);

        AnimateRotate(OuterMotionAssemblyKickRotate, -5, 5, profile.OuterRotationSeconds, true, RepeatBehavior.Forever);
        AddContinuousRotation(InnerMotionAssemblyRotate, 0, profile.InnerRotationDegrees, profile.InnerRotationSeconds);
        AnimateFragmentContraction(profile.FragmentTranslationPixels, profile.FragmentCycleSeconds);
    }

    private void BuildExecuting(Storyboard storyboard, VirgilMotionProfile profile)
    {
        BuildScanning(storyboard, profile);
        AddDouble(storyboard, DataSweepAssembly, UIElement.OpacityProperty, 0.18, 0.62, 0.8, true, RepeatBehavior.Forever);
    }

    private void BuildSuccess(Storyboard storyboard, VirgilMotionProfile profile)
    {
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.86, 0.42, 1.25);
        AddDouble(storyboard, StatusFlash, UIElement.OpacityProperty, 0.70, 0.0, 1.25);
        AddDouble(storyboard, StatusFlashScale, ScaleTransform.ScaleXProperty, 0.68, 1.16, 1.25);
        AddDouble(storyboard, StatusFlashScale, ScaleTransform.ScaleYProperty, 0.68, 1.16, 1.25);
        AddDouble(storyboard, CommunicationWave, UIElement.OpacityProperty, 0.42, 0.0, 0.9);
        AddDouble(storyboard, CommunicationWaveScale, ScaleTransform.ScaleXProperty, 0.54, 1.6, 0.9);
        AddDouble(storyboard, CommunicationWaveScale, ScaleTransform.ScaleYProperty, 0.54, 1.6, 0.9);

        AnimateRotate(OuterMotionAssemblyRotate, 0, profile.SuccessRotationDegrees, 0.85);
        AnimateRotate(InnerMotionAssemblyRotate, 0, -profile.SuccessRotationDegrees * 0.7, 0.85);
        AnimateRotate(SegmentRingRotate, 0, profile.SuccessRotationDegrees, 0.85);
        AnimateFragmentBurst(profile.SuccessTranslationPixels, 0.72);
        AnimateCommunicationFragments(profile.SuccessTranslationPixels, 0.72);
    }

    private void BuildError(Storyboard storyboard, VirgilMotionProfile profile)
    {
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.72, 0.38, 1.15);
        AddDouble(storyboard, StatusFlash, UIElement.OpacityProperty, 0.72, 0.0, 1.15);
        AddDouble(storyboard, StatusFlashScale, ScaleTransform.ScaleXProperty, 0.92, 1.04, 1.1);
        AddDouble(storyboard, StatusFlashScale, ScaleTransform.ScaleYProperty, 0.92, 1.04, 1.1);

        AnimateRotate(OuterMotionAssemblyRotate, 8, profile.OuterRotationDegrees, profile.OuterRotationSeconds, true, new RepeatBehavior(2));
        AnimateRotate(InnerMotionAssemblyRotate, -6, profile.InnerRotationDegrees, profile.InnerRotationSeconds, true, new RepeatBehavior(2));
        AnimateRotate(SegmentRingRotate, -10, 0, profile.SegmentRotationSeconds, true, new RepeatBehavior(2));
        AnimateScalePulse(CenterGlowScale, 0.88, 1.0, 0.22, new RepeatBehavior(2));
        AnimateFragmentContraction(profile.FragmentTranslationPixels, profile.FragmentCycleSeconds, new RepeatBehavior(2));
    }

    private void AddAmbientActivity(Storyboard storyboard)
    {
        AddCyclicPulse(storyboard, AmbientWave, UIElement.OpacityProperty, 0, 0.34, 0, 8.4, 6.2, 6.58, 7.04);
        AddCyclicPulse(storyboard, AmbientWaveScale, ScaleTransform.ScaleXProperty, 0.42, 1.65, 0.42, 8.4, 6.2, 6.58, 7.04);
        AddCyclicPulse(storyboard, AmbientWaveScale, ScaleTransform.ScaleYProperty, 0.42, 1.65, 0.42, 8.4, 6.2, 6.58, 7.04);
        AddCyclicPulse(storyboard, AmbientSegments, UIElement.OpacityProperty, 0, 0.46, 0, 8.4, 6.42, 6.76, 7.12);
    }

    private void AnimateFragments(VirgilMotionProfile profile)
    {
        var offset = profile.FragmentTranslationPixels;
        AddFragmentOpacityPulse(FragmentTop, profile);
        AddFragmentOpacityPulse(FragmentRight, profile);
        AddFragmentOpacityPulse(FragmentBottom, profile);
        AddFragmentOpacityPulse(FragmentLeft, profile);
        AnimateTranslation(FragmentTopTransform, TranslateTransform.YProperty, 0, -offset, profile.FragmentCycleSeconds, true, RepeatBehavior.Forever);
        AnimateTranslation(FragmentRightTransform, TranslateTransform.XProperty, 0, offset, profile.FragmentCycleSeconds + 0.24, true, RepeatBehavior.Forever, 0.32);
        AnimateTranslation(FragmentBottomTransform, TranslateTransform.YProperty, 0, offset, profile.FragmentCycleSeconds + 0.18, true, RepeatBehavior.Forever, 0.64);
        AnimateTranslation(FragmentLeftTransform, TranslateTransform.XProperty, 0, -offset, profile.FragmentCycleSeconds + 0.36, true, RepeatBehavior.Forever, 0.96);
    }

    private void AnimateFragmentBurst(double offset, double seconds, double beginSeconds = 0)
    {
        AnimateTranslation(FragmentTopTransform, TranslateTransform.YProperty, 0, -offset, seconds / 2, true, null, beginSeconds);
        AnimateTranslation(FragmentRightTransform, TranslateTransform.XProperty, 0, offset, seconds / 2, true, null, beginSeconds + 0.02);
        AnimateTranslation(FragmentBottomTransform, TranslateTransform.YProperty, 0, offset, seconds / 2, true, null, beginSeconds + 0.04);
        AnimateTranslation(FragmentLeftTransform, TranslateTransform.XProperty, 0, -offset, seconds / 2, true, null, beginSeconds + 0.06);
    }

    private void AnimateCommunicationFragments(double offset, double seconds)
    {
        CommunicationFragments.Opacity = 0.92;
        AnimateTranslation(CommunicationTopTransform, TranslateTransform.YProperty, 0, -offset, seconds / 2, true);
        AnimateTranslation(CommunicationRightTransform, TranslateTransform.XProperty, 0, offset, seconds / 2, true, beginSeconds: 0.02);
        AnimateTranslation(CommunicationBottomTransform, TranslateTransform.YProperty, 0, offset, seconds / 2, true, beginSeconds: 0.04);
        AnimateTranslation(CommunicationLeftTransform, TranslateTransform.XProperty, 0, -offset, seconds / 2, true, beginSeconds: 0.06);
    }

    private void AnimateFragmentContraction(
        double offset,
        double seconds,
        RepeatBehavior? repeatBehavior = null)
    {
        AnimateTranslation(FragmentTopTransform, TranslateTransform.YProperty, 0, offset * 0.45, seconds, true, repeatBehavior ?? RepeatBehavior.Forever);
        AnimateTranslation(FragmentRightTransform, TranslateTransform.XProperty, 0, -offset * 0.45, seconds, true, repeatBehavior ?? RepeatBehavior.Forever);
        AnimateTranslation(FragmentBottomTransform, TranslateTransform.YProperty, 0, -offset * 0.45, seconds, true, repeatBehavior ?? RepeatBehavior.Forever);
        AnimateTranslation(FragmentLeftTransform, TranslateTransform.XProperty, 0, offset * 0.45, seconds, true, repeatBehavior ?? RepeatBehavior.Forever);
    }

    private void AddFragmentOpacityPulse(UIElement fragment, VirgilMotionProfile profile)
    {
        var animation = new DoubleAnimation
        {
            From = 0.75,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(profile.FragmentCycleSeconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        fragment.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void AnimateIdleSweep()
    {
        var opacity = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(8.2),
            RepeatBehavior = RepeatBehavior.Forever
        };
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(5.8))));
        opacity.KeyFrames.Add(new SplineDoubleKeyFrame(0.56, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(6.08))));
        opacity.KeyFrames.Add(new SplineDoubleKeyFrame(0.18, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(7.02))));
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(7.32))));
        DataSweepAssembly.BeginAnimation(UIElement.OpacityProperty, opacity);

        AnimateKeyFrames(
            DataSweepAssemblyRotate,
            RotateTransform.AngleProperty,
            8.2,
            (-70, 0),
            (-70, 5.8),
            (72, 7.02),
            (72, 8.2));
        AnimateKeyFrames(
            DataSweepLineTransform,
            TranslateTransform.XProperty,
            8.2,
            (-28, 0),
            (-28, 5.8),
            (28, 7.02),
            (28, 8.2));
    }

    private void AddContinuousRotation(
        RotateTransform target,
        double from,
        double to,
        double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            RepeatBehavior = RepeatBehavior.Forever,
            AutoReverse = false,
            EasingFunction = null
        };

        target.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private void AnimateRotate(
        RotateTransform target,
        double from,
        double to,
        double seconds,
        bool autoReverse = false,
        RepeatBehavior? repeatBehavior = null,
        double beginSeconds = 0)
    {
        var animation = CreateMotionAnimation(from, to, seconds, autoReverse, repeatBehavior, beginSeconds);
        target.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private void AnimateTranslation(
        TranslateTransform target,
        DependencyProperty property,
        double from,
        double to,
        double seconds,
        bool autoReverse = false,
        RepeatBehavior? repeatBehavior = null,
        double beginSeconds = 0)
    {
        var animation = CreateMotionAnimation(from, to, seconds, autoReverse, repeatBehavior, beginSeconds);
        target.BeginAnimation(property, animation);
    }

    private void AnimateScalePulse(
        ScaleTransform target,
        double from,
        double to,
        double seconds,
        RepeatBehavior? repeatBehavior = null)
    {
        var xAnimation = CreateMotionAnimation(from, to, seconds, true, repeatBehavior ?? RepeatBehavior.Forever);
        var yAnimation = CreateMotionAnimation(from, to, seconds, true, repeatBehavior ?? RepeatBehavior.Forever);
        target.BeginAnimation(ScaleTransform.ScaleXProperty, xAnimation);
        target.BeginAnimation(ScaleTransform.ScaleYProperty, yAnimation);
    }

    private static DoubleAnimation CreateMotionAnimation(
        double from,
        double to,
        double seconds,
        bool autoReverse,
        RepeatBehavior? repeatBehavior = null,
        double beginSeconds = 0)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            AutoReverse = autoReverse,
            BeginTime = TimeSpan.FromSeconds(beginSeconds),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        if (repeatBehavior.HasValue)
        {
            animation.RepeatBehavior = repeatBehavior.Value;
        }

        return animation;
    }

    private static void AnimateKeyFrames(
        Animatable target,
        DependencyProperty property,
        double cycleSeconds,
        params (double Value, double Second)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(cycleSeconds),
            RepeatBehavior = RepeatBehavior.Forever
        };

        foreach (var frame in frames)
        {
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(
                frame.Value,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(frame.Second))));
        }

        target.BeginAnimation(property, animation);
    }

    private void AddDouble(
        Storyboard storyboard,
        DependencyObject target,
        DependencyProperty property,
        double from,
        double to,
        double seconds,
        bool autoReverse = false,
        RepeatBehavior? repeatBehavior = null,
        double beginSeconds = 0)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            AutoReverse = autoReverse,
            BeginTime = TimeSpan.FromSeconds(beginSeconds),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        if (repeatBehavior.HasValue)
        {
            animation.RepeatBehavior = repeatBehavior.Value;
        }

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private void AddCyclicPulse(
        Storyboard storyboard,
        DependencyObject target,
        DependencyProperty property,
        double idleValue,
        double peakValue,
        double endValue,
        double cycleSeconds,
        double riseSecond,
        double peakSecond,
        double fallSecond)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(cycleSeconds),
            RepeatBehavior = RepeatBehavior.Forever
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(idleValue, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(idleValue, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(riseSecond))));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(peakValue, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(peakSecond))));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(endValue, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(fallSecond))));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(endValue, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycleSeconds))));
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private void ApplyStaticState(VirgilCoreState state)
    {
        var isCompact = IsCompact();
        var accent = state == VirgilCoreState.Error ? "App.AlertBrush" : "App.AccentBrush";
        var light = state == VirgilCoreState.Error ? "App.AlertBrush" : "App.AccentLightBrush";

        CenterGlow.Fill = FindBrush(accent);
        StatusFlash.Stroke = FindBrush(light);
        CenterDot.Fill = FindBrush(light);

        switch (state)
        {
            case VirgilCoreState.Scanning:
            case VirgilCoreState.Executing:
                CenterGlow.Opacity = 0.62;
                OuterHalo.Opacity = isCompact ? 0.3 : 0.36;
                ScanLine.Opacity = isCompact ? 0.2 : 0.34;
                DataSweepAssembly.Opacity = isCompact ? 0.16 : 0.28;
                break;
            case VirgilCoreState.Warning:
            case VirgilCoreState.SensitiveAction:
                CenterGlow.Fill = FindBrush("App.AccentLightBrush");
                CenterGlow.Opacity = 0.62;
                OuterHalo.Opacity = 0.32;
                StatusFlash.Opacity = 0.16;
                break;
            case VirgilCoreState.Error:
                CenterGlow.Opacity = 0.7;
                OuterHalo.Opacity = 0.28;
                StatusFlash.Opacity = 0.2;
                break;
            case VirgilCoreState.Success:
                CenterGlow.Fill = FindBrush("App.AccentLightBrush");
                CenterGlow.Opacity = 0.72;
                OuterHalo.Opacity = 0.3;
                StatusFlash.Opacity = 0.22;
                break;
            default:
                CenterGlow.Opacity = 0.34;
                OuterHalo.Opacity = 0.2;
                break;
        }
    }

    private void ResetVisuals()
    {
        ClearMotionAnimations();
        SegmentRingRotate.Angle = 0;
        OuterMotionAssemblyRotate.Angle = 0;
        OuterMotionAssemblyKickRotate.Angle = 0;
        InnerMotionAssemblyRotate.Angle = 0;
        InnerMotionAssemblyKickRotate.Angle = 0;
        DataSweepAssemblyRotate.Angle = 0;
        OuterGuideRingRotate.Angle = 0;
        InnerGuideRingRotate.Angle = 0;
        HaloScale.ScaleX = 1;
        HaloScale.ScaleY = 1;
        CenterGlowScale.ScaleX = 1;
        CenterGlowScale.ScaleY = 1;
        StatusFlashScale.ScaleX = 0.82;
        StatusFlashScale.ScaleY = 0.82;
        CommunicationFlashScale.ScaleX = 0.62;
        CommunicationFlashScale.ScaleY = 0.62;
        CommunicationWaveScale.ScaleX = 0.42;
        CommunicationWaveScale.ScaleY = 0.42;
        AmbientWaveScale.ScaleX = 0.42;
        AmbientWaveScale.ScaleY = 0.42;
        ScanLineTransform.Y = -54;
        DataSweepLineTransform.X = -18;
        FragmentTopTransform.X = 0;
        FragmentTopTransform.Y = 0;
        FragmentRightTransform.X = 0;
        FragmentRightTransform.Y = 0;
        FragmentBottomTransform.X = 0;
        FragmentBottomTransform.Y = 0;
        FragmentLeftTransform.X = 0;
        FragmentLeftTransform.Y = 0;
        ResetCommunicationMotion();
        ScanLine.Opacity = 0;
        DataSweepAssembly.Opacity = 0;
        AmbientWave.Opacity = 0;
        AmbientSegments.Opacity = 0;
        CommunicationFragments.Opacity = 0;
        CommunicationFlash.Opacity = 0;
        CommunicationWave.Opacity = 0;
        StatusFlash.Opacity = 0;
        FragmentTop.Opacity = 0.78;
        FragmentRight.Opacity = 0.78;
        FragmentBottom.Opacity = 0.78;
        FragmentLeft.Opacity = 0.78;
        SegmentA.Opacity = 1;
        SegmentB.Opacity = 1;
        SegmentC.Opacity = 1;
        SegmentD.Opacity = 1;
        VerticalMarks.Opacity = 1;
        HorizontalMarks.Opacity = 1;
        CornerMarks.Opacity = 1;
        OuterMotionAssembly.Opacity = 1;
        InnerMotionAssembly.Opacity = 0.95;
        CenterGlow.Fill = FindBrush("App.AccentBrush");
        CenterDot.Fill = FindBrush("App.AccentLightBrush");
        CenterGlow.Opacity = 0.34;
        OuterHalo.Opacity = 0.2;
        StatusFlash.Stroke = FindBrush("App.AccentLightBrush");
    }

    private void ResetCommunicationMotion()
    {
        OuterMotionAssemblyKickRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        OuterMotionAssemblyKickRotate.Angle = 0;
        CommunicationTopTransform.BeginAnimation(TranslateTransform.YProperty, null);
        CommunicationRightTransform.BeginAnimation(TranslateTransform.XProperty, null);
        CommunicationBottomTransform.BeginAnimation(TranslateTransform.YProperty, null);
        CommunicationLeftTransform.BeginAnimation(TranslateTransform.XProperty, null);
        CommunicationTopTransform.Y = 0;
        CommunicationRightTransform.X = 0;
        CommunicationBottomTransform.Y = 0;
        CommunicationLeftTransform.X = 0;
        CommunicationFragments.Opacity = 0;
        CommunicationFlash.Opacity = 0;
        CommunicationWave.Opacity = 0;
    }

    private void ClearMotionAnimations()
    {
        ClearRotate(OuterMotionAssemblyRotate);
        ClearRotate(OuterMotionAssemblyKickRotate);
        ClearRotate(InnerMotionAssemblyRotate);
        ClearRotate(InnerMotionAssemblyKickRotate);
        ClearRotate(SegmentRingRotate);
        ClearRotate(OuterGuideRingRotate);
        ClearRotate(InnerGuideRingRotate);
        ClearRotate(DataSweepAssemblyRotate);
        ClearTranslate(FragmentTopTransform);
        ClearTranslate(FragmentRightTransform);
        ClearTranslate(FragmentBottomTransform);
        ClearTranslate(FragmentLeftTransform);
        ClearTranslate(CommunicationTopTransform);
        ClearTranslate(CommunicationRightTransform);
        ClearTranslate(CommunicationBottomTransform);
        ClearTranslate(CommunicationLeftTransform);
        ClearTranslate(ScanLineTransform);
        ClearTranslate(DataSweepLineTransform);
        ClearScale(CenterGlowScale);
        ClearScale(HaloScale);
        ClearScale(CommunicationWaveScale);
        FragmentTop.BeginAnimation(UIElement.OpacityProperty, null);
        FragmentRight.BeginAnimation(UIElement.OpacityProperty, null);
        FragmentBottom.BeginAnimation(UIElement.OpacityProperty, null);
        FragmentLeft.BeginAnimation(UIElement.OpacityProperty, null);
        DataSweepAssembly.BeginAnimation(UIElement.OpacityProperty, null);
    }

    private static void ClearRotate(RotateTransform transform)
    {
        transform.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private static void ClearTranslate(TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private static void ClearScale(ScaleTransform transform)
    {
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    private bool ShouldPlayBootSequence()
    {
        return PlayBootSequence &&
            !_bootSequencePlayed &&
            !IsCompact() &&
            CanAnimate();
    }

    private bool CanAnimate()
    {
        return VirgilCoreAnimationController.CanAnimate(_isLoaded, IsVisible, UserAnimationsEnabled());
    }

    private VirgilMotionProfile MotionProfile(VirgilCoreState state)
    {
        return VirgilMotionProfiles.For(state, IsCompact());
    }

    private static bool UserAnimationsEnabled()
    {
        return SystemParameters.ClientAreaAnimation;
    }

    private bool IsCompact()
    {
        return AnimationDetailLevel == VirgilCoreAnimationDetailLevel.Compact ||
            (AnimationDetailLevel == VirgilCoreAnimationDetailLevel.Auto && ActualWidth > 0 && ActualWidth < 96);
    }

    private void StopAllStoryboards()
    {
        StopBootStoryboard();
        StopCommunicationStoryboard();
        StopTransientStoryboard();
        StopPermanentStoryboard();
    }

    private void StopPermanentStoryboard()
    {
        if (_permanentStoryboard is null)
        {
            return;
        }

        _permanentStoryboard.Stop(this);
        _permanentStoryboard.Remove(this);
        _permanentStoryboard = null;
        _permanentStoryboardState = null;
    }

    private void StopTransientStoryboard()
    {
        if (_transientStoryboard is null)
        {
            return;
        }

        DetachTransientHandler();
        _transientStoryboard.Stop(this);
        _transientStoryboard.Remove(this);
        _transientStoryboard = null;
    }

    private void StopCommunicationStoryboard()
    {
        if (_communicationStoryboard is null)
        {
            return;
        }

        DetachCommunicationHandler();
        _communicationStoryboard.Stop(this);
        _communicationStoryboard.Remove(this);
        _communicationStoryboard = null;
        ResetCommunicationMotion();
    }

    private void StopBootStoryboard()
    {
        if (_bootStoryboard is null)
        {
            return;
        }

        DetachBootHandler();
        _bootStoryboard.Stop(this);
        _bootStoryboard.Remove(this);
        _bootStoryboard = null;
    }

    private void DetachTransientHandler()
    {
        if (_transientStoryboard is not null && _transientCompletedHandler is not null)
        {
            _transientStoryboard.Completed -= _transientCompletedHandler;
        }

        _transientCompletedHandler = null;
    }

    private void DetachCommunicationHandler()
    {
        if (_communicationStoryboard is not null && _communicationCompletedHandler is not null)
        {
            _communicationStoryboard.Completed -= _communicationCompletedHandler;
        }

        _communicationCompletedHandler = null;
    }

    private void DetachBootHandler()
    {
        if (_bootStoryboard is not null && _bootCompletedHandler is not null)
        {
            _bootStoryboard.Completed -= _bootCompletedHandler;
        }

        _bootCompletedHandler = null;
    }

    private Brush FindBrush(string key)
    {
        return TryFindResource(key) as Brush ?? Brushes.Orange;
    }
}
