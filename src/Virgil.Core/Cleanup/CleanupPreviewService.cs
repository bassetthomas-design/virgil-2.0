using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public sealed class CleanupPreviewService : ICleanupService
{
    public CleanupPreview PreviewTemporaryFiles()
    {
        var tempPath = Path.GetTempPath();
        var targets = new List<CleanupTarget>();

        if (Directory.Exists(tempPath))
        {
            targets.Add(ReadDirectorySize("Temporaires utilisateur", tempPath));
        }

        return new CleanupPreview(DateTimeOffset.Now, targets);
    }

    private static CleanupTarget ReadDirectorySize(string name, string directoryPath)
    {
        long totalBytes = 0;
        var totalFiles = 0;

        foreach (var filePath in SafeEnumerateFiles(directoryPath))
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                totalBytes += fileInfo.Length;
                totalFiles++;
            }
            catch
            {
                // Best effort preview.
            }
        }

        return new CleanupTarget(name, directoryPath, totalBytes, totalFiles, "Faible");
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directoryPath)
    {
        var pending = new Stack<string>();
        pending.Push(directoryPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                pending.Push(child);
            }
        }
    }
}
