using Virgil.Core.Cleanup;

namespace Virgil.Infrastructure.Windows;

public sealed class CleanupDisk : ICleanupDisk
{
    public IRecycleBinService RecycleBin { get; } = new WindowsRecycleBinService();

    public CleanupFileState ReadFile(string path)
    {
        var file = new FileInfo(path);
        var lastWrite = new DateTimeOffset(DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc));
        return new CleanupFileState(file.Length, lastWrite);
    }

    public void DeleteFile(string path) => File.Delete(path);
    public IReadOnlyList<string> GetDirectories(string path) => Directory.GetDirectories(path);
    public bool HasEntries(string path) => Directory.EnumerateFileSystemEntries(path).Any();
    public void DeleteEmptyDirectory(string path) => Directory.Delete(path, false);
}
