using System;
using System.Collections.Generic;
using System.IO;
using Virgil.Domain;

namespace Virgil.Core.Cleanup;

public sealed class CleanupPreviewService : ICleanupService
{
    public CleanupPreview PreviewTemporaryFiles()
    {
        var targets = new List<CleanupTarget>();
        var tempPath = TryGetTempPath();

        if (!string.IsNullOrWhiteSpace(tempPath) && CanReadDirectory(tempPath))
        {
            targets.Add(ReadDirectorySize("Temporaires utilisateur", tempPath));
        }

        return new CleanupPreview(DateTimeOffset.Now, targets);
    }

    private static string? TryGetTempPath()
    {
        try
        {
            return Path.GetTempPath();
        }
        catch
        {
            return null;
        }
    }

    private static bool CanReadDirectory(string directoryPath)
    {
        try
        {
            return Directory.Exists(directoryPath);
        }
        catch
        {
            return false;
        }
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

            foreach (var file in GetFiles(current))
            {
                yield return file;
            }

            foreach (var child in GetDirectories(current))
            {
                pending.Push(child);
            }
        }
    }

    private static IReadOnlyList<string> GetFiles(string directoryPath)
    {
        try
        {
            return Directory.GetFiles(directoryPath);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> GetDirectories(string directoryPath)
    {
        try
        {
            return Directory.GetDirectories(directoryPath);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
