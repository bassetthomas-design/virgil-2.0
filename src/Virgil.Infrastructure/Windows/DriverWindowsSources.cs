using Virgil.Contracts;
using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class DriverWindowsInventory(IProcessRunner runner) : IDriverInventoryReader
{
    public Task<DriverInventoryReport> ReadAsync(CancellationToken token) => new DriverInformationService(runner).InspectAsync(token);
}

public sealed class DriverPackageWindowsSource(IProcessRunner runner, WingetAvailabilityService availability) : IDriverPackageSource
{
    private IReadOnlyList<DriverPackageOffer> _offers = [];
    public void SetInventory(IReadOnlyList<UpdateItem> items) => _offers = items
        .Where(item => DriverOfficialPackageCatalog.IsDriverPackage(item) || DriverOfficialPackageCatalog.IsFirmware(item))
        .Select(item => new DriverPackageOffer(item with { }, item.Id == DriverOfficialPackageCatalog.AmdCloud,
            item.Id == DriverOfficialPackageCatalog.IntelAssistant ? "l'outil du fabricant n'installe pas les pilotes tout seul"
                : "Ce paquet ne figure pas dans la liste officielle des pilotes installables sans intervention.")).ToArray();
    public Task<IReadOnlyList<DriverPackageOffer>> ScanAsync(CancellationToken token) => Task.FromResult(_offers);
    public async Task<UpdateSequenceStepResult> InstallAsync(UpdateSequenceStep step, CancellationToken token)
    {
        var offer = _offers.FirstOrDefault(o => o.Item == step.Application);
        if (offer is null || !DriverOfficialPackageCatalog.CanInstall(offer) || !WindowsUpdateSequenceBackend.IsSafeVersion(offer.Item.AvailableVersion))
            return new(step, false, false, "Installation bloquée : paquet officiel ou version non vérifié.") { VerifiedStatus = "bloquée" };
        var source = await availability.DetectAsync(token).ConfigureAwait(false);
        var capabilities = WingetAvailabilityService.GetCapabilities(source.Version);
        if (!source.IsAvailable || source.ExecutablePath is null || !capabilities.SupportsDisableInteractivity)
            return new(step, false, false, "Installation silencieuse indisponible sur ce PC.") { VerifiedStatus = "en erreur" };
        var args = WingetUpdateExecutionService.BuildUpgradeArguments(offer.Item.Id, capabilities).ToList();
        args.AddRange(["--version", offer.Item.AvailableVersion, "--source", "winget", "--silent"]);
        token.ThrowIfCancellationRequested();
        var result = await runner.RunAsync(new(source.ExecutablePath, args, TimeSpan.FromMinutes(20)), CancellationToken.None).ConfigureAwait(false);
        var success = result.ExitCode is 0 or 3010 && !result.TimedOut && !result.Cancelled && result.LaunchError is null;
        return new(step, success, result.ExitCode == 3010, success ? "Paquet officiel installé." : "Installation du paquet officiel impossible.")
            { VerifiedStatus = success ? "mis à jour" : "en erreur" };
    }
}
