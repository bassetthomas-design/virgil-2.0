# Virgil 2.0

**Virgil 2.0** est un nouveau projet créé de zéro : un assistant PC Windows local orienté diagnostic, surveillance, maintenance encadrée et interface tactique sombre/orange.

## Décision de travail

Le projet se travaille uniquement sur **GitHub**.

Aucun développement local n'est requis pour avancer :

- les fichiers sont créés et modifiés par commits GitHub ;
- la compilation se fait avec GitHub Actions ;
- les artefacts testables sont générés par GitHub Actions ;
- l'objectif final est un installateur Windows téléchargeable depuis une release GitHub.

Virgil v1 reste une référence d'idée, mais Virgil 2.0 repart sur une base neuve.

## Objectif final

L'utilisateur final doit pouvoir :

1. télécharger un installateur ;
2. installer Virgil ;
3. lancer l'application ;
4. analyser son PC ;
5. valider les actions proposées.

Aucun add-on manuel. Aucun SDK à installer. Aucun bricolage. Le minimum syndical pour ne pas transformer l'utilisateur en technicien malgré lui.

## Identité

Virgil 2.0 vise une ambiance originale :

- HUD sombre ;
- accent orange / ambre ;
- notifications système brèves ;
- diagnostic local ;
- actions validées par l'utilisateur ;
- aucune copie d'asset, son, nom ou élément propriétaire issu d'une licence tierce.

## Base technique

- .NET 8
- WPF
- Windows x64
- Publication self-contained
- Build GitHub Actions
- Artefact installable à terme

## Modules cibles

| Module | But |
| --- | --- |
| Monitoring | CPU, RAM, disque, réseau, température si disponible |
| Ressources système | Observation CPU/RAM courte, processus lourds, actions processus confirmées, rapport |
| Nettoyage | Prévisualisation avant action |
| Démarrage | Analyse des applications lancées avec Windows |
| Pilotes | Scan puis bouton d'installation si résultat |
| Applications | Vérification et mise à jour encadrée |
| Assistant | Notifications internes et recommandations lisibles |
| HUD | Interface orange/noir avec état système |

## Principe de sécurité

Virgil fonctionne en trois niveaux :

1. **Observation** : lire l'état du PC sans rien modifier.
2. **Recommandation** : proposer une action claire.
3. **Action validée** : agir uniquement après validation.

Le module Ressources applique en plus une confirmation renforcée avant toute fermeture forcée. Les processus Windows, sécurité, VPN et matériel identifiés comme sensibles ne sont jamais proposés à la fermeture. La libération de mémoire inactive reste informative en V1 : aucun « boost RAM » n'est simulé.

## Documentation

- `PROJECT_STATUS.md`
- `docs/CLEAN_REWRITE_DECISION.md`
- `docs/GITHUB_ONLY_WORKFLOW.md`
- `docs/ARCHITECTURE.md`
- `docs/ROADMAP.md`
- `docs/UI_DIRECTION.md`

## Statut

Base neuve Virgil 2.0 en cours de création sur GitHub uniquement.
