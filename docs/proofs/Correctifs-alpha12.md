# Correctifs alpha.12 — Lot C12

Base vérifiée `67516e3eee2a67735648356f30643a97f9d9b56e`. Le run de référence de main est le [run 532](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45568463042).

## N3 — Réparation

- **Symptôme.** Le cache du Store, la planification de la vérification du disque et la remise à zéro de Windows Update étaient introuvables dans Réparation.
- **Cause.** Dans `InterventionsView.xaml` (l. 73–89), les deux boutons appelaient la vérification de santé et le parcours de réparation. `InterventionsViewModel.cs` (l. 145–180, 235 et 276) réservait le diagnostic de tout le catalogue et le lancement de la sélection à des commandes sans bouton.
- **Correction.** Le panneau expose « TOUTES LES RÉPARATIONS » et « LANCER LA SÉLECTION ». Le lancement requiert une sélection disponible et une vue au repos. Le parcours existant de confirmation, d’élévation et de restauration s’applique.
- **Régression.** `Alpha11ReparationVisibleTests.Catalogue_et_lancement_suivent_la_selection_visible` vérifie les deux liaisons XAML, les trois réparations cochables et la notification d’activation à la première coche. À la base, les deux boutons et la propriété d’activation sont absents. Le nouveau fichier contient ses doubles de diagnostic et d’exécution, conformément à la décision de virgil (relecture).

## N1 — Nettoyage

- **Symptôme.** Une phrase de confirmation refusée pour la Corbeille annulait aussi les zones temporaires, sans explication. La validation pouvait s’ouvrir sur une analyse périmée.
- **Cause.** `CleanupSelectionPolicy.cs` (l. 46) inclut la Corbeille dans la sélection globale. `CleanupViewModel.cs` (l. 395–423) émettait un plan unique et annulait toute la sélection au premier refus. `CleanupExecutionService.cs` (l. 58) contrôlait déjà l’expiration au moment d’exécuter.
- **Correction.** Le refus de la phrase annule les zones Critical. La sélection restante reçoit un aperçu et un plan propres, au niveau requis. Le motif apparaît dans le rapport, l’état et le message Virgil. Au-delà de dix minutes, l’état et le message invitent à relancer l’analyse avant d’ouvrir la validation.
- **Régressions.** Les deux cas de `Phrase_refusee_annule_la_corbeille_et_nettoie_la_zone_temporaire` échouent à la base, qui annule aussi la zone temporaire. `Phrase_exacte_et_phrase_refusee_appliquent_des_selections_distinctes` couvre la phrase exacte et la séparation des plans. À la base, son scénario de refus échoue sur l’ordre des opérations. Ces tests vérifient les liaisons XAML ainsi que la commande du ViewModel. `Analyse_expiree_ne_presente_pas_de_validation` échoue à la base, qui ouvre la validation sur l’aperçu antidaté.

### Assertion modifiée avec l’autorisation nommée

`LotCorbeilleSelectionTests.cs`, `Select_all_then_refusal_or_cancel_never_executes_recycle_bin`, cas « vide » et « faux ». Avant, l’assertion d’ordre attendait `Cancel:RecycleBin` puis `Cancel:UserTemporaryFiles`. Elle attend désormais `Cancel:RecycleBin` puis `Execute:UserTemporaryFiles`, parce que le refus de la phrase protège la Corbeille et laisse exécuter la zone temporaire confirmée. Les cas « annuler », « passer » et « exact » gardent leurs attentes.

## N4 — Désinstallation

- **Symptôme.** Un GUID pouvait faire choisir MSI pour un produit tiers. Des échecs étaient annoncés comme des lancements et une commande bloquée pouvait laisser un bouton sans explication.
- **Cause.** `ApplicationRegistryReader.cs` (l. 67–78 et 196–215) extrayait le GUID sans vérifier `WindowsInstaller`. `ApplicationUninstallService.cs` (l. 150–151 et 212–214) ne distinguait pas tous les codes d’échec. `ApplicationsViewModel.cs` (l. 579 et 788) activait le bouton à partir des données d’inventaire.
- **Correction.** La conversion en commande MSI requiert `WindowsInstaller = 1`. Les deux parcours distinguent les annulations 1602 et 1223, le produit introuvable 1605 et les autres échecs. Les détails affichent le motif du validateur sous le bouton.
- **Régressions.** `Un_guid_ne_remplace_la_commande_officielle_que_pour_une_entree_msi` échoue à la base sur la commande du produit non MSI. `Codes_de_sortie_distingues_en_individuel_et_en_lot` couvre les sept codes demandés et échoue à la base dès 1602. `Details_expliquent_le_blocage_et_le_rapport_affiche_le_code_1605` échoue à la base, où le texte de blocage lié est absent et le résultat 1605 est annoncé comme un lancement.

## N2 — Mises à jour

- **Symptôme.** Après Annuler, RETOUR restait grisé jusqu’à la fin de l’étape. Un panneau de progression haut pouvait repousser les commandes.
- **Cause.** `UpdatesViewModel.Sequence.cs` (l. 73–76 et 160) maintient l’opération active pendant l’étape et le contrôle final. `UpdatesView.xaml` (l. 66–67 et 129) utilisait une ligne de progression à hauteur automatique et liait RETOUR à `IsIdle`.
- **Correction.** L’accusé d’annulation permet de revenir à l’accueil et indique quand le rapport sera disponible. Les commandes d’action restent bloquées jusqu’à la fin. Le panneau de progression défile au-delà de sa hauteur maximale et le curseur d’attente est local à la vue.
- **Régression.** `Annulation_rend_le_retour_disponible_pendant_une_etape` vérifie le XAML, la fermeture de l’aperçu, le délai de l’accusé et le retour pendant une étape maintenue par le double. Elle vérifie ensuite la conclusion et le rapport après libération. À la base, le bouton est lié à `IsIdle` et la propriété attendue est absente.

## Vérification

Les motifs d’échec à la base sont établis par comparaison avec `67516e3`. Les tests WPF qui référencent les nouvelles propriétés nécessitent ces propriétés pour compiler.

- `dotnet test` en Release avec les filtres sur les suites du lot a compilé les projets de tests moteur et application sous Linux avec le SDK 8.0.419. Le lancement standard s’arrête faute du runtime Windows Desktop.
- Exécution moteur adaptée sous Linux, dans une copie temporaire des binaires et avec le runtime .NET seul. Les deux nouveaux tests de désinstallation, `LotCorbeilleSelectionCoreTests` et `CleanupSelectionPolicyTests` passent (9/9). Le filtre plus large donne 22 réussites et 2 échecs sur la résolution d’un chemin « Program Files » dans `ApplicationsUninstallSafetyTests`. Ces deux échecs se reproduisent à la base dans le même environnement. Les deux nouveaux tests moteur y échouent également, pour les régressions détaillées ci-dessus.
- Le lancement Linux adapté des tests application ne peut pas charger `WindowsBase`. Les assertions WPF et la suite UI relèvent du run Windows.
- Les 81 cas de `InterfaceAccentTests` passent dans un projet temporaire .NET exécutant le même fichier de test sur les sources du dépôt.
- Les vues Réparation, Nettoyage, Applications et Mises à jour sont valides en XML. `git diff --check` et `git diff -w --check` passent.

Les tests existants cités dans les consignes restent identiques, à l’exception de l’assertion de Corbeille ci-dessus. La CI Windows vérifie les suites moteur, application et interface.

### CI du dernier commit de code ou de tests

Résultat Windows à compléter après publication du code.

La CI du head final, y compris celle d’un éventuel commit de preuve seul, figure dans la description de la PR.

### Périmètre

Le diff est limité à la liste autorisée. Comparaison avec `origin/main`, qui pointe sur la base demandée.

```text
 docs/proofs/Correctifs-alpha12.md                  | 83 ++++++++++++++
 src/Virgil.App/ViewModels/ApplicationsViewModel.cs | 26 ++++--
 src/Virgil.App/ViewModels/CleanupViewModel.cs      | 61 +++++++++++--
 .../ViewModels/InterventionsViewModel.cs           |  3 +
 .../ViewModels/UpdatesViewModel.Sequence.cs        |  6 +-
 src/Virgil.App/ViewModels/UpdatesViewModel.cs      | 11 ++-
 src/Virgil.App/Views/ApplicationsView.xaml         | 17 ++--
 src/Virgil.App/Views/InterventionsView.xaml        | 12 +++
 src/Virgil.App/Views/UpdatesView.xaml              |  6 +-
 src/Virgil.App/Views/UpdatesView.xaml.cs           |  4 +-
 .../Applications/ApplicationRegistryReader.cs      |  2 +-
 .../Applications/ApplicationUninstallService.cs    | 26 ++++--
 .../Alpha11DesinstallationVueTests.cs              | 53 ++++++++++++
 .../Alpha11MisesAJourAnnulationTests.cs            | 99 ++++++++++++++++++++++
 tests/Virgil.App.Tests/Alpha11NettoyageTests.cs    | 99 ++++++++++++++++++++++
 .../Alpha11ReparationVisibleTests.cs               | 74 ++++++++++++++++
 .../Virgil.App.Tests/LotCorbeilleSelectionTests.cs |  1 +
 .../Alpha11DesinstallationTests.cs                 | 84 ++++++++++++++++++
 18 files changed, 637 insertions(+), 30 deletions(-)
```

## Essais sur un PC Windows

Vérifier une désinstallation MSI et une non MSI, la Corbeille réelle et le retour à l’accueil pendant une étape Windows Update réelle. Les doubles couvrent le routage et les résultats rapportés. Le comportement des assistants tiers et la durée d’une étape non interruptible dépendent du système.
