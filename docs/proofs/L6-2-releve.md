# L6-2 — reprise du relevé lexical

État contrôlé : `e6d861cc40c05763c4cc2ca6f612bc43d2547158`, avant ce correctif, dans la même PR #71. Aucun changement de `.github` ou de logique L6-1/L6+.

## Méthode et périmètre

Extraction des contenus de chaînes C# (y compris interpolations, verbatim et scripts bruts) et des attributs/textes XML des dossiers `src/Virgil.*`. Les identifiants et expressions d’interpolation restent hors des segments de texte ; les références XAML et les jetons techniques sans espace contenant `.`, `-`, `_`, `\` ou `/` ne sont pas corrigés. Le relevé de préparation utilise un lexer de chaînes ; le garde faisant foi en CI utilise Roslyn et XDocument.

Inventaire complet des mots ASCII extraits : [L6-2-releve-mots.csv](L6-2-releve-mots.csv), **1639 formes distinctes**, **10506 occurrences**, **3976 segments candidats**. Le CSV conserve aussi les données techniques et les noms propres pour rendre les exclusions visibles ; chaque ligne donne les références physiques du code avant correction. Répartition des segments : Virgil.App : 1662, Virgil.Contracts : 49, Virgil.Core : 1834, Virgil.ElevatedHelper : 136, Virgil.Infrastructure : 295.

Confrontation indépendante à la [liste de fréquences françaises de 50 000 mots de FrequencyWords](https://github.com/hermitdave/FrequencyWords/blob/master/content/2018/fr/fr_50k.txt) : décomposition Unicode FormD, retrait des NonSpacingMark, puis recherche des correspondants accentués de chaque mot sans accent. SHA-256 du fichier consulté : `f81f7c570b6433764da99aa30f4dfb08d81c5926301377b709af23a13b1f9596`. Le relevé initial donne **145 mots candidats** ayant un correspondant accentué dans ce corpus. Les **524 mots hors liste de fréquences** ont aussi été examinés (noms de produits, commandes, propriétés/protocole et termes de maintenance) : ils ne sont pas automatiquement considérés fautifs.

La fréquence n’est pas un correcteur : le corpus contient aussi du bruit (« ést », « içi », etc.) et des homographes valides. Aucune addition massive et aveugle au dictionnaire. Les mots valides tels que « cache », « charge », « commande », « compte », « date », « filtre », « pilote », « risque », « taille » restent inchangés. Les variables PowerShell, clés de protocole, noms Opera/Brave/Google, le service `sense`, les chemins `preferences` et la clé pnputil `Nom publie` restent inchangés.

## Résultat de la revue contextuelle

**70 littéraux corrigés** : tous les exemples du correctif demandé, les neuf occurrences ambiguës nommées, puis les découvertes supplémentaires (« Âge », « analysée », « boîte », « envisagé », « identifiés », « isolé », « Masqués », « placés », « préparé », « stabilité », « succès », « traitée », « incohérents », « revérification », « affichés », « exporté », « sûr » et les prépositions « à »).

Chaque mot fourni dans le correctif est ajouté avec ses variantes au dictionnaire. Les formes ayant un emploi valide sans accent (`trouve`, `estime`, `applique`, `invente`, `tronque`, `envisage`, `isole`, `places`, `identifies`, `masques`, `exporte`) sont justifiées dans la liste blanche ; les usages fautifs actuels sont protégés nommément. « Ou se trouve » reste valide pour le garde conformément à sa limite grammaticale documentée.

Recherche croisée avant correction : les textes corrigés sont des messages, libellés ou sorties de rapport, pas des valeurs de binding ni des clés. `SUCCES` provient d’un état enum lors du rendu (non relu comme clé) ; « Masques dans cet export » et le marqueur de troncature sont des sorties, sans parseur de ces phrases. Aucun nouveau statut sérialisé ni modification de clé JSON. Les tests citant ces sorties sont mis à jour ; les données techniques et les anciennes fixtures restent telles quelles.

Le contrôle du squelette C# (contenus de chaînes retirés) avant/après est identique pour chaque fichier source modifié ; le contrôle des attributs XAML non affichés, des bindings, x:Name, AutomationId et références de ressources est identique. Les conditions des gardes restent inchangées.

## Corrections, références avant modification

| Fichier:ligne | Avant | Après |
| --- | --- | --- |
| `Virgil.App/Hosting/GlobalExceptionHandler.cs:18` | Virgil doit se fermer suite a une erreur inattendue.\n | Virgil doit se fermer suite à une erreur inattendue.\n |
| `Virgil.App/MainWindow.xaml.cs:781` | SUCCES | SUCCÈS |
| `Virgil.App/MainWindow.xaml.cs:1134` |  a revoir ( |  à revoir ( |
| `Virgil.App/ViewModels/ApplicationItemViewModel.cs:28` | Taille estimee :  | Taille estimée :  |
| `Virgil.App/ViewModels/ApplicationsViewModel.cs:341` | Methode :  | Méthode :  |
| `Virgil.App/ViewModels/ApplicationsViewModel.cs:368` | Ne continuez que si vous avez sauvegarde ce qui doit l'être. | Ne continuez que si vous avez sauvegardé ce qui doit l'être. |
| `Virgil.App/ViewModels/ApplicationsViewModel.cs:370` | Confirmer malgre ce risque ? | Confirmer malgré ce risque ? |
| `Virgil.App/ViewModels/ApplicationsViewModel.cs:560` | Validation stricte appliquee avant lancement. | Validation stricte appliquée avant lancement. |
| `Virgil.App/ViewModels/ApplicationsViewModel.cs:605` | Methode :  | Méthode :  |
| `Virgil.App/ViewModels/CleanupViewModel.cs:526` | NETTOYAGE SUR - VALIDATION REQUISE | NETTOYAGE SÛR - VALIDATION REQUISE |
| `Virgil.App/ViewModels/CleanupViewModel.cs:533` | Age minimal :  | Âge minimal :  |
| `Virgil.App/ViewModels/CleanupViewModel.cs:573` | NETTOYAGE SUR | NETTOYAGE SÛR |
| `Virgil.App/ViewModels/CleanupViewModel.cs:591` | Aucune zone analysee. | Aucune zone analysée. |
| `Virgil.App/ViewModels/InterventionsViewModel.cs:638` | Intervention traitee.\nRapport disponible. | Intervention traitée.\nRapport disponible. |
| `Virgil.App/ViewModels/ReportsHistoryViewModel.cs:228` | Rapport exporte manuellement en TXT.\nAucun envoi en ligne. | Rapport exporté manuellement en TXT.\nAucun envoi en ligne. |
| `Virgil.App/ViewModels/ReportsHistoryViewModel.cs:278` | SUCCES | SUCCÈS |
| `Virgil.App/ViewModels/ResourcesViewModel.cs:359` | A utiliser seulement si l'application ne repond plus. | À utiliser seulement si l'application ne répond plus. |
| `Virgil.App/ViewModels/ResourcesViewModel.cs:371` | Validation explicite obligatoire. Aucune fermeture forcée automatique. Aucun redémarrage force. | Validation explicite obligatoire. Aucune fermeture forcée automatique. Aucun redémarrage forcé. |
| `Virgil.App/ViewModels/ResourcesViewModel.cs:432` | Aucun processus lourd selon les seuils V1. Principaux consommateurs affiches pour information. | Aucun processus lourd selon les seuils V1. Principaux consommateurs affichés pour information. |
| `Virgil.App/ViewModels/ResourcesViewModel.cs:437` | Aucun processus accessible a afficher. | Aucun processus accessible à afficher. |
| `Virgil.App/ViewModels/ResourcesViewModel.cs:622` | A SURVEILLER | À SURVEILLER |
| `Virgil.App/Views/ApplicationsView.xaml:114` | Aucune suppression automatique. Aucun dossier d'application supprime directement. | Aucune suppression automatique. Aucun dossier d'application supprimé directement. |
| `Virgil.App/Views/ReportsHistoryView.xaml:72` | Stockage local uniquement. Aucun envoi en ligne, aucune telemetrie, aucun export automatique. | Stockage local uniquement. Aucun envoi en ligne, aucune télémétrie, aucun export automatique. |
| `Virgil.App/Views/ReportsHistoryView.xaml:76` | Comparer deux scans : prevu en V2 | Comparer deux scans : prévu en V2 |
| `Virgil.App/Views/ResourcesView.xaml:119` | Aucune fermeture automatique. Processus critiques bloqués. Aucun redémarrage force. | Aucune fermeture automatique. Processus critiques bloqués. Aucun redémarrage forcé. |
| `Virgil.Contracts/Elevated/ElevatedPathGuard.cs:82` | Un fichier etait attendu. | Un fichier était attendu. |
| `Virgil.Contracts/Elevated/ElevatedPathGuard.cs:133` | Un dossier etait attendu. | Un dossier était attendu. |
| `Virgil.Contracts/ScanRules.cs:125` | A surveiller | À surveiller |
| `Virgil.Core/Applications/ApplicationInventoryService.cs:45` | Applications trouvees :  | Applications trouvées :  |
| `Virgil.Core/Applications/ApplicationRemnantClassifier.cs:53` | Reste AppData non classe : lecture seule et revue manuelle. | Reste AppData non classé : lecture seule et revue manuelle. |
| `Virgil.Core/Applications/ApplicationReportMapper.cs:45` | Methode :  | Méthode :  |
| `Virgil.Core/Applications/ApplicationUninstallCommandValidator.cs:60` | Commande dangereuse ou chainee bloquée. | Commande dangereuse ou chaînée bloquée. |
| `Virgil.Core/Applications/ApplicationUninstallService.cs:137` | Rapport final prepare. | Rapport final préparé. |
| `Virgil.Core/Cleanup/CleanupStorageAnalyzer.cs:166` | lecteur non verifiable | lecteur non vérifiable |
| `Virgil.Core/Cleanup/CleanupZoneCatalog.cs:18` | Bases de miniatures recreees par Windows. | Bases de miniatures recréées par Windows. |
| `Virgil.Core/Cleanup/CleanupZoneCatalog.cs:19` | Shaders temporaires recrees par les applications graphiques. | Shaders temporaires recréés par les applications graphiques. |
| `Virgil.Core/Cleanup/CleanupZoneCatalog.cs:24` | Cache de composants et telemetrie recreable. | Cache de composants et télémétrie recréable. |
| `Virgil.Core/Cleanup/CleanupZoneCatalog.cs:27` | Fichiers places volontairement dans la corbeille. Ils ne seront plus récupérables depuis Windows. | Fichiers placés volontairement dans la corbeille. Ils ne seront plus récupérables depuis Windows. |
| `Virgil.Core/Cleanup/CleanupZoneCatalog.cs:36` | Fichiers d'installation temporaires clairement identifies. | Fichiers d'installation temporaires clairement identifiés. |
| `Virgil.Core/Cleanup/CleanupZoneCatalog.cs:85` | Information uniquement. Aucun nettoyage n'est propose. | Information uniquement. Aucun nettoyage n'est proposé. |
| `Virgil.Core/Interventions/ElevatedHelperRequestStore.cs:248` | Noms des fichiers protocole incoherents. | Noms des fichiers protocole incohérents. |
| `Virgil.Core/Interventions/ExplorerRestarter.cs:56` | Relance Explorer annulée avant exécution complete. | Relance Explorer annulée avant exécution complète. |
| `Virgil.Core/Interventions/InterventionCatalog.cs:49` | Une a deux minutes | Une à deux minutes |
| `Virgil.Core/Interventions/InterventionDiagnosticService.cs:65` | Disponible. Aucune panne n'est inventee par le diagnostic. | Disponible. Aucune panne n'est inventée par le diagnostic. |
| `Virgil.Core/Interventions/InterventionReportBuilder.cs:17` | Reussites :  | Réussites :  |
| `Virgil.Core/Reports/ReportExportService.cs:44` | Masques dans cet export. | Masqués dans cet export. |
| `Virgil.Core/Reports/ReportExportService.cs:61` | Un fichier existe déjà. Confirmez son remplacement dans la boite de dialogue. | Un fichier existe déjà. Confirmez son remplacement dans la boîte de dialogue. |
| `Virgil.Core/Reports/ReportMapper.cs:19` | Nettoyage sur :  | Nettoyage sûr :  |
| `Virgil.Core/Reports/ReportMapper.cs:21` | Éléments personnels a revoir :  | Éléments personnels à revoir :  |
| `Virgil.Core/Reports/ReportMapper.cs:302` | A surveiller | À surveiller |
| `Virgil.Core/Reports/ReportSanitizer.cs:54` | \n[DÉTAILS TRONQUES] | \n[DÉTAILS TRONQUÉS] |
| `Virgil.Core/Resources/ProcessActionService.cs:249` | Processus protégé lors de la reverification. | Processus protégé lors de la revérification. |
| `Virgil.Core/Resources/ProcessProtectionPolicy.cs:80` | Processus installe dans Windows protégé. | Processus installé dans Windows protégé. |
| `Virgil.Core/Resources/ResourceRecommendationService.cs:83` | CPU a surveiller : aucun pic isole ne justifie une action automatique. | CPU à surveiller : aucun pic isolé ne justifie une action automatique. |
| `Virgil.Core/Resources/ResourceRecommendationService.cs:92` | RAM a surveiller : comparer avec les applications actuellement utilisées. | RAM à surveiller : comparer avec les applications actuellement utilisées. |
| `Virgil.Core/Resources/ResourceRecommendationService.cs:101` | Session Windows active depuis plusieurs jours : surveiller la stabilite. | Session Windows active depuis plusieurs jours : surveiller la stabilité. |
| `Virgil.Core/Scanning/SystemScanService.cs:181` | Applications a classification incertaine | Applications à classification incertaine |
| `Virgil.Core/Scanning/SystemScanService.cs:194` | Des applications representent un volume disque significatif. | Des applications représentent un volume disque significatif. |
| `Virgil.Core/Scanning/SystemScanService.cs:208` | Valider explicitement les applications marquees attention avant de lancer leur désinstalleur officiel. | Valider explicitement les applications marquées attention avant de lancer leur désinstalleur officiel. |
| `Virgil.Core/Scanning/SystemScanService.cs:307` | Des processus depassent les seuils CPU ou RAM du module Ressources. | Des processus dépassent les seuils CPU ou RAM du module Ressources. |
| `Virgil.Core/Scanning/SystemScanService.cs:404` | Valeurs mémoire incoherentes. | Valeurs mémoire incohérentes. |
| `Virgil.Core/Scanning/SystemScanService.cs:494` | Vérifier la connexion réseau si une connectivite est attendue. | Vérifier la connexion réseau si une connectivité est attendue. |
| `Virgil.Core/Scanning/SystemScanService.cs:516` |  utilise une quantite importante de mémoire physique. |  utilise une quantité importante de mémoire physique. |
| `Virgil.Core/Scanning/SystemScanService.cs:519` | Vérifier si cette consommation correspond a une application attendue. Aucun processus n'a été modifie. | Vérifier si cette consommation correspond à une application attendue. Aucun processus n'a été modifié. |
| `Virgil.Core/Scanning/SystemScanService.cs:579` | Un nettoyage pourra être envisage après validation explicite. | Un nettoyage pourra être envisagé après validation explicite. |
| `Virgil.Core/Scanning/SystemScanService.cs:639` | Certaines mises à jour touchent a des composants sensibles. | Certaines mises à jour touchent à des composants sensibles. |
| `Virgil.Core/Scanning/SystemScanService.cs:714` | Des diagnostics lecture seule suggerent une ou plusieurs interventions. | Des diagnostics lecture seule suggèrent une ou plusieurs interventions. |
| `Virgil.Core/Scanning/WindowsInfoReader.cs:24` | Edition Windows indisponible. | Édition Windows indisponible. |
| `Virgil.Core/Updates/UpdateRiskClassifier.cs:75` | Application utilisateur courante a faible risque. | Application utilisateur courante à faible risque. |
| `Virgil.Infrastructure/Windows/SingleInstanceGuard.cs:33` | L'instance ne detient pas le mutex. | L'instance ne détient pas le mutex. |

## Limites connues

Ce relevé couvre les littéraux du commit indiqué, pas les textes produits par Windows ou un logiciel tiers. Les homographes et les participes masculins demandent une analyse de la phrase : la liste blanche générale n’offre pas une vérification grammaticale exhaustive. Les assertions nominatives verrouillent les usages corrigés connus. Une liste de fréquences finie ne prouve pas à elle seule l’absence de toute faute future ; le dictionnaire, l’auto-alimentation et la revue contextuelle se complètent. Les résultats Windows et les 14 PNG régénérés sont cités dans la description de PR.

## Correctif b — revue grammaticale de la liste blanche sur `f019e6c`

Le contrôle reprend **les 62 mots** de la liste blanche de cette tête, dans tous les segments de littéraux C# et labels XAML des cinq assemblages. Recherche insensible à la casse, mots entiers, puis examen du contexte gauche : après `non`, `été`, `est`, `n'a`/`n’a`, `avez` (jusqu’à deux mots intermédiaires), ou après un mot se terminant par `s`/`x`, afin de repérer les emplois après un nom pluriel. Ce dernier critère est volontairement large : il sélectionne aussi « après », « sans » ou « Windows » ; la décision finale repose sur la phrase, pas sur cette seule terminaison. Les noms propres, clés, chemins et groupes regex sont vérifiés séparément.

**681 occurrences**, **36 mots présents**, **26 absents**, **20 candidats grammaticaux** après exclusion des mots-outils a/ou/la/des/du/sur/mais dans cette sélection prioritaire. Les occurrences des mots-outils sont néanmoins incluses dans le relevé et la revue générale. Les autres occurrences des mots ambigus ont été relues pour ne pas dépendre uniquement des deux motifs heuristiques.

### Résultats

- « Disques analyses : » est un participe qualifiant le nom pluriel : correction en « Disques analysés : », assertion nominative dans `Contextual_masculine_participles_remain_accented`.
- « Windows Update non confirme localement. » est un participe après `non` : correction en « non confirmé », assertion nominative.
- Les **cinq occurrences affichées** de Executable/executable (deux erreurs du validateur, le résultat et l’erreur d’absence dans ApplicationUninstallService, et le message WinGet) deviennent Exécutable/exécutable. Elles ont quatre assertions nominatives distinctes, plus une assertion de multiplicité exigeant les deux occurrences du service.
- Aucun autre emploi fautif découvert. Exemples conservés : « interfaces actives » et « applications actives » (adjectifs féminins), « disque système est valide » (adjectif), « Processus cible » (nom), « Inventaire pilotes retourne » (verbe), « Tous les comptes » (nom pluriel), « Session ressources sans analyse » (nom), « Analyses : » dans ResourceReportBuilder (nom désignant les analyses).
- `executable` sort de la liste blanche : le dictionnaire contient désormais `executable=exécutable` et `executables=exécutables`. Les deux paramètres `['Executable']` (ApplicationActionPreviewFactory et UpdateActionPreviewFactory) et le groupe de regex `Groups['executable']` restent strictement inchangés, avec trois exemptions nominatives exactes et trois tests exigeant leur présence inchangée. Le groupe nommé dans GeneratedRegex reste lui aussi inchangé et relève de la règle des jetons techniques.
- La recherche croisée de « Disques analyses » dans le dépôt ne trouve que le producteur ReportMapper (et les assertions ajoutées). Le chargement JSON de ReportHistoryService conserve SimpleView comme texte, ReportExportService le rend sans parser ce préfixe. Il n’existe aucune comparaison, StartsWith, découpe ou clé basée sur cet en-tête : les anciens rapports continuent donc à se lire sans migration ni modification de leurs données. La même vérification ne trouve aucun consommateur comparant « Windows Update non confirme localement. » ni les messages Exécutable corrigés.
- Aucun accès réel au registre n’est ajouté dans les tests : le libellé de WindowsUpdateStatusService est vérifié dans la source, sans appeler ReadStatus. Aucun changement de `.github`, de logique L6-1/L6+, de binding, de nom de membre, de clé JSON ou de paramètre.

### Candidats grammaticaux examinés (références de `f019e6c`)

| Fichier:ligne | Mot | Contexte | Décision |
| --- | --- | --- | --- |
| `Virgil.App/ViewModels/CleanupViewModel.cs:587` | sauvegardes | PROTÉGÉ Photos, vidéos, documents, projets, sauvegardes, jeux, applications, cloud, réseau, lecteurs externes et profils ambigus ne son | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.App/ViewModels/ReportsHistoryViewModel.cs:133` | limite |  rapport(s) local(aux), limite  | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.App/ViewModels/StartupViewModel.cs:95` | comptes | Tous les comptes | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.App/ViewModels/StartupViewModel.cs:127` | sauvegardes | ation n’a pas abouti. Actualisez pour vérifier l’état ; les sauvegardes sont conservées. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.App/ViewModels/UpdatesViewModel.cs:566` | analyse | Inventaire disponible après analyse approfondie ou examen manuel. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Core/Applications/ApplicationActionPreviewFactory.cs:26` | restes | Analyse lecture seule des restes. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Core/Applications/ApplicationUninstallService.cs:133` | restes | Analyse lecture seule des restes. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Core/Interventions/InterventionCatalog.cs:43` | actives | Libère puis renouvelle les baux DHCP des interfaces actives compatibles. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Core/Interventions/InterventionCatalog.cs:140` | valide | nible si chkdsk.exe est présent et si le disque système est valide. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Core/Reports/ReportMapper.cs:17` | analyses | Disques analyses :  | corriger : participe passé |
| `Virgil.Core/Reports/ReportMapper.cs:300` | analyse | Session ressources sans analyse. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Core/Resources/ProcessActionService.cs:64` | cible | Processus cible absent. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Core/Resources/ResourceRecommendationService.cs:101` | active | Session Windows active depuis plusieurs jours : surveiller la stabilité. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Core/Scanning/SystemScanService.cs:375` | actives | antanée : si elle reste élevée, identifier les applications actives les plus consommatrices. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Core/Updates/DriverInformationService.cs:37` | retourne | Inventaire pilotes retourne un statut non nul. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Core/Updates/WindowsUpdateStatusService.cs:47` | confirme | Windows Update non confirme localement. | corriger : participe passé |
| `Virgil.Core/Updates/WingetAvailabilityService.cs:71` | executable | WinGet non executable :  | corriger : adjectif français exécutable |
| `Virgil.Infrastructure/Windows/Startup/WindowsStartupInventorySource.cs:30` | comptes | Tous les comptes | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Infrastructure/Windows/Startup/WindowsStartupInventorySource.cs:33` | comptes | tous les comptes. | conserver : nom, adjectif valide ou verbe dans ce contexte |
| `Virgil.Infrastructure/Windows/Startup/WindowsStartupInventorySource.cs:53` | comptes | Tous les comptes · Registre Run ( | conserver : nom, adjectif valide ou verbe dans ce contexte |

### Couverture des 62 entrées de la liste blanche initiale

Le compteur inclut les références techniques lorsqu’elles sont des littéraux simples ; il ne les assimile pas à des libellés affichés. Les entrées absentes ne sont pas supprimées automatiquement, car elles documentent des ambiguïtés admises pour les futurs libellés. `executable` fait exception pour la raison indiquée ci-dessus.

| Mot | Occurrences | Décision de la revue |
| --- | ---: | --- |
| a | 30 | usages valides ou techniques conservés après revue |
| active | 22 | usages valides ou techniques conservés après revue |
| actives | 2 | usages valides ou techniques conservés après revue |
| analyse | 85 | usages valides ou techniques conservés après revue |
| analyses | 2 | Disques analysés à corriger ; Analyses (nom) conservé |
| annule | 0 | absent des segments contrôlés |
| applique | 0 | absent des segments contrôlés |
| autorise | 0 | absent des segments contrôlés |
| bloque | 1 | usages valides ou techniques conservés après revue |
| cible | 4 | usages valides ou techniques conservés après revue |
| cibles | 0 | absent des segments contrôlés |
| classe | 2 | usages valides ou techniques conservés après revue |
| classes | 0 | absent des segments contrôlés |
| complete | 0 | absent des segments contrôlés |
| comptes | 4 | usages valides ou techniques conservés après revue |
| concerne | 1 | usages valides ou techniques conservés après revue |
| confirme | 7 | non confirmé à corriger ; 6 formes verbales conservées |
| conserve | 0 | absent des segments contrôlés |
| demande | 7 | usages valides ou techniques conservés après revue |
| des | 87 | usages valides ou techniques conservés après revue |
| du | 100 | usages valides ou techniques conservés après revue |
| envisage | 0 | absent des segments contrôlés |
| estime | 0 | absent des segments contrôlés |
| executable | 8 | 5 messages à corriger ; 3 identifiants techniques conservés ; retrait de la liste blanche |
| exporte | 0 | absent des segments contrôlés |
| ferme | 0 | absent des segments contrôlés |
| force | 4 | usages valides ou techniques conservés après revue |
| forces | 0 | absent des segments contrôlés |
| identifies | 0 | absent des segments contrôlés |
| ignore | 0 | absent des segments contrôlés |
| initialise | 0 | absent des segments contrôlés |
| installe | 0 | absent des segments contrôlés |
| invente | 0 | absent des segments contrôlés |
| isole | 0 | absent des segments contrôlés |
| la | 112 | usages valides ou techniques conservés après revue |
| lance | 1 | usages valides ou techniques conservés après revue |
| limite | 2 | usages valides ou techniques conservés après revue |
| mais | 1 | usages valides ou techniques conservés après revue |
| marque | 0 | absent des segments contrôlés |
| masque | 3 | usages valides ou techniques conservés après revue |
| masques | 0 | absent des segments contrôlés |
| mesure | 10 | usages valides ou techniques conservés après revue |
| modifie | 2 | usages valides ou techniques conservés après revue |
| ou | 86 | usages valides ou techniques conservés après revue |
| passe | 2 | usages valides ou techniques conservés après revue |
| places | 0 | absent des segments contrôlés |
| propose | 0 | absent des segments contrôlés |
| publie | 1 | usages valides ou techniques conservés après revue |
| refuse | 0 | absent des segments contrôlés |
| reste | 25 | usages valides ou techniques conservés après revue |
| restes | 13 | usages valides ou techniques conservés après revue |
| retourne | 1 | usages valides ou techniques conservés après revue |
| sauvegarde | 22 | usages valides ou techniques conservés après revue |
| sauvegardes | 6 | usages valides ou techniques conservés après revue |
| supprime | 4 | usages valides ou techniques conservés après revue |
| sur | 12 | usages valides ou techniques conservés après revue |
| termine | 1 | usages valides ou techniques conservés après revue |
| tronque | 0 | absent des segments contrôlés |
| trouve | 0 | absent des segments contrôlés |
| utilise | 5 | usages valides ou techniques conservés après revue |
| valide | 6 | usages valides ou techniques conservés après revue |
| valides | 0 | absent des segments contrôlés |

Limite : il s’agit d’une revue contextuelle des littéraux de cette tête, pas d’un analyseur grammatical universel. Les autres homographes restent admis dans la liste blanche et leurs usages connus corrigés demeurent protégés par les assertions nominatives. Les preuves rouge/verte, les totaux et les 14 PNG de la tête finale sont cités dans la PR #71.
