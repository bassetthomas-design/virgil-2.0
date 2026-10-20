# Virgil 2.0 — Spécifications des actions validées

Ces specifications decrivent la cible produit ; les garanties de securite de ARCHITECTURE.md et ROADMAP.md (apercu, ConfirmedPlan, point de restauration avant changement systeme) priment en cas de doute.

| Action | Phase | Statut |
| --- | --- | --- |
| 0 — Tout mettre à jour | 3 | Exigence |
| 1 — Diagnostic | 1 ; 4 pour la vue en direct continue et l’historique | Validée 2026-10-18 |
| 2 — Nettoyage | 1 | Validée 2026-10-18 |
| 3 — Mises à jour « pack all inclusive » | 3 | Validée 2026-10-18 |
| 4 — Applications | 3 ; lien avec le Nettoyage en 1 pour les restes après désinstallation | Validée 2026-10-18 |
| 5 — Réparations | 3 | Validée 2026-10-18 |
| 6 — Ressources | 1 | Fusionnée dans l’Action 1 le 2026-10-18 |
| 7 — Démarrage | 3 | Validée 2026-10-18 |
| 8 — Historique | 4 | Validée, priorité basse |
| 9 — Surveillance / Tray | 5 | Validée 2026-10-18 |
| 10 — Personnalité | 6 | Validée 2026-10-18, bonus |

## Action 0 : Tout mettre a jour (exigence)

Phase roadmap : Phase 3.

Doit tout couvrir : Windows (Windows Update API), pilotes (Windows Update + eventuellement outils constructeurs NVIDIA/AMD/Intel), logiciels (winget + Microsoft Store), jeux (detection Steam/Epic/Battle.net, mise a jour via leur launcher). Un apercu unique valide par l'utilisateur, puis un rapport.

## Action 1 : Diagnostic (VALIDEE 2026-10-18)

Phase roadmap : Phase 1 ; vue en direct continue et historique : Phase 4.

Vue en direct, facon Gestionnaire des taches tres detaille :

- CPU : charge totale et par coeur, frequence, temperature si dispo.
- RAM : utilisee, en cache, compressee, par programme.
- Disques : lecture/ecriture, temps de reponse, sante SMART.
- Reseau : debit, et quel programme l'utilise.
- GPU : charge, VRAM, temperature.
- Processus : tri par conso, editeur, emplacement, explication en francais de chaque programme.

Analyse "ce qui ne va pas" :

- Note de sante globale + top 3 des problemes.
- Programmes gourmands inutilement (ex : launcher en fond).
- Ce qui ralentit le demarrage.
- Signaux d'alerte : surchauffe, disque malade, erreurs Windows recentes, pilotes en echec, espace disque qui fond.
- Pour chaque probleme, une action proposee (fermer, desactiver au demarrage, nettoyer, mettre a jour), toujours apres validation.

Bonus : historique pour comparer l'etat du PC dans le temps.

## Action 2 : Nettoyage (VALIDEE 2026-10-18)

Phase roadmap : Phase 1.

- Restes de logiciels : TOUS les logiciels desinstalles qui ont encore des traces (Dofus n'est qu'un exemple), chacun sur sa ligne avec sa taille.
- Scan avec annonce claire de ce qui peut/va etre efface, en 3 groupes : Sur (temp, caches, corbeille ; coche par defaut), A verifier (vieux telechargements, Windows.old, gros fichiers oublies ; decoche), Restes de logiciels desinstalles (dossiers AppData/ProgramData/Program Files/Documents + cles de registre de programmes absents ; ex. "Dofus (desinstalle) : taille, nb fichiers, cles registre, derniere utilisation").
- Choix par groupe ou ligne par ligne, puis confirmation recapitulative (taille totale, nb fichiers, elements sensibles).
- Quarantaine (1b livré) : les zones fichiers Sûr, y compris temp/caches, passent aussi par la quarantaine conformément aux filets de sécurité. Rétention de 7 jours réglable de 1 à 30, puis effacement définitif proposé avec confirmation, annoncé dans l’aperçu. Aucun effacement silencieux à l’expiration, à l’ouverture, au scan ou après nettoyage. Les éléments « Expiré » disposent d’une proposition de purge dédiée (nombre, taille), toujours irréversible et soumise à un nouveau ConfirmedPlan Sensitive. Restauration vérifiée possible pendant la rétention. Purge immédiate possible par « Vider la quarantaine maintenant », action distincte irréversible avec aperçu et confirmation. La corbeille conserve son comportement actuel. Les restes et les trois groupes sont livrés en 1c.
- GARDE-FOUS ABSOLUS : jamais toucher aux dossiers perso (Images, Photos, Videos, Documents perso, Bureau, Musique, OneDrive), ni a Windows/systeme critique, ni aux programmes encore installes. Les restes trouves dans les dossiers perso peuvent etre affiches mais jamais coches automatiquement. "Le but c'est pas de vitrifier le PC."

1c livré : Sûr coché par défaut, À vérifier et Restes de logiciels désinstallés décochés. Choix par groupe tri-état ou ligne, aperçu récapitulatif unique (taille, fichiers, éléments sensibles, groupes) et plan lié à toute la sélection. Téléchargements résolu : plus de 90 jours ou plus de 500 Mo, sans chercher les gros fichiers ailleurs. Restes : dossiers et clés de logiciels absents, exclusions des programmes installés, références des services/processus et composants protégés ; ambiguïté ou inventaire incomplet → « à confirmer ». Dossier personnel → uniquement reste identifié, ligne par fichier, Critical avec saisie et quarantaine ; gardes existants conservés. Clés affichées en lecture seule : « Suppression des clés de registre : lot ultérieur (sauvegarde et point de restauration) ». Windows.old reste délégué aux outils Windows.

## Action 3 : Mises a jour "pack all inclusive" (VALIDEE 2026-10-18)

Phase roadmap : Phase 3.

Un seul bouton "Tout mettre a jour" qui met a jour tout le PC : Windows (systeme + securite), signatures Microsoft Defender, pilotes, logiciels (winget), applis Microsoft Store, jeux via leurs launchers (Steam, Epic, Battle.net...). Un apercu unique valide par l'utilisateur, puis execution et rapport final.

## Action 4 : Applications (VALIDEE 2026-10-18)

Phase roadmap : Phase 3 (restes apres desinstallation : lien avec le Nettoyage, Phase 1).

- Liste de tous les programmes (Win32 + Store) avec taille, editeur, version.
- Desinstallation multiple en une fois (selection de plusieurs programmes, une confirmation recapitulative, file d'execution, rapport).
- Affichage PRO : vraies cartes avec le logo/icone de chaque logiciel (ApplicationIconExtractor existe deja), nom, editeur, taille, date d'installation/derniere utilisation. Pas juste une ligne de texte.
- Desinstalleur officiel + recherche et suppression des restes apres desinstallation (lien avec Action 2).
- Protection des programmes sensibles (pilotes, antivirus, composants Windows).

## Action 5 : Reparations (VALIDEE 2026-10-18)

Phase roadmap : Phase 3.

Deux modes :

1. "Verifier" : controle d'integrite en lecture seule (sfc /verifyonly, DISM /CheckHealth puis /ScanHealth, chkdsk en lecture, etat Windows Update, Store, reseau, son, imprimante, Explorer). Resultat : "sain" ou liste des problemes.
2. "Scanner et reparer" : tout ce qui est trouve en 1 (et par le Diagnostic) est propose en reparations, apercu + validation, puis execution via le helper eleve (sfc /scannow, DISM /RestoreHealth, chkdsk planifie, reset Windows Update, wsreset Store, reset reseau, relance Explorer/services audio/spouleur) et rapport. Point de restauration avant les reparations systeme.

Reparations a ajouter : Windows Update bloque, Microsoft Store, son, imprimante.

## Action 6 : Ressources -> FUSIONNEE dans l'Action 1 (2026-10-18)

Phase roadmap : fusionnee dans l'Action 1 (Phase 1).

Pas d'ecran separe : observation CPU/RAM, processus gourmands et fermeture confirmee (avec protection des processus Windows/securite/VPN/materiel) font partie du Diagnostic. Pas de mode jeu ni de boost.

## Action 7 : Demarrage (VALIDEE 2026-10-18)

Phase roadmap : Phase 3.

- Liste de tout ce qui se lance au demarrage (programmes, services, taches planifiees) avec logo, editeur, impact (faible/moyen/fort).
- Recommandation par element : utile / inutile au demarrage / inconnu a verifier.
- Desactiver/reactiver en un clic, sauvegarde automatique pour tout restaurer.
- Mesure du temps de demarrage avant/apres.

## Action 8 : Historique (VALIDEE, priorite basse)

Phase roadmap : Phase 4, priorite basse.

Frise chronologique des actions, totaux (espace libere, MAJ, reparations), comparaison de l'etat du PC, export PDF. Garder simple.

## Action 9 : Surveillance en arriere-plan / Tray (VALIDEE 2026-10-18)

Phase roadmap : Phase 5.

- Icone discrete pres de l'horloge, consommation minimale (budget strict CPU/RAM, ex. <1% CPU moyen, <100 Mo RAM).
- Notifications toast Windows uniquement si vrai probleme (disque plein, surchauffe, MAJ importantes, disque malade), rappel entretien optionnel.
- JAMAIS invasif : aucune fenetre qui vole le focus, respect du mode Ne pas deranger / Assistant de concentration, aucune notif quand un jeu ou une app plein ecran tourne (notifs mises en attente), pas de son par defaut, limitation de frequence.
- N'agit jamais seul : clic sur la notif = ouvre Virgil, validation par l'utilisateur.

## Action 10 : Personnalite (VALIDEE 2026-10-18, bonus)

Phase roadmap : Phase 6.

Ton un peu sarcastique et drole (ex. "8 Go de fichiers temporaires... tu collectionnes ?"), explications en langage simple. Reglage ton sobre / ton humour. Jamais de blague dans les confirmations critiques (risque, suppression) : la clarte prime.

Avatar : en attente, a revoir plus tard.
