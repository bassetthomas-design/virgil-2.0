using CommunityToolkit.Mvvm.ComponentModel;
using Virgil.Contracts;

namespace Virgil.App.ViewModels;

public sealed partial class CleanupSelectionRow : ObservableObject
{
    public CleanupSelectionRow(CleanupZonePreview preview)
    {
        Preview = preview;
        isSelected = CanSelect && CanGroupSelect && preview.Definition.Group == CleanupGroup.Sur;
    }
    public CleanupZonePreview Preview { get; }
    public string Title => Preview.Definition.SoftwareName is null ? Preview.Definition.DisplayName
        : Preview.Definition.IsPersonalFolder ? Preview.Candidates.FirstOrDefault()?.FullPath ?? Preview.Definition.RootPath : Preview.Definition.SoftwareName;
    public string Summary => $"{CleanupViewModel.FormatBytes(Preview.EligibleBytes)} · {Preview.EligibleFileCount} fichiers" + (Preview.Definition.NeedsReview ? " · À vérifier" : "");
    public string Details => $"{Preview.Definition.Description}\n{Preview.EligibleBytes} octets, {Preview.EligibleFileCount} fichiers\n" +
        (Preview.Definition.IsPersonalFolder ? "Dossier personnel — sélection ligne par ligne, confirmation critique, quarantaine uniquement.\n" : "") +
        (Preview.Definition.NeedsReview ? "À confirmer — sélection ligne par ligne uniquement.\n" : "") +
        Preview.Definition.RootPath + "\n" + string.Join("\n", Preview.Definition.RegistryKeys) + "\n" +
        string.Join("\n", Preview.Candidates.Select(c => c.FullPath)) + "\n" +
        string.Join("\n", Preview.Candidates.Where(c => !c.IsEligible).Select(c => c.ExclusionReason));
    public bool CanSelect => Preview.Definition.Id != CleanupZoneId.WindowsOld && Preview.Definition.IsExecutable && Preview.HasEligibleCandidates &&
        Preview.Definition.Classification is CleanupClassification.Cleanable or CleanupClassification.AdvancedCleanable;
    public bool CanGroupSelect => CanSelect && !Preview.Definition.IsPersonalFolder && !Preview.Definition.NeedsReview &&
        !Preview.Candidates.Any(c => c.IsPersonalFolder || c.NeedsReview);
    [ObservableProperty] private bool isSelected;
}

public sealed class CleanupGroupItem : ObservableObject
{
    public CleanupGroupItem(CleanupGroup group, IEnumerable<CleanupZonePreview> previews)
        : this(group, previews.Where(p => p.EligibleBytes > 0).OrderByDescending(p => p.EligibleBytes).Select(p => new CleanupSelectionRow(p)).ToArray()) { }
    public CleanupGroupItem(CleanupGroup group, IReadOnlyList<CleanupSelectionRow> rows)
    {
        Group = group;
        Rows = rows;
        Sections = Rows.GroupBy(r => r.Preview.Definition.SoftwareName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CleanupRowSection(g.Key.Length == 0 ? string.Empty : g.First().Preview.Definition.DisplayName, g.ToArray())).ToArray();
        _selectableCount = Rows.Count(r => r.CanGroupSelect);
        _selectedCount = Rows.Count(r => r.CanGroupSelect && r.IsSelected);
        foreach (var row in Rows) row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CleanupSelectionRow.IsSelected) || !row.CanGroupSelect) return;
            _selectedCount += row.IsSelected ? 1 : -1;
            if (!_batch) OnPropertyChanged(nameof(IsSelected));
        };
    }
    private readonly int _selectableCount;
    private int _selectedCount;
    private bool _batch;
    public CleanupGroup Group { get; }
    public string Title => Group switch { CleanupGroup.Sur => "Sans risque", CleanupGroup.AVerifier => "À vérifier", _ => "Restes de logiciels désinstallés" };
    public IReadOnlyList<CleanupSelectionRow> Rows { get; }
    public IReadOnlyList<CleanupRowSection> Sections { get; }
    public long EligibleBytes => Rows.Sum(r => r.Preview.EligibleBytes);
    public bool? IsSelected
    {
        get => _selectedCount == 0 ? false : _selectedCount == _selectableCount ? true : null;
        set
        {
            _batch = true;
            try { foreach (var row in Rows) if (row.CanGroupSelect) row.IsSelected = value == true; }
            finally { _batch = false; OnPropertyChanged(); }
        }
    }

}

public sealed record CleanupRowSection(string Title, IReadOnlyList<CleanupSelectionRow> Rows);

public sealed class CleanupCategoryItem
{
    public CleanupCategoryItem(string title, IEnumerable<CleanupSelectionRow> rows)
    {
        Title = title;
        Selection = new CleanupGroupItem(CleanupGroup.AVerifier, rows.OrderByDescending(r => r.Preview.EligibleBytes).ToArray());
    }
    public string Title { get; }
    public CleanupGroupItem Selection { get; }
    public IReadOnlyList<CleanupSelectionRow> Rows => Selection.Rows;
    public long Bytes => Rows.Sum(r => r.Preview.EligibleBytes);
    public string Header => $"{Title} — {CleanupViewModel.FormatBytes(Bytes)} ({Rows.Count})";
}
