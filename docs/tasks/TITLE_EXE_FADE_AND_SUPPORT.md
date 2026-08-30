# TITLE.EXE - la tache de fondu et trois fonctions de support

## Objectif

Porter `FUN_80038684 @ 0x80038684`, la tache qui anime le fondu d'ecran, et
trois fonctions autonomes qui restaient: `FUN_80049a14` (destruction d'une liste
de taches), `FUN_80057b08` (decodage et televersement d'image) et
`FUN_800583fc` (l'ecran de chargement).

## `FUN_80038684`: les deux masques, fermes par les transitions

Le corps commute sur `DAT_80083454 & 0xbfff` puis re-teste `0x4000` seul avant
chaque `DeleteTask`. Les deux masques ne sont plus supposes; ils se ferment par
les transitions et par la liste des ecrivains.

**`0xbfff`** est le complement du bit 14 sur seize bits: la commutation se fait
donc sur l'etat **prive de son bit 14**. C'est ce qui fait que `0x4003` entre
dans le cas 3 et `0x4005` dans le cas 5.

**`0x4000`** distingue deux modes d'invocation. Ses seuls ecrivains sont les cas
0 et 7 de `FUN_80038228` - et ce sont exactement les deux qui appellent
`FUN_80038684` **directement**, par un `jal`, au lieu de le confier a
`CreateTask`. Quand le bit est pose, il n'existe donc aucune tache a nous sur
aucune liste, et la suppression doit etre sautee. Rien ne l'efface: les etats
terminaux ecrits ici (0, 1, 7) ne le portent simplement pas.

Le cas 6 n'a **pas** ce garde-fou: son bras `== 2` appelle `DeleteTask` sans
tester `0x4000`. Cette asymetrie est reproduite, avec un commentaire qui le dit.

### Largeurs de chargement

Un detail que le decompilateur aplatit: les cas 2 et 4 comparent sur le pas en
**halfword** (`lhu` @ `0x800386E8`) mais soustraient le pas en **byte**
(`lbu` @ `0x800386FC`). Les deux largeurs sont reproduites par des conversions
explicites.

### Le placeholder supprime plutot que reimplemente

`InvokeFadeTask()` routait les cas 0 et 7 par la table de dispatch. Les
references entrantes de Ghidra montrent que ces deux sites sont des `jal`
**directs** (`0x80038614` et `0x80038648`), jamais un pointeur de fonction.
L'appel direct est donc la forme fidele; le placeholder etait un raccourci.

## La consequence: la seconde boucle de `main` se termine enfin

Avant ce changement, la tache de fondu etait **creee mais jamais dispatchee** -
son adresse n'etait enregistree nulle part. `DAT_80083454` restait donc coince a
2 pour toujours, `FUN_80038228(9, 0)` ne rendait jamais 0, et la **seconde**
`RunFrameLoop` de `main` ne pouvait pas sortir. Un quad gris a `0x80` etait
soumis a chaque frame, indefiniment.

Le callback branche, cette boucle suit maintenant le chemin de l'original: la
rampe 2 vers 1 (r0 descend de 4 par frame), puis au frame `0x961` le
`FUN_80038228(3, 0x10)` fait passer de l'etat 1 a l'etat 3, la rampe monte
jusqu'a `0xff`, `SetDispMask(0)`, etat 0, la tache se supprime - et
`FUN_80038228(9, 0)` rend 0, ce qui passe la main a MOVIE.EXE.

**L'ecran titre n'est pas touche.** Le cas 3 exige `DAT_80083454 == 1`, et le
`FUN_80038228(8, 0)` de `main` le pose a 0. Le verificateur a re-derive ce point
depuis Ghidra: sur les 23 references a `DAT_80083454`, **toutes** les ecritures
sont dans `FUN_80038228` ou `FUN_80038684`, et `FUN_80037388` ne fait que lire.
Pendant la boucle du titre l'etat reste 0, donc aucune tache portant
`0x80038684` n'existe sur aucune liste.

Le brief que j'avais donne aux agents disait « le cas 3 est le seul createur ».
C'etait incomplet: le **cas 2 cree aussi la tache**, et c'est par la que passe le
chemin post-titre. Le verificateur l'a releve. La conclusion tient - l'ecran
titre reste inchange - mais la derivation etait fausse.

## `FUN_80049a14` @ 0x80049A14

Detruit toutes les taches d'une liste. Un detail garde tel quel (regle 12): le
compteur n'est decremente **qu'a l'interieur** du garde `flags & 2`. Une tete
portant ce bit, ou un compteur non nul avec une tete nulle, boucle donc
indefiniment. L'original fait pareil.

## `FUN_80057b08` @ 0x80057B08

Compose trois pieces deja portees. Deux points fermes par les octets plutot que
supposes:

- **`DrawSync` vient APRES le televersement**, l'inverse de `FUN_80057c80`.
  Sequence des `jal`: `0x80057B44` vers le decodeur, `0x80057B74` vers
  `LoadImageInVram`, `0x80057B80` vers `DrawSync`.
- **Le tampon de decodage est le meme** que celui de `FUN_80057c80`: Ghidra
  montre le meme symbole `&DAT_80096664` comme second argument du decodeur et
  premier de `LoadImageInVram`. Un seul registre, un seul tampon.

## `FUN_800583fc` @ 0x800583FC

L'ecran de chargement. Le point interessant est `0x800A7830`, que la
reconnaissance avait pris pour une **seconde** table d'affichage.

Ce n'en est pas une. `0x800A7830 - 0x800A6830 = 0x1000 = 0x400 * 4`: c'est la
**case `0x400`** de la table existante. Les references croisees sur `0x800A7830`
en rendent exactement deux, toutes deux dans `FUN_800583fc` - ses propres
`AddPrim`.

`PARTIAL` assume: `CdIntToPos` et `CdPosToInt` sont des stubs vides dans le SDK,
donc le positionnement par `DAT_1f80012c * 10` secteurs est perdu. C'est declare,
pas repare en recodant l'arithmetique MSF dans un fichier de jeu (regle 13).

## Un octet NUL brut, corrige a part

`DisplayMachine.cs` contenait un octet `0x00` **brut** dans un literal de
caractere, la ou l'echappement a deux caracteres est attendu. Ca compilait, mais
un seul NUL fait classer tout le fichier comme binaire par ripgrep, qui le saute
alors dans toute recherche ordinaire - ce qui s'etait produit plusieurs fois
pendant ce travail. Corrige dans son propre commit, comme l'exigent les regles
de commit.

## Verification

Deux verificateurs en contexte neuf, adversariaux: **CONFIRMED** pour les
quatre fonctions, avec pour chacune un tableau point par point recoupe contre
Ghidra plutot que contre les commentaires du portage.

Les sept bancs passent: `heap`, `tasks`, `title-init`, `title-images`, `render`,
`title-task`, `pad-input`.

## Ce qui reste ouvert

- **Le sens visuel du fondu** n'est pas nomme. Les etats restent des nombres
  bruts; rien n'appelle `0x00` « transparent » ni un etat « fade in ». Le fermer
  demanderait de decoder le champ de semi-transparence des tpage `0x50` et
  `0x30` contre les modes de melange du GPU, plus le sens du texel `0x1111FFFF`
  televerse par `FUN_80038228` cas 8.
- **Pourquoi le cas 3 appelle `SetDispMask(0)` et le cas 5 non** n'est pas
  ferme. Le fait est reproduit; aucune raison n'est avancee.
- **`TaskSystem.InvokeCallbackByAddress`** n'a plus d'appelant depuis que
  `InvokeFadeTask` a disparu.
