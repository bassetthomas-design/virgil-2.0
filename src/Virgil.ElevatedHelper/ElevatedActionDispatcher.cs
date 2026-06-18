using Virgil.Core.Interventions;
using Virgil.Domain;

namespace Virgil.ElevatedHelper;

public sealed class ElevatedActionDispatcher
{
    private readonly ElevatedRequestValidator _validator;
    private readonly ElevatedActionAllowlist _allowlist;
    private readonly IElevatedCommandRunner _commandRunner;
    private readonly IElevatedSecurityContext _securityContext;
    private readonly Func<string> _systemDirectoryProvider;
    private readonly ElevatedAtomicResultWriter _resultWriter;

    public ElevatedActionDispatcher()
        : this(
            new ElevatedRequestValidator(),
            new ElevatedActionAllowlist(),
            new ElevatedCommandRunner(),
            new ElevatedSecurityContext(),
            () => Environment.SystemDirectory)
    {
    }

    public ElevatedActionDispatcher(
        ElevatedRequestValidator validator,
        ElevatedActionAllowlist allowlist,
        IElevatedCommandRunner commandRunner,
        IElevatedSecurityContext securityContext,
        Func<string> systemDirectoryProvider)
        : this(
            validator,
            allowlist,
            commandRunner,
            securityContext,
            systemDirectoryProvider,
            new ElevatedAtomicResultWriter(validator.PathGuard, validator.ProtocolRoot))
    {
    }

    public ElevatedActionDispatcher(
        ElevatedRequestValidator validator,
        ElevatedActionAllowlist allowlist,
        IElevatedCommandRunner commandRunner,
        IElevatedSecurityContext securityContext,
        Func<string> systemDirectoryProvider,
        ElevatedAtomicResultWriter resultWriter)
    {
        _validator = validator;
        _allowlist = allowlist;
        _commandRunner = commandRunner;
        _securityContext = securityContext;
        _systemDirectoryProvider = systemDirectoryProvider;
        _resultWriter = resultWriter;
    }

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 1)
        {
            return 2;
        }

        ValidatedElevatedRequest validated;
        try
        {
            validated = await _validator.ValidateAsync(args[0]).ConfigureAwait(false);
        }
        catch
        {
            return 10;
        }

        try
        {
            var result = await ExecuteValidatedAsync(validated.Request).ConfigureAwait(false);
            await _resultWriter.WriteAsync(validated, result).ConfigureAwait(false);
            return result.Status is InterventionStatus.Failed or InterventionStatus.PartialFailure ? 20 : 0;
        }
        catch
        {
            return 30;
        }
        finally
        {
            TryDelete(validated.LocalAppDataDirectory, validated.ProcessingPath);
        }
    }

    public async Task<ElevatedInterventionResult> ExecuteValidatedAsync(ElevatedInterventionRequest request)
    {
        var startedAt = DateTimeOffset.UtcNow;
        if (!_allowlist.TryGet(request.ActionId, GetSystemDrive(), out var spec))
        {
            return Result(request, startedAt, -1, InterventionStatus.Failed, "Action refusee par la liste blanche.", false);
        }

        if (spec.RequiresAdministrator && !_securityContext.IsAdministrator)
        {
            return Result(request, startedAt, -1, InterventionStatus.Failed, "Droits administrateur requis.", false);
        }

        var outputs = new List<string>();
        var exitCode = 0;

        foreach (var command in spec.Commands)
        {
            var commandResult = await _commandRunner.RunAsync(command).ConfigureAwait(false);
            exitCode = commandResult.ExitCode;
            outputs.Add(Summarize(commandResult));

            if (commandResult.ExitCode != 0)
            {
                return Result(request, startedAt, commandResult.ExitCode,
                    InterventionStatus.PartialFailure,
                    string.Join("\n", outputs),
                    spec.RebootRequiredOnSuccess);
            }
        }

        var status = spec.RebootRequiredOnSuccess
            ? InterventionStatus.RebootRequired
            : InterventionStatus.Completed;
        return Result(request, startedAt, exitCode, status, string.Join("\n", outputs), spec.RebootRequiredOnSuccess);
    }

    private string GetSystemDrive()
    {
        var root = Path.GetPathRoot(_systemDirectoryProvider())?.TrimEnd('\\');
        return string.IsNullOrWhiteSpace(root) ? string.Empty : root.ToUpperInvariant();
    }

    private static ElevatedInterventionResult Result(
        ElevatedInterventionRequest request,
        DateTimeOffset startedAt,
        int exitCode,
        InterventionStatus status,
        string message,
        bool rebootRequired)
    {
        return new ElevatedInterventionResult
        {
            ProtocolVersion = request.ProtocolVersion,
            ActionId = request.ActionId,
            Nonce = request.Nonce,
            StartedAt = startedAt,
            FinishedAt = DateTimeOffset.UtcNow,
            ExitCode = exitCode,
            Status = status,
            SummaryOutput = message,
            ReadableError = status is InterventionStatus.Failed or InterventionStatus.PartialFailure ? message : null,
            RebootRequired = rebootRequired
        };
    }

    private static string Summarize(ElevatedCommandResult result)
    {
        var lines = string.Join("\n", result.StandardOutput, result.StandardError)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Take(8);
        var text = string.Join("\n", lines);
        return string.IsNullOrWhiteSpace(text)
            ? $"Code sortie : {result.ExitCode}."
            : $"Code sortie : {result.ExitCode}.\n{text}";
    }

    private void TryDelete(string localAppData, string path)
    {
        try
        {
            var validatedPath = _validator.PathGuard.ValidateFilePath(
                localAppData,
                path,
                ElevatedPathExistence.Optional);
            if (File.Exists(validatedPath))
            {
                File.Delete(validatedPath);
            }
        }
        catch
        {
            // Best effort invalidation never follows a reparse point.
        }
    }
}
