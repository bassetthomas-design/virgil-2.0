using System.Globalization;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Updates;

public static class UpdateActionPreviewFactory
{
    public static ActionPreview Create(UpdateItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var command = item.CommandPreview;
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["InstalledVersion"] = item.InstalledVersion,
            ["AvailableVersion"] = item.AvailableVersion,
            ["Source"] = item.Source.ToString(),
            ["Executable"] = command?.ExecutablePath ?? string.Empty,
            ["ArgumentCount"] = (command?.Arguments.Count ?? 0).ToString(CultureInfo.InvariantCulture)
        };
        if (command is not null)
            for (var i = 0; i < command.Arguments.Count; i++)
                parameters["Argument:" + i.ToString(CultureInfo.InvariantCulture)] = command.Arguments[i];
        // Hash the scanned command, independently of runtime WinGet capability detection.
        return new ActionPreview("updates.winget-upgrade", item.Name, item.RiskLevel.ToRiskLevel(),
            [item.Id, item.Name], parameters, ["Installation de la mise à jour ciblée par --id exact."],
            false, false, Reversibility.None, []);
    }
}
