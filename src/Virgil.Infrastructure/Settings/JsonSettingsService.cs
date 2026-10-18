using System.Text.Json;
using Virgil.Contracts;
using Virgil.Core.Settings;
using Virgil.Infrastructure.Storage;

namespace Virgil.Infrastructure.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly VirgilPaths _paths;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    // The composition root supplies the ILogger warning adapter without coupling storage to a logging provider.
    public JsonSettingsService(VirgilPaths paths, Action<string>? warning = null)
    {
        _paths = paths;
        Current = new VirgilSettings();
        try
        {
            Current = Normalize(JsonSerializer.Deserialize<VirgilSettings>(File.ReadAllText(paths.SettingsFile), JsonOptions)
                    ?? throw new JsonException());
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Never include the JSON or parser exception: these may contain private user data.
            warning?.Invoke("Settings illisibles ou invalides ; utilisation des valeurs par defaut.");
        }
    }

    public VirgilSettings Current { get; private set; }

    public async Task SaveAsync(VirgilSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _saveLock.WaitAsync(ct).ConfigureAwait(false);
        var temporaryFile = _paths.SettingsFile + ".tmp";
        try
        {
            var normalized = Normalize(settings);
            _paths.EnsureSettingsDirectory();
            await File.WriteAllTextAsync(temporaryFile, JsonSerializer.Serialize(normalized, JsonOptions), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(temporaryFile, _paths.SettingsFile, overwrite: true);
            Current = normalized;
        }
        finally
        {
            try { if (File.Exists(temporaryFile)) File.Delete(temporaryFile); }
            finally { _saveLock.Release(); }
        }
    }

    private static VirgilSettings Normalize(VirgilSettings settings)
    {
        string[] levels = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];
        var level = levels.FirstOrDefault(value => string.Equals(value, settings.MinimumLogLevel, StringComparison.OrdinalIgnoreCase));
        return settings with { MinimumLogLevel = level ?? "Information" };
    }
}
