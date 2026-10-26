namespace Virgil.ElevatedHelper;

// Compiled scripts only. No request data is interpolated into PowerShell.
internal static class RepairScripts
{
    internal const string Services = """
        $ErrorActionPreference='Stop'
        function Check($key,$ok,$detail) { if($ok){"VIRGIL|$key|OK|$detail"}else{"VIRGIL|$key|FINDING|$detail"} }
        foreach($name in @('wuauserv','AppXSvc','ClipSVC','Audiosrv','AudioEndpointBuilder','Spooler')) {
          try { $s=Get-Service -Name $name
            if($name -in @('wuauserv','AppXSvc','ClipSVC')) {
              if($s.StartType -eq 'Disabled'){ Check $name $false "$name désactivé : vérifiez la configuration dans Windows" }
              else { "VIRGIL|$name|INFO|$($s.Status) (démarrage à la demande possible)" }
            }
            else { Check $name ($s.Status -eq 'Running') "$name : $($s.Status)" }
          } catch { "VIRGIL|$name|UNKNOWN|État du service indisponible" }
        }
        $pending=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
          (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
        Check 'Reboot' (!$pending) 'Redémarrage Windows en attente'
        try {
          $net=Get-NetIPConfiguration | Where-Object {$_.IPv4DefaultGateway} | Select-Object -First 1
          if(!$net){ Check 'Gateway' $false 'Aucune passerelle active' }
          else { Check 'Gateway' (Test-Connection -ComputerName $net.IPv4DefaultGateway.NextHop -Count 1 -Quiet) 'Réponse de la passerelle' }
          $dns=Resolve-DnsName 'www.microsoft.com' -DnsOnly -ErrorAction Stop
          Check 'DNS' ($null -ne $dns) 'Résolution DNS de www.microsoft.com'
        } catch { Check 'DNS' $false 'Passerelle ou DNS injoignable ; un filtrage réseau est possible' }
        try {
          $queues=Get-Printer -ErrorAction Stop
          $stuck=@(foreach($p in $queues){Get-PrintJob -PrinterName $p.Name -ErrorAction Stop | Where-Object { $_.JobStatus -match 'Error|Blocked|Offline|PaperOut|UserIntervention' -or $_.SubmittedTime -lt (Get-Date).AddHours(-1) }})
          Check 'PrintQueue' ($stuck.Count -eq 0) "$($stuck.Count) tâche(s) d’impression bloquée(s)"
        } catch { 'VIRGIL|PrintQueue|UNKNOWN|File d’impression indisponible' }
        try {
          $endpoints=@(Get-PnpDevice -Class AudioEndpoint -PresentOnly -ErrorAction Stop | Where-Object Status -eq 'OK')
          Check 'AudioEndpoint' ($endpoints.Count -gt 0) "$($endpoints.Count) point(s) de sortie audio disponible(s) ; sortie par défaut à vérifier dans Windows"
        } catch { 'VIRGIL|AudioEndpoint|UNKNOWN|Points de sortie audio indisponibles' }
        """;

    internal const string PrintQueue = """
        $ErrorActionPreference='Stop'
        Start-Service -Name Spooler -ErrorAction Stop
        foreach($p in Get-Printer -ErrorAction Stop) {
          foreach($job in Get-PrintJob -PrinterName $p.Name -ErrorAction Stop) {
            if($job.JobStatus -match 'Error|Blocked|Offline|PaperOut|UserIntervention' -or $job.SubmittedTime -lt (Get-Date).AddHours(-1)) {
              Remove-PrintJob -PrinterName $p.Name -ID $job.ID -ErrorAction Stop
            }
          }
        }
        Restart-Service -Name Spooler -ErrorAction Stop
        """;

    internal const string RestorePoint = """
        $ErrorActionPreference='Stop'
        try {
          $before=@(Get-ComputerRestorePoint -ErrorAction Stop | Select-Object -ExpandProperty SequenceNumber)
          $description='Virgil réparation '+[Guid]::NewGuid().ToString('N')
          Checkpoint-Computer -Description $description -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
          $verified=@(Get-ComputerRestorePoint -ErrorAction Stop | Where-Object { $_.Description -eq $description -and $_.SequenceNumber -notin $before })
          if($verified.Count -ne 1){throw 'Point de restauration non vérifié (fréquence limitée ou restauration désactivée).'}
          'Point de restauration créé et vérifié.'
        } catch { [Console]::Error.WriteLine('Restauration système désactivée, limitée ou indisponible. Activez la protection du système dans Windows. Aucun changement effectué. '+$_.Exception.Message); exit 2 }
        """;
}
