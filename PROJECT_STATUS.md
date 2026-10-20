# Virgil 2.0 — État du projet

Le statut de référence est maintenu dans [docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md).

Voir aussi la [roadmap et ses critères de sortie](docs/ROADMAP.md) et l’[architecture](docs/ARCHITECTURE.md).


## Livraison 1b — PR à renseigner après création

1b livré : quarantaine de Nettoyage, port Core IQuarantineStore et stockage Windows injecté, manifeste JSON atomique complet, durée 7 jours réglable (1–30) figée dans l'aperçu, restauration vérifiée taille/SHA-256 sans écrasement, purge manuelle distincte avec ConfirmedPlan Sensitive et expiration UTC avec horloge testable. Section Quarantaine dans la vue existante, sans changement des autres modules ni du helper. Les zones Sûr passent également par la quarantaine. Aucun espace libéré annoncé pour les fichiers déplacés.

Le SDK dotnet est absent localement ; aucun build local validé. Référence main : 806 tests (Core.Tests 532 + App.Tests 274). Les résultats des nouveaux tests et la présence de Virgil.ElevatedHelper.exe dans le publish seront lus dans les logs de CI Windows de la PR. Phase 1 non clôturée : 1c–1e restent à réaliser.
