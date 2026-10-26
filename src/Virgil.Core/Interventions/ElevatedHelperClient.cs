using Virgil.Core.Processes;
using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public sealed class ElevatedHelperClient : IInterventionElevatedHelperClient
{
    private readonly ElevatedHelperRequestStore _requestStore;
    private readonly IElevatedProcessLauncher _processLauncher;
    private readonly Func<string> _helperPathProvider;

    public ElevatedHelperClient(
        ElevatedHelperRequestStore requestStore,
        IElevatedProcessLauncher processLauncher,
        Func<string> helperPathProvider)
    {
        _requestStore = requestStore;
        _processLauncher = processLauncher;
        _helperPathProvider = helperPathProvider;
    }

    public async Task<ElevatedInterventionResult> ExecuteAsync(
        InterventionDefinition definition,
        CancellationToken cancellationToken)
        => await ExecuteAsync(definition, null, cancellationToken).ConfigureAwait(false);

    public async Task<ElevatedInterventionResult> ExecuteAsync(InterventionDefinition definition,
        IProgress<InterventionProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var helperPath = _helperPathProvider();
        if (!File.Exists(helperPath))
        {
            return Failure(definition, "Assistant élevé introuvable.");
        }

        ElevatedInterventionRequestFile requestFile;
        try
        {
            requestFile = await _requestStore.CreateAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return Failure(definition, "Dossier temporaire Virgil inaccessible.");
        }

        var helperFinished = false;
        try
        {
            // Keep the protocol alive until the helper has acknowledged cancellation and exited.
            using var timeout = new CancellationTokenSource(TimeSpan.FromHours(4));
            var launch = _processLauncher.RunElevatedAsync(helperPath, requestFile.RequestPath, timeout.Token);
            while (!launch.IsCompleted)
            {
                if (cancellationToken.IsCancellationRequested) _requestStore.RequestCancel(requestFile);
                var update = await _requestStore.ReadProgressAsync(requestFile, timeout.Token).ConfigureAwait(false);
                if (update is not null && cancellationToken.IsCancellationRequested)
                    update = update with { Step = "Annulation demandée ; fin de l’étape en cours : " + update.Step };
                progress?.Report(update ?? new InterventionProgress
                {
                    Nonce = requestFile.Nonce,
                    Step = cancellationToken.IsCancellationRequested ? "Annulation demandée, arrêt en cours…" : "Élévation ou vérification en cours…",
                    Elapsed = DateTimeOffset.UtcNow - requestFile.CreatedAt
                });
                await Task.WhenAny(launch, Task.Delay(500, timeout.Token)).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
            }
            var helperExitCode = await launch.ConfigureAwait(false);
            helperFinished = true;
            var result = await _requestStore.ReadResultAsync(requestFile, CancellationToken.None).ConfigureAwait(false);

            return result ?? Failure(definition, $"Assistant élevé terminé sans fichier de résultat ({helperExitCode}).");
        }
        catch (OperationCanceledException)
        {
            _requestStore.RequestCancel(requestFile);
            return Failure(definition, "Délai maximal de l’assistant élevé dépassé ; arrêt demandé.");
        }
        catch (Exception ex)
        {
            return Failure(definition, "Assistant élevé indisponible : " + ex.Message);
        }
        finally
        {
            if (helperFinished) _requestStore.Cleanup(requestFile);
        }
    }

    private static ElevatedInterventionResult Failure(InterventionDefinition definition, string message)
    {
        var now = DateTimeOffset.UtcNow;
        return new ElevatedInterventionResult
        {
            ActionId = definition.Id,
            StartedAt = now,
            FinishedAt = now,
            ExitCode = -1,
            Status = InterventionStatus.Failed,
            ReadableError = message
        };
    }
}

public sealed class ElevatedProcessLauncher : IElevatedProcessLauncher
{
    private readonly IProcessRunner _runner;

    public ElevatedProcessLauncher(IProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<int> RunElevatedAsync(
        string helperPath,
        string requestPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _runner.LaunchAsync(new ProcessLaunchRequest(
            helperPath, Array.Empty<string>(), UseShellExecute: true, Verb: "runas",
            WindowMode: ProcessWindowMode.Hidden, RawArguments: Quote(requestPath), WaitForExit: true),
            cancellationToken).ConfigureAwait(false);
        if (result.LaunchError is not null)
        {
            throw new InvalidOperationException(result.LaunchError);
        }
        return result.Started ? result.ExitCode ?? -1 : -1;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", string.Empty) + "\"";
    }
}
