# Lot G — Signaler un problème

Périmètre : bouton dans l'en-tête, issue GitHub préremplie dans le navigateur, rien d'envoyé par Virgil. Base exacte : `524fcc243d457b51dd95f94a3b72d01c2a7ce40b`. Décisions : loops (relecture), brief G1–G12 du 16/11/2026. Une seule PR, [#87](https://github.com/bassetthomas-design/virgil-2.0/pull/87), conservée en brouillon ; aucune fusion ni demande de relecture à ce stade.

## Séquence Windows

- Tentative initiale tests seuls : `0d2efcbec8d8f73eaf79e494e99b10f5fc2a4a9c`, [run 43845576054](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43845576054). Compilation refusée par xUnit2013 (vérification d'une collection singleton par Assert.Equal). Ce run ne constitue PAS la preuve rouge d'assertion demandée. Aucun test exécuté, aucun PNG ; avertissement de l'action d'upload car aucun artefact.
- Correction des tests seuls : `8170bcb3fcba99eddd440b5e74b2433e8f08a64a`, [run 43846077675](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/43846077675). Assert.Single remplace l'assertion d'égalité du compteur, sans changement d'attente : une seule occurrence de la lecture autorisée. Compilation réussie : 0 avertissement, 0 erreur. Rouge authentique : 18 nouveaux cas App échouent par assertions (types et fichiers absents) et E1 échoue par comparaison de l'inventaire. Core : 1248 réussis / 1249, App : 807 réussis / 825 ; UI non exécutée. Aucun autre test existant en échec. L'action d'upload avertit de l'absence de fichiers, attendue avant le correctif.
- Correctif de production : commit séparé après vérification du rouge d'assertion. Le run vert FINAL, son SHA de tête et l'ID de l'artefact `Virgil-ui-smoke` seront inscrits dans la description de la PR avant toute demande de relecture, conformément à M12. Aucun run rouge n'est une validation finale.

Aucun SDK dotnet disponible dans l'environnement Linux ; aucune compilation ni exécution WPF locale revendiquée. La CI Windows fait foi. Totaux Core/App/UI et avertissements : à relever dans les logs Windows finaux.

## Exception E1 (mot pour mot)

E1 tests/Virgil.Core.Tests/LegacyEffectArchitectureTests.cs, méthode `ProcessRunnerConsumersRemainExplicitlyInventoried`, liste App (l.92-93) : ajout de « BugReportViewModel.cs » à sa place dans l'ordre Ordinal, et rien d'autre. Lignes attendues :
`        Assert.Equal(new[] { "ApplicationsViewModel.cs", "BugReportViewModel.cs", "CleanupEntryItems.cs", "CleanupViewModel.cs",`
`            "HomePrioritiesViewModel.cs", "ServiceCollectionExtensions.cs", "UpdatesViewModel.cs", "VirgilReleaseViewModel.cs" }, app);`
La liste Core (l.87-89), les autres méthodes et le reste du fichier ne changent pas. Ce changement va dans le commit de tests seuls (il y est rouge tant que BugReportViewModel.cs n'existe pas : rouge d'assertion authentique). Lot-G.md le cite.

Aucun autre test existant modifié. Aucun ajout au dictionnaire d'accents. La liste de contrôle externe `/workspace/overmind/revues/liste-controle-virgil.md` est absente de cet environnement ; les huit points et leçons reproduits dans le brief constituent la référence disponible. Aucun statut de carte modifié ; aucun succès affiché sous un échec ; aucune création sans clic ; les trois échecs puis le succès sont testés ; les chemins réels sont utilisés ; aucun feu vert inventé, aucune sortie du brouillon.

## Exigences et preuves préparées

| Exigence | Test nouveau |
| --- | --- |
| R1, R8, R10, P3, M11 : vrais textes, accessibilité, thème, contraste et PNG | LotGPanelTests.Real_control_with_theme_has_exact_labels_copyable_failure_and_no_startup_effect |
| R2, R10 : URL, ordre des paramètres, encodage et annexes exactes | LotGLinkTests.Exact_url_title_body_and_encoding |
| R3 : versions, révision absente et source applicative réelle | Windows_format_omits_revision, Version_uses_real_service_and_filters, Long_version_is_unavailable |
| R3 : valeurs personnelles et sentinelles absentes | No_identifying_values_in_decoded_url |
| R4 : 2000, borne exacte, moins un, 400, impossible, lignes entières et %XX | Truncation_preserves_whole_lines_title_and_labels |
| R5 : onze écrans, ordre du vrai XAML, aucun/deux/inconnu et casse | Screen_list_matches_real_navigation_and_requires_exactly_one_known_active |
| R6 : port unique, requête exacte, double clic et désactivation | Launch_uses_only_port_and_double_execution_is_guarded |
| R7 : constructeur unique, DI singleton hors STA | Constructor_and_singleton_composition_are_explicit |
| R8 : exception, Started faux, LaunchError puis succès | Every_failure_displays_exact_url_then_success_clears |
| R7, R9, R11 : absence d'effets, composition et branchement | Production_sources_have_no_forbidden_effects_and_wiring_is_in_views_only |
| P4 : construction du vrai panneau et changement de ScreenSource sans appel | LotGPanelTests et Launch_uses_only_port_and_double_execution_is_guarded |
| R11, R12 : non-régression | Tous les tests existants et Virgil.UiTests, inchangés sauf E1 |

Préparation : vrai ApplicationVersionService sur faux IApplicationVersionSource ; Version déterministe ; BugReportScreen.From sur paires et vrai MainWindow.xaml ; faux IProcessRunner dont RunAsync lève et LaunchAsync enregistre ; contrôle réel en STA via ThemeContrastTests.OnSta, ressources de l'application chargées. Aucune réflexion sur champ privé. Les recherches de sources sont des sous-chaînes Ordinal sensibles à la casse ; runas et valeurs réelles sont OrdinalIgnoreCase. Les regex IPv4/MAC ont les limites exigées par P1 ; aucun terme interdit ne vise les annexes.

Captures attendues dans `Virgil-ui-smoke/lot-g/` : `bouton-repos.png`, `echec-lien.png` (octets différents, contrôle réel avec thème) et `exemple-lien.txt` (URL P1 et corps décodé). Le test vérifie le champ entier, sa hauteur sans défilement, sa présence dans le cadre et les contrastes ≥ 4,5:1.

Longueur de l'exemple maximal P1 (version de 64 caractères, Windows 11 10.0.22631, écran non déterminé) : **831 caractères**, recalcul indépendant Python avec échappement équivalent ; ce calcul n'est pas une exécution C#. Le test C# vérifie la longueur ≤ 2000 et l'absence de troncature à cette borne. Le titre et labels sont conservés lors de la troncature.

## Fichiers

Production : MainWindow.xaml.cs (injection, ajout après releaseView, lecture de l'écran au clic) ; Hosting/ServiceCollectionExtensions.cs (import du contrôle, AddVirgilViews et fabrique MainWindow ; AddVirgilServices inchangé) ; nouveaux ViewModels/BugReportLink.cs, BugReportScreen.cs, BugReportViewModel.cs et Controls/BugReportPanel.xaml, BugReportPanel.xaml.cs.

Tests : nouveaux LotGLinkTests.cs, LotGViewModelTests.cs, LotGPanelTests.cs ; exception E1 seule dans LegacyEffectArchitectureTests.cs. Documentation : ce fichier et une ligne dans PROJECT_STATUS.md. Aucun fichier généré, binaire, paquet, thème, workflow, vue existante, MainWindow.xaml ou Core de production modifié.

## Limites

Ouverture réelle du navigateur, affichage de la page GitHub et application de l'étiquette bug à vérifier sur le PC à l'alpha.8. GitHub peut ignorer l'étiquette selon les droits. Aucun lancement réel de navigateur dans les tests. Rendu global et vitesse du texte à vérifier à l'alpha.8 ; les captures du panneau ne prouvent pas tous les écrans ni une mesure de performance. Le feu vert de loops (relecture) et la fusion restent hors de cette exécution.
