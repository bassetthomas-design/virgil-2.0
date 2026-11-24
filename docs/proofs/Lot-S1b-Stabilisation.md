# Lot S1b — Stabilisation de deux tests d’annulation

Base vérifiée : `59387b554f20dac933e57fc393a5d276c8b750c3`. Les numéros de ligne ci-dessous se rapportent à cette base.

## T1 — QuarantineReadReturnsControlWhileStorageIsBlockedAndCanBeCanceled

Run [476](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44334906226), identifiant `44334906226`, tentative 1. Échec à la ligne 115 de `QuarantineViewModelTests.cs`, après 5 s (866 tests applicatifs réussis, 1 en échec).

```text
Assert.False() Failure
Expected: False
Actual:   True
```

Dans `QuarantineViewModelTests.cs` (l. 100–115), le bouchon et l’attente d’entrée avaient chacun un plafond de 5 s. Si le test reprenait après l’expiration du bouchon, `CleanupViewModel.cs` (l. 164–187) absorbait l’exception de lecture et terminait l’opération avant l’assertion sur `pending`.

Le bouchon signale son entrée et reste bloqué jusqu’au signal de libération donné par le `finally`, après la demande d’annulation sur le chemin nominal. Les attentes de l’entrée et de la fin de `pending` ont chacune un plafond de 30 s, choisi pour laisser le pool reprendre sous charge tout en bornant le diagnostic d’une absence de signal. Le diagnostic en français indique l’événement attendu et l’état de la tâche. Le signal de libération ne peut pas expirer avant l’assertion.

Assertions conservées, dans l’ordre :

```csharp
Assert.False(pending.IsCompleted);
Assert.True(vm.IsBusy);
Assert.True(vm.Operation.IsRunning);
Assert.False(vm.IsBusy);
Assert.Empty(vm.QuarantineItems);
Assert.Contains("annulée", vm.StatusText);
Assert.Equal(0, store.Effects);
```

Répétitions locales avec `dotnet test --filter "FullyQualifiedName=Virgil.Tests.QuarantineViewModelTests.QuarantineReadReturnsControlWhileStorageIsBlockedAndCanBeCanceled"` : 0/50 effectuées, durée indisponible.

## T2 — Cancel_during_Windows_Update_acknowledges_non_interruptible_step_immediately

Run [464](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44121592223), identifiant `44121592223`, tentative 1. Échec sur l’attente de fin de `pending` après libération du backend (l. 528 de `UpdatesSequenceViewModelTests.cs`).

```text
System.TimeoutException : The operation has timed out.
```

Run [491](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45189181226), identifiant `45189181226`, tentative 1. Échec sur le texte d’accusé (l. 525 du même fichier).

```text
Assert.Contains() Failure: Sub-string not found
String:    "Étape 1/1 — Windows Update : logiciels (j"···
Not found: "ne peut pas être interrompue"
```

Un rappel `Progress<T>` sur le pool pouvait franchir la garde d’annulation avant la commande puis écrire `SequenceStepText` après elle (`UpdatesViewModel.Sequence.cs`, l. 134–154, et `UpdatesViewModel.cs`, l. 94–105). Après le signal `finish`, le contrôle final et le rapport pouvaient aussi dépasser le plafond de 1 s de `UpdatesSequenceViewModelTests.cs` (l. 528).

La préparation et la confirmation s’exécutent sous le contexte de progression synchrone déjà utilisé dans la classe. Le contexte précédent est restauré dans un `finally` avant les attentes du test. La progression précédant l’entrée dans le backend est traitée avant ce signal, puis le backend attend `finish`. Les deux assertions d’accusé suivent immédiatement la commande, avant la libération de l’étape. L’entrée et la fin de la séquence sont attendues sur leurs tâches, chacune avec un plafond de 30 s qui laisse reprendre le pool sous charge et borne le diagnostic d’une absence de signal. L’attente indique l’événement et l’état de la tâche en français.

Assertions conservées, dans l’ordre :

```csharp
Assert.True(f.Vm.IsCancelling);
Assert.Contains("ne peut pas être interrompue", f.Vm.SequenceStepText);
Assert.False(pending.IsCompleted);
Assert.Single(f.Backend.Executed);
```

Répétitions locales avec `dotnet test --filter "FullyQualifiedName=Virgil.Tests.UpdatesSequenceViewModelTests.Cancel_during_Windows_Update_acknowledges_non_interruptible_step_immediately"` : 0/50 effectuées, durée indisponible.

La tentative locale des deux filtres a compilé `Virgil.App.Tests` en Release avec le SDK 8.0.419 en activant le ciblage Windows. VSTest a interrompu le lancement avant l’exécution des tests : `Microsoft.WindowsDesktop.App` 8.0 est requis et indisponible sur le poste Linux.

## Délais avant et après

| Attente | Avant | Après |
| --- | --- | --- |
| T1 — Libération du stockage | Événement avec expiration autonome à 5 s | Signal du `finally`, sans expiration autonome |
| T1 — Entrée dans la lecture | `WaitAsync` à 5 s, sans diagnostic dédié | Signal d’entrée, plafond de 30 s et diagnostic |
| T1 — Fin après libération | Attente sans plafond | Fin de la tâche, plafond de 30 s et diagnostic |
| T2 — Entrée dans Windows Update | `WaitAsync` à 1 s, sans diagnostic dédié | Signal d’entrée, plafond de 30 s et diagnostic |
| T2 — Accusé d’annulation | Assertions immédiates | Assertions immédiates |
| T2 — Fin après libération | `WaitAsync` à 1 s, sans diagnostic dédié | Fin de la tâche, plafond de 30 s et diagnostic |

Seuils R4 intacts : 15 s avant fermeture forcée d’un lanceur, 500 ms de latence UI, 1 s pour l’accusé « Annuler rend la main », 2 s pour « DERNIER RAPPORT après Prêt », 120 s de scan approfondi et 45 s pour l’aperçu « Tout mettre à jour ».

## CI Windows

Dernier commit touchant aux tests : `95c2877931b8b2a62b5ea717a97d0d8878ec8216`. Le run [517](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45449886636), identifiant `45449886636`, a réussi sur Windows. Durée du job `build` : environ 4 min 12 s d’après les horodatages de son journal.

| Étape | Projet | Réussis |
| --- | --- | ---: |
| `Category!=UI` | `Virgil.Core.Tests` | 1 341 |
| `Category!=UI` | `Virgil.App.Tests` | 906 |
| `Category=UI` | `Virgil.UiTests` | 14 |

Les deux tests du lot figurent parmi les réussites applicatives (T1 : 89 ms, T2 : 2 ms). La CI du commit final consacré à cette preuve figure dans la description de la PR.

## Périmètre

Les modifications portent sur les quatre fichiers autorisés. Aucun fichier sous `src/`, aucun workflow et aucun fichier de test UI ne figure dans le diff.

Sortie de `git diff --stat origin/main...HEAD` (ce clone référence `main` par `origin/main`) :

```text
 docs/proofs/Lot-S1b-Stabilisation.md               | 103 +++++++++++++++++++++
 tests/Virgil.App.Tests/QuarantineViewModelTests.cs |  12 ++-
 .../UpdatesSequenceViewModelTests.cs               |  37 ++++++--
 tests/Virgil.App.Tests/WpfConditionWait.cs         |  13 +++
 4 files changed, 150 insertions(+), 15 deletions(-)
```
