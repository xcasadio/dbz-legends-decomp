# SDK - entree pad

## Objectif

Implementer reellement les fonctions `Pad*` du SDK. Elles etaient toutes des
stubs `// Do nothing` dans `PsxSdkMonogame/LibEtc.cs`, donc `PadRead` renvoyait
toujours `0` et aucune entree ne parvenait au runtime translittere: ni le saut
des deux FMV, ni la moindre action de l'ecran titre.

C'est un prerequis a tout portage de `TITLE.EXE`, dont la boucle de frame lit le
pad a chaque tour.

## Preuves

### Contrat des fonctions

Ferme par Ghidra, identique dans les trois overlays.

| Fonction | SLPS_003.55 | MOVIE.EXE | TITLE.EXE |
|---|---|---|---|
| `PadInit` | `0x8002B850` | - | `0x8006FDA0` |
| `PadRead` | `0x8002B8A0` | - | `0x8006FDF0` |
| `PadStop` | `0x8002B8D0` | - | `0x8006FE20` |

```c
void PadInit(int mode)
{
  buffer = 0xffffffff;          // DAT_800B0F34 (SLPS) / DAT_800920D4 (TITLE)
  padMode = mode;
  ResetCallback();
  PAD_init(0x20000001, &buffer);
  ChangeClearPAD(0);
}

u_long PadRead(int id)
{
  PAD_dr();
  return ~buffer;
}
```

Trois points fermes par cette lecture:

1. le buffer est **actif-bas**, initialise a `0xFFFFFFFF`, et `PadRead` en renvoie
   le complement a un, donc **actif-haut**;
2. `PadRead` **ignore son argument `id`**: il rafraichit le buffer partage et
   renvoie les deux ports d'un coup;
3. `PAD_dr` n'est qu'un saut vers le vecteur BIOS `B0`, donc le format du buffer
   est celui du BIOS et non un format propre au jeu.

### Layout des bits

Deux sources independantes, aucune supposition.

**Source 1, le jeu lui-meme.** `FUN_8002165c @ 0x8002165C` remplit une table de
quatorze masques, repetee a l'identique pour un second pad:

```
0x0020 0x0080 0x0010 0x0040 0x2000 0x8000 0x1000 0x4000
0x0100 0x0800 0x0008 0x0002 0x0004 0x0001
```

Ce sont exactement les quatorze boutons d'un pad numerique PlayStation. Les deux
masques absents sont `0x0200` et `0x0400`, soit L3 et R3, ce qui est correct pour
un pad numerique.

**Source 2, PCSX-Redux sur le vrai jeu.** Avec `TITLE.EXE` comme overlay
resident, lecture directe de son buffer `DAT_800920D4`:

| Etat | Valeur lue |
|---|---|
| repos | `0xFFFFFFFF` |
| Start maintenu | `0xFFFFF7FF` |

Le bit `0x0800` tombe a zero, dans le **halfword bas**. Cela ferme les deux
dernieres inconnues: `Start = 0x0800`, et **le port 1 occupe les bits 0 a 15**.
C'est ce qui explique enfin le `PadRead(1) & 0x800` des deux lecteurs FMV et le
`DAT_800834FC & 0x800` de `RunFrameLoop`: ce test lit Start.

Layout retenu, pose en constantes dans `PadButton`:

| Masque | Bouton | Masque | Bouton |
|---|---|---|---|
| `0x0001` | L2 | `0x0100` | Select |
| `0x0002` | R2 | `0x0200` | L3 |
| `0x0004` | L1 | `0x0400` | R3 |
| `0x0008` | R1 | `0x0800` | Start |
| `0x0010` | Triangle | `0x1000` | Up |
| `0x0020` | Circle | `0x2000` | Right |
| `0x0040` | Cross | `0x4000` | Down |
| `0x0080` | Square | `0x8000` | Left |

## Implementation

### LibEtc

`PadInit`, `PadRead` et `PadStop` suivent le contrat ci-dessus. Le buffer
`s_padBuffer` est actif-bas comme le materiel, `PAD_dr` est remplace par la
lecture de l'instantane publie par l'hote, et `PadRead` renvoie toujours
`~s_padBuffer` sans regarder son argument.

`PadStop` marque le pilote comme retire; l'overlay suivant le reinstalle par son
propre `PadInit`, ce qui est exactement la sequence que chaque
`ShutdownAndLoadExecutable` execute.

### PadInputBackend

Nouveau fichier du SDK, sur le modele de `SpuAudioBackend`. Il echantillonne
clavier et manette et publie un mot 32 bits actif-bas, port 1 dans le halfword
bas.

L'echantillonnage a lieu **sur le thread hote**, depuis `Game1.Update`, et le
runtime ne lit que l'instantane publie. Le runtime tourne sur son propre thread
et ne doit jamais appeler MonoGame; c'est la meme frontiere que celle deja posee
pour le rendu. Fonctionnellement, cela correspond au pad echantillonne a chaque
retour de balayage.

### Correspondance des touches

| Bouton PSX | Clavier | Manette |
|---|---|---|
| D-Pad | fleches | D-Pad |
| Cross | `X` | A |
| Circle | `D` | B |
| Square | `Z` | X |
| Triangle | `S` | Y |
| L1 / R1 | `A` / `F` | LB / RB |
| L2 / R2 | `Q` / `R` | gachettes |
| Start | `Entree` | Start |
| Select | `Espace` | Back |
| L3 / R3 | - | clic des sticks |

`Echap` reste reserve a la fermeture de la fenetre par l'hote.

### Diagnostics

Deux variables d'environnement, sur le modele du `PE_AUDIO_DIAG` existant:

- `DBZ_PAD_DIAG=1` trace le mot pad a chaque changement;
- `DBZ_PAD_FORCE=0x0800` force un masque de boutons sur le port 1.

La seconde existe parce que Windows refuse le transfert de focus vers un
processus d'arriere-plan: un test par frappes synthetiques n'atteint jamais la
fenetre SDL, ce qui a ete constate lors de la validation. L'injection se fait au
point d'echantillonnage des peripheriques, donc `PadRead` et tous les sites
d'appel d'origine restent inchanges.

## Validation

```powershell
$env:DBZ_OVERLAY_DIAG = "1"; $env:DBZ_PAD_DIAG = "1"; $env:DBZ_PAD_FORCE = "0x0800"
dotnet run --project .\custom-tools\DbzLegendsAnalyser\DbzLegendsRemaster\DbzLegendsRemaster.csproj -c Release --no-build
```

Sortie complete hors notes `MdecCore`:

```
[pad] active-low=FFFFF7FF PadRead=00000800
[overlay] LoadExec -> MOVIE.EXE
[overlay] LoadExec -> TITLE.EXE
```

Trois resultats:

- `active-low=FFFFF7FF` est **la valeur exacte lue sur la vraie console** par
  PCSX-Redux dans les memes conditions. Le buffer desktop est bit-a-bit
  identique au buffer materiel;
- `PadRead=00000800` confirme le complement, donc `& 0x800` est vrai;
- les deux overlays s'enchainent en moins de 40 secondes au lieu des 75 secondes
  necessaires pour lire les deux films en entier: **Start a bien saute les deux
  FMV**, ce qui etait impossible avant ce lot.

Builds Debug et Release: 0 erreur.

## Limites explicites

- Les fonctions de l'API **libpad** restent des stubs: `PadInitDirect`,
  `PadInitMtap`, `PadInitGun`, `PadStartCom`, `PadStopCom`, `PadEnableCom`,
  `PadChkVsync`, `PadGetState`, `PadInfoAct`, `PadInfoComb`, `PadInfoMode`,
  `PadSetAct`, `PadSetActAlign`, `PadSetMainMode`, `PadEnableGun`,
  `PadRemoveGun`. Aucun de ces symboles n'existe dans `TITLE.EXE`: le jeu
  n'utilise que l'API BIOS. Les implementer maintenant reviendrait a inventer un
  contrat sans usage prouve, et leurs signatures C# actuelles sont d'ailleurs
  douteuses, `PadInitDirect` prenant deux `byte` la ou libpad attend deux
  pointeurs de buffer.
- `PadGetState` conserve son retour `PadStateStable` et le long commentaire
  herite d'un autre portage; ce commentaire cite des fichiers absents de ce
  depot et devra etre revu si libpad est un jour implemente.
- La correspondance clavier n'a pas pu etre validee automatiquement, faute de
  focus; elle demande un essai manuel.
