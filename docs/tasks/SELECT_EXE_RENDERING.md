# SELECT.EXE - le rendu, et trois pannes silencieuses

Ce document couvre ce qui separait l'ecran de mode de SELECT.EXE de la console, une fois le
portage du code termine. Les trois defauts avaient la meme forme: **du code juste, qui ne
produisait rien, sans rien dire**. C'est la forme dominante des pannes de ce portage.

## 1. Un noeud d'OT n'est pas une commande

L'overlay ne dessinait **rien du tout**.

Le rasteriseur traitait chaque noeud de la table d'ordonnancement comme portant exactement une
commande GP0. C'est faux. Un noeud est une **etiquette dont l'octet de longueur compte les mots de
commande qui suivent**, et le DMA les soumet tous. Les paquets de libgs en portent 5 ou 9.

Le fichier se contredisait lui-meme, ce qui rendait la preuve immediate: `SetPolyGT4` estampille
une longueur de 12 pour ses 52 octets. Le rasteriseur, lui, n'en lisait qu'un seul.

`RasterizePrimitivePacket` rend desormais **le nombre de mots consommes**, et
`RasterizeOrderingTableNode` parcourt la longueur de l'etiquette:

```
int len = buf[tagOffset + 3];
int w = 0;
while (w < len) { int c = RasterizePrimitivePacket(buf, tagOffset + w * 4); if (c <= 0) break; w += c; }
```

Les comptes de mots sont derives de chaque commande et recoupes contre les longueurs que le
fichier estampille lui-meme (POLY_GT4 52 o -> 12, TILE 16 o -> 3, SPRT 20 o -> 4). Une commande
inconnue consomme un mot, ce qui fait avancer la boucle au lieu de la bloquer.

## 2. GP0(0x02): l'effacement qui n'existait pas

Une fois l'image visible, le fond s'accumulait: les sept boules de cristal laissaient des ondes
concentriques qui remplissaient la page en quelques centaines de frames.

`GsSortClear @ 0x80047F14` construit un paquet **GP0(0x02) fill-rectangle** et le trie dans la
table a chaque frame - c'est ainsi que cet overlay efface son tampon de dessin. Le rasteriseur
n'avait **aucun bras pour `0x02`**: la commande tombait dans le chemin inconnu, consommait son mot
et ne dessinait rien. Le tampon n'etait jamais efface.

La disposition des champs est prise des ecritures de `GsSortClear` elle-meme, pas d'une
documentation: couleur a +4, origine a +8, taille a +0x0c, longueur d'etiquette 3.

**C'est une ecriture VRAM brute, pas une primitive**, et elle ne passe donc deliberement pas par
`PlotPixel`: sur le materiel, le remplissage ignore la zone de dessin, le decalage de dessin, la
semi-transparence, le tramage et le bit de masque. La faire passer par le chemin pixel decouperait
l'effacement a la zone de dessin et le rendrait inoperant des que l'origine du tampon et la zone
divergent. L'arrondi a 16 pixels que le materiel applique aussi est declare `PARTIAL:` - rien ici
ne l'exerce, les tampons faisant 320 de large a x = 0.

Mesure sur le menu a 600 frames: **76800 pixels allumes** - soit la page entiere, donc de
l'accumulation pure - ramenes a **41654** et une image stable.

## 3. Le chemin matriciel: verifie, et hors de cause

`GsSortSprite @ 0x8004820C` atteint la meme image par deux routes:

| route | condition | sortie |
|---|---|---|
| rapide | sans rotation, echelle unite, ni bit 22 ni bit 23 | un `SPRT` |
| matricielle | sinon | `RotMatrix` ou l'identite de `DAT_800653D8`, puis `ScaleMatrix`, `TransMatrix`, `RotTransPers4` -> un `POLY_FT4` |

Rien a l'ecran ne distingue les deux. Un chemin matriciel qui effondrerait ses quatre coins sur un
point - ce qui arrive si `DAT_800653D8` reste a zero, ou si le decalage GTE diverge de
`DAT_80065394` - ne dessinerait rien et se lirait comme un sprite absent, pas comme une projection
fausse.

`SortSpriteValidation` (`--validate-sortsprite`) les force donc a se rencontrer la ou elles doivent
coincider. A l'echelle unite, le **bit 22 de l'attribut** coute au chemin rapide sa condition
`(attribute & 0xc00000) == 0` et n'achete rien d'autre qu'un echange des deux coordonnees V, que la
geometrie ne voit jamais. Les quatre coins projetes doivent alors reproduire exactement le
rectangle du chemin rapide.

Ils le font, sur les quatre cas, dont les deux que le menu utilise reellement:

```
matrice DAT_800653D8: 4096 0 0 0 4096 0 0 0 4096
decalage 2D: DAT_80065394=160 DAT_80065398=120
  pivot coin:            SPRT (160,192) 160x40 == quad (160,192) (320,192) (160,232) (320,232)
  coordonnees negatives: SPRT (76,12)   176x40 == quad (76,12)   (252,12)  (76,52)   (252,52)
```

L'accord n'est pas un hasard: `GsSetDrawBuffOffset` appelle `SetGeomOffset` avec **exactement** la
valeur qu'il publie dans `DAT_80065394/98`, si bien que les deux routes partagent le meme decalage
par construction.

**Le chemin matriciel n'etait donc pas en cause.** Il n'est d'ailleurs emprunte que par un seul
sprite affiche: le logo a l'echelle `0x0FEE`. Les dix autres sprites a echelle non unitaire portent
le bit 31, celui qui les desaffiche.

## Ce que les boules faisaient vraiment

Le diagnostic ne listait que les sprites a echelle non unitaire, ce qui cachait l'essentiel. Il
liste desormais **tous les sprites reellement affiches**, et la lecture change du tout au tout:

- `[14]` 176x40 en (76,12): le bandeau; `[15..17]` 144x24 en (24,72/104/136): les trois entrees;
  `[1D]` 125x96 en (200,72): le portrait; `[28]` 59x240 en (40,0): la bande verticale. **Tous au
  chemin rapide, tous exactement la ou l'image de la console les montre.**
- Sept sprites `71x70` a `mx=35,my=35` (u=0, v=120, page 0x1D): **les sept boules**, chacune
  flanquee de petits `9x8` a v=123/139/155/171 - les etoiles.

Leur rayon se lit d'un coup: **~150 a n=200, ~40 a n=400, ~190 a n=800**. Les boules convergent
puis se dispersent hors ecran. Elles n'ont jamais ete figees; c'est le tampon qui n'etait jamais
efface.

## La lecon, repetee

Neuf defauts de ce portage ont eu cette forme: correct, muet, invisible aux bancs.

| defaut | symptome invisible |
|---|---|
| `ClearOTag` n'existait qu'a l'envers | la table n'etait jamais chainee |
| `RamResolveLink` ne connaissait qu'un miroir | toute primitive du tas etait perdue |
| `ReadRotMatrix` sans translation | composition contre une translation nulle |
| `CdIntToPos` / `CdPosToInt` en talon | douze lectures de portrait sur un seul secteur |
| le rasteriseur ignorait le bit STP | toute l'image a demi-luminosite |
| **une commande par noeud d'OT** | **libgs ne dessinait rien** |
| **pas de bras GP0(0x02)** | **la frame n'etait jamais effacee** |

Aucun de ces defauts n'a ete trouve en lisant du code. Tous l'ont ete en **regardant l'ecran** ou
en instrumentant. Les bancs restaient verts d'un bout a l'autre - c'est pourquoi
`--validate-sortsprite` existe: il transforme une equivalence qu'on ne peut pas voir en une
egalite qui echoue bruyamment.
