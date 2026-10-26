using Virgil.Contracts.Elevated;
using Virgil.Contracts;

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
            new ElevatedAtomicResultWriter(validator.PathGuard, validator.ValidateProtocolRoot))
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
            using var channel = new ElevatedProgressChannel(_validator, validated);
            var result = await ExecuteValidatedAsync(validated.Request, channel, channel.Token).ConfigureAwait(false);
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

    public async Task<ElevatedInterventionResult> ExecuteValidatedAsync(ElevatedInterventionRequest request,
        IProgress<InterventionProgress>? progress = null, CancellationToken cancellationToken = default)
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
        var checkFailed = false;

        foreach (var command in spec.Commands)
        {
            if (cancellationToken.IsCancellationRequested)
                return Result(request, startedAt, -1, InterventionStatus.Cancelled, "Parcours annulé entre deux commandes.", false);
            progress?.Report(new InterventionProgress { Step = command.FileName });
            var commandResult = await _commandRunner.RunAsync(command,
                new CommandProgress(value => progress?.Report(new InterventionProgress { Step = command.FileName, Percent = value })),
                cancellationToken).ConfigureAwait(false);
            exitCode = commandResult.ExitCode;
            checkFailed |= commandResult.ExitCode != 0;
            if (request.ActionId is InterventionId.VerifyHealth or InterventionId.VerifyHealthDeep) outputs.Add(IntegrityFinding(command, commandResult));
            outputs.Add("Étape : " + command.FileName + " " + string.Join(" ", command.Arguments) + "\n" + Summarize(commandResult));

            if (commandResult.Cancelled)
                return Result(request, startedAt, -1, InterventionStatus.Cancelled, string.Join("\n", outputs), false);
            if (commandResult.ExitCode != 0 && request.ActionId is not (InterventionId.VerifyHealth or InterventionId.VerifyHealthDeep))
            {
                return Result(request, startedAt, commandResult.ExitCode,
                    InterventionStatus.PartialFailure,
                    string.Join("\n", outputs),
                    spec.RebootRequiredOnSuccess);
            }
        }

        if (request.ActionId is InterventionId.VerifyHealth or InterventionId.VerifyHealthDeep)
        {
            var text = string.Join("\n", outputs);
            var incomplete = checkFailed || text.Contains("|UNKNOWN|", StringComparison.Ordinal);
            var finding = text.Contains("|FINDING|", StringComparison.Ordinal);
            outputs.Add(incomplete ? "Vérification incomplète : certains contrôles sont indisponibles. Consultez les détails."
                : finding ? "Des éléments demandent votre attention. Consultez les constats ci-dessus." : "Tout est sain.");
            return Result(request, startedAt, checkFailed ? -1 : 0,
                checkFailed ? InterventionStatus.PartialFailure : InterventionStatus.Completed,
                string.Join("\n", outputs), false);
        }
        var status = spec.RebootRequiredOnSuccess
            ? InterventionStatus.RebootRequired
            : InterventionStatus.Completed;
        return Result(request, startedAt, exitCode, status, string.Join("\n", outputs), spec.RebootRequiredOnSuccess);
    }

    internal static string IntegrityFinding(ElevatedCommandSpec command, ElevatedCommandResult result)
    {
        var output = result.StandardOutput;
        var key = command.FileName switch { "sfc.exe" => "SFC", "dism.exe" => "DISM", "chkdsk.exe" => "CHKDSK", _ => "Services" };
        if (key == "Services") return result.ExitCode == 0 ? "" : "VIRGIL|Services|UNKNOWN|Contrôles des services indisponibles";
        if (result.ExitCode != 0) return $"VIRGIL|{key}|UNKNOWN|Contrôle non terminé : {result.StandardError}";
        var healthy = key switch
        {
            "SFC" => output.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("aucune violation d’intégrité", StringComparison.OrdinalIgnoreCase) || output.Contains("aucune violation d'intégrité", StringComparison.OrdinalIgnoreCase),
            "DISM" => output.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase),
            "CHKDSK" => true, // The documented CHKDSK exit code 0 means no errors were found.
            _ => false
        };
        var damaged = key == "DISM" && output.Contains("component store is repairable", StringComparison.OrdinalIgnoreCase) ||
            key == "SFC" && (output.Contains("found integrity violations", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("a trouvé des violations d’intégrité", StringComparison.OrdinalIgnoreCase) || output.Contains("a trouvé des violations d'intégrité", StringComparison.OrdinalIgnoreCase));
        return $"VIRGIL|{key}|{(healthy ? "OK" : damaged ? "FINDING" : "UNKNOWN")}|" +
            (healthy ? "Aucune anomalie détectée" : damaged ? "Corruption détectée" : "Résultat non interprétable ; consultez la sortie de l’outil");
    }

    private sealed class CommandProgress(Action<int?> report) : IProgress<int?>
    {
        public void Report(int? value) => report(value);
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
            .Take(200);
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
