using Virgil.Contracts;
using Virgil.Core.Processes;
using Virgil.Core.Updates;
using System.Text.RegularExpressions;

namespace Virgil.Infrastructure.Windows;

// Même lecture que la voie logicielle, avec contrôle de toutes les lignes avant --all.
public sealed class DriverFinalPassWindowsReader(IProcessRunner runner, WingetAvailabilityService availability)
{
    public async Task<DriverFinalPassInventory> ReadAsync(CancellationToken token)
    {
        var source = await availability.DetectAsync(token).ConfigureAwait(false);
        if (!source.IsAvailable || source.ExecutablePath is null) return new(false, [], "Liste des logiciels indisponible.");
        var capabilities = WingetAvailabilityService.GetCapabilities(source.Version);
        var args = WingetUpdateScanService.BuildScanArguments(capabilities);
        var result = await runner.RunAsync(new(source.ExecutablePath, args, TimeSpan.FromSeconds(120)), token).ConfigureAwait(false);
        if (result.Cancelled) throw new OperationCanceledException(token);
        if (result.ExitCode != 0 || result.TimedOut || result.LaunchError is not null)
            return new(false, [], "Lecture des logiciels incomplète. Aucune installation groupée lancée.");
        var parsed = WingetUpgradeParser.Parse(result.StandardOutput).Items;
        bool Valid(UpdateItem item) => !item.IsIdTruncated && WindowsUpdateSequenceBackend.IsSafePackageId(item.Id)
            && WindowsUpdateSequenceBackend.IsSafeVersion(item.AvailableVersion);
        var items = parsed.Where(item => Valid(item) || DriverOfficialPackageCatalog.IsFirmware(item)).ToArray();
        var complete = capabilities.SupportsIncludeUnknown && string.IsNullOrWhiteSpace(result.StandardError) && parsed.Count > 0 && parsed.All(Valid)
            && parsed.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == parsed.Count
            && !result.StandardOutput.Contains('…') && EveryRowWasRead(result.StandardOutput, parsed.Count);
        return new(complete, items, complete ? "Liste des logiciels relue et complète."
            : "Liste globale non prouvée : seuls les logiciels identifiés sont traités un par un.");
    }

    private static bool EveryRowWasRead(string output, int expected)
    {
        var lines = output.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        var rows = 0; var lastColumn = 0; var footerCount = 0; var hasFooter = false; var seenTable = false; var inTable = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0 && lines[i + 1].Trim().All(c => c == '-'))
            {
                var columns = Regex.Matches(line, @"\S+");
                if (columns.Count != 5) return false;
                lastColumn = columns[4].Index; seenTable = inTable = true; i++; continue;
            }
            if (string.IsNullOrWhiteSpace(line)) { inTable = false; continue; }
            if (!seenTable) continue;
            if (inTable && line.Length > lastColumn) { rows++; continue; }
            inTable = false;
            var footer = Regex.Match(line.Trim(), @"^(\d+)\s+");
            if (!footer.Success || !int.TryParse(footer.Groups[1].Value, out var count)) return false;
            hasFooter = true; footerCount += count;
        }
        return seenTable && rows == expected && (!hasFooter || footerCount == expected);
    }
}
