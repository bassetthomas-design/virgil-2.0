# Module Apparence / animations - Virgil 2.0

## Décision validée

Virgil doit avoir une identité visuelle forte, professionnelle et cohérente : noyau lumineux géométrique orange / ambre, fond sombre, animations sobres et popups HUD propres.

Virgil ne doit pas être une image fixe.

Virgil doit être réalisé comme un composant vectoriel animé en WPF / XAML.

## Objectif

Donner à Virgil une présence visuelle identifiable, sans avatar humain, sans robot 3D, sans voix et sans imitation directe d'une licence existante.

Virgil doit ressembler à un système tactique abstrait : sobre, précis, lumineux, lisible et opérationnel.

## 1. Forme principale de Virgil

Virgil est représenté par un noyau géométrique lumineux.

Caractéristiques :

- segments lumineux ;
- formes triangulaires, losanges ou fragments abstraits ;
- symétrie visuelle ;
- centre plus brillant ;
- contours fragmentés ;
- halo orange contrôlé ;
- effet de matrice énergétique discret.

Virgil ne doit pas être :

- un visage ;
- un robot ;
- une mascotte ;
- une bouche animée ;
- une image PNG fixe ;
- un GIF ;
- une vidéo en boucle.

## 2. Réalisation technique

Virgil doit être créé comme un composant vectoriel animé.

Proposition technique :

```text
Virgil.App
└── Controls
    ├── VirgilCoreControl.xaml
    └── VirgilCoreControl.xaml.cs

Virgil.App
└── Themes
    └── VirgilCoreAnimations.xaml
```

Le composant doit utiliser :

- XAML vectoriel ;
- Path ;
- Shape ;
- Gradient ;
- Opacity ;
- Glow ;
- Storyboard ;
- animations WPF ;
- états visuels.

Avantages :

- léger ;
- net à toutes les tailles ;
- animable ;
- modifiable ;
- cohérent avec le thème ;
- pas d'asset propriétaire ;
- pas de problème de pixelisation.

## 3. Palette visuelle

Palette principale :

- Fond principal : noir bleuté très sombre ;
- Panneaux : gris anthracite / bleu nuit ;
- Noyau Virgil : orange ambre ;
- Accent actif : orange clair ;
- Alerte : orange rouge ;
- Succès : orange doré ou vert très discret ;
- Erreur : rouge-orange ;
- Texte principal : blanc cassé ;
- Texte secondaire : gris froid.

Règle : l'identité doit rester majoritairement orange / ambre.

Le vert doit être rare et discret pour ne pas casser l'identité tactique orange/noir.

## 4. États lumineux de Virgil

Virgil doit réagir selon son état.

### Idle / repos

- Lumière faible ;
- Pulse lent ;
- Noyau stable ;
- Segments légèrement visibles.

Message type :

```text
[VIRGIL]
Système en veille.
```

### Analyse / scan

- Pulse plus rapide ;
- Segments qui s'allument en séquence ;
- Effet scan circulaire ou horizontal ;
- Lueur orange plus forte.

Message type :

```text
[VIRGIL]
Analyse en cours.
Modules actifs : système, stockage, réseau.
```

### Communication

Quand Virgil affiche un message dans la chatbox :

- Le centre s'éclaircit ;
- Les segments centraux deviennent plus intenses ;
- Une petite onde lumineuse se diffuse ;
- Retour progressif au calme.

Objectif : donner l'impression que Virgil communique sans voix.

### Alerte

- Orange plus intense ;
- Pulse court ;
- Bordure du message renforcée ;
- Effet de verrouillage léger.

Message type :

```text
[VIRGIL]
Anomalie détectée.
Priorité : stockage.
```

### Action sensible

- Orange rouge ;
- Animation de verrouillage ;
- Bordure popup plus lumineuse ;
- Fond plus sombre ;
- Message très court.

Message type :

```text
[VIRGIL]
Action sensible.
Validation requise.
```

### Succès

- Flash orange doux ;
- Pulse unique ;
- Retour progressif au repos.

Message type :

```text
[VIRGIL]
Action terminée.
Rapport disponible.
```

### Erreur

- Clignotement bref rouge-orange ;
- Pas d'effet agressif ;
- Message sec ;
- Bouton Voir détails.

Message type :

```text
[VIRGIL]
Action interrompue.
Cause probable : accès refusé.
```

## 5. Présence de Virgil dans l'interface principale

Dans l'interface principale, Virgil apparaît en grand.

Position recommandée : zone centrale ou zone supérieure principale.

Éléments associés :

```text
[ NOYAU VIRGIL ]
[ SCAN COMPLET ]
État système : non analysé / stable / à surveiller / critique
```

Le noyau doit être la signature visuelle du logiciel.

## 6. Présence de Virgil dans les popups

Virgil doit apparaître aussi dans les popups, mais sous une forme réduite et professionnelle.

Il ne doit pas apparaître en grand au centre comme une mascotte.

### Forme retenue

Dans les popups, Virgil apparaît comme :

- mini noyau animé ;
- sceau lumineux ;
- badge d'état ;
- anneau de statut ;
- sigle géométrique ;
- bordure active.

### Rôle

Le mini Virgil sert à comprendre le niveau et l'intérêt de la popup.

Il indique immédiatement :

- information ;
- validation simple ;
- action sensible ;
- action critique ;
- succès ;
- erreur.

## 7. États du mini Virgil dans les popups

### Information

- Orange doux ;
- Bordure fine ;
- Pulse lent.

Usage : scan terminé, rapport disponible, analyse prête.

### Validation simple

- Orange normal ;
- Bordure orange ;
- Pulse discret.

Usage : nettoyage sûr, fermeture application, relance Explorer.

### Action sensible

- Orange intense ;
- Anneau verrouillé ;
- Bordure renforcée.

Usage : pilote, Windows Update, réseau, nettoyage avancé.

### Action critique

- Rouge-orange ;
- Animation de verrouillage ;
- Titre plus strict.

Usage : reset réseau avancé, réparation Windows avancée, firmware / BIOS en information uniquement.

### Succès

- Orange doré ;
- Flash doux ;
- Bordure calme.

Usage : action terminée, rapport généré, nettoyage fini.

### Erreur

- Rouge-orange bref ;
- Clignotement discret ;
- Bouton Voir détails.

Usage : accès refusé, action échouée, droits administrateur requis.

## 8. Structure professionnelle des popups

Chaque popup doit expliquer son intérêt en quelques secondes.

Elle doit toujours afficher :

1. pourquoi la popup apparaît ;
2. ce qui va être fait ;
3. sur quelle zone l'action agit ;
4. le niveau de risque ;
5. les choix disponibles.

### Structure type

```text
[VIRGIL]
VALIDATION REQUISE

Action : Nettoyage avancé
Zone : Cache Windows Update
Risque : Moyen
Impact : 2,1 Go récupérables
Redémarrage : non requis

Cette action modifiera des fichiers système non personnels.
Aucune action ne sera effectuée sans confirmation.

[CONFIRMER] [PASSER] [ANNULER TOUT]
```

## 9. Apparence des popups

À faire :

- fond sombre mat ;
- bordure orange fine ;
- mini noyau Virgil animé ;
- titre court ;
- informations alignées ;
- boutons sobres ;
- risque visible ;
- animation courte ;
- cohérence avec le HUD principal.

À éviter :

- gros avatar au centre ;
- effets énormes ;
- fenêtre blanche Windows standard ;
- trop de texte ;
- icônes cartoon ;
- animations partout ;
- sons obligatoires.

## 10. Types de popups

Virgil doit utiliser des modèles fixes :

1. Information ;
2. Validation simple ;
3. Validation renforcée ;
4. Validation critique ;
5. Résultat / rapport ;
6. Erreur.

Chaque modèle utilise le même composant Virgil, mais avec un état différent.

## 11. Layout général de l'application

Interface en trois zones :

### Zone centrale

- Noyau Virgil ;
- Bouton SCAN COMPLET ;
- État général.

### Zone droite

- Chatbox Virgil ;
- Boutons contextuels ;
- Journal court.

### Zone basse ou gauche

- Actions ciblées ;
- Rapports ;
- Historique.

Les actions ciblées apparaissent surtout après scan ou via une section dédiée. Elles ne doivent pas surcharger l'écran initial.

## 12. Écran avant scan

Interface minimale :

```text
[VIRGIL]
Système prêt.
Aucune analyse récente.

[SCAN COMPLET]
```

Au clic sur SCAN COMPLET :

```text
[SCAN RAPIDE]
[ANALYSE APPROFONDIE]
[ANNULER]
```

## 13. Écran après scan

Après analyse :

```text
[VIRGIL]
Analyse terminée.
État système : à surveiller.
3 priorités détectées.
```

Actions visibles :

```text
[NETTOYAGE]
[MISES À JOUR]
[RESSOURCES]
[VOIR RAPPORT]
```

Accès secondaire :

```text
Toutes les actions ciblées
```

## 14. Animations autorisées

Animations sobres autorisées :

- pulse lent ;
- glow orange ;
- scanline légère ;
- segments qui s'allument ;
- transition courte ;
- flash succès ;
- clignotement erreur très bref ;
- onde lumineuse à la communication.

Animations à éviter :

- clignotement agressif ;
- effets stroboscopiques ;
- sons obligatoires ;
- animations longues ;
- explosions visuelles ;
- particules partout.

## 15. Sons

### Version 1

Aucun son.

### Plus tard

Sons optionnels uniquement :

- bip court ;
- activation scan ;
- alerte discrète ;
- succès léger ;
- erreur courte.

Règle : pas de voix, pas de son copié, pas de son obligatoire.

## 16. États affichés dans l'interface

Virgil doit toujours afficher son état :

- Repos ;
- Analyse ;
- Communication ;
- Alerte ;
- Validation requise ;
- Action en cours ;
- Succès ;
- Erreur.

Exemple :

```text
État Virgil : Analyse
Module actif : Nettoyage
Progression : 4 / 12
```

## 17. Accessibilité et réduction des animations

À prévoir plus tard :

- réduire animations ;
- désactiver pulses forts ;
- mode basse intensité ;
- contraste renforcé ;
- taille texte ajustable.

Objectif : garder Virgil lisible et confortable.

## 18. Ce que Virgil ne doit pas être visuellement

Virgil ne doit pas être :

- une image fixe ;
- un avatar humain ;
- un robot 3D ;
- une mascotte ;
- une copie d'une licence existante ;
- une interface blanche Windows classique ;
- une explosion RGB ;
- une interface illisible pour faire stylé.

## 19. Version 1

À inclure en V1 :

- noyau vectoriel animé ;
- états Idle, Scan, Communication, Alerte, Succès, Erreur ;
- popups HUD sombres ;
- mini Virgil dans les popups ;
- bordures et couleurs selon le niveau de risque ;
- chatbox textuelle ;
- aucun son ;
- animations sobres.

## 20. Version 2

À prévoir en V2 :

- états plus fins ;
- animations plus fluides ;
- réduction animations ;
- accessibilité renforcée ;
- thèmes d'intensité ;
- sons optionnels.

## 21. Règle produit

L'apparence de Virgil doit renforcer la compréhension et la confiance.

Le noyau animé n'est pas décoratif : il indique l'état du système, le niveau de risque, le type d'action et la progression.

Virgil doit être sobre, professionnel, tactique et lisible.
