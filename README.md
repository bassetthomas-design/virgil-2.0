# Virgil 2.0

**Statut :** préversion Windows `v2.0.0-alpha.4` publiée ; dernier lot fusionné : L4b-5 (#66), après REL-1 (#65), base documentaire `main` à `4af1cce`.

Virgil est un assistant PC Windows local : diagnostic ponctuel, maintenance après confirmation et interface WPF sombre/orange. La réécriture propre remplace l'import de Virgil v1.

## Installer l'alpha

Télécharger l'installateur et son SHA-256 depuis la [release alpha.4](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.4). Publication .NET 8 self-contained Windows x64 : aucun SDK requis pour l'utilisateur. L'installateur Inno Setup distribue aussi le helper élevé. Cette alpha est non signée ; le hash ne remplace pas une signature d'éditeur.

## Les onze écrans de navigation

« Livré » décrit le périmètre indiqué, sans attester toutes les ambitions du module. Les preuves et limites sont dans [ACTIONS](docs/ACTIONS.md) et [ARCHITECTURE](docs/ARCHITECTURE.md).

| Écran | Capacité actuelle | État |
| --- | --- | --- |
| Accueil | Tuiles issues des dernières mesures, accès au dernier rapport | livré |
| Scan | Scan rapide ou analyse approfondie, en lecture seule | livré |
| Nettoyage | Trois groupes, aperçu, sélection, confirmation, quarantaine et récapitulatif | livré |
| Diagnostic | Instantané CPU/RAM/disques/réseau/GPU/processus, note partielle et top 3 ; mesure à l'entrée puis sur Actualiser | partiel |
| Ressources | Écran séparé CPU/RAM et actions processus confirmées ; fusion prévue en L7, écran encore présent | partiel |
| Applications | Inventaire, recherche, désinstallation officielle individuelle ou multiple confirmée | livré |
| Mises à jour | WinGet et séquence Windows/Defender/pilotes ; limites des lanceurs ci-dessous | partiel |
| Démarrage | Inventaire ; activation, désactivation et restauration des autorisations reconnues, avec sauvegarde et confirmation | partiel |
| Réseau | État réseau et lien vers Réparation | livré |
| Réparation | Vérifier / Scanner et réparer ; commandes limitées par l'allowlist | partiel |
| Rapports | Historique local, détail et export texte | livré |

La vue `VirgilReleaseView` permet aussi de vérifier les versions GitHub et d'ouvrir leur page. Elle n'est pas un douzième bouton de navigation.

## Limites actuelles

- La température CPU est indisponible dans la source Windows actuelle. Les données non exposées sont affichées comme indisponibles. Capteurs : cible L9 ; aucun monitoring continu livré.
- L'inventaire des pilotes fixe `CanInstallDrivers = false` : le bouton d'installation dédié ne s'affiche jamais. L'installation passe par « Tout mettre à jour ». Les pilotes y sont actuellement décochés par défaut ; la décision produit du 26/10 prévoit de les cocher par défaut et de retirer le code mort en L6+.
- Steam, Epic Games et Ubisoft Connect sont des consignes : chaque étape est comptée réussie sans mise à jour effective. Aucun scan de mises à jour Microsoft Store. Correction et couverture : cible L6.
- Communication : messages `[VIRGIL]` à sens unique, sans saisie de chat. Ton actuel tactique et neutre ; catalogue et réglage : cible L11.
- Impact du démarrage, temps avant/après et budget CPU en usage Windows : non mesuré.

## Sécurité et références

Les actions à effet utilisent un aperçu et un `ConfirmedPlan`. Le nettoyage conserve ses gardes et sa quarantaine ; les changements système concernés exigent un point de restauration vérifié. Les processus protégés ne sont pas proposés à la fermeture.

- [Statut et livraisons](docs/PROJECT_STATUS.md) : source unique du suivi ; prochain lot L5.
- [Spécification des actions](docs/ACTIONS.md) : livré, limites et cibles par lot.
- [Architecture](docs/ARCHITECTURE.md) : code, protections et tests UI.
- [Roadmap](docs/ROADMAP.md) : ordre des cibles.
- [Ton et communication](docs/modules/CHATBOX_MODULE.md).
- [Règles de contribution](AGENTS.md).

La CI GitHub Actions Windows est la référence de build et de distribution. La présence de cette documentation ne constitue pas une attestation de réussite d'un run.
