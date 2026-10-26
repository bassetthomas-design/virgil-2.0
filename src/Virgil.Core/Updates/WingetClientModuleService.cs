using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Processes;

namespace Virgil.Core.Updates;

/// <summary>Optional module installation, strictly after an explicit preview-bound consent.</summary>
public sealed class WingetClientModuleService(IProcessRunner runner, IConfirmedPlanGate gate)
{
    public static ActionPreview CreatePreview() => new("updates.install-winget-client", "Installer le module WinGet", RiskLevel.Sensitive,
        ["Microsoft.WinGet.Client"], new Dictionary<string, string> { ["Scope"] = "CurrentUser", ["Repository"] = "PSGallery" },
        ["Télécharger et installer le module PowerShell Microsoft.WinGet.Client pour le compte actuel."], false, false, Reversibility.Partial, []);

    public async Task<bool> InstallAsync(ConfirmedPlan? plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!gate.TryConsume(plan, CreatePreview()).IsAccepted) return false;
        var result = await runner.RunAsync(new ProcessRunRequest("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference='Stop'; Install-Module -Name Microsoft.WinGet.Client -Repository PSGallery -Scope CurrentUser -Force"], TimeSpan.FromMinutes(5)), cancellationToken).ConfigureAwait(false);
        if (result.Cancelled) throw new OperationCanceledException(cancellationToken);
        return result.ExitCode == 0 && !result.TimedOut && string.IsNullOrWhiteSpace(result.LaunchError);
    }
}
