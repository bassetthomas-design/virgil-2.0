using System.Text.RegularExpressions;
using Virgil.Core.Updates;
using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public sealed class ApplicationWingetReader : IApplicationInventorySourceReader
{
    private static readonly Regex ColumnSplitter = new(@"\s{2,}|\t+", RegexOptions.Compiled);
    private readonly IProcessRunner _processRunner;

    public ApplicationWingetReader()
        : this(new ProcessRunner())
    {
    }

    public ApplicationWingetReader(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string SourceName => "WinGet";

    public async Task<ApplicationInventorySourceResult> ReadAsync(CancellationToken cancellationToken)
    {
        var result = await _processRunner
            .RunAsync(new ProcessRunRequest(
                "winget",
                ["list", "--accept-source-agreements"],
                TimeSpan.FromSeconds(20)),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.LaunchError is not null)
        {
            return new ApplicationInventorySourceResult(
                Array.Empty<InstalledApplication>(),
                ["Source WinGet indisponible."]);
        }

        if (result.Cancelled)
        {
            throw new OperationCanceledException();
        }

        if (result.TimedOut)
        {
            return new ApplicationInventorySourceResult(
                Array.Empty<InstalledApplication>(),
                ["Source WinGet interrompue : delai depasse."]);
        }

        var parse = Parse(result.StandardOutput);
        return new ApplicationInventorySourceResult(parse.Applications, parse.Errors);
    }

    public static ApplicationInventorySourceResult Parse(string output)
    {
        var apps = new List<InstalledApplication>();
        var errors = new List<string>();
        var headerSeen = false;

        foreach (var raw in output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || IsSeparator(line) || IsSummary(line))
            {
                continue;
            }

            if (IsHeader(line))
            {
                headerSeen = true;
                continue;
            }

            if (!headerSeen)
            {
                continue;
            }

            var parts = ColumnSplitter.Split(line).Where(part => !string.IsNullOrWhiteSpace(part)).ToList();
            if (parts.Count < 3)
            {
                errors.Add($"Ligne WinGet ignoree : {line}");
                continue;
            }

            var source = parts.Count >= 4 ? parts[^1] : "winget";
            var version = parts.Count >= 4 ? parts[^2] : parts[^1];
            var id = parts.Count >= 4 ? parts[^3] : parts[^2];
            var name = string.Join(" ", parts.Take(parts.Count >= 4 ? parts.Count - 3 : parts.Count - 2)).Trim();

            if (string.IsNullOrWhiteSpace(name) || !LooksLikeWingetId(id))
            {
                errors.Add($"Ligne WinGet ambigue ignoree : {line}");
                continue;
            }

            apps.Add(new InstalledApplication
            {
                Id = ApplicationRegistryReader.StableId("winget", id),
                DisplayName = name,
                Version = version,
                Source = IsStore(source) ? ApplicationInventorySource.Store : ApplicationInventorySource.Winget,
                Sources = IsStore(source)
                    ? [ApplicationInventorySource.Winget, ApplicationInventorySource.Store]
                    : [ApplicationInventorySource.Winget],
                WingetId = id,
                UninstallKind = IsStore(source) ? ApplicationUninstallKind.StoreSettings : ApplicationUninstallKind.Winget,
                Status = IsStore(source) ? ApplicationStatus.ReadOnly : ApplicationStatus.UninstallAvailable
            });
        }

        return new ApplicationInventorySourceResult(apps, errors);
    }

    private static bool IsHeader(string line)
    {
        return line.Contains("Name", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("Id", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("Version", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSeparator(string line)
    {
        return line.All(character => character is '-' or ' ' or '\t');
    }

    private static bool IsSummary(string line)
    {
        return line.Contains("packages", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("No installed package", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Aucun package", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeWingetId(string value)
    {
        return value.Contains('.') &&
            value.Any(char.IsLetter) &&
            value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or '+');
    }

    private static bool IsStore(string source)
    {
        return source.Contains("msstore", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("store", StringComparison.OrdinalIgnoreCase);
    }
}

