# Module Interventions ciblees V1

Date: 2026-06-17

## Objectif

Le module Interventions ciblees V1 fournit un parcours guide de diagnostic, validation et execution limitee. Il ne lance aucune action au demarrage, pendant un scan rapide ou pendant une analyse approfondie.

## Actions disponibles

| Action | Categorie | Droits | Risque | Commande ou action |
| --- | --- | --- | --- | --- |
| Relancer l'Explorateur Windows | Interface | Non | Faible | Fermeture douce de `explorer.exe`, puis relance |
| Vider le cache DNS | Reseau | Admin apres confirmation | Faible | `ipconfig.exe /flushdns` |
| Renouveler la configuration IP | Reseau | Admin apres confirmation | Modere | `ipconfig.exe /release`, puis `ipconfig.exe /renew` |
| Analyser les fichiers systeme avec SFC | Systeme | Admin apres confirmation | Modere | `sfc.exe /scannow` |
| Analyser l'image Windows avec DISM | Systeme | Admin apres confirmation | Modere | `dism.exe /Online /Cleanup-Image /ScanHealth` |
| Reparer l'image Windows avec DISM | Systeme | Admin apres confirmation | Sensible | `dism.exe /Online /Cleanup-Image /RestoreHealth` |
| Reinitialiser Winsock | Reseau | Admin apres confirmation | Sensible | `netsh.exe winsock reset` |
| Reinitialiser TCP/IP | Reseau | Admin apres confirmation | Sensible | `netsh.exe int ip reset` |
| Analyser le disque systeme avec CHKDSK | Stockage | Admin apres confirmation | Modere | `chkdsk.exe <system-drive> /scan` |

## Garde-fous

- Aucune commande libre n'est acceptee.
- Le helper eleve recoit uniquement un identifiant d'action allowliste.
- Les fichiers de requete/resultat du helper restent sous `%LOCALAPPDATA%\Virgil\Temp`.
- Le nonce de requete est controle et expire apres 10 minutes.
- L'elevation UAC est demandee uniquement apres confirmation d'une action administrateur.
- Les actions sensibles demandent une validation separee.
- Les boutons du module sont desactives pendant l'analyse ou l'execution.
- Le rapport de session est produit meme en cas d'erreur partielle.
- Les erreurs techniques sont resumees en messages lisibles dans l'UI.

## Interdictions V1

- Aucun Take Ownership.
- Aucun `takeown`.
- Aucun `icacls`.
- Aucun CHKDSK `/f`, `/r` ou `/x`.
- Aucun DISM `/ResetBase`.
- Aucun redemarrage automatique.
- Aucune suppression de fichier.
- Aucune execution depuis le scan rapide ou l'analyse approfondie.
- Aucune installation de composant externe.

## Integration scan

- Scan rapide: diagnostics systeme uniquement, sans diagnostic interventions.
- Analyse approfondie: ajoute une previsualisation lecture seule des interventions disponibles et recommandees.
- L'analyse approfondie ne lance ni helper eleve, ni action locale.

## Limitations restantes

- Pas de rollback automatique pour les actions Windows natives.
- Pas de persistance disque des rapports d'interventions.
- Pas d'inventaire avance des services, programmes au demarrage ou applications.
- Les actions longues comme SFC et DISM ne doivent pas etre interrompues brutalement apres demarrage.
- Take Ownership reste explicitement hors perimetre de cette PR.
