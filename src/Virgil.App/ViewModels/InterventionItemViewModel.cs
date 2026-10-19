using CommunityToolkit.Mvvm.ComponentModel;
using Virgil.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Virgil.App.ViewModels;

public partial class InterventionItemViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    public InterventionItemViewModel(InterventionDiagnostic diagnostic, Action selectionChanged)
    {
        Diagnostic = diagnostic;
        _selectionChanged = selectionChanged;
        var lines = new List<string>
        {
            diagnostic.Definition.Description,
            $"Risque : {InterventionsViewModel.RiskLabel(diagnostic.Definition.RiskLevel)}",
            $"Admin : {(diagnostic.Definition.RequiresAdministrator ? "oui" : "non")}",
            $"Redemarrage possible : {(diagnostic.Definition.RebootPossible ? "oui" : "non")}",
            $"Duree : {diagnostic.Definition.EstimatedDuration}",
            $"Statut : {InterventionsViewModel.StatusLabel(diagnostic.Status)}",
            $"Diagnostic : {diagnostic.StateBefore}"
        };
        if (diagnostic.Errors.Count > 0)
            lines.Add("Erreur : " + string.Join(" | ", diagnostic.Errors.Take(2)));
        Lines = lines;
    }

    public InterventionDiagnostic Diagnostic { get; }
    public string Title => Diagnostic.Definition.Title;
    public bool IsAvailable => Diagnostic.IsAvailable;
    public IReadOnlyList<string> Lines { get; }
    [ObservableProperty] private bool isSelected;
    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}
