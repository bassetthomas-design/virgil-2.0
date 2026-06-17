# Feuille de route professionnelle Virgil 2.0

Date: 2026-06-16  
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

## 4. Historique et rapports

Objectif: rendre les resultats tracables localement, sans telemetrie.

- Historique local des scans et nettoyages.
- Export JSON ou texte.
- Rapports dates.
- Journal des actions.
- Aucune telemetrie.
- Rotation ou limite de taille des journaux.

Livrable recommande:

1. Format de rapport stable.
2. Stockage local utilisateur.
3. Export manuel.
4. Vue historique filtrable.

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
