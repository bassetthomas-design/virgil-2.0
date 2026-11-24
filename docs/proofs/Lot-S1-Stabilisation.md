# Lot S1 — Stabilisation des tests fragiles

Base vérifiée `73e1cb5d5624872afd635563dbd51ecc5d899b1a`. Périmètre arbitré par virgil (relecture).

## T1 — Fermeture des lanceurs

Le run [45226479738](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45226479738), tentative 1, signale `System.TimeoutException : The operation has timed out.` dans `VerifyNativeShutdown` à `LauncherShutdownTests.cs:188`. Le symptôme a aussi été signalé sur la PR #103. Les théories et cas voisins qui utilisent la même horloge sont traités par précaution.

À `73e1cb5`, `LauncherShutdownPolicy.cs:42` attend un minuteur par seconde virtuelle et peut réarmer après le retour du rappel. Deux avances du test pouvaient se succéder pendant que cette continuation attendait son tour. Chaque avance attend désormais l’enregistrement d’un minuteur actif avec échéance future, et le test attend ensuite la fin de la fermeture. Les plafonds de 10 s absorbent la planification des continuations concurrentes sans changer les 15 s virtuelles.

Assertions de `Launcher_is_force_closed_only_after_15_seconds`, `Known_launcher_helpers_allow_shutdown`, `VerifyNativeShutdown` (commun aux deux tests natifs) et `Launcher_exit_before_deadline_does_not_wait_for_force`.

```csharp
Assert.Equal(new[] { false }, backend.Commands);
        Assert.False(pending.IsCompleted);
        Assert.Single(backend.Commands);
        Assert.Equal(exits ? new[] { false } : new[] { false, true }, backend.Commands);
        if (!exits) Assert.Contains(result.Steps, s => s.Message.Contains("fermé de force", StringComparison.Ordinal));

Assert.Equal(new[] { false }, backend.Commands);
        Assert.Equal(new[] { false, true }, backend.Commands);
        Assert.Contains(result.Steps, step => step.Message.Contains("fermé de force", StringComparison.Ordinal));

Assert.Equal(@"C:\Steam\steam.exe", Assert.Single(runner.Requests, r => r.Arguments.Contains("-shutdown")).FileName);
        Assert.DoesNotContain(runner.Requests, r => r.Arguments.Contains("/F"));
        Assert.Contains(runner.Requests, r => r.Arguments.Contains("/F"));
        Assert.Contains(result.Steps, s => s.Message.Contains("fermé de force", StringComparison.Ordinal));

Assert.Equal(new[] { false }, backend.Commands);
```

## T2 — Aperçu « Tout mettre à jour »

Les deux tests `Smoke_run_has_no_destructive_side_effect` et `Cleanup_summary_is_captured_without_execution` ont échoué dans [45226479738](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45226479738) (tentatives 1 et 2 signalées) et [45225285119](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45225285119) (tentative 1). Le journal de ce dernier donne `Actual:   Xunit.Sdk.FailException: Vue/contrôle UIA invisible : UpdatePlanStep.Drivers` dans l’échec de `Assert.Null()`.

À `73e1cb5`, `UiSession.cs:164–165` prenait le premier élément portant l’identifiant partagé dès le début de la préparation. `UpdatesViewModel.Sequence.cs:73–118` enchaîne le scan à 45 s et la lecture des catégories. `UpdatesView.xaml:239–282` place les lignes dans une zone défilante et `UpdatesViewModel.Sequence.cs:493–503` affecte le même identifiant aux lignes pilotes, dont le BIOS.

Le premier plafond de 120 s laisse 75 s après les 45 s produit pour préparer les catégories. La seconde attente dispose de 10 s pour identifier puis rendre visible la case par le motif UIA Scroll. Le libellé exact, l’état activé et le motif Toggle isolent la case Windows Update des autres lignes, y compris les paquets installables du fabricant qui partagent aussi l’identifiant. Un échec écrit `14-plan-mises-a-jour-diagnostic.txt` avec l’état UIA et le temps écoulé. Le PNG existant et la comparaison exacte des PNG sont conservés.

Assertions de l’aperçu, de `AssertNoSideEffects`, de `AssertHealthy`, de la capture et du test de récapitulatif.

```csharp
Assert.Equal(FlaUI.Core.Definitions.ToggleState.On, drivers.Patterns.Toggle.Pattern.ToggleState.Value);
            Assert.Equal(before, SessionFiles.SnapshotReports());

Assert.Null(updatePreviewError);
        Assert.Equal("Témoin de lecture seule : ne jamais nettoyer.",
            File.ReadAllText(Path.Combine(fixtureTemp, "temoin-smoke.tmp")));
        Assert.False(Directory.Exists(Path.Combine(SessionFiles.ArtifactDirectory, "temp-lecture-seule")));
        Assert.Equal(expected, actual);

Assert.NotNull(process);
        Assert.False(process.HasExited, "Le processus Virgil a quitté pendant les tests.");
        Assert.True(files.ErrorLines.Count == 0, string.Join(Environment.NewLine, files.ErrorLines));
        Assert.True(violations.IsEmpty, string.Join(Environment.NewLine, violations));

Assert.True(HasPixelVariance(capture.Bitmap), $"Capture uniforme/noire : {filename}");

Assert.True(File.Exists(Path.Combine(SessionFiles.ArtifactDirectory, "12-recap-nettoyage.png")));
```

## T3 — Sonde WM_NULL hors périmètre

virgil (relecture) a retiré T3 du lot. L’incident historique est le run [44098331297](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44098331297), tentative 1, « SendMessageTimeout WM_NULL a échoué après 501 ms ». La sonde, `AssertHealthy` et `AssertResponsive` sont inchangées.

## T4 — Vues WPF rendues

Le run [45144114142](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45144114142), tentative 1, donne `System.TimeoutException : The operation has timed out.` pour le diagnostic et les mises à jour. Le même symptôme a été signalé pour les priorités dans la PR #98. Le numéro de run de ce signalement n’est pas vérifié.

À `73e1cb5`, `ThemeContrastTests.cs:18,238–242` fait partager le répartiteur STA aux classes de tests. Les attentes autour de `OnSta` incluaient la file, et les rendus attendaient la priorité `ApplicationIdle` (`LotPrioritesTests.cs:237–242`, `LotDiagnosticTests.cs:235–240`, `LotMisesAJourTests.cs:129`). Le plafond de 10 s est conservé et commence au démarrage sur le STA pour couvrir les étapes actives. Les vérifications de rendu attendent une mise en page valide et les textes attendus en sondant à priorité `Background`. Les opérations asynchrones Store signalent l’étape et leur état. Les propriétés WPF sont lues avant l’écriture asynchrone des preuves.

### Priorités avec source, retour à la normale et navigation

```csharp
Assert.Contains(DiskText, texts);
                Assert.Contains("Scan · mesuré à 18 h 24", texts);
                Assert.Contains("Diagnostic · mesuré à 18 h 34", texts);
                Assert.Contains(DiskResolved, texts);
                Assert.DoesNotContain(DiskText, texts);
                    Assert.Equal(new[] { "PRIORITÉS", "1", "1 POINT À VÉRIFIER · Mesuré à 18:44" }, texts);
                    Assert.Equal("Voir les priorités", AutomationProperties.GetName(button));
                    Assert.Equal(new[] { "Priorités" }, destinations);
```

### Priorités avant et après analyse

```csharp
Assert.Contains("Aucune analyse récente : lancez un scan pour voir les priorités.", VisibleTexts(panel));
                Assert.Contains("Aucun point prioritaire au dernier contrôle.", texts);
                Assert.Contains("Mesuré à 18:24", texts);
```

### Diagnostic

```csharp
Assert.Contains("Mesurer ce qui ralentit le PC et savoir quoi vérifier.", texts);
            Assert.Contains("Aucun relevé terminé pour le moment.", texts);
            Assert.Contains("Constats disponibles après le premier relevé.", texts);
            Assert.DoesNotContain("Aucun problème détecté dans les signaux disponibles.", texts);
            Assert.Contains("Actualiser le diagnostic", buttons);
            Assert.DoesNotContain("Actualiser", buttons);
            Assert.Contains("Dernier relevé à 18 h 24", texts);
            Assert.Contains("Note de santé : 50/100", texts);
            Assert.Contains("À vérifier en priorité", texts);
            Assert.Contains("Processeur : ça chauffe", texts);
            Assert.Contains("Action proposée : vérifiez la ventilation et réduisez la charge.", texts);
            Assert.Contains("Détails techniques", texts);
            Assert.DoesNotContain("Windows a quelques réclamations", texts);
            Assert.Contains("Mettre à jour…", buttons);
            Assert.Contains("Nettoyer…", buttons);
            Assert.Equal(new[]
            {
                "Tous les constats", "Démarrage", "Signaux indisponibles — non mesurés ou lectures en échec",
                "Processeur", "Mémoire", "Disques", "Réseau", "Carte graphique"
            }, expanders.Select(expander => (string)expander.Header));
            Assert.Equal(new[] { false, false, false, false, false, false, false, false },
                expanders.Select(expander => expander.IsExpanded));
            Assert.Single(Descendants<DataGrid>(view));
            Assert.InRange(source.Reads, 0, 1);
```

### Mises à jour et ouverture du Store

```csharp
Assert.Contains("1 mise à jour détectée via WinGet.", texts);
            Assert.Contains("À jour selon Windows Update.", texts);
            Assert.Contains("vérification partielle", texts);
            Assert.Contains(StoreReason, texts);
            Assert.True(storeButton.IsEnabled);
            Assert.Equal("ms-windows-store://downloadsandupdates", launch.FileName);
            Assert.True(launch.UseShellExecute);
            Assert.Empty(launch.Arguments);
```

L’assertion enchaînée `Assert.IsAssignableFrom<IAsyncRelayCommand>(storeButton.Command)` est conservée avant l’exécution de la commande.

## T5 — Échap depuis la grille de processus

Le dépassement du plafond de 5 s à `L6BehaviorTests.cs:80` a été signalé sans run d’échec confirmé. Le run [45226479738](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45226479738), tentative 1, est cité comme exemple de réussite, pas comme preuve de ce dépassement.

À `73e1cb5`, `L6BehaviorTests.cs:73–80` fournit l’horloge système à la présentation et `DiagnosticSampler.cs:16` attend une seconde réelle. Le test injecte la même horloge contrôlée dans l’échantillonneur, le modèle de vue et la confirmation. Il avance quand un minuteur est enregistré et attend que la tâche de présentation lancée par la vue se termine. Les 5 s restent un plafond d’échec démarrant sur le STA, suffisant pour le scénario sans source externe.

Assertions d’Échap.

```csharp
Assert.True(cell.Focus());
            Assert.True(cell.IsKeyboardFocusWithin);
            Assert.True(key.Handled);
            Assert.False(vm.IsValidationOpen);
            Assert.Equal(0, action.Calls);
            Assert.False(next.Handled);
```

## Attentes avant et après

| Cas | Avant | Après |
| --- | --- | --- |
| Lanceurs | Avances consécutives, fin sous 1 s ou 30 s | Réarmement observable puis fin de tâche, 10 s par condition |
| Aperçu | Première case avec l’identifiant, visibilité sous 60 s | Ouverture vérifiée sous 120 s, case Windows Update unique et visible sous 10 s |
| Rendus | Inactivité du répartiteur et 10 s incluant la file | Mise en page et textes attendus, 10 s à compter de l’entrée sur le STA |
| Échap | Échantillonnage avec 1 s réelle, fin sous 5 s | Horloge contrôlée et fin de présentation sous 5 s sur le STA |

Les seuils R4 restent à 15 s pour la fermeture forcée, 500 ms pour la latence UI, 1 s pour Annuler, 2 s pour le dernier rapport, 120 s pour le scan approfondi et 45 s pour l’aperçu produit. La liste blanche `SafeUi` est inchangée.

## CI Windows

Cette preuve cite le dernier commit comportant du code de test. La CI du commit final, qui modifie uniquement cette preuve, est consignée dans la description de la PR.

Validation du commit de tests en attente.

## Périmètre du diff

Seuls les fichiers autorisés sont touchés.

```text
 docs/proofs/Lot-S1-Stabilisation.md              | 186 +++++++++++++++++++++++
 tests/Virgil.App.Tests/L6BehaviorTests.cs        |  57 ++++++-
 tests/Virgil.App.Tests/LotDiagnosticTests.cs     |  20 +--
 tests/Virgil.App.Tests/LotMisesAJourTests.cs     |  57 ++++---
 tests/Virgil.App.Tests/LotPrioritesTests.cs      |  31 ++--
 tests/Virgil.App.Tests/ThemeContrastTests.cs     |   7 +
 tests/Virgil.App.Tests/WpfConditionWait.cs       |  42 +++++
 tests/Virgil.Core.Tests/LauncherShutdownTests.cs |  65 ++++++--
 tests/Virgil.UiTests/UiSession.cs                |  79 +++++++++-
 9 files changed, 473 insertions(+), 71 deletions(-)
```
