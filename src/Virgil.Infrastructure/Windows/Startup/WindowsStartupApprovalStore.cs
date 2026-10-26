using Virgil.Core.Interventions;
using Virgil.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Virgil.Core.Startup;

namespace Virgil.Infrastructure.Windows.Startup;

public sealed class WindowsStartupApprovalStore : IStartupApprovalStore
{
    private readonly string _backupDirectory;
    private readonly ElevatedHelperClient? _helper;
    public WindowsStartupApprovalStore(ElevatedHelperClient helper) : this() => _helper = helper;
    public WindowsStartupApprovalStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Virgil", "startup-backups")) { }
    public WindowsStartupApprovalStore(string backupDirectory) => _backupDirectory = backupDirectory;
    private sealed record Envelope(string Payload, string Sha256);

    public Task<string?> ReadAsync(StartupApprovalTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!StartupChangeService.IsSupported(target)) return Task.FromResult<string?>(null);
        using var root = RegistryKey.OpenBaseKey(target.Hive == StartupApprovalHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(KeyPath(target), false);
        var bytes = key?.GetValue(target.ValueName) as byte[];
        return Task.FromResult(bytes is null ? null : Convert.ToHexString(bytes));
    }

    public async Task<string?> SaveVerifiedBackupAsync(StartupBackup backup, CancellationToken token)
    {
        if (!StartupChangeService.IsSupported(backup.Target)) return null;
        Directory.CreateDirectory(_backupDirectory);
        var id = Guid.NewGuid().ToString("N");
        var payload = JsonSerializer.Serialize(backup);
        var envelope = new Envelope(payload, Hash(payload));
        var path = BackupPath(id)!;
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(envelope), token);
            File.Move(temporary, path, false);
            return await ReadBackupAsync(id, token) == backup ? id : null;
        }
        finally
        {
            // Temporary backup metadata only, never startup entries or user files.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<StartupBackup?> ReadBackupAsync(string backupId, CancellationToken token)
    {
        var path = BackupPath(backupId);
        if (path is null || !File.Exists(path)) return null;
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(await File.ReadAllTextAsync(path, token));
            if (envelope is null || !string.Equals(envelope.Sha256, Hash(envelope.Payload), StringComparison.Ordinal)) return null;
            var backup = JsonSerializer.Deserialize<StartupBackup>(envelope.Payload);
            return backup is not null && StartupChangeService.IsSupported(backup.Target) ? backup : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { return null; }
    }

    public async Task<bool> WriteIfUnchangedAsync(StartupApprovalTarget target, string expectedValue, string value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!StartupChangeService.IsSupported(target) || await ReadAsync(target, token) != expectedValue) return false;
        if (target.Hive == StartupApprovalHive.LocalMachine)
        {
            if (_helper is null) return false;
            var kind = target.Kind switch
            {
                Virgil.Core.Startup.StartupApprovalKind.Run => Virgil.Contracts.StartupApprovalKind.Run,
                Virgil.Core.Startup.StartupApprovalKind.Run32 => Virgil.Contracts.StartupApprovalKind.Run32,
                Virgil.Core.Startup.StartupApprovalKind.StartupFolder => Virgil.Contracts.StartupApprovalKind.StartupFolder,
                _ => throw new ArgumentOutOfRangeException(nameof(target))
            };
            var result = await _helper.ExecuteAsync(new InterventionDefinition
            {
                Id = InterventionId.SetStartupApproval, Title = "Modifier le démarrage pour tous les utilisateurs",
                RequiresAdministrator = true, RiskLevel = InterventionRiskLevel.Sensitive
            }, new StartupApprovalWrite(kind, target.ValueName, expectedValue, value), token).ConfigureAwait(false);
            return result.Status == InterventionStatus.Completed && result.ExitCode == 0 && await ReadAsync(target, token) == value;
        }
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        // An existing approval value is required: never create or remove startup entries.
        using var key = root.OpenSubKey(KeyPath(target), true);
        if (key?.GetValue(target.ValueName) is not byte[] previous || Convert.ToHexString(previous) != expectedValue) return false;
        key.SetValue(target.ValueName, Convert.FromHexString(value), RegistryValueKind.Binary);
        key.Flush();
        return await ReadAsync(target, token) == value;
    }

    private string? BackupPath(string id) => Guid.TryParseExact(id, "N", out _) ? Path.Combine(_backupDirectory, id + ".json") : null;
    private static string Hash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    private static string KeyPath(StartupApprovalTarget target) => @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + target.Kind;
}
