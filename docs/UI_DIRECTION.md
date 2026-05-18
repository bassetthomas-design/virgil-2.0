# Virgil 2.0 - Direction UI

## Intention

Virgil 2.0 doit ressembler à un agent système tactique : sombre, précis, sobre, avec un accent orange et des effets visuels légers.

L'objectif n'est pas de copier une interface existante, mais de créer une identité originale :

- lisible
- nerveuse
- technique
- rassurante
- contrôlable

## Palette cible

| Rôle | Couleur |
| --- | --- |
| Fond principal | `#05070A` |
| Fond panneau | `#0B1016` |
| Fond carte | `#111820` |
| Bordure subtile | `#24303A` |
| Accent principal | `#FF8A00` |
| Accent doux | `#FFB14A` |
| Texte principal | `#F4F7FA` |
| Texte secondaire | `#9AA6B2` |
| Alerte | `#FF4D2E` |
| Succès | `#40D47E` |

## Composants visuels

### Main Shell

- Barre supérieure avec statut global
- Zone centrale : cartes système
- Panneau droit : assistant / messages
- Barre inférieure : actions rapides

### Cartes système

Chaque carte doit afficher :

- titre court
- métrique principale
- état lisible
- mini jauge
- recommandation si utile

Exemple :

```text
RAM
67 %
Usage élevé
Analyse recommandée
```

### Notifications internes

Format cible :

```text
[VIRGIL]
Analyse système terminée.
2 recommandations disponibles.
```

Niveaux :

- info
- success
- warning
- danger

### Avatar

États recommandés :

- Idle : respiration légère
- Scan : anneau orange animé
- Alert : pulsation rapide
- Success : flash doux
- Error : tremblement bref ou bordure rouge

## Effets autorisés

- scanline très légère
- glow orange contrôlé
- animation de pulse
- texte progressif pour les messages courts
- transitions rapides

## Effets à éviter

- effets trop lourds
- clignotements agressifs
- sons trop fréquents
- animations qui gênent la lecture
- interface illisible pour faire “stylé”

L'objectif est un outil utilisable, pas une rave party pour carte graphique traumatisée.
