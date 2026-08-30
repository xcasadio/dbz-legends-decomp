# TITLE.EXE - lot 1: fermeture du LoadExec

## Objectif

Plus petit pas executable apres `MOVIE_EXE_DBZ_OP_STR.md`: rendre la chaine
d'overlays complete jusqu'au point d'entree de `TITLE.EXE`.

Le lot s'arrete volontairement avant toute logique de l'ecran titre.

Il se decompose en deux changements de nature differente:

1. **Restauration du controle de flux original** de `SLPS_003.55` et
   `MOVIE.EXE`, deux ecarts de fidelite constates dans le port existant;
2. **Entree dans l'overlay `TITLE.EXE`**, avec un `main` encore vide.

## Preuves

Toutes les lignes de cette section sont fermees par Ghidra via ReVa. Les MD5 des
binaires `data/TITLE.EXE` et `data/MOVIE.EXE` sont identiques a ceux enregistres
dans `ghidra/dbz-legends.rep`, ce qui autorise le recoupement avec le
desassemblage direct des fichiers.

### En-tetes PS-X EXE

| Champ | TITLE.EXE | MOVIE.EXE |
|---|---|---|
| PC initial (`start`) | `0x80068FF4` | `0x8002B954` |
| `t_addr` | `0x80020000` | `0x80020000` |
| `t_size` | `0x000E5800` (940 032 o) | `0x00020000` (131 072 o) |
| stack | `0x801FFFF0` | `0x801FFFF0` |

### Point d'entree de TITLE.EXE

`main @ 0x800581DC`, appelee par `start @ 0x80068FF4` depuis `0x80069090`.
Ghidra porte deja ce nom. Le prologue `start` est structurellement identique a
celui de `MOVIE.EXE`: effacement BSS, installation de `sp`/`gp`/`fp`,
`jal <InitHeap>` avec `addi $a0, $a0, 4` en delay slot, restauration de `$ra`
depuis une globale, `jal <main>`, puis `break`.

`main` ne retourne jamais: son corps se termine sur `do { ... } while (true)`.
Sa sequence d'ouverture est `__main`, `FUN_80070b64` (`syscall(0)`, marquee
*Possible A36.OBJ/EnterCriticalSection*), `ResetCallback`, `ResetGraph(0)`,
`InitGeom`, `SetDispMask(0)`, `FUN_80057508`, `PadInit(0)`, `CdInit`, une boucle
de reessai `CdSearchFile` sur `\SELECT.EXE;1`, puis
`ReadFile("\SUB\TITLE.B;1", &DAT_80110000, 0)`, `InitHeap`, `srand`,
`FUN_80070e44`, `FntLoad(0x3c0, 0x100)` et
`FntOpen(0x10, 0x10, 0x100, 200, 0, 0x200)`.

Dimension du reste a faire: `TITLE.EXE` compte 1 251 fonctions, dont 860 deja
nommees et environ 391 encore brutes.

### Structure reelle des deux overlays deja portes

Les deux `main` ont la meme forme, et **aucun des deux n'appelle**
`ShutdownAndLoadExecutable`:

```c
// main @ 0x80020D10 (SLPS)        // main @ 0x800209FC (MOVIE)
  ...init...                         ...init...
  do {                               do {
    PlayBandaiMovie();                 PlayDbzOpeningMovie();
  } while( true );                   } while( true );
```

L'appel se trouve dans la fonction de lecture, et chaque overlay n'a qu'un seul
appelant confirme par `find-cross-references`:

| Overlay | Fonction appelante | Adresse de l'appel | Argument |
|---|---|---|---|
| SLPS | `PlayBandaiMovie @ 0x80020DE8` | `0x80021040` | `"cdrom:\MOVIE.EXE;1"` |
| MOVIE | `PlayDbzOpeningMovie @ 0x80020A90` | `0x80020CF4` | `"cdrom:\TITLE.EXE;1"` |

Dans les deux cas l'appel est precede de `SetDispMask(0)` et **suivi de code
inatteignable**, puisque `LoadExec` ne retourne pas. Pour `MOVIE.EXE`, tout le
bloc `0x80020CFC..0x80020D54` est mort, epilogue de `PlayDbzOpeningMovie`
compris. La chaine `"cdrom:\TITLE.EXE;1"` est a `0x80020014`, unique chaine
`cdrom:` du binaire.

### Sequences d'arret

| # | SLPS `ShutdownAndLoadExecutable @ 0x800215C0` | MOVIE `@ 0x80021274` |
|--:|---|---|
| 1-4 | `StopRCnt(0xF2000000..3)` | `StopRCnt(0xF2000000..3)` |
| 5 | `PadStop()` | `PadStop()` |
| 6 | `FUN_8002c84c()` | *absent* |
| 7 | `FUN_8002c8f0()` | *absent* |
| 8 | `ResetGraph(0)` | `ResetGraph(0)` |
| 9 | `CdFlush()` | `CdFlush()` |
| 10 | `StopCallback()` | `StopCallback()` |
| 11 | `_96_init()` | `_96_init()` |
| 12 | `LoadExec(exeFileName, &DAT_801fff00, 0)` | `LoadExec(exeFileName, &DAT_801fff00, 0)` |

L'absence des deux appels audio dans `MOVIE.EXE` est coherente: cet overlay
n'appelle pas `SpuInit`.

`_96_init` et `LoadExec` sont des stubs de vecteur BIOS, `A0(0x71)` et
`A0(0x51)`:

```
0x80021310: addiu $t2, $zero, 0xA0
0x80021314: jr    $t2
0x80021318: addiu $t1, $zero, 0x51
```

`LoadExec` recoit donc **trois arguments**. Son prototype n'est pas ferme dans
Ghidra (`undefined LoadExec(void)`), donc les deux arguments de pile gardent des
noms bruts `param_2` et `param_3` cote C#.

## Ecarts corriges dans le port

| | Original | Port avant ce lot | Port apres |
|---|---|---|---|
| A | `ShutdownAndLoadExecutable` appelee depuis la fonction de lecture | deplacee dans `Main()` | remise sur son site d'appel |
| B | `main` fait `do { Play...(); } while (true)` | appel unique, sans boucle | boucle restauree |
| C | `LoadExec` recoit trois arguments | un seul argument | trois arguments |

Les ecarts A et B se compensaient: le comportement observable etait correct,
mais la lecture 1:1 etait rompue et l'erreur allait se propager a `TITLE.EXE`.

## Adaptations desktop

### Contrat de transfert de LoadExec

Sur materiel, `A0(0x51)` remplace l'executable resident et transfere le controle
definitivement: il ne revient jamais a son appelant. C'est precisement ce qui
rend mort le code qui suit chaque site d'appel.

Cote desktop, l'adaptation execute le `Main()` de l'overlay entrant sur le meme
thread, puis leve `LoadExecTransferException` au lieu de retourner. Sans cela,
la boucle `do { ... } while (true)` restauree par l'ecart B rejouerait le film
indefiniment.

`LoadExecTransferException` est une adaptation PSX/desktop locale au remaster;
elle est rattrapee par le wrapper de thread runtime de `Game1`, qui appelle
alors `FrameBaton.CompleteRuntime()`. Le SDK partage n'est pas modifie.

### Bascule du resolveur RAM

`PsxSdkBridges.ActivateTitleExe` installe `TITLE_EXE_exe.ResolveAddress`. Aucune
globale de `TITLE.EXE` n'etant encore translitteree, ce resolveur ne resout
aucune plage; la bascule sert a ce que les plages de `MOVIE.EXE`, qui se
chevauchent, cessent de repondre, comme `LoadExec` le fait de la RAM residente.

### Diagnostic de chaine d'overlays

`PsxSdkBridges.TraceOverlay` trace la bascule sur la console lorsque
`DBZ_OVERLAY_DIAG=1`, sur le modele du `PE_AUDIO_DIAG` du SDK. Le diagnostic est
opt-in et aucun controle de flux du runtime n'en depend.

## Surfaces touchees

| Fichier | Nature |
|---|---|
| `SLPS_003_55/SLPS_003_55_exe.cs` | ecarts A, B, C |
| `MOVIE_EXE/MOVIE_EXE_exe.cs` | ecarts A, B, C + `ShutdownAndLoadExecutable`, `_96_init`, `LoadExec` |
| `LoadExecTransferException.cs` | contrat de transfert |
| `Game1.cs` | rattrapage du transfert |
| `TITLE_EXE/TITLE_EXE_exe.cs` | point d'entree `main @ 0x800581DC`, vide |
| `PsxSdkBridges.cs` | `ActivateTitleExe`, `TraceOverlay` |

## Validation

Commandes:

```powershell
dotnet build .\custom-tools\DbzLegendsAnalyser\DbzLegendsRemaster\DbzLegendsRemaster.csproj
dotnet build .\custom-tools\DbzLegendsAnalyser\DbzLegendsRemaster\DbzLegendsRemaster.csproj -c Release
$env:DBZ_OVERLAY_DIAG = "1"
dotnet run --project .\custom-tools\DbzLegendsAnalyser\DbzLegendsRemaster\DbzLegendsRemaster.csproj -c Release --no-build
```

Resultats observes:

- build Debug: 0 erreur, 0 avertissement;
- build Release: 0 erreur, 209 avertissements, tous preexistants et situes dans
  `PsxSdkMonogame` et `PsxTools`; aucun ne provient des surfaces de ce lot;
- run Debug de 130 s: 948 frames decodees, aucune exception, aucun crash. Le
  decodage y tourne a environ 7 images par seconde, donc la fin de `DBZ_OP.STR`
  n'est pas atteinte dans ce budget; d'ou la reprise en Release;
- run Release avec `DBZ_OVERLAY_DIAG=1`, la sortie complete hors notes
  `MdecCore` est exactement:

```
[overlay] LoadExec -> MOVIE.EXE
[overlay] LoadExec -> TITLE.EXE
```

Ces deux lignes ferment le lot:

- la premiere prouve que `PlayBandaiMovie` atteint son site d'appel original et
  que le resolveur bascule sur `MOVIE.EXE`;
- la seconde prouve la meme chose pour `PlayDbzOpeningMovie` et l'entree dans
  `main @ 0x800581DC` de `TITLE.EXE`;
- **chaque ligne n'apparait qu'une fois**, ce qui prouve que la boucle
  `do { ... } while (true)` restauree ne rejoue pas le film: le contrat de
  transfert de `LoadExec` tient;
- le processus est encore vivant a l'expiration du budget de 260 s, code de
  sortie `124` du `timeout`, donc la fenetre reste presentable apres la fin du
  runtime translittere, conformement au comportement du lot precedent.

Aucune regression: les deux films se lisent comme dans
`MOVIE_EXE_DBZ_OP_STR.md`, et le comportement observable en fin de chaine est
inchange.

## Limites explicites

- `BLOCKED`: tout le runtime de `TITLE.EXE` au-dela du point d'entree. Aucun des
  appeles `FUN_` du chemin d'ouverture de `main` n'est ferme. Porter un prefixe
  tronque entrerait dans la boucle de reessai `CdSearchFile` sans sortie ni
  `VSync`.
- `PARTIAL`: le prototype BIOS `A0(0x51)` n'est pas ferme dans Ghidra; les deux
  arguments de pile gardent des noms bruts des deux cotes.
- `PARTIAL`: `FUN_8002c84c` et `FUN_8002c8f0` du SLPS ferment toujours des
  callbacks audio non installes, l'initialisation du sequenceur restant bloquee.
- Aucune ecriture n'a ete faite dans le projet Ghidra pour ce lot.
