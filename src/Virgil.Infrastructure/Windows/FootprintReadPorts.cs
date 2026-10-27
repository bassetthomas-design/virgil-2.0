using Microsoft.Win32;
using System.Diagnostics;

namespace Virgil.Infrastructure.Windows;

/// <summary>Capacité strictement en lecture seule, y compris pour les sous-clés.</summary>
public interface IFootprintRegistry
{
    IFootprintRegistryKey OpenBaseKey(RegistryHive hive, RegistryView view);
}

public interface IFootprintRegistryKey : IDisposable
{
    IFootprintRegistryKey? OpenSubKey(string name);
    string[] GetSubKeyNames();
    string[] GetValueNames();
    object? GetValue(string name);
}

public sealed class WindowsFootprintRegistry : IFootprintRegistry
{
    public IFootprintRegistryKey OpenBaseKey(RegistryHive hive, RegistryView view) =>
        new ReadKey(RegistryKey.OpenBaseKey(hive, view));

    private sealed class ReadKey(RegistryKey key) : IFootprintRegistryKey
    {
        public IFootprintRegistryKey? OpenSubKey(string name) =>
            key.OpenSubKey(name, writable: false) is { } child ? new ReadKey(child) : null;
        public string[] GetSubKeyNames() => key.GetSubKeyNames();
        public string[] GetValueNames() => key.GetValueNames();
        public object? GetValue(string name) => key.GetValue(name);
        public void Dispose() => key.Dispose();
    }
}

public sealed record FootprintFileRoots(string LocalAppData, string AppData, string ProgramData,
    string ProgramFiles, string ProgramFilesX86)
{
    public IReadOnlyList<string> All => [LocalAppData, AppData, ProgramData, ProgramFiles, ProgramFilesX86];
    public static FootprintFileRoots Windows() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
}

public sealed record FootprintProcessPaths(IReadOnlyList<string> Paths, bool IsComplete);

public interface IFootprintProcessPaths
{
    FootprintProcessPaths Read(CancellationToken cancellationToken);
}

public sealed class WindowsFootprintProcessPaths : IFootprintProcessPaths
{
    public FootprintProcessPaths Read(CancellationToken cancellationToken)
    {
        var paths = new List<string>();
        var complete = true;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { if (process.MainModule?.FileName is { } path) paths.Add(path); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                { complete = false; }
            }
        }
        return new(paths, complete);
    }
}
