using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Virgil.App.Animation;

namespace Virgil.App.Controls;

public enum VirgilCoreAnimationDetailLevel { Auto, Full, Compact }

public partial class VirgilCoreControl : UserControl
{
    public static readonly DependencyProperty AnimationDetailLevelProperty = DependencyProperty.Register(
        nameof(AnimationDetailLevel), typeof(VirgilCoreAnimationDetailLevel), typeof(VirgilCoreControl),
        new PropertyMetadata(VirgilCoreAnimationDetailLevel.Auto, OnDetailChanged));
    public static readonly DependencyProperty PlayBootSequenceProperty = DependencyProperty.Register(
        nameof(PlayBootSequence), typeof(bool), typeof(VirgilCoreControl), new PropertyMetadata(false));
    private bool _isLoaded;
    private bool _animating;
    private VirgilCoreState? _renderedState;
    private int _generation;

    public VirgilCoreControl()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => RefreshFace(UserAnimationsEnabled());
        SizeChanged += (_, _) => RefreshFace(UserAnimationsEnabled());
        RefreshFace(UserAnimationsEnabled());
    }
    public VirgilCoreState State { get; private set; } = VirgilCoreState.Idle;
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
        State = state;
        RefreshFace(UserAnimationsEnabled());
    }
    public void NotifyCommunication()
    {
        if (_animating && State is VirgilCoreState.Idle or VirgilCoreState.Communicating)
            StartBlink(single: true);
    }
    private static void OnDetailChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((VirgilCoreControl)owner).RefreshFace(UserAnimationsEnabled());
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = true;
        SystemParameters.StaticPropertyChanged += OnWindowsPreferenceChanged;
        RefreshFace(UserAnimationsEnabled());
    }
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = false;
        SystemParameters.StaticPropertyChanged -= OnWindowsPreferenceChanged;
        RefreshFace(false);
    }
    private void OnWindowsPreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            Dispatcher.InvokeAsync(() => RefreshFace(UserAnimationsEnabled()));
    }
    private static bool UserAnimationsEnabled() => SystemParameters.ClientAreaAnimation;
    private bool IsCompact() => AnimationDetailLevel == VirgilCoreAnimationDetailLevel.Compact
        || (AnimationDetailLevel == VirgilCoreAnimationDetailLevel.Auto && ActualWidth > 0 && ActualWidth < 96);

    // One preference value controls transitions and blinking, including changes while visible.
    private void RefreshFace(bool animationsEnabled)
    {
        if (Eyes is null) return;
        var animate = _isLoaded && IsVisible && animationsEnabled && !IsCompact();
        if (_renderedState == State && _animating == animate) return;
        var generation = ++_generation;
        var changed = _renderedState != State;
        var wasAnimating = _animating;
        _renderedState = State; _animating = animate;
        Eyes.BeginAnimation(OpacityProperty, null);
        var scale = (ScaleTransform)Eyes.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleY = 1; Eyes.Opacity = 1;
        var profile = VirgilEyeProfiles.For(State);
        Eyes.Tag = profile.Shape.ToString();
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(profile.Color)); brush.Freeze();
        Eyes.Fill = profile.Shape == VirgilEyeShape.Success ? null : brush;
        Eyes.Stroke = profile.Shape == VirgilEyeShape.Success ? brush : null;
        Eyes.StrokeThickness = 18.5;
        Eyes.Data = EyeGeometry(profile.Shape);
        AutomationProperties.SetName(this, profile.AccessibleName);
        if (!animate) return;
        if (changed || !wasAnimating || PlayBootSequence)
        {
            var transition = new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop };
            transition.Completed += (_, _) =>
            {
                if (generation != _generation) return;
                Eyes.BeginAnimation(OpacityProperty, null);
                StartRestBlink();
            };
            Eyes.BeginAnimation(OpacityProperty, transition);
        }
        else StartRestBlink();
    }
    private void StartRestBlink()
    {
        if (_animating && State is VirgilCoreState.Idle or VirgilCoreState.Communicating) StartBlink(single: false);
    }
    private void StartBlink(bool single)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(single ? 0.3 : 5),
            RepeatBehavior = single ? new RepeatBehavior(1) : RepeatBehavior.Forever,
            FillBehavior = FillBehavior.Stop
        };
        var start = single ? 0 : 4.7;
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(start))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.16, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(start + 0.13))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(start + 0.3))));
        if (single)
        {
            var generation = _generation;
            animation.Completed += (_, _) => { if (generation == _generation) StartRestBlink(); };
        }
        ((ScaleTransform)Eyes.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
    private static Geometry EyeGeometry(VirgilEyeShape shape)
    {
        // The annex uses the same disc at 340 DIP (rest) and 110 DIP (other expressions).
        if (shape == VirgilEyeShape.Success)
            return Geometry.Parse("M83.455,170 Q114.364,126.727 145.273,170 M194.727,170 Q225.636,126.727 256.545,170");
        var group = new GeometryGroup();
        if (shape == VirgilEyeShape.Attention)
        {
            group.Children.Add(new EllipseGeometry(new Point(114.364,157.636), 34, 34));
            group.Children.Add(new EllipseGeometry(new Point(225.636,157.636), 34, 34));
        }
        else if (shape == VirgilEyeShape.Sarcastic)
        {
            // Reserved for the next dialogue lot; no VirgilCoreState selects it here.
            group.Children.Add(new RectangleGeometry(new Rect(86.545, 132.909, 55.636, 40.182), 18.545, 18.545));
            group.Children.Add(new RectangleGeometry(new Rect(197.818, 160.727, 55.636, 18.545), 9.273, 9.273));
        }
        else if (shape == VirgilEyeShape.Error)
        {
            // Slanted bars are baked into the paths; no moving transform is used.
            group.Children.Add(Geometry.Parse("M88,132 L142,152 Q151,156 147,166 Q144,174 135,171 L81,151 Q72,147 76,138 Q79,129 88,132 Z"));
            group.Children.Add(Geometry.Parse("M252,132 L198,152 Q189,156 193,166 Q196,174 205,171 L259,151 Q268,147 264,138 Q261,129 252,132 Z"));
        }
        else
        {
            var work = shape == VirgilEyeShape.Work;
            group.Children.Add(new RectangleGeometry(new Rect(88, work ? 151.455 : 140, 54, work ? 21.636 : 40), work ? 9.273 : 20, work ? 9.273 : 20));
            group.Children.Add(new RectangleGeometry(new Rect(198, work ? 151.455 : 140, 54, work ? 21.636 : 40), work ? 9.273 : 20, work ? 9.273 : 20));
        }
        group.Freeze(); return group;
    }
}
