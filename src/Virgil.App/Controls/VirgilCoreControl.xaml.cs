using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Virgil.App.Controls;

public enum VirgilCoreState
{
    Idle,
    Scanning,
    Communicating,
    Warning,
    SensitiveAction,
    Success,
    Error
}

public partial class VirgilCoreControl : UserControl
{
    public static readonly DependencyProperty CoreStateProperty =
        DependencyProperty.Register(
            nameof(CoreState),
            typeof(VirgilCoreState),
            typeof(VirgilCoreControl),
            new PropertyMetadata(VirgilCoreState.Idle, OnCoreStateChanged));

    private Storyboard? _activeStoryboard;

    public VirgilCoreControl()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyState(CoreState);
    }

    public VirgilCoreState CoreState
    {
        get => (VirgilCoreState)GetValue(CoreStateProperty);
        set => SetValue(CoreStateProperty, value);
    }

    public void SetState(VirgilCoreState state)
    {
        CoreState = state;
    }

    private static void OnCoreStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirgilCoreControl control && control.IsLoaded)
        {
            control.ApplyState((VirgilCoreState)e.NewValue);
        }
    }

    private void ApplyState(VirgilCoreState state)
    {
        _activeStoryboard?.Remove(this);
        ResetVisuals();

        var (label, primary, soft, storyboardName) = state switch
        {
            VirgilCoreState.Scanning => ("ANALYSE", Color.FromRgb(255, 156, 24), Color.FromRgb(255, 209, 138), "ScanningStoryboard"),
            VirgilCoreState.Communicating => ("MESSAGE", Color.FromRgb(255, 177, 74), Color.FromRgb(255, 222, 166), "CommunicatingStoryboard"),
            VirgilCoreState.Warning => ("ALERTE", Color.FromRgb(255, 176, 32), Color.FromRgb(255, 218, 126), "WarningStoryboard"),
            VirgilCoreState.SensitiveAction => ("VERROU", Color.FromRgb(255, 92, 46), Color.FromRgb(255, 154, 95), "SensitiveActionStoryboard"),
            VirgilCoreState.Success => ("VALIDE", Color.FromRgb(255, 211, 106), Color.FromRgb(255, 232, 170), "SuccessStoryboard"),
            VirgilCoreState.Error => ("ERREUR", Color.FromRgb(255, 77, 46), Color.FromRgb(255, 154, 95), "ErrorStoryboard"),
            _ => ("REPOS", Color.FromRgb(255, 138, 0), Color.FromRgb(255, 177, 74), "IdleStoryboard")
        };

        StateLabel.Text = label;
        StateLabel.Foreground = new SolidColorBrush(soft);

        var primaryBrush = new SolidColorBrush(primary);
        var softBrush = new SolidColorBrush(soft);
        var haloBrush = new SolidColorBrush(Color.FromArgb(34, primary.R, primary.G, primary.B));

        Halo.Fill = haloBrush;
        CenterCore.Fill = primaryBrush;
        CenterCore.Stroke = softBrush;
        WaveRing.Stroke = softBrush;
        OuterFrame.Stroke = primaryBrush;
        InnerFrame.Stroke = new SolidColorBrush(Color.FromArgb(170, soft.R, soft.G, soft.B));
        ScanLine.Stroke = softBrush;

        foreach (var segment in GetSegments())
        {
            segment.Fill = new SolidColorBrush(Color.FromArgb(102, primary.R, primary.G, primary.B));
            segment.Stroke = softBrush;
        }

        if (FindResource(storyboardName) is Storyboard storyboard)
        {
            _activeStoryboard = storyboard.Clone();
            _activeStoryboard.Begin(this, true);
        }
    }

    private void ResetVisuals()
    {
        Halo.Opacity = 0.35;
        WaveRing.Opacity = 0;
        ScanLine.Opacity = 0;
        LockGlyph.Opacity = 0;
        SuccessFlash.Opacity = 0;
        ErrorFlash.Opacity = 0;
        CenterCore.Opacity = 0.86;
        CoreScale.ScaleX = 1;
        CoreScale.ScaleY = 1;
        CoreRotate.Angle = 0;
        WaveScale.ScaleX = 0.72;
        WaveScale.ScaleY = 0.72;
        ScanLineTranslate.Y = 0;

        foreach (var segment in GetSegments())
        {
            segment.Opacity = segment.Name is "SegmentNorth" or "SegmentEast" or "SegmentSouth" or "SegmentWest"
                ? 1
                : 0.78;
        }
    }

    private Shape[] GetSegments() =>
    [
        SegmentNorth,
        SegmentNorthEast,
        SegmentEast,
        SegmentSouthEast,
        SegmentSouth,
        SegmentSouthWest,
        SegmentWest,
        SegmentNorthWest
    ];
}
