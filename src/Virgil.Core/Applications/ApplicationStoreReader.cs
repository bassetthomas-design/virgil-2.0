using Virgil.Core.Updates;
using Virgil.Domain.Applications;

namespace Virgil.Core.Applications;

public sealed class ApplicationStoreReader : IApplicationInventorySourceReader
{
    private readonly IProcessRunner _processRunner;

    public ApplicationStoreReader()
        : this(new ProcessRunner())
    {
    }

    public ApplicationStoreReader(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public string SourceName => "Store";

    public async Task<ApplicationInventorySourceResult> ReadAsync(CancellationToken cancellationToken)
    {
        var script = "Get-AppxPackage | Select-Object Name,PackageFullName,Publisher,Version,IsFramework | ForEach-Object { \"$($_.Name)|$($_.PackageFullName)|$($_.Publisher)|$($_.Version)|$($_.IsFramework)\" }";
        var result = await _processRunner
            .RunAsync(new ProcessRunRequest(
                "powershell.exe",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
                TimeSpan.FromSeconds(20)),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.LaunchError is not null)
        {
            return new ApplicationInventorySourceResult(Array.Empty<InstalledApplication>(), ["Source Store indisponible."]);
        }

        if (result.Cancelled)
        {
            throw new OperationCanceledException();
        }

        if (result.TimedOut)
        {
            return new ApplicationInventorySourceResult(Array.Empty<InstalledApplication>(), ["Source Store interrompue : delai depasse."]);
        }

        return Parse(result.StandardOutput);
    }

    public static ApplicationInventorySourceResult Parse(string output)
    {
        var apps = new List<InstalledApplication>();
        var errors = new List<string>();

        foreach (var raw in output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length < 5)
            {
                errors.Add($"Ligne Store ignoree : {line}");
                continue;
            }

            var isFramework = bool.TryParse(parts[4], out var framework) && framework;
            apps.Add(new InstalledApplication
            {
                Id = ApplicationRegistryReader.StableId("store", parts[1]),
                DisplayName = parts[0],
                Publisher = parts[2],
                Version = parts[3],
                Source = ApplicationInventorySource.Store,
                Sources = [ApplicationInventorySource.Store],
                StorePackageFullName = parts[1],
                UninstallKind = ApplicationUninstallKind.StoreSettings,
                Status = ApplicationStatus.ReadOnly,
                RiskLevel = isFramework ? ApplicationRiskLevel.Protected : ApplicationRiskLevel.Unknown,
                RiskReason = isFramework
                    ? "Package Store framework protege en V1."
                    : "Application Store inventoriee en lecture seule en V1.",
                CanUninstall = false
            });
        }

        return new ApplicationInventorySourceResult(apps, errors);
    }
}

