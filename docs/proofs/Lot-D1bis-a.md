# Lot D1-bis-a — Microsoft Store et logiciels sans source automatique

## Périmètre livré

Base exacte : `44cb0f52802fa503d5e6adc9cb57b91f2a1f430a`. Branche : `codex/lot-d1bis-a`. PR brouillon : [#81](https://github.com/bassetthomas-design/virgil-2.0/pull/81). Aucune fusion.

`UpdateSequenceService` utilise le même plan confirmé et la préparation Microsoft protégée par le point de restauration avant les installations Store. Le moteur de confirmation et le helper ne sont pas modifiés. Les paquets sont figés dans l'aperçu ; une recherche finale ne déclenche aucune installation supplémentaire. Les applications déjà présentées par la source Microsoft Store de l'inventaire applicatif sont exclues de la passe Store directe. L'annulation attend l'application courante puis empêche la suivante. Aucun redémarrage Windows automatique.

`StoreWindowsClient` utilise le pont WinRT fourni par Windows PowerShell, derrière `IStoreUpdateClient`, sans changement de framework ni nouveau paquet. La recherche utilise `SearchForAllUpdatesAsync` avec téléchargement/installation automatique et redémarrage forcé de l'application désactivés. L'installation explicite utilise l'équivalent officiel `UpdateAppByPackageFamilyNameAsync`, puis `GetCurrentStatus`. Aucune fenêtre Store n'est ouverte. Le code relit `Get-AppxPackage` par famille avant et après, puis effectue un nouveau contrôle. Une version supérieure ou la disparition de la proposition avec un paquet encore présent prouve la mise à jour. L'échec et l'indisponibilité restent distincts du blocage par une garde de sécurité.

L'inventaire hors sources automatiques ouvre les clés Uninstall HKLM/HKCU dans les vues 32/64 bits avec `writable=false`. Il ne lit ni ne lance les désinstalleurs. Il compare les noms à l'inventaire applicatif installé et aux paquets Store, exclut les entrées sans nom, composants système et lanceurs hors lot. Un inventaire incomplet reste explicitement incomplet. Les logiciels sans source connue figurent au bilan avec nom, version et raison, sans lien ni téléchargement.

La ligne de source conserve le reste et l'échec ou le blocage (R7). Les cartes de séquence n'affichent plus la ligne « Identité » ; la clé technique `Component:Id` reste sur le modèle. Le bilan et les libellés de la séquence utilisent « Applications » (R8).

## Séquence rouge puis correctif

Le commit initial `6b2e65bd0342727efb3f19a16e4d1cc8fb41fe74` contient uniquement les tests et les ports nécessaires avec des implémentations vides, sans logique métier corrective.

[Run rouge 42629829626, n°353](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42629829626), head `6b2e65bd0342727efb3f19a16e4d1cc8fb41fe74` : build Windows réussi, zéro avertissement et zéro erreur de compilation. Core : 1 165 tests, 1 163 réussis, 2 échecs. App : 666 tests, 648 réussis, 18 échecs. Les échecs comprennent les vraies assertions Store, inventaire, R7 et R8 ; aucune panne d'environnement ou compilation. UI smoke non exécuté puisque l'étape Test échoue.

Le correctif de production est dans le commit séparé `7b901a22a863611fb2ebd3ce9d89efd3a127cdf1`. Le commit suivant ajoute des tests des adaptateurs et renforce la preuve, sans modifier les tests du commit rouge.

Les adaptations de `tests/Virgil.App.Tests/UpdatesSequenceViewModelTests.cs` et de `tests/Virgil.App.Tests/D1WingetIdentityTests.cs` sont autorisées par virgil (coordination du lot), pour R7/R8 uniquement, conformément à loops (relecture). Les identités sont vérifiées par clé de modèle plutôt que par texte affiché. Aucun autre contrôle n'est retiré. L'homonyme Core mentionné dans la liste initiale ne correspondait pas au chemin réel du second fichier.

## Captures et validation Windows

Les tests `D1bisAPanelTests` rendent le vrai `OperationProgressPanel` WPF sur un thread STA avec le vrai service et le vrai ViewModel ; les seules sources sont factices. Ils produisent dans l'artefact `Virgil-ui-smoke` :

- `lot-d1bis-a/updated.png` : version Store augmentée ;
- `lot-d1bis-a/unsupported.png` : API refusée et raison ;
- `lot-d1bis-a/mixed.png` : application mise à jour, autre en échec et toujours proposée ;
- `lot-d1bis-a/outside.png` : logiciel sans source automatique, sans doublon connu.

Les runs du correctif [42671671975, n°354](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42671671975), head `7b901a22a863611fb2ebd3ce9d89efd3a127cdf1`, et [42671967402, n°355](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42671967402), head `8dc2020882da9ee35719cfc97cd83c831ea8b922`, se terminent en échec **avant tout job**. Leur annotation GitHub est « Internal server error », avec les identifiants de corrélation respectifs `701e9c76-419d-4c48-a06d-9af866112d25` et `68242dca-fdf7-4ce7-aaa5-78a00e18a728`. Zéro job est retourné par l'API GitHub. Aucun build, total de tests, avertissement ou PNG du correctif ne peut être déduit de ces deux tentatives initiales. La relance suivante et la validation finale sont détaillées ci-dessous.

Après relance, le [run 42672782830, n°356](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42672782830), head `48f016ce41f3cc79f88ac6c8dc40a0eebb14c230`, exécute effectivement le build Windows avec zéro avertissement. Il révèle cinq échecs : quatre attentes de l'ancien chemin Store dans `L6PlusExecutionTests` et le garde d'accents sur le paramètre PowerShell « operation ».

L'adaptation limitée de `tests/Virgil.Core.Tests/L6PlusExecutionTests.cs` est autorisée par virgil (coordination du lot), conformément à loops (relecture) : injection d'une API Store factice, scan réel pour figer le paquet, retrait du jeton `store` du groupe du helper et libellé d'indisponibilité actualisé. Toutes les assertions existantes de sécurité, d'échec et de nombre d'élévations sont conservées. Le faux helper continue à simuler les échecs Windows ; les échecs et annulations Store viennent désormais de l'API factice. Le paramètre du script embarqué est renommé « opération » avec son accent. Ces corrections sont ajoutées par de nouveaux commits, sans réécriture d'historique.

## Preuve verte

[Run vert 42676297087, n°357](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42676297087), head `e532f6130ea7a70433839217cf34fe1ac09993a2` : toutes les étapes de `build.yml` réussissent, y compris build, tests, publication, UI smoke et installateur. Le build indique **0 avertissement, 0 erreur** ; aucune autre alerte de compilation n'est présente dans le journal.

| Ensemble | Réussis | Échecs |
| --- | ---: | ---: |
| Core | 1 169 | 0 |
| App | 666 | 0 |
| UI smoke | 14 | 0 |

Les cinq scénarios `D1bisAPanelTests` passent, y compris l'annulation ; les quatre scénarios de capture régénèrent leurs PNG dans `artifacts/ui-smoke/lot-d1bis-a/`. [Artefact Virgil-ui-smoke du run vert](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42676297087/artifacts/13701405709), identifiant `13701405709`, empreinte d'archive `cdae7a0239e63ddaeb0ca8281d2142ccb4adc0d4f0fef976e98adebc01a10172`. Les images sont des rendus du vrai panneau WPF/STA avec sources factices. Pas de revue visuelle locale revendiquée : l'accès de l'environnement à l'URL temporaire de téléchargement de l'archive est refusé par le proxy ; les tests de génération et l'envoi d'artefact sont réussis sur Windows.

Les assertions du commit rouge `6b2e65b` restent inchangées. Le commit correctif `e532f61` adapte uniquement le test hérité autorisé et corrige l'accent du script ; cette documentation est complétée ensuite par un commit distinct, sans réécriture d'historique. La PR demeure en brouillon et n'est pas fusionnée.

## Limites explicites

Aucune installation réelle ni manipulation du registre Windows n'a été exécutée dans l'environnement Linux. Les captures STA factices prouvent la présentation et l'orchestration, pas une installation Store réelle. La preuve sur PC reste prévue pour alpha.8, conformément à loops (relecture).

La [documentation Microsoft d'AppInstallManager](https://github.com/MicrosoftDocs/winrt-api/blob/docs/windows.applicationmodel.store.preview.installcontrol/appinstallmanager.md) précise que son accès est protégé par une capacité privée réservée aux applications Microsoft. Ce lot ne contourne pas cette restriction : un refus sur le PC produit « non pris en charge automatiquement » et une raison en français. La réussite d'une source factice ne garantit donc pas l'autorisation de l'API sur un PC réel.

Méthodes officielles : [recherche](https://github.com/MicrosoftDocs/winrt-api/blob/docs/windows.applicationmodel.store.preview.installcontrol/appinstallmanager_searchforallupdatesasync_80105362.md), [installation par famille](https://github.com/MicrosoftDocs/winrt-api/blob/docs/windows.applicationmodel.store.preview.installcontrol/appinstallmanager_updateappbypackagefamilynameasync_2125212473.md). L'attente WinRT et la consultation périodique de l'état restent confinées au processus PowerShell exécuté par `IProcessRunner` avec délai ; le code managé n'attend pas une tâche de façon synchrone.
