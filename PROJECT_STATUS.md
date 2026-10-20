# Virgil 2.0 — État du projet

Le statut de référence est maintenu dans [docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md).

Voir aussi la [roadmap et ses critères de sortie](docs/ROADMAP.md) et l’[architecture](docs/ARCHITECTURE.md).


## Livraison 1b — PR #51

1b livré : quarantaine de Nettoyage, port Core IQuarantineStore et stockage Windows injecté, manifeste JSON atomique complet, durée 7 jours réglable (1–30) figée dans l'aperçu, restauration vérifiée taille/SHA-256 sans écrasement, purge manuelle distincte avec ConfirmedPlan Sensitive et expiration UTC avec horloge testable qui propose une purge confirmée, sans aucun effacement silencieux. Section Quarantaine dans la vue existante, sans changement des autres modules ni du helper. Les zones Sûr passent également par la quarantaine. Aucun espace libéré annoncé pour les fichiers déplacés.

Le SDK dotnet est absent localement ; aucun build local validé. Référence main : 806 tests (Core.Tests 532 + App.Tests 274). La [CI Windows du code 1b, run 167](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/39478190910) valide 843 tests : Core.Tests 564 (532 + 32 nouveaux) et App.Tests 279 (274 + 5 nouveaux), soit 806 + 37. Logs par projet : 0 échec, 0 ignoré ; build : 0 warning. Le log de publication confirme « Publication verifiee : Virgil.ElevatedHelper.exe est present. » et l’artefact win-x64 est publié. Ce constat concerne le commit de code de52be7 ; la dernière mise à jour documentaire est suivie par la CI de la PR #51. Phase 1 non clôturée : 1c–1e restent à réaliser.


### Correctif P1 — expiration confirmée (PR #51)

Le consentement au nettoyage ne vaut plus consentement à un effacement ultérieur. L'ouverture de la vue, le scan et la fin d'un nettoyage ne purgent rien. L'échéance produit uniquement une proposition liée aux éléments expirés, avec nombre et taille, overlay existant et ConfirmedPlan Sensitive à usage unique. Aucun effacement sans TryConsume ; aucune fenêtre ni notification automatique. Textes et hash d'aperçu adaptés ; accents de « Suppression définitive après confirmation » corrigés. Les tests de l'horloge sont conservés et renforcés, avec refus sans effet et parcours UI/scan/nettoyage sans purge. Les chiffres de CI 843 ci-dessus décrivent la version antérieure au correctif ; la CI Windows du correctif est suivie dans la PR #51. SDK dotnet toujours absent localement.


## Livraison 1c (PR à renseigner)

Trois groupes et restes de logiciels désinstallés livrés : ports de lecture Windows, analyse pure, exclusions des programmes installés/actifs/protégés, téléchargements ciblés, garde-fous personnels et sélection individuelle Critical, aperçu global et ConfirmedPlan unique. Registre en lecture seule ; fichiers via quarantaine de 1b. Tests existants conservés et adaptés au parcours récapitulatif ; tests nouveaux de politiques, seuils, sélection et refus sans effet. SDK dotnet absent localement : la CI Windows reste la référence. Base main : 850 tests (Core.Tests 569 + App.Tests 281). Résultats Windows et publication du helper à consigner après exécution.
