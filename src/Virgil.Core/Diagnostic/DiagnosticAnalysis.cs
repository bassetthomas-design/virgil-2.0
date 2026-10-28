using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Contracts.Diagnostic;

namespace Virgil.Core.Diagnostic;

public sealed record WindowsErrorSummary(int Count, IReadOnlyList<string> MainSources);
public sealed record DeviceProblem(string Name, uint Status, uint ProblemCode);
public sealed record StartupEntry(string Name, string Source, DiagnosticValue<string> Impact);
public sealed record DiagnosticSignals(DiagnosticValue<WindowsErrorSummary> Errors,
    DiagnosticValue<IReadOnlyList<DeviceProblem>> Devices, DiagnosticValue<IReadOnlyList<StartupEntry>> Startup,
    DiagnosticValue<IReadOnlyList<ProcessResourceInfo>> Processes)
{
    public static DiagnosticSignals Unavailable { get; } = new(
        DiagnosticValue<WindowsErrorSummary>.Unavailable("Journal System indisponible."),
        DiagnosticValue<IReadOnlyList<DeviceProblem>>.Unavailable("Périphériques indisponibles."),
        DiagnosticValue<IReadOnlyList<StartupEntry>>.Unavailable("Démarrage indisponible."),
        DiagnosticValue<IReadOnlyList<ProcessResourceInfo>>.Unavailable("Identité des processus indisponible."));
}
public interface IDiagnosticSignalSource
{
    Task<DiagnosticSignals> ReadAsync(CancellationToken cancellationToken);
}
public enum DiagnosticSuggestion { None, Cleanup, Updates, Close, StartupLater }
public sealed record DiagnosticIssue(string Id, string Title, string Description, int Severity, int Impact,
    string Category, DiagnosticSuggestion Suggestion = DiagnosticSuggestion.None, ProcessResourceInfo? Process = null);
public sealed record HealthScore(int Value, bool IsPartial)
{
    public int FailedReadCount { get; init; }
}

/// <summary>Analyse pure. Poids maximaux par catégorie : CPU 20, GPU 20, disque 30,
/// espace 10, erreurs 10, pilotes 10, processus 5. Le score est borné entre 0 et 100.
/// Une catégorie indisponible ne produit aucune pénalité ; le démarrage non mesuré vaut 0.</summary>
public static class DiagnosticAnalyzer
{
    private static readonly HashSet<string> Launchers = new(StringComparer.OrdinalIgnoreCase)
        { "steam", "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper", "Battle.net" };
    public static bool IsLauncher(string name) => Launchers.Contains(name);
    public static DiagnosticFindings Analyze(DiagnosticSnapshot snapshot, DiagnosticSignals signals)
    {
        var issues = new List<DiagnosticIssue>();
        var missing = new List<string>();
        void Missing(string name, string? reason) => missing.Add(name + " : " + reason);
        void Temperature(string id, string name, DiagnosticValue<double> value, string category)
        {
            if (!value.IsAvailable) { Missing(name, value.Reason); return; }
            if (value.Value >= 90) issues.Add(new(id, name + " : ça chauffe", "Température d'au moins 90 °C. Vérifiez la ventilation et réduisez la charge.", 3, 20, category));
        }
        Temperature("cpu.hot", "Processeur", snapshot.Cpu.TemperatureCelsius, "cpu");
        if (snapshot.Gpu.Count == 0) Missing("Température GPU", "aucun capteur exposé.");
        foreach (var gpu in snapshot.Gpu) Temperature("gpu.hot." + gpu.Id, gpu.Id, gpu.TemperatureCelsius, "gpu");
        var physical = snapshot.Disks.Where(d => d.Kind == "Disque physique").ToArray();
        if (physical.Length == 0) Missing("Prédiction de panne", "aucun disque physique observé.");
        foreach (var disk in snapshot.Disks)
        {
            if (disk.Kind == "Disque physique")
            {
                if (!disk.Health.IsAvailable) Missing(disk.Id, disk.Health.Reason);
                else if (disk.Health.Value == "Défaillance prédite par Windows") issues.Add(new("disk.failure." + disk.Id,
                    "Disque : sauvegardez maintenant", disk.Id + " : Windows prédit une défaillance. Sauvegardez vos fichiers et faites contrôler le disque.", 4, 30, "disk"));
            }
            if (disk.Kind != "Volume") continue;
            if (!disk.TotalBytes.IsAvailable || !disk.FreeBytes.IsAvailable || disk.TotalBytes.Value <= 0)
            { Missing("Espace " + disk.Id, disk.FreeBytes.Reason ?? disk.TotalBytes.Reason ?? "capacité invalide."); continue; }
            if (disk.FreeBytes.Value < 10L * 1024 * 1024 * 1024 || disk.FreeBytes.Value / disk.TotalBytes.Value < .1)
                issues.Add(new("space." + disk.Id, disk.Id + " : un peu à l'étroit", "Moins de 10 % ou de 10 Go libres. Ouvrez Nettoyage pour examiner les propositions, sans sélection imposée.", 2, 10, "space", DiagnosticSuggestion.Cleanup));
        }
        if (!snapshot.Disks.Any(d => d.Kind == "Volume")) Missing("Espace libre", "aucun volume observé.");
        if (!signals.Errors.IsAvailable) Missing("Erreurs Windows", signals.Errors.Reason);
        else if (signals.Errors.Value.Count > 0) issues.Add(new("windows.errors", "Windows a quelques réclamations",
            $"{signals.Errors.Value.Count} événements Critique/Erreur dans System sur les dernières 24 heures. Sources principales : {string.Join(", ", signals.Errors.Value.MainSources)}. Aucun message personnel collecté.", 2, 10, "errors", DiagnosticSuggestion.Updates));
        if (!signals.Devices.IsAvailable) Missing("Pilotes", signals.Devices.Reason);
        else foreach (var device in signals.Devices.Value) issues.Add(new("device." + device.Name + "." + device.ProblemCode,
            device.Name + " : périphérique en échec", $"Code problème {device.ProblemCode}, statut {device.Status}. Consultez Mises à jour avant toute intervention.", 3, 10, "devices", DiagnosticSuggestion.Updates));
        if (!signals.Processes.IsAvailable) Missing("Processus", signals.Processes.Reason);
        else foreach (var p in snapshot.Processes.Where(p => IsLauncher(p.Name)))
        {
            if (!p.CpuPercent.IsAvailable || !p.WorkingSetBytes.IsAvailable) Missing(p.Name, "consommation indisponible.");
            var target = signals.Processes.Value.FirstOrDefault(t => t.ProcessId == p.Pid && t.Name == p.Name && p.Path.IsAvailable && t.Path == p.Path.Value);
            // La politique existante conserve ses exclusions, y compris les processus sans fenêtre.
            if (target is null || target.IsCriticalSystemProcess || !target.CanCloseGracefully || !target.CanForceClose ||
                target.StartedAt is null || string.IsNullOrWhiteSpace(target.Path) || target.Name.Contains("virgil", StringComparison.OrdinalIgnoreCase)) continue;
            if ((p.CpuPercent.IsAvailable && p.CpuPercent.Value >= 20) || (p.WorkingSetBytes.IsAvailable && p.WorkingSetBytes.Value >= 512L * 1024 * 1024))
                issues.Add(new("process." + p.Pid, p.Name + " : lanceur gourmand", "Ce lanceur utilise au moins 20 % CPU ou 512 Mo de RAM. Si vous ne l'utilisez pas, vous pouvez proposer sa fermeture. Une activité utile reste possible.", 1, 5, "process", DiagnosticSuggestion.Close, target));
        }
        // Le démarrage est informatif : aucun de ses signaux ne pondère la note.
        var ordered = issues.OrderByDescending(i => i.Severity).ThenByDescending(i => i.Impact).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
        var penalty = ordered.GroupBy(i => i.Category).Sum(g => g.Max(i => i.Impact));
        return new DiagnosticFindings(snapshot, signals, ordered, new HealthScore(Math.Clamp(100 - penalty, 0, 100), missing.Count > 0), missing.Distinct().ToArray());
    }
}
