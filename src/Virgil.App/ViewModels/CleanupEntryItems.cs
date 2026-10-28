using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Virgil.Contracts;
using Virgil.Core.Processes;

namespace Virgil.App.ViewModels;

public sealed record CleanupSectionTitleItem(string Text);
public sealed record CleanupTextCardItem(string Text);

public sealed class CleanupZoneItemViewModel
{
    public CleanupZoneItemViewModel(CleanupZonePreview preview)
    {
        Preview = preview;
        Title = preview.Definition.DisplayName.ToUpperInvariant();
        var lines = new List<string>
        {
            preview.Definition.Description,
            $"Racine : {CleanupViewModel.LogicalRootLabel(preview.Definition.Id)}",
            $"Risque : {CleanupViewModel.RiskLabel(preview.Definition.RiskLevel)}",
            $"Categorie : {CleanupViewModel.ClassificationLabel(preview.Definition.Classification)}",
            $"Action : {(preview.Definition.IsExecutable ? "guidee après validation" : "information seulement")}",
            $"Examines : {preview.ExaminedFileCount} fichiers",
            $"Eligibles : {preview.EligibleFileCount} fichiers, {CleanupViewModel.FormatBytes(preview.EligibleBytes)}",
            $"Exclus : {preview.ExcludedFileCount} fichiers",
            $"Anciennete minimale : {CleanupViewModel.FormatAge(preview.Definition.MinimumAge)}",
            $"Avertissement : {preview.Definition.Warning}"
        };
        if (preview.Errors.Count > 0)
            lines.Add("Erreurs lisibles : " + string.Join(" | ", preview.Errors.Take(3)));
        Lines = lines;
    }

    public CleanupZonePreview Preview { get; }
    public string Title { get; }
    public IReadOnlyList<string> Lines { get; }
}

public partial class CleanupReviewItemViewModel : ObservableObject
{
    private readonly CleanupStorageReviewItem _item;
    private readonly IProcessRunner _processRunner;
    private readonly Action<string> _message;

    public CleanupReviewItemViewModel(CleanupStorageReviewItem item, IProcessRunner processRunner, Action<string> message)
    {
        _item = item;
        _processRunner = processRunner;
        _message = message;
        Title = item.Name.ToUpperInvariant();
        Lines = new[]
        {
            $"{item.ItemType} - {CleanupViewModel.FormatBytes(item.SizeBytes)} - {item.LastWriteTimeUtc:yyyy-MM-dd}",
            $"{CleanupViewModel.ClassificationLabel(item.Classification)} - {item.Reason}",
            $"Emplacement : {item.FullPath}"
        };
    }

    public string Title { get; }
    public IReadOnlyList<string> Lines { get; }
    [ObservableProperty] private bool isHidden;

    [RelayCommand]
    private void Ignore() => IsHidden = true;

    [RelayCommand]
    private void Mark() => _message($"Element marque a revoir : {_item.Name}.\nAucune suppression effectuée.");

    [RelayCommand]
    private async Task OpenLocationAsync()
    {
        try
        {
            var result = await _processRunner.LaunchAsync(new ProcessLaunchRequest(
                "explorer.exe", Array.Empty<string>(), UseShellExecute: true,
                RawArguments: $"/select,\"{_item.FullPath}\""), CancellationToken.None);
            if (result.LaunchError is not null)
                _message("Emplacement indisponible. Aucune modification effectuée.");
        }
        catch
        {
            _message("Emplacement indisponible. Aucune modification effectuée.");
        }
    }
}
