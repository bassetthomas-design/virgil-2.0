using System.ComponentModel;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using Virgil.App.Animation;
using Virgil.App.ViewModels;

namespace Virgil.App.Views;

public partial class ResourcesView : UserControl
{
    private readonly ResourcesViewModel _viewModel;

    public ResourcesView(ResourcesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.VirgilMessageRequested += message => VirgilMessageRequested?.Invoke(message);
        viewModel.VirgilStateRequested += (state, label) => VirgilStateRequested?.Invoke(state, label);
        viewModel.ReturnHomeRequested += (_, args) => ReturnHomeRequested?.Invoke(this, args);
        viewModel.ReportPersisted += (_, args) => ReportPersisted?.Invoke(this, args);
        viewModel.ProcessListScrollRequested += (_, _) => ProcessListHeading.BringIntoView();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public event Action<string>? VirgilMessageRequested;
    public event Action<VirgilCoreState, string>? VirgilStateRequested;
    public event EventHandler? ReturnHomeRequested;
    public event EventHandler? ReportPersisted;

    public void FocusAnalyzeButton() => AnalyzeResourcesButton.Focus();
    public void CancelActiveOperation() => _viewModel.CancelActiveOperation();
    public bool TryCloseOverlay() => _viewModel.TryCloseOverlay();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ResourcesViewModel.IsValidationOpen) && _viewModel.IsValidationOpen)
            Dispatcher.BeginInvoke(new Action(() => ExecuteResourceActionButton.Focus()), DispatcherPriority.Input);
        if (args.PropertyName == nameof(ResourcesViewModel.IsReportOpen) && _viewModel.IsReportOpen)
            Dispatcher.BeginInvoke(new Action(() => CloseResourcesReportButton.Focus()), DispatcherPriority.Input);
        if (args.PropertyName == nameof(ResourcesViewModel.IsBusy))
            Mouse.OverrideCursor = _viewModel.IsBusy ? Cursors.Wait : null;
        if (args.PropertyName == nameof(ResourcesViewModel.MiniCoreState))
            ResourcesMiniCore.SetState(_viewModel.MiniCoreState);
        if (args.PropertyName == nameof(ResourcesViewModel.ValidationCoreState))
            ValidationCore.SetState(_viewModel.ValidationCoreState);
    }
}
