using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Virgil.Core.Interventions;
using Virgil.Domain;

namespace Virgil.ElevatedHelper;

public sealed class ElevatedRequestValidator
{
    private static readonly Regex NoncePattern = new("^[A-F0-9]{32}$", RegexOptions.Compiled);
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ClaimRetention = TimeSpan.FromMinutes(15);

    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<string> _localAppDataProvider;
    private readonly ElevatedPathGuard _pathGuard;
    private readonly ElevatedProtocolRoot _protocolRoot;

    public ElevatedRequestValidator()
        : this(() => DateTimeOffset.UtcNow, () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    public ElevatedRequestValidator(Func<DateTimeOffset> utcNow, Func<string> localAppDataProvider)
    {
        _utcNow = utcNow;
        _localAppDataProvider = localAppDataProvider;
        _pathGuard = new ElevatedPathGuard();
        _protocolRoot = new ElevatedProtocolRoot(_pathGuard);
    }

    public ElevatedRequestValidator(
        Func<DateTimeOffset> utcNow,
        Func<string> localAppDataProvider,
        ElevatedPathGuard pathGuard,
        ElevatedProtocolRoot protocolRoot)
    {
        _utcNow = utcNow;
        _localAppDataProvider = localAppDataProvider;
        _pathGuard = pathGuard;
        _protocolRoot = protocolRoot;
    }

    public string RootDirectory => _pathGuard.GetProtocolRoot(_localAppDataProvider());

    public ElevatedPathGuard PathGuard => _pathGuard;

    public ElevatedProtocolRoot ProtocolRoot => _protocolRoot;

    public async Task<ValidatedElevatedRequest> ValidateAsync(string requestPath)
    {
        var localAppData = _pathGuard.NormalizeLocalAppData(_localAppDataProvider());
        var rootDirectory = _protocolRoot.ValidateExisting(localAppData);
        var fullRequestPath = _pathGuard.ValidateFilePath(
            localAppData,
            requestPath,
            ElevatedPathExistence.MustExist);
        var request = await ReadRequestAsync(fullRequestPath).ConfigureAwait(false);

        ValidateRequest(request, fullRequestPath, rootDirectory, localAppData);
        _pathGuard.ValidateFilePath(localAppData, fullRequestPath, ElevatedPathExistence.MustExist);
        _pathGuard.ValidateFilePath(localAppData, request.ResultPath, ElevatedPathExistence.MustNotExist);

        PruneExpiredClaims(localAppData, rootDirectory);
        var claimPath = Path.Combine(rootDirectory, $"intervention-{request.Nonce}.claim");
        var processingPath = Path.Combine(rootDirectory, $"intervention-{request.Nonce}.processing.json");
        _pathGuard.ValidateFilePath(localAppData, claimPath, ElevatedPathExistence.MustNotExist);
        _pathGuard.ValidateFilePath(localAppData, processingPath, ElevatedPathExistence.MustNotExist);

        await CreateClaimAsync(claimPath).ConfigureAwait(false);
        _pathGuard.ValidateFilePath(localAppData, claimPath, ElevatedPathExistence.MustExist);
        try
        {
            File.Move(fullRequestPath, processingPath, overwrite: false);
            _pathGuard.ValidateFilePath(localAppData, processingPath, ElevatedPathExistence.MustExist);
            _pathGuard.ValidateFilePath(localAppData, fullRequestPath, ElevatedPathExistence.MustNotExist);

            var claimedRequest = await ReadRequestAsync(processingPath).ConfigureAwait(false);
            ValidateRequest(claimedRequest, fullRequestPath, rootDirectory, localAppData);
            if (claimedRequest != request)
            {
                throw new InvalidOperationException("Requete modifiee pendant sa validation.");
            }

            _pathGuard.ValidateFilePath(localAppData, request.ResultPath, ElevatedPathExistence.MustNotExist);
            return new ValidatedElevatedRequest(
                request,
                localAppData,
                rootDirectory,
                fullRequestPath,
                processingPath,
                claimPath);
        }
        catch
        {
            TryDelete(localAppData, processingPath);
            throw;
        }
    }

    public bool IsPathUnderRoot(string path)
    {
        try
        {
            var localAppData = _pathGuard.NormalizeLocalAppData(_localAppDataProvider());
            _pathGuard.ValidateFilePath(localAppData, path, ElevatedPathExistence.Optional);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
    }

    private async Task<ElevatedInterventionRequest> ReadRequestAsync(string path)
    {
        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        return await JsonSerializer.DeserializeAsync<ElevatedInterventionRequest>(stream, JsonOptions())
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Requete invalide.");
    }

    private void ValidateRequest(
        ElevatedInterventionRequest request,
        string originalRequestPath,
        string rootDirectory,
        string localAppData)
    {
        if (request.ProtocolVersion != 1)
        {
            throw new InvalidOperationException("Version protocole refusee.");
        }

        if (!NoncePattern.IsMatch(request.Nonce))
        {
            throw new InvalidOperationException("Nonce invalide.");
        }

        var now = _utcNow();
        if (request.CreatedAt < now - MaximumAge)
        {
            throw new InvalidOperationException("Requete expiree.");
        }

        if (request.CreatedAt > now + MaximumFutureSkew)
        {
            throw new InvalidOperationException("Date future de la requete refusee.");
        }

        var expectedRequestPath = Path.Combine(
            rootDirectory,
            $"intervention-{request.Nonce}.request.json");
        if (!string.Equals(expectedRequestPath, originalRequestPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Nom de requete non associe au nonce.");
        }

        var expectedResultPath = Path.Combine(
            rootDirectory,
            $"intervention-{request.Nonce}.result.json");
        if (!string.Equals(expectedResultPath, Path.GetFullPath(request.ResultPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Nom de resultat non associe au nonce.");
        }

        _pathGuard.ValidateFilePath(localAppData, request.ResultPath, ElevatedPathExistence.Optional);
    }

    private async Task CreateClaimAsync(string claimPath)
    {
        await using (var stream = new FileStream(claimPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough
        }))
        {
            await stream.FlushAsync().ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.SetLastWriteTimeUtc(claimPath, _utcNow().UtcDateTime);
    }

    private void PruneExpiredClaims(string localAppData, string rootDirectory)
    {
        try
        {
            foreach (var claimPath in Directory.EnumerateFiles(
                         rootDirectory,
                         "intervention-*.claim",
                         SearchOption.TopDirectoryOnly))
            {
                var validatedPath = _pathGuard.ValidateFilePath(
                    localAppData,
                    claimPath,
                    ElevatedPathExistence.MustExist);
                if (_utcNow() - File.GetLastWriteTimeUtc(validatedPath) > ClaimRetention)
                {
                    File.Delete(validatedPath);
                }
            }
        }
        catch
        {
            // Claim cleanup is best effort; validation and CreateNew remain authoritative.
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

public sealed record ValidatedElevatedRequest(
    ElevatedInterventionRequest Request,
    string LocalAppDataDirectory,
    string RootDirectory,
    string OriginalRequestPath,
    string ProcessingPath,
    string ClaimPath);
