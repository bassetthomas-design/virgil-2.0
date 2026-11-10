using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class D1bisCSequenceTests
{
    // Identifiants réels, relus dans microsoft/winget-pkgs à db1e2f3a72d88b016050a00b5071c94a2ef70243.
    internal const string AmdId = "AMD.AMDSoftwareCloudEdition";
    internal const string AmdName = "AMD Software: Cloud Edition";
    internal const string AssistantId = "Intel.IntelDriverAndSupportAssistant";
    internal const string FirmwareId = "Nvidia.UEFIFirmwareUpdater40xx";
    internal const string Unsupported = "non pris en charge automatiquement";

    [Fact]
    public async Task Partial_sequence_keeps_each_driver_result_and_reads_inventory_and_bios()
    {
        var backend = new Backend();
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(backend, authority);
        var scanned = await service.ScanAsync([], default);
        var selected = scanned.Where(s => s.Component == UpdateComponent.Driver).ToArray();
        Assert.Contains(selected, s => s.Id == "drivers");
        Assert.Contains(selected, s => s.Id == "WinGet:" + AmdId && s.Title == AmdName);
        var result = await Run(service, authority, selected);
        Assert.False(result.WasRefused);
        Assert.Equal("mis à jour", Card(result, "WU:first").VerifiedStatus);
        Assert.Equal("reste à installer", Card(result, "WU:second").VerifiedStatus);
        Assert.Equal("mis à jour", Card(result, "WinGet:" + AmdId).VerifiedStatus);
        Assert.Equal(Unsupported, Card(result, "Provider:Autre fabricant").VerifiedStatus);
        var bios = Card(result, "BIOS");
        Assert.Equal(Unsupported, bios.VerifiedStatus);
        Assert.Contains("1.2.3", bios.Step.Title + bios.Message);
        Assert.Contains("Virgil n'installe jamais le BIOS", bios.Message);
        Assert.Equal(1, backend.InventoryReads);
        Assert.Equal(1, backend.BiosReads);
        Assert.Equal(new[] { AmdId }, backend.InstalledPackages);
        var source = Assert.Single(result.Verifications, c => c.Component == UpdateComponent.Driver);
        Assert.False(source.IsCurrent);
        Assert.Equal("reste à installer", source.Status);
        Assert.Contains("1 reste à installer", source.Detail);
        Assert.DoesNotContain("0 ", source.Detail);
    }

    [Fact]
    public async Task A_failed_driver_does_not_relabel_a_successful_driver_or_hide_a_remaining_driver()
    {
        var backend = new Backend { AddFailure = true };
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(backend, authority);
        var selected = (await service.ScanAsync([], default)).Where(s => s.Component == UpdateComponent.Driver).ToArray();
        var result = await Run(service, authority, selected);
        Assert.Equal("mis à jour", Card(result, "WU:first").VerifiedStatus);
        Assert.Equal("reste à installer", Card(result, "WU:second").VerifiedStatus);
        var failed = Card(result, "WU:failed");
        Assert.Equal("en erreur", failed.VerifiedStatus);
        Assert.False(failed.Succeeded);
        Assert.DoesNotContain("terminé", failed.Message, StringComparison.OrdinalIgnoreCase);
        var source = Assert.Single(result.Verifications, c => c.Component == UpdateComponent.Driver);
        Assert.Equal("en erreur", source.Status);
        Assert.Contains("1 en erreur", source.Detail);
        Assert.Contains("1 reste à installer", source.Detail);
        Assert.DoesNotContain("0 ", source.Detail);
    }

    [Fact]
    public async Task Empty_windows_update_has_one_current_source_card_and_no_invented_package()
    {
        var backend = new Backend { Empty = true };
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(backend, authority);
        var steps = await service.ScanAsync([], default);
        Assert.DoesNotContain(steps, s => s.Id.StartsWith("WinGet:", StringComparison.Ordinal));
        var result = await Run(service, authority, steps.Where(s => s.Id == "drivers").ToArray());
        var source = Assert.Single(result.Steps, r => r.Step.Id == "drivers");
        Assert.Equal("à jour", source.VerifiedStatus);
        Assert.DoesNotContain(result.Steps, r => r.Step.Id.StartsWith("WU:", StringComparison.Ordinal));
        Assert.Empty(backend.InstalledPackages);
    }

    [Theory]
    [InlineData("Intel.IntelDriverAndSupportAssistant", false)]
    [InlineData("Intel.IntelDriverAndSupportAssistant", true)]
    [InlineData("AMD.AMDSoftwareCloudEdition", false)]
    public async Task Interactive_or_unapproved_offer_is_never_installed(string id, bool claimsSilent)
    {
        var backend = new Backend { OfferedId = id, Silent = claimsSilent };
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(backend, authority);
        var steps = await service.ScanAsync([], default);
        var offer = Assert.Single(steps, s => s.Id == "WinGet:" + id);
        Assert.True(offer.IsInformational);
        var result = await Run(service, authority, steps.Where(s => s.Component == UpdateComponent.Driver).ToArray());
        Assert.Empty(backend.InstalledPackages);
        var card = Card(result, "WinGet:" + id);
        Assert.Equal(Unsupported, card.VerifiedStatus);
        Assert.Contains("n'installe pas les pilotes tout seul", card.Message);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Bios_and_detected_provider_are_information_only_under_one_confirmation()
    {
        var backend = new Backend { FirmwareOffer = true };
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(backend, authority);
        var steps = await service.ScanAsync([], default);
        var bios = Assert.Single(steps, s => s.Id == "BIOS");
        Assert.True(bios.IsInformational);
        var provider = Assert.Single(steps, s => s.Id == "Provider:Autre fabricant");
        Assert.True(provider.IsInformational);
        var preview = service.CreatePreview(steps);
        Assert.Contains(preview.Effects, e => e.Contains("Windows Update : pilotes", StringComparison.Ordinal));
        Assert.Contains(preview.Effects, e => e.Contains(AmdName, StringComparison.Ordinal));
        Assert.Contains(preview.Effects, e => e.Contains("BIOS : information seulement, jamais installé par Virgil", StringComparison.Ordinal));
        Assert.Contains(preview.Effects, e => e.Contains(Unsupported, StringComparison.Ordinal));
        Assert.DoesNotContain(preview.Effects, e => e.Contains("WinGet", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(preview.Effects, e => e.Contains("pnputil", StringComparison.OrdinalIgnoreCase));
        await Run(service, authority, steps.Where(s => s.Component == UpdateComponent.Driver).ToArray());
        Assert.DoesNotContain(backend.Executed, s => s.Id == "BIOS" || s.Id.StartsWith("Provider:", StringComparison.Ordinal));
        Assert.DoesNotContain(backend.Executed, s => s.Application?.Id == FirmwareId);
        Assert.DoesNotContain(backend.InstalledPackages, id => id == FirmwareId);
        Assert.Equal(1, backend.PackageScans);
        Assert.Equal(new[] { AmdId }, backend.InstalledPackages);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task No_plan_changed_plan_and_replayed_plan_start_no_additional_installation()
    {
        var backend = new Backend();
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(backend, authority);
        var scanned = await service.ScanAsync([], default);
        var selected = scanned.Where(s => s.Component == UpdateComponent.Driver).ToArray();
        Assert.True((await service.ExecuteAsync(selected, null, null, default)).WasRefused);
        Assert.Empty(backend.Executed);
        Assert.Empty(backend.InstalledPackages);
        var plan = authority.Issue(service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null));
        Assert.True((await service.ExecuteAsync(selected.Where(s => s.Id != "drivers").ToArray(), plan, null, default)).WasRefused);
        Assert.Empty(backend.Executed);
        Assert.Empty(backend.InstalledPackages);
        Assert.False((await service.ExecuteAsync(selected, plan, null, default)).WasRefused);
        var count = backend.Executed.Count;
        var packages = backend.InstalledPackages.Count;
        Assert.True((await service.ExecuteAsync(selected, plan, null, default)).WasRefused);
        Assert.Equal(count, backend.Executed.Count);
        Assert.Equal(packages, backend.InstalledPackages.Count);
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Restore_failure_blocks_official_package_as_well_as_windows_update()
    {
        var backend = new Backend { RestoreFails = true };
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(backend, authority);
        var selected = (await service.ScanAsync([], default)).Where(s => s.Component == UpdateComponent.Driver).ToArray();
        var result = await Run(service, authority, selected);
        Assert.Empty(backend.InstalledPackages);
        Assert.DoesNotContain(backend.Executed, s => s.Component == UpdateComponent.Driver);
        Assert.Contains(result.Steps, r => r.Step.Id == "WinGet:" + AmdId && UpdateSequenceService.IsSafetyBlocked(r));
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Inventory_and_bios_accept_an_access_that_rejects_every_effect()
    {
        var access = new ReadOnlyAccess();
        var inventory = await new DriverInformationService(access).InspectAsync(default);
        var bios = await new BiosWindowsReader(access).ReadAsync(default);
        Assert.Equal("NVIDIA", Assert.Single(inventory.Drivers).Provider);
        Assert.Equal("OEM", bios.Manufacturer);
        Assert.Equal("1.2.3", bios.Version);
        Assert.Equal(0, access.ForbiddenCalls);
        Assert.Equal(2, access.Reads);
        Assert.Equal(bios, await new BiosWindowsReader(access).ReadAsync(default));
        Assert.Equal(0, access.ForbiddenCalls);
    }

    private static Task<UpdateSequenceResult> Run(UpdateSequenceService service, ConfirmationService authority, IReadOnlyList<UpdateSequenceStep> selected) =>
        service.ExecuteAsync(selected, authority.Issue(service.CreatePreview(selected), new(ConfirmationLevel.Reinforced, null)), null, default);
    private static UpdateSequenceStepResult Card(UpdateSequenceResult result, string id) =>
        Assert.Single(result.Steps, r => r.Step.Component == UpdateComponent.Driver && r.Step.Id == id);

    private sealed class Backend : IUpdateSequenceBackend, IDriverSequenceBackend, IDriverInventoryReader, IBiosInformationReader, IDriverPackageSource
    {
        public bool Empty { get; init; }
        public bool RestoreFails { get; init; }
        public bool FirmwareOffer { get; init; }
        public bool AddFailure { get; init; }
        public string OfferedId { get; init; } = AmdId;
        public bool Silent { get; init; } = true;
        public int InventoryReads { get; private set; }
        public int BiosReads { get; private set; }
        public int PackageScans { get; private set; }
        public List<UpdateSequenceStep> Executed { get; } = [];
        public List<string> InstalledPackages { get; } = [];
        public IDriverInventoryReader DriverInventory => this;
        public IBiosInformationReader Bios => this;
        public IDriverPackageSource DriverPackages => this;
        Task<DriverInventoryReport> IDriverInventoryReader.ReadAsync(CancellationToken token)
        {
            InventoryReads++;
            return Task.FromResult(new DriverInventoryReport { WasAnalyzed = true, Drivers = Empty ? [] : [new() { Provider = "Autre fabricant", PublishedName = "oem1.inf" }] });
        }
        Task<BiosInformation> IBiosInformationReader.ReadAsync(CancellationToken token)
        { BiosReads++; return Task.FromResult(new BiosInformation("OEM", "1.2.3")); }
        public Task<IReadOnlyList<DriverPackageOffer>> ScanAsync(CancellationToken token)
        {
            PackageScans++;
            var offers = new List<DriverPackageOffer>();
            if (!Empty) offers.Add(new(new UpdateItem { Id = OfferedId, Name = OfferedId == AmdId ? AmdName : "Intel Driver & Support Assistant", AvailableVersion = "23.Q3", Source = UpdateSource.Winget }, Silent,
                "l'outil du fabricant n'installe pas les pilotes tout seul"));
            if (FirmwareOffer) offers.Add(new(new UpdateItem { Id = FirmwareId, Name = "NVIDIA UEFI Firmware Updater (40xx series)",
                AvailableVersion = "1.2.0", Source = UpdateSource.FirmwareInformation, RiskLevel = UpdateRiskLevel.CriticalInformationOnly }, true,
                "Virgil n'installe jamais le BIOS ni le firmware"));
            return Task.FromResult<IReadOnlyList<DriverPackageOffer>>(offers);
        }
        public Task<UpdateSequenceStepResult> InstallAsync(UpdateSequenceStep step, CancellationToken token)
        {
            Assert.False(token.IsCancellationRequested);
            Assert.NotNull(step.Application);
            Assert.Equal(AmdId, step.Application.Id);
            InstalledPackages.Add(step.Application.Id);
            return Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Paquet officiel installé.") { VerifiedStatus = "mis à jour" });
        }
        public Task<UpdateSourceVerification> VerifyAsync(UpdateComponent component, CancellationToken token) =>
            Task.FromResult(new UpdateSourceVerification(component, "à jour", "Contrôle global vide : ne doit pas écraser les cartes."));
        public Task<UpdateSequenceStepResult> ExecuteAsync(UpdateSequenceStep step, CancellationToken token)
        { Executed.Add(step); return Task.FromResult(new UpdateSequenceStepResult(step, true, false, "Étape exécutée.")); }
        public Task<IReadOnlyList<UpdateSequenceStepResult>> ExecuteGroupAsync(IReadOnlyList<UpdateSequenceStep> steps, CancellationToken token)
        {
            if (steps.All(s => s.Component is UpdateComponent.Defender or UpdateComponent.MicrosoftUpdate))
                return Task.FromResult<IReadOnlyList<UpdateSequenceStepResult>>([new(new("restore", "Point de restauration", UpdateComponent.Restore), !RestoreFails, false,
                    RestoreFails ? "Installation bloquée : point de restauration non vérifié." : "Point vérifié.") { RestorePointVerified = !RestoreFails }]);
            Executed.AddRange(steps);
            var results = steps.SelectMany(s => s.Id == "drivers"
                ? Empty ? new[] { new UpdateSequenceStepResult(s, true, false, "Aucun pilote proposé.") { VerifiedStatus = "à jour" } }
                    : new[] {
                        new UpdateSequenceStepResult(new("WU:first", "NVIDIA - Display - 32.0.15.7216", UpdateComponent.Driver), true, false, "Pilote installé.") { VerifiedStatus = "mis à jour" },
                        new UpdateSequenceStepResult(new("WU:second", "Intel - System - 10.1.1.38", UpdateComponent.Driver), true, false, "Ce pilote reste proposé.") { VerifiedStatus = "reste à installer" } }
                : new[] { new UpdateSequenceStepResult(s, true, false, "Étape exécutée.") }).ToList();
            if (AddFailure && steps.Any(s => s.Id == "drivers"))
                results.Add(new(new("WU:failed", "Intel - System - 10.1.1.39", UpdateComponent.Driver), false, false,
                    "Installation impossible ; un redémarrage peut être nécessaire.") { VerifiedStatus = "en erreur" });
            return Task.FromResult<IReadOnlyList<UpdateSequenceStepResult>>(results);
        }
    }

    private sealed class ReadOnlyAccess : IProcessRunner
    {
        public int Reads { get; private set; }
        public int ForbiddenCalls { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token)
        {
            if (request.FileName == "pnputil.exe" && request.Arguments.SequenceEqual(new[] { "/enum-drivers" }))
            { Reads++; return Task.FromResult(new ProcessRunResult(0, "Published Name: oem1.inf\nDriver package provider: NVIDIA\n", "")); }
            if (request.FileName == "powershell.exe" && request.Arguments.Last() ==
                "$ErrorActionPreference='Stop'; Get-CimInstance Win32_BIOS -ErrorAction Stop | Select-Object Manufacturer,SMBIOSBIOSVersion | ConvertTo-Json -Compress")
            { Reads++; return Task.FromResult(new ProcessRunResult(0, "{\"Manufacturer\":\"OEM\",\"SMBIOSBIOSVersion\":\"1.2.3\"}", "")); }
            ForbiddenCalls++;
            throw new InvalidOperationException("Écriture, téléchargement et flash interdits dans cette preuve.");
        }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token)
        { ForbiddenCalls++; throw new InvalidOperationException("Aucun lancement autorisé."); }
    }
}
