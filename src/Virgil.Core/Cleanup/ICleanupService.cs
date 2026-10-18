using Virgil.Contracts;

namespace Virgil.Core.Cleanup;

public interface ICleanupService
{
    CleanupPreview PreviewTemporaryFiles();
}
