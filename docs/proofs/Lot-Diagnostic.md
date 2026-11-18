# Lot T5a — Diagnostic

Le retour de test alpha.8 signale que l’écran annonce l’absence de problème avant la fin du premier relevé, et que les actions et les signaux indisponibles sont difficiles à repérer. Le haut de l’écran explique l’usage du Diagnostic, distingue l’attente du premier relevé d’un relevé sans alerte, puis affiche l’heure, la note et le nombre de signaux indisponibles. Les trois premiers constats au plus reprennent leurs recommandations et leurs boutons dans le même ordre que la liste complète. Le bouton « Actualiser le diagnostic » relance la lecture. Les huit sections sont repliées sous « Détails techniques ».

## CI Windows

- Tests seuls, SHA `e8604a02b54c8f405616cb84070d291117ada77a`. Compilation réussie avec zéro avertissement et zéro erreur. Les trois seuls échecs sont les assertions A1, A2 et A3 ci-dessous. G-1 et G-2 ont réussi. [Run #477](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44343149856)
- Correctif, SHA `d2f8c5071e182daabafd96a3569df5f6c48ec6fa`. Zéro avertissement et zéro erreur. Les 2 196 tests hors smoke UI et les 14 smoke tests ont réussi, dont A1 à A3, G-1 et G-2 dans la première suite. [Run #478](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44343815057) · [Artefact Virgil-ui-smoke, ID 14445018227](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44343815057/artifacts/14445018227)
- La [description de la PR #98](https://github.com/bassetthomas-design/virgil-2.0/pull/98) consigne le run Windows final, le SHA de tête testé et l’identifiant de l’artefact `Virgil-ui-smoke`.

## Exigences et assertions du run de tests seuls

Les extraits reprennent la troncature du journal xUnit (`···`). Les tests sont dans `LotDiagnosticTests`.

| Exigence | Test | Assertion du run #477 |
| --- | --- | --- |
| R-1 · État avant le premier relevé, heure, note et mesures partielles | A1 `Start_healthy_and_partial_readings_say_what_was_measured` | `Assert.Equal() Failure: Strings differ`<br>Expected `"Constats disponibles après le premier rel"···`<br>Actual `"Aucun problème détecté dans les signaux d"···` |
| R-1 · Trois constats prioritaires avec leurs recommandations et actions | A2 `Loaded_reading_puts_three_findings_with_their_action_first` | `Assert.Equal() Failure: Strings differ`<br>Expected `"À vérifier en priorité"`<br>Actual `"Top 3 des problèmes"` |
| R-2, R-3 · Phrase d’usage, résultat et cartes prioritaires, bouton d’actualisation et détails repliés | A3 `Rendered_screen_explains_use_shows_result_first_and_folds_details` | `Assert.Contains() Failure: Item not found in collection`<br>Not found `"Mesurer ce qui ralentit le PC et savoir quoi vérif"···`<br>Collection `["Diagnostic", "Aucune action sans votre validation", "Note non calculée", "Aucun problème détecté dans les signaux disponible"···, "Actualiser", ···]` |

## Tests verts dès le départ (gardes)

- G-1 `Process_grid_stays_outside_folded_details` vérifie que l’unique grille des processus est accessible hors des sections repliées, en lecture seule et sous son nom d’automatisation existant.
- G-2 `Complete_findings_list_keeps_analyzer_order_and_actions` vérifie l’ordre des quatre constats, leurs actions et le nombre de constats prioritaires.

## Réserves

- W1 La charge du processeur ou de la mémoire peut rester sans constat. Son analyse relève de T5b.
- W2 La grille des processus reste visible dans le volet inférieur. Le test L6 d’annulation par Échap accède à sa première cellule.
- W3 « Dernier relevé » donne l’heure à la minute selon le fuseau de l’horloge injectée. Le statut de lecture affiche l’heure à la seconde.
- W4 La durée de l’état initial et le rendu dans la fenêtre principale sur un PC Windows restent à vérifier avec les manipulations ci-dessous.

## Liste de contrôle

1. Les constats É1 à É7 correspondent au code de la base `58dd7b1`.
2. Les fixtures fixent les heures à UTC+2 (16 h 24 UTC donne 18 h 24), les notes à 100/100 et 50/100 et l’ordre du scénario à quatre constats à processeur, périphérique, espace disque, erreurs Windows.
3. Le run de tests seuls vérifie les trois échecs par assertion et les deux gardes.
4. Le diff couvre les quatre fichiers autorisés. Périmètre des tests existants — aucune exception E.
5. Les tests n’invoquent aucune action de diagnostic. Le dernier relevé reste en mémoire. A3 écrit uniquement sa preuve sous `artifacts/ui-smoke/lot-t5/`.
6. Les libellés respectent les accents du corpus validé par `InterfaceAccentTests`.
7. A3 écrit `lot-t5/diagnostic.txt` avant les assertions de chaque étape. La CI le collecte dans `Virgil-ui-smoke`.
8. Le contexte et les décisions renvoient au retour de test alpha.8 et à virgil (relecture).

## Manipulations Windows

1. Ouvrir Diagnostic. Vérifier la phrase « Mesurer ce qui ralentit le PC et savoir quoi vérifier. » et, pendant la première lecture, « Aucun relevé terminé pour le moment. » et « Constats disponibles après le premier relevé. ».
2. Sur un PC sans alerte, attendre la fin du relevé puis vérifier l’heure, la note et « Aucun problème détecté dans les signaux disponibles. ».
3. Sur un PC avec un disque presque plein ou des erreurs Windows récentes, vérifier les trois cartes au plus sous « À vérifier en priorité ». Vérifier la recommandation et, selon le constat, le bouton « Nettoyer… » ou « Mettre à jour… ». Cliquer sur ce bouton, vérifier l’ouverture de la validation, puis cliquer sur « ANNULER ».
4. Avec un relevé partiel, déplier « Signaux indisponibles » et comparer les lignes au nombre affiché en haut.
5. À l’ouverture, vérifier que « Tous les constats », « Démarrage », « Signaux indisponibles », « Processeur », « Mémoire », « Disques », « Réseau » et « Carte graphique » sont repliés et que la grille des processus reste visible.
6. Après un changement de minute, cliquer sur « Actualiser le diagnostic ». À la fin de la lecture, vérifier l’actualisation de l’heure.
7. Avec un jeu ou un calcul en cours, relever ce qu’affiche le haut du Diagnostic. La limite W1 s’applique si la charge seule ne déclenche aucun constat.
