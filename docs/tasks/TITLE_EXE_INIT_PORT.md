# TITLE.EXE - portage de l'initialisation et du systeme de taches

## Objectif

Premier lot de portage reel de `TITLE.EXE`: le chemin d'ouverture de
`main @ 0x800581DC` et le systeme de taches sur lequel tout l'overlay repose.

Livrable observable: `SUB/TITLE.B` charge en RAM PSX a `0x80110000`.

## Prerequis: le pad

Toutes les fonctions `Pad*` etaient des stubs `// Do nothing`, donc `PadRead`
renvoyait `0` et aucune entree n'atteignait le runtime. Voir
`SDK_PAD_INPUT.md`. Sans ce prealable, ni le saut des FMV ni l'ecran titre ne
peuvent repondre.

## Ce qui a ete ajoute au SDK

| Element | Etat avant | Preuve |
|---|---|---|
| `InitGeom` | absent | `0x8006E1F0`, sept registres de controle GTE |
| `SetFarColor` | absent | `0x8006D884`, trois `sll ,0x4` puis `ldfcdir` |
| `srand` / `rand` | absents | corps lus **dans le BIOS de la console** via PCSX-Redux |
| `InitHeap` / `malloc` / `free` | stub `// Do nothing` | contrat observable, voir plus bas |
| `CdRead` | stub | `0x800697B4`, appele par `ReadCDData` |
| `CdSync` | renvoyait `0` | renvoie desormais `CdlComplete` |
| `CdControl(CdlSetloc)` | non gere | `0x02`, utilise par `ReadCDData` |
| lecture de fichiers plats | refusee | `LibDs` n'acceptait que 2352/2336 |

### srand et rand

`srand @ 0x8006FC80` et `rand @ 0x8006FD80` ne sont que les stubs `A0(0x30)` et
`A0(0x2F)`: aucun corps n'existe dans l'image du jeu. Plutot que de supposer
l'algorithme, les vecteurs ont ete lus dans la table `A0` en memoire
(`0x2BC` et `0x2C0`), qui pointent vers `0xBFC06228` et `0xBFC06254` dans la
ROM BIOS, puis desassembles:

```
rand:  lw $a0, seed / lui $v0,0x41c6 / addiu $v0,0x4e6d / mult / mflo
       addiu $v0,0x3039 / sw $v0, seed / srl $v0,0x10 / andi $v0,0x7fff
srand: sw $a0, seed
```

Soit `seed = seed * 1103515245 + 12345` puis `(seed >> 16) & 0x7FFF`, avec la
graine a `0x000085EC`. C'est bien le generateur lineaire classique, mais il est
desormais **prouve** et non suppose. Cela compte: l'IA de combat tire
`rand() % 0x65` en 49 endroits.

### L'allocateur

`PsxHeap` n'est **pas** une translitteration. `malloc @ 0x800591A0` fait 464
octets appuyes sur `_ExpAllocArea`, `_expand` et plusieurs fragments
`MALLOC_OBJ_*` mal decoupes par Ghidra, et la regle 13 du mandat interdit de
translitterer les routines du SDK comme du runtime metier. Le contrat
observable est reproduit a la place: adresses alignees sur 4 dans la plage
armee, `0` en cas d'echec, liberation avec fusion, memoire adressable par
`PsxRam`.

Ecart accepte: les adresses rendues ne sont pas celles de la console. Aucun
site d'appel observe n'en depend, `CreateTask` ne les compare qu'a `0` et `-1`.

Banc `--validate-heap`: alignement, plage, absence de chevauchement sur 40
blocs vivants, epuisement, reutilisation apres liberation, fusion, relecture
par `PsxRam`. Toutes les verifications passent.

### La lecture de fichiers de donnees

`LibDs` exigeait un dump brut a secteurs de 2352 ou 2336 octets et levait une
exception sinon. `TITLE.B` fait 151 552 octets, multiple d'aucun des deux: il
etait rejete. La detection retombe maintenant sur 2048 apres les deux
dispositions brutes, qui restent prioritaires, donc aucun `.STR` ne change de
comportement.

`ReadDataSectors` resout un LBA vers le fichier hote enregistre, extrait les
2048 octets utiles de chaque secteur quelle que soit sa disposition (offset 24
pour 2352, 8 pour 2336, 0 pour un fichier plat) et les ecrit en RAM PSX.

`CdSync` renvoyait `0`, ce qui n'etait pas neutre: `ReadCDData` boucle sur
`while (status == 0)` et restait bloque indefiniment. `5` aurait ete pire
encore, c'est `CdlDiskError`, que la boucle englobante reessaie sans fin.

## Le systeme de taches

`CreateTask @ 0x80049504`, `DeleteTask @ 0x80049720` et
`ExecuteTaskList @ 0x800497FC`, avec leurs trois tables de 21 entrees. Les
preuves de structure sont dans `TITLE_EXE_TASK_SYSTEM.md`.

Les blocs restent de la vraie memoire: alloues sur le heap, atteints par
`PsxRam` a des adresses PSX, chainage `0x10`/`0x14` parcouru par offsets bruts.
Aucune collection .NET ne remplace une liste chainee.

Le seul point que C# ne peut pas exprimer litteralement est le pointeur de
fonction a `+0x04`, un delegue manage ne pouvant pas vivre dans un `byte[]`. Le
bloc conserve donc **l'adresse PSX d'origine**, celle que la console contient,
et une table de repartition la retransforme en methode portee au moment de
l'appel. Un bloc de tache issu de ce port se compare donc encore octet par
octet avec un bloc extrait de PCSX-Redux.

L'echec d'allocation leve une exception au lieu de tourner indefiniment comme
l'original, pour qu'un heap epuise se signale au lieu de figer sans diagnostic.

Banc `--validate-tasks`: ordre d'execution, `next`/`prev` apres chaque
insertion, role du compteur (`>= 1` saute et decremente, `0` appelle, `-1`
detruit), les trois valeurs de retour de `DeleteTask` et son drapeau de garde
`0x2`, mise a zero du contexte. Toutes les verifications passent.

## Le chemin d'initialisation

Porte de `__main` jusqu'a `SetupGeometry` inclus, avec `ClearVram @ 0x80057508`
et le trio CD `ReadFile @ 0x80057DF4`, `WaitSearchFile @ 0x80057F80`,
`ReadCDData @ 0x80057E40`.

`ReadCDData` conserve la forme de l'original, y compris la boucle externe de
reessai sur le statut 5. Son `while (readBytes = CdReadSync(...), 0 < readBytes)`
est deplie avec l'affectation sortie de la condition, C# n'ayant pas
d'operateur virgule.

`SetupGeometry` est `PARTIAL`: les quatre appels `SetGeom*`/`Set*Color` sont
repris, mais l'original ecrit aussi les matrices de couleur, de lumiere et de
rotation directement dans le scratch COP2 entre `0x1F8000E4` et `0x1F800124`,
zone que ce port ne modelise pas et qu'aucun site d'appel ferme ne relit.

## Validation

```powershell
dotnet run --project .\custom-tools\DbzLegendsAnalyser\DbzLegendsRemaster\DbzLegendsRemaster.csproj -- --validate-heap
dotnet run --project .\custom-tools\DbzLegendsAnalyser\DbzLegendsRemaster\DbzLegendsRemaster.csproj -- --validate-tasks
dotnet run --project .\custom-tools\DbzLegendsAnalyser\DbzLegendsRemaster\DbzLegendsRemaster.csproj -- --validate-title-init
```

Resultats:

- `HEAP: toutes les verifications passent`
- `TASKS: toutes les verifications passent`
- `TITLE-INIT: toutes les verifications passent`

`--validate-title-init` execute reellement `main` sans fenetre et compare
`TITLE.B` en RAM au fichier du disque: identique sur les `0x25000` octets, et
les deux offsets d'en-tete documentes par `TITLE_B_FILE_FORMAT_ANALYSIS.md`
se relisent correctement a travers `PsxRam`.

Non-regression des deux films, apres modification de `LibDs` et `LibCd` qui
sont partages avec le lecteur FMV:

- BANDAI: 911 secteurs, 90 frames, 113 secteurs XA;
- DBZ_OP: 9 479 secteurs, 945 frames, arret a la frame 930, 1 184 secteurs XA.

Ces chiffres sont identiques a ceux consignes dans `SLPS_003_55_BANDAI_STR.md`
et `MOVIE_EXE_DBZ_OP_STR.md`.

## Limites explicites

- `BLOCKED`: la suite de `main`, a partir de
  `CreateTask(FUN_80037388, ...)`. Ni `FUN_80037388`, ni `FUN_80056dc0`, ni
  `FUN_80038228`, ni `FUN_80058d64` ne sont fermes.
- `BLOCKED`: `RunFrameLoop @ 0x800587A8`, qui atteint `FUN_80038228`,
  `FUN_80056b30` et `FUN_80056d00`. C'est le prochain verrou a lever pour
  obtenir une image a l'ecran.
- `PARTIAL`: `SetupGeometry`, matrices COP2 non modelisees.
- `PARTIAL`: `SetFarColor` stocke les registres FC, mais aucune operation GTE
  modelisee ne les consomme.
- La correspondance clavier du pad n'a pas pu etre validee automatiquement,
  faute de focus; elle demande un essai manuel.
