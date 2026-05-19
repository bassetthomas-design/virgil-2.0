# Module Réseau - Virgil 2.0

## Décision validée

Le module Réseau permet de diagnostiquer l'état de la connexion, d'identifier les anomalies probables liées au PC, au DNS, à la passerelle, au Wi-Fi, au VPN ou au proxy, puis de proposer des réparations légères ou avancées avec garde-fous.

Virgil ne doit jamais modifier la configuration réseau sans validation explicite de l'utilisateur.

## Objectif

Répondre aux questions :

```text
Est-ce que ma connexion fonctionne ?
Est-ce que le problème vient du PC, du DNS, de la box, du Wi-Fi ou d'Internet ?
Que peut-on réparer sans tout casser ?
```

## Actions visibles du module

L'interface doit rester simple.

Actions principales visibles :

```text
[Diagnostic réseau]
[Test DNS / latence]
[Réparation réseau légère]
[Réinitialisation avancée]
```

Les actions détaillées apparaissent après diagnostic.

## 1. Diagnostic réseau

### Type

Lecture seule.

### Éléments analysés

- Connexion Internet active
- Carte réseau utilisée
- Type de connexion : Wi-Fi / Ethernet
- Adresse IP locale
- Passerelle
- DNS utilisés
- État DHCP
- Ping passerelle
- Ping Internet
- Latence moyenne
- Perte de paquets
- Stabilité sur quelques secondes
- VPN actif si détectable
- Proxy actif si détectable

### Message type - réseau stable

```text
[VIRGIL]
Diagnostic réseau terminé.

Connexion : active
Interface : Wi-Fi
DNS : détecté
Latence : 42 ms
Perte de paquets : 0 %

État réseau : stable
```

### Message type - anomalie

```text
[VIRGIL]
Anomalie réseau détectée.

Connexion locale : active
Internet : instable
DNS : réponse lente

Action recommandée : tester DNS ou vider le cache DNS.
```

## 2. Test DNS / latence

### Type

Lecture seule.

### Éléments testés

- Résolution DNS
- Temps de réponse DNS
- Ping passerelle
- Ping serveur externe fiable
- Perte de paquets
- Latence moyenne
- Variation de latence

### Message type

```text
[VIRGIL]
Test DNS terminé.

DNS actuel : 192.168.1.1
Réponse : lente
Latence moyenne : 180 ms

Action recommandée : vider le cache DNS ou tester un DNS alternatif.
```

### Règle

Virgil ne doit pas changer les DNS automatiquement.

## 3. Informations réseau affichables

Virgil peut afficher :

- IP locale
- Adresse MAC, éventuellement masquée partiellement
- Passerelle
- DNS
- Nom de l'interface
- Débit lien si accessible
- Wi-Fi : force du signal si accessible
- VPN détecté
- Proxy détecté

### Règle d'affichage

Virgil doit traduire les informations techniques pour l'utilisateur.

Il ne doit pas afficher des logs réseau bruts interminables sans explication.

## 4. Réparation réseau légère

### Actions possibles

- Vider cache DNS
- Renouveler IP
- Réinitialiser pile DNS locale si applicable
- Ouvrir paramètres réseau Windows

### Niveaux de risque

| Action | Risque |
| --- | --- |
| Vider cache DNS | Faible |
| Renouveler IP | Moyen |
| Redémarrer carte réseau | Moyen / sensible, plus tard |

### Popup simple - Flush DNS

```text
[VIRGIL]
Réparation réseau légère.

Action : vider le cache DNS
Risque : faible
Effet : Windows redemandera les adresses des sites au prochain accès.
Connexion coupée : non

[EXÉCUTER] [PASSER] [ANNULER TOUT]
```

### Popup renforcée - Renouveler IP

```text
[VIRGIL]
Renouvellement IP demandé.

Risque : moyen
Effet possible : coupure réseau temporaire
Durée estimée : courte

[CONFIRMER] [PASSER] [ANNULER TOUT]
```

## 5. Réinitialisation réseau avancée

### Actions possibles

- Reset Winsock
- Reset pile TCP/IP
- Réinitialisation réseau Windows
- Ouvrir les paramètres de réinitialisation réseau

### Niveau de risque

Élevé.

### Popup critique

```text
[VIRGIL]
Réinitialisation réseau avancée.

Cette action peut modifier la configuration réseau.
Une coupure Internet temporaire est probable.
Un redémarrage peut être nécessaire.

Virgil recommande cette action uniquement si les réparations légères ont échoué.

[JE COMPRENDS ET JE CONFIRME] [PASSER] [ANNULER TOUT]
```

## 6. VPN / Proxy

### Éléments à détecter si possible

- VPN actif
- Proxy configuré
- DNS modifié par VPN
- Interface virtuelle VPN

### Règle

Virgil ne doit pas désactiver un VPN ou modifier un proxy automatiquement.

### Message type

```text
[VIRGIL]
VPN ou interface réseau virtuelle détectée.

Certaines anomalies réseau peuvent être liées à cette configuration.
Virgil ne modifiera pas le VPN sans validation.
```

### Actions possibles

- Afficher information
- Ouvrir paramètres réseau
- Conseiller un test sans VPN

## 7. Wi-Fi

### Éléments analysables si accessibles

- Force du signal
- Type de connexion
- Nom de l'interface
- Débit lien
- Déconnexions visibles si détectables

### Actions possibles

- Conseiller rapprochement box
- Conseiller un test Ethernet
- Ouvrir paramètres Wi-Fi
- Relancer diagnostic réseau

### Message type

```text
[VIRGIL]
Signal Wi-Fi faible ou instable.

Action recommandée : tester la connexion près de la box ou en Ethernet.
```

## 8. Ce que Virgil ne doit pas faire

Virgil ne doit jamais :

- changer DNS automatiquement ;
- désactiver VPN sans validation ;
- supprimer profils Wi-Fi sans validation ;
- modifier pare-feu automatiquement ;
- ouvrir des ports ;
- désactiver carte réseau sans avertissement ;
- lancer un reset réseau avancé sans popup critique ;
- promettre de réparer Internet si le problème vient de la box ou du fournisseur.

### Message si le problème ne semble pas venir du PC

```text
[VIRGIL]
Le PC répond correctement.
L'anomalie semble venir de la box, du Wi-Fi ou du fournisseur d'accès.
```

## 9. Rapport final

Après diagnostic ou intervention, Virgil doit produire un rapport.

### Rapport type

```text
[VIRGIL]
Rapport réseau terminé.

Connexion : active
DNS : lent
Latence : instable
Action exécutée : cache DNS vidé
Actions passées : renouvellement IP
Redémarrage requis : non
```

### Le rapport doit contenir

- Date
- Interface réseau
- Type de connexion
- Tests effectués
- Latence
- Perte de paquets
- DNS
- Passerelle
- Actions exécutées
- Actions passées
- Erreurs
- Redémarrage requis

## 10. Version 1

À inclure en V1 :

- diagnostic réseau ;
- afficher IP / DNS / passerelle ;
- test ping ;
- test DNS simple ;
- vider cache DNS ;
- renouveler IP après validation ;
- rapport ;
- popups garde-fou.

## 11. Version 2

À prévoir en V2 :

- stabilité sur 30 secondes ;
- détection VPN / proxy plus propre ;
- force du signal Wi-Fi si accessible ;
- reset Winsock / TCP-IP avec popup critique ;
- ouverture guidée paramètres réseau.

## 12. Plus tard

À prévoir plus tard :

- comparaison DNS ;
- historique qualité réseau ;
- détection box / routeur ;
- analyse conflits IP ;
- diagnostic pare-feu en lecture seule ;
- profils réseau ;
- suppression de profil Wi-Fi uniquement avec confirmation critique si un jour nécessaire.

## 13. Règle produit

Le module Réseau doit diagnostiquer avant de réparer.

Virgil peut proposer des réparations simples, mais toute modification réseau nécessite une validation claire. Les actions avancées doivent être rares, expliquées et encadrées par une popup critique.
