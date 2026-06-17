using System.Text.RegularExpressions;
using Virgil.Domain;

namespace Virgil.Core.Updates;

public sealed class WingetAvailabilityService
{
    private readonly IProcessRunner _processRunner;
    private readonly Func<IReadOnlyList<string>> _candidateProvider;
    private readonly Func<string, bool> _fileExists;

    public WingetAvailabilityService(IProcessRunner processRunner)
        : this(processRunner, BuildDefaultCandidates, File.Exists)
    {
    }

    public WingetAvailabilityService(IProcessRunner processRunner, IEnumerable<string> candidatePaths)
        : this(processRunner, () => candidatePaths.ToList(), _ => true)
    {
    }

    private WingetAvailabilityService(
        IProcessRunner processRunner,
        Func<IReadOnlyList<string>> candidateProvider,
        Func<string, bool> fileExists)
    {
        _processRunner = processRunner;
        _candidateProvider = candidateProvider;
        _fileExists = fileExists;
    }

    public async Task<WingetAvailability> DetectAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        foreach (var candidate in _candidateProvider().Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CanAttempt(candidate))
            {
                continue;
            }

            var result = await _processRunner
                .RunAsync(new ProcessRunRequest(candidate, new[] { "--version" }, TimeSpan.FromSeconds(5)), cancellationToken)
                .ConfigureAwait(false);

            if (result.Cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (result.ExitCode == 0)
            {
                var version = ExtractVersion(result.StandardOutput, result.StandardError);
                return new WingetAvailability
                {
                    IsAvailable = true,
                    ExecutablePath = candidate,
                    Version = version,
                    Message = string.IsNullOrWhiteSpace(version)
                        ? "WinGet detecte."
                        : $"WinGet detecte ({version})."
                };
            }

            if (!string.IsNullOrWhiteSpace(result.LaunchError))
            {
                errors.Add($"WinGet non executable : {result.LaunchError}");
            }
            else if (result.TimedOut)
            {
                errors.Add("Detection WinGet interrompue par timeout.");
            }
        }

        return WingetAvailability.Unavailable("WinGet non detecte.", errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static WingetCapabilities GetCapabilities(string? version)
    {
        var parsed = ParseVersion(version);
        if (parsed is null)
        {
            return WingetCapabilities.Conservative;
        }

        var supportsModernOptions = parsed.Major > 1 || parsed.Major == 1 && parsed.Minor >= 3;
        return new WingetCapabilities
        {
            SupportsAcceptSourceAgreements = parsed.Major >= 1,
            SupportsAcceptPackageAgreements = supportsModernOptions,
            SupportsDisableInteractivity = supportsModernOptions
        };
    }

    private bool CanAttempt(string candidate)
    {
        return string.Equals(Path.GetFileName(candidate), candidate, StringComparison.OrdinalIgnoreCase) ||
            _fileExists(candidate);
    }

    private static IReadOnlyList<string> BuildDefaultCandidates()
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddKnownPath(candidates, seen, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        AddFromPath(candidates, seen);

        return candidates;
    }

    private static void AddKnownPath(
        ICollection<string> candidates,
        ISet<string> seen,
        params string[] segments)
    {
        var path = Path.Combine(segments);
        if (File.Exists(path) && seen.Add(path))
        {
            candidates.Add(path);
        }
    }

    private static void AddFromPath(ICollection<string> candidates, ISet<string> seen)
    {
        var pathEnvironment = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnvironment))
        {
            return;
        }

        foreach (var directory in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var candidate = Path.Combine(trimmed, "winget.exe");
            if (File.Exists(candidate) && seen.Add(candidate))
            {
                candidates.Add(candidate);
            }
        }
    }

    private static string? ExtractVersion(params string[] values)
    {
        var text = string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Regex.Match(text, @"v?\d+(?:\.\d+){1,3}", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.TrimStart('v', 'V') : text.Split('\n')[0].Trim();
    }

    private static Version? ParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var match = Regex.Match(version, @"\d+(?:\.\d+){1,3}");
        if (!match.Success)
        {
            return null;
        }

        return Version.TryParse(match.Value, out var parsed) ? parsed : null;
    }
}
