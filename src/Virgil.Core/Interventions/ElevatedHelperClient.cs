using System.Diagnostics;
using Virgil.Domain;

namespace Virgil.Core.Interventions;

public sealed class ElevatedHelperClient : IInterventionElevatedHelperClient
{
    private readonly ElevatedHelperRequestStore _requestStore;
    private readonly IElevatedProcessLauncher _processLauncher;
    private readonly Func<string> _helperPathProvider;

    public ElevatedHelperClient()
        : this(
            new ElevatedHelperRequestStore(),
            new ElevatedProcessLauncher(),
            () => Path.Combine(AppContext.BaseDirectory, "Virgil.ElevatedHelper.exe"))
    {
    }

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
    {
        cancellationToken.ThrowIfCancellationRequested();

        var helperPath = _helperPathProvider();
        if (!File.Exists(helperPath))
        {
            return Failure(definition, "Assistant eleve introuvable.");
        }

        ElevatedInterventionRequestFile requestFile;
        try
        {
            requestFile = await _requestStore.CreateAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Failure(definition, "Dossier temporaire Virgil inaccessible.");
        }

        try
        {
            var helperExitCode = await _processLauncher
                .RunElevatedAsync(helperPath, requestFile.RequestPath, cancellationToken)
                .ConfigureAwait(false);
            var result = await _requestStore.ReadResultAsync(requestFile, cancellationToken).ConfigureAwait(false);

            return result ?? Failure(definition, $"Assistant eleve termine sans resultat ({helperExitCode}).");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Failure(definition, "Assistant eleve indisponible ou refuse.");
        }
        finally
        {
            _requestStore.Cleanup(requestFile);
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
    public async Task<int> RunElevatedAsync(
        string helperPath,
        string requestPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = Quote(requestPath),
            WindowStyle = ProcessWindowStyle.Hidden
        });

        if (process is null)
        {
            return -1;
        }

        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", string.Empty) + "\"";
    }
}
