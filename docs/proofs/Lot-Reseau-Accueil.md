# Lot 7c — Connexion et sécurité réseau sur l’accueil

La demande réseau t646u place l’encadré à droite des priorités sur l’accueil. Le bandeau et l’encadré partagent le même `ConnectionStatusViewModel` et le même snapshot. « Actualiser » reprend le chemin de lecture du store, et « Voir l’écran Réseau » ouvre la vue existante.

## Tests seuls

- [CI Windows #506](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45222646649), commit `b14322352ea80895b74443e8d4cf95bee75eaad2`. La compilation Release réussit avec 0 avertissement et 0 erreur. Cinq tests échouent, tous sur les assertions ci-dessous.
- [CI Windows #507](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45223247153), commit `a4186664c311c928566740ec9507eada72686640`. Le second commit de tests seuls couvre la barre oblique inverse que WPF exige devant l’apostrophe XML `&apos;` dans la liaison du conseil. La compilation réussit avec 0 avertissement et 0 erreur. Les mêmes cinq assertions échouent.

| ID | Test | Attendu | Obtenu dans les deux runs |
| --- | --- | --- | --- |
| A1 | `ViewModel_formats_panel_texts_advice_and_never_claims_total_security` | Réflexion sur une propriété `InternetStatusText` présente. | `Assert.NotNull() Failure: Value is null` |
| A2 | `ViewModel_updates_panel_with_store_and_stops_after_dispose` | Réflexion sur une propriété `InternetStatusText` présente après `Start()`. | `Assert.NotNull() Failure: Value is null` |
| A3 | `Actualiser_command_refreshes_the_store_and_open_network_command_stays_shared` | Réflexion sur une propriété `ActualiserCommand` présente. | `Assert.NotNull() Failure: Value is null` |
| X1 | `Home_dashboard_hosts_connection_security_panel_beside_priorities_without_a_fifth_tile` | Un descendant de `HomeContentGrid` nommé `ConnectionSecurityPanel`. | `Assert.Single() Failure: The collection did not contain any matching items` puis `Expected:   (predicate expression)`. |
| B1 | `Main_window_binds_the_existing_connection_status_viewmodel_to_the_panel` | `ConnectionSecurityPanel.DataContext = _connectionStatus;` dans `MainWindow.xaml.cs`. | `Assert.Contains() Failure: Sub-string not found` puis `Not found: "ConnectionSecurityPanel.DataContext = _co"···`. |

## Gardes vertes dès le premier commit

- G‑1 `Existing_network_view_priorities_and_header_contracts_remain_intact`.
- G‑2 `Panel_remains_read_only_without_system_writes`.

Les huit tests de `LotReseauBandeauTests` passent dès le commit de tests seuls après le retrait des deux assertions de périmètre du lot 7b.

## Correctif Windows

[CI Windows #508](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45223794957), commit `8f682e17bd1d73925d206f8e5337a0df3b60c4b9`. Le workflow aboutit avec 0 avertissement et 0 erreur à la compilation. Les cinq tests A1 à A3, X1 et B1 passent, ainsi que G‑1, G‑2 et les tests existants. La publication, les smoke tests UI et la construction de l’installeur réussissent. L’artefact [Virgil-ui-smoke](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45223794957/artifacts/14841424306) porte l’ID `14841424306`.

Les runs #506, #507 et #508 ne recensent aucun échec des tests fragiles répertoriés pour le lot.

Le [run Windows #509](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45225285119), sur le commit `10654484f1642476bcf33cfaca723da425eae987`, compile avec 0 avertissement et 0 erreur. Tous les tests hors UI passent. Les deux seuls échecs appartiennent à la suite fragile `UiSmokeTests` :

- `Smoke_run_has_no_destructive_side_effect`
- `Cleanup_summary_is_captured_without_execution`

Les deux tests remontent `Assert.Null() Failure: Value is not null` avec `Xunit.Sdk.FailException: Vue/contrôle UIA invisible : UpdatePlanStep.Drivers`. L’[artefact Virgil-ui-smoke](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45225285119/artifacts/14844755476) de ce diagnostic porte l’ID `14844755476`.

## Validation du head relu

La section « Vérifications réalisées » de la [PR #104](https://github.com/bassetthomas-design/virgil-2.0/pull/104) est la référence pour le run Windows de validation du head de relecture, son SHA et l’ID de l’artefact associé.

## Vérifications manuelles Windows restantes

1. Lancer Virgil sur l’accueil. Vérifier que « CONNEXION ET SÉCURITÉ RÉSEAU » se trouve à droite des priorités et que la rangée contient quatre tuiles. Réduire la fenêtre à 1080 px, vérifier les retours à la ligne et comparer l’état des badges à celui de l’encadré.
2. Cliquer sur « Actualiser » et vérifier les libellés, l’absence de demande UAC et l’absence de changement des réglages réseau. Cliquer sur « Voir l’écran Réseau », puis revenir à l’accueil. Ouvrir « Détails » et vérifier « Mesuré à HH:mm » et « Mesure locale en lecture seule uniquement. ». Vérifier qu’aucun écran ne promet une sécurité totale.
3. Sur un poste disposant des configurations de test, consulter l’encadré avec un Wi-Fi ouvert ou WEP, un VPN complet, un VPN partiel, un client VPN fermé et un portail captif réel. Vérifier le conseil et l’affichage du fournisseur connu ou de « fournisseur non identifié » pour un VPN actif.
