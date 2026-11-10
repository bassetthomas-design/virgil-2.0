using Virgil.Contracts;
using Virgil.Contracts.Actions;
using Virgil.Core.Actions;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using Virgil.Infrastructure.Windows;
using Xunit;

namespace Virgil.Tests;

public sealed class D1bisCFirmwarePassTests
{
    private const string FirmwareId = "Nvidia.UEFIFirmwareUpdater40xx";
    private const string FirmwareName = "NVIDIA UEFI Firmware Updater (40xx series)";

    [Fact, Trait("Category", "Safety")]
    public async Task A_readable_complete_software_table_keeps_the_existing_grouped_command()
    {
        var runner = new Runner { IncludeFirmware = false };
        var availability = new WingetAvailabilityService(runner, ["winget.exe"]);
        var inventory = await new DriverFinalPassWindowsReader(runner, availability).ReadAsync(default);
        Assert.True(inventory.IsComplete);
        Assert.Equal("VideoLAN.VLC", Assert.Single(inventory.Items).Id);
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(new WindowsUpdateSequenceBackend(runner, null!, null!, () => "helper", availability),
            authority, new EmptyPendingRebootProbe());
        UpdateSequenceStep[] selection = [new("winget-final", "Passe finale : applications disponibles", UpdateComponent.WingetFinal)];
        var result = await service.ExecuteAsync(selection, authority.Issue(service.CreatePreview(selection), new(ConfirmationLevel.Reinforced, null)), null, default);
        Assert.True(Assert.Single(result.Steps, r => r.Step.Component == UpdateComponent.WingetFinal).Succeeded);
        Assert.Equal(WindowsUpdateSequenceBackend.BuildFinalArguments(WingetAvailabilityService.GetCapabilities("1.8.0")),
            Assert.Single(runner.Requests, r => r.Arguments.Contains("--all")).Arguments);
        Assert.DoesNotContain(runner.Requests, r => r.Arguments.Contains("--id"));
    }

    [Fact, Trait("Category", "Safety")]
    public async Task Firmware_and_software_in_the_same_final_pass_update_only_the_software()
    {
        var runner = new Runner();
        var authority = new ConfirmationService(TimeProvider.System);
        var backend = new WindowsUpdateSequenceBackend(runner, null!, null!, () => "helper", new WingetAvailabilityService(runner, ["winget.exe"]));
        var service = new UpdateSequenceService(backend, authority, new EmptyPendingRebootProbe());
        UpdateSequenceStep[] selection = [new("winget-final", "Passe finale : applications disponibles", UpdateComponent.WingetFinal)];
        Assert.True((await service.ExecuteAsync(selection, null, null, default)).WasRefused);
        Assert.Empty(runner.Requests);
        var plan = authority.Issue(service.CreatePreview(selection), new(ConfirmationLevel.Reinforced, null));
        var result = await service.ExecuteAsync(selection, plan, null, default);
        Assert.False(result.WasRefused);
        Assert.DoesNotContain(runner.Requests, r => r.Arguments.Contains("--all"));
        var installation = Assert.Single(runner.Requests, r => r.Arguments.Contains("--id"));
        var expected = WingetUpdateExecutionService.BuildUpgradeArguments("VideoLAN.VLC", WingetAvailabilityService.GetCapabilities("1.8.0"))
            .Concat(new[] { "--version", "3.0.21" });
        Assert.Equal(expected, installation.Arguments);
        Assert.DoesNotContain(runner.Requests, r => r.Arguments.Contains(FirmwareId));
        Assert.Contains(result.Steps, r => r.Step.Application?.Id == "VideoLAN.VLC" && r.Succeeded);
        var firmware = Assert.Single(result.Steps, r => r.Step.Title == FirmwareName);
        Assert.True(firmware.Step.IsInformational);
        Assert.Equal("non pris en charge automatiquement", firmware.VerifiedStatus);
        Assert.Contains("Virgil n'installe jamais le BIOS", firmware.Message);
        Assert.Contains("firmware", firmware.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(firmware.Succeeded);
    }

    [Theory, Trait("Category", "Safety")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_truncated_identity_or_a_dropped_table_row_keeps_the_set_unproven(bool truncated)
    {
        var sample = await new Runner().RunAsync(new("winget.exe", ["upgrade"], TimeSpan.FromSeconds(1)), default);
        var text = truncated ? sample.StandardOutput.Replace(FirmwareId, FirmwareId + "…", StringComparison.Ordinal)
            : sample.StandardOutput.Replace("\nVLC", "\nligne invalide\nVLC", StringComparison.Ordinal);
        var runner = new Runner { Inventory = text };
        var inventory = await new DriverFinalPassWindowsReader(runner, new WingetAvailabilityService(runner, ["winget.exe"])).ReadAsync(default);
        Assert.False(inventory.IsComplete);
        Assert.DoesNotContain(runner.Requests, request => request.Arguments.Contains("--all") || request.Arguments.Contains("--id"));
    }

    [Theory, Trait("Category", "Safety")]
    [InlineData("")]
    [InlineData("inventaire illisible")]
    public async Task An_unproven_inventory_never_authorizes_a_grouped_installation(string inventory)
    {
        var runner = new Runner { Inventory = inventory };
        var authority = new ConfirmationService(TimeProvider.System);
        var service = new UpdateSequenceService(new WindowsUpdateSequenceBackend(runner, null!, null!, () => "helper",
            new WingetAvailabilityService(runner, ["winget.exe"])), authority, new EmptyPendingRebootProbe());
        UpdateSequenceStep[] selection = [new("winget-final", "Passe finale : applications disponibles", UpdateComponent.WingetFinal)];
        var result = await service.ExecuteAsync(selection, authority.Issue(service.CreatePreview(selection), new(ConfirmationLevel.Reinforced, null)), null, default);
        Assert.DoesNotContain(runner.Requests, r => r.Arguments.Contains("--all") || r.Arguments.Contains("--id"));
        Assert.Contains(result.Steps, r => r.Step.Id == "winget-final" && !r.Succeeded);
    }

    private sealed class Runner : IProcessRunner
    {
        public string? Inventory { get; init; }
        public bool IncludeFirmware { get; init; } = true;
        public List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token)
        {
            Requests.Add(request);
            if (request.Arguments.Contains("--version") && request.Arguments.Count == 1)
                return Task.FromResult(new ProcessRunResult(0, "v1.8.0", ""));
            if (request.FileName == "powershell.exe") return Task.FromResult(new ProcessRunResult(0, "VIRGIL_MODULE_MISSING", ""));
            if (request.Arguments.FirstOrDefault() == "upgrade" && !request.Arguments.Contains("--all") && !request.Arguments.Contains("--id"))
            {
                var header = $"{"Name",-60}{"Id",-45}{"Version",-16}{"Available",-16}Source";
                var rows = (IncludeFirmware ? new[] { (FirmwareName, FirmwareId, "1.1.0", "1.2.0") } : [])
                    .Concat(new[] { ("VLC media player", "VideoLAN.VLC", "3.0.20", "3.0.21") }).ToArray();
                var output = header + "\n" + new string('-', header.Length) + "\n"
                    + string.Join("\n", rows.Select(row => $"{row.Item1,-60}{row.Item2,-45}{row.Item3,-16}{row.Item4,-16}winget")) + $"\n\n{rows.Length} upgrades available.";
                return Task.FromResult(new ProcessRunResult(0, Inventory ?? output, ""));
            }
            return Task.FromResult(new ProcessRunResult(0, "", ""));
        }
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken token) => throw new InvalidOperationException("Aucun lancement autorisé.");
    }
}
