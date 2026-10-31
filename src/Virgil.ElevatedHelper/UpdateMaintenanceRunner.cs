using System.Runtime.InteropServices;
using Virgil.Contracts;

namespace Virgil.ElevatedHelper;

/// <summary>Only fixed maintenance operations; no caller-supplied command or script.</summary>
public static class UpdateMaintenanceRunner
{
    // Neutral injection seam: implementation follows the regression commit.
    public static Task<(bool Success, bool Reboot, string Message)> ExecuteAsync(
        ElevatedInterventionRequest request, IElevatedCommandRunner commands, IWindowsUpdateAgent agent,
        CancellationToken cancellationToken = default, IProgress<InterventionProgress>? progress = null)
        => ExecuteAsync(request, commands, cancellationToken, progress);

    public static bool Handles(InterventionId id) => id is InterventionId.InstallWindowsUpdates or InterventionId.InstallDriverUpdate or InterventionId.UpdateDefenderSignatures;
    public static bool ValidIdentity(InterventionId id, string identity) => id == InterventionId.InstallDriverUpdate
        ? Guid.TryParseExact(identity, "D", out _) : string.IsNullOrEmpty(identity);

    public static async Task<(bool Success, bool Reboot, string Message)> ExecuteAsync(ElevatedInterventionRequest request, IElevatedCommandRunner commands, CancellationToken cancellationToken = default, IProgress<InterventionProgress>? progress = null)
    {
        progress?.Report(new InterventionProgress { Step = "Préparation de la maintenance" });
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidIdentity(request.ActionId, request.UpdateIdentity)) return (false, false, "Identité de mise à jour refusée.");
        if (request.ActionId == InterventionId.UpdateDefenderSignatures)
        {
            var defender = await commands.RunAsync(new("WindowsPowerShell\\v1.0\\powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "Update-MpSignature -ErrorAction Stop"]) { NonInterruptible = true }).ConfigureAwait(false);
            return (defender.ExitCode == 0, false, defender.ExitCode == 0 ? "Signatures Defender à jour." : "Mise à jour Defender impossible.");
        }
        // Protection fails closed: installation never starts unless creation and verification succeed.
        if (!Guid.TryParseExact(request.RestoreSessionId, "D", out _)) return (false, false, "Session de restauration invalide.");
        var description = "Virgil mises à jour " + request.RestoreSessionId;
        // Correlated to this single execution; never reuse an unrelated existing checkpoint.
        var restoreScript = "$ErrorActionPreference='Stop'; $name='" + description + "'; " +
            "$point=Get-ComputerRestorePoint | Where-Object { $_.Description -eq $name -and [Management.ManagementDateTimeConverter]::ToDateTime($_.CreationTime) -ge (Get-Date).AddHours(-2) -and [Management.ManagementDateTimeConverter]::ToDateTime($_.CreationTime) -le (Get-Date) }; " +
            "if (-not $point) { Checkpoint-Computer -Description $name -RestorePointType MODIFY_SETTINGS }; " +
            "$verified=Get-ComputerRestorePoint | Where-Object { $_.Description -eq $name -and [Management.ManagementDateTimeConverter]::ToDateTime($_.CreationTime) -ge (Get-Date).AddHours(-2) -and [Management.ManagementDateTimeConverter]::ToDateTime($_.CreationTime) -le (Get-Date) }; if (-not $verified) { exit 1 }";
        progress?.Report(new InterventionProgress { Step = "Création du point de restauration" });
        cancellationToken.ThrowIfCancellationRequested();
        var restore = await commands.RunAsync(new("WindowsPowerShell\\v1.0\\powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", restoreScript]) { NonInterruptible = true }).ConfigureAwait(false);
        if (restore.ExitCode != 0) return (false, false, "Point de restauration non vérifié. Activez la protection du système puis réessayez. Installation bloquée.");
        cancellationToken.ThrowIfCancellationRequested();
        try { return Install(request, cancellationToken, progress); }
        catch (COMException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.ActionId == InterventionId.InstallDriverUpdate) return (false, false, "Canal de pilotes Windows Update indisponible.");
            var fallback = await commands.RunAsync(new("UsoClient.exe", ["StartInteractiveScan"])).ConfigureAwait(false);
            return (false, false, fallback.ExitCode == 0 ? "Recherche Windows Update lancée en secours ; vérifiez les Paramètres pour terminer." : "Windows Update indisponible.");
        }
    }

    private static (bool Success, bool Reboot, string Message) Install(ElevatedInterventionRequest request, CancellationToken cancellationToken, IProgress<InterventionProgress>? progress)
    {
        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: true)!;
        dynamic session = Activator.CreateInstance(sessionType)!;
        dynamic searcher = session.CreateUpdateSearcher();
        var driver = request.ActionId == InterventionId.InstallDriverUpdate;
        progress?.Report(new InterventionProgress { Step = "Recherche Windows Update — ne peut pas être interrompue" });
        cancellationToken.ThrowIfCancellationRequested();
        dynamic search = searcher.Search(driver ? $"IsInstalled=0 and Type='Driver' and UpdateID='{request.UpdateIdentity}'" : "IsInstalled=0 and Type='Software' and IsHidden=0");
        cancellationToken.ThrowIfCancellationRequested();
        dynamic collection = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", true)!)!;
        for (var i = 0; i < search.Updates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic update = search.Updates.Item(i);
            // Exclude feature upgrades from this quality/security maintenance sequence.
            var featureUpgrade = false;
            for (var categoryIndex = 0; categoryIndex < update.Categories.Count; categoryIndex++)
                if (string.Equals((string)update.Categories.Item(categoryIndex).CategoryID, "3689bdc8-b205-4af4-8d4a-a63924c5e9d5", StringComparison.OrdinalIgnoreCase)) featureUpgrade = true;
            if (featureUpgrade) continue;
            if (!update.EulaAccepted) update.AcceptEula();
            collection.Add(update);
        }
        if (collection.Count == 0) return (true, false, "Aucune mise à jour nécessaire.");
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new InterventionProgress { Step = "Téléchargement Windows Update — ne peut pas être interrompu" });
        cancellationToken.ThrowIfCancellationRequested();
        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = collection;
        dynamic downloaded = downloader.Download();
        if ((int)downloaded.ResultCode != 2) return (false, false, "Téléchargement incomplet ; installation bloquée.");
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new InterventionProgress { Step = "Installation Windows Update — ne peut pas être interrompue" });
        cancellationToken.ThrowIfCancellationRequested();
        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = collection;
        dynamic installed = installer.Install();
        return ((int)installed.ResultCode == 2, (bool)installed.RebootRequired, (int)installed.ResultCode == 2 ? "Installation terminée." : "Installation terminée avec erreurs.");
    }
}
