using System.Text.Json;
using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

// Pont WinRT du système via Windows PowerShell 5.1 : aucun changement de framework ni dépendance téléchargée.
public sealed class StoreWindowsClient(IProcessRunner runner) : IStoreUpdateClient
{
    public async Task<StoreSearch> SearchAsync(CancellationToken token)
    {
        var result = await RunAsync(Prelude + SearchScript, TimeSpan.FromSeconds(40), token).ConfigureAwait(false);
        if (!Succeeded(result)) return new(false, UnavailableReason, []);
        try { return JsonSerializer.Deserialize<StoreSearch>(result.StandardOutput, JsonOptions) ?? new(false, UnavailableReason, []); }
        catch (JsonException) { return new(false, UnavailableReason, []); }
    }
    public async Task<IReadOnlyDictionary<string, string>> ReadVersionsAsync(CancellationToken token)
    {
        var result = await RunAsync("$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $versions=@{}; Get-AppxPackage -ErrorAction Stop | ForEach-Object {$versions[$_.PackageFamilyName]=$_.Version.ToString()}; ConvertTo-Json -InputObject $versions -Compress", TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
        if (!Succeeded(result)) throw new InvalidOperationException("Lecture de version indisponible.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(result.StandardOutput) ?? throw new InvalidOperationException("Version absente.");
    }
    public async Task<StoreStart> StartAsync(StorePackage package, CancellationToken token)
    {
        // Seule la famille provenant du scan confirmé entre dans le script ; aucune interpolation libre.
        if (string.IsNullOrWhiteSpace(package.FamilyName) || package.FamilyName.Length > 256
            || !package.FamilyName.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            return new(false, "Installation bloquée : identité du paquet non vérifiée.");
        token.ThrowIfCancellationRequested();
        var script = Prelude + "\n$family='" + package.FamilyName + "'\n" + InstallScript;
        // Ne jamais couper l'installation en cours : le service observe l'annulation entre applications.
        using var installationTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var result = await RunAsync(script, TimeSpan.FromMinutes(20), installationTimeout.Token).ConfigureAwait(false);
        if (!Succeeded(result)) return new(false, "L'installation n'a pas fourni de résultat vérifiable.");
        try { return JsonSerializer.Deserialize<StoreStart>(result.StandardOutput, JsonOptions) ?? new(false, "Résultat de l'installation absent."); }
        catch (JsonException) { return new(false, "Résultat de l'installation illisible."); }
    }
    private Task<ProcessRunResult> RunAsync(string script, TimeSpan timeout, CancellationToken token) =>
        runner.RunAsync(new("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], timeout), token);
    private static bool Succeeded(ProcessRunResult result) => result.ExitCode == 0 && !result.TimedOut && !result.Cancelled && result.LaunchError is null;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string UnavailableReason = "Les droits ou le contexte de ce compte ne permettent pas la mise à jour automatique depuis le Microsoft Store.";
    private const string Prelude = """
        $ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $itemType=[Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallItem,Windows.ApplicationModel.Store.Preview.InstallControl,ContentType=WindowsRuntime]
        $managerType=[Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallManager,Windows.ApplicationModel.Store.Preview.InstallControl,ContentType=WindowsRuntime]
        function Await-Store($opération,[Type]$resultType) {
          $method=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1} | Select-Object -First 1
          $task=$method.MakeGenericMethod($resultType).Invoke($null,@($opération))
          $awaiter=$task.GetAwaiter()
          $awaiter.GetResult()
        }
        """;
    private const string SearchScript = """

        try {
          $manager=[Activator]::CreateInstance($managerType)
          $options=[Windows.ApplicationModel.Store.Preview.InstallControl.AppUpdateOptions,Windows.ApplicationModel.Store.Preview.InstallControl,ContentType=WindowsRuntime]::new()
          $options.AutomaticallyDownloadAndInstallUpdateIfFound=$false
          $options.AllowForcedAppRestart=$false
          $listType=[System.Collections.Generic.IReadOnlyList[Windows.ApplicationModel.Store.Preview.InstallControl.AppInstallItem]]
          $items=Await-Store ($manager.SearchForAllUpdatesAsync('','',$options)) $listType
          $installed=@(Get-AppxPackage -ErrorAction Stop)
          $packages=@(foreach($item in $items) {
            $package=$installed | Where-Object {$_.PackageFamilyName -eq $item.PackageFamilyName} | Select-Object -First 1
            if($null -eq $package){throw 'Version installée introuvable'}
            $name='Application du Microsoft Store'
            $manifest=Get-AppxPackageManifest -Package $package.PackageFullName -ErrorAction Stop
            $display=[string]$manifest.Package.Properties.DisplayName
            if($display -and -not $display.StartsWith('ms-resource:')){$name=$display}
            @{ProductId=[string]$item.ProductId;FamilyName=[string]$item.PackageFamilyName;Name=$name;Version=$package.Version.ToString()}
          })
          @{Supported=$true;Reason='';Packages=@($packages)} | ConvertTo-Json -Depth 5 -Compress
        } catch {
          @{Supported=$false;Reason='Les droits ou le contexte de ce compte ne permettent pas la recherche automatique dans le Microsoft Store.';Packages=@()} | ConvertTo-Json -Compress
        }
        """;
    private const string InstallScript = """
        try {
          $manager=[Activator]::CreateInstance($managerType)
          $item=Await-Store ($manager.UpdateAppByPackageFamilyNameAsync($family)) $itemType
          if($null -eq $item){throw 'Installation absente'}
          $item.LaunchAfterInstall=$false
          $watch=[Diagnostics.Stopwatch]::StartNew()
          do {
            $status=$item.GetCurrentStatus()
            if([int]$status.InstallState -eq 6){break}
            if([int]$status.InstallState -in @(7,9)) {
              @{Accepted=$false;Reason="L'installation a échoué ou a été annulée par le système.";WasUnsupported=$false} | ConvertTo-Json -Compress
              exit 0
            }
            if($watch.Elapsed.TotalMinutes -gt 18) {
              @{Accepted=$false;Reason="L'installation reste en attente ; son achèvement n'est pas prouvé.";WasUnsupported=$false} | ConvertTo-Json -Compress
              exit 0
            }
            Start-Sleep -Milliseconds 500
          } while($true)
          @{Accepted=$true;Reason='';WasUnsupported=$false} | ConvertTo-Json -Compress
        } catch {
          @{Accepted=$false;Reason="Les droits ou le contexte de ce compte ne permettent pas cette mise à jour automatique.";WasUnsupported=$true} | ConvertTo-Json -Compress
        }
        """;
}
