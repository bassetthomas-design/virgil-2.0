# Virgil 2.0 — Statut à l’alpha.10

La dernière préversion publiée est `v2.0.0-alpha.10` (`73e1cb5`). La base `main` de ce suivi est `7c7c075`, qui ajoute S1 et S1b pour stabiliser les tests, sans changement produit. [ACTIONS](ACTIONS.md), [ROADMAP](ROADMAP.md) et [ARCHITECTURE](ARCHITECTURE.md) servent de références historiques autour de `4af1cce`.

## Distribution

| Tag publié | Date de publication | État |
| --- | --- | --- |
| [`v2.0.0-alpha`](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha) | 21 octobre 2026 | préversion publiée |
| [`v2.0.0-alpha.2`](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.2) | 26 octobre 2026 | préversion publiée |
| [`v2.0.0-alpha.3`](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.3) | 27 octobre 2026 | préversion publiée |
| [`v2.0.0-alpha.4`](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.4) | 27 octobre 2026 | préversion publiée |
| [`v2.0.0-alpha.5`](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.5) | 27 octobre 2026 | préversion publiée |
| [`v2.0.0-alpha.6`](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.6) | 2 novembre 2026 | préversion publiée |
| [`v2.0.0-alpha.7`](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.7) | 5 novembre 2026 | préversion publiée |
| [`v2.0.0-alpha.8`](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.8) | 16 novembre 2026 | préversion publiée |
| [`v2.0.0-alpha.9`](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.9) | 23 novembre 2026 | préversion publiée |
| [`v2.0.0-alpha.10`](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.10) | 23 novembre 2026 | dernière préversion publiée |

Dates de publication GitHub converties en heure de Paris. L’installateur Windows x64 non signé et son fichier SHA-256 sont joints à la release alpha.10. Voir la [vérification SHA-256 et SmartScreen](../README.md#installer-lalpha). Un avertissement alpha s’affiche à chaque lancement. La licence est [GPL-3.0](../LICENSE).

La publication ne prouve pas une validation manuelle complète sur un vrai PC Windows. Les notes de l’alpha.10 précisent que Steam, le Store, SFC/CHKDSK sur un vrai PC et un vrai portail Wi-Fi captif n’ont pas encore été testés de bout en bout. Les essais manuels d’installation, de compte standard et de désinstallation ne sont pas attestés ici.

## Phases

| Phase historique | État à l’alpha.10 | Suite |
| --- | --- | --- |
| 0 — Fondations | Lots 0a–0g et 0f-bis livrés ; dette d'accès OS encore présente dans Core | Séparation cible décrite dans ARCHITECTURE ; pas de clôture totale revendiquée |
| 1 — Diagnostic et Nettoyage | Lots 1a–1e livrés (#50–#54) | Limites des capteurs et cadence ponctuelle conservées |
| 2 — Distribution alpha | 2a/2b livrés (#55/#56) ; voir les tags ci-dessus | Essais manuels non attestés, signature hors de cette livraison |
| 3 — Maintenance | 3a–3d livrés (#57–#60), périmètre décrit ci-dessous | Mises à jour étendues jusqu’à T6b ; Démarrage et Réparation restent partiels |
| 4 — Monitoring continu | Non livré ; Diagnostic ponctuel | Ressources reste séparé ; capteurs : cible historique L9 |
| 5 — Agent / tray | Non livré ; aucun projet Agent/Tray | Cible reportée, sous-lot non fixé dans le cadrage L4c |
| 6 — Personnalité | Catalogue de répliques écrites livré au lot F ; aucun réglage de ton | Réglage sobre/humour : cible L11 |

## Socle livré avant les correctifs alpha

0a–0g (#27–#48), correctifs P1 (#46) et 0f-bis (#49) : reconstruction, couches et composition DI, MVVM, contrats de confirmation et séparation Core.Tests/App.Tests. La présence d'accès OS dans Core reste une limite explicite.

1a (#50) : contrat commun et Nettoyage ; 1b (#51) : quarantaine/restauration ; 1c (#52) : trois groupes et restes ; 1d (#53) : Diagnostic en lecture seule ; 1e (#54) : note, top 3 et propositions confirmées. La cadence a ensuite été modifiée en 3d.

2a (#55) : Inno Setup, helper publié et distribution par tag ; 2b (#56) : vérification des versions GitHub, ouverture de page sans installation automatique.

## Lot 3a — PR #58

Livré : exclusions des logiciels installés, regroupement des résultats de Nettoyage, virtualisation et progression/annulation avec `LongOperationViewModel` et `OperationProgressPanel`.

## Lot 3b — PR #59

Livré : scan WinGet renforcé et séquence « Tout mettre à jour », aperçu lié à la sélection, exécution séquentielle, rapport et redémarrage manuel. D1 à T6b ont ensuite étendu le périmètre au Store et à Steam, avec contrôle final.

## Lot 3c — PR #57

Livré : modes Vérifier / Scanner et réparer, suivi du helper, protection par restauration ; inventaire Applications et désinstallation multiple confirmée. La liste exacte des commandes reste celle de l'allowlist.

## Lot 3d — PR #60

Livré : Diagnostic à l'entrée puis sur Actualiser, tuiles de dernières mesures, en-tête et accès Scan, écran Réseau et modification/restauration des autorisations Démarrage reconnues. Aucun monitoring continu.

## L4b-1 — PR #61

Livré : smoke UI FlaUI sur l'exécutable publié, navigation des onze écrans et garde de lecture seule `SafeUi`.

## L4b-2 — PR #62

Livré : lisibilité du Nettoyage et inventaire de faux PC en lecture seule pour vérifier les exclusions des restes.

## L4b-3 — PR #63

Livré : sélection globale et par catégorie, nature/risque par ligne et phrase de confirmation liée à l'aperçu ; exclusions personnelles conservées.

## L4b-4 — PR #64

Livré : vérification de la latence après Idle, accents et contraste des onze vues. Ce test automatisé ne mesure pas le budget CPU en usage réel.

## REL-1 — PR #65

Livré : exclusion `Category!=UI` dans le workflow de release ; les tests UI restent exécutés par `build.yml` sur Windows.

## L4b-5 — PR #66

Livré : accents des messages Communication, récapitulatif Nettoyage corrigé et contrôle des treize PNG attendus dans l'artefact smoke, dont la capture du bureau.

## Journal depuis l’alpha.4

| SHA du squash | PR | Livraison |
| --- | --- | --- |
| `9f76123` | [#67](https://github.com/bassetthomas-design/virgil-2.0/pull/67) | L4c — Documentation alignée sur le code réel |
| `26ccd1d` | [#68](https://github.com/bassetthomas-design/virgil-2.0/pull/68) | L5-1 — Progression et Annuler sûr sur les 5 écrans |
| `8f275e9` | [#69](https://github.com/bassetthomas-design/virgil-2.0/pull/69) | L5-2 — Gardes d'architecture et accents |
| `5cdc5b2` | [#70](https://github.com/bassetthomas-design/virgil-2.0/pull/70) | L6-1 — Défauts de comportement alpha.2 |
| `2e28501` | [#71](https://github.com/bassetthomas-design/virgil-2.0/pull/71) | L6-2 — Accents verrouillés sur les 5 assemblages |
| `ee72c94` | [#72](https://github.com/bassetthomas-design/virgil-2.0/pull/72) | L6+-a — Helper Microsoft Update, passes WU, redémarrage, pnputil |
| `be30824` | [#73](https://github.com/bassetthomas-design/virgil-2.0/pull/73) | L6+-b — Tout mettre à jour : plan, winget, lanceurs, smoke |
| `63dedc9` | [#74](https://github.com/bassetthomas-design/virgil-2.0/pull/74) | L7-1 — Store non disponible : information en best effort |
| `ca01052` | [#75](https://github.com/bassetthomas-design/virgil-2.0/pull/75) | L7-2 — Retirer le monitoring legacy et conserver les lecteurs actifs |
| `9b3621b` | [#76](https://github.com/bassetthomas-design/virgil-2.0/pull/76) | Alpha-Licence : avertissement au démarrage et licence GPL-3.0 |
| `6b83871` | [#77](https://github.com/bassetthomas-design/virgil-2.0/pull/77) | Lot A — présélection Core explicite, catégories éligibles et lignes sombres |
| `ceece03` | [#78](https://github.com/bassetthomas-design/virgil-2.0/pull/78) | Lot B Apps/Steam — désinstallation officielle confirmée et causes distinctes |
| `548e982` | [#79](https://github.com/bassetthomas-design/virgil-2.0/pull/79) | Lot C — fenêtre d’avancement commune |
| `44cb0f5` | [#80](https://github.com/bassetthomas-design/virgil-2.0/pull/80) | alpha.8 / D1 — Tout mettre à jour : contrôle final et bilan honnête |
| `4276261` | [#81](https://github.com/bassetthomas-design/virgil-2.0/pull/81) | Lot D1-bis-a — Microsoft Store réel et logiciels sans source automatique |
| `1beb2d1` | [#82](https://github.com/bassetthomas-design/virgil-2.0/pull/82) | Lot D1-bis-b — cartes par lanceur et mises à jour Steam vérifiées |
| `4cca374` | [#83](https://github.com/bassetthomas-design/virgil-2.0/pull/83) | Lot D1-bis-c — pilotes officiels et BIOS en information seulement |
| `7ad4a7d` | [#84](https://github.com/bassetthomas-design/virgil-2.0/pull/84) | Lot D2 — Mettre à jour un seul élément |
| `cd7cd53` | [#85](https://github.com/bassetthomas-design/virgil-2.0/pull/85) | Lot E — Visage WPF, progression typée et icône partagée |
| `524fcc2` | [#86](https://github.com/bassetthomas-design/virgil-2.0/pull/86) | Lot F — Répliques de Virgil, texte progressif et Priorités |
| `2a20466` | [#87](https://github.com/bassetthomas-design/virgil-2.0/pull/87) | Lot G — Signaler un problème, issue GitHub préremplie |
| `411fc89` | [#88](https://github.com/bassetthomas-design/virgil-2.0/pull/88) | Lot H — correctifs avant alpha.8 |
| `8ef3986` | [#89](https://github.com/bassetthomas-design/virgil-2.0/pull/89) | docs: lot H-doc — preuves finales D2 à H |
| `5ab0db9` | [#90](https://github.com/bassetthomas-design/virgil-2.0/pull/90) | Lot Bureau — protéger le cache d’icônes et les données du shell (alpha.8) |
| `0da6adb` | [#91](https://github.com/bassetthomas-design/virgil-2.0/pull/91) | Lot T2a — Réparation : parseur PowerShell et scripts |
| `287649d` | [#92](https://github.com/bassetthomas-design/virgil-2.0/pull/92) | Bureau B3 : stabiliser le relevé des iconcache verrouillés |
| `373118f` | [#93](https://github.com/bassetthomas-design/virgil-2.0/pull/93) | Lot T3a — Corbeille : sélection et motifs des lignes inéligibles |
| `35fe5fc` | [#94](https://github.com/bassetthomas-design/virgil-2.0/pull/94) | Lot T2b — Réparation : progression par étape et bilan en français |
| `7193729` | [#95](https://github.com/bassetthomas-design/virgil-2.0/pull/95) | Lot T3b — Corbeille : vidage vérifié |
| `857d217` | [#96](https://github.com/bassetthomas-design/virgil-2.0/pull/96) | T3c · Résumé de la sélection et informations complémentaires |
| `58dd7b1` | [#97](https://github.com/bassetthomas-design/virgil-2.0/pull/97) | T4 — Conserver les priorités par source |
| `aedc5fc` | [#98](https://github.com/bassetthomas-design/virgil-2.0/pull/98) | T5a — Clarifier l’écran Diagnostic |
| `39cf280` | [#99](https://github.com/bassetthomas-design/virgil-2.0/pull/99) | T5b — Diagnostic de charge processeur et mémoire |
| `26e0c7a` | [#100](https://github.com/bassetthomas-design/virgil-2.0/pull/100) | T6a — Couverture des sources au scan Mises à jour |
| `ec25eb8` | [#101](https://github.com/bassetthomas-design/virgil-2.0/pull/101) | Lot T6b — Steam au scan des mises à jour |
| `a1bb5e7` | [#102](https://github.com/bassetthomas-design/virgil-2.0/pull/102) | Lot 7a — Moteur Connexion & Sécurité réseau en lecture seule |
| `5c99626` | [#103](https://github.com/bassetthomas-design/virgil-2.0/pull/103) | Lot 7b — Bandeau permanent Connexion & Sécurité réseau |
| `73e1cb5` | [#104](https://github.com/bassetthomas-design/virgil-2.0/pull/104) | Lot 7c — Connexion et sécurité réseau sur l’accueil |
| `59387b5` | [#105](https://github.com/bassetthomas-design/virgil-2.0/pull/105) | Lot S1 — Stabilisation des tests fragiles (tests uniquement) |
| `7c7c075` | [#106](https://github.com/bassetthomas-design/virgil-2.0/pull/106) | Lot S1b — stabilisation QuarantineViewModel et UpdatesSequenceViewModel (tests uniquement) |

## Capacités de la préversion publiée

- **Accueil.** Quatre tuiles, accès au dernier rapport et tuile Priorités ouvrant la liste de l’accueil. Le scan et le diagnostic conservent leurs constats par source. L’encadré « CONNEXION ET SÉCURITÉ RÉSEAU », à droite des priorités et hors de la rangée des tuiles, propose « Actualiser », « Voir l’écran Réseau » et « Détails » (« Mesuré à HH:mm »).
- **Nettoyage.** Trois groupes, aperçu, sélection, confirmation, quarantaine et récapitulatif actualisé à chaque changement de sélection. Sections complémentaires repliables avec totaux. Le cache d’icônes et les raccourcis épinglés sont protégés. « Tout sélectionner » inclut la Corbeille disponible. Le vidage est vérifié ou le résultat indique pourquoi Windows ne peut pas le confirmer.
- **Diagnostic.** Relevé ponctuel CPU/RAM/disques/réseau/GPU/processus, note et « À vérifier en priorité » (trois constats au plus). « Actualiser le diagnostic » relance le relevé. « Voir les processus » apparaît si une charge CPU ou mémoire d’au moins 85 % est confirmée sur deux relevés espacés de 5 s à 5 min.
- **Ressources.** Écran séparé de Diagnostic, avec mesures CPU/RAM et actions sur les processus après confirmation.
- **Applications.** Inventaire, recherche et désinstallation officielle individuelle ou multiple après confirmation, y compris les jeux Steam via le client officiel.
- **Mises à jour.** Scan WinGet, Windows Update, Microsoft Store et Steam, avec résultats conservés en cas de vérification partielle. Steam affiche les états des jeux et propose « Ouvrir Steam » quand le client est fermé ou hors ligne. « Tout mettre à jour » et « Mettre à jour » pour un seul élément proposent un aperçu, une confirmation et un contrôle final.
- **Réseau.** État réseau et lien vers Réparation. Le bandeau permanent et l’encadré d’accueil ouvrent cet écran existant.
- **Réparation.** « Vérifier » et « Scanner et réparer » limités aux commandes autorisées, dont SFC, DISM et `chkdsk /scan` en ligne. Vérification du disque système planifiable au prochain redémarrage et remise à zéro de Windows Update, sur sélection et confirmation après analyse. Progression par étape, bilans en français et correctif Services pour la file d’impression.

Le bandeau « Connexion & Sécurité réseau » affiche les badges « Internet : … » et « VPN : … » dans l’en-tête de tous les écrans. Un clic ouvre Réseau. L’infobulle indique l’état Internet, le VPN et la protection, avec « Mesure locale en lecture seule uniquement. ». Le moteur (routes, Wi-Fi et sondes d’accès Internet) était déjà livré dans l’alpha.9 sans interface. Il ne modifie aucun réglage système, ne demande aucune élévation et n’installe rien. L’affichage ne promet pas une sécurité totale. La mesure périodique a lieu au plus toutes les 30 s, les transitions sont confirmées après 5 s et les notifications suivent un délai de 30 s par clé.

Le Microsoft Store utilise la voie officielle de Windows. Steam a un suivi par jeu. Les jeux des autres lanceurs, dont Epic Games et Ubisoft Connect, sont signalés comme non pris en charge automatiquement. Le BIOS et le firmware sont affichés pour information.

Le visage de Virgil réagit à l’état de l’application et ses répliques s’affichent progressivement. « Signaler un problème » ouvre une issue GitHub préremplie dans le navigateur sans envoi par Virgil.

## Limites et travail restant

- La source Windows du Diagnostic ne fournit pas la température CPU et affiche les données non exposées comme indisponibles. Aucun monitoring continu du système ni budget CPU mesuré en usage Windows.
- Aucun projet Agent/Tray ni icône Virgil dans la zone de notification.
- Réparation reste partielle : le lot R1 ajoute la réinitialisation du cache du Microsoft Store (`wsreset.exe`) sur sélection et confirmation après analyse. Le lot R2 ajoute la vérification planifiée du disque système au prochain redémarrage. Le lot R3 ajoute la remise à zéro complète de Windows Update (`virgil-update-reset`) après analyse, confirmation et vérification du point de restauration. L’hôte réel reste à essayer sur un PC Windows.
- Démarrage reste partiel : logos et recommandations absents, impact et temps avant/après non mesurés.
- L’historique propose le détail et l’export texte, sans export PDF ni comparaison.
- Le fil Communication et les répliques de Virgil n’offrent aucun réglage de ton.

Démarrage propose l’inventaire, l’activation, la désactivation et la restauration des autorisations reconnues, avec sauvegarde et confirmation. Le [README](../README.md#les-onze-écrans-de-navigation) répertorie les onze écrans. Les cibles historiques de fusion de Ressources et de compléments du Diagnostic, du Démarrage, de la Réparation et du ton restent des références de cadrage. Le lot S2 est documentaire et ne traite aucune issue.
