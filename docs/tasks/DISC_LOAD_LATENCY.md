# Latence de chargement disque

## Symptome

Un seul appui sur Start passait **les deux** films de demarrage, alors que
l'original en demande un par film.

## Ce qui n'etait pas en cause

Le code translitere. Verifie directement sur la console dans PCSX-Redux:
apres 30 secondes avec Start maintenu depuis le reset, le PC se trouve dans
`TITLE.EXE` (`0x8006E444`) et le tampon pad de cet overlay, `0x800920D4`, vaut
`0xFFFFF7FF`. Or `DBZ_OP.STR` dure 63 secondes: elle a donc bien ete sautee
elle aussi.

**La vraie console passe les deux films si l'on maintient Start.** Les deux
boucles de lecture testent le pad exactement comme le port le fait.

## Ce qui etait en cause

Le temps. Cote port, chaque operation disque etait instantanee:

```
[overlay] t=0ms   LoadExec -> MOVIE.EXE
[overlay] t=66ms  LoadExec -> TITLE.EXE
```

66 millisecondes entre le moment ou un overlay cesse de lire le pad et celui
ou le suivant commence. Un appui humain dure 100 a 300 ms: il couvre les deux.

Sur console, ce meme intervalle contient le chargement de l'overlay, la
recherche du fichier `.STR`, le seek et la mise en tampon du flux.

## Mesure

Le compteur de cycles de PCSX-Redux donne une mesure exacte, independante de
toute latence d'outillage. Points d'arret sur le site d'appel de
`ShutdownAndLoadExecutable` puis sur le `main` de l'overlay charge:

| Overlay | Taille | Cycles | Duree |
|---|---:|---:|---:|
| `MOVIE.EXE` | 133 120 o | 34 496 533 | 1018,5 ms |
| `TITLE.EXE` | 942 080 o | 123 154 369 | 3636,2 ms |

Horloge PSX: 33 868 800 Hz. Les compteurs sont reproductibles au cycle pres
d'une execution a l'autre, la machine etant deterministe.

Mesure complementaire, du `main` de `MOVIE.EXE` au premier test du pad dans
`PlayDbzOpeningMovie`: 20 249 918 cycles, soit **598 ms**. La fenetre reelle
entre les deux films est donc d'environ **1,6 seconde**.

## Modele

Deux mesures, deux inconnues:

```
duree = 587,8 ms + taille / 309 037 o/s
```

Le debit obtenu vaut **309 037 o/s**, soit 301,8 Kio/s. C'est la vitesse reelle
du lecteur en **2x**: `2 x 75 secteurs/s x 2048 octets = 307 200 o/s`. Le modele
retombe donc sur la specification materielle au lieu d'etre ajuste a vue.

Un delai fixe aurait ete faux: `TITLE.EXE` est sept fois plus gros que
`MOVIE.EXE`.

## Implementation

`LibCd.WaitDiscLoad(isoPath)` lit la taille reelle du fichier par
`LibDs.DiscFileSize`, applique le modele et consomme le temps en `VSync`, afin
que l'hote continue de presenter des images pendant le chargement.

Les deux adaptations `LoadExec` l'appellent avant de passer la main a l'overlay
suivant.

Changements de support:

- le resolveur ISO accepte le prefixe `cdrom:` que `LoadExec` emploie, les
  autres sites d'appel ne le mettant pas;
- `MOVIE.EXE` et `TITLE.EXE` sont copies dans la sortie de build, leur taille
  reelle etant la donnee d'entree du modele;
- le chronometre de diagnostic est ancre au demarrage. Il etait
  `beforefieldinit`, donc ne demarrait qu'a son premier usage et affichait
  toujours `t=0` pour la premiere trace.

## Validation

```
[overlay] t=1165ms LoadExec -> MOVIE.EXE
[overlay] t=4865ms LoadExec -> TITLE.EXE
```

L'ecart de 3700 ms vaut les 3636 ms de `TITLE.EXE` plus les 64 ms de
`DBZ_OP.STR`, conformement aux mesures console.

La fenetre entre la fin de `BANDAI.STR` et le premier test du pad de
`DBZ_OP.STR` passe de 66 ms a environ 1085 ms.

Start maintenu saute toujours les deux films, comme sur console: la correction
retablit un temps, elle ne modifie pas le controle de flux translitere.

Bancs `--validate-heap`, `--validate-tasks` et `--validate-title-init`: tous au
vert apres la modification.

## Limite

Les 598 ms qui suivent le chargement, soit la recherche du `.STR`, le seek et
la mise en tampon, ne sont pas modelises: seul le chargement de l'overlay
l'est. Cela suffit au comportement observable vise. Modeliser la latence disque
generale toucherait le chemin FMV deja valide et demanderait de le revalider.
