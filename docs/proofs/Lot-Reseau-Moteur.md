# Lot 7a — Connexion & Sécurité réseau

Le moteur commun classe la connectivité Internet, le routage VPN et la protection Wi-Fi à partir d’observations en lecture seule. Les sondes Windows lisent les interfaces, les routes, le chiffrement et les noms des clients VPN reconnus. Le store partagé confirme les transitions après cinq secondes et applique un délai de trente secondes par clé de notification. La composition enregistre la sonde en transient et le store en singleton.

## Tests seuls

La CI Windows [#498](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45205223956) du commit `5660d66916a96f6a80b4762d8608d05cf5528cb2` a compilé avec 0 avertissement et 0 erreur. Les huit tests ci-dessous ont échoué par assertion. Le run précédent [#497](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45204701661) s’est arrêté à la compilation sur trois appels ambigus du test Windows, corrigés dans le second commit de tests seuls.

| Repère | Test | Attendu | Obtenu dans le journal #498 |
| --- | --- | --- | --- |
| A1 | `Evaluator_distinguishes_connected_single_target_failure_captive_portal_limited_and_no_network` | Type `ConnectionSecurityEvaluator` résolu | `Assert.NotNull() Failure: Value is null` |
| A2 | `Evaluator_distinguishes_vpn_full_tunnel_split_tunnel_inactive_and_virtual_adapters` | Type `ConnectionSecurityEvaluator` résolu | `Assert.NotNull() Failure: Value is null` |
| A3 | `Evaluator_evaluates_wifi_protection_and_never_claims_totally_secure` | Type `ConnectionSecurityEvaluator` résolu | `Assert.NotNull() Failure: Value is null` |
| A4 | `Store_debounces_brief_drops_and_notifies_only_after_confirmation_window` | Type `ConnectionStatusStore` résolu | `Assert.NotNull() Failure: Value is null` |
| A5 | `Store_serializes_concurrent_refreshes_and_probes_http_captive_portal_without_redirects` | Type `ConnectionStatusStore` résolu | `Assert.NotNull() Failure: Value is null` |
| A6 | `Native_wifi_security_mapper_classifies_auth_and_cipher_algorithms` | Type `WindowsNetworkNativeProbe` résolu | `Assert.NotNull() Failure: Value is null` |
| W1 | `Windows_read_only_probe_reads_network_state_and_routes_without_elevation` | Type `WindowsNetworkNativeProbe` résolu | `Assert.NotNull() Failure: Value is null` |
| B1 | `Di_registers_connection_status_store_as_singleton_and_probe_as_transient` | Type `IConnectionStatusStore` résolu | `Assert.NotNull() Failure: Value is null` |

## Gardes vertes dès les tests seuls

- G‑1, `Existing_network_status_reader_and_vpn_protection_policy_remain_intact`, vérifie le lecteur réseau existant et la protection des clients VPN.
- G‑2, `Network_read_only_boundary_has_no_system_writes_elevation_or_ui_dependency`, vérifie les références de Core et les appels interdits dans les dossiers réseau.

## CI Windows du correctif

La CI Windows [#499](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45205720918) du commit `ba6a8676c3fbb9ebfdca2320ba1494a07f6b80b2` a réussi, avec 0 avertissement et 0 erreur de compilation. A1 à A6, W1, B1, G‑1 et G‑2 ont réussi, ainsi que les 14 tests UI. Artefact [Virgil-ui-smoke 14833447012](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/45205720918/artifacts/14833447012).

Le SHA, le run Windows et l’identifiant de l’artefact du head relu figurent dans la [description de la PR #102](https://github.com/bassetthomas-design/virgil-2.0/pull/102).

## Vérifications manuelles Windows restantes

1. Sur un poste Windows connecté à un Wi-Fi WPA2 ou WPA3 avec le VPN fermé, exécuter `dotnet test tests/Virgil.Core.Tests --filter FullyQualifiedName~LotReseauMoteurWindowsTests`. En 7b/7c, ouvrir le module, actualiser et contrôler le chiffrement affiché et `RoutesAvailable == true`, sans invite UAC ni écriture système.
2. En 7b/7c, après intégration des badges, couper puis rétablir la connexion en moins de cinq secondes. Vérifier que l’état affiché reste stable et qu’aucune notification n’apparaît. Connecter ensuite un VPN complet, passer au routage partiel, puis fermer le client. Vérifier les états correspondants. Se connecter enfin à un portail captif sans s’authentifier, puis à un Wi-Fi ouvert et vérifier les libellés et niveaux associés.
