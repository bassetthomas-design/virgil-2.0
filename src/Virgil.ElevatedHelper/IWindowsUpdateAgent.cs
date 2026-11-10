namespace Virgil.ElevatedHelper;

// The adapter owns COM objects; callers receive only this narrow maintenance port.
public interface IWindowsUpdateItem
{
    string Identity => string.Empty;
    string Title => string.Empty;
    bool IsFirmware => false;
    bool IsInstalled => false;
    bool IsFeatureUpgrade { get; }
    bool EulaAccepted { get; }
    bool IsDownloaded { get; }
    void AcceptEula();
}
public sealed record WindowsUpdateItemInstallResult(string Identity, bool Success, int ResultCode, int ErrorCode);
public sealed record WindowsUpdateInstallResult(int InstalledCount, bool RebootRequired, bool Success)
{
    public IReadOnlyList<WindowsUpdateItemInstallResult> Items { get; init; } = [];
}
public interface IWindowsUpdateAgent
{
    bool IsServiceRegistered(string serviceId);
    void AddService(string serviceId, int flags);
    Task<IReadOnlyList<IWindowsUpdateItem>> SearchAsync(string criteria, int serverSelection, string? serviceId);
    Task DownloadAsync(IReadOnlyList<IWindowsUpdateItem> updates);
    Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<IWindowsUpdateItem> updates);
}
