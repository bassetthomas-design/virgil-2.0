using CommunityToolkit.Mvvm.ComponentModel;
using Virgil.Core.Cleanup;

namespace Virgil.App.ViewModels;

public partial class QuarantineItemViewModel(QuarantineEntry entry, DateTimeOffset now) : ObservableObject
{
    public QuarantineEntry Entry { get; } = entry;
    public string Name => System.IO.Path.GetFileName(Entry.OriginalPath);
    public bool IsExpired { get; } = entry.ExpiresAtUtc <= now;
    public string ExpirationStatus => IsExpired ? "Expiré" : "Conservé en quarantaine";
    public string Details => $"{Entry.SizeBytes} octets — mise en quarantaine : {Entry.QuarantinedAtUtc.LocalDateTime:g} — expiration : {Entry.ExpiresAtUtc.LocalDateTime:g}";
    [ObservableProperty] private bool isSelected;
}
