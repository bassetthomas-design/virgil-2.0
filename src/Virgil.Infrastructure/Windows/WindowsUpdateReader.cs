using System.Text.Json;
using Virgil.Core.Processes;
using Virgil.Core.Updates;

namespace Virgil.Infrastructure.Windows;

public sealed class WindowsUpdateReader(IProcessRunner runner) : IWindowsUpdateReader
{
    private const string UnavailableReason = "Lecture Windows Update indisponible ; ouvrez Windows Update pour vérifier.";
    private const string SearchScript = """
        $ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.Encoding]::UTF8
        $s=New-Object -ComObject Microsoft.Update.Session
        $q=$s.CreateUpdateSearcher()
        $m=New-Object -ComObject Microsoft.Update.ServiceManager
        if (@($m.Services | Where-Object { $_.ServiceID -eq '7971f918-a847-4430-9279-4a52d1efe18d' }).Count -gt 0) {
          $q.ServerSelection=3
          $q.ServiceID='7971f918-a847-4430-9279-4a52d1efe18d'
        }
        $r = $q.Search("IsInstalled=0 and IsHidden=0")
        if ($r.ResultCode -ne 2) { throw 'VIRGIL_WUA_001' }
        $titles=@(for ($i=0; $i -lt [Math]::Min(5,$r.Updates.Count); $i++) { [string]$r.Updates.Item($i).Title })
        @{Count=$r.Updates.Count;Titles=$titles} | ConvertTo-Json -Compress
        """;

    public async Task<WindowsUpdateProbeResult> ReadAsync(CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(new("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", SearchScript],
            TimeSpan.FromSeconds(40)), cancellationToken).ConfigureAwait(false);
        if (result.Cancelled) cancellationToken.ThrowIfCancellationRequested();
        if (result.TimedOut)
            return Unavailable("Lecture Windows Update interrompue (délai dépassé) ; ouvrez Windows Update pour vérifier.");
        if (result.StandardOutput.Contains("VIRGIL_WUA_001", StringComparison.Ordinal) ||
            result.StandardError.Contains("VIRGIL_WUA_001", StringComparison.Ordinal))
            return Unavailable("Recherche Windows Update incomplète ; ouvrez Windows Update pour vérifier.");
        if (result.ExitCode != 0 || result.Cancelled || result.LaunchError is not null)
            return Unavailable(UnavailableReason);

        SearchResult? search;
        try { search = JsonSerializer.Deserialize<SearchResult>(result.StandardOutput); }
        catch (JsonException) { return Unavailable(UnavailableReason); }
        if (search?.Count is not int count || count < 0) return Unavailable(UnavailableReason);
        if (count == 0) return new("à jour", 0, "À jour selon Windows Update.");
        var status = count == 1
            ? "1 mise à jour Windows Update détectée (ouverture manuelle ou « Tout mettre à jour »)."
            : $"{count} mises à jour Windows Update détectées (ouverture manuelle ou « Tout mettre à jour »).";
        return new("mises à jour détectées", count, status,
            Notes: search.Titles?.Take(5).Select(title => "Windows Update : " + title).ToArray());
    }

    private static WindowsUpdateProbeResult Unavailable(string reason) => new("inconnu", 0, reason, reason);

    private sealed record SearchResult(int? Count, string[]? Titles);
}
