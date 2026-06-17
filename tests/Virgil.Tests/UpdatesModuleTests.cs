using Virgil.Core.Cleanup;
using Virgil.Core.Scanning;
using Virgil.Core.Updates;
using Virgil.Domain;
using Xunit;

namespace Virgil.Tests;

public sealed class UpdatesModuleTests
{
    [Fact]
    public async Task WingetAvailability_returns_unavailable_when_launch_fails()
    {
        var runner = new FakeProcessRunner(request =>
            new ProcessRunResult(-1, string.Empty, string.Empty, LaunchError: "missing"));
        var service = new WingetAvailabilityService(runner, new[] { @"C:\tools\winget.exe" });

        var availability = await service.DetectAsync(CancellationToken.None);

        Assert.False(availability.IsAvailable);
        Assert.Contains("WinGet non executable", availability.Errors[0]);
    }

    [Theory]
    [InlineData("v1.2.0", false, true)]
    [InlineData("1.8.1911", true, true)]
    public void WingetCapabilities_follow_detected_version(string version, bool modernOptions, bool sourceAgreements)
    {
        var capabilities = WingetAvailabilityService.GetCapabilities(version);

        Assert.Equal(sourceAgreements, capabilities.SupportsAcceptSourceAgreements);
        Assert.Equal(modernOptions, capabilities.SupportsDisableInteractivity);
        Assert.Equal(modernOptions, capabilities.SupportsAcceptPackageAgreements);
    }

    [Fact]
    public async Task WingetScan_reports_timeout_without_items()
    {
        var runner = new FakeProcessRunner(
            _ => new ProcessRunResult(0, "v1.8.0", string.Empty),
            _ => new ProcessRunResult(-1, string.Empty, string.Empty, TimedOut: true));
        var scan = CreateScanService(runner);

        var report = await scan.ScanAsync(UpdateScanRequest.DeepPreview, null, CancellationToken.None);

        Assert.Empty(report.Items);
        Assert.Contains("Scan WinGet interrompu par timeout.", report.Errors);
    }

    [Fact]
    public async Task WingetScan_can_be_cancelled()
    {
        var runner = new FakeProcessRunner(
            _ => new ProcessRunResult(-1, string.Empty, string.Empty, Cancelled: true));
        var scan = CreateScanService(runner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            scan.ScanAsync(UpdateScanRequest.DeepPreview, null, cancellation.Token));
    }

    [Fact]
    public void Parser_reads_english_and_french_tables_and_ignores_ambiguous_lines()
    {
        const string output = """
Name             Id                    Version      Available    Source
-----------------------------------------------------------------------
VLC media player  VideoLAN.VLC          3.0.18       3.0.20       winget
Broken row without package id

Nom              ID                    Version      Disponible   Source
-----------------------------------------------------------------------
7-Zip            7zip.7zip             23.01        24.09        winget
Power Toys       Microsoft.PowerToys   0.76.0       0.77.0       winget
""";

        var result = WingetUpgradeParser.Parse(output);

        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, item => item.Id == "VideoLAN.VLC" && item.Name == "VLC media player");
        Assert.Contains(result.Items, item => item.Id == "7zip.7zip" && item.Name == "7-Zip");
        Assert.Contains(result.Items, item => item.Id == "Microsoft.PowerToys" && item.Name == "Power Toys");
        Assert.Contains(result.Warnings, warning => warning.Contains("ignoree", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Scan_prepares_preview_without_targeted_execution()
    {
        var runner = new FakeProcessRunner(
            _ => new ProcessRunResult(0, "v1.8.0", string.Empty),
            _ => new ProcessRunResult(0, """
Name             Id                    Version      Available    Source
-----------------------------------------------------------------------
VLC media player  VideoLAN.VLC          3.0.18       3.0.20       winget
""", string.Empty),
            _ => new ProcessRunResult(0, string.Empty, string.Empty));
        var scan = CreateScanService(runner);

        var report = await scan.ScanAsync(UpdateScanRequest.DeepPreview, null, CancellationToken.None);

        Assert.Single(report.Items);
        Assert.DoesNotContain(runner.Requests, request => request.Arguments.Contains("--id"));
        Assert.DoesNotContain(runner.Requests, request => request.Arguments.Contains("--all"));
    }

    [Theory]
    [InlineData("VideoLAN.VLC", "VLC", UpdateRiskLevel.Safe)]
    [InlineData("Nvidia.Driver", "NVIDIA Display Driver", UpdateRiskLevel.Sensitive)]
    [InlineData("Vendor.BiosTool", "BIOS Update Utility", UpdateRiskLevel.CriticalInformationOnly)]
    [InlineData("Unknown.App", "Unknown App", UpdateRiskLevel.ValidationRequired)]
    public void Risk_classifier_assigns_expected_levels(string id, string name, UpdateRiskLevel expected)
    {
        var classifier = new UpdateRiskClassifier();

        var risk = classifier.Classify(new UpdateItem
        {
            Id = id,
            Name = name,
            Source = UpdateSource.Winget
        });

        Assert.Equal(expected, risk.Level);
    }

    [Fact]
    public async Task Execution_uses_exact_targeted_winget_command_only()
    {
        var runner = new FakeProcessRunner(
            _ => new ProcessRunResult(0, "v1.8.0", string.Empty),
            _ => new ProcessRunResult(0, "done", string.Empty));
        var availability = new WingetAvailabilityService(runner, new[] { @"C:\tools\winget.exe" });
        var service = new WingetUpdateExecutionService(availability, runner);
        var item = new UpdateItem
        {
            Id = "Microsoft.PowerToys",
            Name = "PowerToys",
            InstalledVersion = "0.76.0",
            AvailableVersion = "0.77.0",
            Source = UpdateSource.Winget,
            RiskLevel = UpdateRiskLevel.ValidationRequired
        };

        var result = await service.ExecuteAsync(item, CancellationToken.None);
        var command = runner.Requests[1].Arguments;

        Assert.Equal(UpdateItemStatus.Completed, result.Status);
        Assert.Contains("--id", command);
        Assert.Contains("Microsoft.PowerToys", command);
        Assert.Contains("--exact", command);
        Assert.DoesNotContain("--all", command);
        Assert.DoesNotContain("--silent", command);
        Assert.DoesNotContain("--force", command);
    }

    [Fact]
    public async Task Execution_blocks_firmware_information_items()
    {
        var runner = new FakeProcessRunner(_ => new ProcessRunResult(0, "should not run", string.Empty));
        var service = new WingetUpdateExecutionService(
            new WingetAvailabilityService(runner, new[] { "winget.exe" }),
            runner);

        var result = await service.ExecuteAsync(new UpdateItem
        {
            Id = "Vendor.Bios",
            Name = "BIOS updater",
            Source = UpdateSource.FirmwareInformation,
            RiskLevel = UpdateRiskLevel.CriticalInformationOnly
        }, CancellationToken.None);

        Assert.Equal(UpdateItemStatus.InformationOnly, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void Execution_report_counts_skip_cancel_and_errors()
    {
        var service = new WingetUpdateExecutionService(new FakeProcessRunner());
        var item = new UpdateItem { Id = "VideoLAN.VLC", Name = "VLC", Source = UpdateSource.Winget };

        var report = service.CreateReport(
            DateTimeOffset.Now,
            new[]
            {
                service.Skip(item),
                service.Cancel(item)
            },
            new[] { "Sequence annulee." });

        Assert.Equal(1, report.SkippedCount);
        Assert.True(report.WasCancelled);
        Assert.Contains("Sequence annulee.", report.Errors);
    }

    [Fact]
    public void Driver_inventory_parser_is_read_only_and_hides_install()
    {
        const string output = """
Published Name:     oem12.inf
Driver package provider: NVIDIA
Class:              Display adapters
Driver version:     31.0.15
Signer Name:        Microsoft Windows Hardware Compatibility Publisher
""";

        var drivers = DriverInformationService.ParsePnPUtil(output);

        Assert.Single(drivers);
        Assert.Equal("NVIDIA", drivers[0].Provider);
    }

    [Fact]
    public async Task Driver_inventory_report_never_enables_install_button_in_v1()
    {
        var runner = new FakeProcessRunner(_ => new ProcessRunResult(0, """
Published Name:     oem12.inf
Driver package provider: NVIDIA
Class:              Display adapters
Driver version:     31.0.15
Signer Name:        Microsoft Windows Hardware Compatibility Publisher
""", string.Empty));
        var service = new DriverInformationService(runner);

        var report = await service.InspectAsync(CancellationToken.None);

        Assert.True(report.WasAnalyzed);
        Assert.False(report.CanInstallDrivers);
        Assert.Contains("Masque en V1", report.InstallButtonVisibilityReason);
    }

    [Fact]
    public void Upgrade_arguments_never_use_dangerous_global_options()
    {
        var arguments = WingetUpdateExecutionService.BuildUpgradeArguments(
            "VideoLAN.VLC",
            new WingetCapabilities
            {
                SupportsAcceptPackageAgreements = true,
                SupportsAcceptSourceAgreements = true,
                SupportsDisableInteractivity = true
            });

        Assert.Contains("--id", arguments);
        Assert.Contains("VideoLAN.VLC", arguments);
        Assert.Contains("--exact", arguments);
        Assert.DoesNotContain("--all", arguments);
        Assert.DoesNotContain("--silent", arguments);
        Assert.DoesNotContain("--force", arguments);
    }

    [Fact]
    public async Task Deep_scan_uses_preview_request_and_does_not_install()
    {
        var updateService = new RecordingUpdateScanService();
        var systemScan = new SystemScanService(new EmptyCleanupService(), updateService);

        var report = await systemScan.RunAsync(ScanMode.Deep, null, CancellationToken.None);

        Assert.True(report.Updates.WasAnalyzed);
        Assert.Single(updateService.Requests);
        Assert.True(updateService.Requests[0].IncludeApplicationUpdates);
        Assert.Equal(0, updateService.ExecutionCount);
    }

    private static WingetUpdateScanService CreateScanService(FakeProcessRunner runner)
    {
        return new WingetUpdateScanService(
            new WingetAvailabilityService(runner, new[] { @"C:\tools\winget.exe" }),
            runner,
            new UpdateRiskClassifier(),
            new WindowsUpdateStatusService(),
            new DriverInformationService(runner));
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Queue<Func<ProcessRunRequest, ProcessRunResult>> _responses = new();

        public FakeProcessRunner(params Func<ProcessRunRequest, ProcessRunResult>[] responses)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response);
            }
        }

        public List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(new ProcessRunResult(-1, string.Empty, string.Empty, Cancelled: true));
            }

            Requests.Add(request);
            return Task.FromResult(_responses.Count == 0
                ? new ProcessRunResult(0, string.Empty, string.Empty)
                : _responses.Dequeue()(request));
        }
    }

    private sealed class RecordingUpdateScanService : IUpdateScanService
    {
        public List<UpdateScanRequest> Requests { get; } = new();

        public int ExecutionCount { get; private set; }

        public Task<UpdateScanReport> ScanAsync(
            UpdateScanRequest request,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new UpdateScanReport
            {
                Scope = request.Scope,
                OverallStatus = "Preview",
                Winget = new WingetAvailability
                {
                    IsAvailable = true,
                    ExecutablePath = "winget.exe",
                    Version = "1.8.0",
                    Message = "WinGet detecte."
                },
                Items = new[]
                {
                    new UpdateItem
                    {
                        Id = "VideoLAN.VLC",
                        Name = "VLC",
                        InstalledVersion = "3.0",
                        AvailableVersion = "3.1",
                        Source = UpdateSource.Winget,
                        RiskLevel = UpdateRiskLevel.Safe
                    }
                }
            });
        }
    }

    private sealed class EmptyCleanupService : ICleanupService
    {
        public CleanupPreview PreviewTemporaryFiles()
        {
            return new CleanupPreview(DateTimeOffset.Now, Array.Empty<CleanupTarget>());
        }
    }
}
