using Virgil.Contracts.Actions;
using Virgil.Core.Actions;

namespace Virgil.Core.Updates;

/// <summary>Optional module installation, strictly after an explicit preview-bound consent.</summary>
public sealed class WingetClientModuleService(IWingetClientModuleInstaller installer, IConfirmedPlanGate gate)
{
    public static ActionPreview CreatePreview() => new("updates.install-winget-client", "Installer le module WinGet", RiskLevel.Sensitive,
        ["Microsoft.WinGet.Client"], new Dictionary<string, string> { ["Scope"] = "CurrentUser", ["Repository"] = "PSGallery" },
        ["Télécharger et installer le module PowerShell Microsoft.WinGet.Client pour le compte actuel."], false, false, Reversibility.Partial, []);

    public async Task<bool> InstallAsync(ConfirmedPlan? plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!gate.TryConsume(plan, CreatePreview()).IsAccepted) return false;
        return await installer.InstallAsync(cancellationToken).ConfigureAwait(false);
    }
}
