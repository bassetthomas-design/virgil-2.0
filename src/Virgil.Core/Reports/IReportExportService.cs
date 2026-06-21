using Virgil.Domain;

namespace Virgil.Core.Reports;

public interface IReportExportService
{
    string BuildText(ReportEntry report, bool includeTechnicalDetails);

    Task<ReportExportResult> ExportAsync(
        ReportEntry report,
        string destinationPath,
        bool includeTechnicalDetails,
        bool overwriteConfirmed,
        CancellationToken cancellationToken);
}
