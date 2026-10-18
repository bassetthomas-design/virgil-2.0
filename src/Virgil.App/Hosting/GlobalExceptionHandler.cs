using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Threading;
using Virgil.Infrastructure.Storage;

namespace Virgil.App.Hosting;

public interface IUserNotifier
{
    void Notify(bool fatal);
}

public sealed class MessageBoxUserNotifier(VirgilPaths paths, Dispatcher dispatcher) : IUserNotifier
{
    public void Notify(bool fatal)
    {
        void Show() => MessageBox.Show(
            (fatal ? "Virgil doit se fermer suite a une erreur inattendue.\n" : "Une erreur inattendue s'est produite.\n")
            + "Details enregistres dans les journaux : " + paths.LogsDirectory,
            "Virgil", MessageBoxButton.OK, MessageBoxImage.Error);
        if (dispatcher.CheckAccess()) Show();
        else dispatcher.Invoke(Show);
    }
}

public sealed class ExceptionReporter(ILogger<ExceptionReporter> logger, IUserNotifier notifier)
{
    public void Report(Exception ex, string source, bool fatal)
    {
        // Exception messages can contain file contents or secrets. Keep only type and stack trace.
        Write(fatal ? LogLevel.Critical : LogLevel.Error, source, ex);
        try { notifier.Notify(fatal); }
        catch (Exception notificationError) { Write(LogLevel.Error, "UserNotifier", notificationError); }
    }

    private void Write(LogLevel level, string source, Exception ex)
    {
        try { logger.Log(level, "Exception depuis {Source} : {ExceptionType}\n{StackTrace}", source, ex.GetType().FullName, ex.StackTrace); }
        catch { /* Reporting must never throw, including a failing logging provider. */ }
    }
}
