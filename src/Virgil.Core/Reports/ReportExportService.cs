using System.Text;
using Virgil.Domain;

namespace Virgil.Core.Reports;

public sealed class ReportExportService : IReportExportService
{
    private readonly IReportSanitizer _sanitizer;

    public ReportExportService()
        : this(new ReportSanitizer())
    {
    }

    public ReportExportService(IReportSanitizer sanitizer)
    {
        _sanitizer = sanitizer;
    }

    public string BuildText(ReportEntry report, bool includeTechnicalDetails)
    {
        var sanitized = _sanitizer.Sanitize(report);
        var builder = new StringBuilder();
        builder.AppendLine("VIRGIL 2.0 - RAPPORT");
        builder.AppendLine();
        builder.AppendLine($"Date : {sanitized.Date:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Type : {sanitized.Kind}");
        builder.AppendLine($"Titre : {sanitized.Title}");
        builder.AppendLine($"Resume : {sanitized.Summary}");
        builder.AppendLine($"Etat : {sanitized.Status}");
        builder.AppendLine($"Module : {sanitized.Module}");
        builder.AppendLine($"Redemarrage requis : {(sanitized.RestartRequired ? "oui" : "non")}");
        builder.AppendLine();
        AppendActions(builder, "Actions proposees", sanitized.ProposedActions);
        AppendActions(builder, "Actions executees", sanitized.ExecutedActions);
        AppendActions(builder, "Actions passees", sanitized.SkippedActions);
        AppendValues(builder, "Erreurs", sanitized.Errors);
        builder.AppendLine("Vue simple :");
        builder.AppendLine(string.IsNullOrWhiteSpace(sanitized.SimpleView) ? sanitized.Summary : sanitized.SimpleView);
        builder.AppendLine();
        builder.AppendLine("Details techniques :");
        builder.AppendLine(includeTechnicalDetails && !string.IsNullOrWhiteSpace(sanitized.TechnicalDetails)
            ? sanitized.TechnicalDetails
            : "Masques dans cet export.");
        return builder.ToString();
    }

    public async Task<ReportExportResult> ExportAsync(
        ReportEntry report,
        string destinationPath,
        bool includeTechnicalDetails,
        bool overwriteConfirmed,
        CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        try
        {
            var destination = ValidateDestination(destinationPath);
            if (File.Exists(destination) && !overwriteConfirmed)
            {
                return Failure("Un fichier existe deja. Confirmez son remplacement dans la boite de dialogue.");
            }

            var directory = Path.GetDirectoryName(destination)!;
            temporaryPath = Path.Combine(
                directory,
                "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            var text = BuildText(report, includeTechnicalDetails);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destination, overwrite: overwriteConfirmed);
            temporaryPath = null;
            return new ReportExportResult
            {
                Success = true,
                ExportedPath = destination
            };
        }
        catch (OperationCanceledException)
        {
            return Failure("Export annule.");
        }
        catch
        {
            return Failure("Export du rapport impossible.");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDelete(temporaryPath);
            }
        }
    }

    private static void AppendActions(StringBuilder builder, string title, IReadOnlyList<ReportAction> actions)
    {
        builder.AppendLine(title + " :");
        if (actions.Count == 0)
        {
            builder.AppendLine("- Aucune");
        }
        else
        {
            foreach (var action in actions)
            {
                var error = string.IsNullOrWhiteSpace(action.ReadableError)
                    ? string.Empty
                    : $" - erreur : {action.ReadableError}";
                builder.AppendLine($"- {action.Name} [{action.Status}] : {action.Result}{error}");
            }
        }

        builder.AppendLine();
    }

    private static void AppendValues(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine(title + " :");
        if (values.Count == 0)
        {
            builder.AppendLine("- Aucune");
        }
        else
        {
            foreach (var value in values)
            {
                builder.AppendLine("- " + value);
            }
        }

        builder.AppendLine();
    }

    private static string ValidateDestination(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("TXT destination required.");
        }

        if (path.StartsWith("\\\\", StringComparison.Ordinal) || Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            throw new InvalidOperationException("Network destinations refused.");
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException();
        }

        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrWhiteSpace(root))
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType == DriveType.Network)
            {
                throw new InvalidOperationException("Network destinations refused.");
            }
        }

        return fullPath;
    }

    private static ReportExportResult Failure(string message)
    {
        return new ReportExportResult
        {
            Success = false,
            ReadableError = message
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort only.
        }
    }
}
