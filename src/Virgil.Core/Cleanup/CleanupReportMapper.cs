using Virgil.Core.Reports;
using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

public static class CleanupReportMapper
{
    public static ReportEntry Map(CleanupSessionReport report, int analyzedZones)
    {
        return ReportMapper.FromCleanup(report, analyzedZones);
    }
}
