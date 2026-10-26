namespace Virgil.Core.Processes;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken);

    /// <summary>Lance un processus sans capture, avec attente optionnelle annulable.</summary>
    Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken);
}

/// <summary>Exécution avec capture des sorties et délai maximal.</summary>
/// <param name="FileName">Exécutable à lancer sans shell.</param>
/// <param name="Arguments">Arguments copiés dans ArgumentList dans leur ordre d'origine.</param>
/// <param name="Timeout">Délai maximal ; son expiration provoque l'arrêt de l'arbre de processus.</param>
public sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);

/// <summary>Résultat d'une exécution avec capture.</summary>
/// <param name="ExitCode">Code de sortie, ou -1 en cas d'échec, timeout ou annulation.</param>
/// <param name="StandardOutput">Sortie standard capturée ; vide si échec ou interruption.</param>
/// <param name="StandardError">Sortie d'erreur capturée ; vide si échec ou interruption.</param>
/// <param name="TimedOut">Délai dépassé sans annulation par l'appelant.</param>
/// <param name="Cancelled">Annulation demandée par le token de l'appelant.</param>
/// <param name="LaunchError">Erreur de lancement ou d'état, sinon null.</param>
public sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    bool Cancelled = false,
    string? LaunchError = null);

/// <summary>Mode d'affichage demandé pour la fenêtre du processus.</summary>
public enum ProcessWindowMode
{
    /// <summary>Affichage normal, sans option de fenêtre imposée.</summary>
    Normal,
    /// <summary>Fenêtre masquée.</summary>
    Hidden
}

/// <summary>Paramètres de lancement sans capture des sorties ni timeout.</summary>
/// <param name="FileName">Exécutable ou cible à ouvrir.</param>
/// <param name="Arguments">Arguments dans l'ordre, copiés dans ArgumentList ; vide si RawArguments est fourni.</param>
/// <param name="UseShellExecute">Utilise le shell Windows lorsque vrai.</param>
/// <param name="Verb">Verbe du shell, par exemple runas ; aucune valeur imposée si null.</param>
/// <param name="WindowMode">Affichage normal ou fenêtre masquée ; ne force pas CreateNoWindow.</param>
/// <param name="RawArguments">Chaîne d'arguments déjà formatée, utilisée à la place d'ArgumentList si non null.</param>
/// <param name="WaitForExit">Attend la sortie avec le token d'annulation si vrai ; sinon renvoie immédiatement.</param>
public sealed record ProcessLaunchRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool UseShellExecute,
    string? Verb = null,
    ProcessWindowMode WindowMode = ProcessWindowMode.Normal,
    string? RawArguments = null,
    bool WaitForExit = false);

/// <summary>Résultat d'un lancement sans capture.</summary>
/// <param name="Started">Vrai si un objet processus a été obtenu et son état lu sans erreur.</param>
/// <param name="ExitCode">Code de sortie si le processus est terminé, sinon null.</param>
/// <param name="LaunchError">Message de l'exception de lancement ou de lecture d'état ; null en l'absence d'exception.</param>
public sealed record ProcessLaunchResult(bool Started, int? ExitCode, string? LaunchError = null);
