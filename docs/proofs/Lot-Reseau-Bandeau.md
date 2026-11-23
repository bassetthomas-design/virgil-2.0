# Lot 7b — Bandeau permanent Connexion & Sécurité réseau

Le bandeau affiche les états Internet et VPN fournis par le store du lot 7a. Les deux badges restent dans l'en-tête, ouvrent la vue Réseau et partagent une infobulle qui précise la portée de la mesure. Les notifications émises par le store alimentent le fil COMMUNICATION VIRGIL. Le sondage et les abonnements prennent fin à la fermeture de la fenêtre.

## CI Windows

- [Run rouge nº 502](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45214324873) sur `9db1ca5860dac324238d3b67c8c56a046790f1a3` (tests seuls). Compilation réussie avec 0 avertissement et 0 erreur. Les six tests ci-dessous sont les seuls échecs et chacun provient d'une assertion.

- [Run vert du correctif nº 503, tentative 2](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45215282729/attempts/2) sur `05a29ea8095cbc8b151b2063d0d1af44849807bc`. Compilation avec 0 avertissement et 0 erreur, huit tests du lot réussis, quatorze tests de fumée réussis. L'artefact `Virgil-ui-smoke` porte l'ID [14838936650](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45215282729/artifacts/14838936650).
- Le numéro, le SHA et l'artefact du run vert sur le commit qui contient cette preuve figurent dans la [description de la PR nº 103](https://github.com/bassetthomas-design/virgil-2.0/pull/103).

## Test fragile observé

La [première tentative du run nº 503](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45215282729/attempts/1) a échoué uniquement sur `LauncherShutdownTests.Launcher_is_force_closed_only_after_15_seconds(exits: True)` avec `System.TimeoutException : The operation has timed out.` Le même test passe à la deuxième tentative sur le même commit.

## Scénarios rouges puis verts

| Cas | Test | Attendu au run rouge | Résultat réel au run rouge |
| --- | --- | --- | --- |
| A1 | `ViewModel_reads_store_snapshot_and_formats_french_badges_and_tooltip` | Le type `Virgil.App.ViewModels.ConnectionStatusViewModel` | `Assert.NotNull() Failure: Value is null` |
| A2 | `ViewModel_subscribes_to_store_changes_and_unsubscribes_on_dispose` | Le type `Virgil.App.ViewModels.ConnectionStatusViewModel` | `Assert.NotNull() Failure: Value is null` |
| A3 | `ViewModel_debounces_network_change_refresh_and_polls_at_most_every_30_seconds` | Le type `Virgil.App.ViewModels.ConnectionStatusViewModel` | `Assert.NotNull() Failure: Value is null` |
| A4 | `ViewModel_forwards_store_notifications_and_open_network_command` | Le type `Virgil.App.ViewModels.ConnectionStatusViewModel` | `Assert.NotNull() Failure: Value is null` |
| X1 | `HeaderPanel_keeps_existing_badges_and_hosts_permanent_clickable_connection_badges` | Un élément nommé `ConnectionBadgesHost` dans `HeaderPanel` | `Assert.Single() Failure: The collection did not contain any matching items` |
| B1 | `Di_registers_connection_status_viewmodel_as_singleton_and_wires_main_window` | Le type `Virgil.App.ViewModels.ConnectionStatusViewModel` | `Assert.NotNull() Failure: Value is null` |

## Gardes vertes dès le départ

- G-1 `Existing_header_contracts_and_network_view_remain_intact` couvre les fichiers de la vue Réseau et de ses tests, les éléments existants du bandeau et les quatre tuiles de l'accueil.
- G-2 `Bandeau_remains_read_only_without_system_writes` couvre les interdits du lot et les dépendances du signal Core.

## Vérifications manuelles Windows restantes

1. Lancer Virgil et réduire la fenêtre à sa largeur minimale de 1080 px. Afficher l'accueil puis chaque module. Vérifier que les six badges tiennent sur la même ligne et que le statut suit le module courant.
2. Couper puis rétablir la connexion en moins de cinq secondes. Vérifier que les badges restent stables et qu'aucune nouvelle notification réseau n'apparaît.
3. Couper de nouveau la connexion, maintenir la coupure au-delà de cinq secondes et attendre la mesure périodique qui confirme la transition. Vérifier les badges et le message dans le fil. Rétablir la connexion et vérifier les états après confirmation par le store. Les notifications suivent le délai de 30 secondes du store pour une même clé.
4. Survoler chaque badge et vérifier les deux états, la protection et la phrase « Mesure locale en lecture seule uniquement. ». Cliquer sur chaque badge depuis un autre module et vérifier l'ouverture de la vue Réseau. Fermer la fenêtre et vérifier l'absence de demande UAC et de modification des paramètres réseau.
