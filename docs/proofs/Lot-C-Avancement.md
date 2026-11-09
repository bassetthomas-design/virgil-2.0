# Lot C — avancement commun

Base exacte : `ceece03a635e80500d1ae5a4a2f543b6baf4d9b5` (lot B #78).
PR brouillon : https://github.com/bassetthomas-design/virgil-2.0/pull/79.

## Séquence de preuve

Le commit tests seuls `bfacf33906f6dbff3cec7673c38f12eb084168aa` précède tout correctif.
Run Windows rouge : https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42595659871.
Checkout de fusion technique CI : `a1f261641d6dd7cac18dc01aaf0bee7c5a0a8877`.
Build réussi, 0 avertissement, 0 erreur. App : 600 tests, 598 réussites, 2 échecs :
`Repeated_reports_for_one_sequence_step_update_a_single_visible_entry` échoue car la collection contient 2 entrées ;
`DeterminateProgressCancellationAndFinalResultRemainVisible` échoue car `IsVisible` vaut false.
Ce sont des échecs d'assertion reproduisant les défauts de production, sans panne de compilation ou d'environnement.

Run Windows vert du correctif : https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42596613599.
Head : `bc4972704fe40e3564eac008ced607f074bf16ed` ; checkout de fusion technique CI : `14fafa70ac9a6742314829bff74ca54f1471081b`.
Build : 0 avertissement, 0 erreur. Core : 1148/1148 ; App : 606/606 ; UI : 14/14.
Publication de l'application et création de l'installateur réussies.
Artefact UI de ce run : `13663548163` (`Virgil-ui-smoke`). Les trois tests de capture lot C ont réussi.
Un commit de finition ajoute un fond opaque aux captures, aligne le bouton Annuler en haut et refuse aussi
les notifications d'une ancienne session de séquence. La CI du head final est référencée dans la PR ;
les résultats ci-dessus restent attribués au head `bc497270…`, sans les transposer au commit suivant.

## Comportement et vérification

| Étape | Comportement | Test |
| --- | --- | --- |
| Panneau commun | Identité stable, mise à jour du même objet, résumé et commentaire | `Step_identity_updates_in_place_and_explicit_dismiss_or_restart_clears_final_state` |
| Mises à jour | Ticks 1/40 puis 12/40, deux sources ordonnées ; source bloquée et redémarrage conservés | `Two_steps_keep_order_latest_tick_and_honest_final_summary` |
| Annulation | Non lancée reste distinct de source indisponible ; aucune carte fantôme | variante annulée du test précédent et tests Safety existants |
| Nettoyage | Zones dans l'ordre, ticks regroupés, bilan quarantaine et fichiers hors sélection visibles | `Cleanup_multi_zone_final_panel_preserves_quarantine_and_untouched_summary` |
| Résultat final | Succès, annulation et bilan partiel restent visibles | tests LongOperation et captures STA |
| Applications R9 | Statut et architecture en français, éditeur vide non renseigné | `Application_details_use_simple_french_without_executing` |
| Attribution R8 | Ajout Steam accepté par loops (relecture), protections et Caution conservées | lecture ciblée de `Lot-B-Apps.md` |

R3 : `Complete` arrête les minuteries et conserve le panneau, `Step` et `FinalSummary`.
Le bouton « Fermer le bilan » appelle `Dismiss` uniquement après la fin ; une nouvelle opération efface étapes,
résumé et commentaire. Les notifications tardives ne remplacent pas le bilan. Les autres écrans gardent le panneau L5.

R5 : `Commentary` reprend la première ligne des messages existants, ou le statut de progression de la séquence.
C'est une prise pour F : ni IA, ni nouvelle banque de textes, ni défilement mot à mot.
Les étapes de séquence sont identifiées par leur titre stable ; la collection de compatibilité `SequenceProgress`
met à jour la même paire index/titre et n'est plus affichée en parallèle dans UpdatesView.
Les zones Nettoyage sont identifiées par `CleanupZoneId`, sans employer le compteur de fichiers comme identité.

D1/D2 restent responsables du contenu Store et des passes Windows Update. Le lot C présente les résultats existants,
y compris sources indisponibles, erreurs, étapes sautées/non lancées et redémarrage ; il ne prétend pas que tout est à jour.
Les moteurs de confirmation et de quarantaine ainsi que les workflows sont inchangés.

## Preuves UI et limites

Les trois PNG sont régénérés par les tests STA/WPF sur des services factices à chaque run du correctif :

- `artifacts/ui-smoke/lot-c/mises-a-jour-avancement.png` : panneau final de deux sources factices, dont une bloquée, redémarrage requis ;
- `artifacts/ui-smoke/lot-c/nettoyage-avancement.png` : bilan de deux zones factices, aucune action sur des fichiers réels ;
- `artifacts/ui-smoke/lot-c/applications-details-libellés.png` : vraie vue Applications avec inventaire factice, sans lancement.

Ces captures sont des rendus STA factices ; elles ne prouvent ni une installation réelle ni un nettoyage réel.
SafeUi n'est pas élargi. Aucun build/test local Linux n'est présenté comme une validation Windows.
## Reprise après réserves de loops (relecture)

La relecture du head `35f699ff57d636b6a54a8ba9f3e222bf7e8bbe1e`, à partir des PNG du run
`42597242589`, a relevé cinq réserves : conclusion répétée, barre finale incomplète, Annuler encore visible,
raison absente dans les détails Applications et pluriel incorrect pour un fichier en quarantaine. Aucun feu vert acquis.

La reprise commence par des tests seuls : `677207cf1fac676664a464799bf85871b1c9cc1f`, puis
`7d36fdcf95f45e6c12c210ee6434840f2490fab9` corrige seulement l'assertion de cardinalité imposée par xUnit2013.
Le run `42598318889`, arrêté à la compilation sur cette règle d'analyse des tests, est exclu de la preuve rouge.

Preuve rouge Windows : https://github.com/bassetthomas-design/virgil-2.0/actions/runs/42598508477,
head tests seuls `7d36fdcf95f45e6c12c210ee6434840f2490fab9`.
Build : 0 avertissement, 0 erreur. Core : 1148 réussites ; App : 616 tests, 609 réussites, 7 échecs d'assertion :
deux conclusions présentes deux fois dans le texte WPF visible, deux cas de fin non annulée restant à 0/9 %,
Annuler encore visible, raison vide et singulier absent. Les cas annulés et les pluriels 0/2 passent déjà.
Le correctif de production est committé séparément après ce run rouge.

| Réserve | Correction | Test |
| --- | --- | --- |
| Conclusion répétée | Le commentaire reste disponible, mais n'est affiché que s'il apporte du texte absent du résumé | `Final_panel_displays_conclusion_exactly_once`, deux écrans, texte WPF réellement visible |
| Barre finale | Fin non annulée : 100 %, déterminée ; annulation demandée ou signalée par le résultat : valeur atteinte conservée | `Completed_bar_reaches_100_but_cancellation_preserves_reached_value`, quatre cas ; séquence factice annulée/non annulée |
| Annuler | Visibilité liée à `IsRunning`, sans changement de style ni d'AutomationId | `Cancel_button_is_visible_only_while_operation_runs` |
| Raison absente | « Raison : non renseignée » dans ShowDetails | `Application_details_use_simple_french_without_executing` |
| Pluriel | 0 fichiers / 1 fichier / 2 fichiers, résumé et détail de zone | `Quarantine_counts_use_correct_plural_in_final_summary_and_step` |

Le premier run du correctif (`42598996265`, head `83e37b04142880078163a313771b6bb9dc9f68e1`)
compile sans avertissement, puis le processus App est interrompu par une collection d'étapes Nettoyage modifiée
pendant une énumération. Le rappel des étapes est désormais synchrone sans contexte UI et conserve le dispatch
sur le contexte WPF lorsqu'il existe. Aucun résultat vert n'est revendiqué pour ce run interrompu.

Validation verte de la reprise : en attente de la CI Windows. Les trois PNG lot C seront régénérés par ce run.
Le style des boutons et la marge gauche restent réservés au lot E. Aucun changement des moteurs de confirmation
ou de quarantaine, de SafeUi, des workflows ou des réglages. La revue visuelle des nouveaux PNG et la relecture
du nouveau head exact restent à effectuer par loops (relecture).
