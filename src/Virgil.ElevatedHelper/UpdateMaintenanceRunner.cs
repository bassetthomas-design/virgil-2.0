using System.Runtime.InteropServices;
using Virgil.Contracts;

namespace Virgil.ElevatedHelper;

/// <summary>Only fixed maintenance operations; no caller-supplied command or script.</summary>
public static class UpdateMaintenanceRunner
{
    internal const string MicrosoftUpdateServiceId = "7971f918-a847-4430-9279-4a52d1efe18d";
    internal const string FeatureCategoryId = "3689bdc8-b205-4af4-8d4a-a63924c5e9d5";
    internal const string StoreScript = "Get-CimInstance -Namespace root\\cimv2\\mdm\\dmmap -ClassName MDM_EnterpriseModernAppManagement_AppManagement01 -ErrorAction Stop | Invoke-CimMethod -MethodName UpdateScanMethod -ErrorAction Stop";
    private const string CancelMessage = "Annulation demandée — l'opération Windows Update en cours se termine (elle ne peut pas être interrompue), rien d'autre ne sera lancé.";

    public static bool Handles(InterventionId id) => id is InterventionId.InstallWindowsUpdates or InterventionId.InstallDriverUpdate or
        InterventionId.UpdateDefenderSignatures or InterventionId.PrepareMicrosoftUpdates or InterventionId.InstallMicrosoftUpdates;
    public static bool ValidIdentity(InterventionId id, string identity)
    {
        if (!Handles(id) || identity is null) return false;
        if (id == InterventionId.InstallDriverUpdate) return Guid.TryParseExact(identity, "D", out _);
        if (id is InterventionId.InstallWindowsUpdates or InterventionId.UpdateDefenderSignatures) return identity.Length == 0;
        if (identity.Length == 0) return id == InterventionId.PrepareMicrosoftUpdates;
        string[] allowed = id == InterventionId.PrepareMicrosoftUpdates ? ["defender", "microsoft-update"] : ["software", "drivers", "store", "windows-feature-upgrade"];
        var tokens = identity.Split(',');
        return tokens.Distinct(StringComparer.Ordinal).Count() == tokens.Length && tokens.All(t => allowed.Contains(t, StringComparer.Ordinal));
    }

    public static Task<(bool Success, bool Reboot, string Message)> ExecuteAsync(ElevatedInterventionRequest request,
        IElevatedCommandRunner commands, CancellationToken cancellationToken = default, IProgress<InterventionProgress>? progress = null)
        => ExecuteAsync(request, commands, new ComWindowsUpdateAgent(), cancellationToken, progress);

    public static async Task<(bool Success, bool Reboot, string Message)> ExecuteAsync(ElevatedInterventionRequest request,
        IElevatedCommandRunner commands, IWindowsUpdateAgent agent, CancellationToken cancellationToken = default, IProgress<InterventionProgress>? progress = null, TimeProvider? timeProvider = null)
    {
        if (!ValidIdentity(request.ActionId, request.UpdateIdentity)) return (false, false, "Identité de mise à jour refusée.");
        progress?.Report(new() { Step = "Préparation de la maintenance" });
        cancellationToken.ThrowIfCancellationRequested();
        var legacy = request.ActionId is InterventionId.InstallWindowsUpdates or InterventionId.InstallDriverUpdate or InterventionId.UpdateDefenderSignatures;
        var finalMessage = string.Empty;
        var reusedPoint = string.Empty;
        var output = new List<string>(); var reboot = false; var success = true;
        string Summary() => legacy ? finalMessage + (reusedPoint.Length == 0 ? "" : "\n" + reusedPoint) : string.Join('\n', output);
        void Report(string step, string status, string message)
        {
            if (legacy && status != "INFO") finalMessage = message;
            output.Add("VIRGIL|" + step + "|" + status + "|" + message);
            progress?.Report(new() { Step = message });
        }
        var tokens = request.UpdateIdentity.Split(',').ToHashSet(StringComparer.Ordinal);
        bool Has(string token) => tokens.Contains(token);
        if (request.ActionId != InterventionId.UpdateDefenderSignatures)
        {
            var restore = await UpdateRestorePoint.EnsureAsync(request, commands, cancellationToken, progress, timeProvider ?? TimeProvider.System).ConfigureAwait(false);
            if (!restore.Success) return (false, false, restore.Output);
            output.Add(restore.Output);
            if (legacy)
            {
                const string reusedPrefix = "VIRGIL|restore-point|INFO|";
                var reused = restore.Output.Split('\n').FirstOrDefault(line => line.StartsWith(reusedPrefix, StringComparison.Ordinal));
                if (reused is not null) reusedPoint = reused[reusedPrefix.Length..];
            }
            progress?.Report(new() { Step = "Point de restauration vérifié" });
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (request.ActionId == InterventionId.UpdateDefenderSignatures || request.ActionId == InterventionId.PrepareMicrosoftUpdates && Has("defender"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var defender = await commands.RunAsync(UpdateRestorePoint.PowerShell("Update-MpSignature -ErrorAction Stop")).ConfigureAwait(false);
            success &= defender.ExitCode == 0;
            Report("defender", defender.ExitCode == 0 ? "OK" : "FAILED", defender.ExitCode == 0 ? "Signatures Defender à jour." : "Mise à jour Defender impossible.");
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (request.ActionId == InterventionId.UpdateDefenderSignatures) return (success, false, Summary());
        if (request.ActionId == InterventionId.PrepareMicrosoftUpdates)
        {
            if (Has("microsoft-update"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!agent.IsServiceRegistered(MicrosoftUpdateServiceId)) agent.AddService(MicrosoftUpdateServiceId, 7);
                    Report("microsoft-update", "OK", "Microsoft Update enregistré.");
                }
                catch (COMException) { Report("microsoft-update", "UNAVAILABLE", "Microsoft Update indisponible : Windows Update seul"); }
                cancellationToken.ThrowIfCancellationRequested();
            }
            return (success, false, string.Join('\n', output));
        }
        var selection = 0; string? serviceId = null;
        cancellationToken.ThrowIfCancellationRequested();
        try { if (agent.IsServiceRegistered(MicrosoftUpdateServiceId)) { selection = 3; serviceId = MicrosoftUpdateServiceId; } }
        catch (COMException) { }
        if (selection == 0) Report("microsoft-update", "UNAVAILABLE", "Microsoft Update indisponible : Windows Update seul");
        using var registration = cancellationToken.Register(() => progress?.Report(new() { Step = CancelMessage }));
        try
        {
            for (var pass = 1; pass <= (legacy ? 1 : 3); pass++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = 0;
                if (Has("software") || request.ActionId == InterventionId.InstallWindowsUpdates) count += await InstallKindAsync("Software", false).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (Has("drivers") || request.ActionId == InterventionId.InstallDriverUpdate) count += await InstallKindAsync("Driver", false).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (count == 0) break;
            }
            if (!legacy && Has("store"))
            {
                Report("store", "INFO", "Recherche des mises à jour du Store.");
                cancellationToken.ThrowIfCancellationRequested();
                var store = await commands.RunAsync(UpdateRestorePoint.PowerShell(StoreScript)).ConfigureAwait(false);
                Report("store", store.ExitCode == 0 ? "OK" : "UNAVAILABLE", store.ExitCode == 0 ? "Scan Store lancé." : "Scan Store non disponible sur ce PC");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!legacy && Has("windows-feature-upgrade"))
            {
                Report("windows-feature-upgrade", "INFO", "Nouvelle version de Windows (si disponible)");
                Report("windows-feature-upgrade", "INFO", "Opération longue, redémarrage obligatoire si une nouvelle version est installée.");
                await InstallKindAsync("Software", true).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { Report("maintenance", "CANCELLED", "Étape terminée après annulation. Étapes suivantes non lancées."); }
        catch (COMException) { success = false; Report("maintenance", "FAILED", "Opération Windows Update impossible."); }
        return (success, reboot, Summary());

        async Task<int> InstallKindAsync(string kind, bool feature)
        {
            var step = feature ? "windows-feature-upgrade" : kind == "Driver" ? "drivers" : "software";
            Report(step, "INFO", "Recherche Windows Update — ne peut pas être interrompue");
            cancellationToken.ThrowIfCancellationRequested();
            var criteria = "IsInstalled=0 and IsHidden=0 and Type='" + kind + "'";
            if (feature) criteria += " and CategoryIDs contains '" + FeatureCategoryId + "'";
            if (request.ActionId == InterventionId.InstallDriverUpdate) criteria += " and UpdateID='" + request.UpdateIdentity + "'";
            IReadOnlyList<IWindowsUpdateItem> found;
            try { found = await agent.SearchAsync(criteria, selection, serviceId).ConfigureAwait(false); }
            catch (COMException)
            {
                cancellationToken.ThrowIfCancellationRequested(); success = false;
                if (request.ActionId == InterventionId.InstallDriverUpdate) Report(step, "FAILED", "Canal de pilotes Windows Update indisponible.");
                else
                {
                    var fallback = await commands.RunAsync(new("UsoClient.exe", ["StartInteractiveScan"])).ConfigureAwait(false);
                    Report(step, "FAILED", fallback.ExitCode == 0 ? "Recherche Windows Update lancée en secours ; vérifiez les Paramètres pour terminer." : "Windows Update indisponible.");
                }
                return 0;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var updates = found.Where(u => u.IsFeatureUpgrade == feature).ToArray();
            foreach (var update in updates) { cancellationToken.ThrowIfCancellationRequested(); if (!update.EulaAccepted) update.AcceptEula(); }
            if (updates.Length == 0) { Report(step, "OK", legacy ? "Aucune mise à jour nécessaire." : "Rien à installer"); return 0; }
            Report(step, "INFO", "Téléchargement Windows Update — ne peut pas être interrompu");
            cancellationToken.ThrowIfCancellationRequested();
            try { await agent.DownloadAsync(updates).ConfigureAwait(false); }
            catch (COMException) { success = false; Report(step, "FAILED", "Téléchargement Windows Update impossible."); return 0; }
            cancellationToken.ThrowIfCancellationRequested();
            var downloaded = updates.Where(u => u.IsDownloaded).ToArray();
            if (downloaded.Length < updates.Length) { success = false; Report(step, "FAILED", legacy ? "Téléchargement incomplet ; installation bloquée." : "Téléchargement incomplet."); }
            if (downloaded.Length == 0) return 0;
            Report(step, "INFO", "Installation Windows Update — ne peut pas être interrompue");
            cancellationToken.ThrowIfCancellationRequested();
            WindowsUpdateInstallResult installed;
            try { installed = await agent.InstallAsync(downloaded).ConfigureAwait(false); }
            catch (COMException) { success = false; Report(step, "FAILED", "Installation Windows Update impossible."); return 0; }
            reboot |= installed.RebootRequired; success &= installed.Success;
            Report(step, installed.Success ? "OK" : "FAILED", installed.Success ? "Installation terminée." : "Installation terminée avec erreurs.");
            cancellationToken.ThrowIfCancellationRequested();
            return installed.InstalledCount;
        }
    }
}
