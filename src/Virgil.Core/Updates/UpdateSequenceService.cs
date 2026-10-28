using System.Text.Json;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Updates;

public enum UpdateComponent { Windows, Defender, Driver, Winget, Launcher }
public sealed record UpdateSequenceStep(string Id, string Title, UpdateComponent Component, UpdateItem? Application = null)
{
    public bool IsInformational => Component == UpdateComponent.Launcher;
}
public sealed record UpdateSequenceProgress(int Current, int Total, string Title, string Status);
public sealed record UpdateSequenceStepResult(UpdateSequenceStep Step, bool Succeeded, bool RebootRequired, string Message);
public sealed record UpdateSequenceResult(bool WasRefused, bool WasCancelled, bool RebootRequired, IReadOnlyList<UpdateSequenceStepResult> Steps, string Message)
{
    public int SuccessfulCount => Steps.Count(step => !step.Step.IsInformational && step.Succeeded);
    public bool HasFailures => Steps.Any(step => !step.Step.IsInformational && !step.Succeeded);
}
public interface IUpdateSequenceBackend
{
    Task<IReadOnlyList<string>> DetectLaunchersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
    void BeginExecution(string restoreSessionId) { }
    Task<IReadOnlyList<UpdateSequenceStep>> ScanDriversAsync(CancellationToken cancellationToken);
    Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken cancellationToken);
}
public interface IUpdateSequenceService
{
    Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> apps, CancellationToken cancellationToken);
    ActionPreview CreatePreview(IReadOnlyList<UpdateSequenceStep> selected);
    Task<UpdateSequenceResult> ExecuteAsync(IReadOnlyList<UpdateSequenceStep> selected, ConfirmedPlan? plan, IProgress<UpdateSequenceProgress>? progress, CancellationToken cancellationToken);
}
public sealed class UpdateSequenceService(IUpdateSequenceBackend backend, IConfirmedPlanGate gate) : IUpdateSequenceService
{
    public async Task<IReadOnlyList<UpdateSequenceStep>> ScanAsync(IReadOnlyList<UpdateItem> apps, CancellationToken cancellationToken)
    {
        var steps = new List<UpdateSequenceStep>
        {
            new("windows", "Windows : qualité et sécurité", UpdateComponent.Windows),
            new("defender", "Signatures Microsoft Defender", UpdateComponent.Defender)
        };
        steps.AddRange(await backend.ScanDriversAsync(cancellationToken).ConfigureAwait(false));
        steps.AddRange(apps.Where(a => a.Status == UpdateItemStatus.Available).Select(a => new UpdateSequenceStep(a.Id, a.Name, UpdateComponent.Winget, a)));
        var launchers = LauncherCatalog.Detect(await backend.DetectLaunchersAsync(cancellationToken).ConfigureAwait(false));
        steps.AddRange(launchers.Select(name => new UpdateSequenceStep(name, name + " : mises à jour à faire depuis le lanceur", UpdateComponent.Launcher)));
        return steps;
    }
    public ActionPreview CreatePreview(IReadOnlyList<UpdateSequenceStep> selected)
    {
        selected = selected.Where(step => !step.IsInformational).ToArray();
        return new(
        "updates.sequence", "Tout mettre à jour", RiskLevel.Sensitive,
        selected.Select(s => s.Title).ToArray(),
        new Dictionary<string, string> { ["Steps"] = JsonSerializer.Serialize(selected) },
        ["Installer les composants cochés dans l’ordre présenté.", "Un redémarrage peut être nécessaire ; il restera manuel.", "Les pilotes et Windows exigent un point de restauration vérifié."],
        selected.Any(s => s.Component is UpdateComponent.Windows or UpdateComponent.Driver or UpdateComponent.Defender), true, Reversibility.Partial, ["Point de restauration avant Windows et les pilotes"]);
    }
    public async Task<UpdateSequenceResult> ExecuteAsync(IReadOnlyList<UpdateSequenceStep> selected, ConfirmedPlan? plan, IProgress<UpdateSequenceProgress>? progress, CancellationToken cancellationToken)
    {
        // Freeze the entire selection before hashing and before the first effect.
        var snapshot = selected.Where(step => !step.IsInformational).Select(s => s with { Application = s.Application is null ? null : s.Application with { CommandPreview = s.Application.CommandPreview is null ? null : s.Application.CommandPreview with { Arguments = s.Application.CommandPreview.Arguments.ToArray() } } }).ToArray();
        var validation = gate.TryConsume(plan, CreatePreview(snapshot));
        if (!validation.IsAccepted) return new(true, false, false, [], validation.Message);
        backend.BeginExecution(Guid.NewGuid().ToString("D"));
        var results = selected.Where(step => step.IsInformational).DistinctBy(step => step.Id)
            .Select(step => new UpdateSequenceStepResult(step, false, false, "À faire dans le lanceur")).ToList();
        var completed = 0;
        foreach (var step in snapshot)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.AddRange(snapshot.Skip(completed).Select(pending => new UpdateSequenceStepResult(pending, false, false, "Non lancée : annulation demandée.")));
                return new(false, true, results.Any(r => r.RebootRequired), results, "Séquence annulée entre deux étapes.");
            }
            progress?.Report(new(completed + 1, snapshot.Length, step.Title, "En cours"));
            UpdateSequenceStepResult result;
            try { result = await backend.ExecuteAsync(step, cancellationToken).ConfigureAwait(false); }
            catch { result = new(step, false, false, "Étape impossible. Aucune autre action pour ce composant."); }
            if (cancellationToken.IsCancellationRequested) result = result with { Message = result.Message + " Étape terminée après annulation." };
            results.Add(result);
            completed++;
            progress?.Report(new(completed, snapshot.Length, step.Title, result.Message));
        }
        var message = cancellationToken.IsCancellationRequested ? "Séquence annulée." :
            results.Any(r => !r.Step.IsInformational && !r.Succeeded) ? "Séquence terminée avec erreurs ; consultez les étapes." : "Séquence terminée.";
        return new(false, cancellationToken.IsCancellationRequested, results.Any(r => r.RebootRequired), results, message);
    }
}
