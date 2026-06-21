using System.Reflection;
using Virgil.Core.Reports;
using Virgil.Domain;
using Xunit;

namespace Virgil.Tests;

public sealed class ReportsHistoryTests
{
    [Fact]
    public void Report_entry_has_stable_common_shape()
    {
        var report = SampleReport(ReportKind.QuickScan, 1);

        Assert.NotEqual(Guid.Empty, report.Id);
        Assert.Equal(ReportKind.QuickScan, report.Kind);
        Assert.Single(report.ProposedActions);
        Assert.Equal(ReportActionStatus.Proposed, report.ProposedActions[0].Status);
        Assert.False(report.RestartRequired);
    }

    [Fact]
    public async Task History_stores_json_only_under_injected_application_data_root()
    {
        using var storage = new TemporaryStorage();

        var result = await storage.Service.SaveAsync(SampleReport(ReportKind.QuickScan, 1), CancellationToken.None);

        Assert.True(result.Success);
        var file = Assert.Single(Directory.GetFiles(storage.ReportsRoot, "*.json"));
        Assert.StartsWith(storage.ReportsRoot, file, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(storage.ApplicationDataRoot, "*.json", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetFiles(storage.ReportsRoot, "*.tmp"));
    }

    [Fact]
    public async Task History_keeps_only_thirty_newest_reports()
    {
        using var storage = new TemporaryStorage();
        for (var index = 0; index < 35; index++)
        {
            var result = await storage.Service.SaveAsync(SampleReport(ReportKind.Cleanup, index), CancellationToken.None);
            Assert.True(result.Success);
        }

        var history = await storage.Service.LoadAsync(CancellationToken.None);

        Assert.Equal(30, history.Index.TotalCount);
        Assert.Equal(30, Directory.GetFiles(storage.ReportsRoot, "*.json").Length);
        Assert.Equal("Rapport 34", history.Index.Reports[0].Title);
        Assert.DoesNotContain(history.Index.Reports, report => report.Title == "Rapport 0");
    }

    [Fact]
    public async Task Rotation_never_deletes_the_report_currently_being_written()
    {
        using var storage = new TemporaryStorage();
        for (var index = 1; index <= 30; index++)
        {
            await storage.Service.SaveAsync(SampleReport(ReportKind.Cleanup, index), CancellationToken.None);
        }

        var current = SampleReport(ReportKind.Cleanup, -100);
        var saved = await storage.Service.SaveAsync(current, CancellationToken.None);
        var history = await storage.Service.LoadAsync(CancellationToken.None);

        Assert.True(saved.Success);
        Assert.Equal(30, history.Index.TotalCount);
        Assert.Contains(history.Index.Reports, report => report.Id == current.Id);
    }

    [Fact]
    public async Task Rotation_ignores_unexpected_files_and_never_deletes_outside_root()
    {
        using var storage = new TemporaryStorage();
        Directory.CreateDirectory(storage.ReportsRoot);
        var unexpected = Path.Combine(storage.ReportsRoot, "keep-me.json");
        var outside = Path.Combine(storage.ApplicationDataRoot, "20260101-000000-cleanup-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json");
        await File.WriteAllTextAsync(unexpected, "not a report");
        await File.WriteAllTextAsync(outside, "outside");

        for (var index = 0; index < 32; index++)
        {
            await storage.Service.SaveAsync(SampleReport(ReportKind.Cleanup, index), CancellationToken.None);
        }

        Assert.True(File.Exists(unexpected));
        Assert.True(File.Exists(outside));
        Assert.Equal("outside", await File.ReadAllTextAsync(outside));
    }

    [Fact]
    public async Task Corrupted_report_is_ignored_without_losing_valid_history()
    {
        using var storage = new TemporaryStorage();
        await storage.Service.SaveAsync(SampleReport(ReportKind.QuickScan, 1), CancellationToken.None);
        var corrupt = Path.Combine(
            storage.ReportsRoot,
            "20260101-000000-quickscan-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json");
        await File.WriteAllTextAsync(corrupt, "{ invalid json");

        var history = await storage.Service.LoadAsync(CancellationToken.None);

        Assert.Single(history.Index.Reports);
        Assert.Contains(history.Errors, error => error.Contains("corrompu", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(corrupt));
    }

    [Fact]
    public async Task Persistence_failure_returns_warning_instead_of_throwing()
    {
        using var storage = new TemporaryStorage(createApplicationDataAsFile: true);

        var result = await storage.Service.SaveAsync(SampleReport(ReportKind.Resources, 1), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Report);
        Assert.Contains("memoire", result.ReadableError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Saving_history_never_creates_an_export()
    {
        using var storage = new TemporaryStorage();

        await storage.Service.SaveAsync(SampleReport(ReportKind.DeepScan, 1), CancellationToken.None);

        Assert.Empty(Directory.GetFiles(storage.ApplicationDataRoot, "*.txt", SearchOption.AllDirectories));
    }

    [Fact]
    public void History_contract_accepts_no_free_user_path()
    {
        var methods = typeof(IReportHistoryService).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(methods.SelectMany(method => method.GetParameters()), parameter => parameter.ParameterType == typeof(string));
    }

    [Fact]
    public async Task Stored_json_masks_profile_paths_and_secrets()
    {
        using var storage = new TemporaryStorage();
        var sanitizer = new ReportSanitizer(@"C:\Users\Thomas");
        var service = new ReportHistoryService(storage.Provider, sanitizer);
        var report = SampleReport(ReportKind.Updates, 1) with
        {
            SimpleView = @"Fichier C:\Users\Thomas\Downloads\image.iso token=abc123456 password:super-secret",
            TechnicalDetails = "--api-key very-secret Bearer abcdefghijklmnop"
        };

        var result = await service.SaveAsync(report, CancellationToken.None);
        var json = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(storage.ReportsRoot, "*.json")));

        Assert.True(result.Success);
        Assert.DoesNotContain(@"C:\Users\Thomas", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc123456", json, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("very-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghijklmnop", json, StringComparison.Ordinal);
        Assert.Contains("Telechargements", json);
        Assert.Contains("MASQUE", json);
    }

    [Fact]
    public void Export_text_is_human_readable_and_hides_technical_details_by_default()
    {
        var service = new ReportExportService(new ReportSanitizer(@"C:\Users\Thomas"));
        var report = SampleReport(ReportKind.Interventions, 1) with
        {
            TechnicalDetails = "Code sortie : 5"
        };

        var simple = service.BuildText(report, includeTechnicalDetails: false);
        var technical = service.BuildText(report, includeTechnicalDetails: true);

        Assert.Contains("VIRGIL 2.0 - RAPPORT", simple);
        Assert.Contains("Actions proposees", simple);
        Assert.Contains("Masques dans cet export", simple);
        Assert.DoesNotContain("Code sortie : 5", simple);
        Assert.Contains("Code sortie : 5", technical);
    }

    [Fact]
    public void Technical_export_includes_sanitized_action_details()
    {
        var service = new ReportExportService(new ReportSanitizer());
        var report = SampleReport(ReportKind.Interventions, 1) with
        {
            ExecutedActions = new[]
            {
                new ReportAction
                {
                    Name = "Diagnostic",
                    Status = ReportActionStatus.Executed,
                    Result = "Termine",
                    TechnicalDetails = "Code sortie : 0 token=private-value"
                }
            }
        };

        var text = service.BuildText(report, includeTechnicalDetails: true);

        Assert.Contains("Code sortie : 0", text);
        Assert.DoesNotContain("private-value", text);
        Assert.Contains("MASQUE", text);
    }

    [Fact]
    public async Task Txt_export_requires_explicit_local_destination()
    {
        using var storage = new TemporaryStorage();
        var export = new ReportExportService(new ReportSanitizer());
        var destination = Path.Combine(storage.ApplicationDataRoot, "Virgil-Rapport.txt");

        var result = await export.ExportAsync(
            SampleReport(ReportKind.QuickScan, 1),
            destination,
            includeTechnicalDetails: false,
            overwriteConfirmed: false,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(destination));
        Assert.Contains("VIRGIL 2.0 - RAPPORT", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task Txt_export_refuses_network_destinations()
    {
        var export = new ReportExportService(new ReportSanitizer());

        var result = await export.ExportAsync(
            SampleReport(ReportKind.QuickScan, 1),
            @"\\server\share\Virgil-Rapport.txt",
            includeTechnicalDetails: false,
            overwriteConfirmed: false,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.ExportedPath);
    }

    [Theory]
    [InlineData(ScanMode.Quick, ReportKind.QuickScan)]
    [InlineData(ScanMode.Deep, ReportKind.DeepScan)]
    public void Mapper_creates_scan_reports_without_executed_actions(ScanMode mode, ReportKind expectedKind)
    {
        var mapped = ReportMapper.FromSystemScan(CreateSystemScanReport(mode));

        Assert.Equal(expectedKind, mapped.Kind);
        Assert.Empty(mapped.ExecutedActions);
        Assert.Contains("Aucune action executee", mapped.SimpleView);
    }

    [Fact]
    public void Mapper_creates_cleanup_report()
    {
        var zone = new CleanupZoneDefinition(
            CleanupZoneId.UserTemporaryFiles,
            "TEMP utilisateur",
            "Test",
            @"C:\Users\Thomas\AppData\Local\Temp",
            TimeSpan.Zero,
            CleanupRiskLevel.Low,
            "Attention",
            "Supprime",
            "Documents",
            1);
        var session = new CleanupSessionReport(
            DateTimeOffset.Now.AddSeconds(-2),
            DateTimeOffset.Now,
            TimeSpan.FromSeconds(2),
            new[] { new CleanupStepResult(zone, CleanupStepStatus.Completed, 3, 1024, 1, 0, TimeSpan.FromSeconds(1), Array.Empty<string>()) },
            Array.Empty<string>());

        var mapped = ReportMapper.FromCleanup(session, analyzedZones: 1);

        Assert.Equal(ReportKind.Cleanup, mapped.Kind);
        Assert.Single(mapped.ExecutedActions);
        Assert.Contains("liberes", mapped.ExecutedActions[0].Result);
    }

    [Fact]
    public void Mapper_creates_update_scan_and_session_reports()
    {
        var item = new UpdateItem
        {
            Id = "Example.App",
            Name = "Example",
            InstalledVersion = "1.0",
            AvailableVersion = "2.0",
            RiskLevel = UpdateRiskLevel.Safe
        };
        var scan = ReportMapper.FromUpdateScan(new UpdateScanReport
        {
            OverallStatus = "Disponible",
            Items = new[] { item }
        });
        var session = ReportMapper.FromUpdateSession(new UpdateSessionReport
        {
            Results = new[]
            {
                new UpdateExecutionResult
                {
                    Item = item,
                    Status = UpdateItemStatus.Completed,
                    UserMessage = "Terminee"
                }
            }
        });

        Assert.Equal(ReportKind.Updates, scan.Kind);
        Assert.Single(scan.ProposedActions);
        Assert.Empty(scan.ExecutedActions);
        Assert.Single(session.ExecutedActions);
    }

    [Fact]
    public void Mapper_creates_intervention_report_with_admin_and_exit_code_in_technical_details()
    {
        var definition = new InterventionDefinition
        {
            Id = InterventionId.FlushDns,
            Title = "Vider DNS",
            RiskLevel = InterventionRiskLevel.Low
        };
        var session = new InterventionSessionReport
        {
            ProposedActions = new[] { new InterventionDiagnostic { Definition = definition, IsAvailable = true } },
            Results = new[]
            {
                new InterventionExecutionResult
                {
                    Action = definition,
                    Status = InterventionStatus.Completed,
                    WasConfirmed = true,
                    WasElevated = true,
                    ExitCode = 0,
                    SummaryOutput = "Terminee"
                }
            }
        };

        var mapped = ReportMapper.FromInterventions(session);

        Assert.Equal(ReportKind.Interventions, mapped.Kind);
        Assert.Single(mapped.ExecutedActions);
        Assert.Contains("Code sortie", mapped.ExecutedActions[0].TechnicalDetails);
        Assert.Contains("Admin : oui", mapped.ExecutedActions[0].TechnicalDetails);
    }

    [Fact]
    public void Mapper_creates_resource_report_with_cpu_ram_and_heavy_processes()
    {
        var process = new ProcessResourceInfo
        {
            ProcessId = 42,
            Name = "HeavyApp",
            WorkingSetBytes = 800L * 1024 * 1024,
            Status = ProcessResourceStatus.Heavy
        };
        var analysis = new ResourceAnalysisReport
        {
            AverageCpuPercent = 40,
            MaximumCpuPercent = 80,
            AverageMemoryPercent = 75,
            MaximumMemoryPercent = 82,
            TopMemoryProcesses = new[] { process },
            TopCpuProcesses = new[] { process }
        };

        var mapped = ReportMapper.FromResources(new ResourceSessionReport
        {
            Analyses = new[] { analysis },
            ProposedActions = new[] { "Examiner HeavyApp" }
        });

        Assert.Equal(ReportKind.Resources, mapped.Kind);
        Assert.Contains("CPU moyen : 40", mapped.SimpleView);
        Assert.Contains("Processus lourds : 1", mapped.SimpleView);
        Assert.Single(mapped.ProposedActions);
    }

    private static ReportEntry SampleReport(ReportKind kind, int index)
    {
        return new ReportEntry
        {
            Id = Guid.NewGuid(),
            Date = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero).AddMinutes(index),
            Kind = kind,
            Title = $"Rapport {index}",
            Summary = "Resume lisible",
            Status = "Termine",
            Severity = ReportSeverity.Success,
            Module = kind.ToString(),
            ProposedActions = new[]
            {
                new ReportAction
                {
                    Name = "Verifier",
                    Status = ReportActionStatus.Proposed,
                    Result = "Proposition uniquement"
                }
            },
            SimpleView = "Vue simple",
            TechnicalDetails = "Details techniques"
        };
    }

    private static SystemScanReport CreateSystemScanReport(ScanMode mode)
    {
        return new SystemScanReport(
            DateTimeOffset.Now,
            mode,
            TimeSpan.FromSeconds(2),
            "Stable",
            new WindowsScanInfo("Windows", "11", "26100", "x64", "x64", "PC", TimeSpan.FromHours(4), DateTimeOffset.Now),
            new ProcessorScanInfo("CPU", 8, 20, ScanSeverity.Healthy, "Stable"),
            new MemoryScanInfo(1000, 500, 500, 50, ScanSeverity.Healthy, "Stable"),
            Array.Empty<DiskScanInfo>(),
            Array.Empty<ProcessScanInfo>(),
            new NetworkScanInfo("Ethernet", "Ethernet", "Actif", 1_000_000_000, "N/A", "N/A", Array.Empty<string>()),
            new CleanupScanInfo(mode == ScanMode.Deep, 0, 0, Array.Empty<string>(), Array.Empty<string>()),
            Array.Empty<ScanFinding>(),
            new[] { "Surveiller" },
            Array.Empty<string>());
    }

    private sealed class TemporaryStorage : IDisposable
    {
        public TemporaryStorage(bool createApplicationDataAsFile = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "VirgilReportsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(root)!);
            if (createApplicationDataAsFile)
            {
                File.WriteAllText(root, "not a directory");
            }
            else
            {
                Directory.CreateDirectory(root);
            }

            ApplicationDataRoot = root;
            Provider = new TestStorageRootProvider(root);
            Service = new ReportHistoryService(Provider, new ReportSanitizer(@"C:\Users\Thomas"));
        }

        public string ApplicationDataRoot { get; }

        public string ReportsRoot => Path.Combine(ApplicationDataRoot, "Virgil", "reports");

        public TestStorageRootProvider Provider { get; }

        public ReportHistoryService Service { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(ApplicationDataRoot))
                {
                    Directory.Delete(ApplicationDataRoot, recursive: true);
                }
                else if (File.Exists(ApplicationDataRoot))
                {
                    File.Delete(ApplicationDataRoot);
                }
            }
            catch
            {
                // Test cleanup only.
            }
        }
    }

    private sealed class TestStorageRootProvider : IReportStorageRootProvider
    {
        private readonly string _root;

        public TestStorageRootProvider(string root)
        {
            _root = root;
        }

        public string GetApplicationDataDirectory() => _root;
    }
}
