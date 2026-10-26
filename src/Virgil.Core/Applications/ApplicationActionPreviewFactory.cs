using System.Globalization;
using Virgil.Contracts.Actions;
using Virgil.Contracts.Applications;
using Virgil.Core.Actions;

namespace Virgil.Core.Applications;

public static class ApplicationActionPreviewFactory
{
    public static ActionPreview Create(ApplicationUninstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var application = plan.Application;
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Method"] = plan.Method.ToString(),
            ["Executable"] = plan.Validation.Executable ?? string.Empty,
            ["WingetId"] = application.WingetId ?? string.Empty,
            ["ArgumentCount"] = plan.Validation.Arguments.Count.ToString(CultureInfo.InvariantCulture)
        };
        for (var i = 0; i < plan.Validation.Arguments.Count; i++)
            parameters["Argument:" + i.ToString(CultureInfo.InvariantCulture)] = plan.Validation.Arguments[i];

        return new ActionPreview("applications.uninstall", application.DisplayName,
            application.RiskLevel.ToRiskLevel(), [application.Id, application.DisplayName], parameters,
            ["Lancement du desinstalleur officiel valide.", "Analyse lecture seule des restes."],
            false, false, Reversibility.None, []);
    }
}

public static class ApplicationBatchPreviewFactory
{
    public static ActionPreview Create(IReadOnlyList<ApplicationUninstallPlan> plans)
    {
        var previews = plans.Select(ApplicationActionPreviewFactory.Create).ToArray();
        return new ActionPreview("applications.uninstall-batch", "Désinstaller la sélection",
            previews.Length == 0 ? RiskLevel.Critical : previews.Max(item => item.RiskLevel),
            plans.Select(item => item.Application.Id).ToArray(),
            previews.Select((preview, index) => new KeyValuePair<string, string>(index.ToString(CultureInfo.InvariantCulture),
                ActionPreviewHasher.Hash(preview))).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            plans.Select(item => item.Application.DisplayName + " : " + item.Validation.Executable + " " + string.Join(" ", item.Validation.Arguments))
                .Append("Désinstalleurs officiels séquentiels ; aucune suppression directe de dossier ni de reste.").ToArray(),
            false, false, Reversibility.None, []);
    }
}
