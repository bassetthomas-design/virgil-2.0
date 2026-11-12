using System;
using System.ComponentModel;
using Virgil.Core.Updates;

namespace Virgil.App.ViewModels;

public sealed record UpdateTextCardViewModel(string Text) : INotifyPropertyChanged
{
    public UpdateSequenceStep? Step { get; init; }
    public string Key => Step is null ? string.Empty : Step.Component + ":" + Step.Id;
    public string Title => Step?.Title ?? Text;
    public string ButtonText => "Mettre à jour";
    public string MethodText => "Même méthode que « Tout mettre à jour ».";
    private string _status = "reste à installer";
    public string Status { get => _status; set { _status = value; Changed(); } }
    private string _detail = string.Empty;
    public string Detail { get => _detail; set { _detail = value; Changed(); } }
    private bool _busy;
    public bool Busy { get => _busy; set { _busy = value; Changed(); } }
    public bool HasUpdateAction => Step is not null && UpdateSequenceService.IsSingleTarget(Step)
        && Status != DriverCardSummary.Unsupported;
    public bool CanUpdate => HasUpdateAction && !Busy && Status is not ("mis à jour" or "à jour" or "à jour selon Steam");
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed()
    {
        foreach (var property in new[] { nameof(Status), nameof(Detail), nameof(Busy), nameof(HasUpdateAction), nameof(CanUpdate) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
