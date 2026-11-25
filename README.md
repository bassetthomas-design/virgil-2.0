# Virgil 2.0

**Statut :** préversion Windows `v2.0.0-alpha.10` publiée.

Virgil est un assistant PC Windows local : diagnostic ponctuel, maintenance après confirmation et interface WPF sombre/orange. La réécriture propre remplace l'import de Virgil v1.

## Installer l'alpha

Télécharger `Virgil-2.0.0-alpha.10-win-x64-setup.exe` et `Virgil-2.0.0-alpha.10-win-x64-setup.exe.sha256` depuis la même [release alpha.10 du dépôt officiel](https://github.com/bassetthomas-design/virgil-2.0/releases/tag/v2.0.0-alpha.10). Publication .NET 8 self-contained Windows x64 : aucun SDK requis. L’installateur Inno Setup distribue aussi le helper élevé.

Dans PowerShell, depuis le dossier de téléchargement, calculer l’empreinte de l’installateur. Adapter le chemin si le fichier se trouve dans un autre dossier.

```powershell
Get-FileHash .\Virgil-2.0.0-alpha.10-win-x64-setup.exe -Algorithm SHA256
```

Comparer intégralement le résultat à l’empreinte du fichier `.sha256` publié. En cas de différence, ne pas lancer l’installateur. Le hash détecte une différence de fichier et ne remplace pas une signature d’éditeur.

L’installateur de cette alpha est non signé et aucun certificat de signature n’est livré. Windows SmartScreen peut afficher « éditeur inconnu » ou bloquer le lancement faute de réputation. Vérifier la provenance GitHub et le hash avant de décider de poursuivre. Pour poursuivre depuis le bandeau, ouvrir « Informations complémentaires », puis « Exécuter quand même ». Garder SmartScreen et l’antivirus activés.

Un avertissement alpha s’affiche à chaque lancement.

## Les onze écrans de navigation

« Livré » décrit le périmètre indiqué, sans attester toutes les ambitions du module. Les livraisons et leurs sources figurent dans le [statut](docs/PROJECT_STATUS.md) et la [preuve documentaire](docs/proofs/Lot-S2-Docs.md).

| Écran | Capacité actuelle | État |
| --- | --- | --- |
| Accueil | Quatre tuiles, accès au dernier rapport et tuile Priorités ouvrant la liste de l’accueil. Le scan et le diagnostic conservent leurs constats par source. L’encadré « CONNEXION ET SÉCURITÉ RÉSEAU », à droite des priorités et hors de la rangée des tuiles, propose « Actualiser », « Voir l’écran Réseau » et « Détails » (« Mesuré à HH:mm »). | livré |
| Scan | Scan rapide ou analyse approfondie, en lecture seule | livré |
| Nettoyage | Trois groupes, aperçu, sélection, confirmation, quarantaine et récapitulatif actualisé à chaque changement de sélection. Sections complémentaires repliables avec totaux. Le cache d’icônes et les raccourcis épinglés sont protégés. « Tout sélectionner » inclut la Corbeille disponible. Le vidage est vérifié ou le résultat indique pourquoi Windows ne peut pas le confirmer. | livré |
| Diagnostic | Relevé ponctuel CPU/RAM/disques/réseau/GPU/processus, note et « À vérifier en priorité » (trois constats au plus). « Actualiser le diagnostic » relance le relevé. « Voir les processus » apparaît si une charge CPU ou mémoire d’au moins 85 % est confirmée sur deux relevés espacés de 5 s à 5 min. | partiel |
| Ressources | Écran séparé de Diagnostic, avec mesures CPU/RAM et actions sur les processus après confirmation. | partiel |
| Applications | Inventaire, recherche et désinstallation officielle individuelle ou multiple après confirmation, y compris les jeux Steam via le client officiel. | livré |
| Mises à jour | Scan WinGet, Windows Update, Microsoft Store et Steam, avec résultats conservés en cas de vérification partielle. Steam affiche les états des jeux et propose « Ouvrir Steam » quand le client est fermé ou hors ligne. « Tout mettre à jour » et « Mettre à jour » pour un seul élément proposent un aperçu, une confirmation et un contrôle final. | partiel |
| Démarrage | Inventaire ; activation, désactivation et restauration des autorisations reconnues, avec sauvegarde et confirmation | partiel |
| Réseau | État réseau et lien vers Réparation. Le bandeau permanent et l’encadré d’accueil ouvrent cet écran existant. | livré |
| Réparation | « Vérifier » et « Scanner et réparer » limités aux commandes autorisées, dont SFC, DISM et `chkdsk /scan` en ligne. Vérification du disque système planifiable au prochain redémarrage et remise à zéro de Windows Update, sur sélection et confirmation après analyse. Progression par étape, bilans en français et correctif Services pour la file d’impression. | partiel |
| Rapports | Historique local, détail et export texte | livré |

La vue `VirgilReleaseView` permet aussi de vérifier les versions GitHub et d'ouvrir leur page. Elle n'est pas un douzième bouton de navigation.

Le bandeau « Connexion & Sécurité réseau » affiche les badges « Internet : … » et « VPN : … » dans l’en-tête de tous les écrans. Un clic ouvre Réseau. L’infobulle indique l’état Internet, le VPN et la protection, avec « Mesure locale en lecture seule uniquement. ». Le moteur (routes, Wi-Fi et sondes d’accès Internet) était déjà livré dans l’alpha.9 sans interface. Il ne modifie aucun réglage système, ne demande aucune élévation et n’installe rien. L’affichage ne promet pas une sécurité totale. La mesure périodique a lieu au plus toutes les 30 s, les transitions sont confirmées après 5 s et les notifications suivent un délai de 30 s par clé.

Le Microsoft Store utilise la voie officielle de Windows. Steam a un suivi par jeu. Les jeux des autres lanceurs, dont Epic Games et Ubisoft Connect, sont signalés comme non pris en charge automatiquement. Le BIOS et le firmware sont affichés pour information.

Le visage de Virgil réagit à l’état de l’application et ses répliques s’affichent progressivement. « Signaler un problème » ouvre une issue GitHub préremplie dans le navigateur sans envoi par Virgil.

## Limites actuelles

- La source Windows du Diagnostic ne fournit pas la température CPU et affiche les données non exposées comme indisponibles. Aucun monitoring continu du système ni budget CPU mesuré en usage Windows.
- Aucun projet Agent/Tray ni icône Virgil dans la zone de notification.
- Réparation reste partielle : après analyse, la réinitialisation du cache du Microsoft Store (`wsreset.exe`) est disponible sur sélection et confirmation si l’exécutable est présent. Le lot R2 ajoute la vérification planifiée du disque système au prochain redémarrage. Le lot R3 ajoute la remise à zéro complète de Windows Update (`virgil-update-reset`) après analyse, confirmation et vérification du point de restauration. L’hôte réel reste à essayer sur un PC Windows.
- Démarrage reste partiel : logos et recommandations absents, impact et temps avant/après non mesurés.
- L’historique propose le détail et l’export texte, sans export PDF ni comparaison.
- Le fil Communication et les répliques de Virgil n’offrent aucun réglage de ton.

La publication ne prouve pas une validation manuelle complète sur un vrai PC Windows. Les notes de l’alpha.10 précisent que Steam, le Store, SFC/CHKDSK sur un vrai PC et un vrai portail Wi-Fi captif n’ont pas encore été testés de bout en bout. Les essais manuels d’installation, de compte standard et de désinstallation ne sont pas attestés ici.

## Sécurité et références

Les actions à effet utilisent un aperçu et un `ConfirmedPlan`. Le nettoyage conserve ses gardes et sa quarantaine ; les changements système concernés exigent un point de restauration vérifié. Les processus protégés ne sont pas proposés à la fermeture.

- [Statut et livraisons](docs/PROJECT_STATUS.md) : suivi des préversions et des livraisons.
- [Spécification des actions](docs/ACTIONS.md) : référence historique pour les actions et les cibles.
- [Architecture](docs/ARCHITECTURE.md) : référence pour les couches et les protections.
- [Roadmap](docs/ROADMAP.md) : cadrage historique.
- [Ton et communication](docs/modules/CHATBOX_MODULE.md).
- [Règles de contribution](AGENTS.md).

La CI GitHub Actions Windows est la référence de build et de distribution. La présence de cette documentation ne constitue pas une attestation de réussite d'un run.

## Licence

Virgil 2.0 est distribué sous licence [GPL-3.0](LICENSE).
