using System.Globalization;
using Virgil.Contracts;
using Virgil.Contracts.Actions;

namespace Virgil.Core.Resources;

public static class ProcessActionPreviewFactory
{
    public static ActionPreview Create(ProcessActionKind action, ProcessResourceInfo? target)
    {
        var (id, title, risk, effect) = action switch
        {
            ProcessActionKind.CloseMainWindow => ("resources.close", "Fermeture propre", RiskLevel.Low, "Demande de fermeture de la fenêtre principale."),
            ProcessActionKind.KillProcess => ("resources.kill", "Fermeture forcée", RiskLevel.Sensitive, "Fermeture forcée : perte possible de données non sauvegardées."),
            ProcessActionKind.RestartExplorer => ("resources.restart-explorer", "Relance Explorer", RiskLevel.Low, "Relance du bureau et de la barre des tâches."),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        // Only stable identity data, never CPU or memory measurements.
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (target is not null)
        {
            parameters["ProcessId"] = target.ProcessId.ToString(CultureInfo.InvariantCulture);
            parameters["Name"] = target.Name;
            parameters["Path"] = target.Path ?? string.Empty;
            parameters["StartedAt"] = target.StartedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
        }
        return new ActionPreview(id, title, risk,
            target is null ? ["Explorer Windows"] : [parameters["ProcessId"], target.Name, target.Path ?? string.Empty],
            parameters, [effect], false, false, Reversibility.None, []);
    }
}
