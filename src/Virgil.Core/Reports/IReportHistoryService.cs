using Virgil.Domain;

namespace Virgil.Core.Reports;

public interface IReportStorageRootProvider
{
    string GetApplicationDataDirectory();
}

public interface IReportHistoryService
{
    Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken);

    Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken);

    Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken);

    Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken);
}
