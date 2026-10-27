# Virgil 2.0 — Spécification des actions

Référence : `4af1cce`. « Livré » porte seulement sur la capacité indiquée. « Partiel » signale une limite fonctionnelle. Une cible serait implémentée dans le lot indiqué ; elle n'est pas une capacité actuelle. Le [statut](PROJECT_STATUS.md) suit les livraisons et l'[architecture](ARCHITECTURE.md) décrit les protections et les fichiers de preuve.

| Action | État à 4af1cce | Cible |
| --- | --- | --- |
| 0 — Tout mettre à jour | partiel | L6 |
| 1 — Diagnostic | partiel | L7, L9 ; monitoring continu à planifier |
| 2 — Nettoyage | livré dans le périmètre décrit | Compléments non livrés à répartir dans L5–L13 |
| 3 — Mises à jour | partiel | L6 |
| 4 — Applications | livré dans le périmètre décrit | Compléments non livrés à répartir dans L5–L13 |
| 5 — Réparation | partiel | L12b |
| 6 — Ressources | partiel, écran encore présent | Fusion prévue en L7 |
| 7 — Démarrage | partiel | L12 |
| 8 — Historique | partiel | Comparaison/PDF à répartir dans L5–L13 |
| 9 — Surveillance / Tray | cible, non livré | Reporté ; sous-lot non fixé |
| 10 — Personnalité | partiel | L11 |

Le cadrage disponible ne fournit pas l'affectation de toutes les ambitions à un sous-lot L5–L13. Ces mentions ne créent pas une décision de planification. Les spécifications détaillées anciennes de modules ne valent pas preuve de livraison.

## Action 0 — Tout mettre à jour

**État à 4af1cce : partiel.**

Le bouton prépare une sélection et un aperçu unique. `UpdateSequenceService` lie toute la sélection au `ConfirmedPlan`, puis exécute les étapes et produit un résultat. L'annulation intervient entre deux étapes ; elle ne coupe pas brutalement une installation. Le redémarrage reste manuel.

Windows qualité/sécurité, Defender, pilotes via Windows Update et applications WinGet ont un backend effectif. Windows et les pilotes exigent un point de restauration vérifié. Steam, Epic Games et Ubisoft Connect sont seulement trois consignes fixes ; leur détection et leur mise à jour ne sont pas assurées par cette séquence. Le backend les compte réussies sans rien faire (« À mettre à jour depuis le lanceur. »). Aucun scan de mises à jour Store.

**Cible L6 :** le résultat des lanceurs devrait distinguer une consigne d'une installation réussie. La couverture Store et jeux devrait être complétée et vérifiée. Cible L6 : Battle.net pourrait rejoindre le périmètre des mises à jour ; il n'y figure pas aujourd'hui. Les outils constructeurs, les pilotes GPU et les runtimes devraient être précisés dans cette cible L6. BIOS et firmware resteraient en information seulement, sans installation automatique.

## Action 1 — Diagnostic

**État à 4af1cce : partiel.**

L'écran présente un instantané CPU, RAM, disques, réseau, GPU et processus, un tri et des explications françaises. La note est partielle si une source manque ; le top 3 vient de l'analyse déterministe. Les données absentes portent une raison, sans zéro inventé.

Une mesure est faite à l'entrée, puis seulement sur « Actualiser ». Aucun minuteur de monitoring continu. La température CPU est toujours indisponible dans la source actuelle. Les débits réseau et disque par programme ne sont pas fournis ; les connexions TCP et points UDP ne sont pas des débits. Une vérification SMART limitée ne garantit pas la santé du disque.

Les propositions de fermeture utilisent les protections processus et un `ConfirmedPlan`. Nettoyer / Mettre à jour ouvrent leurs écrans après validation ; cette navigation n'exécute pas l'entretien. La modification du démarrage est disponible dans son écran depuis 3d, voir Action 7.

**Cibles :** la fusion Ressources serait faite en L7 ; les capteurs seraient traités en L9. La fréquence CPU, la mémoire compressée, la charge GPU, la VRAM et les données SMART devraient rester explicitement indisponibles quand Windows ne les expose pas ; leur couverture matérielle complète serait une cible L9. Les débits et E/S par programme, le monitoring continu et la comparaison historique resteraient des cibles L5–L13 à affecter. Budget CPU et temps en usage réel Windows : non mesuré.

## Action 2 — Nettoyage

**État à 4af1cce : livré pour le parcours ci-dessous.**

Trois groupes : Sûr, À vérifier et Restes de logiciels. Analyse, lignes regroupées, sélection individuelle/par catégorie/globale, aperçu récapitulatif et confirmation précèdent les effets. Nature, compteurs et phrase de confirmation entrent dans l'aperçu lié au plan. Une sélection n'émet aucun consentement à elle seule.

La sélection globale exclut les dossiers personnels, Téléchargements, Corbeille et Windows.old. Un fichier personnel admissible exige un choix individuel et une confirmation critique. Windows.old reste délégué aux outils Windows. Les restes détectés excluent les preuves d'installation ; un inventaire incomplet impose une vérification manuelle. Aucun scan ne garantit de trouver tous les restes de tous les logiciels. Les clés de registre sont en lecture seule.

Les fichiers nettoyés par ce parcours passent par la quarantaine ; la Corbeille conserve son API distincte. Rétention : 7 jours par défaut, réglable de 1 à 30. La restauration vérifie taille et SHA-256 et refuse l'écrasement. L'expiration ne supprime rien seule : purge et vidage immédiat exigent un nouvel aperçu et un nouveau plan. Un déplacement en quarantaine ne constitue pas de l'espace disque libéré.

`LongOperationViewModel` et `OperationProgressPanel` donnent progression et annulation dans Nettoyage. L'harmonisation des autres vues serait traitée en **cible L5**. La suppression des clés de registre resterait une **cible L5–L13, sous-lot non fixé**, avec sauvegarde et point de restauration ; aucune écriture de registre n'est promise ici.

## Action 3 — Mises à jour

**État à 4af1cce : partiel.**

Le scan WinGet utilise le module JSON quand disponible et un repli CLI localisé. Les installations individuelles épinglent la version confirmée. « Tout mettre à jour » est spécifié une seule fois, dans l'Action 0.

L'inventaire des pilotes retourne `CanInstallDrivers = false` : le bouton dédié ne s'affiche jamais. Leur recherche/installation effective passe par la séquence Windows Update. Les pilotes sont actuellement décochés par défaut (`UpdateSequenceChoiceViewModel`).

**Cible L6+ :** conformément à la décision de thoms du 26 octobre à 19 h 14, les pilotes devraient être cochés par défaut et le code mort du bouton retiré. La décision ne décrit pas le comportement actuel. **Cible L6 :** couverture et résultats Store/lanceurs, voir Action 0.

## Action 4 — Applications

**État à 4af1cce : livré pour l'inventaire et la désinstallation officielle confirmée.**

Inventaire Win32/Store, recherche, détail et chargement des icônes en arrière-plan. La désinstallation individuelle ou multiple utilise un aperçu, un plan et les protections des composants sensibles. Elle lance le désinstalleur officiel ; un lancement réussi ne garantit pas sa réussite finale.

Des cartes complètes avec logo, éditeur, taille et dates fiables seraient des cibles L5–L13 à affecter lorsque ces données manquent. Les restes sont consultables ; leur nettoyage relève de l'Action 2. Une suppression automatique de tous les restes, une date de dernière utilisation exhaustive et une couverture parfaite des icônes resteraient des **cibles L5–L13, sous-lot non fixé**. Une valeur absente n'est pas une mesure.

## Action 5 — Réparation

**État à 4af1cce : partiel.**

Les modes « Vérifier » et « Scanner et réparer » passent par les services d'intervention. L'allowlist du helper borne les commandes : SFC vérification/réparation, DISM CheckHealth/ScanHealth/RestoreHealth et `chkdsk /scan` en ligne. Le redémarrage du service Windows Update (`virgil-update`), les actions audio/impression et les opérations réseau autorisées restent des actions précises, pas une réparation universelle.

Les changements système concernés exigent aperçu, confirmation et point de restauration créé/vérifié. Un refus UAC ou un échec de protection bloque l'action. Le helper n'accepte pas de commande libre.

**Cible L12b :** un chkdsk planifié, une remise à zéro complète de Windows Update et `wsreset` pour le Store seraient des ajouts distincts. Ils ne sont pas livrés par l'allowlist actuelle. Les diagnostics son/imprimante/Store exhaustifs resteraient à compléter dans ce lot.

## Action 6 — Ressources

**État à 4af1cce : partiel ; fusion prévue en L7, écran encore présent.**

`ResourcesNavButton`, `ResourcesView` et `ResourcesViewModel` existent. L'écran observe CPU/RAM et les processus ; les actions de fermeture propre/forcée et de relance Explorer sont confirmées et protégées. La libération de mémoire inactive est informative, sans boost simulé.

**Cible L7 :** ces parcours seraient intégrés à Diagnostic. Aucun mode jeu ni promesse de gain FPS.

## Action 7 — Démarrage

**État à 4af1cce : partiel ; modification livrée en 3d.**

L'inventaire lit les entrées Run, dossiers et tâches prises en charge par la source. Les autorisations StartupApproved reconnues peuvent être désactivées, réactivées et restaurées avec aperçu et `ConfirmedPlan`. Sauvegarde vérifiée et point de restauration précèdent l'écriture. La vue peut restaurer la dernière modification de la session ; ce n'est pas une restauration globale de tout le démarrage. Aucune entrée ni tâche n'est supprimée ; les tâches restent en lecture seule.

**Cible L12 :** logos, impact faible/moyen/fort, recommandation par élément et temps avant/après devraient être ajoutés. Impact et temps actuels : « indisponible sans mesure » / non mesuré. L'inventaire ne prouve pas une lenteur.

## Action 8 — Historique

**État à 4af1cce : partiel.**

L'historique conserve des rapports JSON locaux et propose détail et export texte assaini. Aucune comparaison continue ni export PDF livré. Une frise, des totaux consolidés et une comparaison de santé seraient des **cibles L5–L13, sous-lot non fixé** ; aucun espace libéré ou temps gagné ne devrait être calculé sans preuve.

## Action 9 — Surveillance / Tray

**État à 4af1cce : cible reportée ; aucun projet Agent/Tray.**

Le futur agent pourrait ouvrir Virgil et proposer des actions, sans agir seul. Notifications seulement en cas de problème, rappel optionnel, respect de Ne pas déranger et du plein écran, absence de son par défaut et limitation de fréquence seraient des exigences à valider. Les budgets cibles historiques (<1 % CPU moyen et <100 Mo RAM) resteraient à vérifier, sans mesure revendiquée. Le sous-lot n'est pas fixé dans le cadrage L4c ; aucun monitoring continu ni budget mesuré n'est livré.

## Action 10 — Personnalité

**État à 4af1cce : partiel.**

Les messages `[VIRGIL]` sont à sens unique, sans saisie de chat. Ton actuel tactique et neutre ; catalogue et réglage en **cible L11**. La définition unique du ton est dans [CHATBOX_MODULE.md](modules/CHATBOX_MODULE.md#style-de-communication) ; les documents produit y renvoient.

**Cible L11 :** le ton serait sarcastique mais discret ; jamais d'humour dans une confirmation, un risque ou une suppression ; clarté d'abord. Aucun catalogue sarcastique ni réglage sobre/humour n'est livré. Une IA libre et un avatar nouveau ne sont pas décidés par cette spécification.
