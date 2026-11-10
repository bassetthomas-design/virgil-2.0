using Virgil.Contracts;
using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

// La table de la voie logicielle fournit des cibles applicables, mais ne certifie pas
// l'exhaustivité de --all (langue, paquets inconnus/épinglés, tables secondaires).
public sealed class DriverFinalPassWindowsReader(IProcessRunner runner, WingetAvailabilityService availability)
{
    public async Task<DriverFinalPassInventory> ReadAsync(CancellationToken token)
    {
        var source = await availability.DetectAsync(token).ConfigureAwait(false);
        if (!source.IsAvailable || source.ExecutablePath is null) return new(false, [], "Liste des logiciels indisponible.");
        var args = WingetUpdateScanService.BuildScanArguments(WingetAvailabilityService.GetCapabilities(source.Version));
        var result = await runner.RunAsync(new(source.ExecutablePath, args, TimeSpan.FromSeconds(120)), token).ConfigureAwait(false);
        if (result.Cancelled) throw new OperationCanceledException(token);
        if (result.ExitCode != 0 || result.TimedOut || result.LaunchError is not null)
            return new(false, [], "Lecture des logiciels incomplète. Aucune installation groupée lancée.");
        var items = WingetUpgradeParser.Parse(result.StandardOutput).Items
            .Where(item => !item.IsIdTruncated && WindowsUpdateSequenceBackend.IsSafePackageId(item.Id)
                && WindowsUpdateSequenceBackend.IsSafeVersion(item.AvailableVersion)).ToArray();
        return new(false, items, "Liste globale non prouvée : seuls les logiciels identifiés sont traités un par un.");
    }
}
