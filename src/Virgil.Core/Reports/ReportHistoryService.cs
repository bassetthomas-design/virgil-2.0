using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Virgil.Domain;

namespace Virgil.Core.Reports;

public sealed class ApplicationDataReportStorageRootProvider : IReportStorageRootProvider
{
    public string GetApplicationDataDirectory()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }
}

public sealed partial class ReportHistoryService : IReportHistoryService
{
    public const int HistoryLimit = 30;
    private static readonly SemaphoreSlim StorageLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly IReportStorageRootProvider _rootProvider;
    private readonly IReportSanitizer _sanitizer;

    public ReportHistoryService()
        : this(new ApplicationDataReportStorageRootProvider(), new ReportSanitizer())
    {
    }

    public ReportHistoryService(IReportStorageRootProvider rootProvider, IReportSanitizer sanitizer)
    {
        _rootProvider = rootProvider;
        _sanitizer = sanitizer;
    }

    public async Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken)
    {
        await StorageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var root = EnsureStorageRoot();
            var sanitized = _sanitizer.Sanitize(report);
            var fileName = BuildFileName(sanitized);
            var finalPath = SafeCombine(root, fileName);
            temporaryPath = SafeCombine(root, fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, sanitized, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            temporaryPath = null;
            var rotationErrors = Rotate(root, finalPath);
            return new ReportSaveResult
            {
                Success = true,
                Report = sanitized,
                ReadableError = rotationErrors.Count == 0
                    ? null
                    : "Rapport enregistre, rotation partiellement indisponible."
            };
        }
        catch (OperationCanceledException)
        {
            return new ReportSaveResult
            {
                Success = false,
                Report = report,
                ReadableError = "Enregistrement du rapport annule. Le rapport reste disponible en memoire."
            };
        }
        catch
        {
            return new ReportSaveResult
            {
                Success = false,
                Report = report,
                ReadableError = "Historique local indisponible. Le rapport reste disponible en memoire."
            };
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }

            StorageLock.Release();
        }
    }

    public async Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        await StorageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = EnsureStorageRoot();
            return await LoadCoreAsync(root, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ReportHistoryLoadResult
            {
                Errors = new[] { "Historique local indisponible." }
            };
        }
        finally
        {
            StorageLock.Release();
        }
    }

    public async Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var history = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return history.Index.Reports.FirstOrDefault();
    }

    public async Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var history = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return history.Index.Reports.FirstOrDefault(report => report.Id == id);
    }

    private async Task<ReportHistoryLoadResult> LoadCoreAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var reports = new List<ReportEntry>();
        var errors = new List<string>();
        foreach (var path in EnumerateExpectedReportFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                EnsureSafeFile(root, path);
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    16_384,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var report = await JsonSerializer.DeserializeAsync<ReportEntry>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (report is null || report.Id == Guid.Empty)
                {
                    errors.Add($"Rapport ignore car invalide : {Path.GetFileName(path)}.");
                    continue;
                }

                reports.Add(_sanitizer.Sanitize(report));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                errors.Add($"Rapport corrompu ignore : {Path.GetFileName(path)}.");
            }
        }

        var ordered = reports
            .OrderByDescending(report => report.Date)
            .ThenByDescending(report => report.Id)
            .Take(HistoryLimit)
            .ToList();
        return new ReportHistoryLoadResult
        {
            Index = new ReportHistoryIndex
            {
                Reports = ordered,
                TotalCount = ordered.Count,
                LastReportDate = ordered.FirstOrDefault()?.Date,
                AppliedLimit = HistoryLimit
            },
            Errors = errors
        };
    }

    private static IReadOnlyList<string> Rotate(string root, string currentReportPath)
    {
        var errors = new List<string>();
        var candidates = new List<(string Path, DateTimeOffset Date)>();
        foreach (var path in EnumerateExpectedReportFiles(root))
        {
            try
            {
                EnsureSafeFile(root, path);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var report = JsonSerializer.Deserialize<ReportEntry>(stream, JsonOptions);
                if (report is not null)
                {
                    candidates.Add((path, report.Date));
                }
            }
            catch
            {
                errors.Add($"Rotation ignore un rapport illisible : {Path.GetFileName(path)}.");
            }
        }

        foreach (var candidate in candidates
            .OrderByDescending(item => string.Equals(item.Path, currentReportPath, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item => item.Date)
            .ThenByDescending(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Skip(HistoryLimit))
        {
            if (string.Equals(candidate.Path, currentReportPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                EnsureSafeFile(root, candidate.Path);
                File.Delete(candidate.Path);
            }
            catch
            {
                errors.Add($"Ancien rapport non supprime : {Path.GetFileName(candidate.Path)}.");
            }
        }

        return errors;
    }

    private string EnsureStorageRoot()
    {
        var applicationData = _rootProvider.GetApplicationDataDirectory();
        if (string.IsNullOrWhiteSpace(applicationData))
        {
            throw new InvalidOperationException("ApplicationData unavailable.");
        }

        var trustedBase = Path.GetFullPath(applicationData);
        var virgilRoot = SafeCombine(trustedBase, "Virgil");
        var reportsRoot = SafeCombine(virgilRoot, "reports");
        Directory.CreateDirectory(virgilRoot);
        RejectReparsePoint(virgilRoot);
        Directory.CreateDirectory(reportsRoot);
        RejectReparsePoint(reportsRoot);
        return reportsRoot;
    }

    private static IEnumerable<string> EnumerateExpectedReportFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly)
            .Where(path => ExpectedReportFileRegex().IsMatch(Path.GetFileName(path)));
    }

    private static string BuildFileName(ReportEntry report)
    {
        var kind = report.Kind.ToString().ToLowerInvariant();
        return $"{report.Date:yyyyMMdd-HHmmss}-{kind}-{report.Id:N}.json";
    }

    private static string SafeCombine(string root, string child)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, child));
        if (!candidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path outside report root.");
        }

        return candidate;
    }

    private static void EnsureSafeFile(string root, string path)
    {
        var fileName = Path.GetFileName(path);
        if (!ExpectedReportFileRegex().IsMatch(fileName))
        {
            throw new InvalidOperationException("Unexpected report file name.");
        }

        var expected = SafeCombine(root, fileName);
        if (!string.Equals(expected, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Report outside root.");
        }

        RejectReparsePoint(path);
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Reparse point refused.");
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path) && !new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A failed cleanup must never hide the original persistence result.
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [GeneratedRegex("^[0-9]{8}-[0-9]{6}-[a-z]+-[0-9a-f]{32}\\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex ExpectedReportFileRegex();
}
