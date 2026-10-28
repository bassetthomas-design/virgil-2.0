using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Startup;

namespace Virgil.App.ViewModels;

public partial class StartupViewModel : ObservableObject
{
    private readonly IStartupInventorySource _source;
    private readonly StartupChangeService _changes;
    private readonly IConfirmationService _confirmation;
    private StartupChange? _pending;
    private string? _backupId;
    [ObservableProperty] private IReadOnlyList<StartupItem> items = Array.Empty<StartupItem>();
    [ObservableProperty] private string status = "Inventaire en lecture seule.";
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isChanging;
    [ObservableProperty] private StartupItem? selectedItem;
    [ObservableProperty] private bool isConfirmationOpen;
    [ObservableProperty] private bool confirmationAccepted;
    [ObservableProperty] private string confirmationText = string.Empty;

    public StartupViewModel(IStartupInventorySource source, StartupChangeService changes, IConfirmationService confirmation)
    {
        _source = source;
        _changes = changes;
        _confirmation = confirmation;
    }

    public string SelectionDetails => Status;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            var inventory = await _source.ReadAsync(CancellationToken.None);
            Items = inventory.Items;
            Status = inventory.Warnings.Count == 0 ? $"{Items.Count} éléments au démarrage." : string.Join(" ", inventory.Warnings.Distinct());
        }
        catch (Exception) { Status = "Impossible de lire les éléments au démarrage."; }
        finally { IsRefreshing = false; }
    }

    [RelayCommand] private Task EnableAsync() => PrepareAsync(true);
    [RelayCommand] private Task DisableAsync() => PrepareAsync(false);
    private async Task PrepareAsync(bool enabled)
    {
        if (IsChanging || IsConfirmationOpen) return;
        var target = SelectedItem?.ApprovalTarget;
        if (target is null) { Status = "Cette entrée reste en lecture seule. Sa modification sera bientôt disponible."; return; }
        try
        {
            var change = await _changes.PreviewAsync(target, enabled);
            if (change is null) { Status = "Autorisation absente ou non prise en charge : aucune modification proposée."; return; }
            ShowConfirmation(change, (enabled ? "Activer" : "Désactiver") + " au démarrage : " + target.ValueName);
        }
        catch (Exception) { Status = "Impossible de préparer cette modification. Aucune entrée modifiée."; }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsChanging || IsConfirmationOpen) return;
        if (_backupId is null) { Status = "Aucune sauvegarde à restaurer dans cette session."; return; }
        try
        {
            var change = await _changes.PreviewRestoreAsync(_backupId);
            if (change is null) { Status = "La sauvegarde n’a pas pu être vérifiée. Aucune modification."; return; }
            ShowConfirmation(change, "Restaurer l’autorisation précédente : " + change.Target.ValueName);
        }
        catch (Exception) { Status = "Impossible de préparer la restauration. Aucune entrée modifiée."; }
    }

    private void ShowConfirmation(StartupChange change, string title)
    {
        _pending = change;
        ConfirmationAccepted = false;
        var scope = change.Target.Hive == StartupApprovalHive.CurrentUser ? "Compte actuel" : "Tous les comptes";
        var path = Items.FirstOrDefault(item => item.ApprovalTarget == change.Target)?.Path ?? "Chemin indisponible";
        ConfirmationText = title + "\nPortée : " + scope + "\nSource : StartupApproved / " + change.Target.Kind + "\nChemin : " + path + "\nL’entrée et sa commande seront conservées. Une sauvegarde vérifiée et un point de restauration sont obligatoires avant l’écriture. Si une protection échoue, la modification sera bloquée. Le nouvel état s’appliquera aux prochaines ouvertures de session.";
        IsConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelConfirmation()
    {
        if (IsChanging) return;
        _pending = null;
        IsConfirmationOpen = false;
        ConfirmationAccepted = false;
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (!IsConfirmationOpen || !ConfirmationAccepted || _pending is null || IsChanging) return;
        IsChanging = true;
        var change = _pending;
        try
        {
            var preview = StartupChangeService.CreatePreview(change);
            var plan = _confirmation.Issue(preview, new(ConfirmationLevel.Reinforced, null));
            IsConfirmationOpen = false;
            _pending = null;
            var result = await _changes.ExecuteAsync(change, plan);
            if (result.BackupId is not null) _backupId = result.BackupId;
            await RefreshAsync();
            Status = result.Message;
        }
        catch (Exception) { Status = "La modification n’a pas abouti. Actualisez pour vérifier l’état ; les sauvegardes sont conservées."; }
        finally { IsChanging = false; ConfirmationAccepted = false; }
    }
}
