# Lot A — Nettoyage sûr

Base exacte : `9b3621b0fcdefe43eacdf0f4bd90bdfcbc709fe3`. PR brouillon unique : [#77](https://github.com/bassetthomas-design/virgil-2.0/pull/77). Aucune fusion.
Les horaires d'arrêt du brief ont été annulés explicitement ; les autres limites restent applicables.

## Cause et limites en trois lignes

1. R4 prouvé : zéro ligne éligible dans Téléchargements, mais case de catégorie sans condition de disponibilité ; le setter ignore ces lignes, donc la case active ne fait rien.
2. Téléchargements coché automatiquement : non reproduit sur cette base, y compris scan/rescan, progression, retour, groupes/catégories partagés et estimateur ; aucune restauration par clé ni reconstruction progressive trouvée.
3. Blocs blancs : non reproduits sur le runner Windows ; conteneurs WPF locaux désormais explicitement sombres, avec contraste mesuré et PNG du vrai CleanupView alimenté par des ports factices.

## Politique unique et décisions exhaustives

`CleanupSelectionPolicy.IsSelectedByDefault` est la seule fonction de présélection utilisée par les lignes App.
Une décision positive exige aussi : exécutable, candidat éligible, classification nettoyable, groupe Sûr, nature non personnelle et aucun NeedsReview (zone ou candidat).
Le test compare exactement les clés de `DefaultDecisions` aux valeurs de `CleanupZoneId` : toute nouvelle valeur impose une décision explicite. Valeur inconnue/non listée : refus par défaut.

| CleanupZoneId | Présélection autorisée | Raison |
| --- | --- | --- |
| UserTemporaryFiles | Oui sous conditions | Temporaires techniques anciens |
| UserCrashDumps | Oui sous conditions | Dumps techniques anciens |
| DirectXShaderCache | Oui sous conditions | Cache recréable |
| WindowsTemporaryFiles | Oui sous conditions | Temporaires techniques Windows |
| WindowsThumbnailCache | Oui sous conditions | Miniatures recréables |
| WindowsErrorReports | Oui sous conditions | Rapports techniques anciens |
| TechnicalLogs | Oui sous conditions | Journaux techniques identifiés |
| BattleNetCache | Oui sous conditions | Cache recréable |
| VisualStudioCache | Oui sous conditions | Cache recréable |
| InternetCache | Oui sous conditions | Cache technique |
| BrowserEdgeCache | Non | Revue du cache navigateur |
| BrowserChromeCache | Non | Revue du cache navigateur |
| BrowserFirefoxCache | Non | Revue du cache navigateur |
| BrowserBraveCache | Non | Revue du cache navigateur |
| BrowserOperaCache | Non | Revue du cache navigateur |
| RecycleBin | Non | Personnel, suppression irréversible |
| WindowsUpdateCache | Non | Information, orchestration requise |
| DeliveryOptimizationCache | Non | Information, orchestration requise |
| MicrosoftStoreCache | Non | Information sans API de nettoyage fiable |
| InstallerTemporaryFiles | Non | Revue des restes d'installation |
| WindowsOld | Non | Outils Windows, non exécutable ici |
| PrefetchInformation | Non | Information, nettoyage déconseillé |
| OldDownloads | Non | Téléchargements personnels, gros fichiers ambigus |
| SoftwareLeftovers | Non | Restes à examiner |

La sélection volontaire globale/par catégorie reste possible pour les lignes techniques éligibles, y compris caches navigateurs et restes non personnels ; elle n'ajoute jamais de personnel. La catégorie est indéterminée quand seule une partie des lignes éligibles est sélectionnée.
Sans ligne éligible, sa case est masquée/désactivée et remplacée par « Choix ligne par ligne : fichiers personnels ».
Un choix personnel individuel est conservé lors d'un simple retour écran ; aucun choix n'est restauré après rescan. Tout désélectionner peut l'effacer.

## Transitions, progression et consentement

- Tests Core : scan réel via ports factices (quatre fichiers, 226,9 Mo), six dossiers connus personnels, liste blanche exhaustive et restrictions, identifiants inconnus refusés.
- Tests App : chaque zone marquée personnelle au niveau zone/candidat ; OldDownloads/RecycleBin sans marque ; scan complet, rescan, sélection globale, catégorie coche/décoche, retour, modules distincts avec/sans estimateur.
- Scan tenu par une barrière déterministe : gestes avant restitution du résultat, avec/sans notifications Cache Brave 324/422. Les anciens objets restent affichés pendant le scan ; les catégories partagent les lignes de Groups ; aucune ligne personnelle ajoutée. Après résultat, de nouveaux objets sont créés sans restauration de sélection.
- WPF : contrôles désactivés pendant rescan, case Téléchargements réellement décochée avant/après gestes. Aucun protocole de résultats partiels n'existe dans l'interface de scan actuelle ; le test « progressif » porte sur les notifications, sans inventer une API.
- PersonalFolderPolicy préserve les marques personnelles entrantes et marque OldDownloads/RecycleBin même sans chemin de dossier connu. Les gardes de chemins et l'éligibilité ne sont pas assouplis.
- Tests de consentement existant : choix individuel, aperçu Critical, refus sans texte/avec « oui », acceptation du texte exact via doubles sans effets OS ; dossiers connus également Critical. Ni ConfirmationService ni CleanupExecutionService ni ConfirmedPlan ne sont modifiés.
- La capture contient des compteurs « mis en quarantaine ». Sur cette base, CleanupPreviewService ne les émet pas ; CleanupExecutionService les émet pendant l'exécution. Cela ne prouve ni l'état exact ni la version de l'application photographiée. Version/commit et gestes précédents restent nécessaires pour reproduire ce cas.

## Preuves Windows

| État | Head tests | Run build.yml | Résultat |
| --- | --- | --- | --- |
| Rouge initial | `2d9e4dd9e41a24b6a06e10daae2b015ddfd270d1` | [42514787106](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42514787106) | Build 0 avertissement ; Core 1119/1119 ; App 575/576, assertion R4 |
| Rouge confirmé | `3f1d0d21c3507fd0f0a1e44c26c52a7bec15a0ec` | [42515088398](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42515088398) | Build 0 avertissement ; Core 1119/1119 ; App 577/578, même assertion R4 ; estimateur OK |
| Après correctif séparé | À renseigner après CI | En attente | Aucun résultat anticipé |

Rouge authentique : `Downloads_category_without_eligible_rows_has_no_enabled_bulk_checkbox`, `Assert.DoesNotContain`, case visible et active (ligne 69 du commit rouge). Aucun échec de compilation/environnement. Le rouge ne reproduit PAS Téléchargements coché.
Commande Windows : `dotnet test Virgil.sln --configuration Release --no-build --filter "Category!=UI" --logger "console;verbosity=normal"` ; puis smoke existant `Category=UI` après publication.
Local Linux : `dotnet: command not found` ; aucune validation Windows revendiquée depuis Linux.

## Contraste et PNG

Avant correctif, rendu WPF testé : 17,42:1 à 18,06:1, sans reproduction du fond blanc. Après correctif : mesure et revue PNG en attente de CI.
Les conteneurs sombres sont limités aux catégories/lignes de CleanupView ; aucun thème global changé. Fond normal `#0A0F14`, sélection `#111B23`, survol `#0E151C`, texte `#F1F4F6` ; ratios de palette tous supérieurs à 15:1, à confirmer sur le rendu effectif.
Artefact prévu `Virgil-ui-smoke/lot-a/nettoyage-apres-scan.png` et `nettoyage-conteneur-selectionne.png` : vrai XAML WPF après scan, ports factices en lecture seule, Téléchargements visible et décoché, détails ouverts. Il s'agit d'une preuve de rendu déterministe, distincte du parcours FlaUI de l'exécutable publié et d'un essai sur le PC de la capture.
Les deux runs rouges sautent publication/smoke après échec Test, donc aucun PNG pour eux ; le warning d'artefact absent n'est pas un avertissement de compilation.
