using System.Diagnostics;
using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public sealed class ApplicationInventoryService : IApplicationInventoryService
{
    private readonly IReadOnlyList<IApplicationInventorySourceReader> _readers;
    private readonly ApplicationRiskClassifier _riskClassifier;
    private readonly ApplicationIconExtractor _iconExtractor;

    public ApplicationInventoryService()
        : this(
            [
                new ApplicationRegistryReader(),
                new ApplicationWingetReader(),
                new ApplicationStoreReader()
            ],
            new ApplicationRiskClassifier(),
            new ApplicationIconExtractor())
    {
    }

    public ApplicationInventoryService(
        IReadOnlyList<IApplicationInventorySourceReader> readers,
        ApplicationRiskClassifier riskClassifier,
        ApplicationIconExtractor iconExtractor)
    {
        _readers = readers;
        _riskClassifier = riskClassifier;
        _iconExtractor = iconExtractor;
    }

    public async Task<ApplicationInventoryReport> InventoryAsync(
        IProgress<ApplicationInventoryProgress>? progress,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var all = new List<InstalledApplication>();
        var errors = new List<string>();

        Report(progress, "Initialisation", "Inventaire", 0, 0, "Initialisation de l'inventaire.");

        for (var index = 0; index < _readers.Count; index++)
        {
            var reader = _readers[index];
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, "Analyse des applications", reader.SourceName, all.Count, Percent(index, _readers.Count), $"Source : {reader.SourceName}");

            try
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                all.AddRange(result.Applications);
                errors.AddRange(result.Errors);
                Report(progress, "Analyse des applications", reader.SourceName, all.Count, Percent(index + 1, _readers.Count), $"Applications trouvees : {all.Count}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                errors.Add($"Source {reader.SourceName} indisponible.");
            }
        }

        var merged = Merge(all)
            .Select(app => app with
            {
                ExtractedIconPath = _iconExtractor.ResolveIconPath(app.IconPath, app.InstallLocation)
            })
            .Select(_riskClassifier.Classify)
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        stopwatch.Stop();
        Report(progress, "Termine", "Synthese", merged.Count, 100, "Inventaire termine.");

        return new ApplicationInventoryReport
        {
            CapturedAt = started,
            Duration = stopwatch.Elapsed,
            Applications = merged,
            Errors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public static IReadOnlyList<InstalledApplication> Merge(IEnumerable<InstalledApplication> applications)
    {
        var merged = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in applications.Where(app => !string.IsNullOrWhiteSpace(app.DisplayName)))
        {
            var key = MergeKey(app);
            if (!merged.TryGetValue(key, out var existing))
            {
                merged[key] = NormalizeSources(app);
                continue;
            }

            merged[key] = Combine(existing, app);
        }

        return merged.Values.ToList();
    }

    private static InstalledApplication Combine(InstalledApplication existing, InstalledApplication next)
    {
        var sources = existing.Sources
            .Concat(next.Sources.Count == 0 ? new[] { next.Source } : next.Sources)
            .Distinct()
            .ToList();

        return existing with
        {
            Publisher = FirstText(existing.Publisher, next.Publisher),
            Version = FirstText(existing.Version, next.Version),
            InstallDate = existing.InstallDate ?? next.InstallDate,
            EstimatedSizeBytes = existing.EstimatedSizeBytes ?? next.EstimatedSizeBytes,
            InstallLocation = First(existing.InstallLocation, next.InstallLocation),
            IconPath = First(existing.IconPath, next.IconPath),
            UninstallCommand = First(existing.UninstallCommand, next.UninstallCommand),
            QuietUninstallCommand = First(existing.QuietUninstallCommand, next.QuietUninstallCommand),
            MsiProductCode = First(existing.MsiProductCode, next.MsiProductCode),
            WingetId = First(existing.WingetId, next.WingetId),
            StorePackageFullName = First(existing.StorePackageFullName, next.StorePackageFullName),
            Source = existing.Source == ApplicationInventorySource.Unknown ? next.Source : existing.Source,
            Sources = sources,
            Architecture = existing.Architecture == ApplicationArchitecture.Unknown ? next.Architecture : existing.Architecture,
            UninstallKind = existing.UninstallKind == ApplicationUninstallKind.None ? next.UninstallKind : existing.UninstallKind,
            Status = existing.Status == ApplicationStatus.Unknown ? next.Status : existing.Status,
            Warnings = existing.Warnings.Concat(next.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static InstalledApplication NormalizeSources(InstalledApplication application)
    {
        return application.Sources.Count == 0
            ? application with { Sources = [application.Source] }
            : application;
    }

    private static string MergeKey(InstalledApplication application)
    {
        var name = Normalize(application.DisplayName);
        var publisher = Normalize(application.Publisher);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return $"{name}|{publisher}";
        }

        if (!string.IsNullOrWhiteSpace(application.WingetId))
        {
            return "winget:" + application.WingetId.Trim().ToLowerInvariant();
        }

        return application.Id;
    }

    private static string Normalize(string value)
    {
        return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    private static string? First(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left) ? left : string.IsNullOrWhiteSpace(right) ? null : right;
    }

    private static string FirstText(string? left, string? right)
    {
        return First(left, right) ?? string.Empty;
    }

    private static int Percent(int index, int total)
    {
        return total == 0 ? 100 : Math.Clamp(index * 100 / total, 0, 100);
    }

    private static void Report(
        IProgress<ApplicationInventoryProgress>? progress,
        string step,
        string source,
        int count,
        int percent,
        string status)
    {
        progress?.Report(new ApplicationInventoryProgress
        {
            Step = step,
            Source = source,
            ApplicationsFound = count,
            Percent = percent,
            Status = status
        });
    }
}
