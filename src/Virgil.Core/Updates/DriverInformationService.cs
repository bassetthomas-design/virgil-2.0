using Virgil.Domain;

namespace Virgil.Core.Updates;

public sealed class DriverInformationService
{
    private readonly IProcessRunner _processRunner;

    public DriverInformationService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<DriverInventoryReport> InspectAsync(CancellationToken cancellationToken)
    {
        var result = await _processRunner
            .RunAsync(new ProcessRunRequest("pnputil.exe", new[] { "/enum-drivers" }, TimeSpan.FromSeconds(20)), cancellationToken)
            .ConfigureAwait(false);

        if (result.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var errors = new List<string>();
        if (result.TimedOut)
        {
            errors.Add("Inventaire pilotes interrompu par timeout.");
        }
        else if (!string.IsNullOrWhiteSpace(result.LaunchError))
        {
            errors.Add("Inventaire pilotes indisponible.");
        }
        else if (result.ExitCode != 0)
        {
            errors.Add("Inventaire pilotes retourne un statut non nul.");
        }

        var drivers = errors.Count == 0
            ? ParsePnPUtil(result.StandardOutput)
            : Array.Empty<DriverInformation>();

        return new DriverInventoryReport
        {
            WasAnalyzed = true,
            CanInstallDrivers = false,
            Drivers = drivers,
            Recommendations = new[]
            {
                "Consulter Windows Update pour les pilotes proposes par Microsoft.",
                "Pour GPU et chipset, verifier l'outil officiel du fabricant avant toute installation."
            },
            Errors = errors
        };
    }

    public static IReadOnlyList<DriverInformation> ParsePnPUtil(string output)
    {
        var drivers = new List<DriverInformation>();
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                AddCurrent();
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            current[line[..separatorIndex].Trim()] = line[(separatorIndex + 1)..].Trim();
        }

        AddCurrent();
        return drivers;

        void AddCurrent()
        {
            if (current.Count == 0)
            {
                return;
            }

            drivers.Add(new DriverInformation
            {
                PublishedName = ValueFor(current, "Published Name", "Nom publie"),
                Provider = ValueFor(current, "Driver package provider", "Fournisseur"),
                ClassName = ValueFor(current, "Class", "Classe"),
                Version = ValueFor(current, "Driver version", "Version du pilote", "Date et version"),
                Date = ValueFor(current, "Driver date", "Date du pilote"),
                Signer = ValueFor(current, "Signer Name", "Signataire")
            });
            current.Clear();
        }
    }

    private static string ValueFor(IReadOnlyDictionary<string, string> values, params string[] keyFragments)
    {
        foreach (var fragment in keyFragments)
        {
            var match = values.FirstOrDefault(pair => pair.Key.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value;
            }
        }

        return string.Empty;
    }
}
