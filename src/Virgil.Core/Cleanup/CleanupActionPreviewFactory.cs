using System.Globalization;
using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Cleanup;

public static class CleanupActionPreviewFactory
{
    public static ActionPreview Create(CleanupZonePreview preview)
    {
        var definition = preview.Definition;
        var quarantine = definition.Id != CleanupZoneId.RecycleBin;
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ZoneId"] = definition.Id.ToString(),
            ["GeneratedAt"] = preview.GeneratedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        };
        var eligible = preview.Candidates.Where(candidate => candidate.IsEligible).ToList();
        if (definition.Id == CleanupZoneId.RecycleBin)
        {
            parameters["ItemCount"] = preview.EligibleFileCount.ToString(CultureInfo.InvariantCulture);
            parameters["EligibleBytes"] = preview.EligibleBytes.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            parameters["QuarantineRetentionDays"] = preview.QuarantineRetentionDays.ToString(CultureInfo.InvariantCulture);
            parameters["EligibleFileCount"] = preview.EligibleFileCount.ToString(CultureInfo.InvariantCulture);
            parameters["EligibleBytes"] = preview.EligibleBytes.ToString(CultureInfo.InvariantCulture);
            for (var i = 0; i < eligible.Count; i++)
            {
                var index = i.ToString(CultureInfo.InvariantCulture);
                parameters["Size:" + index] = eligible[i].SizeBytes.ToString(CultureInfo.InvariantCulture);
                parameters["LastWriteUtc:" + index] = eligible[i].LastWriteTimeUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }
        }
        // Advanced Medium zones and the recycle bin retain their independent reinforced requirement (0e-2).
        var risk = definition.RiskLevel.ToRiskLevel();
        if (definition.RequiresReinforcedConfirmation && risk < RiskLevel.Sensitive)
            risk = RiskLevel.Sensitive;
        return new ActionPreview("cleanup.zone." + definition.Id.ToString().ToLowerInvariant(),
            definition.DisplayName, risk, eligible.Select(candidate => candidate.FullPath).ToArray(),
            parameters, quarantine ? [$"Conservé {preview.QuarantineRetentionDays} jours en quarantaine, puis effacement définitif proposé avec confirmation. Restauration vérifiée possible."] : [definition.Effect],
            definition.RequiresElevation, false, quarantine ? Reversibility.Full : Reversibility.None, []);
    }
}
