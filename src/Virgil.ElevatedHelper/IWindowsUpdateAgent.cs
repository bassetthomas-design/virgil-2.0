namespace Virgil.ElevatedHelper;

// The adapter owns COM objects; callers receive only this narrow maintenance port.
public interface IWindowsUpdateItem
{
    bool IsFeatureUpgrade { get; }
    bool EulaAccepted { get; }
    bool IsDownloaded { get; }
    void AcceptEula();
}
public sealed record WindowsUpdateInstallResult(int InstalledCount, bool RebootRequired, bool Success);
public interface IWindowsUpdateAgent
{
    bool IsServiceRegistered(string serviceId);
    void AddService(string serviceId, int flags);
    Task<IReadOnlyList<IWindowsUpdateItem>> SearchAsync(string criteria, int serverSelection, string? serviceId);
    Task DownloadAsync(IReadOnlyList<IWindowsUpdateItem> updates);
    Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<IWindowsUpdateItem> updates);
}
