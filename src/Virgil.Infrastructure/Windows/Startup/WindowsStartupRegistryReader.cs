using Microsoft.Win32;
using Virgil.Core.Startup;

namespace Virgil.Infrastructure.Windows.Startup;

public sealed record StartupRunValue(string Name, string Command, byte[]? Approval);
public interface IStartupRegistryReader
{
    IReadOnlyList<StartupRunValue> ReadRun(StartupApprovalHive hive, bool is32Bit);
}
internal sealed class WindowsStartupRegistryReader : IStartupRegistryReader
{
    public IReadOnlyList<StartupRunValue> ReadRun(StartupApprovalHive hive, bool is32Bit)
    {
        var registryHive = hive == StartupApprovalHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        using var root = RegistryKey.OpenBaseKey(registryHive, is32Bit ? RegistryView.Registry32 : RegistryView.Registry64);
        using var run = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        using var approvalRoot = RegistryKey.OpenBaseKey(registryHive, RegistryView.Registry64);
        var kind = is32Bit ? "Run32" : "Run";
        using var approved = approvalRoot.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + kind, false);
        return run?.GetValueNames().Select(name => new StartupRunValue(name,
            run.GetValue(name) as string ?? "Chemin indisponible", approved?.GetValue(name) as byte[])).ToArray() ?? [];
    }
}
