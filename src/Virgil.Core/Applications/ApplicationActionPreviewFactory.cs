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
