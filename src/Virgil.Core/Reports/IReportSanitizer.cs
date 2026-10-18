using Virgil.Contracts;

namespace Virgil.Core.Reports;

public interface IReportSanitizer
{
    ReportEntry Sanitize(ReportEntry report);

    string SanitizeText(string? value);
}
