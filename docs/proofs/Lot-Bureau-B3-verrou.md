# Lot Bureau B3 — iconcache verrouillé

Base : `0da6adb140dc5b862cda5d5fa6cb8c490de59071`. Branche : `codex/lot-bureau-b3-verrou`. Relecture : loops (relecture).

## Constat et cause

Le relevé ouvrait les caches avec `File.OpenRead` / `FileShare.Read`, incompatible avec un écrivain Explorer. Les métadonnées des vrais iconcache peuvent aussi changer sans nettoyage.

## Rouge par assertion

Premier commit de tests seuls : `40f15a12706f5172710ddf735cca6af22f397d29`.
[CI Windows 44062880005, numéro 448](https://github.com/bassetthomas-design/virgil-2.0/actions/runs/44062880005), job `133969007319` ; checkout de fusion technique `3192ab08672074ea70f84038e03c3f22bec2242d`.
Compilation réussie : `0 Warning(s)`, `0 Error(s)`.

Le nouveau test échoue par assertion, et non par exception non capturée :

```text
Assert.Null() Failure: Value is not null
Expected: null
Actual:   System.IO.IOException: The process cannot access the file 'C:\Users\runneradmin\AppData\Local\Microsoft\Windows\Explorer\iconcache_48.db' because it is being used by another process.
```

Les seuls échecs sont le nouveau test et le B3 existant, déjà rouge sur la base. Core : 1298/1300 réussis ; App : 840/840 réussis.

## Correctif

Lecture avec `FileShare.ReadWrite | FileShare.Delete`. Les vrais iconcache ont seulement une ligne `Icônes: P: <relatif>` ; les fixtures conservent taille, date et SHA-256. Les fichiers du Bureau, du Bureau public et des épinglés dont la lecture échoue restent dans le relevé avec `<verrouillé>` ; leur disparition entre les relevés reste visible dans `comparaison.txt`.

Le corps du B3 existant est vérifié identique octet pour octet à la base. Aucun fichier de production, workflow ou autre test modifié. Aucun build local revendiqué. Le document référencé `briefs/T2-reparation.md` est absent ; la méthode explicitement fournie dans le brief joint est suivie.

## Validation Windows du correctif

CI du commit correctif : en attente. Re-run du job : en attente. Artefact `Virgil-ui-smoke/lot-bureau/` : comparaison avant/après en attente.

## Manipulations Windows restantes

1. Exécuter la CI Windows du correctif ; vérifier les deux tests B3 réussis, le build avec 0 avertissement et le workflow complet vert.
2. Relancer le job Windows du même commit ; vérifier à nouveau B3 et le workflow vert.
3. Inspecter `Virgil-ui-smoke`, dossier `lot-bureau` : comparer les octets de `avant.txt` et `apres.txt`, vérifier `comparaison.txt` vide, puis consulter `apercu.json` et `execution.json` pour le nettoyage réel et la fixture icône exclue.

Aucun essai manuel sur PC Windows personnel n’est attesté. Aucun changement de comportement de production dans ce micro-lot. PR conservée en brouillon, sans fusion.
