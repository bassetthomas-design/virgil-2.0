using System.ComponentModel;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using Virgil.App.Animation;
using Virgil.App.ViewModels;

namespace Virgil.App.Views;

public partial class InterventionsView : UserControl
{
    private readonly InterventionsViewModel _viewModel;

    public InterventionsView(InterventionsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.VirgilMessageRequested += message => VirgilMessageRequested?.Invoke(message);
        viewModel.VirgilStateRequested += (state, label) => VirgilStateRequested?.Invoke(state, label);
        viewModel.ReturnHomeRequested += (_, args) => ReturnHomeRequested?.Invoke(this, args);
        viewModel.ReportPersisted += (_, args) => ReportPersisted?.Invoke(this, args);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public event Action<string>? VirgilMessageRequested;
    public event Action<VirgilCoreState, string>? VirgilStateRequested;
    public event EventHandler? ReturnHomeRequested;
    public event EventHandler? ReportPersisted;

    public void FocusAnalyzeButton() => AnalyzeInterventionsButton.Focus();
    public void CancelActiveOperation() => _viewModel.CancelActiveOperation();
    public bool TryCloseOverlay() => _viewModel.TryCloseOverlay();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(InterventionsViewModel.IsValidationOpen) && _viewModel.IsValidationOpen)
            Dispatcher.BeginInvoke(new Action(() => ExecuteInterventionButton.Focus()), DispatcherPriority.Input);
        if (args.PropertyName == nameof(InterventionsViewModel.IsReportOpen))
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_viewModel.IsReportOpen) CloseInterventionsReportButton.Focus();
                else ViewInterventionsReportButton.Focus();
            }), DispatcherPriority.Input);
        }
        if (args.PropertyName == nameof(InterventionsViewModel.IsBusy))
            Mouse.OverrideCursor = _viewModel.IsBusy ? Cursors.Wait : null;
        if (args.PropertyName == nameof(InterventionsViewModel.MiniCoreState))
            InterventionsMiniCore.SetState(_viewModel.MiniCoreState);
        if (args.PropertyName == nameof(InterventionsViewModel.ValidationCoreState))
            ValidationCore.SetState(_viewModel.ValidationCoreState);
    }
}
