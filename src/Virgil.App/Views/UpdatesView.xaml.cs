using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Virgil.App.Animation;
using Virgil.App.ViewModels;

namespace Virgil.App.Views;

public partial class UpdatesView : UserControl
{
    private readonly UpdatesViewModel _viewModel;

    public UpdatesView(UpdatesViewModel viewModel)
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

    public void FocusScanButton() => ScanUpdatesButton.Focus();
    public void CancelActiveOperation() => _viewModel.CancelActiveOperation();
    public bool TryCloseOverlay() => _viewModel.TryCloseOverlay();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(UpdatesViewModel.IsValidationOpen) && _viewModel.IsValidationOpen)
            Dispatcher.BeginInvoke(new Action(() => ExecuteUpdateButton.Focus()), DispatcherPriority.Input);
        if (args.PropertyName == nameof(UpdatesViewModel.IsReportOpen))
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_viewModel.IsReportOpen) CloseUpdatesReportButton.Focus();
                else ViewUpdatesReportButton.Focus();
            }), DispatcherPriority.Input);
        if (args.PropertyName == nameof(UpdatesViewModel.IsBusy))
            Mouse.OverrideCursor = _viewModel.IsBusy ? Cursors.Wait : null;
        if (args.PropertyName == nameof(UpdatesViewModel.MiniCoreState))
            UpdatesMiniCore.SetState(_viewModel.MiniCoreState);
        if (args.PropertyName == nameof(UpdatesViewModel.ValidationCoreState))
            ValidationCore.SetState(_viewModel.ValidationCoreState);
    }
}
