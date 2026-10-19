# Virgil 2.0 — État du projet

Les spécifications validées des actions et leur rattachement aux phases sont décrits dans [ACTIONS.md](ACTIONS.md) ; elles décrivent la cible produit.

## Situation après le lot 0g

La solution comporte App (WPF, animation dans App/Animation), Core, Contracts, Infrastructure, ElevatedHelper et les projets xUnit Virgil.Core.Tests et Virgil.App.Tests (0g livré, PR #48). Le dépôt contient déjà diagnostic, nettoyage, mises à jour, applications, interventions, ressources/monitoring et rapports. Leur présence ne signifie pas qu’ils respectent le nouveau contrat ou que la refonte est achevée. Les dépendances et limites réelles sont décrites dans [ARCHITECTURE.md](ARCHITECTURE.md).

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
| 0 — Fondations | 0a fusionné (PR #27), corrections 0a.1 ; 0b livré (PR #29) ; 0c livré (PR #30, #31) ; 0d-1 livré (PR #32) ; 0d-2 livré (PR #33) ; 0d-3 livré (PR #34) ; 0d-4 livré (PR #36) ; 0d-5 livré (PR #37) ; 0d-6 livré (PR #38) ; 0d-7 livré (PR #39) ; 0d-8 livré (PR #40) ; 0d-9 livré (PR #41) ; lot 0d terminé ; 0e-1 livré (PR #42) ; 0e-2 livré (PR #43) : correspondances RiskLevel, purge et pilote Applications ; 0e-3 livré (PR #44) : porte Ressources et Mises à jour ; 0e-4 livré (PR #45) : porte Interventions et Nettoyage ; lot 0e terminé ; 0f livré (PR #47) ; 0g livré (PR #48), sous-lots 0a–0g livrés ; 0f-bis livré (PR #49) : code OS hors de Contracts ; phase 0 non clôturée, dette OS Core existante à migrer vers Infrastructure | Tous les critères de phase 0, y compris séparation des couches, DI et validation du contrat, satisfaits. |
| 1 — Diagnostic + Nettoyage | À migrer sur le nouveau contrat | Parcours scan/aperçu/ConfirmedPlan/résultat et restauration testés. |
| 2 — Installateur Inno Setup + v2.0.0-alpha | À réaliser | Installation Windows vérifiée et release alpha publiée. |
| 3 — Mises à jour, Applications, Démarrage, Réparations | Capacités partielles existantes ; migration et compléments à réaliser | Contrat commun, sauvegarde du démarrage et protections validés. |
| 4 — Monitoring continu | Services existants ; stabilisation à réaliser | Observation continue maîtrisée et budget de ressources validé. |
| 5 — Agent barre des tâches | À réaliser | Tray opérationnel, n’agissant jamais seul. |
| 6 — Personnalité | Contrôle animé existant ; parcours complet à réaliser | Avatar, messages `[VIRGIL]` et intentions du chat passant par ConfirmationService validés. |

Les critères de sortie détaillés, y compris la CI Windows pour chaque phase, figurent dans [ROADMAP.md](ROADMAP.md). Ne clôturer aucune phase sur la seule présence d’un fichier ou d’un écran.

## Correctifs P1 post-0e

Les deux P1 sont livres (PR #46) : execution WinGet epinglee sur la version confirmee avec refus si elle manque ou est invalide ; sonde et ouverture des emplacements Applications via le port Core IInstallationLocationService, implemente dans Infrastructure et injecte par Hosting. Textes UI existants inchanges. 0f livré (PR #47) ; 0g livré (PR #48). La Phase 1 reste hors périmètre de ces correctifs ; 0f-bis livré (PR #49).

## Prochain travail

0g livré (PR #48) : `Virgil.Core.Tests` et `Virgil.App.Tests`, 746 cas existants conservés et deux tests d’architecture ajoutés (476 + 272 = 748 cas sur main, CI Windows verte, 0 warning). 0f-bis livré (PR #49) : le code OS est sorti de Contracts conformément à la cible d’[ARCHITECTURE.md](ARCHITECTURE.md). La phase 0 n’est pas encore close : la dette OS existante dans Core reste à migrer vers Infrastructure. Préserver les gardes existants ; aucune fonctionnalité de Phase 1 n’est livrée par ces lots.

## Livraison 0f-bis (PR #49)

0f-bis livré (PR #49) : gardes de chemins purs et inspections injectées dans Contracts ; inspection disque, création des dossiers, ACL et fichiers du protocole client dans Infrastructure.Windows derrière les ports Core injectés par Hosting, validation disque/ACL interne au helper limité à Contracts. Cas de sécurité du protocole partagés entre les deux validations, garde d’architecture des références de Contracts et synchronisation native du test de mutex abandonné. Aucun changement de protocole, d’allowlist ou de comportement applicatif ; Phase 1 hors périmètre.

Le correctif P1 de la même PR #49 retire les adaptateurs OS client de Core : `ElevatedProtocolRoot`, `ElevatedDiskInspector` et les accès aux fichiers du protocole sont dans Infrastructure.Windows ; Core conserve les ports et la logique du magasin. La dette de placement Core est levée pour ce point ; les autres accès OS legacy restent hors lot. Les tests partagés client/helper et le correctif du mutex sont conservés.
