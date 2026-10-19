using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Virgil.App.Animation;
using Virgil.App.ViewModels;

namespace Virgil.App.Views;

public partial class ApplicationsView : UserControl
{
    private readonly ApplicationsViewModel _viewModel;

    public ApplicationsView(ApplicationsViewModel viewModel)
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

    public void FocusAnalyzeButton() => Dispatcher.BeginInvoke(new Action(() => AnalyzeApplicationsButton.Focus()), DispatcherPriority.Input);
    public void CancelActiveOperation() => _viewModel.CancelActiveOperation();
    public bool TryCloseOverlay() => _viewModel.TryCloseOverlay();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ApplicationsViewModel.IsDetailsOpen) && _viewModel.IsDetailsOpen)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_viewModel.IsDetailsOpen) CloseDetailsButton.Focus();
            }), DispatcherPriority.Input);
        if (args.PropertyName == nameof(ApplicationsViewModel.IsReportOpen) && _viewModel.IsReportOpen)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_viewModel.IsReportOpen) CloseApplicationsReportButton.Focus();
            }), DispatcherPriority.Input);
        if (args.PropertyName == nameof(ApplicationsViewModel.IsBusy))
            Mouse.OverrideCursor = _viewModel.IsBusy ? Cursors.Wait : null;
    }
}
