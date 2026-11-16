using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Core.Processes;

namespace Virgil.App.ViewModels;

public partial class BugReportViewModel : ObservableObject
{
    private readonly IProcessRunner _runner;
    private readonly BugReportContext _context;
    private bool _opening;
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _failedUrl = "";
    [ObservableProperty] private bool _hasError;

    public BugReportViewModel(IProcessRunner runner, BugReportContext context)
    {
        _runner = runner;
        _context = context;
    }

    public Func<string> ScreenSource { get; set; } = () => "non déterminé";

    private bool CanOpen() => !_opening;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenAsync(CancellationToken token)
    {
        if (_opening) return;
        _opening = true;
        OpenCommand.NotifyCanExecuteChanged();
        var url = "";
        try
        {
            string screen;
            try { screen = ScreenSource(); }
            catch (Exception) { screen = "non déterminé"; }
            url = BugReportLink.Build(_context, screen);
            var result = await _runner.LaunchAsync(new ProcessLaunchRequest(url, [], UseShellExecute: true), token);
            if (!result.Started || result.LaunchError is not null) ShowFailure(url);
            else
            {
                HasError = false;
                ErrorText = "";
                FailedUrl = "";
            }
        }
        catch (Exception)
        {
            ShowFailure(url);
        }
        finally
        {
            _opening = false;
            OpenCommand.NotifyCanExecuteChanged();
        }
    }

    private void ShowFailure(string url)
    {
        ErrorText = "Le navigateur ne s'est pas ouvert. Copiez ce lien dans votre navigateur pour signaler le problème :";
        FailedUrl = url;
        HasError = true;
    }
}
