using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Core.Processes;
using Virgil.Core.Releases;
using Virgil.Core.Settings;

namespace Virgil.App.ViewModels;

public partial class VirgilReleaseViewModel : ObservableObject, IDisposable
{
    private readonly VirgilReleaseChecker _checker;
    private readonly ISettingsService _settings;
    private readonly IProcessRunner _runner;
    private readonly CancellationTokenSource _lifetime = new();
    private Uri? _releasePage;
    private bool _disposed;
    private bool _started;
    private bool _busy;
    public string CurrentVersion { get; }
    [ObservableProperty] private string status = "Vérification de Virgil disponible à la demande. Même moi, je vieillis.";
    [ObservableProperty] private string settingsStatus = "";
    [ObservableProperty] private bool checkAtStartup;

    public VirgilReleaseViewModel(VirgilReleaseChecker checker, ApplicationVersionService version,
        ISettingsService settings, IProcessRunner runner)
    {
        _checker = checker;
        _settings = settings;
        _runner = runner;
        CurrentVersion = "Virgil " + (version.Current?.Value ?? "— version indisponible");
        checkAtStartup = settings.Current.CheckVirgilUpdatesAtStartup;
    }

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        await CheckAsync(true);
    }

    private bool CanWork() => !_busy && !_disposed;
    private void NotifyWorkCommands()
    { CheckCommand.NotifyCanExecuteChanged(); SavePreferenceCommand.NotifyCanExecuteChanged(); OpenReleaseCommand.NotifyCanExecuteChanged(); }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private Task Check() => CheckAsync(false);

    private async Task CheckAsync(bool startup)
    {
        if (!CanWork()) return;
        _busy = true;
        NotifyWorkCommands();
        _releasePage = null;
        OpenReleaseCommand.NotifyCanExecuteChanged();
        try
        {
            if (!startup) Status = "Vérification en cours… je consulte les nouvelles.";
            var result = await _checker.CheckAsync(startup, _lifetime.Token);
            if (result is null) return;
            Status = result switch
            {
                ReleaseCheckResult.UpToDate => "À jour. Pour une fois, rien à rattraper.",
                ReleaseCheckResult.NewVersion next => $"Nouvelle version disponible : {next.Release.Version.Value} — {next.Release.PublishedAt:dd/MM/yyyy}. Ouvrez la page pour la récupérer vous-même.",
                ReleaseCheckResult.Impossible failure => "Vérification impossible : " + FailureText(failure.Reason),
                _ => "Vérification impossible : réponse invalide."
            };
            if (result is ReleaseCheckResult.NewVersion available) _releasePage = available.Release.Page;
        }
        finally { _busy = false; NotifyWorkCommands(); }
    }

    public static string FailureText(ReleaseFailure reason) => reason switch
    {
        ReleaseFailure.OfflineOrTimeout => "hors ligne ou délai d'attente dépassé.",
        ReleaseFailure.ApiLimit => "limite de l'API GitHub atteinte (403/429). Réessayez plus tard.",
        ReleaseFailure.InvalidResponse => "réponse ou version invalide.",
        ReleaseFailure.NoPublishedRelease => "aucune release publiée avec un tag valide.",
        ReleaseFailure.Cancelled => "vérification annulée.",
        ReleaseFailure.SettingsUnavailable => "paramètres indisponibles ; horodatage non enregistré.",
        _ => "réponse invalide."
    };

    private bool CanOpenRelease() => !_busy && _releasePage is not null;
    [RelayCommand(CanExecute = nameof(CanOpenRelease))]
    private async Task OpenRelease()
    {
        if (!CanOpenRelease()) return;
        try
        {
            var result = await _runner.LaunchAsync(new ProcessLaunchRequest(_releasePage!.AbsoluteUri, [],
                UseShellExecute: true), _lifetime.Token);
            if (!result.Started) Status = "Impossible d'ouvrir la page de release dans le navigateur.";
        }
        catch (Exception) { Status = "Impossible d'ouvrir la page de release dans le navigateur."; }
    }

    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task SavePreference()
    {
        if (!CanWork()) return;
        _busy = true;
        NotifyWorkCommands();
        try
        {
            await _settings.SaveAsync(_settings.Current with { CheckVirgilUpdatesAtStartup = CheckAtStartup }, _lifetime.Token);
            SettingsStatus = CheckAtStartup ? "Vérification au démarrage activée : au plus une fois par 24 h."
                : "Vérification au démarrage désactivée. Le bouton reste disponible.";
        }
        catch (Exception) { SettingsStatus = "Impossible d'enregistrer ce réglage. Le choix précédent reste actif."; CheckAtStartup = _settings.Current.CheckVirgilUpdatesAtStartup; }
        finally { _busy = false; NotifyWorkCommands(); }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); }
}
