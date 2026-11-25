# Lot R3 — Remise à zéro complète de Windows Update

## Commande et parcours

Après « Analyser », la confirmation présente « Remettre à zéro Windows Update » et l’aperçu exact `virgil-update-reset`, sans argument. Le diagnostic laisse la vérification des préconditions au helper. La sélection et le plan confirmé précèdent tout appel au helper et toute demande d’élévation. Le parcours « Scanner et réparer » ne sélectionne pas cette action, y compris pour un constat de service Windows Update désactivé.

Un point de restauration doit être créé et vérifié avant la commande, même si la définition reçue autorise une dérogation. L’échec bloque l’action avec « Remise à zéro de Windows Update non lancée : point de restauration non créé ou non vérifié. », le détail de l’erreur et l’instruction d’activer la protection du système. Une annulation avant la commande empêche son lancement. Une fois lancée, la séquence va jusqu’au bout, rattrapage compris. Le journal en français apparaît dans les détails techniques et dans le rapport.

## Exécution dans le helper

virgil (relecture) a retenu une commande interne en C# avec hôte et horloge injectés afin que les renommages, les délais et le rattrapage soient testables par injection. L’hôte réel utilise l’API du gestionnaire de services Windows, lit les clés CBS et Windows Update de redémarrage en attente et renomme les dossiers via `Directory.Move`. Aucun processus externe ni script PowerShell n’est lancé.

1. Vérifier que le répertoire système est absolu et se termine par `System32`. Vérifier l’existence et le type de démarrage de `wuauserv`, `bits`, `cryptsvc` et `msiserver`. Refuser si l’un est désactivé, si `msiserver` ou `TrustedInstaller` est actif, ou si Windows attend un redémarrage. Vérifier les sources `SoftwareDistribution` et `System32\catroot2` dans cet ordre, puis l’absence des deux destinations dans le même ordre. Une source absente est consignée et ignorée. Une source de type fichier ou point d’analyse bloque l’action. Tout échec à ce stade renvoie « Remise à zéro de Windows Update non lancée : … Aucune modification. ».
2. Arrêter `wuauserv`, `bits`, `cryptsvc`, puis `msiserver`. Un service déjà arrêté satisfait l’étape. Chaque transition dispose de 30 secondes, avec observation asynchrone. Le journal indique l’état attendu et l’état observé en cas de dépassement.
3. Renommer `SoftwareDistribution`, puis `catroot2`, avec le même suffixe UTC `.virgil-yyyyMMdd-HHmmss`. Les chemins proviennent du répertoire système.
4. Démarrer `cryptsvc`, `bits`, puis `wuauserv`. `msiserver` reste arrêté. Le code 0 accompagne « Remise à zéro de Windows Update terminée. », les chemins conservés ou la mention « absent », et la possibilité de supprimer plus tard les dossiers manuellement.

Après un échec en cours d’opération, le helper tente une seule fois le rattrapage. Si un dossier a été déplacé, il réessaie d’arrêter les quatre services dans l’ordre initial et journalise les échecs éventuels. Il examine ensuite les dossiers déplacés dans l’ordre inverse. Il rétablit le nom d’origine si celui-ci est libre et si le dossier renommé existe encore sous forme de dossier ordinaire. En cas de conflit, il laisse les entrées en place et journalise les deux chemins. Il tente enfin de démarrer `cryptsvc`, `bits` et `wuauserv` dans cet ordre, même après un échec intermédiaire. Le code 1 indique « Rattrapage effectué » après un rétablissement complet. Le code 2 indique « Rattrapage incomplet » et liste les chemins et services concernés.

Virgil conserve les dossiers renommés, ne change aucun type de démarrage et ne redémarre pas le PC. Windows recrée son cache, ce qui peut imposer de refaire les téléchargements en attente et vider l’historique affiché. Les mises à jour déjà installées restent installées.

## Tests et périmètre

- `LotReparationWindowsUpdateResetTests` vérifie le catalogue et l’allowlist, l’ordre des sondes et des mutations, les préconditions, les sources absentes, chaque échec de la séquence, les délais simulés d’arrêt et de démarrage, le retour en ordre inverse, les collisions et les échecs de rattrapage. Un contrôle de source interdit les API destructives et les commandes externes. Le double d’hôte enregistre les opérations et conserve les états en mémoire. L’horloge factice pilote les délais.
- Les tests `Windows_update_reset_*` d’`InterventionsModuleTests` réutilisent les doubles du runner et du client du helper pour vérifier la commande, le libellé, les droits, les codes 0 à 2, le journal, la confirmation, le point de restauration sans dérogation et l’annulation avant exécution.
- `ElevatedAtomicCommandFinishesAfterCancellationWithoutReceivingKillToken` couvre l’annulation pendant la commande avec le double existant.
- Les scénarios n’exécutent aucun processus de réparation, n’appellent jamais l’hôte Windows réel et ne modifient aucun service ni dossier réel.

Les assertions existantes sont conservées, dont l’inventaire fermé des scripts. Le nouveau test vérifie le titre, l’ordre et la commande de `RestartWindowsUpdate`, ainsi que l’absence de proposition du reset pour un constat `wuauserv`. Les gardes `IsForbiddenArgument`, `IsSafe`, le comportement de `RepairsFor`, les délais `TimeoutFor` et les seuils RG4 restent ceux de la base. Aucune correction n’a été nécessaire au titre de la règle générale.

virgil (relecture) a autorisé exceptionnellement les trois ajouts `affiche`, `occupe` et `zero` dans `tests/Virgil.App.Tests/Accents/ValideSansAccent.txt`. Ces entrées distinguent les formes verbales et le membre technique `IntPtr.Zero` des mots accentués introduits par le lot R3.

## Vérifications

Dans une copie Linux isolée, le projet moteur a compilé en Release avec les avertissements traités comme erreurs. Les filtres par nom de test ont donné 53 réussites et aucun échec après retrait de la dépendance d’exécution à Windows Desktop dans le fichier généré de la copie. Le scénario nominal avec chemins Windows et le cas R2 de la théorie d’annulation sont réservés à la CI Windows. Le premier lancement Linux de cette théorie a été interrompu, car le cas R2 attend le démarrage d’une commande qui exige un lecteur Windows valide. Aucun fichier de projet ni test versionné n’a été modifié pour cette vérification.

Résultats Windows du dernier commit de production ou de test à renseigner après le run. La CI du commit final consacré uniquement à cette preuve sera indiquée dans la description de la PR.

## Validation sur PC

L’hôte réel reste à essayer sur un PC Windows pour vérifier l’arrêt et le démarrage effectifs des services, ainsi que les renommages. Windows peut relancer `cryptsvc` avant le renommage de `catroot2`. Si le renommage échoue, le helper déclenche le rattrapage.
