using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public interface ICleanupService
{
    CleanupPreview PreviewTemporaryFiles();
}
