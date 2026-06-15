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

    private readonly VirgilCoreAnimationController _animationController = new();
    private Storyboard? _permanentStoryboard;
    private Storyboard? _transientStoryboard;
    private Storyboard? _communicationStoryboard;
    private EventHandler? _transientCompletedHandler;
    private EventHandler? _communicationCompletedHandler;
    private VirgilCoreState? _permanentStoryboardState;
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
        ApplyAnimationPlan(_animationController.SetHostState(_isLoaded, IsVisible, UserAnimationsEnabled()));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        ApplyAnimationPlan(_animationController.SetHostState(false, false, UserAnimationsEnabled()));
        StopAllStoryboards();
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
        if (plan == null)
        {
            return;
        }

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

        StopPermanentStoryboard();
        StopTransientStoryboard();
        ResetVisuals();
        ApplyStaticState(state);

        var storyboard = new Storyboard();

        switch (state)
        {
            case VirgilCoreState.Scanning:
                BuildScanning(storyboard);
                break;
            case VirgilCoreState.Warning:
                BuildWarning(storyboard);
                break;
            case VirgilCoreState.SensitiveAction:
                BuildSensitiveAction(storyboard);
                break;
            case VirgilCoreState.Executing:
                BuildExecuting(storyboard);
                break;
            default:
                BuildIdle(storyboard);
                break;
        }

        _permanentStoryboard = storyboard;
        _permanentStoryboardState = state;
        storyboard.Begin(this, true);
    }

    private void StartTransientState(VirgilCoreState state)
    {
        StopTransientStoryboard();
        StopPermanentStoryboard();
        ResetVisuals();
        ApplyStaticState(state);

        var storyboard = new Storyboard();

        if (state == VirgilCoreState.Error)
        {
            BuildError(storyboard);
        }
        else
        {
            BuildSuccess(storyboard);
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

    private void StartCommunicationPulse()
    {
        if (!CanAnimate())
        {
            return;
        }

        StopCommunicationStoryboard();

        CommunicationFragments.Opacity = 0;
        CommunicationFlash.Opacity = 0;
        CommunicationWave.Opacity = 0;
        CommunicationFlashScale.ScaleX = 0.62;
        CommunicationFlashScale.ScaleY = 0.62;
        CommunicationWaveScale.ScaleX = 0.42;
        CommunicationWaveScale.ScaleY = 0.42;

        var storyboard = new Storyboard();
        AddDouble(storyboard, CommunicationFlash, UIElement.OpacityProperty, 0.0, 0.58, 0.16, false, null, 0.00);
        AddDouble(storyboard, CommunicationFlash, UIElement.OpacityProperty, 0.58, 0.0, 0.34, false, null, 0.16);
        AddDouble(storyboard, CommunicationFlashScale, ScaleTransform.ScaleXProperty, 0.62, 1.05, 0.48);
        AddDouble(storyboard, CommunicationFlashScale, ScaleTransform.ScaleYProperty, 0.62, 1.05, 0.48);
        AddDouble(storyboard, CommunicationWave, UIElement.OpacityProperty, 0.48, 0.0, 0.56);
        AddDouble(storyboard, CommunicationWaveScale, ScaleTransform.ScaleXProperty, 0.42, 1.72, 0.56);
        AddDouble(storyboard, CommunicationWaveScale, ScaleTransform.ScaleYProperty, 0.42, 1.72, 0.56);
        AddDouble(storyboard, CommunicationFragments, UIElement.OpacityProperty, 0.0, 0.92, 0.16, false, null, 0.06);
        AddDouble(storyboard, CommunicationFragments, UIElement.OpacityProperty, 0.92, 0.0, 0.28, false, null, 0.28);

        EventHandler completed = (_, _) =>
        {
            DetachCommunicationHandler();
            CommunicationFragments.Opacity = 0;
            CommunicationFlash.Opacity = 0;
            CommunicationWave.Opacity = 0;
        };

        _communicationCompletedHandler = completed;
        storyboard.Completed += completed;
        _communicationStoryboard = storyboard;
        storyboard.Begin(this, true);
    }

    private void BuildIdle(Storyboard storyboard)
    {
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.28, 0.48, 3.1, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleXProperty, 0.96, 1.04, 3.1, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleYProperty, 0.96, 1.04, 3.1, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterHalo, UIElement.OpacityProperty, 0.14, 0.25, 3.4, true, RepeatBehavior.Forever);
        AddDouble(storyboard, HaloScale, ScaleTransform.ScaleXProperty, 0.98, 1.035, 3.4, true, RepeatBehavior.Forever);
        AddDouble(storyboard, HaloScale, ScaleTransform.ScaleYProperty, 0.98, 1.035, 3.4, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterGuideRingRotate, RotateTransform.AngleProperty, 0, 360, 36, false, RepeatBehavior.Forever);
        AddDouble(storyboard, InnerGuideRingRotate, RotateTransform.AngleProperty, 0, -360, 28, false, RepeatBehavior.Forever);
        AddFragmentIdle(storyboard);
        AddTechnicalSweep(storyboard, 7.2);

        if (!IsCompact())
        {
            AddAmbientActivity(storyboard);
        }
    }

    private void BuildScanning(Storyboard storyboard)
    {
        AddDouble(storyboard, SegmentRingRotate, RotateTransform.AngleProperty, 0, 360, 7.2, false, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterGuideRingRotate, RotateTransform.AngleProperty, 0, 360, 8.4, false, RepeatBehavior.Forever);
        AddDouble(storyboard, InnerGuideRingRotate, RotateTransform.AngleProperty, 0, -360, 4.6, false, RepeatBehavior.Forever);
        AddDouble(storyboard, ScanLine, UIElement.OpacityProperty, 0.14, 0.78, 1.2, true, RepeatBehavior.Forever);
        AddDouble(storyboard, ScanLineTransform, TranslateTransform.YProperty, -58, 58, 2.4, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.42, 0.72, 1.25, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleXProperty, 0.98, 1.06, 1.25, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleYProperty, 0.98, 1.06, 1.25, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterHalo, UIElement.OpacityProperty, 0.22, 0.42, 1.4, true, RepeatBehavior.Forever);
        AddDouble(storyboard, HaloScale, ScaleTransform.ScaleXProperty, 1.0, 1.045, 1.4, true, RepeatBehavior.Forever);
        AddDouble(storyboard, HaloScale, ScaleTransform.ScaleYProperty, 1.0, 1.045, 1.4, true, RepeatBehavior.Forever);
        AddFragmentScan(storyboard);
        AddTechnicalSweep(storyboard, 1.8);
    }

    private void BuildWarning(Storyboard storyboard)
    {
        AddDouble(storyboard, OuterGuideRingRotate, RotateTransform.AngleProperty, 0, 360, 18, false, RepeatBehavior.Forever);
        AddDouble(storyboard, InnerGuideRingRotate, RotateTransform.AngleProperty, 0, -360, 22, false, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.52, 0.82, 1.7, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleXProperty, 0.99, 1.045, 1.7, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleYProperty, 0.99, 1.045, 1.7, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterHalo, UIElement.OpacityProperty, 0.24, 0.4, 1.7, true, RepeatBehavior.Forever);
        AddDouble(storyboard, StatusFlash, UIElement.OpacityProperty, 0.06, 0.22, 1.9, true, RepeatBehavior.Forever);
        AddDouble(storyboard, SegmentA, UIElement.OpacityProperty, 0.62, 1.0, 1.8, true, RepeatBehavior.Forever, 0.0);
        AddDouble(storyboard, SegmentB, UIElement.OpacityProperty, 0.54, 0.92, 1.8, true, RepeatBehavior.Forever, 0.32);
        AddDouble(storyboard, SegmentC, UIElement.OpacityProperty, 0.58, 0.96, 1.8, true, RepeatBehavior.Forever, 0.64);
        AddDouble(storyboard, SegmentD, UIElement.OpacityProperty, 0.5, 0.86, 1.8, true, RepeatBehavior.Forever, 0.96);
    }

    private void BuildSensitiveAction(Storyboard storyboard)
    {
        AddDouble(storyboard, OuterGuideRingRotate, RotateTransform.AngleProperty, 0, 24, 18, true, RepeatBehavior.Forever);
        AddDouble(storyboard, InnerGuideRingRotate, RotateTransform.AngleProperty, 0, -18, 20, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.48, 0.72, 2.1, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleXProperty, 0.98, 1.035, 2.1, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleYProperty, 0.98, 1.035, 2.1, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterHalo, UIElement.OpacityProperty, 0.18, 0.34, 2.1, true, RepeatBehavior.Forever);
        AddDouble(storyboard, StatusFlash, UIElement.OpacityProperty, 0.05, 0.18, 2.1, true, RepeatBehavior.Forever);
    }

    private void BuildExecuting(Storyboard storyboard)
    {
        AddDouble(storyboard, SegmentRingRotate, RotateTransform.AngleProperty, 0, 360, 5.6, false, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterGuideRingRotate, RotateTransform.AngleProperty, 0, 360, 7.0, false, RepeatBehavior.Forever);
        AddDouble(storyboard, InnerGuideRingRotate, RotateTransform.AngleProperty, 0, -360, 3.8, false, RepeatBehavior.Forever);
        AddDouble(storyboard, ScanLine, UIElement.OpacityProperty, 0.18, 0.84, 1.0, true, RepeatBehavior.Forever);
        AddDouble(storyboard, ScanLineTransform, TranslateTransform.YProperty, -62, 62, 2.0, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.46, 0.76, 1.0, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterHalo, UIElement.OpacityProperty, 0.24, 0.44, 1.25, true, RepeatBehavior.Forever);
        AddFragmentScan(storyboard);
        AddTechnicalSweep(storyboard, 1.4);
    }

    private void BuildSuccess(Storyboard storyboard)
    {
        AddDouble(storyboard, SegmentRingRotate, RotateTransform.AngleProperty, 0, 80, 1.15);
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.72, 0.36, 1.15);
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleXProperty, 1.14, 1.0, 1.15);
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleYProperty, 1.14, 1.0, 1.15);
        AddDouble(storyboard, StatusFlash, UIElement.OpacityProperty, 0.78, 0.0, 1.15);
        AddDouble(storyboard, StatusFlashScale, ScaleTransform.ScaleXProperty, 0.64, 1.08, 1.15);
        AddDouble(storyboard, StatusFlashScale, ScaleTransform.ScaleYProperty, 0.64, 1.08, 1.15);
        AddFragmentFlash(storyboard, 0.0);
    }

    private void BuildError(Storyboard storyboard)
    {
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleXProperty, 1.0, 0.88, 0.22, true, new RepeatBehavior(2));
        AddDouble(storyboard, CenterGlowScale, ScaleTransform.ScaleYProperty, 1.0, 0.88, 0.22, true, new RepeatBehavior(2));
        AddDouble(storyboard, CenterGlow, UIElement.OpacityProperty, 0.72, 0.38, 1.1);
        AddDouble(storyboard, StatusFlash, UIElement.OpacityProperty, 0.72, 0.0, 1.1);
        AddDouble(storyboard, StatusFlashScale, ScaleTransform.ScaleXProperty, 0.92, 1.04, 1.1);
        AddDouble(storyboard, StatusFlashScale, ScaleTransform.ScaleYProperty, 0.92, 1.04, 1.1);
        AddDouble(storyboard, SegmentRingRotate, RotateTransform.AngleProperty, -8, 0, 0.62);
    }

    private void AddFragmentIdle(Storyboard storyboard)
    {
        AddFragmentPulse(storyboard, FragmentTop, FragmentTopTransform, 0, -2, 3.9, 0.0);
        AddFragmentPulse(storyboard, FragmentRight, FragmentRightTransform, 2, 0, 4.6, 0.7);
        AddFragmentPulse(storyboard, FragmentBottom, FragmentBottomTransform, 0, 2, 4.2, 1.3);
        AddFragmentPulse(storyboard, FragmentLeft, FragmentLeftTransform, -2, 0, 5.0, 1.9);
    }

    private void AddFragmentScan(Storyboard storyboard)
    {
        AddDouble(storyboard, FragmentTop, UIElement.OpacityProperty, 0.45, 0.94, 0.45, true, RepeatBehavior.Forever, 0.0);
        AddDouble(storyboard, FragmentRight, UIElement.OpacityProperty, 0.45, 0.94, 0.45, true, RepeatBehavior.Forever, 0.28);
        AddDouble(storyboard, FragmentBottom, UIElement.OpacityProperty, 0.45, 0.94, 0.45, true, RepeatBehavior.Forever, 0.56);
        AddDouble(storyboard, FragmentLeft, UIElement.OpacityProperty, 0.45, 0.94, 0.45, true, RepeatBehavior.Forever, 0.84);
    }

    private void AddFragmentPulse(
        Storyboard storyboard,
        UIElement fragment,
        TranslateTransform transform,
        double offsetX,
        double offsetY,
        double seconds,
        double beginSeconds)
    {
        AddDouble(storyboard, fragment, UIElement.OpacityProperty, 0.45, 0.9, seconds, true, RepeatBehavior.Forever, beginSeconds);
        AddDouble(storyboard, transform, TranslateTransform.XProperty, 0, offsetX, seconds, true, RepeatBehavior.Forever, beginSeconds);
        AddDouble(storyboard, transform, TranslateTransform.YProperty, 0, offsetY, seconds, true, RepeatBehavior.Forever, beginSeconds);
    }

    private void AddFragmentFlash(Storyboard storyboard, double beginSeconds)
    {
        AddDouble(storyboard, CommunicationFragments, UIElement.OpacityProperty, 0.0, 1.0, 0.18, false, null, beginSeconds);
        AddDouble(storyboard, CommunicationFragments, UIElement.OpacityProperty, 1.0, 0.0, 0.48, false, null, beginSeconds + 0.42);
    }

    private void AddTechnicalSweep(Storyboard storyboard, double cycleSeconds)
    {
        AddDouble(storyboard, VerticalMarks, UIElement.OpacityProperty, 0.42, 1.0, cycleSeconds / 4, true, RepeatBehavior.Forever, 0.0);
        AddDouble(storyboard, HorizontalMarks, UIElement.OpacityProperty, 0.42, 1.0, cycleSeconds / 4, true, RepeatBehavior.Forever, cycleSeconds / 3);
        AddDouble(storyboard, CornerMarks, UIElement.OpacityProperty, 0.44, 0.94, cycleSeconds / 4, true, RepeatBehavior.Forever, cycleSeconds * 0.62);
    }

    private void AddAmbientActivity(Storyboard storyboard)
    {
        AddCyclicPulse(storyboard, AmbientWave, UIElement.OpacityProperty, 0, 0.46, 0, 8.4, 6.2, 6.58, 7.04);
        AddCyclicPulse(storyboard, AmbientWaveScale, ScaleTransform.ScaleXProperty, 0.42, 1.65, 0.42, 8.4, 6.2, 6.58, 7.04);
        AddCyclicPulse(storyboard, AmbientWaveScale, ScaleTransform.ScaleYProperty, 0.42, 1.65, 0.42, 8.4, 6.2, 6.58, 7.04);
        AddCyclicPulse(storyboard, AmbientSegments, UIElement.OpacityProperty, 0, 0.62, 0, 8.4, 6.42, 6.76, 7.12);
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
        SegmentRingRotate.Angle = 0;
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
        FragmentTopTransform.X = 0;
        FragmentTopTransform.Y = 0;
        FragmentRightTransform.X = 0;
        FragmentRightTransform.Y = 0;
        FragmentBottomTransform.X = 0;
        FragmentBottomTransform.Y = 0;
        FragmentLeftTransform.X = 0;
        FragmentLeftTransform.Y = 0;
        ScanLine.Opacity = 0;
        AmbientWave.Opacity = 0;
        AmbientSegments.Opacity = 0;
        CommunicationFragments.Opacity = 0;
        CommunicationFlash.Opacity = 0;
        CommunicationWave.Opacity = 0;
        StatusFlash.Opacity = 0;
        FragmentTop.Opacity = 0.68;
        FragmentRight.Opacity = 0.62;
        FragmentBottom.Opacity = 0.58;
        FragmentLeft.Opacity = 0.64;
        SegmentA.Opacity = 1;
        SegmentB.Opacity = 1;
        SegmentC.Opacity = 1;
        SegmentD.Opacity = 1;
        VerticalMarks.Opacity = 1;
        HorizontalMarks.Opacity = 1;
        CornerMarks.Opacity = 1;
        CenterGlow.Fill = FindBrush("App.AccentBrush");
        CenterDot.Fill = FindBrush("App.AccentLightBrush");
        CenterGlow.Opacity = 0.34;
        OuterHalo.Opacity = 0.2;
        StatusFlash.Stroke = FindBrush("App.AccentLightBrush");
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

    private bool CanAnimate()
    {
        return VirgilCoreAnimationController.CanAnimate(_isLoaded, IsVisible, UserAnimationsEnabled());
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
        CommunicationFragments.Opacity = 0;
        CommunicationFlash.Opacity = 0;
        CommunicationWave.Opacity = 0;
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

    private Brush FindBrush(string key)
    {
        return TryFindResource(key) as Brush ?? Brushes.Orange;
    }
}
