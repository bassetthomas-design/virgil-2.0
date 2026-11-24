# Lot S2 — Docs à niveau alpha.10

L’arbre était propre après `git fetch origin main`. La branche `codex/lot-s2-docs` part de `7c7c075052e482f68364a225ede3626e2bd847e2`. Le tag publié `v2.0.0-alpha.10` pointe sur `73e1cb5`. Entre ces deux références, S1 (#105, `59387b5`) et S1b (#106, `7c7c075`) stabilisent les tests. Leur diff contient les tests et leurs preuves, sans changement produit.

## Sources consultées

- Les pages GitHub Releases de `v2.0.0-alpha` et de `v2.0.0-alpha.2` à `v2.0.0-alpha.10`, liées dans [Distribution](../PROJECT_STATUS.md#distribution). Les dates proviennent de l’horodatage de publication affiché dans l’en-tête de chaque release, converti avec `Europe/Paris`, et non de l’horodatage du tag. Les noms des deux assets ont été vérifiés dans la liste des fichiers de l’alpha.10. Le contenu de [l’asset .sha256](https://github.com/bassetthomas-design/virgil-2.0/releases/download/v2.0.0-alpha.10/Virgil-2.0.0-alpha.10-win-x64-setup.exe.sha256) concorde avec le digest affiché par GitHub pour l’exécutable.
- Le README, le statut et son pointeur racine à la base du lot, [ACTIONS](../ACTIONS.md), [ROADMAP](../ROADMAP.md), [ARCHITECTURE](../ARCHITECTURE.md) et les [notes de la première alpha](../RELEASE_NOTES_2.0.0-alpha.md). Les trois références de cadrage sont historiques.
- Les preuves versionnées citées ci-dessous, ainsi que `L5-2.md`, `L6-1.md`, `L6-2-releve.md`, `L6plus-a.md`, `Lot-C-Avancement.md`, `Lot-H.md` et `Lot-Bureau-B3-verrou.md` pour le contexte des livraisons. Les spécifications de modules ne servent pas à attester une livraison.
- `git log --reverse --oneline 4af1cce..7c7c075` pour les PR et SHA de squash, `git ls-remote --tags origin` pour l’alpha.10 et l’arbre des projets à `7c7c075` pour confirmer les limites.

## Traçabilité

Dans le tableau, « statut » désigne `docs/PROJECT_STATUS.md`. Les noms de preuves sont relatifs à `docs/proofs/`.

| Fait mis à jour ou revalidé | Fichier | Source vérifiée |
| --- | --- | --- |
| Accueil : tuiles, rapport, priorités par source, encadré réseau et commandes | README et statut | Alpha.2, alpha.9, alpha.10, `Lot-F.md`, `Lot-Priorites.md`, `Lot-Reseau-Accueil.md`, README de base pour le rapport |
| Scan : rapide ou approfondi, en lecture seule | README | ACTIONS et ARCHITECTURE, alpha.5 pour la progression |
| Nettoyage : aperçu, quarantaine, protections du Bureau, Corbeille, résumé et sections | README et statut | Alpha initiale, alpha.3, alpha.8 à alpha.10, `Lot-A-Nettoyage.md`, `Lot-Bureau.md`, `Lot-Bureau-B3-verrou.md`, `Lot-Corbeille-Selection.md`, `Lot-Corbeille-Vidage.md`, `Lot-Nettoyage-Infos.md` |
| Diagnostic : relevés ponctuels, top 3 et confirmation de charge | README et statut | Alpha.2, alpha.9, `Lot-Diagnostic.md`, `Lot-Diagnostic-Charge.md` |
| Ressources : écran séparé et actions processus | README et statut | Alpha.7 à alpha.10, ACTIONS, ROADMAP, `L7-2.md`, bouton Ressources dans `src/Virgil.App/MainWindow.xaml` |
| Applications : inventaire et désinstallation officielle avec confirmation, dont Steam | README et statut | ACTIONS, alpha.2, alpha.8, `Lot-B-Apps.md` |
| Mises à jour : sources, scan partiel, Steam, Store, aperçu, élément unique et contrôle final | README et statut | Alpha.6 à alpha.10, `L6plus-a.md`, `L6plus-b.md`, `L7-1.md`, `Lot-D1-MAJ.md`, `Lot-D1bis-a.md`, `Lot-D1bis-b.md`, `Lot-D1bis-c.md`, `Lot-D2.md`, `Lot-MisesAJour.md`, `Lot-MisesAJour-Steam.md` |
| Démarrage : inventaire, sauvegarde et autorisations reconnues | README et statut | Alpha.2 et alpha.6, lot 3d dans le statut de base, ACTIONS et ARCHITECTURE |
| Réseau : écran existant et lien vers Réparation | README et statut | Alpha.2, alpha.9, alpha.10, ACTIONS, `Lot-Reseau-Bandeau.md`, `Lot-Reseau-Accueil.md` |
| Réparation : modes, allowlist, progression, bilans et file d’impression | README et statut | ACTIONS, alpha.2, alpha.9 et alpha.10, `Lot-Reparation-Services.md`, `Lot-Reparation-Progression.md` |
| Rapports : historique local, détail et export texte | README | ACTIONS (action 8), ARCHITECTURE |
| Onze boutons, vue de version séparée, bandeau permanent et moteur réseau | README et statut | Alpha.4, alpha.9, alpha.10, README de base, `Lot-Reseau-Moteur.md`, `Lot-Reseau-Bandeau.md`, `Lot-Reseau-Accueil.md` et onze boutons de navigation dans `src/Virgil.App/MainWindow.xaml` |
| Réseau : cadence 30 s, confirmation 5 s, notification 30 s par clé | README et statut | `Lot-Reseau-Moteur.md` et `Lot-Reseau-Bandeau.md` |
| Visage, répliques et signalement | README et statut | Alpha.8, `Lot-E.md`, `Lot-F.md`, `Lot-G.md`, `Lot-H.md` |
| Avertissement à chaque lancement et licence GPL-3.0 | README et statut | Alpha.7, `Alpha-Licence.md`, `LICENSE` |
| Distribution .NET 8 autonome x64, Inno Setup et helper | README | Lot 2a dans le statut de base, `.github/workflows/release.yml`, `src/Virgil.App/Virgil.App.csproj`, `installer/Virgil.iss` |
| SHA-256, noms des fichiers et comparaison intégrale | README et renvoi dans le statut | Assets alpha.10 et notes de la première alpha, « SHA-256, signature et SmartScreen » |
| SmartScreen, installateur non signé et absence de certificat | README et renvoi dans le statut | README de base et notes de la première alpha, chaîne de distribution relue, deux libellés tirés du brief S2 |
| Température CPU indisponible, relevés ponctuels et budget CPU non mesuré | README et statut | ACTIONS, ROADMAP, ARCHITECTURE, `L7-2.md`, `Lot-Diagnostic-Charge.md` et `src/Virgil.Infrastructure/Windows/Diagnostic/WindowsDiagnosticSource.cs` |
| Agent/Tray et icône de notification absents | README et statut | ACTIONS, ROADMAP, statut à `4af1cce` et inventaire des projets à `7c7c075` |
| Réparation partielle : CHKDSK au redémarrage, reset complet Windows Update et wsreset | README et statut | ACTIONS, ROADMAP (L12b), preuves T2a/T2b et allowlist du helper |
| Démarrage partiel : logos, recommandations, impact et temps avant/après | README et statut | ACTIONS, ARCHITECTURE, ROADMAP (L12), alpha.6 |
| Historique sans PDF ni comparaison | README et statut | ACTIONS (action 8), ARCHITECTURE |
| Absence de réglage de ton | README et statut | `Lot-F.md`, ROADMAP (L11) |
| Réserve sur les essais de bout en bout et les essais manuels d’installation | README et statut | Alpha.10 et statut à `4af1cce` |
| Tableau des tags et journal depuis l’alpha.4, dont S1 et S1b | Statut | Horodatages des releases, historique Git, `Lot-S1-Stabilisation.md`, `Lot-S1b-Stabilisation.md` |

## Vérifications et réserves

- « Informations complémentaires » et « Exécuter quand même » sont les libellés SmartScreen fournis par la consigne S2. Aucun fichier versionné ne les atteste pour le dialogue SmartScreen. La recherche trouve « Informations complémentaires » dans le contexte du Nettoyage. Les notes et la chaîne de distribution examinées ne montrent aucun ajout de signature depuis l’exception alpha.
- Le test `ViewModel_debounces_network_change_refresh_and_polls_at_most_every_30_seconds` et les scénarios du bandeau étayent la fréquence maximale de mesure périodique. Le moteur documente séparément la confirmation des transitions après cinq secondes et le délai de trente secondes par clé de notification. Aucune durée d’exécution de sonde n’est déduite.
- `WindowsDiagnosticSource.cs` fournit encore `Missing<double>()` pour la température CPU. Aucun projet Agent/Tray n’apparaît dans l’arbre. L’agent COM de Windows Update concerne les mises à jour.
- Les preuves et l’allowlist ne montrent ni CHKDSK planifié au redémarrage ni `wsreset`. Le redémarrage du service Windows Update et le traitement de mises à jour par le helper ne fournissent pas une remise à zéro complète.
- Aucun chiffre n’a été inventé et aucune empreinte d’une autre alpha n’a été recopiée. Les vérifications sur les sources ne constituent pas un essai sur un vrai PC.

## Périmètre du diff

La référence locale `origin/main` correspond à `main` vérifiée à `7c7c075`. Sorties des équivalents de `git diff --stat main...HEAD` et `git diff --name-only main...HEAD` avec cette référence.

```text
 README.md                  |  61 ++++++++++++++++---------
 docs/PROJECT_STATUS.md     | 110 +++++++++++++++++++++++++++++++++++++--------
 docs/proofs/Lot-S2-Docs.md |  72 +++++++++++++++++++++++++++++
 3 files changed, 204 insertions(+), 39 deletions(-)
```

```text
README.md
docs/PROJECT_STATUS.md
docs/proofs/Lot-S2-Docs.md
```

Le diff avec `-w` montre les modifications de fond. Les documents sont en UTF-8 avec des fins de ligne LF. Le diff est limité aux trois chemins autorisés.

La CI du commit final est rapportée dans la description de la PR.
