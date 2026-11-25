using System.Globalization;

namespace Virgil.ElevatedHelper;

internal enum WindowsUpdateServiceState : uint
{
    Stopped = 1,
    StartPending,
    StopPending,
    Running,
    ContinuePending,
    PausePending,
    Paused
}

internal readonly record struct WindowsUpdateServiceInfo(WindowsUpdateServiceState State, bool Disabled);

internal interface IWindowsUpdateResetHost
{
    WindowsUpdateServiceInfo InspectService(string name);
    WindowsUpdateServiceState ReadServiceState(string name);
    void RequestStop(string name);
    void RequestStart(string name);
    bool IsRebootPending();
    FileAttributes? ReadAttributes(string path);
    void MoveDirectory(string source, string destination);
}

internal sealed class WindowsUpdateResetRunner(
    IWindowsUpdateResetHost host, TimeProvider timeProvider, string systemDirectory)
{
    private static readonly string[] StopOrder = ["wuauserv", "bits", "cryptsvc", "msiserver"];
    private static readonly string[] StartOrder = ["cryptsvc", "bits", "wuauserv"];
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public async Task<ElevatedCommandResult> RunAsync()
    {
        var journal = new List<string>();
        var renamed = new List<Backup>();
        var step = "validation du répertoire système";
        Backup[] backups;
        try
        {
            backups = GetBackups();
            journal.Add($"{step} : réussie.");
            foreach (var name in StopOrder)
            {
                step = $"contrôle du service {name}";
                var service = host.InspectService(name);
                if (service.Disabled)
                    throw new InvalidOperationException($"le service {name} est désactivé");
                if (name == "msiserver")
                    RequireIdle(name, service.State);
                journal.Add($"{step} : disponible, état {Describe(service.State)}.");
            }

            step = "contrôle du service TrustedInstaller";
            RequireIdle("TrustedInstaller", host.ReadServiceState("TrustedInstaller"));
            journal.Add($"{step} : arrêté.");

            step = "contrôle du redémarrage en attente";
            if (host.IsRebootPending())
                throw new InvalidOperationException("redémarrez d’abord le PC");
            journal.Add($"{step} : aucun redémarrage en attente.");

            foreach (var backup in backups)
            {
                step = $"contrôle de {backup.Source}";
                var attributes = host.ReadAttributes(backup.Source);
                if (attributes is { } value && !IsOrdinaryDirectory(value))
                    throw new IOException($"dossier ordinaire attendu à {backup.Source}");
                backup.Present = attributes is not null;
                journal.Add($"{step} : {(backup.Present ? "dossier valide" : "absent, rien à renommer")}.");
            }

            foreach (var backup in backups)
            {
                step = $"contrôle de {backup.Destination}";
                if (host.ReadAttributes(backup.Destination) is not null)
                    throw new IOException($"le nom de destination {backup.Destination} existe déjà");
                journal.Add($"{step} : nom disponible.");
            }
        }
        catch (Exception ex)
        {
            journal.Add($"{step} : échec ({ex.Message}).");
            return new(1, string.Join("\n", journal),
                $"Remise à zéro de Windows Update non lancée : {ex.Message.TrimEnd('.')}. Aucune modification.");
        }

        try
        {
            foreach (var name in StopOrder)
            {
                step = $"arrêt de {name}";
                await SetServiceStateAsync(name, WindowsUpdateServiceState.Stopped).ConfigureAwait(false);
                journal.Add($"{step} : réussi.");
            }

            foreach (var backup in backups)
            {
                if (!backup.Present)
                    continue;
                step = $"renommage de {backup.Source} vers {backup.Destination}";
                host.MoveDirectory(backup.Source, backup.Destination);
                renamed.Add(backup);
                journal.Add($"{step} : réussi.");
            }

            foreach (var name in StartOrder)
            {
                step = $"démarrage de {name}";
                await SetServiceStateAsync(name, WindowsUpdateServiceState.Running).ConfigureAwait(false);
                journal.Add($"{step} : réussi.");
            }

            journal.Add("msiserver reste à démarrage à la demande et Virgil ne le démarre pas.");
            var kept = backups.Select(backup => $"{Path.GetFileName(backup.Source)} : " +
                (backup.Present ? backup.Destination : "absent"));
            return new(0, "Remise à zéro de Windows Update terminée.\n" +
                string.Join("\n", kept) +
                "\nVous pouvez supprimer manuellement les dossiers renommés plus tard. Virgil les conserve.\n" +
                string.Join("\n", journal), "");
        }
        catch (Exception ex)
        {
            journal.Add($"{step} : échec ({ex.Message}).");
            var incomplete = await RecoverAsync(renamed, journal).ConfigureAwait(false);
            var summary = incomplete.Count == 0
                ? "Rattrapage effectué : noms d'origine rétablis, services redémarrés."
                : "Rattrapage incomplet :\n" + string.Join("\n", incomplete);
            return new(incomplete.Count == 0 ? 1 : 2, string.Join("\n", journal),
                $"Remise à zéro de Windows Update interrompue à l'étape {step}. {summary}");
        }
    }

    private Backup[] GetBackups()
    {
        if (!Path.IsPathFullyQualified(systemDirectory))
            throw new InvalidOperationException("le répertoire système doit être un chemin complet");
        var system = Path.TrimEndingDirectorySeparator(Path.GetFullPath(systemDirectory));
        if (!string.Equals(Path.GetFileName(system), "System32", StringComparison.OrdinalIgnoreCase) ||
            Path.GetDirectoryName(system) is not { } windows)
            throw new InvalidOperationException("le répertoire système doit se terminer par System32");

        var suffix = ".virgil-" + timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return [new(Path.Combine(windows, "SoftwareDistribution"), suffix), new(Path.Combine(system, "catroot2"), suffix)];
    }

    private static void RequireIdle(string name, WindowsUpdateServiceState state)
    {
        if (state != WindowsUpdateServiceState.Stopped)
            throw new InvalidOperationException($"le service {name} est {Describe(state)}, une installation est probablement en cours. Réessayez plus tard");
    }

    private static bool IsOrdinaryDirectory(FileAttributes attributes) =>
        (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory;

    private async Task SetServiceStateAsync(string name, WindowsUpdateServiceState expected)
    {
        var started = timeProvider.GetTimestamp();
        var requested = false;
        while (true)
        {
            var current = host.ReadServiceState(name);
            var remaining = ServiceTimeout - timeProvider.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException($"service {name} : état attendu {Describe(expected)} en 30 secondes, état observé {Describe(current)}");
            if (current == expected)
                return;

            if (!requested && current is WindowsUpdateServiceState.Running or WindowsUpdateServiceState.Stopped or WindowsUpdateServiceState.Paused)
            {
                if (expected == WindowsUpdateServiceState.Stopped)
                    host.RequestStop(name);
                else
                    host.RequestStart(name);
                requested = true;
                continue;
            }

            await Task.Delay(remaining < PollInterval ? remaining : PollInterval, timeProvider, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task<List<string>> RecoverAsync(List<Backup> renamed, List<string> journal)
    {
        var incomplete = new List<string>();
        if (renamed.Count > 0)
        {
            foreach (var name in StopOrder)
                await RecoverServiceAsync(name, WindowsUpdateServiceState.Stopped, journal).ConfigureAwait(false);

            for (var i = renamed.Count - 1; i >= 0; i--)
            {
                var backup = renamed[i];
                var paths = $"{backup.Destination} vers {backup.Source}";
                try
                {
                    if (host.ReadAttributes(backup.Source) is not null)
                        throw new IOException("le nom d’origine est occupé");
                    if (host.ReadAttributes(backup.Destination) is not { } attributes || !IsOrdinaryDirectory(attributes))
                        throw new IOException("le dossier renommé est absent ou n’est plus un dossier ordinaire");
                    host.MoveDirectory(backup.Destination, backup.Source);
                    journal.Add($"Rattrapage, renommage de {paths} : réussi.");
                }
                catch (Exception ex)
                {
                    incomplete.Add(paths);
                    journal.Add($"Rattrapage, renommage de {paths} : échec ({ex.Message}).");
                }
            }
        }

        foreach (var name in StartOrder)
        {
            if (!await RecoverServiceAsync(name, WindowsUpdateServiceState.Running, journal).ConfigureAwait(false))
                incomplete.Add(name);
        }
        journal.Add("Rattrapage : Virgil ne démarre pas msiserver.");
        return incomplete;
    }

    private async Task<bool> RecoverServiceAsync(string name, WindowsUpdateServiceState expected, List<string> journal)
    {
        var step = $"Rattrapage, service {name} vers l’état {Describe(expected)}";
        try
        {
            await SetServiceStateAsync(name, expected).ConfigureAwait(false);
            journal.Add($"{step} : réussi.");
            return true;
        }
        catch (Exception ex)
        {
            journal.Add($"{step} : échec ({ex.Message}).");
            return false;
        }
    }

    private static string Describe(WindowsUpdateServiceState state) => state switch
    {
        WindowsUpdateServiceState.Stopped => "arrêté",
        WindowsUpdateServiceState.StartPending => "en cours de démarrage",
        WindowsUpdateServiceState.StopPending => "en cours d’arrêt",
        WindowsUpdateServiceState.Running => "en cours d’exécution",
        WindowsUpdateServiceState.ContinuePending => "en cours de reprise",
        WindowsUpdateServiceState.PausePending => "en cours de mise en pause",
        WindowsUpdateServiceState.Paused => "en pause",
        _ => $"inconnu ({(uint)state})"
    };

    private sealed class Backup(string source, string suffix)
    {
        public string Source { get; } = source;
        public string Destination { get; } = source + suffix;
        public bool Present { get; set; }
    }
}
