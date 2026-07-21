# Feuille de route professionnelle Virgil 2.0

Date: 2026-06-17
Principe: chaque action sensible doit etre precedee d'une lecture, d'une previsualisation, d'une validation explicite et d'un rapport local.

## 1. Module mises a jour

Objectif: informer et guider, sans installation automatique.

- Windows Update en lecture et lancement controle depuis l'interface Windows quand possible.
- Winget: inventaire, versions installees, versions disponibles, commande preparee mais non executee sans validation.
- Applications Microsoft Store si une API ou commande fiable est disponible.
- Detection des pilotes en lecture seule avec source identifiee.
- Aucune installation sans validation utilisateur.
- Popup garde-fou par categorie: Windows, winget, Store, pilotes.
- Rapport apres action: element, version avant, version cible, resultat, erreurs.

Livrable recommande:

1. Inventaire lecture seule.
2. Preview des actions possibles.
3. Execution unitaire avec confirmation.
4. Rapport local date.

## 2. Interventions ciblees

Objectif: proposer des reparations explicites, limitees et reversibles quand c'est possible.

Etat PR #19: V1 livre pour les interventions ciblees de base.

- Diagnostic lecture seule raccorde au scan approfondi.
- Module UI dedie avec parcours guide et validation par action.
- Helper eleve produit pour les commandes administrateur.
- Elevation demandee uniquement apres confirmation explicite.
- Actions reseau V1: flush DNS, renouvellement DHCP, reset Winsock, reset TCP/IP.
- Actions Windows V1: SFC, DISM ScanHealth, DISM RestoreHealth, CHKDSK `/scan`.
- Action interface V1: relance douce d'Explorer.
- Rapport de session local en memoire.
- Take Ownership non implemente.

- Reparations reseau: diagnostic, DNS, Winsock, renouvellement IP.
- Verifications systeme: SFC, DISM, verification disque.
- Applications au demarrage: lecture seule, puis activation/desactivation ciblee.
- Services: lecture, statut, actions limitees apres confirmation.
- Processus: affichage, fin de processus uniquement apres validation claire.
- Desinstallation propre: inventaire, source, confirmation, rapport.
- Reparation d'applications quand la plateforme expose une action fiable.

Garde-fous communs:

- Aucun script cache.
- Affichage de la commande ou de l'action avant execution.
- Confirmation explicite par action.
- Blocage des actions globales trop larges.
- Rapport apres action, meme en cas d'echec.
- Annulation ou etat partiel clairement signale.

### Interventions ciblees avancees: Take Ownership

Take Ownership ne doit pas etre implemente dans cette PR. Sa conception doit rester dans ce module avance.

Garde-fous obligatoires:

- Droits administrateur explicites.
- Fichier ou dossier cible uniquement.
- Affichage du chemin exact.
- Affichage du proprietaire actuel.
- Affichage du nouveau proprietaire.
- Sauvegarde des ACL avant modification.
- Bouton Restaurer les permissions.
- Previsualisation de l'impact.
- Confirmation explicite.
- Rapport apres action.
- Aucune execution automatique.
- Aucune action sur un disque complet.
- Aucune action recursive sur `Windows`, `System32`, `Program Files` ou le profil utilisateur complet.
- Refus des chemins critiques par defaut.
- Journalisation locale.

## Nettoyage complet guidé V2

État branche `feat/cleanup-guided-v2` : V2 implémentée avec garde-fous stricts.

- Nettoyage sûr et avancé séparés, validation par zone et aucune action globale.
- Corbeille via API Windows avec estimation tolérante aux erreurs et confirmation renforcée.
- Caches navigateurs uniquement; identifiants, cookies, historique, sessions, profils et extensions protégés.
- Cache Windows Update, Delivery Optimization, Store et Windows.old en information seulement jusqu'à une orchestration fiable.
- Prefetch jamais nettoyé.
- Analyse du Bureau et des Téléchargements en lecture seule; ouvrir/ignorer/marquer à revoir uniquement.
- Photos, vidéos, documents, archives, ISO, projets, sauvegardes, jeux et applications protégés.
- Garde de réparation de droits ciblée disponible; aucune exécution `takeown` tant que le helper ne peut pas recevoir une cible exacte de manière strictement allowlistée.
- Rapports V1 et analyse approfondie enrichis sans exécution.
- Le script utilisateur reste une inspiration de zones, jamais une logique ou une commande copiée.

Étapes ultérieures conditionnelles :

1. Ajouter au helper un protocole de cible exacte sans chaîne de commande libre, puis auditer séparément la réparation de droits.
2. Orchestrer Windows Update/Delivery Optimization uniquement avec gestion fiable des services et vérification post-action.
3. Ajouter une restauration seulement lorsqu'une API Windows fiable la permet.

## 3. Ressources et monitoring

Objectif: passer des snapshots actuels a une vue temps reel fiable.

- Vue temps reel CPU, RAM, disque et reseau.
- Courbes simples et lisibles, avec echantillonnage raisonnable.
- Processus consommateurs CPU/RAM.
- Temperatures uniquement si une source fiable est disponible.
- Aucune valeur inventee.
- Etat degrade clair si un compteur est inaccessible.

Livrable recommande:

1. Service de monitoring cancellable.
2. Vue Ressources dediee.
3. Tests des seuils et fallbacks.
4. Controle de performance sur machines modestes.

## 3 bis. Applications et desinstallateur

Etat branche `feat/applications-uninstaller-v1` : V1 implementee avec garde-fous stricts.

- Inventaire registre, WinGet et Store en lecture seule.
- Classification des applications en desinstallable, attention, protege, inconnu et Store.
- Desinstallation individuelle uniquement via MSI, desinstalleur officiel ou WinGet exact, lancee depuis les details de l'application.
- Aucun bouton `DESINSTALLER` direct dans les cartes de liste.
- Confirmation explicite obligatoire pour toute desinstallation.
- Confirmation renforcee obligatoire pour `ApplicationRiskLevel.Caution`.
- Applications Store limitees a l'ouverture des parametres Windows.
- Blocage des pilotes, securite, runtimes, frameworks et composants systeme.
- Commandes dangereuses, suppression par dossier, commandes chainees et profils utilisateur refuses.
- Scan des restes en lecture seule apres lancement du desinstalleur officiel.
- Aucune suppression automatique de donnees personnelles et aucun "delete all remnants".
- Rapports locaux `ApplicationManagement` pour inventaire et desinstallation.

Etapes ulterieures conditionnelles :

1. Ajouter une comparaison d'inventaires avant/apres sans supposer le succes d'un assistant externe.
2. Ajouter un export dedie des restes pour revue manuelle.
3. Envisager une suppression technique ciblee seulement avec confirmations separees, allowlist stricte et exclusion explicite des donnees personnelles.

## 4. Historique et rapports

Objectif: rendre les resultats tracables localement, sans telemetrie.

Etat branche `feat/reports-history-v1`: V1 implementee.

- Historique local des scans rapides/approfondis, nettoyages, mises a jour, interventions et ressources.
- Format interne JSON stable sous `%APPDATA%\Virgil\reports`.
- Ecriture atomique et rotation stricte aux 30 derniers evenements.
- Dernier rapport persistant avec repli vers le rapport memoire.
- Export TXT uniquement manuel via la boite de dialogue Windows.
- Vue simple par defaut et details techniques masques.
- Sanitation des chemins de profil, tokens, mots de passe, secrets et cles.
- Aucun envoi en ligne, aucune telemetrie, aucune synchronisation.
- Comparaison de deux scans reportee en V2.

Livrable recommande:

1. Format de rapport stable : termine V1.
2. Stockage local utilisateur : termine V1.
3. Export manuel TXT : termine V1.
4. Vue historique compacte : termine V1 ; filtres avances reportes.

## 5. Finition produit

Objectif: rendre la preview distribuable et maintenable.

- Parametres locaux.
- Installateur.
- Raccourcis.
- Signature si disponible.
- Gestion des versions.
- Mise a jour de Virgil.
- Restauration apres erreur.
- Documentation utilisateur.
- Checklist de release: restore, tests, build, publish, smoke test visuel.
