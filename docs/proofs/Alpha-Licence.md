# Alpha-Licence — avertissement au démarrage et licence

Base distante `main` vérifiée par le connecteur GitHub :
`ca01052345735d90658cb112533bfae0613fd27b`.
Branche neuve locale : `alpha-licence-2`. Aucun commit `55197b4` repris.

## Avertissement

Constante unique de production : `AlphaWarningWindow.WarningText`.

```text
Attention, ceci est une version alpha. Vous êtes seul responsable de ce que vous faites. En cas de doute, ne lancez rien. Merci.
```

`AppBootstrap.OnStartup` appelle `AlphaWarningWindow.RunStartup` après affectation
de `MainWindow`, avant `window.Show()`. La fenêtre modale possède un seul bouton
OK, par défaut et annulation : OK, Échap et la croix ferment la modale puis
la fenêtre principale est affichée. Aucun acquittement n'est mémorisé.
Les ressources TacticalHud existantes sont réutilisées.

Le pilote CI existant définit `VIRGIL_APP_EXE` dans le workflow et le transmet
par héritage au processus enfant (`UseShellExecute = false`). Sa présence non
vide saute la modale. Aucun workflow ni liste de captures n'est modifié.
Les trois nouveaux tests vérifient le texte, deux démarrages successifs,
le parcours capture sans appel modal et le raccordement dans OnStartup.

## Licence : copie officielle fournie et vérifiée

LICENSE est la copie binaire, sans modification, du fichier `gpl-3.0.txt`
fourni par l'utilisateur comme téléchargement officiel de
`https://www.gnu.org/licenses/gpl-3.0.txt` (674 lignes, 35149 octets).
L'identité octet par octet avec la pièce jointe a été vérifiée.
Le SHA-256 calculé de la pièce jointe et de LICENSE correspond au hash annoncé :
`3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986`.
Le téléchargement direct depuis cet exécuteur reste indisponible ; la source
utilisée pour cette vérification est le fichier officiel fourni par l'utilisateur.

README contient le lien Licence. Aucun champ de licence n'existait dans les
csproj/props inspectés ; ils ne sont pas modifiés.

## Vérifications et limites

- `git diff --check` : réussi.
- Périmètre vérifié : seulement les sept fichiers autorisés de ce lot.
- Fins de ligne des fichiers existants conservées ; aucun reformatage global.
- Aucun fichier généré ajouté ; `.github/`, réglages et secrets inchangés.
- Aucun SDK .NET disponible : les tests sont écrits mais non exécutés.
- CI Windows, 0 warning, 15 PNG et installateur : non vérifiés pour ce lot.
- Publication via le connecteur GitHub, autorisée après fourniture du fichier officiel.
- La CI Windows de la PR doit valider tests, 0 warning, 15 PNG et installateur ; aucun résultat anticipé.

- Correctif P1 : retrait de `Owner = window` avant le premier affichage de la fenêtre principale pour éviter InvalidOperationException WPF ; centrage CenterScreen conservé, test source ajusté pour interdire cette affectation.
