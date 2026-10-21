# Virgil 2.0 — État du projet

Le statut de référence est maintenu dans [docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md).

Voir aussi la [roadmap et ses critères de sortie](docs/ROADMAP.md) et l’[architecture](docs/ARCHITECTURE.md).


## Livraison 1b — PR #51

1b livré : quarantaine de Nettoyage, port Core IQuarantineStore et stockage Windows injecté, manifeste JSON atomique complet, durée 7 jours réglable (1–30) figée dans l'aperçu, restauration vérifiée taille/SHA-256 sans écrasement, purge manuelle distincte avec ConfirmedPlan Sensitive et expiration UTC avec horloge testable qui propose une purge confirmée, sans aucun effacement silencieux. Section Quarantaine dans la vue existante, sans changement des autres modules ni du helper. Les zones Sûr passent également par la quarantaine. Aucun espace libéré annoncé pour les fichiers déplacés.

Le SDK dotnet est absent localement ; aucun build local validé. Référence main : 806 tests (Core.Tests 532 + App.Tests 274). La [CI Windows du code 1b, run 167](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/39478190910) valide 843 tests : Core.Tests 564 (532 + 32 nouveaux) et App.Tests 279 (274 + 5 nouveaux), soit 806 + 37. Logs par projet : 0 échec, 0 ignoré ; build : 0 warning. Le log de publication confirme « Publication verifiee : Virgil.ElevatedHelper.exe est present. » et l’artefact win-x64 est publié. Ce constat concerne le commit de code de52be7 ; la dernière mise à jour documentaire est suivie par la CI de la PR #51. Phase 1 non clôturée : 1c–1e restent à réaliser.


### Correctif P1 — expiration confirmée (PR #51)

Le consentement au nettoyage ne vaut plus consentement à un effacement ultérieur. L'ouverture de la vue, le scan et la fin d'un nettoyage ne purgent rien. L'échéance produit uniquement une proposition liée aux éléments expirés, avec nombre et taille, overlay existant et ConfirmedPlan Sensitive à usage unique. Aucun effacement sans TryConsume ; aucune fenêtre ni notification automatique. Textes et hash d'aperçu adaptés ; accents de « Suppression définitive après confirmation » corrigés. Les tests de l'horloge sont conservés et renforcés, avec refus sans effet et parcours UI/scan/nettoyage sans purge. Les chiffres de CI 843 ci-dessus décrivent la version antérieure au correctif ; la CI Windows du correctif est suivie dans la PR #51. SDK dotnet toujours absent localement.


## Livraison 1c (PR #52)

Trois groupes et restes de logiciels désinstallés livrés : ports de lecture Windows, analyse pure, exclusions des programmes installés/actifs/protégés, téléchargements ciblés, garde-fous personnels et sélection individuelle Critical, aperçu global et ConfirmedPlan unique. Registre en lecture seule ; fichiers via quarantaine de 1b. Tests existants conservés et adaptés au parcours récapitulatif ; tests nouveaux de politiques, seuils, sélection et refus sans effet. SDK dotnet absent localement : la CI Windows reste la référence. Base main : 850 tests (Core.Tests 569 + App.Tests 281). La [CI Windows du code final 1c, run 176](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/39496186080) valide 915 tests : Core.Tests 625 (569 + 56 nouveaux) et App.Tests 290 (281 + 9 nouveaux), soit 850 + 65 nouveaux ; 0 échec, 0 ignoré, build à 0 warning. Le log confirme « Publication verifiee : Virgil.ElevatedHelper.exe est present. » et l’artefact win-x64 est publié. Ce constat concerne le commit de code 77e1fdb ; la dernière mise à jour est documentaire uniquement, avec sa CI suivie dans la PR #52.


## Livraison 1d — Diagnostic en lecture seule (PR #53)

Contrats de mesures disponibles/indisponibles, port de lecture Windows, sampler pur et horloge injectée, module sans actions, catalogue français, vue détaillée et tableau triable, rafraîchissement limité à la visibilité et annulé à la réduction. Aucun helper, service de fond, écriture, historique ou fermeture de processus. Ressources et les protections existantes conservées. Objectif <1 % CPU moyen pendant l'affichage, mesure Windows restant à effectuer.

SDK dotnet absent localement ; aucun build local revendiqué. La [CI Windows du code final 1d, run 180](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/39515664167), commit cf2f29e, valide 953 tests : Core.Tests 655 (625 + 30 nouveaux), App.Tests 298 (290 + 8 nouveaux), soit 915 + 38 nouveaux ; 0 échec, 0 ignoré, build à 0 warning et 0 erreur. Le log confirme « Publication verifiee : Virgil.ElevatedHelper.exe est present. » et l'artefact win-x64 est publié. La mise à jour suivante est documentaire uniquement ; sa CI reste suivie dans la PR #53.


### Correctif P2 — ordre des cœurs PDH (PR #53)

`ProcessorInstanceOrdering` analyse les identités groupe/index et trie numériquement par groupe puis index ; les totaux machine et de groupe sont exclus. Une instance invalide ou une identité numérique dupliquée rend la charge par cœur indisponible avec raison française, sans libellé de cœur deviné. Les compteurs conservent leur identité entre deux mesures. Cinq tests ajoutés : ordre numérique, totaux exclus, instances invalides, doublons et affichage sans numéro inventé. Aucun autre périmètre modifié ni test supprimé ou affaibli.

La [CI Windows du correctif, run 182](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/39547143412), commit 7362e80, valide 958 tests : Core.Tests 659 (655 + 4 nouveaux), App.Tests 299 (298 + 1 nouveau), soit 953 + 5 nouveaux, ou 915 + 43 depuis main. Logs par projet : 0 échec, 0 ignoré ; build : 0 warning, 0 erreur. Publication : « Publication verifiee : Virgil.ElevatedHelper.exe est present. » et artefact win-x64 publié. SDK dotnet toujours absent localement. Le commit suivant est documentaire uniquement ; dernière CI suivie dans la PR #53.


## Livraison 1e — analyse Diagnostic

Implémentation du lot 1e livrée : signaux Windows en lecture seule, analyse pure, note déterministe partielle, top 3, constats et actions proposées après validation. Fermetures protégées via le port existant, navigation seule vers Nettoyage/Mises à jour ; aucun changement du démarrage. PR #54 : https://github.com/bassetthomas-design/virgil-2.0/pull/54. Phase 1 terminée : contrats communs, refus sans effet, quarantaine/restauration et parcours de confirmation validés par les tests Windows. L’objectif de performance pendant l’affichage reste à mesurer sur une session Windows représentative ; aucune mesure CPU revendiquée.

La [CI Windows du code final 1e, run 188](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/39549633419), commit d90dea2, valide 1 010 tests : Core.Tests 706 (659 + 47 nouveaux), App.Tests 304 (299 + 5 nouveaux), soit 958 + 52 nouveaux. Les logs de chaque projet indiquent 0 échec et 0 ignoré ; build : 0 warning, 0 erreur. Publication : « Publication verifiee : Virgil.ElevatedHelper.exe est present. » ; artefact win-x64 publié. Aucun test existant supprimé ou affaibli. SDK dotnet absent localement ; aucune validation locale de build revendiquée. La dernière mise à jour documentaire est suivie dans la PR #54.
