using System.Text.Json;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Updates;

public enum UpdateComponent { Windows, Defender, Driver, Winget, Launcher, Restore, MicrosoftUpdate, Store, WindowsFeatureUpgrade, WingetFinal, PendingReboot }
public sealed record UpdateSequenceStep(string Id, string Title, UpdateComponent Component, UpdateItem? Application = null)
{
    public bool IsInformational => Component is UpdateComponent.Restore or UpdateComponent.PendingReboot;
}
public sealed record UpdateSequenceProgress(int Current, int Total, string Title, string Status)
{
    public string? StepId { get; init; }
    public UpdateComponent? Component { get; init; }
}
public sealed record UpdateSourceVerification(UpdateComponent Component, string Status, string Detail)
{
    public bool? HasRemaining { get; init; }
    public bool IsCurrent => Status is "à jour" or "mis à jour";
}
public sealed record UpdateSequenceStepResult(UpdateSequenceStep Step, bool Succeeded, bool RebootRequired, string Message)
{
    public bool WasSkipped { get; init; }
    public bool WasUnavailable { get; init; }
    public bool? RestorePointVerified { get; init; }
}
public sealed record UpdateSequenceResult(bool WasRefused, bool WasCancelled, bool RebootRequired, IReadOnlyList<UpdateSequenceStepResult> Steps, string Message)
{
    public IReadOnlyList<UpdateSourceVerification> Verifications { get; init; } = [];
    public int SuccessfulCount => Steps.Count(step => !step.Step.IsInformational && !step.WasSkipped && !step.WasUnavailable && step.Succeeded);
    public bool HasFailures => Steps.Any(step => !step.Step.IsInformational && !step.WasSkipped && !step.WasUnavailable && !step.Succeeded);
}
public interface IUpdateSequenceBackend
{
    Task<UpdateSourceVerification> VerifyAsync(UpdateComponent component, CancellationToken token) =>
        Task.FromResult(new UpdateSourceVerification(component, "indisponible", "Contrôle final non disponible sur ce PC."));
    Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    void BeginExecution(string restoreSessionId) { }
    async Task<IReadOnlyList<UpdateSequenceStepResult>> ExecuteGroupAsync(IReadOnlyList<UpdateSequenceStep> steps, CancellationToken token)
    {
        var results = new List<UpdateSequenceStepResult>();
        foreach (var step in steps)
        {
            if (token.IsCancellationRequested) break;
            results.Add(await ExecuteAsync(step, token).ConfigureAwait(false));
        }
        return results;
    }
    Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken cancellationToken);
}
public interface IUpdateSequenceService
{
    Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> apps, CancellationToken cancellationToken);
    ActionPreview CreatePreview(IReadOnlyList<UpdateSequenceStep> selected);
    Task<UpdateSequenceResult> ExecuteAsync(IReadOnlyList<UpdateSequenceStep> selected, ConfirmedPlan? plan, IProgress<UpdateSequenceProgress>? progress, CancellationToken cancellationToken);
}
public sealed class UpdateSequenceService(IUpdateSequenceBackend backend, IConfirmedPlanGate gate, IPendingRebootProbe? rebootProbe = null, LauncherShutdownPolicy? launcherShutdown = null) : IUpdateSequenceService
{
    public async Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> apps, CancellationToken cancellationToken)
    {
        var steps = new List<UpdateSequenceStep>
        {
            new("restore", "Point de restauration", UpdateComponent.Restore),
            new("defender", "Signatures Microsoft Defender", UpdateComponent.Defender),
            new("microsoft-update", "Enregistrer Microsoft Update (Office et produits Microsoft)", UpdateComponent.MicrosoftUpdate)
        };
        var launchers = LauncherCatalog.Detect(await backend.DetectLaunchersAsync(cancellationToken).ConfigureAwait(false));
        if (launchers.Count > 0) steps.Add(new("launchers", "Virgil fermera : " + string.Join(", ", launchers), UpdateComponent.Launcher));
        steps.AddRange(apps.Where(a => a.Status == UpdateItemStatus.Available).Select(a => new UpdateSequenceStep(a.Id, a.Name, UpdateComponent.Winget, a)));
        steps.AddRange([
            new("software", "Windows Update : logiciels (jusqu'à 3 passes)", UpdateComponent.Windows),
            new("drivers", "Windows Update : pilotes (jusqu'à 3 passes)", UpdateComponent.Driver),
            new("store", "Rechercher les mises à jour du Microsoft Store", UpdateComponent.Store),
            new("windows-feature-upgrade", "Nouvelle version de Windows (si disponible)", UpdateComponent.WindowsFeatureUpgrade),
            new("winget-final", "Passe finale : tout ce que winget trouve", UpdateComponent.WingetFinal),
            new("pending-reboot", "Vérifier le redémarrage en attente", UpdateComponent.PendingReboot)
        ]);
        return steps;
    }
    public ActionPreview CreatePreview(IReadOnlyList<UpdateSequenceStep> selected)
    {
        selected = selected.Where(step => step.Component != UpdateComponent.Restore && step.Component != UpdateComponent.PendingReboot).ToArray();
        return new(
        "updates.sequence", "Tout mettre à jour", RiskLevel.Sensitive,
        selected.Select(s => s.Title).ToArray(),
        new Dictionary<string, string> { ["Steps"] = JsonSerializer.Serialize(selected) },
        ["Installer les composants cochés dans l’ordre présenté.", "Un redémarrage peut être nécessaire ; il restera manuel.", "Les pilotes et Windows exigent un point de restauration vérifié."],
        selected.Any(s => IsElevated(s.Component)), true, Reversibility.Partial, ["Point de restauration"]);
    }
    public static bool IsElevated(UpdateComponent component) => component is UpdateComponent.Windows or UpdateComponent.Driver or UpdateComponent.Defender or UpdateComponent.MicrosoftUpdate or UpdateComponent.Store or UpdateComponent.WindowsFeatureUpgrade;
    public static bool IsSafetyBlocked(UpdateSequenceStepResult result) => !result.Succeeded && !result.WasUnavailable
        && (result.RestorePointVerified == false || result.Message.Contains("bloquée", StringComparison.OrdinalIgnoreCase)
            || result.Message.Contains("refus", StringComparison.OrdinalIgnoreCase) || result.Message.Contains("garde de sécurité", StringComparison.OrdinalIgnoreCase));

    public async Task<UpdateSequenceResult> ExecuteAsync(IReadOnlyList<UpdateSequenceStep> selected, ConfirmedPlan? plan, IProgress<UpdateSequenceProgress>? progress, CancellationToken cancellationToken)
    {
        var snapshot = selected.Where(s => s.Component is not UpdateComponent.Restore and not UpdateComponent.PendingReboot).Select(s => s with { Application = s.Application is null ? null : s.Application with { } }).ToArray();
        var validation = gate.TryConsume(plan, CreatePreview(snapshot));
        if (!validation.IsAccepted) return new(true, false, false, [], validation.Message);
        backend.BeginExecution(Guid.NewGuid().ToString("D"));
        var results = new List<UpdateSequenceStepResult>();
        var handled = new HashSet<(UpdateComponent Component, string Id)>();
        var microsoftBlocked = false;
        if (snapshot.Any(s => IsElevated(s.Component)))
        {
            var preparation = snapshot.Where(s => s.Component is UpdateComponent.Defender or UpdateComponent.MicrosoftUpdate).ToArray();
            if (!cancellationToken.IsCancellationRequested)
            {
                foreach (var member in preparation) handled.Add((member.Component, member.Id));
                progress?.Report(new(1, snapshot.Length, "Point de restauration et préparation Microsoft Update", "En cours"));
                IReadOnlyList<UpdateSequenceStepResult> protectedResults;
                try { protectedResults = await backend.ExecuteGroupAsync(preparation, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { protectedResults = []; }
                catch
                {
                    protectedResults = preparation.Length > 0
                        ? preparation.Select(s => new UpdateSequenceStepResult(s, false, false, "Installation bloquée : préparation non vérifiée.")).ToArray()
                        : [new(new("restore", "Point de restauration", UpdateComponent.Restore), false, false, "Installation bloquée : préparation non vérifiée.")];
                }
                results.AddRange(protectedResults);
                microsoftBlocked = protectedResults.Any(r => r.RestorePointVerified == false) ||
                    (protectedResults.All(r => r.RestorePointVerified is null) && protectedResults.Any(r => !r.Succeeded));
            }
        }
        foreach (var step in snapshot)
        {
            if (handled.Contains((step.Component, step.Id))) continue;
            if (cancellationToken.IsCancellationRequested) break;
            if (microsoftBlocked && IsElevated(step.Component))
            {
                results.Add(new(step, false, false, "Installation Microsoft bloquée : point de restauration non vérifié."));
                handled.Add((step.Component, step.Id));
                continue;
            }
            progress?.Report(new(results.Count + 1, snapshot.Length, step.Title, "En cours") { StepId = step.Id, Component = step.Component });
            try
            {
                if (step.Component == UpdateComponent.Launcher)
                {
                    var names = step.Title.Replace("Virgil fermera : ", "", StringComparison.Ordinal).Split(", ");
                    var shutdown = launcherShutdown is null ? new LauncherShutdownResult(false, "mis à jour au prochain lancement") : await launcherShutdown.ExecuteAsync(names, cancellationToken).ConfigureAwait(false);
                    results.Add(new(step, shutdown.AnyClosed, false, shutdown.Message) { WasSkipped = !shutdown.AnyClosed });
                    handled.Add((step.Component, step.Id));
                }
                else if (IsElevated(step.Component))
                {
                    var prepare = step.Component is UpdateComponent.Defender or UpdateComponent.MicrosoftUpdate;
                    var group = snapshot.Where(s => IsElevated(s.Component) && (s.Component is UpdateComponent.Defender or UpdateComponent.MicrosoftUpdate) == prepare).ToArray();
                    foreach (var member in group) handled.Add((member.Component, member.Id));
                    results.AddRange(await backend.ExecuteGroupAsync(group, cancellationToken).ConfigureAwait(false));
                }
                else
                {
                    results.Add(await backend.ExecuteAsync(step, cancellationToken).ConfigureAwait(false));
                    handled.Add((step.Component, step.Id));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch { results.Add(new(step, false, false, "Étape impossible. Aucune autre action pour ce composant.")); }
            if (cancellationToken.IsCancellationRequested)
                for (var index = 0; index < results.Count; index++)
                    if (!results[index].Message.Contains("Étape terminée après annulation.", StringComparison.Ordinal))
                        results[index] = results[index] with { Message = results[index].Message + " Étape terminée après annulation." };
            progress?.Report(new(results.Count, snapshot.Length, step.Title, cancellationToken.IsCancellationRequested ? "Étape terminée après annulation." : results.LastOrDefault()?.Message ?? "Étape terminée.") { StepId = step.Id, Component = step.Component });
        }
        results.AddRange(snapshot.Where(s => !handled.Contains((s.Component, s.Id)) && !results.Any(r => r.Step.Component == s.Component && r.Step.Id == s.Id)).Select(s => new UpdateSequenceStepResult(s, false, false, "Non lancée : annulation demandée.")));
        // Lectures seulement : même après annulation, aucune installation supplémentaire.
        var checks = new List<UpdateSourceVerification>();
        foreach (var component in snapshot.Select(s => s.Component).Distinct().Where(c => IsElevated(c)))
        {
            UpdateSourceVerification check;
            using var verificationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { check = await backend.VerifyAsync(component, verificationTimeout.Token).WaitAsync(verificationTimeout.Token).ConfigureAwait(false); }
            catch { check = new(component, "indisponible", "Contrôle final indisponible ou délai dépassé."); }
            var outcomes = results.Where(r => r.Step.Component == component).ToArray();
            if (outcomes.Length == 0 || outcomes.Any(r => r.Message.StartsWith("Non lancée", StringComparison.Ordinal)))
                check = new(component, "non lancée", check.Detail);
            else if (outcomes.Any(r => r.WasUnavailable)) check = new(component, "indisponible", check.Detail);
            else if (outcomes.Any(r => !r.Succeeded && !r.WasSkipped))
                check = new(component, outcomes.Any(IsSafetyBlocked) ? "bloquée" : "en erreur", check.Detail);
            // UpdateScanMethod ne prouve jamais l'installation des UWP hors WinGet.
            else if (component == UpdateComponent.Store)
                check = new(component, "à vérifier", "à terminer dans le Microsoft Store");
            checks.Add(check with { Component = component });
        }
        var sources = new List<string>();
        if (results.Any(r => r.RebootRequired && r.Step.Component is UpdateComponent.Windows or UpdateComponent.Driver or UpdateComponent.WindowsFeatureUpgrade)) sources.Add("après Windows Update");
        if (results.Any(r => r.RebootRequired && r.Step.Component is not UpdateComponent.Windows and not UpdateComponent.Driver and not UpdateComponent.WindowsFeatureUpgrade)) sources.Add("après une installation");
        try
        {
            if (rebootProbe is not null) sources.AddRange(rebootProbe.Read().Sources.Select(source => "signalé par " + source));
            else checks.Add(new(UpdateComponent.PendingReboot, "indisponible", "Sonde de redémarrage absente."));
        }
        catch { sources.Add("Lecture du redémarrage indisponible"); }
        var reboot = sources.Count > 0;
        var verdict = "Redémarrage nécessaire : " + (reboot ? "oui, " + string.Join(" ; ", sources.Distinct()) : "non") + ".";
        results.Add(new(new("pending-reboot", "Vérifier le redémarrage en attente", UpdateComponent.PendingReboot), true, reboot, verdict));
        var message = cancellationToken.IsCancellationRequested ? "Séquence annulée entre deux étapes." : results.Any(r => !r.Succeeded && !r.Step.IsInformational && !r.WasSkipped && !r.WasUnavailable) ? "Séquence terminée avec erreurs ; consultez les étapes." : "Séquence terminée.";
        return new(false, cancellationToken.IsCancellationRequested, reboot, results, message) { Verifications = checks };
    }
}
