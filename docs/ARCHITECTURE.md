# Virgil 2.0 — Architecture

Source unique de l'architecture, vérifiée à `4af1cce`. Le [statut](PROJECT_STATUS.md) suit les lots ; [ACTIONS.md](ACTIONS.md) spécifie les capacités et les cibles. La présence d'un fichier ne vaut pas validation manuelle Windows.

## Huit projets réels

La solution `Virgil.sln` contient huit projets compilés (les dossiers de solution ne sont pas des projets).

| Projet | Responsabilité actuelle |
| --- | --- |
| `src/Virgil.App` | WPF .NET 8 Windows x64 ; composition Hosting/DI, vues et ViewModels CommunityToolkit.Mvvm |
| `src/Virgil.Contracts` | DTO et modèles partagés ; `Actions`, `Diagnostic`, protocole élevé ; `ScanRules.cs` encore présent |
| `src/Virgil.Core` | Orchestration, contrats de modules/actions, confirmation et politiques ; contient encore des accès OS legacy |
| `src/Virgil.Infrastructure` | Adaptateurs Windows, `ProcessRunner`, quarantaine, démarrage et releases ; référence Core/Contracts |
| `src/Virgil.ElevatedHelper` | Exécutable privilégié séparé, protocole et allowlist ; référence seulement Contracts parmi les projets Virgil |
| `tests/Virgil.Core.Tests` | Services, sécurité, architecture et protocole |
| `tests/Virgil.App.Tests` | ViewModels, composition, animation et architecture App |
| `tests/Virgil.UiTests` | FlaUI sur l'exécutable publié, tests de sécurité de SafeUi |

La séparation métier/OS est incomplète : `WindowsUpdateStatusService`, `WindowsInfoReader`, `ProcessReader` et `ApplicationRegistryReader` sont encore dans Core. `Contracts/ScanRules.cs` reste présent. Ces faits sont conservés, pas transformés en architecture cible déjà livrée.

## Composition et interface

`AppBootstrap` et `App/Hosting` composent les services via Microsoft.Extensions.Hosting. Les vues de modules ont des ViewModels injectés ; les comportements WPF restent dans les vues. `IProcessRunner` est le port applicatif, implémenté par `Infrastructure/Processes/ProcessRunner.cs` ; le helper garde son exécuteur interne borné.

La navigation dans `MainWindow.xaml` comporte onze écrans : Accueil, Scan, Nettoyage, Diagnostic, Ressources, Applications, Mises à jour, Démarrage, Réseau, Réparation et Rapports. Les vues dédiées dans `src/Virgil.App/Views/` incluent `DiagnosticView`, `NetworkView`, `StartupView` et `VirgilReleaseView`, en plus des vues Nettoyage, Mises à jour, Applications, Interventions, Ressources et Historique. `VirgilReleaseView` est un panneau de vérification des versions, sans bouton de navigation supplémentaire.

`MainWindow.xaml.cs` reçoit les événements `VirgilMessageRequested` et affiche les messages `[VIRGIL]`. Il n'y a pas de champ de saisie de chat. Les modèles d'animation sont dans `App/Animation`. Le ton est défini dans [CHATBOX_MODULE.md](modules/CHATBOX_MODULE.md#style-de-communication).

## Contrats et confirmation

`Contracts/Actions` porte `RiskLevel`, `ConfirmationLevel`, `Reversibility`, `ActionPreview`, `ActionResult` et Findings. `Core/Actions` porte `IMaintenanceModule`, `IMaintenanceAction`, `ConfirmedPlan`, `ConfirmationService` et `IConfirmedPlanGate`. Le plan a une identité en mémoire, un hash d'aperçu, une expiration et un usage unique. Un objet désérialisé ou un booléen ne vaut pas consentement.

Les modules Nettoyage et Diagnostic utilisent le contrat commun. Diagnostic est migré ; ses propositions réelles sont construites par `DiagnosticModule.GetActions`. La porte est consommée avant l'effet dans les services à action. Les tests couvrent plans absents, expirés, altérés et rejoués avec refus sans effet.

Les gardes `CleanupPathGuard`, `CleanupSafetyClassifier`, `ProcessProtectionPolicy` et `ElevatedActionAllowlist` restent en place. Les fermetures lient l'identité du processus, la relisent avant l'effet et refusent les processus protégés. Fermeture forcée et fermeture propre sont des actions distinctes.

## Nettoyage, quarantaine et progression

`CleanupMaintenanceModule` produit les constats ; `CleanupSelectionAction` lie toute la sélection à l'aperçu. `LeftoverAnalyzer` exclut les preuves d'installation. `SoftwareFootprintSource` lit les empreintes Windows ; ses ports permettent aussi un faux PC en lecture seule pour les tests. Les clés de registre restent en lecture seule. Les lectures incomplètes produisent des restes possibles à vérifier.

`CleanupViewModel` affiche les trois groupes et les sélections globales/par catégorie/individuelles. Les marqueurs personnels priment sur `CleanupItemNature`. Les dossiers personnels, Téléchargements, Corbeille et Windows.old sont exclus de la sélection globale. Le récapitulatif et `CleanupConfirmationSummary` lient nature, compteurs et phrase à l'aperçu confirmé. Les temporaires Windows et caches ne sont pas assimilés à des fichiers système sensibles par ce résumé.

`CleanupExecutionService` applique les gardes par fichier et utilise `QuarantineStore` ; la Corbeille garde son parcours Windows distinct. Le stockage vérifie les restaurations par taille/SHA-256 et refuse d'écraser un fichier. Rétention de 7 jours par défaut, réglable de 1 à 30. La purge expirée ou immédiate reste une action distincte confirmée ; aucun effacement silencieux à l'échéance. Un déplacement en quarantaine ne libère pas l'espace correspondant.

`OperationProgress`, `IOperationProgress` et `ThrottledOperationProgress` sont les contrats Core. `LongOperationViewModel` et `OperationProgressPanel` affichent étape, progression, durée, estimation et annulation dans Nettoyage seulement. Généralisation : **cible L5** ; le temps estimé n'est pas une mesure de performance Windows.

## Diagnostic ponctuel

`WindowsDiagnosticSource` lit les mesures Windows, `DiagnosticSampler` calcule les variations à partir de deux lectures et `DiagnosticModule` produit les constats. `DiagnosticValue<T>` porte disponibilité et raison. PDH couvre notamment CPU et mémoire ; les compteurs non exposés restent indisponibles.

**Une mesure à l'entrée, puis seulement sur Actualiser** : `DiagnosticViewModel` n'a pas de minuteur. Il annule la présentation au masquage/réduction et évite la superposition des lectures. La cadence introduite en 3d remplace celle de 1d/1e. Aucun monitoring continu ni historique de mesures livré.

La source passe `Missing<double>()` pour la température CPU. `DiagnosticViewModel.CpuDetails` en affiche l'indisponibilité. Les températures GPU, la VRAM totale et SMART complet ne sont pas garantis ; le contrôle de prédiction de panne ne vaut pas diagnostic complet du disque. Les connexions TCP/UDP par processus ne sont pas des débits réseau ; les E/S disque par programme restent indisponibles.

`DiagnosticAnalyzer` calcule note partielle, top 3 et constats ; `WindowsDiagnosticSignals` apporte les signaux ponctuels. Une note partielle de 100 ne prouve pas la santé des sources absentes. `DiagnosticCloseAction` utilise le service processus gardé ; `DiagnosticNavigationAction` ouvre Nettoyage/Mises à jour après validation, sans y exécuter d'action. Le démarrage n'est plus seulement informatif dans l'application : son écran distinct est décrit ci-dessous.

Capteurs : **cible L9** ; fusion Ressources : **cible L7**. Budget CPU et temps représentatifs Windows : **non mesuré**. Les seuils d'analyse sont des règles de code, pas des mesures de performance.

## Mises à jour et Applications

`WingetUpdateScanService` utilise JSON et repli CLI ; `WingetUpdateExecutionService` épingle la version confirmée. `UpdateSequenceService` fige la sélection, lie son aperçu au plan et orchestre Windows, Defender, pilotes WUA et WinGet. `WindowsUpdateSequenceBackend` porte les effets Windows ; `UpdateMaintenanceRunner` borne l'exécution élevée et vérifie le point de restauration de la session avant Windows et les pilotes.

Steam, Epic Games et Ubisoft Connect sont seulement des étapes de consigne fixes. Le backend retourne une réussite sans installation effective pour `UpdateComponent.Launcher`. Aucun scan de mises à jour Store. Correction : **cible L6**.

`DriverInformationService` fixe `CanInstallDrivers = false` ; `UpdatesViewModel.IsInstallDriversVisible` reste donc faux. `UpdateSequenceChoiceViewModel` décoche les pilotes par défaut. La décision produit les voudrait cochés et retirerait ce code mort en **cible L6+**.

La progression et l'annulation restent locales à UpdatesView : arrêt entre étapes, sans coupure brutale d'une installation. `OperationProgressPanel` et `LongOperationViewModel` existent dans Nettoyage seulement ; harmonisation en **cible L5**. Les résultats de séquence alimentent les rapports et signalent le besoin de redémarrage, sans redémarrer seuls.

`ApplicationUninstallService` lance les désinstalleurs officiels, individuellement ou en lot après aperçu/plan et protections. `ApplicationsViewModel` porte l'inventaire, la recherche et les icônes chargées en arrière-plan. Le lancement ne garantit pas que le désinstalleur a achevé son travail.

## Démarrage et Réparation

`StartupChangeService` construit et confirme activation/désactivation/restauration des autorisations reconnues. `WindowsStartupApprovalStore` sauvegarde et vérifie l'état précédent. `StartupApprovalHandler` limite l'écriture HKLM à StartupApproved ; aucun chemin arbitraire. La sauvegarde, le point de restauration et la comparaison de l'état précèdent l'écriture. Une protection indisponible bloque le changement. Aucune entrée n'est supprimée ; les tâches restent en lecture seule. Logos, impact et temps avant/après : **cible L12**, non mesuré actuellement.

`InterventionDiagnosticService` et `InterventionExecutionService` portent Vérifier / Scanner et réparer. `ElevatedActionAllowlist` autorise SFC, DISM et `chkdsk /scan`, plus les actions fixes réseau et services. `virgil-update` redémarre le service Windows Update ; il ne fait pas une remise à zéro complète. `chkdsk` planifié et `wsreset` ne sont pas livrés : **cible L12b**. Les changements système concernés sont bloqués sans point de restauration vérifié.

## Rapports, Réseau et distribution

`ReportHistoryService` conserve les rapports locaux JSON ; `ReportExportService` construit un export texte assaini par `ReportSanitizer`. Comparaison continue/PDF : **cible L5–L13, sous-lot non fixé**. `NetworkViewModel` et `WindowsNetworkStatusReader` lisent l'état réseau ; la vue propose un lien vers Réparation.

`VirgilReleaseChecker` et `GitHubVirgilReleaseSource` comparent la version aux releases GitHub ; `VirgilReleaseViewModel` propose l'ouverture de page, sans téléchargement ni installation automatique. `installer/Virgil.iss` et `installer/Build-Installer.ps1` distribuent la publication self-contained et le helper. Tags et dates : voir PROJECT_STATUS, sans dupliquer le suivi ici.

## Tests UI

`tests/Virgil.UiTests` utilise FlaUI et l'exécutable publié indiqué par `VIRGIL_APP_EXE`. `UiSmokeTests` porte `Category=UI` : onze écrans, scan approfondi, récapitulatif Nettoyage, réactivité et absence d'effet destructif. `SafeUi` refuse tout identifiant absent de son allowlist de lecture seule avant même de rechercher le contrôle. Ouvrir le récapitulatif est autorisé ; Exécuter/Confirmer, nettoyage, désinstallation, mise à jour, purge et modification Démarrage sont interdits par ce pilote.

`SafeUiTests` porte `Category=Safety` et vérifie les refus sans invocation. Les tests dépendant des API Windows portent `Category=Windows` (par exemple `StartupFileMetadataTests`) ; ce trait est distinct du filtre UI. Les tests FlaUI tournent sur le runner Windows via `Category=UI` ; ils ne portent pas tous aussi le trait Windows.

`UiSession.AssertNoSideEffects` compare exactement les noms des treize PNG : `01-accueil.png`, `02-scan.png`, `03-nettoyage.png`, `04-diagnostic.png`, `05-ressources.png`, `06-applications.png`, `07-mises-a-jour.png`, `08-demarrage.png`, `09-reseau.png`, `10-reparation.png`, `11-rapports.png`, `12-recap-nettoyage.png` et `session-desktop.png`. Il vérifie aussi le témoin TEMP et la quarantaine. Ce contrôle des noms ne prouve pas la qualité visuelle des pixels ; l'artefact permet la revue humaine.

`UiSession.AssertResponsive` vérifie un plafond de latence de 500 ms. Ce plafond est un critère de test, pas un résultat mesuré annoncé par cette documentation.

## CI Windows

`.github/workflows/build.yml` se déclenche aussi sur les PR docs vers main, sans filtre de chemins. Étapes : restore, build Release, tests `Category!=UI`, publication win-x64 self-contained, réglage d'affichage 1920×1080 si la commande est disponible (sinon fenêtre FlaUI 1600×900), puis « UI smoke tests » avec `Category=UI` sur l'exécutable publié.

L'artefact `Virgil-ui-smoke` recueille les PNG et fichiers de diagnostic, même en cas d'échec. Les artefacts applicatif `Virgil-2.0-win-x64` et installateur `Virgil-win-x64-setup` sont distincts. Le workflow de release exclut UI de son étape Test depuis REL-1 ; `build.yml` reste la référence UI Windows. Un build Linux ne vaut pas validation Windows.

## Architecture cible

**Cible de séparation, à répartir dans L5–L13, sous-lot non fixé :** Core ne devrait conserver que logique, orchestration et ports ; Infrastructure devrait porter tous les accès OS. Contracts devrait rester sans dépendance OS/WPF ; le helper devrait conserver sa frontière privilégiée et son allowlist. La dette Core signalée en tête n'est pas résolue par ce document.

**Cibles planifiées par le cadrage :** L5 harmoniserait les opérations longues ; L6 compléterait les mises à jour ; L7 fusionnerait Ressources ; L9 traiterait les capteurs ; L11 livrerait catalogue/réglage du ton ; L12 compléterait Démarrage ; L12b compléterait Réparation. Agent/Tray reste reporté, sans projet ni sous-lot fixé. Voir ACTIONS pour les autres limites et ROADMAP pour l'ordre de travail.

## L5-1 — progression et annulation

Scan, Diagnostic, Mises à jour, Réparation et Applications partagent `LongOperationViewModel` et `OperationProgressPanel`. Les notifications du panneau passent par `ThrottledOperationProgress`, avec une horloge injectable, un délai d’affichage de deux secondes et un acquittement immédiat d’Annuler. Les séquences, réparations et désinstallations affichent le panneau immédiatement. Les étapes non interruptibles finissent sans recevoir le jeton utilisateur ; les étapes suivantes sont bloquées. Le helper protège notamment SFC, DISM, les réinitialisations réseau et les points de restauration ; renouveler l’IP exécute toujours `/renew` après `/release`. Le backend MAJ transmet `.cancel` et conserve le protocole jusqu’à la fin du helper. WUA reste synchrone ; l’annulation pendant un appel COM acquitte la demande sans promettre l’arrêt de cet appel. Les gardes d’architecture et l’extension des contrôles d’accents relèvent de L5-2, hors de ce sous-lot. La CI Windows et ses artefacts restent la preuve de validation ; aucune validation locale Linux ne s’y substitue.

## L5-2 — gardes et accents

Les gardes d’attentes synchrones couvrent désormais App, Core, Infrastructure et ElevatedHelper ; `Thread.Sleep` et `WaitForExit()` sans délai sont interdits. Une liste nominative d’opérations longues vérifie par réflexion le contrat d’annulation et la présence d’un panneau lié dans chacune des cinq vues. Une mutation en mémoire des cinq vues prouve le rejet d’un panneau absent. `CancellationToken.None` est contrôlé par une liste blanche de lignes précises avec justification. Le garde d’accents compare sans distinction de casse les littéraux App/Core/Helper et les textes XAML/ressources ; les identifiants techniques ne sont pas des libellés. Les preuves et limites sont détaillées dans [L5-2](proofs/L5-2.md), avec les résultats Windows et captures dans la PR.

## L6-1 — défauts de comportement alpha.2

L’inventaire Démarrage lit HKCU Run uniquement en vue 64 bits, avec une cible `Run` ; HKLM conserve ses deux vues et `Run32` reste réservé à HKLM 32 bits. Un lecteur de registre injectable et `StartupInventoryMerger` permettent de tester les doublons sans registre réel. Les commandes sans cible sont désactivées ; Restaurer utilise exclusivement l’instantané vérifié déjà créé par `StartupChangeService` dans la session.

`DiagnosticValue.Unmeasured` distingue une mesure non exposée d’un échec de lecture (`Unavailable`). Seuls les échecs rendent la note partielle ; les signaux absents restent explicitement non mesurés et ne coûtent aucun point. Les lanceurs gourmands sans fenêtre produisent un constat informatif de cinq points, sans cible ni action de fermeture. `Agent.exe` exige aussi une preuve Blizzard/Battle.net dans le chemin ou l’éditeur. La politique de protection des processus reste inchangée. Échap est intercepté en tunnel dans la vue Diagnostic uniquement lorsqu’une validation est ouverte ; la lecture du dernier rapport démarre indépendamment de la vérification réseau.

La séquence de mises à jour détecte Steam, Epic Games, Ubisoft Connect et Battle.net dans l’inventaire des applications installées. Leurs lignes informatives sont non cochables, exclues du plan confirmé et du backend d’exécution ; le rapport les conserve « À faire dans le lanceur », parmi les actions non exécutées. Le bouton et la commande factices « Marquer à revoir » du Nettoyage sont retirés. Les capteurs réels, le dictionnaire étendu d’accents (L6-2) et la fermeture automatique des lanceurs (L6+) restent hors périmètre.
