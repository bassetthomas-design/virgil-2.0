using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using Virgil.Core.Applications;

namespace Virgil.Infrastructure.Applications;

public sealed class ApplicationIconLoader : IApplicationIconLoader
{
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _workers = new(4);

    public Task<byte[]?> LoadAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            return Task.FromResult<byte[]?>(null);
        if (_cache.Count > 256) _cache.Clear();
        return _cache.GetOrAdd(path, value => new Lazy<Task<byte[]?>>(() => LoadCoreAsync(value))).Value.WaitAsync(cancellationToken);
    }

    private async Task<byte[]?> LoadCoreAsync(string path)
    {
        await _workers.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists || info.Length > 32 * 1024 * 1024 && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
                    var extension = Path.GetExtension(path);
                    if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        using var icon = Icon.ExtractAssociatedIcon(path);
                        if (icon is null) return null;
                        using var bitmap = icon.ToBitmap();
                        using var stream = new MemoryStream();
                        bitmap.Save(stream, ImageFormat.Png);
                        return stream.ToArray();
                    }
                    if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)) return File.ReadAllBytes(path);
                    return null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.ExternalException) { return null; }
            }).ConfigureAwait(false);
        }
        finally { _workers.Release(); }
    }
}
