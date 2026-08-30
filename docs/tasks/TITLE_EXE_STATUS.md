# TITLE.EXE - etat du portage

Carte de reprise. Ce qui tourne, ce qui est bloque, et pourquoi.

## Ou en est le chemin de `main`

    FUN_80038228(8,0); FUN_80021dd0(); RunFrameLoop(); FUN_80058a9c(); FUN_80038228(2,4); RunFrameLoop();

**Les six pas sont portes.** Il n'y a plus de stub vide dans la boucle.

L'ecran titre **dessine**: la soumission ecrit 74108 cellules de VRAM, et la
chaine de la case 0 porte les 12 primitives attendues - 10 sprites des quatre
groupes vivants plus les 2 bandes de fond.

## Chiffres

| | |
|---|---:|
| fichiers du portage TITLE.EXE | 15 |
| lignes | 5283 |
| fonctions et globales annotees `GHIDRA:` | 214 |
| bancs de validation | 7, tous verts |

## Ce qui est bloque, et par quoi

Trois sous-systemes, aucun par manque d'analyse - tous par une piece
d'infrastructure absente.

### 1. L'audio

`LAB_800532a4` (la tache) et `FUN_80053304` (l'init) restent bloques.

Le SDK **declare** toute la surface `libsnd` - `SsInit`, `SsStart`, `SsVabOpen*`,
`SsSeq*`, `SsUt*` - et **chaque corps est vide**. Cote `libspu`, `SpuSetVoiceAttr`,
`SpuSetKey`, `SpuWrite0`, `SpuStInit`, `SpuStQuit` et les callbacks de streaming
sont des stubs. `SpuCore.cs` modelise le vrai materiel SPU (512 Kio, 24 voix,
ADPCM, ADSR) mais rien ne l'alimente depuis `libsnd`.

Fait utile: **l'ecran titre n'a pas d'audio dans l'original non plus.** La tache
audio nait de `FUN_80058a9c`, apres la premiere `RunFrameLoop`. L'audio commence
avec l'ecran suivant.

### 2. La carte memoire

`FUN_80022630`, `FUN_80022680`, `FUN_80022780`, `FUN_80023374` et leur cloture.
`libcard` n'est pas modelise.

Les deux branches que cela decide ont ete **mesurees** plutot que supposees, sur
PCSX-Redux avec le vrai jeu: point d'arret `0x80021EA8` -> `v0 = 0` (la sonde
reussit), point d'arret `0x80021EC0` -> `v0 = 0` (aucune sauvegarde valide). Le
code pose alors `DAT_801ff068 = 2` et efface la table - l'etat dans lequel les
statiques C# demarrent deja.

### 3. Le solveur de camera, `LAB_80027f5c`

Bloque pour une raison differente et plus interessante: **ses entrees n'ont aucun
producteur**. `DAT_800833dc` n'est ecrit qu'une fois dans tout le programme, dans
`FUN_8004c0b4`, non porte; `DAT_80083644` n'est ecrit qu'une fois, dans une region
que Ghidra n'a pas desassemblee.

Un portage fait aujourd'hui ne planterait pas - `PsxRam` rend 0 - il injecterait
du bruit dans les neuf mots de scratchpad que `FUN_80037388` lit a chaque frame,
sans banc pour le detecter ni capture console pour le comparer.

Ses trois callees, qui sont fermees, **sont** portees.

### 4. L'ecran de selection proprement dit

`LAB_8004c010` et ses quatre bras, plus environ 21 000 octets de tâches de menu.
C'est un autre ecran, en aval de tout ce qui precede.

## Les cinq defauts silencieux trouves en route

Tous de la meme famille: le code faisait quelque chose de correct, et rien
n'arrivait, sans message.

| defaut | ce qui ne se voyait pas |
|---|---|
| `ClearOTag` n'existait qu'en forme **inverse** | la table d'affichage n'etait jamais chainee |
| `DrawOTag(int)` sans gestionnaire | ne faisait rien du tout |
| `RamResolveLink` ne connaissait qu'un miroir | **toute** primitive du tas jetee par le rasteriseur |
| `ReadRotMatrix` sans la translation | chaque sprite compose contre une translation nulle |
| `InitHeap` ajoutait une ligne au lieu de remplacer | apres le ré-armement, la ligne **perimee** gagnait |
| `CdIntToPos` / `CdPosToInt` stubs | les douze lectures de portraits sur **le meme** secteur |
| `LoadClut` rendait 0 | la CLUT de 256 entrees sans identifiant |

Le troisieme merite d'etre retenu: `TITLE.EXE` arme son tas a `0x00010000`, **sans
bit de segment** - ce que fait la console (mesure: `0x00017CB4`). Sur PSX, KUSEG
et KSEG0 sont la meme RAM physique et le DMA du GPU lit l'adresse physique. Le
resolveur n'essayait que le miroir `0x80000000`.

Deux d'entre eux etaient dans du travail que j'avais deja livre, dont un que
j'avais **introduit** en corrigeant le premier.

## Ce qui n'est pas observe

**Aucun banc ne pilote `main` au-dela de sa premiere `RunFrameLoop`**: le budget
de frames sans tete vaut 1, donc le premier `VSync` leve. `FUN_80058a9c`,
`FUN_800376c0` et `LoadFACE_B` ne sont donc jamais entres par les bancs.

Leur exactitude repose sur la relecture octet par octet et sur les verificateurs
en contexte neuf, pas sur une execution. C'est le premier endroit ou porter
l'effort: un banc qui fait tourner `main` sur plusieurs frames.

## Methode

Chaque tranche a suivi le meme cycle: reconnaissance en lecture seule sur des
surfaces disjointes, synthese en session principale, portage par ecrivains
sequentiels a possession de fichiers exclusive, puis **verificateurs adversaires
en contexte neuf** qui redecodent les octets eux-memes plutot que de lire les
commentaires du portage.

Ce dernier point a paye. Les verificateurs ont attrape: une affirmation fausse
que j'avais ecrite dans un brief (« le cas 3 est le seul createur de la tache »),
une affirmation fausse que j'avais mise dans une claim de verification
(« Camera.cs translittere `LAB_80027f5c` » - le fichier dit lui-meme qu'il est
partiel), et une supposition qu'un agent precedent avait faite sur `LoadClut` et
qui aurait ete heritee sans cela.

Un agent a par ailleurs **retire de lui-meme** une phrase qu'il avait ecrite
depuis sa connaissance generale du PSX plutot que depuis les octets, et l'a
signale. C'est exactement la discipline demandee.
