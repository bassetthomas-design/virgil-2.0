using Virgil.Contracts.Elevated;
using System.Text.Json;
using System.Text.Json.Serialization;
using Virgil.Contracts;

namespace Virgil.Core.Interventions;

public sealed class ElevatedHelperRequestStore
{
    private const int ProtocolVersion = 1;
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    private readonly Func<string> _localAppDataProvider;
    private readonly ElevatedPathGuard _pathGuard;
    private readonly IElevatedProtocolRoot _protocolRoot;
    private readonly IElevatedProtocolFiles _files;
    private readonly Func<byte[]> _nonceProvider;
    private readonly Func<DateTimeOffset> _utcNow;

    public ElevatedHelperRequestStore(
        Func<string> localAppDataProvider,
        IElevatedProtocolRoot protocolRoot,
        IElevatedProtocolFiles files,
        Func<byte[]> nonceProvider,
        Func<DateTimeOffset> utcNow)
    {
        _localAppDataProvider = localAppDataProvider;
        _pathGuard = protocolRoot.PathGuard;
        _protocolRoot = protocolRoot;
        _files = files;
        _nonceProvider = nonceProvider;
        _utcNow = utcNow;
    }

    public string RootDirectory => _pathGuard.GetProtocolRoot(_localAppDataProvider());

    public Task<ElevatedInterventionRequestFile> CreateAsync(InterventionId actionId, CancellationToken cancellationToken)
        => CreateAsync(actionId, null, cancellationToken);

    public async Task<ElevatedInterventionRequestFile> CreateAsync(
        InterventionId actionId, StartupApprovalWrite? payload, CancellationToken cancellationToken)
    {
        if ((actionId == InterventionId.SetStartupApproval) != (payload is not null) || (payload is not null && !payload.IsValid()))
            throw new ArgumentException("Demande de démarrage invalide.");
        var localAppData = _pathGuard.NormalizeLocalAppData(_localAppDataProvider());
        var rootDirectory = _protocolRoot.EnsureCreated(localAppData);
        var nonceBytes = _nonceProvider();
        if (nonceBytes.Length != 16)
        {
            throw new InvalidOperationException("Nonce cryptographique invalide.");
        }

        var nonce = Convert.ToHexString(nonceBytes);
        var requestPath = Path.Combine(rootDirectory, $"intervention-{nonce}.request.json");
        var resultPath = Path.Combine(rootDirectory, $"intervention-{nonce}.result.json");
        var createdAt = _utcNow();
        var request = new ElevatedInterventionRequest
        {
            ProtocolVersion = ProtocolVersion,
            StartupApproval = payload,
            ActionId = actionId,
            Nonce = nonce,
            CreatedAt = createdAt,
            ResultPath = resultPath
        };

        _pathGuard.ValidateFilePath(localAppData, requestPath, ElevatedPathExistence.MustNotExist);
        _pathGuard.ValidateFilePath(localAppData, resultPath, ElevatedPathExistence.MustNotExist);

        try
        {
            await _files.WriteRequestAsync(requestPath, request, JsonOptions(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            TryDelete(localAppData, requestPath);
            throw;
        }

        _pathGuard.ValidateFilePath(localAppData, requestPath, ElevatedPathExistence.MustExist);
        return new ElevatedInterventionRequestFile(
            localAppData,
            requestPath,
            resultPath,
            nonce,
            ProtocolVersion,
            actionId,
            createdAt);
    }

    public async Task<ElevatedInterventionResult?> ReadResultAsync(
        ElevatedInterventionRequestFile requestFile,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateRequestFileMetadata(requestFile);
            _protocolRoot.ValidateExisting(requestFile.LocalAppDataDirectory);
            var resultPath = _pathGuard.ValidateFilePath(
                requestFile.LocalAppDataDirectory,
                requestFile.ResultPath,
                ElevatedPathExistence.Optional);
            if (!_files.Exists(resultPath))
            {
                return null;
            }

            var result = await _files.ReadResultAsync(resultPath, JsonOptions(), cancellationToken)
                .ConfigureAwait(false);

            _pathGuard.ValidateFilePath(
                requestFile.LocalAppDataDirectory,
                resultPath,
                ElevatedPathExistence.MustExist);
            return ValidateResult(requestFile, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return InvalidResult(requestFile, "Resultat eleve refuse : " + ex.Message);
        }
    }

    public void Cleanup(ElevatedInterventionRequestFile requestFile)
    {
        TryDelete(requestFile.LocalAppDataDirectory, requestFile.RequestPath);
        TryDelete(requestFile.LocalAppDataDirectory, requestFile.ResultPath);
    }

    public static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
    }

    private ElevatedInterventionResult ValidateResult(
        ElevatedInterventionRequestFile requestFile,
        ElevatedInterventionResult? result)
    {
        if (result is null)
        {
            return InvalidResult(requestFile, "Resultat eleve vide.");
        }

        if (result.ProtocolVersion != requestFile.ProtocolVersion)
        {
            return InvalidResult(requestFile, "Version protocole du resultat incoherente.");
        }

        if (!string.Equals(result.Nonce, requestFile.Nonce, StringComparison.Ordinal))
        {
            return InvalidResult(requestFile, "Nonce du resultat incoherent.");
        }

        if (result.ActionId != requestFile.ActionId)
        {
            return InvalidResult(requestFile, "Action du resultat incoherente.");
        }

        var now = _utcNow();
        if (result.StartedAt < requestFile.CreatedAt - ClockSkew || result.StartedAt > now + ClockSkew)
        {
            return InvalidResult(requestFile, "Date de debut du resultat incoherente.");
        }

        if (result.FinishedAt < result.StartedAt)
        {
            return InvalidResult(requestFile, "Chronologie du resultat incoherente.");
        }

        if (result.FinishedAt > now + ClockSkew)
        {
            return InvalidResult(requestFile, "Date de fin du resultat trop future.");
        }

        return result;
    }

    private void ValidateRequestFileMetadata(ElevatedInterventionRequestFile requestFile)
    {
        var currentLocalAppData = _pathGuard.NormalizeLocalAppData(_localAppDataProvider());
        if (!string.Equals(
                currentLocalAppData,
                requestFile.LocalAppDataDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Racine LocalAppData modifiee.");
        }

        var rootDirectory = _pathGuard.GetProtocolRoot(currentLocalAppData);
        var expectedRequestPath = Path.Combine(
            rootDirectory,
            $"intervention-{requestFile.Nonce}.request.json");
        var expectedResultPath = Path.Combine(
            rootDirectory,
            $"intervention-{requestFile.Nonce}.result.json");

        if (!string.Equals(expectedRequestPath, requestFile.RequestPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expectedResultPath, requestFile.ResultPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Noms des fichiers protocole incoherents.");
        }
    }

    private ElevatedInterventionResult InvalidResult(
        ElevatedInterventionRequestFile requestFile,
        string message)
    {
        var now = _utcNow();
        var readableMessage = message.StartsWith("Resultat eleve refuse", StringComparison.OrdinalIgnoreCase)
            ? message
            : "Resultat eleve refuse : " + message;
        return new ElevatedInterventionResult
        {
            ProtocolVersion = requestFile.ProtocolVersion,
            ActionId = requestFile.ActionId,
            Nonce = requestFile.Nonce,
            StartedAt = now,
            FinishedAt = now,
            ExitCode = -1,
            Status = InterventionStatus.Failed,
            ReadableError = readableMessage
        };
    }

    private void TryDelete(string localAppData, string path)
    {
        try
        {
            var validatedPath = _pathGuard.ValidateFilePath(
                localAppData,
                path,
                ElevatedPathExistence.Optional);
            if (_files.Exists(validatedPath))
            {
                _files.Delete(validatedPath);
            }
        }
        catch
        {
            // Best effort cleanup never follows a reparse point.
        }
    }
}

public sealed record ElevatedInterventionRequestFile(
    string LocalAppDataDirectory,
    string RequestPath,
    string ResultPath,
    string Nonce,
    int ProtocolVersion,
    InterventionId ActionId,
    DateTimeOffset CreatedAt);
