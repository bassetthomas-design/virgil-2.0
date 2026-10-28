using Virgil.Core.Actions;
using Virgil.Contracts.Actions;
using Virgil.App.Animation;
using Virgil.App.ViewModels;
using Virgil.Contracts;
using Virgil.Core.Cleanup;
using Virgil.Core.Processes;
using Virgil.Core.Reports;
using Xunit;

namespace Virgil.Tests;

public sealed class CleanupViewModelTests
{
    [Fact]
    public async Task PersonalSelectionRequiresIndividualChoiceAndTypedCriticalConsent()
    {
        var f = new Fixture();
        var preview = Preview();
        f.Previews.Items = new[] { preview with { Definition = preview.Definition with { IsPersonalFolder = true } } };
        await f.Analyze();
        var group = f.Vm.Groups.Single(g => g.Group == CleanupGroup.Sur);
        group.IsSelected = true;
        var row = Assert.Single(group.Rows);
        Assert.False(row.IsSelected);
        row.IsSelected = true;
        var pending = f.Run();
        Assert.True(f.Vm.RequiresCriticalConfirmation);
        Assert.Equal("Concernés : 1 élément(s) personnel(s) (UserTemporaryFiles).", f.Vm.ValidationNatureSummary);
        Assert.Contains("dossier personnel", f.Vm.ValidationTitle);
        Assert.Contains("Dossier personnel", f.Vm.ValidationWarning);
        Assert.NotEmpty(f.Vm.ExpectedCriticalConfirmation);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(new[] { "Cancel:UserTemporaryFiles" }, f.Actions.Order);
        f.Vm.CloseReportCommand.Execute(null);
        f.Actions.Order.Clear();
        pending = f.Run();
        f.Vm.CriticalConfirmationText = f.Vm.ExpectedCriticalConfirmation;
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(new[] { "Execute:UserTemporaryFiles" }, f.Actions.Order);
        Assert.Equal(2, f.Confirmation.Calls);
    }

    [Fact]
    public async Task RefusedIssuanceUsesCancelAllPath()
    {
        var f = new Fixture(); f.Confirmation.Refuse = true;
        await f.Analyze(); var pending = f.Run();
        f.Vm.ExecuteDecisionCommand.Execute(null); await pending;
        Assert.Equal(1, f.Confirmation.Calls);
        Assert.Equal(new[] { "Cancel:UserTemporaryFiles" }, f.Actions.Order);
        Assert.Equal("Session annulée par l'utilisateur.", Assert.Single(f.Actions.Report!.Errors));
    }

    [Fact]
    public void InitialStateMatchesViewAndCommandsHaveNoSideEffects()
    {
        var f = new Fixture();
        var vm = f.Vm;
        Assert.Equal("Aucune analyse nettoyage lancée.", vm.StatusText);
        Assert.Equal("Lancez une analyse des zones pour afficher les fichiers temporaires eligibles. Aucune suppression ne demarre depuis le scan.", Assert.IsType<CleanupTextCardItem>(Assert.Single(vm.Entries)).Text);
        AssertIdle(vm);
        Assert.False(vm.IsLaunchCleanupEnabled);
        Assert.False(vm.IsAdvancedCleanupEnabled);
        Assert.False(vm.IsReviewManualEnabled);
        Assert.False(vm.IsReportButtonEnabled);
        Assert.False(vm.IsValidationOpen);
        Assert.False(vm.IsReportOpen);
        Assert.Equal("VALIDATION REQUISE", vm.ValidationTitle);
        Assert.Equal("EXECUTER", vm.ExecuteZoneLabel);
        Assert.Empty(vm.ValidationReason + vm.ValidationRoot + vm.ValidationMetrics + vm.ValidationEffect + vm.ValidationNotTouched + vm.ValidationWarning + vm.ReportText);
        Assert.Equal(VirgilCoreState.Idle, vm.MiniCoreState);
        Assert.Equal(VirgilCoreState.Idle, vm.ValidationCoreState);
        Assert.Equal(VirgilCoreState.Idle, vm.ReportCoreState);
        Assert.False(vm.TryCloseOverlay());
        vm.ExecuteDecisionCommand.Execute(null);
        vm.SkipDecisionCommand.Execute(null);
        vm.CancelDecisionCommand.Execute(null);
        vm.CancelActiveOperation();
        Assert.Empty(f.Actions.Order);
        vm.ReviewManualCommand.Execute(null);
        Assert.Equal("Aucun element personnel volumineux détecté dans les emplacements analyses.", f.Messages.Last());
        vm.ViewReportCommand.Execute(null);
        Assert.Equal("Aucun rapport nettoyage disponible.", f.Messages.Last());
        var home = 0;
        vm.ReturnHomeRequested += (_, _) => home++;
        vm.ReturnHomeCommand.Execute(null);
        Assert.Equal(1, home);
    }

    [Fact]
    public async Task AnalyzeKeepsEntriesOrderAndCardContent()
    {
        var f = new Fixture();
        var safeLater = Preview(CleanupZoneId.DirectXShaderCache, order: 4);
        var safeFirst = Preview(order: 1) with { Errors = new[] { "un", "deux", "trois", "quatre" } };
        var advanced = Preview(CleanupZoneId.WindowsOld, CleanupClassification.AdvancedCleanable, 8);
        var info = Preview(CleanupZoneId.PrefetchInformation, CleanupClassification.InformationOnly, 0, executable: false);
        f.Previews.Items = new[] { safeLater, info, advanced, safeFirst, Preview(CleanupZoneId.TechnicalLogs, CleanupClassification.Protected) };
        await f.Analyze();
        AssertIdle(f.Vm);
        Assert.True(f.Vm.IsLaunchCleanupEnabled);
        Assert.True(f.Vm.IsAdvancedCleanupEnabled);
        Assert.False(f.Vm.IsReviewManualEnabled);
        Assert.Equal("Prévisualisation disponible. Validation par zone requise.", f.Vm.StatusText);
        Assert.Equal(VirgilCoreState.Success, f.Vm.MiniCoreState);
        Assert.Contains("5 zones analysées.", f.Messages.Last());
        Assert.Equal(9, f.Vm.Entries.Count);
        Assert.Equal("NETTOYAGE SUR", Assert.IsType<CleanupSectionTitleItem>(f.Vm.Entries[0]).Text);
        var card = Assert.IsType<CleanupZoneItemViewModel>(f.Vm.Entries[1]);
        AssertSnapshot(safeFirst, card.Preview);
        Assert.Equal("USERTEMPORARYFILES", card.Title);
        Assert.Equal(new[] { "Description", "Racine : %TEMP% utilisateur", "Risque : faible", "Categorie : nettoyable sur", "Action : guidee apres validation", "Examines : 3 fichiers", "Eligibles : 2 fichiers, 2 Ko", "Exclus : 1 fichiers", "Anciennete minimale : 2 jours", "Avertissement : Attention", "Erreurs lisibles : un | deux | trois" }, card.Lines);
        AssertSnapshot(safeLater, Assert.IsType<CleanupZoneItemViewModel>(f.Vm.Entries[2]).Preview);
        Assert.Equal("NETTOYAGE AVANCE", Assert.IsType<CleanupSectionTitleItem>(f.Vm.Entries[3]).Text);
        AssertSnapshot(advanced, Assert.IsType<CleanupZoneItemViewModel>(f.Vm.Entries[4]).Preview);
        AssertSnapshot(info, Assert.IsType<CleanupZoneItemViewModel>(f.Vm.Entries[5]).Preview);
        Assert.Contains("Action : information seulement", Assert.IsType<CleanupZoneItemViewModel>(f.Vm.Entries[5]).Lines);
        Assert.Equal(new[]
        {
            "NETTOYAGE TECHNIQUE BLOQUE\nLes acces refuses sont signales. Toute réparation exige un chemin technique allowliste, une confirmation critique et le helper eleve. Aucune commande libre takeown.",
            "À REVOIR MANUELLEMENT\nLes gros fichiers, dossiers, telechargements, archives, ISO, videos, photos, projets et sauvegardes restent en lecture seule. Aucun bouton supprimer.",
            "PROTÉGÉ\nPhotos, videos, documents, projets, sauvegardes, jeux, applications, cloud, réseau, lecteurs externes et profils ambigus ne sont jamais nettoyes automatiquement."
        }, f.Vm.Entries.OfType<CleanupTextCardItem>().Select(c => c.Text));
        Assert.Empty(f.Actions.Order);
    }

    [Fact]
    public async Task EmptyAnalysisRetainsSectionsAndThreeFixedCards()
    {
        var f = new Fixture();
        f.Previews.Items = Array.Empty<CleanupZonePreview>();
        await f.Analyze();
        Assert.Equal(5, f.Vm.Entries.Count);
        Assert.Equal(2, f.Vm.Entries.OfType<CleanupSectionTitleItem>().Count());
        Assert.Equal(3, f.Vm.Entries.OfType<CleanupTextCardItem>().Count());
        Assert.Equal("Analyse terminée.\nAucun nettoyage nécessaire.", f.Messages.Last());
        await f.Run();
        Assert.Equal("Aucune zone eligible.\nRelancez une analyse.", f.Messages.Last());
        Assert.False(f.Vm.IsLaunchCleanupEnabled);
        Assert.False(f.Vm.IsAdvancedCleanupEnabled);
        Assert.Empty(f.Actions.Order);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnalysisCancellationOrErrorRestoresButtonsAndTexts(bool error)
    {
        var f = new Fixture();
        var gate = new TaskCompletionSource<IReadOnlyList<CleanupZonePreview>>();
        f.Previews.Handler = token => gate.Task.WaitAsync(token);
        var pending = f.Analyze();
        AssertBusy(f.Vm);
        Assert.Equal("Analyse en cours.", f.Vm.StatusText);
        Assert.Equal(VirgilCoreState.Scanning, f.Vm.MiniCoreState);
        if (error) gate.SetException(new InvalidOperationException());
        else Assert.True(f.Vm.TryCloseOverlay());
        await pending;
        AssertIdle(f.Vm);
        Assert.Equal(error ? "Analyse nettoyage indisponible." : "Analyse annulée.", f.Vm.StatusText);
        Assert.Equal(error ? VirgilCoreState.Error : VirgilCoreState.Idle, f.Vm.MiniCoreState);
        Assert.Contains(error ? "Analyse nettoyage indisponible.\nAucune action effectuée." : "Nettoyage interrompu.\nLes zones restantes n'ont pas été modifiees.", f.Messages);
        if (!error) Assert.Contains("Annulation demandee.\nOpération en cours d'arret.", f.Messages);
        Assert.False(f.Vm.IsLaunchCleanupEnabled);
        Assert.False(f.Vm.IsAdvancedCleanupEnabled);
        Assert.False(f.Vm.IsReviewManualEnabled);
        Assert.Empty(f.Actions.Order);
        Assert.Equal(0, f.History.Calls);
    }

    [Theory]
    [InlineData("SkipDecision")]
    [InlineData("CancelDecision")]
    [InlineData("TryCloseOverlay")]
    [InlineData("CancelActiveOperation")]
    public async Task AbandoningValidationNeverExecutesAndAlwaysPersists(string decision)
    {
        var f = new Fixture();
        await f.Analyze();
        var pending = f.Run();
        Assert.True(f.Vm.IsValidationOpen);
        AssertBusy(f.Vm);
        Assert.Empty(f.Actions.Order);
        Assert.Equal(VirgilCoreState.SensitiveAction, f.Vm.ValidationCoreState);
        Decide(f.Vm, decision);
        await pending;
        var skip = decision is "SkipDecision" or "TryCloseOverlay";
        Assert.Equal(new[] { (skip ? "Skip:" : "Cancel:") + CleanupZoneId.UserTemporaryFiles }, f.Actions.Order);
        Assert.Equal(skip ? CleanupStepStatus.Skipped : CleanupStepStatus.Cancelled, Assert.Single(f.Actions.Report!.Results).Status);
        Assert.Equal(1, f.History.Calls);
        Assert.Equal(1, f.Persisted);
        Assert.False(f.History.Token.CanBeCanceled);
        Assert.Equal(ReportKind.Cleanup, f.History.Saved!.Kind);
        AssertIdle(f.Vm);
        Assert.True(f.Vm.IsReportButtonEnabled);
        Assert.False(f.Vm.IsValidationOpen);
        Assert.Equal(VirgilCoreState.Idle, f.Vm.ValidationCoreState);
        Assert.Equal(skip ? VirgilCoreState.Success : VirgilCoreState.Idle, f.Vm.MiniCoreState);
        Assert.Equal(skip ? VirgilCoreState.Success : VirgilCoreState.Warning, f.Vm.ReportCoreState);
        Assert.True(f.Vm.IsReportOpen);
        Assert.Contains(skip ? "Zones passees : 1" : "Annulation : oui", f.Vm.ReportText);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        Assert.DoesNotContain(f.Actions.Order, action => action.StartsWith("Exécute:"));
    }

    [Fact]
    public async Task SelectionKeepsPreviewOrderAndRequiresOneDecision()
    {
        var f = new Fixture();
        // Une seule décision lie toute la sélection ; les zones décochées n'entrent pas dans l'aperçu.
        f.Previews.Items = new[] { Preview(CleanupZoneId.DirectXShaderCache, order: 9), Preview(order: 1), Preview(CleanupZoneId.UserCrashDumps, order: 4), Preview(CleanupZoneId.TechnicalLogs) };
        await f.Analyze();
        var rows = f.Vm.Groups.SelectMany(g => g.Rows).ToArray();
        rows[0].IsSelected = false;
        rows[2].IsSelected = false;
        var pending = f.Run();
        Assert.Contains("UserTemporaryFiles", f.Vm.ValidationReason);
        Assert.Contains("TechnicalLogs", f.Vm.ValidationReason);
        Assert.DoesNotContain("DirectXShaderCache", f.Vm.ValidationReason);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(1, f.Confirmation.Calls);
        Assert.Equal(new[] { "Execute:UserTemporaryFiles", "Execute:TechnicalLogs" }, f.Actions.Order);
        Assert.All(f.Actions.Report!.Results, r => Assert.Equal(CleanupStepStatus.Completed, r.Status));
        Assert.Empty(f.Actions.Report.Errors);
        Assert.Contains("Volume estime : 4 Ko", f.Vm.ReportText);
        Assert.Contains("Zones executees : 2", f.Vm.ReportText);
        Assert.Contains("Zones analysees : 4", f.Vm.ReportText);
        Assert.Contains("Fichiers mis en quarantaine : 2", f.Vm.ReportText);
        Assert.Equal(1, f.Persisted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyEligibleExecutableZonesOfRequestedClassificationAreProposed(bool advanced)
    {
        var f = new Fixture();
        var classification = advanced ? CleanupClassification.AdvancedCleanable : CleanupClassification.Cleanable;
        f.Previews.Items = new[]
        {
            Preview(), Preview(CleanupZoneId.BrowserChromeCache, CleanupClassification.AdvancedCleanable),
            Preview(CleanupZoneId.TechnicalLogs, classification, executable: false),
            Preview(CleanupZoneId.UserCrashDumps, classification, eligible: 0),
            Preview(CleanupZoneId.PrefetchInformation, CleanupClassification.InformationOnly),
            Preview(CleanupZoneId.RecycleBin, CleanupClassification.Protected),
            Preview(CleanupZoneId.InternetCache, CleanupClassification.ReviewOnly)
        };
        await f.Analyze();
        foreach (var group in f.Vm.Groups) group.IsSelected = false;
        f.Vm.Groups.Single(g => g.Group == (advanced ? CleanupGroup.AVerifier : CleanupGroup.Sur)).IsSelected = true;
        var pending = f.Run(advanced);
        Assert.Contains(advanced ? "BrowserChromeCache" : "UserTemporaryFiles", f.Vm.ValidationReason);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(new[] { advanced ? "Execute:BrowserChromeCache" : "Execute:UserTemporaryFiles" }, f.Actions.Order);
        Assert.Contains("Volume estime : 2 Ko", f.Vm.ReportText);
    }

    [Theory]
    [InlineData(CleanupZoneId.UserTemporaryFiles, false)]
    [InlineData(CleanupZoneId.BrowserChromeCache, true)]
    [InlineData(CleanupZoneId.RecycleBin, true)]
    public async Task ValidationPreservesTitleLabelMetricsAndSafety(CleanupZoneId id, bool reinforced)
    {
        var f = new Fixture();
        var p = Preview(id) with { Definition = Preview(id).Definition with { RequiresReinforcedConfirmation = reinforced } };
        f.Previews.Items = new[] { p };
        await f.Analyze();
        if (id == CleanupZoneId.RecycleBin) Assert.Single(f.Vm.Groups.SelectMany(g => g.Rows)).IsSelected = true;
        var pending = f.Run();
        Assert.Equal("Nettoyage — confirmation récapitulative", f.Vm.ValidationTitle);
        Assert.Equal("CONFIRMER LA SÉLECTION", f.Vm.ExecuteZoneLabel);
        Assert.Empty(f.Vm.ValidationRoot);
        Assert.Contains(id.ToString(), f.Vm.ValidationReason);
        Assert.Contains("Taille totale : 2 Ko", f.Vm.ValidationMetrics);
        Assert.Contains("Nombre de fichiers : 2", f.Vm.ValidationMetrics);
        Assert.Contains("Éléments sensibles : " + (reinforced ? "2" : "0"), f.Vm.ValidationMetrics);
        Assert.Equal(id == CleanupZoneId.RecycleBin ? "Effet"
            : "Conservé 7 jours en quarantaine, puis effacement définitif proposé avec confirmation. Restauration vérifiée possible.", f.Vm.ValidationEffect);
        Assert.Contains("Clés de registre en lecture seule", f.Vm.ValidationNotTouched);
        Assert.Contains(id == CleanupZoneId.RecycleBin ? "irréversible" : "quarantaine uniquement", f.Vm.ValidationWarning);
        Assert.StartsWith("Validation requise.", f.Messages.Last());
        f.Vm.CancelDecisionCommand.Execute(null);
        await pending;
        Assert.DoesNotContain(f.Actions.Order, action => action.StartsWith("Exécute:"));
    }

    [Theory]
    [InlineData("SkipDecision")]
    [InlineData("CancelDecision")]
    public async Task DecisionCanBeCompletedSynchronouslyFromValidationAnnouncement(string decision)
    {
        var f = new Fixture();
        await f.Analyze();
        f.Vm.VirgilMessageRequested += message =>
        {
            if (message.StartsWith("Validation requise.")) Decide(f.Vm, decision);
        };
        await f.Run().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(f.Vm.IsValidationOpen);
        Assert.DoesNotContain(f.Actions.Order, action => action.StartsWith("Exécute:"));
        Assert.Equal(1, f.Persisted);
    }

    [Fact]
    public async Task CancellationDuringExecutionCancelsTokenAndPersistsPartialReport()
    {
        var f = new Fixture();
        var gate = new TaskCompletionSource<CleanupStepResult>();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Actions.Handler = (_, token) =>
        {
            started.TrySetResult(true);
            return gate.Task.WaitAsync(token);
        };
        await f.Analyze();
        var pending = f.Run();
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(f.Vm.IsValidationOpen);
        AssertBusy(f.Vm);
        Assert.True(f.Vm.TryCloseOverlay());
        Assert.True(f.Actions.Token.IsCancellationRequested);
        await pending;
        Assert.Contains("Annulation demandee.\nOpération en cours d'arret.", f.Messages);
        Assert.Contains("Session annulée.", f.Actions.Report!.Errors);
        Assert.Equal(VirgilCoreState.Idle, f.Vm.MiniCoreState);
        Assert.Equal(VirgilCoreState.Warning, f.Vm.ReportCoreState);
        Assert.True(f.Vm.IsReportOpen);
        Assert.Equal(1, f.Persisted);
        AssertIdle(f.Vm);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "Historique inaccessible.")]
    [InlineData(true, "Avertissement historique.")]
    public async Task PersistenceResultPreservesErrorMessageAndEvent(bool success, string? error)
    {
        var f = new Fixture();
        f.History.Result = new() { Success = success, ReadableError = error };
        await f.Analyze();
        var pending = f.Run();
        f.Vm.SkipDecisionCommand.Execute(null);
        await pending;
        Assert.Contains(error ?? "Historique local indisponible. Rapport conservé en mémoire.", f.Messages);
        Assert.Equal(success ? 1 : 0, f.Persisted);
        Assert.True(f.Vm.IsReportOpen);
        Assert.True(f.Vm.IsReportButtonEnabled);
    }

    [Fact]
    public async Task ComputedReportIncludesAdvancedRefusalsLocksReparseAndDistinctRefusedPaths()
    {
        var f = new Fixture();
        f.Previews.Items = new[]
        {
            Preview(CleanupZoneId.WindowsOld, CleanupClassification.AdvancedCleanable) with { Errors = new[] { "Point de reanalyse ignore", "Chemin refuse A" } },
            Preview(CleanupZoneId.RecycleBin, CleanupClassification.AdvancedCleanable) with { Errors = new[] { "POINT DE REANALYSE ignore", "CHEMIN REFUSE A", "Chemin refuse B" } }
        };
        f.Actions.Errors = new[] { "Fichier verrouille" };
        f.Actions.ErrorFiles = 3;
        await f.Analyze();
        f.Vm.Groups.SelectMany(g => g.Rows).Single(r => r.Preview.Definition.Id == CleanupZoneId.RecycleBin).IsSelected = true;
        var pending = f.Run(true);
        Assert.DoesNotContain("WindowsOld", f.Vm.ValidationReason);
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        var saved = f.History.Saved!;
        Assert.Contains("Zones avancees refusees : 0", saved.SimpleView);
        Assert.Contains("Espace estime : 2 Ko", saved.SimpleView);
        Assert.Contains("Fichiers ignores ou verrouilles : 3", saved.SimpleView);
        Assert.Contains("Elements a revoir : 0", saved.SimpleView);
        Assert.Contains("Elements proteges : 0", saved.SimpleView);
        Assert.Contains("Points de reanalyse ignores : 2", saved.TechnicalDetails);
        Assert.Contains("Chemins refuses : Chemin refuse A | Chemin refuse B", saved.TechnicalDetails);
        Assert.Contains("Erreurs lisibles : Point de reanalyse ignore | Chemin refuse A", Assert.IsType<CleanupZoneItemViewModel>(f.Vm.Entries[2]).Lines);
        Assert.Equal(VirgilCoreState.Warning, f.Vm.MiniCoreState);
        Assert.Equal(VirgilCoreState.Warning, f.Vm.ReportCoreState);
        Assert.Contains("Fichiers verrouilles : 3", f.Vm.ReportText);
        Assert.Contains("  Erreur : Fichier verrouille", f.Vm.ReportText);
    }

    [Fact]
    public async Task ExecutionErrorStillBuildsAndPersistsReport()
    {
        var f = new Fixture();
        f.Actions.Handler = (_, _) => throw new InvalidOperationException();
        await f.Analyze();
        var pending = f.Run();
        f.Vm.ExecuteDecisionCommand.Execute(null);
        await pending;
        Assert.Equal(new[] { "Erreur pendant le nettoyage guide." }, f.Actions.Report!.Errors);
        Assert.Equal(VirgilCoreState.Error, f.Vm.MiniCoreState);
        Assert.Equal(VirgilCoreState.Warning, f.Vm.ReportCoreState);
        Assert.Contains("Nettoyage interrompu.\nRapport partiel disponible.", f.Messages);
        Assert.Contains("Erreurs session :", f.Vm.ReportText);
        Assert.Equal(1, f.Persisted);
        AssertIdle(f.Vm);
    }

    [Fact]
    public async Task ReportCloseHasPriorityOverPendingValidation()
    {
        var f = new Fixture();
        await f.Analyze();
        var pending = f.Run();
        f.Vm.SkipDecisionCommand.Execute(null);
        await pending;
        Assert.True(f.Vm.TryCloseOverlay());
        Assert.Equal(VirgilCoreState.Idle, f.Vm.ReportCoreState);
        Assert.False(f.Vm.TryCloseOverlay());
        pending = f.Run();
        f.Vm.ViewReportCommand.Execute(null);
        Assert.True(f.Vm.TryCloseOverlay());
        Assert.True(f.Vm.IsValidationOpen);
        Assert.False(pending.IsCompleted);
        Assert.True(f.Vm.TryCloseOverlay());
        await pending;
        f.Vm.CloseReportCommand.Execute(null);
        Assert.False(f.Vm.IsReportOpen);
        Assert.Equal(VirgilCoreState.Idle, f.Vm.ReportCoreState);
        Assert.DoesNotContain(f.Actions.Order, action => action.StartsWith("Exécute:"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ReviewCommandsKeepThreeLinesExplorerArgumentsAndNoCleanupEffects(int failure)
    {
        var runner = new FakeRunner { Failure = failure };
        var messages = new List<string>();
        var item = new CleanupStorageReviewItem("C:\\Donnees\\Mon fichier.iso", "Mon fichier.iso", "ISO", 2048, new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), CleanupClassification.ReviewOnly, "A verifier");
        var card = new CleanupReviewItemViewModel(item, runner, messages.Add);
        Assert.Equal("MON FICHIER.ISO", card.Title);
        Assert.Equal(new[] { "ISO - 2 Ko - 2026-10-19", "a revoir manuellement - A verifier", "Emplacement : C:\\Donnees\\Mon fichier.iso" }, card.Lines);
        Assert.False(card.IsHidden);
        card.IgnoreCommand.Execute(null);
        Assert.True(card.IsHidden);
        Assert.Equal(0, runner.Calls);
        Assert.Empty(messages);
        card.MarkCommand.Execute(null);
        Assert.Equal("Element marque a revoir : Mon fichier.iso.\nAucune suppression effectuee.", messages.Last());
        Assert.Equal(0, runner.Calls);
        await card.OpenLocationCommand.ExecuteAsync(null);
        var request = runner.Request!;
        Assert.Equal("explorer.exe", request.FileName);
        Assert.Equal("/select,\"C:\\Donnees\\Mon fichier.iso\"", request.RawArguments);
        Assert.Empty(request.Arguments);
        Assert.True(request.UseShellExecute);
        Assert.False(request.WaitForExit);
        Assert.False(runner.Token.CanBeCanceled);
        Assert.Equal(1, runner.Calls);
        if (failure != 0) Assert.Equal("Emplacement indisponible. Aucune modification effectuée.", messages.Last());
        else Assert.Single(messages);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task StorageReviewCapsCardsAtTwentyPreservesOrderAndCountsAllItemsInReport()
    {
        var directory = Directory.CreateTempSubdirectory("VirgilCleanupVm-");
        try
        {
            for (var i = 1; i <= 22; i++) File.WriteAllBytes(Path.Combine(directory.FullName, $"archive-{i:00}.bin"), new byte[i]);
            File.WriteAllBytes(Path.Combine(directory.FullName, "photo.jpg"), new byte[23]);
            var f = new Fixture(new CleanupStorageAnalyzer(new[] { directory.FullName }, largeFileThreshold: 1));
            await f.Analyze();
            var cards = f.Vm.Entries.OfType<CleanupReviewItemViewModel>().ToList();
            Assert.Equal(20, cards.Count);
            Assert.Equal("PHOTO.JPG", cards[0].Title);
            Assert.Equal("ARCHIVE-22.BIN", cards[1].Title);
            Assert.Equal("ARCHIVE-04.BIN", cards[19].Title);
            Assert.True(f.Vm.IsReviewManualEnabled);
            f.Vm.ReviewManualCommand.Execute(null);
            Assert.Equal("Elements volumineux detectes : 23.\nIls semblent personnels ou ambigus. Virgil ne les supprimera pas automatiquement.", f.Messages.Last());
            cards[0].IgnoreCommand.Execute(null);
            Assert.True(cards[0].IsHidden);
            Assert.Empty(f.Actions.Order);
            Assert.Equal(0, f.Runner.Calls);
            var pending = f.Run();
            f.Vm.SkipDecisionCommand.Execute(null);
            await pending;
            Assert.Contains("Elements a revoir : 22 (253 o)", f.History.Saved!.SimpleView);
            Assert.Contains("Elements proteges : 1", f.History.Saved.SimpleView);
            Assert.Contains("Elements a revoir : 22 - 253 o", f.Vm.ReportText);
            Assert.False(f.Vm.Entries.OfType<CleanupReviewItemViewModel>().First().IsHidden);
            Assert.Equal(23, Directory.GetFiles(directory.FullName).Length);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void AssertSnapshot(CleanupZonePreview expected, CleanupZonePreview actual)
    {
        Assert.NotSame(expected, actual);
        Assert.NotSame(expected.Definition, actual.Definition);
        // Toutes les propriétés, y compris les collections ordonnées et les racines enfants,
        // restent identiques après la copie profonde, indépendamment de leur implémentation.
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected.Definition),
            System.Text.Json.JsonSerializer.Serialize(actual.Definition));
        Assert.Equal(expected.Definition.AllowedExtensions, actual.Definition.AllowedExtensions);
        Assert.Equal(expected.Definition.RequiredPathFragments, actual.Definition.RequiredPathFragments);
        Assert.Equal(expected.Definition.ExcludedPathFragments, actual.Definition.ExcludedPathFragments);
        Assert.Equal(expected with { Definition = actual.Definition, Candidates = actual.Candidates, Errors = actual.Errors }, actual);
        Assert.Equal(expected.Candidates, actual.Candidates);
        Assert.Equal(expected.Errors, actual.Errors);
    }

    private static async Task NextValidation(CleanupViewModel vm, Action decide, string name)
    {
        var next = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(vm.IsValidationOpen) && vm.IsValidationOpen && vm.ValidationReason.StartsWith($"Zone : {name}\n")) next.TrySetResult(true);
        }
        vm.PropertyChanged += Changed;
        try
        {
            decide();
            await next.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { vm.PropertyChanged -= Changed; }
    }

    private static void AssertIdle(CleanupViewModel vm)
    {
        Assert.True(vm.IsIdle);
        Assert.False(vm.IsBusy);
        Assert.True(vm.IsAnalyzeEnabled);
        Assert.True(vm.IsReturnHomeEnabled);
    }

    private static void AssertBusy(CleanupViewModel vm)
    {
        Assert.True(vm.IsBusy);
        Assert.False(vm.IsIdle);
        Assert.False(vm.IsAnalyzeEnabled);
        Assert.False(vm.IsLaunchCleanupEnabled);
        Assert.False(vm.IsAdvancedCleanupEnabled);
        Assert.False(vm.IsReviewManualEnabled);
        Assert.False(vm.IsReportButtonEnabled);
        Assert.True(vm.IsReturnHomeEnabled);
    }

    private static void Decide(CleanupViewModel vm, string decision)
    {
        switch (decision)
        {
            case "SkipDecision": vm.SkipDecisionCommand.Execute(null); break;
            case "CancelDecision": vm.CancelDecisionCommand.Execute(null); break;
            case "TryCloseOverlay": Assert.True(vm.TryCloseOverlay()); break;
            case "CancelActiveOperation": vm.CancelActiveOperation(); break;
            default: throw new ArgumentOutOfRangeException(nameof(decision));
        }
    }

    [Fact]
    public async Task Dev_cache_rows_never_appear_under_restes_category()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Path.GetFullPath("cache-vm-fixture");
        var candidates = new[] { "npm-cache", "NuGet", "pnpm-cache", "pnpm", "OldTool" }.Select(name =>
        {
            var path = Path.Combine(root, name);
            return new SoftwareFootprintCandidate(name, path, root,
                [new(Path.Combine(path, "cache.tmp"), 100, now)], [@"HKCU\SOFTWARE\" + name], now);
        }).ToArray();
        var f = new Fixture();
        f.Previews.Items = new LeftoverAnalyzer().Analyze(new([], [], candidates), now);
        await f.Analyze();
        var category = Assert.Single(f.Vm.Categories);
        Assert.Equal("Restes de logiciels désinstallés", category.Title);
        Assert.Equal("OldTool", Assert.Single(category.Rows).Preview.Definition.SoftwareName);
        Assert.DoesNotContain(category.Rows, r => DevelopmentCacheCatalog.Contains(r.Preview.Definition.SoftwareName ?? ""));
    }

    internal static async Task<CleanupViewModel> ContrastFixture()
    {
        var f = new Fixture();
        var now = DateTimeOffset.UtcNow;
        var leftovers = Enumerable.Range(1, 20).Select(i => Preview(CleanupZoneId.SoftwareLeftovers) with
        {
            Definition = Preview(CleanupZoneId.SoftwareLeftovers).Definition with
            { SoftwareName = $"Ancien outil {i}", DisplayName = $"Ancien outil {i}", Classification = CleanupClassification.AdvancedCleanable },
            EligibleBytes = (long)(2.1 * 1024 * 1024 * 1024 / 20)
        });
        f.Previews.Items = leftovers.Concat(new[] { Preview(CleanupZoneId.OldDownloads),
            Preview(CleanupZoneId.BrowserChromeCache), Preview() }).ToArray();
        await f.Analyze();
        return f.Vm;
    }

    private static CleanupZonePreview Preview(CleanupZoneId id = CleanupZoneId.UserTemporaryFiles,
        CleanupClassification classification = CleanupClassification.Cleanable, int order = 0, bool executable = true, int eligible = 2) =>
        new(new CleanupZoneDefinition(id, id.ToString(), "Description", "Zone test", TimeSpan.FromDays(2), CleanupRiskLevel.Low, "Attention", "Effet", "Non touche", order)
        { Classification = classification, IsExecutable = executable }, DateTimeOffset.Now, 3, eligible, 2048, 1, Array.Empty<CleanupCandidate>(), Array.Empty<string>());

    internal sealed class Fixture
    {
        public FakePreview Previews { get; } = new();
        public RecordingConfirmation Confirmation { get; } = new();
        public FakeActions Actions { get; } = new();
        public FakeHistory History { get; } = new();
        public FakeRunner Runner { get; } = new();
        public CleanupViewModel Vm { get; }
        public List<string> Messages { get; } = new();
        public int Persisted { get; private set; }
        public Fixture(CleanupStorageAnalyzer? analyzer = null, IQuarantineStore? quarantine = null)
        {
            Actions.Authority = Confirmation.Authority;
            var module = new CleanupMaintenanceModule(new CleanupAnalyzer(Previews,
                analyzer ?? new CleanupStorageAnalyzer(Array.Empty<string>())), Actions);
            Vm = new(module, Runner, History, Confirmation, quarantine is null ? null : new QuarantineService(quarantine, Confirmation.Authority, TimeProvider.System));
            Vm.VirgilMessageRequested += Messages.Add;
            Vm.ReportPersisted += (_, _) => Persisted++;
        }
        public Task Analyze() => Vm.AnalyzeZonesCommand.ExecuteAsync(null);
        public Task Run(bool advanced = false) => advanced ? Vm.LaunchCleanupAdvancedCommand.ExecuteAsync(null) : Vm.LaunchCleanupSafeCommand.ExecuteAsync(null);
    }

    internal sealed class FakePreview : ICleanupPreviewService
    {
        public IReadOnlyList<CleanupZonePreview> Items { get; set; } = new[] { Preview() };
        public Func<CancellationToken, Task<IReadOnlyList<CleanupZonePreview>>>? Handler { get; set; }
        public IReadOnlyList<CleanupZoneDefinition> GetZones() => Items.Select(p => p.Definition).ToList();
        public Task<IReadOnlyList<CleanupZonePreview>> PreviewAsync(IProgress<CleanupProgress>? progress, CancellationToken cancellationToken) =>
            Handler?.Invoke(cancellationToken) ?? Task.FromResult(Items);
    }

    internal sealed class FakeActions : ICleanupExecutionService
    {
        public ConfirmationService Authority { get; set; } = null!;
        public List<string> Order { get; } = new();
        public CleanupSessionReport? Report { get; private set; }
        public Func<CleanupZonePreview, CancellationToken, Task<CleanupStepResult>>? Handler { get; set; }
        public CancellationToken Token { get; private set; }
        public int ErrorFiles { get; set; }
        public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
        public Task<CleanupStepResult> ExecuteZoneAsync(CleanupZonePreview preview, ConfirmedPlan? plan, IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
        {
            Assert.NotNull(plan);
            Assert.True(Authority.TryConsume(plan, CleanupActionPreviewFactory.Create(preview)).IsAccepted);
            Order.Add("Exécute:" + preview.Definition.Id);
            Token = cancellationToken;
            var recycle = preview.Definition.Id == CleanupZoneId.RecycleBin;
            return Handler?.Invoke(preview, cancellationToken) ?? Task.FromResult(new CleanupStepResult(preview.Definition, CleanupStepStatus.Completed,
                recycle ? 1 : 0, recycle ? 1024 : 0, 0, ErrorFiles, TimeSpan.FromSeconds(1), Errors)
                { QuarantinedFiles = recycle ? 0 : 1, QuarantinedBytes = recycle ? 0 : 1024 });
        }
        public async Task<CleanupSelectionResult> ExecuteSelectionAsync(IReadOnlyList<CleanupZonePreview> selection, ConfirmedPlan? plan,
            IProgress<CleanupProgress>? progress, CancellationToken cancellationToken)
        {
            Assert.NotNull(plan);
            Assert.True(Authority.TryConsume(plan, CleanupSelectionAction.CreatePreview(selection)).IsAccepted);
            var steps = new List<CleanupStepResult>();
            foreach (var preview in selection)
            {
                Order.Add("Exécute:" + preview.Definition.Id);
                Token = cancellationToken;
                var recycle = preview.Definition.Id == CleanupZoneId.RecycleBin;
                steps.Add(Handler is not null ? await Handler(preview, cancellationToken) : new CleanupStepResult(preview.Definition, CleanupStepStatus.Completed,
                    recycle ? 1 : 0, recycle ? 1024 : 0, 0, ErrorFiles, TimeSpan.FromSeconds(1), Errors)
                    { QuarantinedFiles = recycle ? 0 : 1, QuarantinedBytes = recycle ? 0 : 1024 });
            }
            return new(steps, null);
        }
        public CleanupStepResult SkipZone(CleanupZonePreview preview)
        {
            Order.Add("Skip:" + preview.Definition.Id);
            return Result(preview, CleanupStepStatus.Skipped);
        }
        public CleanupStepResult CancelZone(CleanupZonePreview preview)
        {
            Order.Add("Cancel:" + preview.Definition.Id);
            return Result(preview, CleanupStepStatus.Cancelled);
        }
        private static CleanupStepResult Result(CleanupZonePreview p, CleanupStepStatus status) => new(p.Definition, status, 0, 0, 0, 0, TimeSpan.Zero, Array.Empty<string>());
        public CleanupSessionReport CreateReport(DateTimeOffset startedAt, IReadOnlyList<CleanupStepResult> results, IReadOnlyList<string> errors)
        {
            Report = new(startedAt, startedAt.AddSeconds(2), TimeSpan.FromSeconds(2), results.ToArray(), errors.ToArray());
            return Report;
        }
    }

    internal sealed class FakeRunner : IProcessRunner
    {
        public int Calls { get; private set; }
        public int Failure { get; init; }
        public ProcessLaunchRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Aucun processus de nettoyage attendu.");
        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Token = cancellationToken;
            if (Failure == 2) throw new InvalidOperationException();
            return Task.FromResult(new ProcessLaunchResult(Failure == 0, null, Failure == 1 ? "Erreur" : null));
        }
    }

    internal sealed class FakeHistory : IReportHistoryService
    {
        public ReportSaveResult Result { get; set; } = new() { Success = true };
        public ReportEntry? Saved { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Task<ReportSaveResult> SaveAsync(ReportEntry report, CancellationToken cancellationToken)
        {
            Saved = report;
            Token = cancellationToken;
            Calls++;
            return Task.FromResult(Result);
        }
        public Task<ReportHistoryLoadResult> LoadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetLatestAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReportEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

