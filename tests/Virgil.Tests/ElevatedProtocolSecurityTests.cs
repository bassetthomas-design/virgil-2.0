using System.Text.Json;
using Virgil.Core.Interventions;
using Virgil.Domain;
using Virgil.ElevatedHelper;
using Xunit;

namespace Virgil.Tests;

public sealed class ElevatedProtocolSecurityTests
{
    private const string Nonce = "ABCDEF0123456789ABCDEF0123456789";
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 6, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Path_guard_rejects_reparse_temp_root()
    {
        using var sandbox = TemporarySandbox.Create();
        var root = ProtocolRootPath(sandbox.Root);
        var guard = GuardWithReparsePoint(sandbox.Root, root, reparseIsDirectory: true);

        Assert.Throws<InvalidOperationException>(() => guard.ValidateFilePath(
            sandbox.Root,
            Path.Combine(root, $"intervention-{Nonce}.request.json"),
            ElevatedPathExistence.MustNotExist));
    }

    [Fact]
    public void Path_guard_rejects_reparse_parent_directory()
    {
        using var sandbox = TemporarySandbox.Create();
        var virgilDirectory = Path.Combine(sandbox.Root, "Virgil");
        var root = ProtocolRootPath(sandbox.Root);
        var guard = GuardWithReparsePoint(sandbox.Root, virgilDirectory, reparseIsDirectory: true);

        Assert.Throws<InvalidOperationException>(() => guard.ValidateFilePath(
            sandbox.Root,
            Path.Combine(root, $"intervention-{Nonce}.request.json"),
            ElevatedPathExistence.MustNotExist));
    }

    [Fact]
    public void Path_guard_rejects_reparse_request_file()
    {
        using var sandbox = TemporarySandbox.Create();
        var root = ProtocolRootPath(sandbox.Root);
        var requestPath = Path.Combine(root, $"intervention-{Nonce}.request.json");
        var guard = GuardWithReparsePoint(sandbox.Root, requestPath, reparseIsDirectory: false);

        Assert.Throws<InvalidOperationException>(() => guard.ValidateFilePath(
            sandbox.Root,
            requestPath,
            ElevatedPathExistence.MustExist));
    }

    [Fact]
    public void Path_guard_rejects_reparse_result_file()
    {
        using var sandbox = TemporarySandbox.Create();
        var root = ProtocolRootPath(sandbox.Root);
        var resultPath = Path.Combine(root, $"intervention-{Nonce}.result.json");
        var guard = GuardWithReparsePoint(sandbox.Root, resultPath, reparseIsDirectory: false);

        Assert.Throws<InvalidOperationException>(() => guard.ValidateFilePath(
            sandbox.Root,
            resultPath,
            ElevatedPathExistence.Optional));
    }

    [Fact]
    public async Task Existing_result_is_rejected_before_execution()
    {
        using var sandbox = TemporarySandbox.Create();
        var fixture = WriteRequest(sandbox.Root, FixedUtcNow);
        File.WriteAllText(fixture.Request.ResultPath, "occupied");
        var runner = new RecordingCommandRunner();
        var dispatcher = CreateDispatcher(fixture.Validator, runner);

        var exitCode = await dispatcher.RunAsync(new[] { fixture.RequestPath });

        Assert.Equal(10, exitCode);
        Assert.Empty(runner.Commands);
        Assert.Equal("occupied", File.ReadAllText(fixture.Request.ResultPath));
    }

    [Fact]
    public async Task Existing_request_is_never_overwritten()
    {
        using var sandbox = TemporarySandbox.Create();
        var guard = new ElevatedPathGuard();
        var protocolRoot = new ElevatedProtocolRoot(guard);
        var root = protocolRoot.EnsureCreated(sandbox.Root);
        var requestPath = Path.Combine(root, $"intervention-{Nonce}.request.json");
        File.WriteAllText(requestPath, "original");
        var store = CreateStore(sandbox.Root, guard, protocolRoot, () => FixedUtcNow);

        await Assert.ThrowsAsync<IOException>(() =>
            store.CreateAsync(InterventionId.FlushDns, CancellationToken.None));

        Assert.Equal("original", File.ReadAllText(requestPath));
    }

    [Fact]
    public async Task Request_older_than_ten_minutes_is_rejected()
    {
        using var sandbox = TemporarySandbox.Create();
        var fixture = WriteRequest(sandbox.Root, FixedUtcNow.AddMinutes(-10).AddSeconds(-1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Validator.ValidateAsync(fixture.RequestPath));
    }

    [Fact]
    public async Task Request_more_than_two_minutes_in_future_is_rejected()
    {
        using var sandbox = TemporarySandbox.Create();
        var fixture = WriteRequest(sandbox.Root, FixedUtcNow.AddMinutes(2).AddSeconds(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Validator.ValidateAsync(fixture.RequestPath));
    }

    [Fact]
    public async Task Small_future_clock_skew_is_allowed()
    {
        using var sandbox = TemporarySandbox.Create();
        var fixture = WriteRequest(sandbox.Root, FixedUtcNow.AddMinutes(1));

        var validated = await fixture.Validator.ValidateAsync(fixture.RequestPath);

        Assert.Equal(Nonce, validated.Request.Nonce);
        Assert.True(File.Exists(validated.ProcessingPath));
        Assert.True(File.Exists(validated.ClaimPath));
    }

    [Fact]
    public async Task Request_file_name_must_exactly_match_nonce()
    {
        using var sandbox = TemporarySandbox.Create();
        var fixture = WriteRequest(
            sandbox.Root,
            FixedUtcNow,
            requestFileName: "intervention-FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF.request.json");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Validator.ValidateAsync(fixture.RequestPath));
    }

    [Fact]
    public async Task Result_file_name_must_exactly_match_nonce()
    {
        using var sandbox = TemporarySandbox.Create();
        var fixture = WriteRequest(
            sandbox.Root,
            FixedUtcNow,
            resultFileName: $"intervention-{Nonce}.result-extra.json");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Validator.ValidateAsync(fixture.RequestPath));
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("action")]
    [InlineData("protocol")]
    [InlineData("chronology")]
    [InlineData("early-start")]
    [InlineData("future-start")]
    [InlineData("future-finish")]
    public async Task Application_rejects_tampered_results(string tampering)
    {
        using var sandbox = TemporarySandbox.Create();
        var current = FixedUtcNow;
        var guard = new ElevatedPathGuard();
        var protocolRoot = new ElevatedProtocolRoot(guard);
        var store = CreateStore(sandbox.Root, guard, protocolRoot, () => current);
        var requestFile = await store.CreateAsync(InterventionId.FlushDns, CancellationToken.None);
        current = FixedUtcNow.AddSeconds(5);
        var validResult = ValidResult(requestFile, FixedUtcNow.AddSeconds(1), FixedUtcNow.AddSeconds(2));
        var tampered = tampering switch
        {
            "nonce" => validResult with { Nonce = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF" },
            "action" => validResult with { ActionId = InterventionId.SfcScan },
            "protocol" => validResult with { ProtocolVersion = 2 },
            "chronology" => validResult with
            {
                StartedAt = FixedUtcNow.AddSeconds(3),
                FinishedAt = FixedUtcNow.AddSeconds(2)
            },
            "early-start" => validResult with { StartedAt = FixedUtcNow.AddMinutes(-3) },
            "future-start" => validResult with
            {
                StartedAt = current.AddMinutes(3),
                FinishedAt = current.AddMinutes(3).AddSeconds(1)
            },
            "future-finish" => validResult with { FinishedAt = current.AddMinutes(3) },
            _ => throw new InvalidOperationException("Cas de test inconnu.")
        };
        File.WriteAllText(requestFile.ResultPath, JsonSerializer.Serialize(tampered));

        var result = await store.ReadResultAsync(requestFile, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(InterventionStatus.Failed, result.Status);
        Assert.Contains("refuse", result.ReadableError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Second_execution_with_same_nonce_is_rejected()
    {
        using var sandbox = TemporarySandbox.Create();
        var fixture = WriteRequest(sandbox.Root, FixedUtcNow);
        var first = await fixture.Validator.ValidateAsync(fixture.RequestPath);
        File.Delete(first.ProcessingPath);
        File.WriteAllText(first.OriginalRequestPath, JsonSerializer.Serialize(first.Request));

        await Assert.ThrowsAnyAsync<IOException>(() =>
            fixture.Validator.ValidateAsync(first.OriginalRequestPath));

        Assert.True(File.Exists(first.ClaimPath));
    }

    [Fact]
    public async Task Result_is_written_atomically_without_temporary_file_left_behind()
    {
        using var sandbox = TemporarySandbox.Create();
        var fixture = WriteRequest(sandbox.Root, FixedUtcNow);
        var validated = await fixture.Validator.ValidateAsync(fixture.RequestPath);
        var writer = new ElevatedAtomicResultWriter(
            fixture.Validator.PathGuard,
            fixture.Validator.ProtocolRoot,
            () => new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var result = ValidResult(
            validated.Request,
            FixedUtcNow.AddSeconds(1),
            FixedUtcNow.AddSeconds(2));

        await writer.WriteAsync(validated, result);

        Assert.True(File.Exists(validated.Request.ResultPath));
        Assert.Empty(Directory.EnumerateFiles(validated.RootDirectory, "*.tmp"));
        var persisted = JsonSerializer.Deserialize<ElevatedInterventionResult>(
            File.ReadAllText(validated.Request.ResultPath));
        Assert.Equal(InterventionStatus.Completed, persisted!.Status);
    }

    [Fact]
    public async Task Normal_secure_protocol_flow_remains_valid()
    {
        using var sandbox = TemporarySandbox.Create();
        var current = FixedUtcNow;
        var guard = new ElevatedPathGuard();
        var protocolRoot = new ElevatedProtocolRoot(guard);
        var store = CreateStore(sandbox.Root, guard, protocolRoot, () => current);
        var requestFile = await store.CreateAsync(InterventionId.FlushDns, CancellationToken.None);
        var validator = new ElevatedRequestValidator(
            () => current,
            () => sandbox.Root,
            guard,
            protocolRoot);
        var validated = await validator.ValidateAsync(requestFile.RequestPath);
        current = FixedUtcNow.AddSeconds(5);
        var writer = new ElevatedAtomicResultWriter(guard, protocolRoot);
        await writer.WriteAsync(
            validated,
            ValidResult(requestFile, FixedUtcNow.AddSeconds(1), FixedUtcNow.AddSeconds(2)));

        var result = await store.ReadResultAsync(requestFile, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(InterventionStatus.Completed, result.Status);
        Assert.Equal(Nonce, result.Nonce);
        Assert.Equal(InterventionId.FlushDns, result.ActionId);
    }

    private static ElevatedActionDispatcher CreateDispatcher(
        ElevatedRequestValidator validator,
        RecordingCommandRunner runner)
    {
        return new ElevatedActionDispatcher(
            validator,
            new ElevatedActionAllowlist(),
            runner,
            new AdministratorSecurityContext(),
            () => @"C:\Windows\System32");
    }

    private static ElevatedHelperRequestStore CreateStore(
        string localAppData,
        ElevatedPathGuard guard,
        ElevatedProtocolRoot protocolRoot,
        Func<DateTimeOffset> utcNow)
    {
        return new ElevatedHelperRequestStore(
            () => localAppData,
            guard,
            protocolRoot,
            () => Convert.FromHexString(Nonce),
            utcNow);
    }

    private static RequestFixture WriteRequest(
        string localAppData,
        DateTimeOffset createdAt,
        string? requestFileName = null,
        string? resultFileName = null)
    {
        var root = new ElevatedProtocolRoot().EnsureCreated(localAppData);
        var requestPath = Path.Combine(
            root,
            requestFileName ?? $"intervention-{Nonce}.request.json");
        var resultPath = Path.Combine(
            root,
            resultFileName ?? $"intervention-{Nonce}.result.json");
        var request = new ElevatedInterventionRequest
        {
            ProtocolVersion = 1,
            ActionId = InterventionId.FlushDns,
            Nonce = Nonce,
            CreatedAt = createdAt,
            ResultPath = resultPath
        };
        File.WriteAllText(requestPath, JsonSerializer.Serialize(request));
        var validator = new ElevatedRequestValidator(() => FixedUtcNow, () => localAppData);
        return new RequestFixture(validator, request, requestPath);
    }

    private static ElevatedInterventionResult ValidResult(
        ElevatedInterventionRequestFile requestFile,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt)
    {
        return new ElevatedInterventionResult
        {
            ProtocolVersion = requestFile.ProtocolVersion,
            ActionId = requestFile.ActionId,
            Nonce = requestFile.Nonce,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            ExitCode = 0,
            Status = InterventionStatus.Completed,
            SummaryOutput = "ok"
        };
    }

    private static ElevatedInterventionResult ValidResult(
        ElevatedInterventionRequest request,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt)
    {
        return new ElevatedInterventionResult
        {
            ProtocolVersion = request.ProtocolVersion,
            ActionId = request.ActionId,
            Nonce = request.Nonce,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            ExitCode = 0,
            Status = InterventionStatus.Completed,
            SummaryOutput = "ok"
        };
    }

    private static ElevatedPathGuard GuardWithReparsePoint(
        string localAppData,
        string reparsePath,
        bool reparseIsDirectory)
    {
        var fullLocalAppData = Normalize(localAppData);
        var virgilDirectory = Normalize(Path.Combine(fullLocalAppData, "Virgil"));
        var rootDirectory = Normalize(Path.Combine(virgilDirectory, "Temp"));
        var fullReparsePath = Normalize(reparsePath);

        return new ElevatedPathGuard(path =>
        {
            var fullPath = Normalize(path);
            if (string.Equals(fullPath, fullReparsePath, StringComparison.OrdinalIgnoreCase))
            {
                var attributes = FileAttributes.ReparsePoint;
                if (reparseIsDirectory)
                {
                    attributes |= FileAttributes.Directory;
                }

                return new ElevatedPathEntry(true, attributes);
            }

            if (string.Equals(fullPath, fullLocalAppData, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullPath, virgilDirectory, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullPath, rootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return new ElevatedPathEntry(true, FileAttributes.Directory);
            }

            return ElevatedPathEntry.Missing;
        });
    }

    private static string ProtocolRootPath(string localAppData)
    {
        return Path.Combine(localAppData, "Virgil", "Temp");
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed record RequestFixture(
        ElevatedRequestValidator Validator,
        ElevatedInterventionRequest Request,
        string RequestPath);

    private sealed class RecordingCommandRunner : IElevatedCommandRunner
    {
        public List<ElevatedCommandSpec> Commands { get; } = new();

        public Task<ElevatedCommandResult> RunAsync(ElevatedCommandSpec command)
        {
            Commands.Add(command);
            return Task.FromResult(new ElevatedCommandResult(0, "ok", string.Empty));
        }
    }

    private sealed class AdministratorSecurityContext : IElevatedSecurityContext
    {
        public bool IsAdministrator => true;
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
            var root = Path.Combine(
                Path.GetTempPath(),
                "virgil-elevated-security-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TemporarySandbox(root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // Test cleanup is best effort only.
            }
        }
    }
}
