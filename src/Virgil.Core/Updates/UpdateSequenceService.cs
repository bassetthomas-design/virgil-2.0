using System.Text.Json;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Updates;

public enum UpdateComponent { Windows, Defender, Driver, Winget, Launcher }
public sealed record UpdateSequenceStep(string Id, string Title, UpdateComponent Component, UpdateItem? Application = null);
public sealed record UpdateSequenceProgress(int Current, int Total, string Title, string Status);
public sealed record UpdateSequenceStepResult(UpdateSequenceStep Step, bool Succeeded, bool RebootRequired, string Message);
public sealed record UpdateSequenceResult(bool WasRefused, bool WasCancelled, bool RebootRequired, IReadOnlyList<UpdateSequenceStepResult> Steps, string Message);
public interface IUpdateSequenceBackend
{
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
        steps.AddRange(new[] { "Steam", "Epic Games", "Ubisoft Connect" }.Select(s => new UpdateSequenceStep(s, s + " : mise à jour depuis le lanceur", UpdateComponent.Launcher)));
        return steps;
    }
    public ActionPreview CreatePreview(IReadOnlyList<UpdateSequenceStep> selected) => new(
        "updates.sequence", "Tout mettre à jour", RiskLevel.Sensitive,
        selected.Select(s => s.Title).ToArray(),
        new Dictionary<string, string> { ["Steps"] = JsonSerializer.Serialize(selected) },
        ["Installer les composants cochés dans l’ordre présenté.", "Un redémarrage peut être nécessaire ; il restera manuel.", "Les pilotes et Windows exigent un point de restauration vérifié."],
        selected.Any(s => s.Component is UpdateComponent.Windows or UpdateComponent.Driver or UpdateComponent.Defender), true, Reversibility.Partial, ["Point de restauration avant Windows et les pilotes"]);
    public async Task<UpdateSequenceResult> ExecuteAsync(IReadOnlyList<UpdateSequenceStep> selected, ConfirmedPlan? plan, IProgress<UpdateSequenceProgress>? progress, CancellationToken cancellationToken)
    {
        // Freeze the entire selection before hashing and before the first effect.
        var snapshot = selected.Select(s => s with { Application = s.Application is null ? null : s.Application with { CommandPreview = s.Application.CommandPreview is null ? null : s.Application.CommandPreview with { Arguments = s.Application.CommandPreview.Arguments.ToArray() } } }).ToArray();
        var validation = gate.TryConsume(plan, CreatePreview(snapshot));
        if (!validation.IsAccepted) return new(true, false, false, [], validation.Message);
        backend.BeginExecution(Guid.NewGuid().ToString("D"));
        var results = new List<UpdateSequenceStepResult>();
        foreach (var step in snapshot)
        {
            if (cancellationToken.IsCancellationRequested) return new(false, true, results.Any(r => r.RebootRequired), results, "Séquence annulée entre deux étapes.");
            progress?.Report(new(results.Count + 1, snapshot.Length, step.Title, "En cours"));
            UpdateSequenceStepResult result;
            try { result = await backend.ExecuteAsync(step, CancellationToken.None).ConfigureAwait(false); }
            catch { result = new(step, false, false, "Étape impossible. Aucune autre action pour ce composant."); }
            results.Add(result);
            progress?.Report(new(results.Count, snapshot.Length, step.Title, result.Message));
        }
        var message = cancellationToken.IsCancellationRequested ? "Séquence annulée." :
            results.Any(r => !r.Succeeded) ? "Séquence terminée avec erreurs ; consultez les étapes." : "Séquence terminée.";
        return new(false, cancellationToken.IsCancellationRequested, results.Any(r => r.RebootRequired), results, message);
    }
}
