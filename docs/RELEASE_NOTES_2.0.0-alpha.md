# Virgil 2.0.0-alpha — notes de version

Première distribution alpha installable Windows 10/11 x64. Diagnostic détaillé, analyse et propositions après validation ; Nettoyage avec aperçu récapitulatif, quarantaine, restauration vérifiée et purge confirmée. Les parcours existants Applications, Ressources, Mises à jour et Interventions conservent leurs portes ConfirmedPlan et leurs protections.

L'installateur inclut l'application et Virgil.ElevatedHelper.exe ainsi que le runtime .NET 8 self-contained. Aucun SDK ni installation .NET séparée n'est requis. Installation administrateur par machine dans Program Files\Virgil, interface française, menu Démarrer et bureau optionnel décoché. L'application s'exécute sans élévation ; seul le helper demande l'élévation pour les commandes autorisées. Fermer Virgil proprement avant une mise à jour : le mutex bloque l'installation tant qu'il est ouvert, y compris en mode silencieux ; aucun kill automatique.

## Alpha non signée

Exception de distribution explicitement retenue pour cette alpha : aucun certificat ni signature de code. Microsoft Defender SmartScreen peut afficher « Windows a protégé votre ordinateur » ou un éditeur inconnu. Télécharger uniquement depuis la GitHub Release officielle et vérifier le SHA-256 avec le fichier `.sha256`. Le hash vérifie l'intégrité du téléchargement ; il ne remplace pas une signature de l'éditeur. La signature reste à prévoir pour une distribution ultérieure.

## Données et désinstallation

Les binaires de Program Files sont retirés. Paramètres `%APPDATA%\Virgil\settings.json`, rapports `%APPDATA%\Virgil\reports` et logs `%LOCALAPPDATA%\Virgil\logs` restent présents par défaut. Une case française décochée permet leur suppression explicite pour le seul compte exécutant le désinstalleur. Avec des identifiants administrateur distincts, il s'agit du compte administrateur : aucune recherche ni suppression dans les autres profils. En mode silencieux, toutes ces données sont conservées.

La quarantaine `%LOCALAPPDATA%\Virgil\Quarantine` de chaque compte contient des fichiers de l'utilisateur : elle n'est JAMAIS supprimée par le désinstalleur, même avec l'option de suppression. Restaurer ou purger explicitement depuis Virgil avant désinstallation si souhaité.

## Limites connues et actions non incluses

Alpha : pas de garantie de stabilité ou de mesure de performance sur une session Windows représentative. Capteurs, SMART, éditeurs ou compteurs indisponibles sont signalés ; la note peut être partielle. Un processus inaccessible ou protégé n'est pas fermé. Pas de fermeture forcée automatique après un échec de fermeture propre.

La vérification des mises à jour de Virgil via GitHub Releases reste au lot 2b. Pas de modification du démarrage, du registre ou des services, de suppression des restes de registre, de tray, de notifications, de service de fond ou d'entrée de chat active. Les entrées Démarrage/Réseau non livrées affichent un refus français et n'exécutent aucune action. La vérification manuelle installation/lancement/élévation/mise à jour/désinstallation sur Windows sans SDK et en compte standard reste nécessaire avant création du tag alpha. Le workflow sur tags `v*` crée une prerelease avec le setup et son SHA-256 ; le lot 2a ne crée aucun tag.
