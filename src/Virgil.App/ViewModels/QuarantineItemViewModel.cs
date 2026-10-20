using CommunityToolkit.Mvvm.ComponentModel;
using Virgil.Core.Cleanup;

namespace Virgil.App.ViewModels;

public partial class QuarantineItemViewModel(QuarantineEntry entry) : ObservableObject
{
    public QuarantineEntry Entry { get; } = entry;
    public string Name => System.IO.Path.GetFileName(Entry.OriginalPath);
    public string Details => $"{Entry.SizeBytes} octets — mise en quarantaine : {Entry.QuarantinedAtUtc.LocalDateTime:g} — expiration : {Entry.ExpiresAtUtc.LocalDateTime:g}";
    [ObservableProperty] private bool isSelected;
}
