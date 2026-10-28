using System.Text.RegularExpressions;

namespace Virgil.Core.Startup;

/// <summary>Fusion pure, sans lecture du registre ni modification de commande.</summary>
public static class StartupInventoryMerger
{
    public static IReadOnlyList<StartupItem> Merge(IEnumerable<StartupItem> items) => items
        .Select(item => item.ApprovalTarget is { Hive: StartupApprovalHive.CurrentUser, Kind: StartupApprovalKind.Run32 } target
            ? item with { ApprovalTarget = target with { Kind = StartupApprovalKind.Run } } : item)
        .GroupBy(item => string.Join("\0", item.ApprovalTarget?.Hive.ToString() ?? item.Source,
            SourceType(item), item.ApprovalTarget?.ValueName ?? item.Name, Normalize(item.Path)), StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    private static string SourceType(StartupItem item) => item.ApprovalTarget?.Kind switch
    {
        StartupApprovalKind.Run or StartupApprovalKind.Run32 => "RegistryRun",
        StartupApprovalKind.StartupFolder => "StartupFolder",
        _ => item.Source
    };
    // Les espaces à l’intérieur des arguments entre guillemets restent significatifs.
    private static string Normalize(string command) => Regex.Replace(command.Trim(),
        "\"[^\"]*\"|\\s+", match => match.Value.StartsWith('"') ? match.Value : " ");
}
