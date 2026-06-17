using System.Text.Json;
using Virgil.Core.Cleanup;
using Virgil.Core.Interventions;
using Virgil.Core.Scanning;
using Virgil.Core.Updates;
using Virgil.Domain;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

public sealed class InterventionsModuleTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 6, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Catalog_exposes_only_targeted_interventions_without_take_ownership()
    {
        var catalog = new InterventionCatalog();
        var definitions = catalog.GetAll();

        Assert.Equal(9, definitions.Count);
        Assert.DoesNotContain(definitions, definition =>
            definition.Title.Contains("ownership", StringComparison.OrdinalIgnoreCase) ||
            definition.CommandPreviews.Any(command =>
                command.Executable.Contains("takeown", StringComparison.OrdinalIgnoreCase) ||
                command.Arguments.Any(argument =>
                    argument.Contains("takeown", StringComparison.OrdinalIgnoreCase) ||
                    argument.Contains("icacls", StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public void Elevated_allowlist_uses_fixed_safe_commands_only()
    {
        var allowlist = new ElevatedActionAllowlist();
        var elevatedActions = Enum.GetValues<InterventionId>()
            .Where(id => id != InterventionId.RestartExplorer)
            .ToList();

        foreach (var action in elevatedActions)
        {
            Assert.True(allowlist.TryGet(action, "C:", out var spec));
            foreach (var command in spec.Commands)
            {
                var text = command.FileName + " " + string.Join(" ", command.Arguments);
                Assert.DoesNotContain("takeown", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("icacls", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("/ResetBase", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("&", text);
                Assert.DoesNotContain("|", text);
                Assert.DoesNotContain(";", text);
            }
        }

        Assert.False(allowlist.TryGet(InterventionId.RestartExplorer, "C:", out _));
        Assert.True(allowlist.TryGet(InterventionId.ChkdskOnlineScan, "C:", out var chkdsk));
        var chkdskCommand = Assert.Single(chkdsk.Commands);
        Assert.Contains("/scan", chkdskCommand.Arguments);
        Assert.DoesNotContain("/f", chkdskCommand.Arguments, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("/r", chkdskCommand.Arguments, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("/x", chkdskCommand.Arguments, StringComparer.OrdinalIgnoreCase);
        Assert.False(allowlist.TryGet(InterventionId.ChkdskOnlineScan, @"C:\", out _));
    }

    [Fact]
    public async Task Request_validator_refuses_free_arguments()
    {
        using var sandbox = TemporarySandbox.Create();
        var validator = new ElevatedRequestValidator(() => FixedUtcNow, () => sandbox.Root);
        var requestPath = WriteRequest(
            validator.RootDirectory,
            """
            {
              "ProtocolVersion": 1,
              "ActionId": 1,
              "Nonce": "ABCDEF0123456789ABCDEF0123456789",
              "CreatedAt": "2026-06-17T10:00:00+00:00",
              "ResultPath": "__RESULT__",
              "Arguments": ["/f"]
            }
            """);

        await Assert.ThrowsAsync<JsonException>(() => validator.ValidateAsync(requestPath));
    }

    [Fact]
    public async Task Request_validator_rejects_bad_nonce_expired_and_outside_result()
    {
        using var sandbox = TemporarySandbox.Create();
        var validator = new ElevatedRequestValidator(() => FixedUtcNow, () => sandbox.Root);

        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(
            WriteValidRequest(validator.RootDirectory, nonce: "bad")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(
            WriteValidRequest(validator.RootDirectory, createdAt: FixedUtcNow.AddMinutes(-11))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(
            WriteValidRequest(validator.RootDirectory, resultPath: Path.Combine(sandbox.Root, "outside.result.json"))));
    }

    [Fact]
    public async Task Dispatcher_requires_admin_before_running_allowlisted_commands()
    {
        var runner = new RecordingElevatedCommandRunner();
        var dispatcher = CreateDispatcher(runner, isAdministrator: false);

        var result = await dispatcher.ExecuteValidatedAsync(Request(InterventionId.FlushDns));

        Assert.Equal(InterventionStatus.Failed, result.Status);
        Assert.Contains("administrateur", result.ReadableError!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task Dispatcher_executes_network_reset_and_marks_reboot_required()
    {
        var runner = new RecordingElevatedCommandRunner(_ =>
            new ElevatedCommandResult(0, "reset ok", string.Empty));
        var dispatcher = CreateDispatcher(runner, isAdministrator: true);

        var result = await dispatcher.ExecuteValidatedAsync(Request(InterventionId.ResetWinsock));

        Assert.Equal(InterventionStatus.RebootRequired, result.Status);
        Assert.True(result.RebootRequired);
        var command = Assert.Single(runner.Commands);
        Assert.Equal("netsh.exe", command.FileName);
        Assert.Equal(new[] { "winsock", "reset" }, command.Arguments);
    }

    [Fact]
    public async Task Dispatcher_reports_partial_failure_without_continuing_past_failed_step()
    {
        var runner = new RecordingElevatedCommandRunner(command =>
            command.Arguments.Contains("/renew")
                ? new ElevatedCommandResult(2, string.Empty, "renew failed")
                : new ElevatedCommandResult(0, "release ok", string.Empty));
        var dispatcher = CreateDispatcher(runner, isAdministrator: true);

        var result = await dispatcher.ExecuteValidatedAsync(Request(InterventionId.RenewIp));

        Assert.Equal(InterventionStatus.PartialFailure, result.Status);
        Assert.Equal(2, runner.Commands.Count);
        Assert.Contains("renew failed", result.ReadableError);
    }

    [Fact]
    public async Task Execution_service_never_elevates_without_explicit_confirmation()
    {
        var helper = new RecordingHelperClient();
        var service = new InterventionExecutionService(helper, new FakeExplorerRestarter());

        var result = await service.ExecuteAsync(Diagnostic(InterventionId.FlushDns), confirmed: false, CancellationToken.None);

        Assert.Equal(InterventionStatus.Failed, result.Status);
        Assert.False(result.WasElevated);
        Assert.Equal(0, helper.Calls);
    }

    [Fact]
    public async Task Execution_service_restarts_explorer_locally_without_helper()
    {
        var helper = new RecordingHelperClient();
        var restarter = new FakeExplorerRestarter();
        var service = new InterventionExecutionService(helper, restarter);

        var result = await service.ExecuteAsync(Diagnostic(InterventionId.RestartExplorer), confirmed: true, CancellationToken.None);

        Assert.Equal(InterventionStatus.Completed, result.Status);
        Assert.False(result.WasElevated);
        Assert.Equal(1, restarter.Calls);
        Assert.Equal(0, helper.Calls);
    }

    [Fact]
    public async Task Elevated_client_reports_temp_folder_failure_without_launching_helper()
    {
        using var sandbox = TemporarySandbox.Create();
        var occupiedPath = Path.Combine(sandbox.Root, "occupied");
        File.WriteAllText(occupiedPath, "not a directory");
        var launcher = new RecordingElevatedProcessLauncher();
        var helperPath = Path.Combine(sandbox.Root, "Virgil.ElevatedHelper.exe");
        File.WriteAllText(helperPath, string.Empty);
        var client = new ElevatedHelperClient(
            new ElevatedHelperRequestStore(() => occupiedPath),
            launcher,
            () => helperPath);

        var result = await client.ExecuteAsync(new InterventionCatalog().Get(InterventionId.FlushDns), CancellationToken.None);

        Assert.Equal(InterventionStatus.Failed, result.Status);
        Assert.Contains("Dossier temporaire", result.ReadableError);
        Assert.Equal(0, launcher.Calls);
    }

    [Fact]
    public async Task Quick_scan_does_not_run_intervention_diagnostics()
    {
        var interventions = new RecordingInterventionDiagnosticService();
        var service = new SystemScanService(new EmptyCleanupService(), new EmptyUpdateScanService(), interventions);

        var report = await service.RunAsync(ScanMode.Quick, null, CancellationToken.None);

        Assert.False(report.Interventions.WasAnalyzed);
        Assert.Equal(0, interventions.Calls);
    }

    [Fact]
    public async Task Deep_scan_adds_intervention_preview_without_execution()
    {
        var interventions = new RecordingInterventionDiagnosticService(
            Diagnostic(InterventionId.FlushDns) with
            {
                Status = InterventionStatus.Recommended,
                Recommendation = "DNS a verifier."
            });
        var service = new SystemScanService(new EmptyCleanupService(), new EmptyUpdateScanService(), interventions);

        var report = await service.RunAsync(ScanMode.Deep, null, CancellationToken.None);

        Assert.True(report.Interventions.WasAnalyzed);
        Assert.Equal(1, interventions.Calls);
        Assert.Equal(1, report.Interventions.RecommendedActions);
        Assert.Contains(report.Recommendations, recommendation =>
            recommendation.Contains("DNS a verifier.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Report_builder_includes_success_failure_reboot_and_readable_errors()
    {
        var definition = new InterventionCatalog().Get(InterventionId.ResetWinsock);
        var report = new InterventionSessionReport
        {
            StartedAt = FixedUtcNow,
            Duration = TimeSpan.FromSeconds(4),
            ProposedActions = new[] { Diagnostic(InterventionId.ResetWinsock) },
            Results = new[]
            {
                new InterventionExecutionResult
                {
                    Action = definition,
                    Status = InterventionStatus.RebootRequired,
                    ExitCode = 0,
                    WasConfirmed = true,
                    WasElevated = true,
                    RebootRequired = true,
                    StateBefore = "Avant",
                    StateAfter = "Apres"
                },
                new InterventionExecutionResult
                {
                    Action = definition,
                    Status = InterventionStatus.PartialFailure,
                    ExitCode = 2,
                    WasConfirmed = true,
                    WasElevated = true,
                    ReadableError = "Erreur lisible",
                    StateBefore = "Avant",
                    StateAfter = "Partiel"
                }
            },
            Errors = new[] { "Erreur session lisible" }
        };

        var text = new InterventionReportBuilder().Build(report);

        Assert.Contains("Redemarrage requis : oui", text);
        Assert.Contains("Echecs : 1", text);
        Assert.Contains("Erreur : Erreur lisible", text);
        Assert.Contains("Erreur session lisible", text);
    }

    private static ElevatedActionDispatcher CreateDispatcher(
        RecordingElevatedCommandRunner runner,
        bool isAdministrator)
    {
        return new ElevatedActionDispatcher(
            new ElevatedRequestValidator(() => FixedUtcNow, () => Path.GetTempPath()),
            new ElevatedActionAllowlist(),
            runner,
            new FakeSecurityContext(isAdministrator),
            () => @"C:\Windows\System32");
    }

    private static ElevatedInterventionRequest Request(InterventionId action)
    {
        return new ElevatedInterventionRequest
        {
            ActionId = action,
            Nonce = "ABCDEF0123456789ABCDEF0123456789",
            CreatedAt = FixedUtcNow,
            ResultPath = Path.Combine(Path.GetTempPath(), "Virgil", "Temp", "intervention-ABCDEF0123456789ABCDEF0123456789.result.json")
        };
    }

    private static InterventionDiagnostic Diagnostic(InterventionId id)
    {
        return new InterventionDiagnostic
        {
            Definition = new InterventionCatalog().Get(id),
            IsAvailable = true,
            Status = InterventionStatus.Available,
            StateBefore = "Etat test",
            Recommendation = "Action test"
        };
    }

    private static string WriteValidRequest(
        string root,
        string nonce = "ABCDEF0123456789ABCDEF0123456789",
        DateTimeOffset? createdAt = null,
        string? resultPath = null)
    {
        var request = new ElevatedInterventionRequest
        {
            ActionId = InterventionId.FlushDns,
            Nonce = nonce,
            CreatedAt = createdAt ?? FixedUtcNow,
            ResultPath = resultPath ?? Path.Combine(root, $"intervention-{nonce}.result.json")
        };
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"intervention-{Guid.NewGuid():N}.request.json");
        File.WriteAllText(path, JsonSerializer.Serialize(request));
        return path;
    }

    private static string WriteRequest(string root, string jsonTemplate)
    {
        Directory.CreateDirectory(root);
        var resultPath = Path.Combine(root, "intervention-ABCDEF0123456789ABCDEF0123456789.result.json")
            .Replace(@"\", @"\\");
        var path = Path.Combine(root, $"intervention-{Guid.NewGuid():N}.request.json");
        File.WriteAllText(path, jsonTemplate.Replace("__RESULT__", resultPath));
        return path;
    }

    private sealed class RecordingElevatedCommandRunner : IElevatedCommandRunner
    {
        private readonly Func<ElevatedCommandSpec, ElevatedCommandResult> _response;

        public RecordingElevatedCommandRunner()
            : this(_ => new ElevatedCommandResult(0, string.Empty, string.Empty))
        {
        }

        public RecordingElevatedCommandRunner(Func<ElevatedCommandSpec, ElevatedCommandResult> response)
        {
            _response = response;
        }

        public List<ElevatedCommandSpec> Commands { get; } = new();

        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command)
        {
            Commands.Add(command);
            return Task.FromResult(_response(command));
        }
    }

    private sealed class FakeSecurityContext(bool isAdministrator) : IElevatedSecurityContext
    {
        public bool IsAdministrator { get; } = isAdministrator;
    }

    private sealed class RecordingHelperClient : IInterventionElevatedHelperClient
    {
        public int Calls { get; private set; }

        public Task<ElevatedInterventionResult> ExecuteAsync(
            InterventionDefinition definition,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ElevatedInterventionResult
            {
                ActionId = definition.Id,
                Status = InterventionStatus.Completed
            });
        }
    }

    private sealed class FakeExplorerRestarter : IExplorerRestarter
    {
        public int Calls { get; private set; }

        public Task<InterventionExecutionResult> RestartAsync(
            InterventionDiagnostic diagnostic,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new InterventionExecutionResult
            {
                Action = diagnostic.Definition,
                Status = InterventionStatus.Completed,
                ExitCode = 0,
                StateBefore = diagnostic.StateBefore,
                StateAfter = "Explorer actif.",
                WasConfirmed = true,
                WasElevated = false
            });
        }
    }

    private sealed class RecordingElevatedProcessLauncher : IElevatedProcessLauncher
    {
        public int Calls { get; private set; }

        public Task<int> RunElevatedAsync(
            string helperPath,
            string requestPath,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(0);
        }
    }

    private sealed class RecordingInterventionDiagnosticService : IInterventionDiagnosticService
    {
        private readonly IReadOnlyList<InterventionDiagnostic> _diagnostics;

        public RecordingInterventionDiagnosticService(params InterventionDiagnostic[] diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public int Calls { get; private set; }

        public Task<IReadOnlyList<InterventionDiagnostic>> DiagnoseAllAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_diagnostics);
        }

        public Task<InterventionDiagnostic> DiagnoseAsync(InterventionId id, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class EmptyCleanupService : ICleanupService
    {
        public CleanupPreview PreviewTemporaryFiles()
        {
            return new CleanupPreview(DateTimeOffset.Now, Array.Empty<CleanupTarget>());
        }
    }

    private sealed class EmptyUpdateScanService : IUpdateScanService
    {
        public Task<UpdateScanReport> ScanAsync(
            UpdateScanRequest request,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new UpdateScanReport
            {
                Scope = request.Scope,
                OverallStatus = "A jour",
                Winget = WingetAvailability.Unavailable("WinGet non detecte.")
            });
        }
    }

    private sealed class TemporarySandbox : IDisposable
    {
        private TemporarySandbox(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporarySandbox Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "virgil-intervention-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TemporarySandbox(root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
                }
            }
            catch
            {
                // Test cleanup is best effort only.
            }
        }
    }
}
