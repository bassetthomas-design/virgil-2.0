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
        : Preview.Definition.IsPersonalFolder ? Preview.Candidates.FirstOrDefault()?.FullPath ?? Preview.Definition.RootPath : Preview.Definition.RootPath;
    public string Details => $"{Preview.Definition.Description}\n{Preview.EligibleBytes} octets, {Preview.EligibleFileCount} fichiers\n" +
        (Preview.Definition.IsPersonalFolder ? "Dossier personnel — sélection ligne par ligne, confirmation Critical, quarantaine uniquement.\n" : "") +
        (Preview.Definition.NeedsReview ? "À confirmer — sélection ligne par ligne uniquement.\n" : "") +
        string.Join("\n", Preview.Definition.RegistryKeys) + "\n" +
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
    {
        Group = group;
        Rows = previews.SelectMany(Split).Select(p => new CleanupSelectionRow(p)).ToArray();
        Sections = Rows.GroupBy(r => r.Preview.Definition.SoftwareName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CleanupRowSection(g.Key.Length == 0 ? string.Empty : g.First().Preview.Definition.DisplayName, g.ToArray())).ToArray();
        foreach (var row in Rows) row.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(CleanupSelectionRow.IsSelected)) OnPropertyChanged(nameof(IsSelected)); };
    }
    public CleanupGroup Group { get; }
    public string Title => Group switch { CleanupGroup.Sur => "Sûr", CleanupGroup.AVerifier => "À vérifier", _ => "Restes de logiciels désinstallés" };
    public IReadOnlyList<CleanupSelectionRow> Rows { get; }
    public IReadOnlyList<CleanupRowSection> Sections { get; }
    public bool? IsSelected
    {
        get
        {
            var rows = Rows.Where(r => r.CanGroupSelect).ToArray();
            return rows.Length == 0 || rows.All(r => !r.IsSelected) ? false : rows.All(r => r.IsSelected) ? true : null;
        }
        set
        {
            foreach (var row in Rows.Where(r => r.CanGroupSelect)) row.IsSelected = value == true;
            OnPropertyChanged();
        }
    }
    private static IEnumerable<CleanupZonePreview> Split(CleanupZonePreview p)
    {
        if (p.Definition.IsExecutable && p.Candidates.Count > 0 &&
            (p.Definition.IsPersonalFolder || p.Definition.Id == CleanupZoneId.OldDownloads))
        {
            foreach (var c in p.Candidates)
                yield return p with
                {
                    Definition = p.Definition with { Description = p.Definition.Description + "\n" + c.FullPath },
                    Candidates = new[] { c }, ExaminedFileCount = 1, EligibleFileCount = c.IsEligible ? 1 : 0,
                    EligibleBytes = c.IsEligible ? c.SizeBytes : 0, ExcludedFileCount = c.IsEligible ? 0 : 1
                };
        }
        else yield return p;
    }
}

public sealed record CleanupRowSection(string Title, IReadOnlyList<CleanupSelectionRow> Rows);
