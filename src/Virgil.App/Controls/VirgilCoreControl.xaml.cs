using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Virgil.App.Controls;

public enum VirgilCoreState
{
    Idle,
    Scanning,
    Success,
    Warning,
    Error
}

public partial class VirgilCoreControl : UserControl
{
    private Storyboard? _activeStoryboard;
    private EventHandler? _completedHandler;
    private VirgilCoreState _state = VirgilCoreState.Idle;
    private bool _isLoaded;

    public VirgilCoreControl()
    {
        InitializeComponent();
    }

    public VirgilCoreState State => _state;

    public void SetState(VirgilCoreState state)
    {
        _state = state;

        if (!_isLoaded)
        {
            return;
        }

        StopActiveStoryboard();
        ResetVisuals();

        switch (state)
        {
            case VirgilCoreState.Scanning:
                StartScanning();
                break;
            case VirgilCoreState.Success:
                StartTransientFlash("App.AccentLightBrush", VirgilCoreState.Success);
                break;
            case VirgilCoreState.Warning:
                ApplyWarning();
                break;
            case VirgilCoreState.Error:
                StartTransientFlash("App.AlertBrush", VirgilCoreState.Error);
                break;
            default:
                StartIdlePulse();
                break;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        SetState(_state);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        StopActiveStoryboard();
    }

    private void StartIdlePulse()
    {
        var storyboard = new Storyboard();
        AddDouble(storyboard, CenterGlow, OpacityProperty, 0.28, 0.46, 2.6, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterHalo, OpacityProperty, 0.16, 0.28, 2.6, true, RepeatBehavior.Forever);
        AddDouble(storyboard, HaloScale, ScaleTransform.ScaleXProperty, 0.98, 1.04, 2.6, true, RepeatBehavior.Forever);
        AddDouble(storyboard, HaloScale, ScaleTransform.ScaleYProperty, 0.98, 1.04, 2.6, true, RepeatBehavior.Forever);
        BeginStoryboard(storyboard);
    }

    private void StartScanning()
    {
        var storyboard = new Storyboard();
        AddDouble(storyboard, SegmentRingRotate, RotateTransform.AngleProperty, 0, 360, 7.2, false, RepeatBehavior.Forever);
        AddDouble(storyboard, ScanLine, OpacityProperty, 0.12, 0.76, 1.2, true, RepeatBehavior.Forever);
        AddDouble(storyboard, ScanLineTransform, TranslateTransform.YProperty, -58, 58, 2.4, true, RepeatBehavior.Forever);
        AddDouble(storyboard, CenterGlow, OpacityProperty, 0.34, 0.64, 1.1, true, RepeatBehavior.Forever);
        AddDouble(storyboard, OuterHalo, OpacityProperty, 0.22, 0.38, 1.1, true, RepeatBehavior.Forever);
        BeginStoryboard(storyboard);
    }

    private void StartTransientFlash(string brushKey, VirgilCoreState transientState)
    {
        StatusFlash.Stroke = FindBrush(brushKey);
        CenterGlow.Fill = FindBrush(brushKey);

        var storyboard = new Storyboard();
        AddDouble(storyboard, StatusFlash, OpacityProperty, 0.78, 0, 0.85);
        AddDouble(storyboard, CenterGlow, OpacityProperty, 0.7, 0.34, 0.85);
        AddDouble(storyboard, OuterHalo, OpacityProperty, 0.48, 0.2, 0.85);

        EventHandler completed = (_, _) =>
        {
            if (_state == transientState)
            {
                SetState(VirgilCoreState.Idle);
            }
        };

        BeginStoryboard(storyboard, completed);
    }

    private void ApplyWarning()
    {
        CenterGlow.Fill = FindBrush("App.AccentLightBrush");
        StatusFlash.Stroke = FindBrush("App.AccentLightBrush");
        CenterGlow.Opacity = 0.46;
        StatusFlash.Opacity = 0.22;
        OuterHalo.Opacity = 0.26;
    }

    private void ResetVisuals()
    {
        SegmentRingRotate.Angle = 0;
        HaloScale.ScaleX = 1;
        HaloScale.ScaleY = 1;
        ScanLineTransform.Y = -54;
        ScanLine.Opacity = 0;
        CenterGlow.Fill = FindBrush("App.AccentBrush");
        CenterGlow.Opacity = 0.34;
        OuterHalo.Opacity = 0.2;
        StatusFlash.Stroke = FindBrush("App.AccentLightBrush");
        StatusFlash.Opacity = 0;
    }

    private void BeginStoryboard(Storyboard storyboard, EventHandler? completed = null)
    {
        if (completed is not null)
        {
            storyboard.Completed += completed;
        }

        _activeStoryboard = storyboard;
        _completedHandler = completed;
        storyboard.Begin(this, true);
    }

    private void StopActiveStoryboard()
    {
        if (_activeStoryboard is null)
        {
            return;
        }

        if (_completedHandler is not null)
        {
            _activeStoryboard.Completed -= _completedHandler;
        }

        _activeStoryboard.Stop(this);
        _activeStoryboard.Remove(this);
        _activeStoryboard = null;
        _completedHandler = null;
    }

    private void AddDouble(
        Storyboard storyboard,
        DependencyObject target,
        DependencyProperty property,
        double from,
        double to,
        double seconds,
        bool autoReverse = false,
        RepeatBehavior? repeatBehavior = null)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            AutoReverse = autoReverse
        };

        if (repeatBehavior.HasValue)
        {
            animation.RepeatBehavior = repeatBehavior.Value;
        }

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private Brush FindBrush(string key)
    {
        return TryFindResource(key) as Brush ?? Brushes.Orange;
    }
}
