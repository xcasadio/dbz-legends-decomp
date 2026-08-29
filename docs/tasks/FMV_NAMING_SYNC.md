# Synchronisation des noms FMV C# / Ghidra

## Portée

Ce lot synchronise uniquement les éléments dont la sémantique est fermée par
les accès ASM, les prototypes, les adresses mémoire et les deux lecteurs STR.
Chaque renommage ci-dessous est classé `CERTAIN`. Les adresses et les layouts ne
changent pas.

Programmes Ghidra concernés:

- `/SLPS_003.55`
- `/MOVIE.EXE`

Surfaces C# concernées:

- `SLPS_003_55/SLPS_003_55_exe.cs`
- `MOVIE_EXE/MOVIE_EXE_exe.cs`
- `Types/MoviePlaybackState.cs`

## Type partagé par rôle

Ghidra conserve un type indépendant dans chaque programme, car ses gestionnaires
de types sont propres aux overlays. Le C# partage un miroir neutre unique:
`DbzLegendsRemaster.Types.MoviePlaybackState`. Ce type ne contient aucune adresse;
les adresses qui se chevauchent restent isolées dans chaque résolveur d'overlay.

| Programme | Ancien type | Nouveau type | Adresse | Taille |
|---|---|---|---:|---:|
| SLPS | `UnkStruct_8009A594` | `MoviePlaybackState` | `0x8009A594` | `0x30` |
| MOVIE | `UnkStruct_8008DC30` | `MoviePlaybackState` | `0x8008DC30` | `0x30` |

| Offset | Ancien champ | Nouveau champ | Type Ghidra |
|---:|---|---|---|
| `0x00` | `field_0x00` | `vlcBuffer0` | `u_long *` |
| `0x04` | `field_0x04` | `vlcBuffer1` | `u_long *` |
| `0x08` | `field_0x08` | `vlcBufferIndex` | `u_long` |
| `0x0C` | `field_0x0C` | `mdecOutputBuffer` | `u_long *` |
| `0x10` | `field_0x10` | `frameBuffer0Rect` | `RECT` |
| `0x18` | `field_0x18` | `frameBuffer1Rect` | `RECT` |
| `0x20` | `field_0x20` | `writeBufferIndex` | `u_long` |
| `0x24` | `field_0x24` | `mdecOutputRect` | `RECT` |
| `0x2C` | `field_0x2C` | `frameUploadComplete` | `u_long` |

Ghidra confirme dans les deux programmes: taille `0x30`, alignement 4, neuf
champs et trois `RECT` de 8 octets. Le miroir C# utilise `Types.RECT`, type valeur
blittable également de taille `0x8`. Les anciens types ont été supprimés avec
`force=false` après vérification de zéro référence résiduelle.

## Fonctions

### SLPS_003.55

| Adresse | Ancien nom | Nouveau nom |
|---:|---|---|
| `0x80020DE8` | `MainLoop` | `PlayBandaiMovie` |
| `0x800210A4` | `FUN_800210a4` | `InitializeMoviePlaybackState` |
| `0x80021118` | `FUN_80021118` | `StartMovieStream` |
| `0x800211B0` | `FUN_800211b0` | `MovieMdecOutputCallback` |
| `0x800212E4` | `FUN_800212e4` | `DecodeNextMovieFrameVlc` |
| `0x8002136C` | `FUN_8002136c` | `GetNextMovieFrame` |
| `0x800214B0` | `FUN_800214b0` | `WaitForMovieFrameUpload` |
| `0x80021574` | `FUN_80021574` | `SeekAndStartMovieStream` |
| `0x800215C0` | `FUN_800215c0` | `ShutdownAndLoadExecutable` |
| `0x8002C9DC` | `FUN_8002c9dc` | `SetSpuInputVolume` |
| `0x80035410` | `FUN_80035410` | `SetSpuInputAttribute` |

La callback SLPS conserve l'anomalie ReVa préexistante `sizeInBytes=1`; sa
décompilation continue néanmoins jusqu'au retour à `0x800212DC`.

### MOVIE.EXE

| Adresse | Ancien nom | Nouveau nom |
|---:|---|---|
| `0x80020A90` | `Mainloop` | `PlayDbzOpeningMovie` |
| `0x80020D58` | `FUN_80020d58` | `InitializeMoviePlaybackState` |
| `0x80020DCC` | `FUN_80020dcc` | `StartMovieStream` |
| `0x80020E64` | `FUN_80020e64` | `MovieMdecOutputCallback` |
| `0x80020F98` | `FUN_80020f98` | `DecodeNextMovieFrameVlc` |
| `0x80021020` | `FUN_80021020` | `GetNextMovieFrame` |
| `0x80021164` | `FUN_80021164` | `WaitForMovieFrameUpload` |
| `0x80021228` | `FUN_80021228` | `SeekAndStartMovieStream` |
| `0x80021274` | `FUN_80021274` | `ShutdownAndLoadExecutable` |

`MovieMdecOutputCallback` conserve son corps de `0x134` octets, de
`0x80020E64` à `0x80020F97`. `ShutdownAndLoadExecutable` n'est renommée que dans
Ghidra: son port C# appartient au prochain lot `TITLE.EXE`.

## Globales

| Rôle | SLPS adresse / ancien nom | MOVIE adresse / ancien nom | Nouveau nom |
|---|---|---|---|
| largeur vidéo | `0x8004C874` / `DAT_8004c874` | `0x8003FF10` / `DAT_8003ff10` | `g_MovieFrameWidth` |
| hauteur vidéo | `0x8004C878` / `DAT_8004c878` | `0x8003FF14` / `DAT_8003ff14` | `g_MovieFrameHeight` |
| position CD | `0x8004C87C` / `DAT_8004c87c` | `0x8003FF18` / `DAT_8003ff18` | `g_MovieStartLocation` |
| statut | `0x8004C880` / `DAT_8004c880` | `0x8003FF1C` / `DAT_8003ff1c` | `g_MovieStatus` |
| matrice audio CD | `0x8004C888` / `DAT_8004c888` | `0x8003FF24` / `DAT_8003ff24` | `g_MovieCdAudioMix` |
| délai de fin | `0x8004C890` / `DAT_8004c890` | `0x8003FF2C` / `DAT_8003ff2c` | `g_MovieEndCountdown` |
| buffer VLC 0 | `0x8004C894` / `DAT_8004c894` | `0x8003FF30` / `DAT_8003ff30` | `g_MovieVlcBuffer0` |
| buffer VLC 1 | `0x80072094` / `DAT_80072094` | `0x80065730` / `DAT_80065730` | `g_MovieVlcBuffer1` |
| sortie MDEC | `0x80097894` / `DAT_80097894` | `0x8008AF30` / `DAT_8008af30` | `g_MovieMdecOutputBuffer` |
| état de lecture | `0x8009A594` / `DAT_8009a594` | `0x8008DC30` / `DAT_8008dc30` | `g_MoviePlayback` |
| ring STR | `0x8009A5C4` / `DAT_8009a5c4` | `0x8008DC60` / `DAT_8008dc60` | `g_MovieStreamRing` |
| interruption CD différée | `0x800B1704` / `DAT_800b1704` | `0x800A45F4` / `DAT_800a45f4` | `g_StCdInterruptPending` |

Les constantes C# qui matérialisent les quatre adresses de buffers sont
`MovieVlcBuffer0Address`, `MovieVlcBuffer1Address`,
`MovieMdecOutputBufferAddress` et `MovieStreamRingAddress`.

## Paramètres synchronisés

Les prototypes C# et Ghidra utilisent les noms prouvés suivants:

- `state`
- `frameBuffer0X`, `frameBuffer0Y`, `frameBuffer1X`, `frameBuffer1Y`
- `startLocation`, `mdecOutputCallback`
- `inputIndex`, `attributeIndex`, `value`
- `leftVolume`, `rightVolume`
- `exeFileName`

## Exclusions

Les symboles suivants restent bruts, faute de preuve suffisante pour un nom
sémantique stable:

- `FUN_8002C80C`
- `FUN_8002C57C`
- `FUN_8002C84C`
- `FUN_8002C8F0`
- callee `FUN_800378C8`
- `FUN_8002165C`
- `SHORT_ARRAY_801FF000`

Les noms existants `main`, `__main`, `_96_init`, `LoadExec` et `SetVolume` sont
conservés. Aucun sous-système audio incomplet ni donnée inter-overlay non lue
n'a reçu de nom spéculatif.

## Validation

- Rebuild .NET non incrémental: succès.
- BANDAI: 911 secteurs, 90 frames complètes, 113 secteurs XA.
- DBZ_OP: 9 479 secteurs, 945 frames complètes, seuil de sortie à 930,
  1 184 secteurs XA.
- Transition XA: premier paquet DBZ_OP bit-identique après reset de fichier;
  seek dans le même fichier sans reset.
- STR v2: première frame de 300 macroblocs décodée, livrée et libérée.
- Chaîne native BANDAI vers DBZ_OP: aucune faute runtime; audio non nul pendant
  l'opening (`windowMax=13338`, puis `11922`) et retour au silence après le flux.
- Recherche C#: aucun `DAT_`, `UnkStruct` ou `field_0x` dans les deux surfaces;
  les seuls `FUN_` SLPS restants appartiennent à la liste d'exclusion.
- Recherche Ghidra: aucun ancien type ni ancien nom de fonction ciblé dans les
  décompilations des deux programmes après synchronisation.
- `git diff --check`: succès avant la synchronisation documentaire.

## Retour arrière

Les tableaux de ce document constituent le journal réversible: chaque nouveau
nom est associé à son ancien nom et à son adresse. Aucun renommage ne dépend
d'une relocalisation. Les anciens types peuvent être recréés avec les mêmes neuf
champs et réappliqués aux adresses indiquées si un retour arrière est nécessaire.