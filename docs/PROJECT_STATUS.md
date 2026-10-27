# Virgil 2.0 — Situation après L4b-5 / REL-1

Source unique du statut. Référence : `main` à `4af1cce`, le 27 octobre 2026. Les capacités et leurs limites sont spécifiées dans [ACTIONS.md](ACTIONS.md), l'implémentation dans [ARCHITECTURE.md](ARCHITECTURE.md).

## Distribution

| Tag publié | Date de publication | État |
| --- | --- | --- |
| `v2.0.0-alpha` | 21 octobre 2026 | préversion publiée |
| `v2.0.0-alpha.2` | 26 octobre 2026 | préversion publiée |
| `v2.0.0-alpha.3` | 27 octobre 2026 | préversion publiée |
| `v2.0.0-alpha.4` | 27 octobre 2026 | dernière préversion de cette base |

Les releases GitHub fournissent un installateur x64 et son SHA-256. La publication ne prouve pas une validation manuelle complète sur un vrai PC Windows. Les scénarios manuels sans SDK, compte standard, élévation, mise à jour multi-session et désinstallation ne sont pas attestés ici. Performances en usage réel : non mesuré.

## Phases

| Phase historique | État à cette base | Suite |
| --- | --- | --- |
| 0 — Fondations | Lots 0a–0g et 0f-bis livrés ; dette d'accès OS encore présente dans Core | Séparation cible décrite dans ARCHITECTURE ; pas de clôture totale revendiquée |
| 1 — Diagnostic et Nettoyage | Lots 1a–1e livrés (#50–#54) | Limites des capteurs et cadence ponctuelle conservées |
| 2 — Distribution alpha | 2a/2b livrés (#55/#56) ; quatre tags publiés | Essais manuels non attestés, signature hors de cette livraison |
| 3 — Maintenance | 3a–3d livrés (#57–#60), périmètre décrit ci-dessous | Complétude des mises à jour : cible L6 ; Démarrage : cible L12 ; Réparation : cible L12b |
| 4 — Monitoring continu | Non livré ; Diagnostic ponctuel | Fusion Ressources : cible L7 ; capteurs : cible L9 ; suivi continu à planifier |
| 5 — Agent / tray | Non livré ; aucun projet Agent/Tray | Cible reportée, sous-lot non fixé dans le cadrage L4c |
| 6 — Personnalité | Messages tactiques neutres ; aucun réglage de ton | Catalogue et réglage : cible L11 |

## Socle livré avant les correctifs alpha

0a–0g (#27–#48), correctifs P1 (#46) et 0f-bis (#49) : reconstruction, couches et composition DI, MVVM, contrats de confirmation et séparation Core.Tests/App.Tests. La présence d'accès OS dans Core reste une limite explicite.

1a (#50) : contrat commun et Nettoyage ; 1b (#51) : quarantaine/restauration ; 1c (#52) : trois groupes et restes ; 1d (#53) : Diagnostic en lecture seule ; 1e (#54) : note, top 3 et propositions confirmées. La cadence a ensuite été modifiée en 3d.

2a (#55) : Inno Setup, helper publié et distribution par tag ; 2b (#56) : vérification des versions GitHub, ouverture de page sans installation automatique.

## Lot 3a — PR #58

Livré : exclusions des logiciels installés, regroupement des résultats de Nettoyage, virtualisation et progression/annulation avec `LongOperationViewModel` et `OperationProgressPanel`.

## Lot 3b — PR #59

Livré : scan WinGet renforcé et séquence « Tout mettre à jour », aperçu lié à la sélection, exécution séquentielle, rapport et redémarrage manuel. Les consignes des lanceurs sont comptées réussies sans action réelle ; le Store n'est pas scanné pour ses mises à jour. Cible L6.

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

## Prochain travail

**L5** : harmonisation des opérations longues et textes UI restants selon le cadrage produit, dont L5-2 pour les libellés. Puis cibles de [ROADMAP.md](ROADMAP.md). `loops/plan-lots.md`, cité dans le cadrage, est absent du dépôt à `4af1cce` ; aucun découpage supplémentaire n'est inventé ici.

La CI Windows de chaque PR reste la référence. Aucun nouveau résultat de tests, total de tests ou temps Windows n'est déduit de ces descriptions. Les issues #1–#10 restent hors de ce lot ; leur décision appartient à thoms.
