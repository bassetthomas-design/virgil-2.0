# Lot A — Nettoyage sûr

Base exacte : `9b3621b0fcdefe43eacdf0f4bd90bdfcbc709fe3`. PR brouillon unique : [#77](https://github.com/bassetthomas-design/virgil-2.0/pull/77). Aucune fusion.
Les horaires d'arrêt du brief ont été annulés explicitement ; les autres limites restent applicables.
loops (relecture) a accepté la poursuite de R4, de la politique et des preuves malgré la non-reproduction de Téléchargements coché ; aucune permission P1 n'est attribuée à thoms.

## Cause et limites en trois lignes

1. R4 prouvé : zéro ligne éligible dans Téléchargements, mais case de catégorie sans condition de disponibilité ; le setter ignore ces lignes, donc la case active ne fait rien.
2. Téléchargements coché automatiquement : non reproduit sur cette base, y compris scan/rescan, progression, retour, groupes/catégories partagés et estimateur ; aucune restauration par clé ni reconstruction progressive trouvée.
3. PNG de `8d33d74` refusé par loops : les mesures limitées aux lignes masquaient le haut transparent rendu blanc ; fond racine/ScrollViewer désormais explicite, nouvelle mesure de tous les textes visibles sur hôte blanc et PNG opaque requis.

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
Les caches navigateurs ne sont plus présélectionnés, même si un fournisseur les classe Sûrs : choix prudent explicite de la liste blanche ; leur sélection volontaire reste possible.
Sans ligne éligible, sa case est masquée/désactivée et remplacée par « Choix ligne par ligne : fichiers personnels ».
Un choix personnel individuel est conservé lors d'un simple retour écran ; aucun choix n'est restauré après rescan. Tout désélectionner peut l'effacer.

## Transitions, progression et consentement

- Tests Core : scan réel via ports factices (quatre fichiers, 226,9 Mo), six dossiers connus personnels, liste blanche exhaustive et restrictions, identifiants inconnus refusés.
- Tests App : chaque zone marquée personnelle au niveau zone/candidat ; OldDownloads/RecycleBin sans marque ; scan complet, rescan, sélection globale, catégorie coche/décoche, retour, modules distincts avec/sans estimateur.
- Scan tenu par une barrière déterministe : gestes avant restitution du résultat, avec/sans notifications Cache Brave 324/422. Les anciens objets restent affichés pendant le scan ; les catégories partagent les lignes de Groups ; aucune ligne personnelle ajoutée. Après résultat, de nouveaux objets sont créés sans restauration de sélection.
- WPF : contrôles désactivés pendant rescan, case Téléchargements réellement décochée avant/après gestes. Aucun protocole de résultats partiels n'existe dans l'interface de scan actuelle ; le test « progressif » porte sur les notifications, sans inventer une API.
- PersonalFolderPolicy préserve les marques personnelles entrantes et marque OldDownloads/RecycleBin même sans chemin de dossier connu. Les gardes de chemins et l'éligibilité ne sont pas assouplis.
- Tests de consentement existant : choix individuel, aperçu Critical, refus sans texte/avec « oui », acceptation du texte exact via doubles sans effets OS ; dossiers connus également Critical. Ni ConfirmationService ni CleanupExecutionService ni ConfirmedPlan ne sont modifiés.
- Test d'exécution via double tenu par une barrière : après Nettoyer la sélection, gestes pendant/après exécution puis rescan/reconstruction, Téléchargements reste décoché et seul TEMP est transmis à l'exécution. Le consentement individuel critique est couvert séparément, sans toucher au moteur.
- La capture contient des compteurs « mis en quarantaine ». Ils peuvent indiquer un nettoyage préalable : hypothèse seulement, jamais cause prouvée. thoms ne se souvient pas des gestes ; ni clic individuel ni sélection automatique ne sont prouvés. Aucun geste n'est inventé.

## Preuves Windows

| État | Head tests | Run build.yml | Résultat |
| --- | --- | --- | --- |
| Rouge initial | `2d9e4dd9e41a24b6a06e10daae2b015ddfd270d1` | [42514787106](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42514787106) | Build 0 avertissement ; Core 1119/1119 ; App 575/576, assertion R4 |
| Rouge confirmé | `3f1d0d21c3507fd0f0a1e44c26c52a7bec15a0ec` | [42515088398](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42515088398) | Build 0 avertissement ; Core 1119/1119 ; App 577/578, même assertion R4 ; estimateur OK |
| Correctif séparé | `dadd936e6beb93642cba1e67459c91247333977c` | [42516026257](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42516026257) | R4 vert ; Core 1123/1123 ; App 586/587 : une fixture Chrome suppose encore une présélection |
| Vert après choix explicite dans cette fixture | `d56dc0508e3c76afccc75a25f78c885073e9aaaf` | [42516214815](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42516214815) | Succès complet : build 0 avertissement/0 erreur, Core 1123/1123, App 587/587, smoke UI 14/14, publication et installateur |
| Head effectivement relu par loops | `8d33d745995d0bfc25b634ac79ac212289a7f07f` | [42516576617](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42516576617) | CI verte, preuve visuelle refusée : le haut du PNG est blanc/transparent |
| Correction de l'opacité et preuve complète | `c51170a6c33b52062214232020bc64d9a20dd7ee` | [42516899613](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42516899613) | Build 0 avertissement/0 erreur, Core 1125/1125, App 588/588, UI 14/14 ; publication, artefacts et installateur réussis |

Le dernier complément ne modifie que ce document de preuve ; sa CI finale est référencée dans la PR avec son head exact. Une CI verte n'est pas un feu vert de revue : brouillon conservé, aucune fusion ce soir, revue lundi 9 h Paris.

Rouge authentique : `Downloads_category_without_eligible_rows_has_no_enabled_bulk_checkbox`, `Assert.DoesNotContain`, case visible et active (ligne 69 du commit rouge). Aucun échec de compilation/environnement. Le rouge ne reproduit PAS Téléchargements coché.
Commande Windows : `dotnet test Virgil.sln --configuration Release --no-build --filter "Category!=UI" --logger "console;verbosity=normal"` ; puis smoke existant `Category=UI` après publication.
Local Linux : `dotnet: command not found` ; aucune validation Windows revendiquée depuis Linux.

## Contraste et PNG

Les mesures historiques (17,42:1 normal, 15,78:1 sélectionné) portaient sur les lignes ; elles ne validaient PAS le haut du PNG transparent, refusé par loops sur `8d33d74`. R4 était vert, sans affaiblir son assertion. Nouvelle preuve complète exécutée sur `c51170a`, hôte blanc/noir : tous les textes visibles >= 4,5:1, opacité du PNG vérifiée (alpha 255).
Les conteneurs sombres sont limités aux catégories/lignes de CleanupView ; aucun thème global changé. Fond normal `#0A0F14`, sélection `#111B23`, survol `#0E151C`, texte `#F1F4F6` ; ratios de palette tous supérieurs à 15:1, rendu normal/sélectionné mesuré en WPF Windows.
[Nouvel artefact CI Virgil-ui-smoke](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42516899613/artifacts/13631379492), 2 949 210 octets, head `c51170a` : `lot-a/nettoyage-apres-scan.png` et `lot-a/nettoyage-conteneur-selectionne.png`, renouvelés et annoncés dans les logs. Vrai XAML WPF après scan, ports factices en lecture seule, Téléchargements visible et décoché, détails ouverts. Il s'agit d'une preuve de rendu déterministe, distincte du parcours FlaUI de l'exécutable publié (14 tests verts, 15 PNG contrôlés) et d'un essai sur le PC de la capture.

| Texte réellement rendu (deux états) | Contraste mesuré |
| --- | --- |
| Résumé « 226.9 Mo récupérables · 5 fichiers » | 18,06:1 |
| Risques « Sans risque : 10 o · À vérifier : 226.9 Mo » | 18,06:1 |
| Bandeau des exclusions de sélection globale/catégorie | 18,06:1 |
| Informations complémentaires (lecture seule) | 18,06:1 |
| Quarantaine | 18,06:1 |
| Détails de ligne, état sélectionné | 15,78:1 minimum |
| Minimum de tous les textes visibles mesurés (boutons secondaires inclus) | 6,40:1 |

Les assertions Windows et la génération PNG sont exécutées. La revue humaine des pixels reste à faire : le connecteur fournit l'artefact, mais son téléchargement vers l'atelier est refusé par le proxy (CONNECT 403). Aucun contournement ni changement de réseau/settings.
Le nouveau test vérifie aussi les ScrollViewer visibles opaques, la présence des cinq textes signalés et la case Téléchargements décochée. Les PNG historiques ne valent pas acceptation visuelle ; revue des nouveaux pixels par loops encore requise.
Les deux runs rouges sautent publication/smoke après échec Test, donc aucun PNG pour eux ; le warning d'artefact absent n'est pas un avertissement de compilation.
