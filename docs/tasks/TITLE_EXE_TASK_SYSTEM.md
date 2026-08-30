# TITLE.EXE - fermeture du systeme de taches

## Objectif

Fermer `FUN_80049504`, verrou identifie par `TITLE_EXE_INIT_RECON.md`, puis
appliquer dans Ghidra les renommages dont la semantique est fermee.

Aucun code C# n'a ete porte. Seule la surface de commentaires du stub
`TITLE_EXE/TITLE_EXE_exe.cs` est mise a jour pour refleter les nouveaux noms.

## Le systeme de taches

`TITLE.EXE` est construit sur un ordonnanceur d'objets a callback, reparti en
**21 listes** doublement chainees. Trois fonctions le composent, et trois tables
globales de 21 entrees l'ancrent.

### Tables

| Adresse | Nouveau nom | Forme | Role prouve |
|---|---|---|---|
| `0x80079854` | `g_TaskListHead` | 21 x 4 octets | premier objet du parcours |
| `0x800798A8` | `g_TaskListTail` | 21 x 4 octets | dernier objet du parcours |
| `0x800798FC` | `g_TaskListCount` | 21 x 2 octets | nombre d'objets de la liste |

Les trois tables sont contigues: `0x800798A8 - 0x80079854 = 0x54 = 21 * 4`, et
`0x800798FC - 0x800798A8 = 0x54`. Le nombre 21 est confirme independamment par
`RunFrameLoop`, qui appelle `ExecuteTaskList` sur l'index `0x14` puis sur les
index `0` a `0x13`, soit 21 appels.

### Structure d'un objet

Bloc alloue par `malloc`, de taille `0x18 + align4(contextSize)`:

| Offset | Taille | Contenu prouve |
|---|---:|---|
| `0x00` | 2 | `id`, argument de `CreateTask` |
| `0x02` | 2 | drapeaux; `ExecuteTaskList` teste `flags & 3`, `DeleteTask` refuse d'agir si `flags & 2` |
| `0x04` | 4 | `callback` |
| `0x08` | 4 | pointeur de contexte, egal au bloc + `0x18`, ou nul si `contextSize == 0` |
| `0x0C` | 4 | compteur, argument `param_5` de `CreateTask` |
| `0x10` | 4 | chainage arriere |
| `0x14` | 4 | chainage avant, suivi par le parcours |
| `0x18` | n | contexte, mis a zero a la creation |

L'orientation du chainage est tranchee par `ExecuteTaskList`, dont l'avancement
de boucle est `PTR_80083224 = PTR_80083224->0x14`. Le parcours part donc de
`g_TaskListHead[i]` et suit `0x14`. `DeleteTask` le confirme: il reecrit
`g_TaskListHead[i]` quand l'objet retire a `0x10 == 0`, et `g_TaskListTail[i]`
quand il a `0x14 == 0`.

### Fonctions

| Adresse | Nouveau nom | Comportement prouve |
|---|---|---|
| `0x80049504` | `CreateTask` | alloue `0x18 + align4(contextSize)`, boucle infinie si l'allocation renvoie `-1`, ecrit les champs, met le contexte a zero, insere dans la liste `listIndex`, incremente `g_TaskListCount`, retourne le bloc |
| `0x80049720` | `DeleteTask` | retourne `0` sur pointeur nul, `2` si `flags & 2`, sinon retire du chainage double, libere, decremente le compteur et retourne `1` |
| `0x800497FC` | `ExecuteTaskList` | parcourt la liste et agit selon le compteur `0x0C` de chaque objet |

Regles de `ExecuteTaskList` sur le compteur `0x0C`:

| Valeur | Effet |
|---|---|
| `>= 1` | decremente, callback non appele |
| `== 0` | si `flags & 3 == 0`, callback appele |
| `< -1` | si `flags & 3 == 0`, incremente puis callback appele |
| `== -1` | l'objet est retire de la liste et libere |

Quand `flags & 3 == 1`, l'objet est retire au lieu d'etre execute; sinon le
bit 0 des drapeaux est pose. Les appelants observes ne passent que `0` ou `1`
comme `param_5`, ce qui en fait en pratique un delai d'activation en frames. Sa
semantique complete n'etant pas fermee, le parametre garde un nom brut.

### Point d'insertion

Le sixieme argument de `CreateTask` designe un objet existant. Les appelants
passent la valeur courante de `g_TaskListHead[i]` ou de `g_TaskListTail[i]` de
la **meme** liste que `listIndex`, ce qui se verifie sur les adresses: l'appel
`CreateTask(FUN_80021e28, 0, 6, 0x70, 0, DAT_800798c0)` utilise
`0x800798C0 = g_TaskListTail + 6 * 4`. Quand l'argument vaut la queue, l'objet
est ajoute en fin; sinon il est insere juste avant l'objet designe. Le parametre
est nomme `insertPoint`.

## Boucle de frame

`RunFrameLoop @ 0x800587A8`, 756 octets, est la boucle de frame de l'ecran
titre. Par tour: `VSync(3)`, bascule du double buffer entre `y = 0` et
`y = 0xF0` en 320 x 240, `PutDispEnv` et `PutDrawEnv`, `ProcessPadInput(0)`,
`ExecuteTaskList(0x14)`, `ClearOTag`, les 20 `ExecuteTaskList` restants,
`DrawOTag`, `DrawSync(0)`. Elle boucle tant que `DAT_800835B4` garde la valeur
lue a l'entree.

Le compteur `DAT_80083504` s'incremente a chaque tour. Passe `0x960`, soit
2 400 frames, la fonction declenche un fondu par `FUN_80038228(3, 0x10)` puis
appelle `ShutdownAndLoadExecutable`, avec `cdrom:\MOVIE.EXE;1` si le compteur
est inferieur a `0x12C1`, et `cdrom:\TITLE.EXE;1` sinon. Le bouton du masque
`0x800` force `DAT_80083504 = 0x12C1` lorsque `DAT_800835B4 == 2`.

## ShutdownAndLoadExecutable de TITLE.EXE

`0x80058158` porte le meme role que ses homologues des deux autres overlays,
avec deux differences reelles:

| # | SLPS `0x800215C0` | MOVIE `0x80021274` | TITLE `0x80058158` |
|--:|---|---|---|
| 1-4 | `StopRCnt(0xF2000000..3)` | idem | idem |
| 5 | `PadStop()` | `PadStop()` | **`ResetGraph(0)`** |
| 6 | `FUN_8002c84c()` | absent | absent |
| 7 | `FUN_8002c8f0()` | absent | absent |
| 8 | `ResetGraph(0)` | `ResetGraph(0)` | **`PadStop()`** |
| 9 | `CdFlush()` | `CdFlush()` | **absent** |
| 10 | `StopCallback()` | idem | idem |
| 11 | `_96_init()` | idem | idem |
| 12 | `LoadExec(exeFileName, 0x801fff00, 0)` | idem | idem |

`ResetGraph` et `PadStop` sont donc inverses, et `CdFlush` n'est pas appele.

Ses appelants dessinent la navigation de l'overlay: `FUN_800324D8` est un
`switch` chargeant `DEMO.EXE`, `GAME.EXE`, `SP.EXE`, `TITLE.EXE` et
`ENDING.EXE`; `FUN_80021E28` charge `SELECT.EXE`; `FUN_800360F0` charge
`MOVIE.EXE`.

## Renommages appliques

Programme Ghidra concerne: `/TITLE.EXE`. Chaque ligne est classee `CERTAIN`.
Aucune adresse ni aucun layout n'a change.

### Fonctions

| Adresse | Ancien nom | Nouveau nom | Preuve |
|---|---|---|---|
| `0x80049504` | `FUN_80049504` | `CreateTask` | corps complet, 23 appelants |
| `0x80049720` | `FUN_80049720` | `DeleteTask` | corps complet, 14 appelants |
| `0x800497FC` | `FUN_800497fc` | `ExecuteTaskList` | corps complet |
| `0x80057508` | `FUN_80057508` | `ClearVram` | `ClearImage({0,0,0x400,0x200},0,0,0)` puis `DrawSync(0)` |
| `0x80057674` | `FUN_80057674` | `SetupGeometry` | `SetGeomOffset`, `SetGeomScreen`, matrices GTE |
| `0x80058158` | `FUN_80058158` | `ShutdownAndLoadExecutable` | corps homologue des deux autres overlays |
| `0x800587A8` | `FUN_800587a8` | `RunFrameLoop` | boucle de frame complete |
| `0x80070B64` | `FUN_80070b64` | `EnterCriticalSection` | ASM `addiu $a0, $zero, 1` puis `syscall` |
| `0x80070E44` | `FUN_80070e44` | `ExitCriticalSection` | ASM `addiu $a0, $zero, 2` puis `syscall` |

Les deux derniers sont tranches par l'ASM: le decompilateur affichait
`syscall(0)`, qui est le champ code de l'instruction et non l'argument. C'est
`$a0` qui distingue les deux services.

### Globales

| Adresse | Ancien nom | Nouveau nom |
|---|---|---|
| `0x80079854` | `DAT_80079854` | `g_TaskListHead` |
| `0x800798A8` | `DAT_800798a8` | `g_TaskListTail` |
| `0x800798FC` | `DAT_800798fc` | `g_TaskListCount` |

### Prototypes

| Adresse | Ancien | Nouveau |
|---|---|---|
| `0x80070DB4` | `undefined LoadExec(void)` | `void LoadExec(char *exeFileName, u_long param_2, u_long param_3)` |

Meme stub `A0(0x51)` que dans les deux autres overlays, meme traitement.

## Restant ouvert

| Adresse | Symbole | Etat |
|---|---|---|
| `0x80038228` | `FUN_80038228` | machine a etats d'affichage et de fondu, 11 appelants; etat `DAT_80083454` non ferme |
| `0x80037388` | `FUN_80037388` | tache enregistree puis appelee directement par `main` |
| `0x80056DC0` | `FUN_80056dc0` | huit arguments, enregistre `LAB_80056D84` |
| `0x80021DD0` | `FUN_80021dd0` | interprete le script de chargement d'images de `TITLE.B` |
| `0x80058A9C` | `FUN_80058a9c` | charge `CHR_DATA/EFF_AUTO.B` et `CHR_DATA/CH_EF_P0.B` |
| `0x80058D64` | `FUN_80058d64` | initialise 5 `POLY_FT4` a `DAT_800A8894` |
| `0x800324D8` | `FUN_800324d8` | routeur de menu vers les autres overlays |

## Anomalie signalee, non corrigee

`PTR_80083224` et `PTR_ARRAY_80083228` portent le type `TitleAudioBlock *`,
issu d'une analyse anterieure. Les deux sont en realite l'objet courant et
l'index de liste courant de `ExecuteTaskList`. Ce typage rend la decompilation
de `ExecuteTaskList` et de `DeleteTask` difficile a lire, en exprimant les acces
sous la forme `(obj->cd).pad_00 + offset`. Il n'a pas ete modifie, car
`TitleAudioBlock` peut avoir un usage legitime ailleurs. Correction possible si
le type se revele n'avoir aucun autre emploi.

## Retour arriere

Les tableaux de renommage de ce document constituent le journal reversible:
chaque nouveau nom est associe a son ancien nom et a son adresse. Aucun
renommage ne depend d'une relocalisation, aucun type n'a ete cree ni supprime.
