# MOVIE.EXE - DBZ_OP.STR

## Objectif

Deuxième jalon vidéo du port C#: adapter le `LoadExec` du bootstrap, exécuter
l'overlay `MOVIE.EXE`, puis afficher `\MOVIE\DBZ_OP.STR;1` avec son audio XA.

Le lot s'arrête volontairement avant le chargement de `TITLE.EXE`.

## Preuves Ghidra

### Entrée et contrôle de flux

- `start @ 0x8002B954` appelle `main @ 0x800209FC`.
- `main` initialise les services PSY-Q graphiques/CD/pad, écrit `0x1E` dans
  `DAT_8003ff2c`, puis appelle `Mainloop @ 0x80020A90`.
- `Mainloop` est l'unique référence à `\MOVIE\DBZ_OP.STR;1` à `0x80020000`.
- Le pipeline local est constitué de `FUN_80020d58`, `FUN_80020dcc`,
  `FUN_80020e64`, `FUN_80020f98`, `FUN_80021020`, `FUN_80021164` et
  `FUN_80021228`.
- `DecDCTin` reçoit le mode `1`, contrairement au mode `3` du lecteur Bandai.
- La boucle attend avec `VSync(4)` et lit le pad deux fois sur le chemin de fin.
- `FUN_80021020` passe l'état à 1 lorsque `frameNumber > 0x3A1`: la frame 930
  déclenche l'arrêt et les frames 1 à 929 sont soumises au MDEC.
- Après le délai final, `Mainloop` appelle
  `FUN_80021274("cdrom:\\TITLE.EXE;1")`.

### État FMV

Le type créé et appliqué dans `/MOVIE.EXE` est:

```c
struct UnkStruct_8008DC30 {
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

Taille: `0x30` octets, alignement 4.

| Adresse | Taille | Usage prouvé |
|---|---:|---|
| `0x8003FF30` | `0x25800` | buffer VLC 0 |
| `0x80065730` | `0x25800` | buffer VLC 1 |
| `0x8008AF30` | `0x2D00` | bande MDEC RGB24 |
| `0x8008DC30` | `0x30` | état FMV |
| `0x8008DC60` | `0x10000` | ring STR de 32 slots |

Ces plages chevauchent celles de `SLPS_003.55`. Elles ne peuvent pas être
résolues simultanément: l'adaptation `LoadExec` remplace le résolveur RAM actif
au passage dans `MOVIE.EXE`.

### Synchronisation Ghidra

- `FUN_80020e64 @ 0x80020E64` est une vraie fonction de `0x134` octets, jusqu'à
  `0x80020F97`.
- `UnkStruct_8008DC30` est appliqué à `DAT_8008dc30`.
- `CdlLOC` est appliqué à `DAT_8003ff18`; `CdlATV` à `DAT_8003ff24`.
- Les dimensions, l'état et le compteur ont leurs largeurs scalaires prouvées.
- Les prototypes de `main`, `Mainloop` et des fonctions locales du pipeline sont
  synchronisés avec ces types.
- Les labels `DAT_...` restent primaires; aucun renommage sémantique spéculatif
  n'a été effectué.

## Fichier DBZ_OP.STR

Mesures exhaustives sur `data/MOVIE/DBZ_OP.STR`:

| Élément | Valeur |
|---|---:|
| Taille | 22 294 608 octets |
| Secteurs raw | 9 479 x 2352 octets |
| Secteurs vidéo | 8 269 |
| Secteurs XA audio | 1 184 |
| Autres secteurs | 26 |
| Frames | 945, numérotées 1 à 945 |
| Dimensions/version | 320 x 240, BS v3 |
| Chunks par frame | 8 ou 9 |
| Taille démultiplexée maximale | 10 080 octets |
| XA | canal 1, submode `0x64`, coding-info `0x01` |

Aucune frame incomplète, aucun chunk dupliqué et aucune taille démultiplexée ne
dépasse la capacité annoncée. jPSXdec identifie indépendamment le fichier comme
BIN/CUE raw, 945 frames, 320 x 240, lecture 2x à 15 fps.

## Adaptations desktop

- Le `LoadExec` du SLPS conserve l'ordre de teardown observable, commute
  `PsxRam.AddressResolver`, puis appelle `MOVIE_EXE_exe.Main` sur le même thread.
- Le pump SPU n'est pas redémarré: `MOVIE.EXE` n'appelle pas `SpuInit` et le
  périphérique SPU physique survit au remplacement d'overlay.
- `LibDs.DsRead2` détecte un changement de fichier enregistré et remet alors le
  FIFO/prédicteur XA à zéro. Un seek dans le même fichier conserve cet état.
- Le fichier `DBZ_OP.STR` est copié dans la sortie du remaster et résolu par le
  pont ISO existant.
- La frontière `TITLE.EXE` reste `BLOCKED`; le host conserve la dernière VRAM.

## Validation

Commandes principales:

```powershell
dotnet run --project .\custom-tools\DbzLegendsAnalyser\DbzLegendsRemaster\DbzLegendsRemaster.csproj -- --validate-dbz-op .\data\MOVIE\DBZ_OP.STR

dotnet run --project .\custom-tools\DbzLegendsAnalyser\DbzLegendsRemaster\DbzLegendsRemaster.csproj -- --validate-xa-transition .\data\MOVIE\BANDAI.STR .\data\MOVIE\DBZ_OP.STR
```

Résultats:

- layout C# `UnkStruct_8008DC30`: taille et offsets conformes à Ghidra;
- 945 en-têtes complets et séquentiels; premier seuil de sortie à la frame 930;
- frames 1, 300, 600 et 929: 300 macroblocs et 20 bandes RGB24;
- 1 184 secteurs XA, PCM non silencieux;
- transition XA: premier paquet DBZ_OP bit-identique entre démarrage propre et
  démarrage après Bandai;
- seek dans DBZ_OP: compteur XA conservé, donc absence de reset intempestif;
- frame 600 contre jPSXdec: erreur absolue moyenne `0.9507` par canal, erreur RGB
  cumulée maximale `57`, rendu visuellement identique;
- lecture native complète BANDAI vers DBZ_OP: fenêtres PCM non nulles pendant
  l'opening, notamment `windowMax=13338` et `11922`;
- fenêtre finale contre frame 929 hors ligne: 76 799 pixels identiques sur
  76 800, erreur absolue moyenne `0.00016` par canal;
- fermeture normale de la fenêtre en moins de 5 secondes.

## Limites explicites

- `BLOCKED`: chargement et exécution de `TITLE.EXE`, prochain lot.
- `PARTIAL`: `FUN_8002c84c` et `FUN_8002c8f0` ferment des callbacks/événements
  audio non installés parce que l'initialisation du séquenceur reste bloquée.
- Le MDEC reste un décodeur spec-based, non bit-exact au matériel PSX.