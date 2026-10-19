# Virgil 2.0 — État du projet

Les spécifications validées des actions et leur rattachement aux phases sont décrits dans [ACTIONS.md](ACTIONS.md) ; elles décrivent la cible produit.

## Situation au démarrage de la phase 0a

La solution comporte App (WPF, animation dans App/Animation), Core, Contracts, ElevatedHelper et Virgil.Tests (xUnit). Le dépôt contient déjà diagnostic, nettoyage, mises à jour, applications, interventions, ressources/monitoring et rapports. Leur présence ne signifie pas qu’ils respectent le nouveau contrat ou que la refonte est achevée. Les dépendances et limites réelles sont décrites dans [ARCHITECTURE.md](ARCHITECTURE.md).

La CI `.github/workflows/build.yml` sur Windows restaure, compile, teste et publie un artefact self-contained win-x64. La réussite d’un build se constate uniquement dans GitHub Actions ; ce document ne constitue pas une attestation de CI verte. Aucun installateur Inno Setup n’est encore présent.

## Livraison documentaire 0a

- Suppression du workflow invalide de payload de confirmation Applications, dont le travail a été livré par les PR #24 à #26.
- Suppression du workflow désactivé et du script d’initialisation devenus obsolètes selon la décision de reconstruction propre.
- Architecture réécrite : inventaire réel, cible, contrats, confirmation liée au hash d’aperçu et filets de sécurité.
- Roadmap remplacée par les phases 0 à 6 et leurs critères de sortie ; règles agents ajoutées à la racine.
- Aucun code C#, projet ou comportement applicatif modifié par ce lot. La phase 0 complète reste à réaliser ; la cible n’est pas implémentée par la documentation.

## Corrections documentaires 0a.1

Périmètre des points de restauration précisé, critères de distribution alpha complétés, phase 0 découpée en sous-lots ordonnés, inventaire actuel et règles agents corrigés. Les deux anciens documents de décisions/roadmap sont marqués historiques et le README renvoie aux références actuelles. Aucun changement C#, csproj ou workflow ; ces corrections ne livrent pas la cible.

## Suivi des phases

| Phase | État | Condition de clôture |
| --- | --- | --- |
| 0 — Fondations | 0a fusionné (PR #27), corrections 0a.1 ; 0b livré (PR #29) ; 0c livré (PR #30, #31) ; 0d-1 livré (PR #32) ; 0d-2 livré (PR #33) ; 0d-3 livré (PR #34) ; 0d-4 livré (PR #36) ; 0d-5 livré (PR #37) ; 0d-6 livré (PR #38) ; 0d-7 livré (PR #39) ; 0d-8 livré (PR #40) ; 0d-9 (Applications) à 0g à réaliser | Tous les critères de phase 0, y compris séparation des couches, DI et validation du contrat, satisfaits. |
| 1 — Diagnostic + Nettoyage | À migrer sur le nouveau contrat | Parcours scan/aperçu/ConfirmedPlan/résultat et restauration testés. |
| 2 — Installateur Inno Setup + v2.0.0-alpha | À réaliser | Installation Windows vérifiée et release alpha publiée. |
| 3 — Mises à jour, Applications, Démarrage, Réparations | Capacités partielles existantes ; migration et compléments à réaliser | Contrat commun, sauvegarde du démarrage et protections validés. |
| 4 — Monitoring continu | Services existants ; stabilisation à réaliser | Observation continue maîtrisée et budget de ressources validé. |
| 5 — Agent barre des tâches | À réaliser | Tray opérationnel, n’agissant jamais seul. |
| 6 — Personnalité | Contrôle animé existant ; parcours complet à réaliser | Avatar, messages `[VIRGIL]` et intentions du chat passant par ConfirmationService validés. |

Les critères de sortie détaillés, y compris la CI Windows pour chaque phase, figurent dans [ROADMAP.md](ROADMAP.md). Ne clôturer aucune phase sur la seule présence d’un fichier ou d’un écran.

## Prochain travail

Lots 0d-7 livré (PR #39) et 0d-8 livré (PR #40). Prochains lots : 0d-9 (Applications selon le modèle ResourcesViewModel), puis 0e contrats de confirmation testés, 0f helper limité à Contracts, puis 0g séparation des tests. Une PR par sous-lot, selon [AGENTS.md](../AGENTS.md). Préserver les gardes existants pendant toute la migration et ne pas ajouter de fonctionnalité avant la clôture de la phase 0.
