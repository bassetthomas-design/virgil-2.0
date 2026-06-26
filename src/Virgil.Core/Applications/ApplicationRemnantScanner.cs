using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public sealed class ApplicationRemnantScanner
{
    private readonly ApplicationRemnantClassifier _classifier;

    public ApplicationRemnantScanner()
        : this(new ApplicationRemnantClassifier())
    {
    }

    public ApplicationRemnantScanner(ApplicationRemnantClassifier classifier)
    {
        _classifier = classifier;
    }

    public Task<ApplicationRemnantScanReport> ScanAsync(
        InstalledApplication application,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ApplicationRemnantCandidate>();
        var errors = new List<string>();

        foreach (var path in BuildCandidatePaths(application).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Directory.Exists(path))
                {
                    candidates.Add(_classifier.Classify(path, isDirectory: true, TryGetDirectorySize(path)));
                }
                else if (File.Exists(path))
                {
                    candidates.Add(_classifier.Classify(path, isDirectory: false, new FileInfo(path).Length));
                }
            }
            catch
            {
                errors.Add($"Reste inaccessible en lecture : {path}.");
            }
        }

        return Task.FromResult(new ApplicationRemnantScanReport
        {
            Application = application,
            Remnants = candidates,
            Errors = errors
        });
    }

    private static IEnumerable<string> BuildCandidatePaths(InstalledApplication application)
    {
        if (!string.IsNullOrWhiteSpace(application.InstallLocation))
        {
            yield return application.InstallLocation;
        }

        var names = BuildSearchNames(application).ToList();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Path.GetTempPath()
        };

        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            foreach (var name in names)
            {
                yield return Path.Combine(root, name);
            }
        }
    }

    private static IEnumerable<string> BuildSearchNames(InstalledApplication application)
    {
        foreach (var value in new[] { application.DisplayName, application.Publisher, application.WingetId })
        {
            var clean = CleanName(value);
            if (!string.IsNullOrWhiteSpace(clean))
            {
                yield return clean;
            }
        }
    }

    private static string CleanName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Where(character => !invalid.Contains(character)).ToArray()).Trim();
    }

    private static long? TryGetDirectorySize(string path)
    {
        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Take(500))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // Best effort read-only estimate.
                }
            }

            return total;
        }
        catch
        {
            return null;
        }
    }
}

