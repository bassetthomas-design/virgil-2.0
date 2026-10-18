using CommunityToolkit.Mvvm.Input;
using Virgil.Contracts;

namespace Virgil.App.ViewModels;

public sealed class ReportHistoryItemViewModel
{
    public ReportHistoryItemViewModel(ReportEntry report, string header, Action open, Func<Task> export)
    {
        Report = report;
        Header = header;
        OpenCommand = new RelayCommand(open);
        ExportCommand = new AsyncRelayCommand(export);
    }

    public string Header { get; }
    public string Title => Report.Title;
    public string Summary => Report.Summary;
    public string StatusLine => $"Statut : {Report.Status}";
    public ReportEntry Report { get; }
    public IRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand ExportCommand { get; }
}
