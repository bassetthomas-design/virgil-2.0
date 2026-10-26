using System.Text.Json;
using Virgil.Core.Processes;
using Virgil.Contracts.Applications;

namespace Virgil.Core.Applications;

public sealed class ApplicationStoreReader(IProcessRunner processRunner) : IApplicationInventorySourceReader
{
    public string SourceName => "Store";

    // Manifest metadata and resource lookup are performed by Windows in the current user's context.
    // Package paths/names are never inserted into executable PowerShell source.
    private const string InventoryScript = """
        [Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)
        $ErrorActionPreference='Stop'
        Add-Type -TypeDefinition @'
        using System;
        using System.Text;
        using System.Runtime.InteropServices;
        public static class VirgilPackageResource {
          [DllImport("shlwapi.dll", CharSet=CharSet.Unicode)]
          static extern int SHLoadIndirectString(string source, StringBuilder output, uint length, IntPtr reserved);
          public static string Resolve(string value, string fullName, string name) {
            if (String.IsNullOrWhiteSpace(value) || !value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return value;
            string resource=value.Substring(12);
            string[] candidates={ value, "ms-resource://"+name+"/resources/"+resource.TrimStart('/'), "ms-resource://"+name+"/"+resource.TrimStart('/') };
            foreach(var candidate in candidates) {
              var result=new StringBuilder(4096);
              if(SHLoadIndirectString("@{"+fullName+"?"+candidate+"}",result,4096,IntPtr.Zero)==0 && result.Length>0) return result.ToString();
            }
            return null;
          }
        }
        '@
        $items=@(foreach($p in Get-AppxPackage -ErrorAction Stop) {
          if($p.IsResourcePackage -or $p.SignatureKind -eq 'System' -or $p.NonRemovable){continue}
          try {
            $manifest=Get-AppxPackageManifest -Package $p.PackageFullName -ErrorAction Stop
            $name=[VirgilPackageResource]::Resolve([string]$manifest.Package.Properties.DisplayName,$p.PackageFullName,$p.Name)
            $publisher=[VirgilPackageResource]::Resolve([string]$manifest.Package.Properties.PublisherDisplayName,$p.PackageFullName,$p.Name)
            $logo=[string]$manifest.Package.Properties.Logo
            $logoPath=$null
            if($logo -and !$logo.Contains('..') -and ![IO.Path]::IsPathRooted($logo)) {
              $path=Join-Path $p.InstallLocation $logo
              if(Test-Path -LiteralPath $path -PathType Leaf){$logoPath=$path}
              else {
                $parent=Split-Path $path
                $stem=[IO.Path]::GetFileNameWithoutExtension($path)
                $ext=[IO.Path]::GetExtension($path)
                $logoPath=Get-ChildItem -LiteralPath $parent -File -ErrorAction SilentlyContinue | Where-Object { $_.Name.StartsWith($stem+'.scale-') -and $_.Extension -eq $ext } | Select-Object -First 1 -ExpandProperty FullName
              }
            }
            [pscustomobject]@{ Name=$p.Name; PackageFullName=$p.PackageFullName; DisplayName=$name; PublisherDisplayName=$publisher; Version=[string]$p.Version; IsFramework=[bool]$p.IsFramework; Logo=$logoPath; InstallLocation=$p.InstallLocation }
          } catch { [pscustomobject]@{ Name=$p.Name; PackageFullName=$p.PackageFullName; Version=[string]$p.Version; IsFramework=[bool]$p.IsFramework; MetadataUnavailable=$true } }
        })
        ConvertTo-Json -InputObject $items -Depth 3 -Compress
        """;

    public async Task<ApplicationInventorySourceResult> ReadAsync(CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(new ProcessRunRequest("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", InventoryScript], TimeSpan.FromMinutes(2)), cancellationToken).ConfigureAwait(false);
        if (result.Cancelled) throw new OperationCanceledException(cancellationToken);
        if (result.LaunchError is not null || result.TimedOut || result.ExitCode != 0)
            return new([], [result.TimedOut ? "Inventaire Store : délai dépassé." : "Inventaire Store indisponible."]);
        return Parse(result.StandardOutput);
    }

    public static ApplicationInventorySourceResult Parse(string output)
    {
        var apps = new List<InstalledApplication>();
        var errors = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(output.Trim().TrimStart('\uFEFF'));
            var rows = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToArray() : [document.RootElement];
            foreach (var row in rows)
            {
                if (Flag(row, "IsResourcePackage") || Flag(row, "NonRemovable") || Text(row, "SignatureKind") == "System") continue;
                var package = Text(row, "PackageFullName");
                if (string.IsNullOrWhiteSpace(package)) continue;
                var framework = Flag(row, "IsFramework");
                var name = Text(row, "DisplayName");
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                {
                    name = Text(row, "Name");
                    errors.Add($"Nom convivial Store indisponible pour {name}.");
                }
                var publisher = Text(row, "PublisherDisplayName");
                if (publisher.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) publisher = string.Empty;
                apps.Add(new InstalledApplication
                {
                    Id = ApplicationRegistryReader.StableId("store", package), DisplayName = name,
                    Publisher = publisher, Version = Text(row, "Version"), StorePackageFullName = package,
                    Source = ApplicationInventorySource.Store, Sources = [ApplicationInventorySource.Store],
                    IconPath = Text(row, "Logo"), InstallLocation = Text(row, "InstallLocation"),
                    IsSystemComponent = framework, CanUninstall = !framework,
                    RiskLevel = framework ? ApplicationRiskLevel.Protected : ApplicationRiskLevel.Caution,
                    UninstallKind = ApplicationUninstallKind.StorePackage,
                    RiskReason = framework ? "Framework protégé." : "Retrait officiel pour le compte actuel après confirmation renforcée.",
                    Status = framework ? ApplicationStatus.Protected : ApplicationStatus.UninstallAvailable
                });
            }
        }
        catch (JsonException) { errors.Add("Métadonnées Store illisibles ; aucune désinstallation proposée."); }
        return new(apps, errors);
    }

    private static bool Flag(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
