using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Virgil.App.Animation;
using Virgil.App.ViewModels;

namespace Virgil.App.Views;

public partial class CleanupView : UserControl
{
    private readonly CleanupViewModel _viewModel;

    public CleanupView(CleanupViewModel viewModel)
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

    public void FocusAnalyzeButton() => AnalyzeZonesButton.Focus();
    public void CancelActiveOperation() => _viewModel.CancelActiveOperation();
    public bool TryCloseOverlay() => _viewModel.TryCloseOverlay();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CleanupViewModel.IsValidationOpen) && _viewModel.IsValidationOpen)
            Dispatcher.BeginInvoke(new Action(() => ExecuteZoneButton.Focus()), DispatcherPriority.Input);
        if (args.PropertyName == nameof(CleanupViewModel.IsReportOpen))
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_viewModel.IsReportOpen) CloseCleanupReportButton.Focus();
                else ViewCleanupReportButton.Focus();
            }), DispatcherPriority.Input);
        if (args.PropertyName == nameof(CleanupViewModel.IsBusy))
            Mouse.OverrideCursor = _viewModel.IsBusy ? Cursors.Wait : null;
        if (args.PropertyName == nameof(CleanupViewModel.MiniCoreState))
            CleanupMiniCore.SetState(_viewModel.MiniCoreState);
        if (args.PropertyName == nameof(CleanupViewModel.ValidationCoreState))
            ValidationCore.SetState(_viewModel.ValidationCoreState);
        if (args.PropertyName == nameof(CleanupViewModel.ReportCoreState))
            ReportCore.SetState(_viewModel.ReportCoreState);
    }
}
