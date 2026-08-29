# SLPS_003.55 - BANDAI.STR

## Objectif

Premier jalon du port C# de `SLPS_003.55`: exécuter le chemin original du lecteur
vidéo intégré et afficher `\MOVIE\BANDAI.STR;1` avec le backend MonoGame.

Le lot s'arrête volontairement avant le chargement de `MOVIE.EXE`.

## Preuves

### Entrée et contrôle de flux

- `start @ 0x8002BF00` appelle `main @ 0x80020D10`.
- `main` initialise les bibliothèques PSY-Q puis appelle `MainLoop @ 0x80020DE8`.
- `MainLoop` est l'unique référence à la chaîne `\MOVIE\BANDAI.STR;1` située à
  `0x80020000`.
- Le lecteur appelle, dans l'ordre, `CdSearchFile`, `FUN_800210a4`,
  `FUN_80021118`, `FUN_800212e4`, `DecDCTin`, `DecDCTout`, `FUN_800214b0`, puis
  attend avec `VSync(4)`.
- `FUN_8002136c` passe l'état à 1 lorsque le numéro de frame est supérieur à
  `0x59`. La frame 90 termine donc le flux; les frames 1 à 89 sont présentées.
- Après le délai final initialisé à `0x1E`, l'original appelle
  `FUN_800215c0("cdrom:\\MOVIE.EXE;1")`.

### Fichier STR

Mesures sur `data/MOVIE/BANDAI.STR`:

| Élément | Valeur |
|---|---:|
| Taille de secteur source | 2352 octets |
| Secteurs totaux | 911 |
| Secteurs vidéo | 788 |
| Secteurs XA audio | 113 |
| Autres secteurs | 10 |
| Frames vidéo | 90, numérotées 1 à 90 |
| Dimensions | 320 x 240 pour toutes les frames |
| Version BS | 3 pour toutes les frames |

Le payload de chaque chunk commence par 2016 octets utiles après l'en-tête STR
de 32 octets. Le premier payload commence par l'en-tête BS
`20 07 00 38 01 00 03 00`.

### État FMV

Ghidra et une lecture RAM PCSX pendant la vidéo ferment la structure suivante:

```c
struct UnkStruct_8009A594 {
    u_long *field_0x00;
    u_long *field_0x04;
    u_long field_0x08;
    u_long *field_0x0C;
    RECT field_0x10;
    RECT field_0x18;
    u_long field_0x20;
    RECT field_0x24;
    u_long field_0x2C;
};
```

Taille: `0x30` octets.

Valeurs observées pendant `BANDAI.STR`:

- `field_0x10 = (0, 0, 480, 240)`
- `field_0x18 = (0, 240, 480, 240)`
- `field_0x24 = (0, 240, 24, 240)` au moment de la capture
- `field_0x20 = 1`

Les plages adjacentes sont:

| Adresse | Taille | Usage prouvé |
|---|---:|---|
| `0x8004C894` | `0x25800` | buffer VLC 0 |
| `0x80072094` | `0x25800` | buffer VLC 1 |
| `0x80097894` | `0x2D00` | bande MDEC RGB24, 16 x 240 x 3 |
| `0x8009A594` | `0x30` | état FMV |
| `0x8009A5C4` | `0x10000` | ring STR de 32 slots |

## Synchronisation Ghidra

- Création de `FUN_800211b0 @ 0x800211B0` avec prototype `void(void)`.
- Création et application de `UnkStruct_8009A594` à `0x8009A594`.
- Application des types Psy-Q existants `CdlLOC` à `0x8004C87C` et `CdlATV`
  à `0x8004C888`.
- Prototypes synchronisés pour `main`, `MainLoop`, `FUN_800210a4`,
  `FUN_80021118`, `FUN_800212e4`, `FUN_8002136c`, `FUN_800214b0`,
  `FUN_80021574`, `SetVolume`, `FUN_8002c57c`, `FUN_8002c80c`,
  `FUN_8002c9dc`, `FUN_80035410` et `FUN_8002165c`.
- Aucun `FUN_` ou `DAT_` n'a reçu de nom sémantique spéculatif.

Note ReVa: `FUN_800211b0` se décompile sur le corps complet jusqu'au `jr ra` à
`0x800212DC`, mais la métadonnée `sizeInBytes` renvoyée par ReVa reste à 1.

## Adaptations desktop

- `LibDs` détecte les sources raw 2336 ou 2352 octets. Pour 2352, les 16 octets
  sync/MSF/mode sont retirés avant de transmettre le secteur au contrat 2336
  existant de `LibCd` et `XaAudio`.
- `CdSearchFile`, `CdControl(CdlSeekL)` et `CdRead2` délèguent au registre et à
  la source desktop de `LibDs`.
- `MdecCore` accepte les versions BS 2 et 3. La version 3 utilise les tables DC
  MPEG-1 documentées, trois prédicteurs Cr/Cb/Y remis à zéro par frame et des
  différentiels multipliés par 4. Le chemin AC v2 existant reste commun.
- `SpuInit` démarre le pump audio desktop qui consomme le FIFO XA via
  `SpuCore.RenderSamples`; le runtime configure ensuite le mix et les volumes par
  ses appels originaux.
- Le runtime tourne sur un thread séparé. `FrameBaton` conserve l'ordre
  runtime/VSync/présentation et signale la fin normale du lot sans bloquer le
  thread MonoGame.
- `Game1` ne contient aucune logique vidéo: il présente uniquement la fenêtre
  active de `LibGpu.Vram` en 320 x 240.

## Validation

Commandes:

```powershell
dotnet run --project .\custom-tools\DbzLegendsAnalyser\DbzLegendsRemaster\DbzLegendsRemaster.csproj --no-build -- --validate-bandai .\data\MOVIE\BANDAI.STR

dotnet run --project .\custom-tools\DbzLegendsAnalyser\DbzLegendsRemaster\DbzLegendsRemaster.csproj --no-build -- --validate-str-v2 D:\development\repo\decomp\parasite-eve-1\data\disk-1\FMV1\FMV000.STR
```

Résultats observés:

- layout C# `UnkStruct_8009A594`: `0x30`, offsets conformes;
- flux DBZ: frames 1 à 90, en-têtes 320 x 240 v3 conformes;
- frames 1, 8, 30 et 89: 300 macroblocs et 20 bandes RGB24;
- frame 8 C#: non uniforme, visuellement identique à l'oracle jPSXdec;
- comparaison frame 8 avec jPSXdec: erreur absolue moyenne `1.0244` par canal,
  erreur RGB cumulée maximale `61`;
- comparaison de la fenêtre MonoGame finale avec la frame 89 du banc: 76 798
  pixels identiques sur 76 800, erreur absolue moyenne `0.0029` par canal;
- vraie frame STR v2 `FMV000.STR`: 300 macroblocs, succès;
- la même fixture v2 traverse aussi le chemin de streaming 2336 et libère sa
  première frame correctement;
- build du projet remaster: succès;
- lancement natif: fenêtre réactive, dernière image Bandai présentée et conservée;
- capture native non occluse via `PrintWindow`: logo final complet;
- audio natif: `PE_AUDIO_DIAG` observe `windowMax=5185`, puis `14495` pendant le
  film; le banc vérifie aussi les 113 secteurs XA et un PCM non silencieux;
- fermeture normale de la fenêtre: thread runtime terminé en moins de 5 secondes.

## Limites explicites

- `BLOCKED`: chargement et exécution de `MOVIE.EXE`, prochain lot.
- `BLOCKED`: corps de `FUN_8002c57c`, initialisation des timers/IRQ du séquenceur
  audio; le flux matériel XA est indépendant et fonctionne via le pump SPU.