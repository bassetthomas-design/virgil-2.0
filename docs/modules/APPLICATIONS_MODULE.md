# Module Applications - Desinstallateur Pro V1

## Etat livre

Le module Applications V1 inventorie les applications installees, les classe par risque, lance uniquement un desinstalleur officiel valide depuis l'ecran de details d'une application choisie, puis analyse les restes en lecture seule.

Le module ne contient pas de desinstallation par lot, pas de suppression directe de dossier d'application et pas de bouton global de nettoyage des restes.

## Objectifs

- Voir les applications installees avec nom, editeur, version, source, taille estimee et emplacement quand disponible.
- Identifier les applications avec desinstalleur officiel exploitable.
- Bloquer les composants systeme, pilotes, securite, runtimes et frameworks.
- Traiter les applications Microsoft Store en lecture seule, avec ouverture des parametres Windows uniquement.
- Produire un rapport local pour l'inventaire et pour chaque lancement de desinstalleur.

## Sources d'inventaire

- Registre Windows 64 bits et 32 bits.
- Registre utilisateur courant.
- WinGet en lecture seule quand disponible.
- Packages Store en lecture seule via PowerShell.

Les entrees sont fusionnees par nom et editeur, avec enrichissement par les sources disponibles. Aucune source n'est consideree comme suffisante pour supprimer un dossier.

## Classification

| Classe | Effet UI | Regle principale |
| --- | --- | --- |
| Desinstallable | Details puis confirmation explicite | Desinstalleur officiel, MSI ou ID WinGet exact |
| Attention | Details puis confirmation explicite et renforcee | Applications pouvant contenir projets, profils, presets ou bibliotheques |
| Protege | Desinstallation bloquee | Pilotes, securite, runtimes, frameworks, composants Windows |
| Inconnu | Lecture seule | Informations incompletes ou commande non fiable |
| Store | Parametres uniquement | Pas de suppression Store par Virgil V1 |

## Garde-fous de desinstallation

- Une seule application a la fois.
- Aucun bouton `DESINSTALLER` direct dans les cartes de liste.
- Le lancement est disponible uniquement depuis les details de l'application.
- Le bouton des details ne doit jamais echouer silencieusement : il affiche une confirmation, une annulation, un blocage ou une erreur de lancement.
- Confirmation explicite obligatoire avant tout lancement.
- Confirmation renforcee obligatoire pour `ApplicationRiskLevel.Caution`.
- Validation de commande avant lancement.
- MSI autorise seulement avec code produit et action de desinstallation.
- WinGet autorise seulement avec `--id` exact et `--exact`.
- Commandes chainees, `del`, `rmdir`, `Remove-Item`, `takeown`, `icacls`, pipes et zones utilisateur sont bloquees.
- Les executables locaux doivent ressembler a un desinstalleur officiel.
- Les applications protegees restent bloquees meme si une commande est presente.

## Restes apres desinstallation

Le scan des restes est strictement en lecture seule. Il peut afficher ou reporter :

- restes techniques probables ;
- restes inconnus a revoir ;
- donnees personnelles ou projets proteges ;
- dossiers AppData ambigus.

Virgil V1 ne supprime pas automatiquement les restes. Les actions exposees sont l'ouverture d'emplacement, l'export dans le rapport, l'ignorance ou la revue manuelle.

## Rapport

Les rapports Applications utilisent `ReportKind.ApplicationManagement`.

Ils indiquent :

- inventaire total, desinstallables, proteges, inconnus et attention ;
- methode de desinstallation prevue ou lancee ;
- statut externe possiblement inconnu lorsque l'assistant officiel gere la suite ;
- restes detectes en lecture seule ;
- garantie qu'aucune donnee personnelle n'a ete supprimee automatiquement.

## Limites V1

- Pas de suppression automatique des restes.
- Pas de desinstallation par lot.
- Pas d'analyse registre de restes.
- Pas de reparation ou modification d'application.
- Pas de gestion avancee Store au-dela des parametres Windows.
- Pas de garantie de statut final quand un assistant externe prend la main.
