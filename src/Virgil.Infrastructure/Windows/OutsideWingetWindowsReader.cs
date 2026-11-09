using System.Text.Json;
using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class OutsideWingetWindowsReader(IProcessRunner runner) : IOutsideWingetReader
{
    public async Task<OutsideWingetSnapshot> ReadAsync(CancellationToken token)
    {
        var result = await runner.RunAsync(new("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", Script], TimeSpan.FromSeconds(45)), token).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.TimedOut || result.Cancelled || result.LaunchError is not null)
            return new([], [], false);
        try { return JsonSerializer.Deserialize<OutsideWingetSnapshot>(result.StandardOutput, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new([], [], false); }
        catch (JsonException) { return new([], [], false); }
    }

    // Clés ouvertes avec writable=false, quatre combinaisons ruche/vue ; aucun désinstalleur n'est lu ou lancé.
    private const string Script = """
        $ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8
        $installed=@(); $managed=@(); $complete=$true
        foreach($hive in @([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryHive]::CurrentUser)) {
          foreach($view in @([Microsoft.Win32.RegistryView]::Registry32,[Microsoft.Win32.RegistryView]::Registry64)) {
            $root=$null; $key=$null
            try {
              $root=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
              $key=$root.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',$false)
              if($null -eq $key){continue}
              foreach($name in $key.GetSubKeyNames()) {
                $entry=$null
                try {
                  $entry=$key.OpenSubKey($name,$false)
                  $display=[string]$entry.GetValue('DisplayName','')
                  if([string]::IsNullOrWhiteSpace($display)){continue}
                  $system=($entry.GetValue('SystemComponent',0) -eq 1) -or ($null -ne $entry.GetValue('ParentKeyName')) -or ($null -ne $entry.GetValue('ReleaseType'))
                  $installed+=@{Name=$display;Version=[string]$entry.GetValue('DisplayVersion','');SystemComponent=$system}
                } catch {$complete=$false} finally {if($null -ne $entry){$entry.Dispose()}}
              }
            } catch {$complete=$false} finally {if($null -ne $key){$key.Dispose()};if($null -ne $root){$root.Dispose()}}
          }
        }
        try {
          $packages=@(Get-AppxPackage -ErrorAction Stop)
          foreach($package in $packages) {
            $managed+=[string]$package.Name
            try {
              $manifest=Get-AppxPackageManifest -Package $package.PackageFullName -ErrorAction Stop
              $display=[string]$manifest.Package.Properties.DisplayName
              if($display -and -not $display.StartsWith('ms-resource:')){$managed+=$display}
            } catch {$complete=$false}
          }
          if(Get-Module -ListAvailable -Name Microsoft.WinGet.Client) {
            Import-Module Microsoft.WinGet.Client -ErrorAction Stop
            $managed+=@(Get-WinGetPackage -ErrorAction Stop | ForEach-Object {$_.Name})
          } else {
            $command=Get-Command winget.exe -ErrorAction Stop
            $lines=@(& $command.Source list --accept-source-agreements --disable-interactivity 2>$null)
            if($LASTEXITCODE -ne 0){throw 'Inventaire indisponible'}
            $separator=-1
            for($i=0;$i -lt $lines.Count;$i++){if([string]$lines[$i] -match '^\s*-{5,}\s*$'){$separator=$i;break}}
            if($separator -lt 1){throw 'Inventaire incomplet'}
            $gap=[regex]::Match([string]$lines[$separator-1],'\S\s{2,}')
            if(-not $gap.Success){throw 'Colonnes indisponibles'}
            $width=$gap.Index+$gap.Length
            foreach($line in $lines[($separator+1)..($lines.Count-1)]) {
              if([string]$line -and $line.Length -ge $width) {
                $name=$line.Substring(0,$width).Trim()
                if($name.Contains('…') -or $name.EndsWith('...')){$complete=$false} else {$managed+=$name}
              }
            }
          }
        } catch {$complete=$false}
        @{Installed=@($installed);ManagedNames=@($managed);IsComplete=$complete} | ConvertTo-Json -Depth 5 -Compress
        """;
}
