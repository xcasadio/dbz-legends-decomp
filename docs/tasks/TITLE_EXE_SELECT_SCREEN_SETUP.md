# TITLE.EXE - la bascule vers l'ecran de selection, et la camera qui ne l'est pas

## Objectif

Fermer `FUN_80058a9c @ 0x80058A9C`, le dernier pas encore vide de la boucle de
`main`, et evaluer `LAB_80027f5c`, la « camera ».

`main` boucle:

    FUN_80038228(8,0); FUN_80021dd0(); RunFrameLoop(); FUN_80058a9c(); FUN_80038228(2,4); RunFrameLoop();

`FUN_80058a9c` etait le seul appel encore stub.

## `FUN_80058a9c`: ce qu'il fait

Dans l'ordre, verifie pas a pas contre Ghidra:

1. `ClearOTag` sur la table `0x800A6830`
2. six `FUN_80057030` - liberation des six pools de primitives vivants,
   `DAT_800835f8` **relu a chaque tour**, pas hisse hors de la boucle
3. vingt `FUN_80049a14` - destruction des listes de taches `0` a `0x13`.
   **La liste `0x14` est epargnee**, ce qui est precisement pourquoi la tache
   audio survit a la bascule
4. `InitHeap(0x00010000, 0x10000)` - le meme armement que `main`, qui jette donc
   tout ce qui a ete alloue depuis; c'est pourquoi les liberations et les vingt
   destructions passent d'abord
5. `SetupGeometry(0xA0, 0xEF, 0x200, 0,0,0, 0x400, 0,0,0)` - la projection 2D du
   titre remplacee par celle de l'ecran de selection. Comparer avec celle de
   `main`: `(0xA8, 0x80, 0x1000, 0,0,0, 0x1000, 0,0,0)`
6. `FUN_80038228(8, 0)` - la meme paire que `main` emet juste avant
7. quatre `CreateTask` (ci-dessous)
8. `FUN_80037388` en appel direct, puis `FUN_80056dc0(0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0)` -
   les huit memes arguments que `main`, octet pour octet
9. `FUN_800583fc` - l'ecran de chargement
10. une CLUT de 256 entrees construite sur sa propre pile de `0x238` octets
    (entree 0 = `0x0000`, entrees 1..255 = `0x8000`), televersee par `LoadClut`
11. deux fichiers `CHR_DATA` lus a `0x801D2000`, le second ecrasant le premier
12. `DAT_80083544 = 1`

### Les quatre taches, et une asymetrie

| site | callback | id | liste | contexte | point d'insertion |
|---|---|---:|---:|---:|---|
| `0x80058B60` | `FUN_80037388` | `0x58` | 0 | 0 | `g_TaskListHead[0]` |
| `0x80058BC0` | `LAB_80027f5c` | `0x55` | `0x13` | 0 | `g_TaskListHead[0x13]` |
| `0x80058BF8` | `LAB_800532a4` | `0x57` | `0x14` | `0x194` | `g_TaskListTail[0x14]` |
| `0x80058D1C` | `LAB_8004c010` | `0x51` | 9 | `0x3034` | `g_TaskListTail[9]` |

**Tete** pour les listes 0 et `0x13`, **queue** pour `0x14` et 9. Le
verificateur a ferme chaque point d'insertion par arithmetique sur les symboles:
`g_TaskListHead` est a `0x80079854` et `g_TaskListTail` a `0x800798A8`, donc
`0x800798A0 - 0x80079854 = 0x4C = 19 * 4` et
`0x800798F8 - 0x800798A8 = 0x50 = 20 * 4`.

Le site 1 cree `FUN_80037388` avec l'id `0x58`, **pas** l'id 0 que `main` utilise.

Les quatre taches sont creees exactement comme dans l'original. Seuls les
callbacks qui existent sont enregistres. L'ordonnanceur saute un callback jamais
enregistre, donc la forme des listes, les ids, les index et les tailles de
contexte restent justes sans qu'aucun ecran du dessous ne soit invente.

## Trois choses volontairement pas appelees

Chacune porte un `BLOCKED:` **a sa position exacte dans l'ordre des appels**,
pas une omission silencieuse.

| ce qui manque | ce qui bloque |
|---|---|
| `LAB_800532a4`, la tache audio | libsnd et libspu. Le SDK declare toute la surface `Ss*` et **chaque corps est vide** |
| `LoadFACE_B`, les portraits | `CdPosToInt` et `CdIntToPos` sont des stubs, donc les douze lectures atterriraient **sur le meme secteur**: les portraits seraient *faux*, pas absents |
| `FUN_800376c0`, la toile de fond | `FUN_80057c80` est cable en dur sur le tampon `TITLE.B`, alors que ce site parcourt `0x801D2000` |

Le troisieme est un blocage que le portage s'est inflige a lui-meme, pas une
limite de preuve.

## La camera: trois fonctions, pas une

La reconnaissance precedente decrivait `LAB_80027f5c` comme une seule fonction
sans appelants sortants, d'environ 6752 octets, remplissant le trou
`0x80027F5C..0x800299BB`. Elle avait honnetement signale ne pas pouvoir prouver
que le trou n'en contenait qu'une.

**Il en contient trois.** Ferme par lecture memoire brute: un `jr ra`
(`08 00 E0 03`) a `0x8002991C`, `0x80029968` et `0x800299B4`, chacun suivi d'un
`nop` en delay slot puis, a `0x80029924`, `0x80029970` et `0x800299BC`, d'un
prologue frais `27BDFFE0`. `LAB_80027f5c` va donc de `0x80027F5C` a `0x80029923`,
soit `0x19C8` = 6600 octets.

Les deux queues de 76 octets sont des helpers « lancer `FUN_80029aec` en mode N »
sans **aucune** reference entrante dans le programme. Ghidra refuse meme de les
decompiler. Elles ne sont pas portees: ce serait ajouter du code mort sur la
preuve la plus faible du dossier.

### Pourquoi le solveur reste bloque

Ses deux pointeurs d'entree **n'ont aucun producteur dans le portage**:

- `DAT_800833dc` - huit lectures dans le solveur, et dans tout le programme il
  n'est ecrit **qu'une fois**, a `0x8004C0D8` dans `FUN_8004c0b4`, que Ghidra
  enregistre sans appelant et qui appartient au corps non defini de
  `LAB_8004c010`. Non porte.
- `DAT_80083644` - ecrit **une seule fois**, a `0x80035830`, dans une region que
  Ghidra n'a pas desassemblee du tout.

`PsxRam` rend 0 pour une adresse non resolue, donc un portage fait aujourd'hui ne
planterait pas: il tournerait en injectant discretement du bruit dans les neuf
mots de scratchpad que `FUN_80037388` consomme en tete de chaque frame. Et il n'y
aurait ni banc pour le piloter, ni capture console pour le comparer.

Ses **trois callees** (`FUN_8003bec8`, `FUN_8003c108`, `FUN_8003d724`) sont
fermees et sont portees, verifiees enonce par enonce - y compris l'asymetrie du
repli de composante Y dans `FUN_8003bec8` et la position du `*param_3 = 0` au
milieu de `FUN_8003c108`.

Le verdict du verificateur sur la camera est **REFUTED** - non pas parce que le
travail est mauvais, mais parce que **l'affirmation que j'avais ecrite** dans le
brief de verification (« Camera.cs translittere fidelement `LAB_80027f5c` ») etait
fausse. Le fichier dit lui-meme qu'il est partiel. C'est exactement ce que la
verification doit attraper.

## Un bug que j'avais introduit

En Tier 1, pour qu'un lien de table d'affichage puisse atteindre une primitive
allouee par `malloc`, `PsxHeap.InitHeap` s'est mis a declarer son espace a
`LibGpu.RamRegion`. Correct - mais avec un defaut qui ne se voit qu'au **second**
armement.

`RamRegion` apparie par `ReferenceEquals`. Un tableau neuf revendiquant une
adresse qu'une ligne vivante detient deja ajoute donc une **seconde** ligne au
lieu de mettre a jour la premiere. Et le departage de `RamResolve` est un
`>` **strict** sur la base: avec deux lignes de meme base, la premiere trouvee
gagne pour toujours. Le tampon **perime** continue de repondre, et tout ce qui
est alloue apres le re-armement devient inatteignable au rasteriseur. Le registre
fait par ailleurs 64 lignes fixes.

`FUN_80058a9c` re-arme le tas une fois par tour de la boucle de `main` - le
defaut passait donc de latent a atteignable au moment meme ou cette fonction
etait portee.

Sur console `InitHeap` n'alloue rien: il enregistre une base et une taille sur de
la RAM qui existe deja. Le portage s'y conforme maintenant - il garde son tableau
quand la taille ne change pas, ce qui est le cas des deux appels de TITLE.EXE, et
libere l'ancienne region sinon.

**Le banc a ete verifie contre le bug, pas seulement contre le correctif.**
Annuler le changement le fait echouer, et le tampon perime qu'il relit porte
encore l'en-tete de bloc du tas precedent (`lu 0x04`).

## Verification

`FUN_80058a9c`: **CONFIRMED**, tableau d'acceptation point par point contre
Ghidra, incluant les bornes de boucle, les quatre `CreateTask`, les noms de
fichiers et leurs tampons, et le remplissage de CLUT jusqu'au placement de
`iVar5 = 1` **dans le delay slot** de la branche.

Les quatre notes `BLOCKED` et quatre `PARTIAL` ont ete verifiees honnetes une par
une: aucune n'est un stub deguise. `RegisterCallback` n'est appele que pour cinq
adresses dans tout le portage, dont aucune des trois non portees.

Les sept bancs passent.
