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
| 1 — Diagnostic + Nettoyage | 1a livré (PR #50) : contrat commun et Nettoyage branché ; 1b livré ; 1c–1e à réaliser | Parcours scan/aperçu/ConfirmedPlan/résultat et restauration testés. |
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


## Livraison 1a (PR #50)

1a livré (PR #50) : `Findings` en lecture seule dans Contracts/Actions ; `IMaintenanceModule` et `IMaintenanceAction` dans Core/Actions, avec annulation et `ConfirmedPlan`. Nettoyage utilise le module et les actions injectés ; zones, tailles, textes français, confirmations et effets existants sont conservés, sans XAML. Les accès disque du service d’exécution modifié sont derrière `ICleanupDisk` dans Infrastructure.Windows ; gardes et porte à usage unique conservés.

Tests ajoutés pour les refus sans effet fichier/corbeille, le scan sans suppression, l’aperçu déterministe, l’équivalence du parcours en bac à sable, l’architecture et la DI validée. Aucun test existant supprimé ou affaibli. Référence main confirmée dans les logs Windows : 786 tests (Core.Tests 513 + App.Tests 273). La CI Windows du code 1a (run 161, PR #50) passe 803 tests (Core.Tests 529 + App.Tests 274), soit 17 nouveaux, avec 0 échec, 0 ignoré et 0 warning. La cible de publication vérifie explicitement la présence de `Virgil.ElevatedHelper.exe` ; sa validation sur le dernier commit est suivie dans la PR. Le SDK dotnet est absent de l’environnement de travail ; aucune validation locale de build revendiquée.

Suite de Phase 1 : 1b quarantaine de 7 jours réglable avec restauration vérifiée ; 1c trois groupes et restes de logiciels avec garde-fous dossiers personnels ; 1d Diagnostic détaillé en lecture seule ; 1e note, top 3 et actions proposées après validation avec fermeture de processus protégée. Ces capacités ne sont pas livrées par 1a.


### Correctifs de revue 1a — PR #50

Les actions liées au scan sont obtenues par `IMaintenanceModule.GetActions(Findings)`, également utilisé par le VM ; aucune action inerte n’est exposée en DI. Le service d’exécution transmet une raison de refus distincte des erreurs opérationnelles : une annulation après erreur n’est pas un refus avant effet. Les zones de `CleanupFindings` sont copiées en profondeur, avec leurs candidats et listes de règles.

Trois tests de régression sont ajoutés sans retirer de cas existant : exécution via les seuls contrats après scan, mutation des listes source sans modification des Findings, annulation après erreur sans WasRefused. Le lot comporte désormais 806 cas (Core.Tests 532 + App.Tests 274), soit 786 + 20 nouveaux ; résultat Windows du dernier commit et logs par projet suivis dans la PR #50. Les dix cas de refus fichier/corbeille restent obligatoires, sans appel disque. SDK dotnet toujours absent localement.


## Livraison 1b — PR #51

1b livré : quarantaine de Nettoyage, port Core IQuarantineStore et stockage Windows injecté, manifeste JSON atomique complet, durée 7 jours réglable (1–30) figée dans l'aperçu, restauration vérifiée taille/SHA-256 sans écrasement, purge manuelle distincte avec ConfirmedPlan Sensitive et expiration UTC avec horloge testable qui propose une purge confirmée, sans aucun effacement silencieux. Section Quarantaine dans la vue existante, sans changement des autres modules ni du helper. Les zones Sûr passent également par la quarantaine. Aucun espace libéré annoncé pour les fichiers déplacés.

Le SDK dotnet est absent localement ; aucun build local validé. Référence main : 806 tests (Core.Tests 532 + App.Tests 274). La [CI Windows du code 1b, run 167](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/39478190910) valide 843 tests : Core.Tests 564 (532 + 32 nouveaux) et App.Tests 279 (274 + 5 nouveaux), soit 806 + 37. Logs par projet : 0 échec, 0 ignoré ; build : 0 warning. Le log de publication confirme « Publication verifiee : Virgil.ElevatedHelper.exe est present. » et l’artefact win-x64 est publié. Ce constat concerne le commit de code de52be7 ; la dernière mise à jour documentaire est suivie par la CI de la PR #51. Phase 1 non clôturée : 1c–1e restent à réaliser.


### Correctif P1 — expiration confirmée (PR #51)

Le consentement au nettoyage ne vaut plus consentement à un effacement ultérieur. L'ouverture de la vue, le scan et la fin d'un nettoyage ne purgent rien. L'échéance produit uniquement une proposition liée aux éléments expirés, avec nombre et taille, overlay existant et ConfirmedPlan Sensitive à usage unique. Aucun effacement sans TryConsume ; aucune fenêtre ni notification automatique. Textes et hash d'aperçu adaptés ; accents de « Suppression définitive après confirmation » corrigés. Les tests de l'horloge sont conservés et renforcés, avec refus sans effet et parcours UI/scan/nettoyage sans purge. Les chiffres de CI 843 ci-dessus décrivent la version antérieure au correctif ; la CI Windows du correctif est suivie dans la PR #51. SDK dotnet toujours absent localement.
