# Lot T3c — Résumé de la sélection et informations complémentaires

## Périmètre

Branche `codex/lot-nettoyage-infos`, issue de `main` à `7193729a5bc7ea17514743cbd8d5a12837d449ae` après T3b. Les trois fichiers de production, le nouveau fichier de tests et cette preuve constituent les cinq fichiers du lot. La sélection, les confirmations et le parcours d’exécution conservent leur comportement. [PR #96](https://github.com/bassetthomas-design/virgil-2.0/pull/96)

Le résumé sous les totaux d’analyse reflète les zones éligibles cochées, leur nombre de fichiers et leur taille. Il signale la suppression définitive lorsque la Corbeille est incluse. Les cinq sections complémentaires réutilisent les objets d’`Entries`, dans leur ordre d’origine. Le total de « À revoir manuellement » compte les cartes d’éléments affichées, dans la limite de 20. Les volumes des sections de zones sont calculés à partir des aperçus que la politique autorise à sélectionner.

## CI Windows — tests seuls

Commit `0b7146db00a03317c7ddf3d9db4c180fbb4c4d3e`. Le workflow `build.yml` sur `windows-latest` compile avec **0 avertissement et 0 erreur**. Les deux nouveaux tests C1 et C2 échouent sur la vraie vue WPF. [Run #469, job build](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44313083976/job/134831993417)

| Test | Message d’assertion du run rouge et valeurs |
| --- | --- |
| C1 · `Rendered_selection_summary_follows_every_check` | `Assert.Contains() Failure: Item not found in collection`. Attendu parmi les textes visibles : `Sélection : 1 zone(s) · 2 fichiers · 2 Ko`. Obtenu : collection des textes visibles sans ce résumé. Le journal affiche notamment `NETTOYAGE COMPLET GUIDÉ V2` et `NETTOYER LA SÉLECTION`. |
| C2 · `Rendered_additional_information_is_split_into_collapsed_sections_with_totals` | `Assert.Equal() Failure: Collections differ`. Attendu : `[Nettoyage sûr — 1 zone(s) · 2 Ko sélectionnables, Nettoyage avancé — 3 zone(s) · 3 Ko sélectionnables, Nettoyage technique bloqué, À revoir manuellement — 0 élément(s), Protégé]`. Obtenu : `[]`. xUnit tronque le deuxième en-tête dans son journal. |

Le test existant `L6AppBehaviorTests.Escape_closes_diagnostic_validation_even_when_process_grid_has_focus` a levé une `TimeoutException` au bout de 5 s dans `tests/Virgil.App.Tests/L6BehaviorTests.cs:80`. virgil (relecture) a identifié ce test Diagnostic comme déjà fragile sur le runner et a validé le run #469 comme rouge par assertion pour C1 et C2. Le test Diagnostic a été conservé à l’identique.

## Tests verts dès le départ (gardes)

**G-C1** · `Entries_are_unchanged_by_sections` passe au run #469. L’égalité de la liste des types couvre les neuf objets et leur ordre, dont les deux titres, les quatre cartes de zone et les trois cartes de texte.

## CI Windows — correctif et référence finale

Le correctif `5a8050d1b00e92af9dd8d429aa171d8a67fbc6c4` passe avec **0 avertissement et 0 erreur**. Core : 1319/1319, App : 861/861, UI : 14/14. C1, C2 et G-C1 passent, ainsi que le test Diagnostic dont le délai a été dépassé au run #469. [Run Windows #470](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44320869671)

C2 écrit `lot-t3c/sections.txt` en UTF-8 sans BOM avant les assertions sur les en-têtes, avec l’en-tête et le nombre de cartes de chaque section. [Artefact Virgil-ui-smoke 14434931962](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44320869671/artifacts/14434931962)

La section 5 de la PR porte la référence du run final sur le commit qui contient cette preuve, avec son SHA et l’ID de l’artefact `Virgil-ui-smoke`. [PR #96](https://github.com/bassetthomas-design/virgil-2.0/pull/96)

## Vérifications manuelles Windows restantes

1. Dans **Nettoyage**, lancer **ANALYSER NETTOYAGE COMPLET** sur un profil d’essai et un jeu de données de plusieurs centaines de lignes. Réduire la fenêtre et vérifier que **NETTOYER LA SÉLECTION** reste visible en haut.
2. Ouvrir **Informations complémentaires (lecture seule)**. Déplier puis replier chaque section. Dans une section dont les cartes dépassent la hauteur de la liste, utiliser la molette au-dessus des cartes et vérifier que le défilement reste dans la liste.
3. Utiliser **TOUT SÉLECTIONNER**, modifier les cases individuellement (dont la Corbeille si elle contient des éléments), puis utiliser **TOUT DÉSÉLECTIONNER**. Vérifier à chaque étape les zones, les fichiers, la taille et la mention de la Corbeille. Arrêter le parcours à l’aperçu.

## Liste de contrôle

1. Totaux par section : C2 vérifie chaque en-tête sur les données de sa section.
2. Statut « Terminé » sous un échec : sans objet pour ce changement d’affichage.
3. Contenu des sections : les cinq sections utilisent les cartes d’`Entries`. C2 vérifie leurs effectifs et G-C1 vérifie les objets de la liste historique.
4. Cas partiel : C1 couvre la sélection par défaut, l’ajout, le retrait et la remise de la Corbeille. C2 couvre une zone visible dont le volume est exclu du total sélectionnable.
5. Vue réelle : C1 et C2 ouvrent `CleanupView` sur un thread STA et utilisent le vrai ViewModel avec les fixtures existantes.
6. Rouge puis vert : les runs de tests seuls, du correctif et du head final sont référencés dans cette preuve et dans la PR.
7. Feu vert du relecteur : la décision de virgil porte sur le rouge #469.
8. État de la PR : brouillon.
