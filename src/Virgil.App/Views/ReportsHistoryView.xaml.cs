using System.ComponentModel;
using System.Windows.Controls;
using Virgil.App.Animation;
using Virgil.App.ViewModels;
using Virgil.Contracts;

namespace Virgil.App.Views;

public partial class ReportsHistoryView : UserControl
{
    private readonly ReportsHistoryViewModel _viewModel;

    public ReportsHistoryView(ReportsHistoryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.VirgilMessageRequested += message => VirgilMessageRequested?.Invoke(message);
        viewModel.VirgilStateRequested += (state, label) => VirgilStateRequested?.Invoke(state, label);
        viewModel.ReturnHomeRequested += (_, args) => ReturnHomeRequested?.Invoke(this, args);
        viewModel.ReportPersisted += (_, args) => ReportPersisted?.Invoke(this, args);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.RefreshCommand.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(viewModel.RefreshCommand.IsRunning) && !viewModel.RefreshCommand.IsRunning)
                HistoryHeading.BringIntoView();
        };
        ReportsMiniCore.SetState(viewModel.MiniCoreState);
    }

    public event Action<string>? VirgilMessageRequested;
    public event Action<VirgilCoreState, string>? VirgilStateRequested;
    public event EventHandler? ReturnHomeRequested;
    public event EventHandler? ReportPersisted;

    public async Task RefreshAsync()
    {
        await _viewModel.RefreshAsync();
        HistoryHeading.BringIntoView();
    }

    public Task OpenLatestAsync() => _viewModel.OpenLatestAsync();
    public void ShowTransientReport(ReportEntry report) => _viewModel.ShowTransientReport(report);

    public bool TryCloseOverlay()
    {
        if (!_viewModel.IsDetailsOpen) return false;
        _viewModel.CloseDetailsCommand.Execute(null);
        return true;
    }

    public void FocusPrimaryButton() => LastPersistentReportButton.Focus();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ReportsHistoryViewModel.MiniCoreState))
            ReportsMiniCore.SetState(_viewModel.MiniCoreState);
        if (args.PropertyName == nameof(ReportsHistoryViewModel.IsDetailsOpen))
        {
            if (_viewModel.IsDetailsOpen) CloseReportDetailsButton.Focus();
            else LastPersistentReportButton.Focus();
        }
    }
}
