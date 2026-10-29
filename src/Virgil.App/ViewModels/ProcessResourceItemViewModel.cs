using CommunityToolkit.Mvvm.Input;
using Virgil.Contracts;
using Virgil.Core.Scanning;

namespace Virgil.App.ViewModels;

public sealed class ProcessResourceItemViewModel
{
    public ProcessResourceItemViewModel(ProcessResourceInfo process, Func<Task> openLocation,
        Func<Task> closeGracefully, Func<Task> forceClose, Func<Task> ignore)
    {
        Process = process;
        OpenLocationCommand = new AsyncRelayCommand(openLocation);
        CloseGracefullyCommand = new AsyncRelayCommand(closeGracefully);
        ForceCloseCommand = new AsyncRelayCommand(forceClose);
        IgnoreCommand = new AsyncRelayCommand(ignore);
    }

    public ProcessResourceInfo Process { get; }
    public string Header => $"{Process.Name} (PID {Process.ProcessId}) - {StatusLabel(Process.Status)}";
    public string UsageLine => $"RAM {ScanRules.FormatBytes(Process.WorkingSetBytes)} - CPU {Process.CpuPercent:0.0} %";
    public string PublisherLine => $"Éditeur : {Process.Publisher ?? "non disponible"}";
    public string UserMessage => Process.UserMessage;
    public bool CanOpenLocation => !string.IsNullOrWhiteSpace(Process.Path);
    public bool CanCloseGracefully => Process.CanCloseGracefully;
    public bool CanForceClose => Process.CanForceClose;
    public IAsyncRelayCommand OpenLocationCommand { get; }
    public IAsyncRelayCommand CloseGracefullyCommand { get; }
    public IAsyncRelayCommand ForceCloseCommand { get; }
    public IAsyncRelayCommand IgnoreCommand { get; }

    private static string StatusLabel(ProcessResourceStatus status)
    {
        return status switch
        {
            ProcessResourceStatus.Heavy => "LOURD",
            ProcessResourceStatus.Review => "À VÉRIFIER",
            ProcessResourceStatus.Protected => "PROTÉGÉ",
            ProcessResourceStatus.System => "SYSTÈME PROTÉGÉ",
            _ => "NORMAL"
        };
    }

}
