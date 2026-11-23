# Lot T5b — Diagnostic de charge

Base `aedc5fc1cd689f92e17655587fb4f8d4c4735f2a`. Retour de test alpha.8, point 5.

Le Diagnostic signale une charge processeur ou mémoire à partir de 85 % sur deux relevés consécutifs espacés de 5 secondes à 5 minutes incluses. Un relevé élevé isolé invite à actualiser et une mesure absente ne produit aucun constat de charge. Les deux constats partagent une pénalité maximale de 10 points. Le bouton « Voir les processus » trie la liste par consommation décroissante et la fait défiler à l’écran. Dans l’Accueil, une mesure sous le seuil résout la priorité. Une mesure indisponible ou une charge élevée à confirmer la laisse à revérifier.

## CI Windows

- Tests seuls, [run #481](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45138546983), commit `b604033c5199f8549c314ed3942c254ebfeb0e19`. Compilation réussie avec 0 avertissement et 0 erreur. Les cinq échecs sont ceux du tableau.
- Le résultat du correctif et la validation finale du commit qui contient cette preuve sont référencés dans la [description de la PR #99](https://github.com/bassetthomas-design/virgil-2.0/pull/99), avec le run Windows, le SHA et l’identifiant de l’artefact `Virgil-ui-smoke`. La trace de rendu se trouve dans `lot-t5b/diagnostic-charge.txt`.

## Exigences et assertions du run rouge

| Exigence | Test | Message xUnit | Attendu | Obtenu |
| --- | --- | --- | --- | --- |
| A1. Charge processeur confirmée | `Two_high_processor_readings_make_a_load_finding` | `Assert.Equal() Failure: Collections differ` | `["cpu.load"]` | `EmptyPartition<string> []` |
| A2. Charge mémoire et poids commun | `Two_high_memory_readings_make_a_load_finding_and_share_the_load_weight` | `Assert.Equal() Failure: Collections differ` | `["memory.load"]` | `EmptyPartition<string> []` |
| A3. Carte et tri des processus | `Load_card_recommends_and_opens_the_process_list` | `Assert.Equal() Failure: Strings differ` | `À vérifier en priorité` | `Aucun problème détecté dans les signaux d`… (message tronqué par xUnit) |
| A4. Invitation et carte dans la vue WPF | `Rendered_screen_shows_pending_hint_then_load_card_with_its_button` | `Assert.Contains() Failure: Item not found in collection` | `Charge élevée sur ce relevé (processeur 97 %). Actualisez le diagnostic dans quelques secondes pour vérifier qu'elle dure.` (tronqué après « Act » dans le journal) | Élément absent des textes visibles |
| A5. Priorité d’Accueil et retour à la normale | `Load_priority_opens_diagnostic_and_returns_to_normal` | `Assert.Equal() Failure: Collections differ` | `["cpu.load"]` | `SelectIListIterator<PriorityPoint, string> []` |

## Tests verts dès le départ (gardes)

- G-1 `Spike_short_gap_stale_or_unreadable_readings_never_conclude`.
- G-2 `One_reading_alone_keeps_the_existing_analysis`.

## Limites et vérifications Windows

1. W1. Au repos, ouvrir Diagnostic et vérifier l’absence d’invitation. Puis maintenir le processeur au-dessus de 85 % dans le Gestionnaire des tâches, par exemple pendant une compression. Ouvrir Diagnostic et vérifier l’invitation sans baisse de note. Attendre une dizaine de secondes et actualiser. Vérifier la carte « Processeur : très sollicité », les deux mesures, la recommandation et la baisse de note d’au plus 10 points.
2. W2. Les deux fenêtres d’une seconde ne mesurent pas l’intervalle entre elles. Répéter deux relevés élevés à plus de cinq minutes d’écart et vérifier que seule l’invitation s’affiche.
3. W3. Accueil distingue les sources Scan et Diagnostic par le détail horodaté et par l’action. Ouvrir « Ouvrir Diagnostic » depuis la priorité de charge, arrêter la charge et actualiser. Vérifier le retour à la normale horodaté dans l’Accueil.
4. W4. Trier les processus par nom puis cliquer sur « Voir les processus ». Observer le défilement et le tri CPU décroissant dans la fenêtre Windows.
5. W5. Vérifier le parcours mémoire en dépassant 85 % de mémoire utilisée dans le Gestionnaire des tâches, puis vérifier le tri par mémoire privée. Windows compte le cache en attente dans la mémoire disponible. La fréquence de l’alerte reste à observer sur un PC à 8 Go.

## Liste de contrôle

- Base et zones de modification vérifiées sur `aedc5fc`. Neuf fichiers dans la liste fermée, aucune exception E.
- Horloge de test avec un relevé d’une seconde. Horodatages attendus à +1 s, +32 s et +93 s, affichés à 11 h 00 ou 11 h 01 en UTC+2.
- Attendus mémoire de 90 %, 87 % et 50 %, note de 90 ou 100, ordre des identifiants et libellés exacts.
- Cinq assertions rouges et deux gardes vertes au run #481. Aucun échec fragile connu observé sur ce run.
- Le bouton trie et fait défiler les processus. Le relevé précédent reste en mémoire. La seule écriture des nouveaux tests est la trace de rendu dans l’artefact de CI.
- Libellés recopiés du brief. Les formes sans accent de « relevés », « consécutifs » et « sollicité » sont absentes du code source.
- Preuve et trace de rendu associées à la PR. Origine du changement documentée comme retour de test alpha.8.
