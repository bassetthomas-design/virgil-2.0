using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Virgil.Core.Applications;
using System.Threading;
using Virgil.Contracts.Applications;

namespace Virgil.App.ViewModels;

public sealed partial class ApplicationItemViewModel : ObservableObject
{
    public ApplicationItemViewModel(InstalledApplication application, Action<InstalledApplication> showDetails,
        Func<InstalledApplication, Task> openLocation, Action<InstalledApplication, bool>? selectionChanged = null, IApplicationIconLoader? iconLoader = null)
    {
        Application = application;
        CanSelect = application.CanUninstall && !application.IsSystemComponent && application.RiskLevel != ApplicationRiskLevel.Protected;
        _selectionChanged = selectionChanged;
        if (iconLoader is not null) _ = LoadIconAsync(iconLoader, application.ExtractedIconPath ?? application.IconPath);
        DisplayName = application.DisplayName;
        Lines = string.Join("\n", new[]
        {
            $"Éditeur : {ApplicationsViewModel.Empty(application.Publisher)}",
            $"Version : {ApplicationsViewModel.Empty(application.Version)}",
            $"Source : {string.Join(", ", application.Sources.Count == 0 ? [application.Source] : application.Sources)}",
            $"Risque : {ApplicationsViewModel.RiskLabel(application.RiskLevel)} - {application.RiskReason}",
            $"Taille estimée : {ApplicationsViewModel.FormatBytes(application.EstimatedSizeBytes)}"
        });
        DetailsCommand = new RelayCommand(() => showDetails(application));
        OpenLocationCommand = new AsyncRelayCommand(() => openLocation(application));
        IsLocationVisible = application.CanOpenLocation || application.UninstallKind == ApplicationUninstallKind.StoreSettings;
        LocationLabel = application.UninstallKind == ApplicationUninstallKind.StoreSettings ? "PARAMÈTRES" : "OUVRIR EMPLACEMENT";
    }

    private readonly Action<InstalledApplication, bool>? _selectionChanged;
    public InstalledApplication Application { get; }
    public bool CanSelect { get; }
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private byte[]? iconBytes;
    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !CanSelect) { IsSelected = false; return; }
        _selectionChanged?.Invoke(Application, value);
    }
    private async Task LoadIconAsync(IApplicationIconLoader loader, string? path)
    {
        try { IconBytes = await loader.LoadAsync(path, CancellationToken.None).ConfigureAwait(true); }
        catch { IconBytes = null; }
    }

    public string DisplayName { get; }
    public string Lines { get; }
    public IRelayCommand DetailsCommand { get; }
    public IAsyncRelayCommand OpenLocationCommand { get; }
    public bool IsLocationVisible { get; }
    public string LocationLabel { get; }
}
