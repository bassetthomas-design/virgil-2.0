using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Virgil.App.ViewModels;
using Virgil.Core.Diagnostic;

namespace Virgil.App.Views;

public partial class DiagnosticView : UserControl
{
    private readonly DiagnosticViewModel _viewModel;
    private Window? _window;
    public event Action<DiagnosticSuggestion>? NavigationRequested;
    public bool TryCloseOverlay()
    {
        if (!_viewModel.IsValidationOpen) return false;
        _viewModel.CancelConfirmation();
        return true;
    }
    public DiagnosticView(DiagnosticViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        PreviewKeyDown += View_PreviewKeyDown;
        DataContext = viewModel;
        viewModel.NavigationRequested += destination => NavigationRequested?.Invoke(destination);
    }
    private void View_Loaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.StateChanged += Window_StateChanged;
        }
        UpdatePresentation();
    }
    private void View_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.StateChanged -= Window_StateChanged;
        }
        _window = null;
        _viewModel.SetPresentation(false, false);
    }
    private void View_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdatePresentation();
    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsVisible && e.Key == Key.Escape && TryCloseOverlay()) e.Handled = true;
    }
    private void Window_StateChanged(object? sender, EventArgs e) => UpdatePresentation();
    private void UpdatePresentation() => _viewModel.SetPresentation(IsLoaded && IsVisible, _window?.WindowState == WindowState.Minimized);
    private void Processes_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (Enum.TryParse<DiagnosticProcessSort>(e.Column.SortMemberPath, out var sort)) _viewModel.Sort(sort);
    }
}
