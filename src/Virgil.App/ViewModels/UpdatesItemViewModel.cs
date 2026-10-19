using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Virgil.Contracts;

namespace Virgil.App.ViewModels;

public partial class UpdateItemViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    public UpdateItemViewModel(UpdateItem item, Action selectionChanged)
    {
        Item = item;
        _selectionChanged = selectionChanged;
        Header = $"{item.Name} - {item.AvailableVersion}";
        IsSelectable = item.Status == UpdateItemStatus.Available && item.RiskLevel != UpdateRiskLevel.CriticalInformationOnly;
        var lines = new List<string>
        {
            $"ID : {item.Id}",
            $"Version installee : {item.InstalledVersion}",
            $"Version disponible : {item.AvailableVersion}",
            $"Source : {UpdatesViewModel.SourceLabel(item.Source)}",
            $"Risque : {UpdatesViewModel.RiskLabel(item.RiskLevel)} - {item.RiskReason}",
            $"Statut : {UpdatesViewModel.StatusLabel(item.Status)}"
        };
        if (item.CommandPreview is not null)
            lines.Add($"Commande : {UpdatesViewModel.FormatCommand(item.CommandPreview)}");
        Lines = lines;
    }

    public UpdateItem Item { get; }
    public string Header { get; }
    public bool IsSelectable { get; }
    public IReadOnlyList<string> Lines { get; }
    [ObservableProperty] private bool isSelected;
    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}
