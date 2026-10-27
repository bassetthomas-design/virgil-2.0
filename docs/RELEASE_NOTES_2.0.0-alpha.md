# Virgil 2.0.0-alpha

> Document historique — voir [PROJECT_STATUS](PROJECT_STATUS.md) pour l’état actuel.

Première alpha installable pour Windows 10/11 x64. L’application et son helper sont autonomes : aucun SDK ni runtime .NET à installer. Cette note décrit le contenu préparé ; le tag et la release seront publiés après les essais manuels Windows sans SDK.

## Contenu

- Diagnostic en lecture seule : CPU, RAM, disques, réseau, GPU et processus, valeurs indisponibles expliquées, note partielle et trois priorités déterministes.
- Propositions de nettoyage, de mises à jour et de fermeture de processus : aperçu et validation explicite, identité et protections des processus revérifiées avant action.
- Nettoyage en trois groupes : Sûr, À vérifier et Restes de logiciels. Dossiers personnels exclus des sélections automatiques ; confirmation critique individuelle si admissible.
- Quarantaine locale : conservation réglable de 1 à 30 jours (7 par défaut), restauration vérifiée par taille et SHA-256 sans écrasement. L’expiration propose un effacement confirmé ; elle ne supprime rien seule.
- Historique et rapports locaux ; installateur par machine dans Program Files, helper distinct et élevé seulement pour ses commandes autorisées.
- Vérification des versions de Virgil sur GitHub Releases, y compris les prereleases du canal alpha, sans jeton, télémétrie ni identifiant utilisateur.

## Vérifier et mettre à jour Virgil

Le panneau en haut de l’application affiche la version issue du tag. Cliquez sur **Vérifier les mises à jour de Virgil**. Le résultat indique **À jour**, **Nouvelle version disponible** avec version et date, ou **Vérification impossible** avec motif : hors ligne/délai dépassé, limite GitHub 403/429, réponse invalide ou aucune release publiée.

Une vérification discrète au démarrage est activée par défaut, au plus une tentative par 24 heures, échecs compris. Décochez **Vérifier au démarrage** puis cliquez sur **Enregistrer ce réglage** pour la désactiver. Le bouton manuel reste disponible. Aucun son, notification système, popup ou prise de focus.

Une nouvelle version propose uniquement **Ouvrir la page de release** dans votre navigateur. Téléchargez vous-même l’installateur depuis cette page, vérifiez son SHA-256 puis fermez Virgil dans toutes les sessions Windows avant de lancer le setup. Aucun téléchargement ni installation automatique. En cas d’indisponibilité du mutex global, un avertissement est journalisé ; la fermeture dans toutes les sessions reste indispensable. Aucun tag n’est créé par l’application.

## SHA-256, signature et SmartScreen

L’installateur officiel et son fichier `.sha256` sont joints à la même release GitHub de `bassetthomas-design/virgil-2.0`. Vérifiez le fichier téléchargé avec PowerShell :

```powershell
Get-FileHash .\Virgil-2.0.0-alpha-win-x64-setup.exe -Algorithm SHA256
```

Comparez intégralement le résultat avec le fichier `.sha256`. En cas de différence, ne lancez pas le setup. Le hash détecte une différence de fichier ; il ne remplace pas une signature d’éditeur.

**Exception alpha non signée explicitement acceptée pour cette distribution.** Aucun certificat de signature n’est livré. Windows SmartScreen peut afficher « éditeur inconnu » ou bloquer le lancement faute de réputation. Vérifiez la provenance GitHub et le hash avant de décider de poursuivre ; ne désactivez pas globalement SmartScreen ou l’antivirus. La signature de code sera traitée séparément.

## Limites connues

- Alpha : sauvegardez vos données importantes. Les essais manuels d’installation, compte standard, élévation, redémarrage, mise à jour multi-session et désinstallation sur Windows sans SDK restent nécessaires avant le tag.
- Températures, SMART complet, VRAM totale et débits par programme peuvent être indisponibles selon Windows et le matériel. Une note partielle de 100 n’atteste pas la santé des éléments non observés. Objectif CPU du Diagnostic restant à mesurer sur Windows.
- Le périmètre complet Windows/pilotes/Store/jeux, les modifications du démarrage et du registre, les sauvegardes et points de restauration associés relèvent de la Phase 3. Les entrées non livrées restent bloquées ; aucune commande libre du chat, tray ou service de fond.
- L’API publique GitHub peut être hors ligne ou limitée ; Virgil affiche cette impossibilité et conserve sa version actuelle. Le canal alpha inclut aussi les versions stables, comparées selon SemVer 2.0.
- Désinstallation : paramètres, rapports et logs conservés par défaut. Leur suppression exige une case décochée, limitée au compte exécutant le désinstalleur. La quarantaine `%LOCALAPPDATA%\Virgil\Quarantine` est toujours conservée, même après ce choix ; restaurez vos fichiers avant de retirer l’application si nécessaire. Aucun autre profil Windows n’est parcouru.
