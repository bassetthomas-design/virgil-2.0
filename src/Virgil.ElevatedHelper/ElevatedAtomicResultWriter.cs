using System.Security.Cryptography;
using System.Text.Json;
using Virgil.Core.Interventions;
using Virgil.Domain;

namespace Virgil.ElevatedHelper;

public sealed class ElevatedAtomicResultWriter
{
    private readonly ElevatedPathGuard _pathGuard;
    private readonly ElevatedProtocolRoot _protocolRoot;
    private readonly Func<byte[]> _temporaryNonceProvider;

    public ElevatedAtomicResultWriter(
        ElevatedPathGuard pathGuard,
        ElevatedProtocolRoot protocolRoot)
        : this(pathGuard, protocolRoot, () => RandomNumberGenerator.GetBytes(8))
    {
    }

    public ElevatedAtomicResultWriter(
        ElevatedPathGuard pathGuard,
        ElevatedProtocolRoot protocolRoot,
        Func<byte[]> temporaryNonceProvider)
    {
        _pathGuard = pathGuard;
        _protocolRoot = protocolRoot;
        _temporaryNonceProvider = temporaryNonceProvider;
    }

    public async Task WriteAsync(
        ValidatedElevatedRequest validated,
        ElevatedInterventionResult result)
    {
        ValidateResultIdentity(validated.Request, result);
        _protocolRoot.ValidateExisting(validated.LocalAppDataDirectory);
        var resultPath = _pathGuard.ValidateFilePath(
            validated.LocalAppDataDirectory,
            validated.Request.ResultPath,
            ElevatedPathExistence.MustNotExist);
        var temporaryNonce = Convert.ToHexString(_temporaryNonceProvider());
        if (temporaryNonce.Length == 0)
        {
            throw new InvalidOperationException("Nonce temporaire invalide.");
        }

        var temporaryPath = Path.Combine(
            validated.RootDirectory,
            $"intervention-{validated.Request.Nonce}.result-{temporaryNonce}.tmp");
        _pathGuard.ValidateFilePath(
            validated.LocalAppDataDirectory,
            temporaryPath,
            ElevatedPathExistence.MustNotExist);

        try
        {
            await using (var stream = new FileStream(temporaryPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough
            }))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    result,
                    new JsonSerializerOptions { WriteIndented = true }).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            _pathGuard.ValidateFilePath(
                validated.LocalAppDataDirectory,
                temporaryPath,
                ElevatedPathExistence.MustExist);
            _pathGuard.ValidateFilePath(
                validated.LocalAppDataDirectory,
                resultPath,
                ElevatedPathExistence.MustNotExist);
            File.Move(temporaryPath, resultPath, overwrite: false);
            _pathGuard.ValidateFilePath(
                validated.LocalAppDataDirectory,
                resultPath,
                ElevatedPathExistence.MustExist);
        }
        catch
        {
            TryDelete(validated.LocalAppDataDirectory, temporaryPath);
            throw;
        }
    }

    private static void ValidateResultIdentity(
        ElevatedInterventionRequest request,
        ElevatedInterventionResult result)
    {
        if (result.ProtocolVersion != request.ProtocolVersion ||
            result.ActionId != request.ActionId ||
            !string.Equals(result.Nonce, request.Nonce, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Identite du resultat eleve incoherente.");
        }
    }

    private void TryDelete(string localAppData, string path)
    {
        try
        {
            var validatedPath = _pathGuard.ValidateFilePath(
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
            // Best effort cleanup never follows a reparse point.
        }
    }
}
