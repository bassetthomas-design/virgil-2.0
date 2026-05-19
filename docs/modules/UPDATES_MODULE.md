# Module Mises à jour - Virgil 2.0

## Décision validée

Le module Mises à jour doit scanner tout ce qui peut raisonnablement être vérifié ou mis à jour sur le PC, puis classer chaque élément par niveau de risque.

Virgil ne doit jamais installer une mise à jour sans validation explicite de l'utilisateur.

Le module utilise des popups garde-fou étape par étape.

## Objectif

Répondre aux questions :

```text
Qu'est-ce qui n'est pas à jour ?
Qu'est-ce qui peut être mis à jour sans risque ?
Qu'est-ce qui est sensible ?
Est-ce qu'un redémarrage est nécessaire ?
```

## Actions visibles du module

L'interface doit rester simple.

Actions principales visibles :

```text
[Scanner les mises à jour]
[Mettre à jour ce qui est sûr]
[Voir et choisir]
[Examiner les pilotes]
```

Les catégories détaillées apparaissent après scan.

## 1. Scanner les mises à jour

### Type

Lecture seule.

### Éléments à scanner

- Windows Update
- Applications via winget
- Microsoft Store si possible
- Pilotes
- Pilote GPU
- Runtimes importants
- Navigateurs
- Applications avec updateur interne
- Firmware / BIOS en information uniquement

### Message type

```text
[VIRGIL]
Scan mises à jour terminé.

Applications sûres : 5
À valider : 2
Sensibles : 1
Critiques / information : 1

Séquence de validation disponible.
```

## 2. Windows Update

### Éléments à vérifier

- Mises à jour de sécurité
- Mises à jour qualité
- Mises à jour fonctionnalités
- Redémarrage requis
- Échecs de mise à jour
- Statut Windows Update

### Actions possibles

- Afficher les mises à jour disponibles
- Installer après validation
- Ouvrir les paramètres Windows Update si nécessaire
- Signaler le redémarrage requis

### Niveau de risque

À valider ou sensible selon le type de mise à jour.

### Popup renforcée

```text
[VIRGIL]
Mise à jour Windows détectée.

Type : sécurité / qualité
Risque : moyen
Redémarrage possible : oui

Virgil recommande d'enregistrer le travail en cours.

[CONFIRMER] [PASSER] [ANNULER TOUT]
```

## 3. Applications via winget

### Éléments à vérifier

- Disponibilité de winget
- Applications compatibles
- Version installée
- Version disponible
- Source
- Mise à jour individuelle
- Mise à jour groupée

### Si winget est disponible

```text
[VIRGIL]
Winget opérationnel.
5 mises à jour applicatives détectées.
```

### Si winget est absent

```text
[VIRGIL]
Winget indisponible.
Le scan des applications sera limité.
```

### Actions possibles

- Mettre à jour une application
- Mettre à jour les applications sûres
- Mettre à jour une sélection
- Passer une application

### Niveau de risque

Sûr ou à valider selon l'application.

### Popup simple

```text
[VIRGIL]
Mise à jour prête.

Application : VLC
Version actuelle : 3.0.x
Version disponible : 3.0.y
Risque : faible
Redémarrage : non

[EXÉCUTER] [PASSER] [ANNULER TOUT]
```

## 4. Microsoft Store

### Éléments à prévoir

- Applications Store
- Mises à jour Store si accessibles
- Ouverture du Microsoft Store si nécessaire
- Signalement des limites

### Message type

```text
[VIRGIL]
Certaines applications Store nécessitent une vérification dans Microsoft Store.
```

### Règle

Virgil ne doit pas prétendre gérer les applications Store si Windows ne fournit pas une méthode fiable.

## 5. Pilotes

### Éléments à scanner

- Pilotes système
- Pilotes réseau
- Pilotes audio
- Pilotes Bluetooth
- Pilotes Wi-Fi
- Pilotes chipset
- Pilotes stockage
- Pilotes périphériques

### Actions possibles

- Afficher les pilotes disponibles
- Installer un pilote après validation renforcée
- Ignorer un pilote
- Signaler un redémarrage possible

### Niveau de risque

Sensible.

### Popup renforcée

```text
[VIRGIL]
Mise à jour pilote détectée.

Périphérique : carte réseau
Risque : sensible
Effet possible : coupure temporaire de connexion
Redémarrage : possible

[JE CONFIRME] [PASSER] [ANNULER TOUT]
```

## 6. Pilote GPU

### Éléments à vérifier

- GPU NVIDIA / AMD / Intel
- Version pilote actuelle si accessible
- Mise à jour disponible si détectable
- Outil constructeur disponible

### Actions possibles

- Examiner le pilote GPU
- Ouvrir l'outil officiel
- Installer uniquement si une méthode fiable est disponible

### Niveau de risque

Sensible à critique selon la méthode.

### Règle

En V1, Virgil peut détecter le GPU et afficher l'information.

L'installation avancée du pilote GPU doit rester très encadrée.

## 7. Runtimes importants

### Éléments à vérifier

- .NET Runtime
- Visual C++ Redistributables
- DirectX Runtime
- Java si installé
- WebView2 Runtime

### Actions possibles

- Afficher les runtimes installés
- Détecter une version obsolète si possible
- Mettre à jour après validation

### Niveau de risque

À valider.

### Message type

```text
[VIRGIL]
Runtime important détecté comme obsolète.
Certaines applications peuvent en dépendre.
Validation requise.
```

## 8. Navigateurs

### Navigateurs à vérifier

- Edge
- Chrome
- Firefox
- Brave
- Opera

### Actions possibles

- Mettre à jour via winget si disponible
- Signaler un updateur interne
- Ouvrir l'application si nécessaire

### Règle

Virgil doit indiquer que certains navigateurs utilisent leur propre système de mise à jour.

## 9. Applications avec updateur interne

### Exemples

- Adobe
- Antivirus
- VPN
- Logiciels constructeurs
- Launchers
- Outils périphériques
- Suites professionnelles

### Action possible

- Ouvrir l'application ou l'outil officiel
- Signaler que la mise à jour est gérée par l'application

### Message type

```text
[VIRGIL]
Cette application utilise son propre système de mise à jour.
Action recommandée : ouvrir l'application ou son outil officiel.
```

## 10. Firmware / BIOS

### Décision

Firmware et BIOS sont en information uniquement.

Virgil ne doit jamais installer automatiquement un BIOS ou un firmware.

### Actions possibles

- Détecter une information si accessible
- Signaler qu'une vérification constructeur est recommandée
- Ouvrir une page ou fournir une consigne si disponible

### Popup critique / information

```text
[VIRGIL]
Action critique.

Firmware / BIOS détecté ou suspecté.
Installation automatique non proposée.

Une erreur à ce niveau peut rendre le PC inutilisable.

[OUVRIR INFORMATION] [PASSER]
```

## 11. Classement des risques

Chaque mise à jour doit être classée.

| Niveau | Exemples |
| --- | --- |
| Sûr | Applications classiques, navigateurs, outils simples |
| À valider | Windows Update, runtimes, applications professionnelles |
| Sensible | Pilotes, GPU, réseau, stockage, outils constructeur |
| Critique / information uniquement | BIOS, firmware, composants bas niveau |

## 12. Séquence étape par étape

Virgil doit pouvoir proposer une séquence après scan.

Exemple :

```text
[VIRGIL]
Scan mises à jour terminé.

Applications sûres : 5
À valider : 2
Sensibles : 1
Critiques / information : 1

Séquence de validation disponible.
```

Étapes :

```text
Étape 1/8 - VLC
[EXÉCUTER] [PASSER] [ANNULER TOUT]
```

```text
Étape 6/8 - Windows Update
[CONFIRMER] [PASSER] [ANNULER TOUT]
```

```text
Étape 8/8 - Firmware constructeur
[OUVRIR INFORMATION] [PASSER]
```

## 13. Règles de sécurité

Virgil ne doit jamais :

- installer les pilotes sans validation ;
- installer BIOS / firmware automatiquement ;
- forcer un redémarrage ;
- masquer une erreur winget ;
- mettre à jour un antivirus ou VPN sans avertissement ;
- mettre à jour un pilote réseau sans prévenir ;
- installer une version bêta sans demande explicite ;
- forcer une mise à jour majeure Windows sans explication.

## 14. Rapport final

Après une séquence de mises à jour, Virgil doit produire un rapport.

### Rapport type

```text
[VIRGIL]
Séquence mises à jour terminée.

Applications mises à jour : 4
Actions passées : 2
Échecs : 1
Redémarrage requis : oui

Détail :
- VLC : terminé
- 7-Zip : terminé
- Windows Update : terminé
- Pilote réseau : passé
- Discord : échec
```

### Le rapport doit contenir

- Date
- Source utilisée
- Élément concerné
- Ancienne version
- Nouvelle version
- Statut
- Niveau de risque
- Redémarrage requis
- Erreurs éventuelles

## 15. Version 1

À inclure en V1 :

- scanner winget ;
- mettre à jour applications via winget ;
- détecter winget absent ;
- afficher Windows Update ou ouvrir les paramètres ;
- classer les mises à jour ;
- utiliser les popups garde-fou ;
- générer un rapport.

## 16. Version 2

À prévoir en V2 :

- intégration Windows Update plus poussée ;
- scan pilotes ;
- scan GPU ;
- runtimes importants ;
- Microsoft Store ;
- séquence étape par étape plus complète.

## 17. Plus tard / très encadré

À prévoir plus tard :

- pilotes avancés ;
- pilotes réseau / stockage ;
- outils constructeurs ;
- firmware / BIOS en information uniquement ;
- intégration officielle constructeur si disponible.

## 18. Règle produit

Le module Mises à jour doit être complet, mais jamais brutal.

Virgil scanne tout ce qui est possible, classe les risques, explique, demande validation, puis agit uniquement si l'utilisateur confirme.
