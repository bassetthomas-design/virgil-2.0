namespace Virgil.Core.Scanning;

internal sealed record ScanReaderResult<T>(T Value, IReadOnlyList<string> Errors)
{
    public static ScanReaderResult<T> Success(T value)
    {
        return new ScanReaderResult<T>(value, Array.Empty<string>());
    }
}
