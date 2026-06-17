using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Virgil.Domain;

namespace Virgil.ElevatedHelper;

public sealed class ElevatedRequestValidator
{
    private static readonly Regex NoncePattern = new("^[A-F0-9]{32}$", RegexOptions.Compiled);
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<string> _localAppDataProvider;

    public ElevatedRequestValidator()
        : this(() => DateTimeOffset.UtcNow, () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    public ElevatedRequestValidator(Func<DateTimeOffset> utcNow, Func<string> localAppDataProvider)
    {
        _utcNow = utcNow;
        _localAppDataProvider = localAppDataProvider;
    }

    public string RootDirectory => Path.Combine(_localAppDataProvider(), "Virgil", "Temp");

    public async Task<ValidatedElevatedRequest> ValidateAsync(string requestPath)
    {
        var fullRequestPath = Path.GetFullPath(requestPath);
        if (!IsPathUnderRoot(fullRequestPath))
        {
            throw new InvalidOperationException("Requete hors racine Virgil autorisee.");
        }

        var json = await File.ReadAllTextAsync(fullRequestPath).ConfigureAwait(false);
        var request = JsonSerializer.Deserialize<ElevatedInterventionRequest>(json, JsonOptions())
            ?? throw new InvalidOperationException("Requete invalide.");

        ValidateRequest(request);
        return new ValidatedElevatedRequest(request, fullRequestPath);
    }

    public bool IsPathUnderRoot(string path)
    {
        var fullRoot = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
    }

    private void ValidateRequest(ElevatedInterventionRequest request)
    {
        if (request.ProtocolVersion != 1)
        {
            throw new InvalidOperationException("Version protocole refusee.");
        }

        if (!NoncePattern.IsMatch(request.Nonce))
        {
            throw new InvalidOperationException("Nonce invalide.");
        }

        if (_utcNow() - request.CreatedAt.ToUniversalTime() > TimeSpan.FromMinutes(10))
        {
            throw new InvalidOperationException("Requete expiree.");
        }

        if (!IsPathUnderRoot(request.ResultPath))
        {
            throw new InvalidOperationException("Chemin resultat hors racine Virgil autorisee.");
        }

        var resultFile = Path.GetFileName(request.ResultPath);
        if (!resultFile.StartsWith($"intervention-{request.Nonce}.", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Chemin resultat non associe au nonce.");
        }
    }
}

public sealed record ValidatedElevatedRequest(
    ElevatedInterventionRequest Request,
    string RequestPath);
