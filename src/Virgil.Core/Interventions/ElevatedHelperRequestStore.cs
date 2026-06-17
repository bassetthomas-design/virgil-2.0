using System.Security.Cryptography;
using System.Text.Json;
using Virgil.Domain;

namespace Virgil.Core.Interventions;

public sealed class ElevatedHelperRequestStore
{
    private const int ProtocolVersion = 1;
    private readonly Func<string> _localAppDataProvider;

    public ElevatedHelperRequestStore()
        : this(() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    public ElevatedHelperRequestStore(Func<string> localAppDataProvider)
    {
        _localAppDataProvider = localAppDataProvider;
    }

    public string RootDirectory => Path.Combine(_localAppDataProvider(), "Virgil", "Temp");

    public async Task<ElevatedInterventionRequestFile> CreateAsync(
        InterventionId actionId,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RootDirectory);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var requestPath = Path.Combine(RootDirectory, $"intervention-{nonce}.request.json");
        var resultPath = Path.Combine(RootDirectory, $"intervention-{nonce}.result.json");
        var request = new ElevatedInterventionRequest
        {
            ProtocolVersion = ProtocolVersion,
            ActionId = actionId,
            Nonce = nonce,
            CreatedAt = DateTimeOffset.UtcNow,
            ResultPath = resultPath
        };

        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(request, JsonOptions()),
            cancellationToken).ConfigureAwait(false);

        return new ElevatedInterventionRequestFile(requestPath, resultPath, nonce);
    }

    public async Task<ElevatedInterventionResult?> ReadResultAsync(
        ElevatedInterventionRequestFile requestFile,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(requestFile.ResultPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(requestFile.ResultPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ElevatedInterventionResult>(json, JsonOptions());
    }

    public void Cleanup(ElevatedInterventionRequestFile requestFile)
    {
        TryDelete(requestFile.RequestPath);
        TryDelete(requestFile.ResultPath);
    }

    public static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions { WriteIndented = true };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Request files contain no secret; cleanup is best effort.
        }
    }
}

public sealed record ElevatedInterventionRequestFile(
    string RequestPath,
    string ResultPath,
    string Nonce);
