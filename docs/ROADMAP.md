# Virgil 2.0 — Roadmap

Le [statut de référence](PROJECT_STATUS.md) suit les phases et les PR livrées. [ACTIONS.md](ACTIONS.md) est la source des spécifications ; [ARCHITECTURE.md](ARCHITECTURE.md) décrit l'implémentation et les protections. Cette roadmap ne duplique pas le journal des livraisons.

## Ordre des cibles après L4c

| Lot cible | Travail prévu par le cadrage L4c |
| --- | --- |
| L5 | Harmoniser les opérations longues ; L5-2 traiterait les libellés UI restants |
| L6 / L6+ | Corriger les résultats des lanceurs, compléter les mises à jour ; pilotes cochés par défaut et retrait du bouton mort en L6+ |
| L7 | Fusionner Ressources dans Diagnostic ; l'écran Ressources est encore présent à la base de L4c |
| L9 | Traiter les capteurs ; aucune température CPU disponible dans la source actuelle |
| L11 | Catalogue et réglage du ton ; règle commune dans CHATBOX_MODULE |
| L12 | Compléter Démarrage : logos, impact, recommandations et temps avant/après |
| L12b | Compléter Réparation : chkdsk planifié, reset Windows Update complet et wsreset |

Le prochain travail est L5. Le fichier `loops/plan-lots.md` cité par le cadrage est absent du dépôt à `4af1cce`. Aucun contenu n'est inventé pour L8, L10 ou L13. Les ambitions sans sous-lot fourni restent des **cibles L5–L13 à affecter** dans ACTIONS : comparaison historique/PDF, compléments Applications/Nettoyage et séparation des accès OS legacy. Agent/Tray et monitoring continu restent reportés, sans sous-lot fixé par ce cadrage.

## Diagnostic : cadence de référence

À `4af1cce`, Diagnostic mesure à l'entrée, puis seulement sur « Actualiser », sans minuteur (`DiagnosticViewModel`). Cette cadence de 3d remplace celle de la livraison initiale 1d ; aucun suivi continu livré. L'architecture en donne le détail. Budget CPU et temps représentatifs sur Windows : non mesuré.

## Critères des futurs lots

Chaque cible devrait conserver les gardes existants, les confirmations et les refus sans effet. Les changements système concernés devraient garder sauvegarde et point de restauration vérifiés ; aucune commande libre ne devrait entrer dans le helper. Les plans devraient rester liés à l'aperçu réellement exécuté.

Une capacité ne devrait passer de « cible » à « livré » qu'avec ses fichiers de preuve, ses limites, les vérifications adaptées et une CI Windows réussie. La présence d'un écran, d'un tag ou d'un test ne devrait pas attester à elle seule un essai manuel Windows. Les mesures non effectuées devraient rester « non mesuré ».

## Anciennes phases

Les phases 0 à 6 restent des regroupements historiques, pas l'ordre actuel des lots. Leur situation après L4b-5 / REL-1 et les quatre tags publiés sont dans PROJECT_STATUS. Les spécifications historiques ne prouvent pas leur livraison intégrale.
