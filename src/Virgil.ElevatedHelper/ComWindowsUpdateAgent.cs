using System.Runtime.InteropServices;

namespace Virgil.ElevatedHelper;

// All WUA dynamic/COM access is confined to this adapter. WUA calls are synchronous:
// cancellation is observed by the runner between calls, never by aborting COM.
internal sealed class ComWindowsUpdateAgent : IWindowsUpdateAgent
{
    private static dynamic Create(string progId) => Activator.CreateInstance(Type.GetTypeFromProgID(progId, true)!)!;
    private dynamic? _session;
    private dynamic Session => _session ??= Create("Microsoft.Update.Session");
    public bool IsServiceRegistered(string serviceId)
    {
        dynamic manager = Create("Microsoft.Update.ServiceManager");
        for (var i = 0; i < manager.Services.Count; i++)
            if (string.Equals((string)manager.Services.Item(i).ServiceID, serviceId, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    public void AddService(string serviceId, int flags)
    {
        dynamic manager = Create("Microsoft.Update.ServiceManager");
        manager.ClientApplicationID = "MAJ-TOTALE";
        manager.AddService2(serviceId, flags, "");
    }
    public Task<IReadOnlyList<IWindowsUpdateItem>> SearchAsync(string criteria, int serverSelection, string? serviceId)
    {
        dynamic searcher = Session.CreateUpdateSearcher();
        searcher.ServerSelection = serverSelection;
        if (serviceId is not null) searcher.ServiceID = serviceId;
        dynamic result = searcher.Search(criteria);
        if ((int)result.ResultCode is not (2 or 3)) throw new COMException("Recherche Windows Update impossible.");
        var updates = new List<IWindowsUpdateItem>();
        for (var i = 0; i < result.Updates.Count; i++) updates.Add(new Item(result.Updates.Item(i)));
        return Task.FromResult<IReadOnlyList<IWindowsUpdateItem>>(updates);
    }
    private static dynamic Collection(IReadOnlyList<IWindowsUpdateItem> updates)
    {
        dynamic collection = Create("Microsoft.Update.UpdateColl");
        foreach (var update in updates.Cast<Item>()) collection.Add(update.Value);
        return collection;
    }
    public Task DownloadAsync(IReadOnlyList<IWindowsUpdateItem> updates)
    {
        dynamic downloader = Session.CreateUpdateDownloader();
        downloader.Updates = Collection(updates);
        downloader.Download();
        return Task.CompletedTask;
    }
    public Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<IWindowsUpdateItem> updates)
    {
        dynamic installer = Session.CreateUpdateInstaller();
        installer.Updates = Collection(updates);
        dynamic result = installer.Install();
        var count = 0;
        for (var i = 0; i < updates.Count; i++)
            if ((int)result.GetUpdateResult(i).ResultCode == 2) count++;
        return Task.FromResult(new WindowsUpdateInstallResult(count, (bool)result.RebootRequired, (int)result.ResultCode == 2));
    }
    private sealed class Item(dynamic value) : IWindowsUpdateItem
    {
        internal dynamic Value => value;
        public bool IsFeatureUpgrade
        {
            get
            {
                for (var i = 0; i < value.Categories.Count; i++)
                    if (string.Equals((string)value.Categories.Item(i).CategoryID, UpdateMaintenanceRunner.FeatureCategoryId, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
        }
        public bool EulaAccepted => (bool)value.EulaAccepted;
        public bool IsDownloaded => (bool)value.IsDownloaded;
        public void AcceptEula() => value.AcceptEula();
    }
}
