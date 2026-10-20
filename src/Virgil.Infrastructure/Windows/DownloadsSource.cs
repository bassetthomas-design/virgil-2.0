using Virgil.Core.Cleanup;

namespace Virgil.Infrastructure.Windows;

public sealed class DownloadsSource : IDownloadsSource
{
    public IReadOnlyList<FootprintFile> ReadFiles(string downloadsRoot, CancellationToken cancellationToken) =>
        SoftwareFootprintSource.ReadFiles(downloadsRoot, cancellationToken);
}
