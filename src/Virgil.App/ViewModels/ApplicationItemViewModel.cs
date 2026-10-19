using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Virgil.Contracts.Applications;

namespace Virgil.App.ViewModels;

public sealed class ApplicationItemViewModel
{
    public ApplicationItemViewModel(InstalledApplication application, Action<InstalledApplication> showDetails,
        Func<InstalledApplication, Task> openLocation)
    {
        DisplayName = application.DisplayName;
        Lines = string.Join("\n", new[]
        {
            $"Editeur : {ApplicationsViewModel.Empty(application.Publisher)}",
            $"Version : {ApplicationsViewModel.Empty(application.Version)}",
            $"Source : {string.Join(", ", application.Sources.Count == 0 ? [application.Source] : application.Sources)}",
            $"Risque : {ApplicationsViewModel.RiskLabel(application.RiskLevel)} - {application.RiskReason}",
            $"Taille estimee : {ApplicationsViewModel.FormatBytes(application.EstimatedSizeBytes)}"
        });
        DetailsCommand = new RelayCommand(() => showDetails(application));
        OpenLocationCommand = new AsyncRelayCommand(() => openLocation(application));
        IsLocationVisible = application.CanOpenLocation || application.UninstallKind == ApplicationUninstallKind.StoreSettings;
        LocationLabel = application.UninstallKind == ApplicationUninstallKind.StoreSettings ? "PARAMETRES" : "OUVRIR EMPLACEMENT";
    }

    public string DisplayName { get; }
    public string Lines { get; }
    public IRelayCommand DetailsCommand { get; }
    public IAsyncRelayCommand OpenLocationCommand { get; }
    public bool IsLocationVisible { get; }
    public string LocationLabel { get; }
}
