# TITLE.EXE - la tache de l'ecran titre

## Objectif

Porter `FUN_80021e28 @ 0x80021E28`, la tache que `FUN_80021dd0` inscrit en
liste 6 et qui tient l'ecran titre, puis basculer en memoire les primitives que
le rasteriseur doit pouvoir atteindre par adresse.

## Le point d'architecture

Le choix precedent etait « objets C# quand c'est possible ». Ici ce n'est pas
possible, et la preuve est dans le code lui-meme.

Le contexte de la tache fait `0x70` octets et la fonction le lit comme trois
creneaux `POLY_FT4`:

| Creneau | Taille | Role |
|---|---:|---|
| `p[0]` | `0x28` | la bande de fond haute, y `0` a `0x58` |
| `p[1]` | `0x28` | la bande de fond basse, y `0xbc` a `0xf0` |
| `p[2]` | `0x20` | **pas une primitive**: la memoire de travail de la tache |

`0x28 + 0x28 + 0x20 = 0x70` exactement: le troisieme creneau est volontairement
tronque. Il n'existe aucun troisieme quad.

`p[2]` est adresse **a travers les noms de champs de `POLY_FT4`**, avec des
lectures 16 bits qui traversent des frontieres de champs:

| Ce que lit l'original | Ce que ca porte |
|---|---|
| `*(short *)&p[2].tag` | l'index de la machine a etats, `0` a `5` |
| `*(short *)((int)&p[2].tag + 2)` | un compteur de frames |
| `uVar1._0_1_ = p[2].r0; uVar1._1_1_ = p[2].g0;` | un offset 16 bits, lu en paire |
| `*(short *)&p[2].b0` | l'offset de fond, soit `b0` et `code` ensemble |
| `p[2].x0` | le glissement horizontal des deux bandes |
| `p[2].u1` et `p[2]._3` lus ensemble | un drapeau de clignotement |
| `p[2].u2` | le niveau de fondu, `0` a `0x80` |
| `p[2].tpage` | `0xffff` des que Start est presse |

Un objet C# avec des champs nommes ne peut pas exprimer une lecture 16 bits qui
enjambe `r0` et `g0`. Seuls des octets le peuvent.

Deuxieme preuve, independante: `FUN_80037388 @ 0x80037388` soumet le quad de
fondu par `AddPrim(DAT_800834e0 + 0x206c, &POLY_GT4_800b9518)`. Le bucket recoit
l'**adresse PSX** du paquet; une primitive sans adresse ne peut pas etre pointee.

**Decision:** toute primitive qui atteint `AddPrim` vit en memoire.

## Ce qui a ete ajoute au SDK

`PsxSdkMonogame/PrimitiveRef.cs`, nouveau fichier: `POLY_FT4Ref` et
`POLY_GT4Ref`, deux `readonly struct` sur `(byte[], offset)` qui exposent les
noms de champs psyq. Un site d'appel continue d'ecrire `p.tpage = 0x46;` pendant
que le stockage dessous est le paquet que le GPU voit. Ils portent aussi
`ReadHalf` / `WriteHalf` pour les lectures qui traversent les champs, et un
indexeur pour l'arithmetique de pointeur `p + 1` / `p[2]`.

Les decalages sont recoupes contre le rasteriseur du SDK lui-meme, qui lit les
sommets `POLY_GT4` a `+8/+20/+32/+44` et les couleurs a `+4/+16/+28/+40`.

Dans `LibGpu.cs`:

| Ajout | Role |
|---|---|
| `SetShadeTex(byte[], int, int)` | forme tampon, translitteree de `SetShadeTex @ 0x800711B8` |
| `SetPolyFT4/SetPolyGT4/SetSemiTrans/SetShadeTex(ref, ...)` | surcharges sur les deux `Ref` |
| `AddPrim(int otAddress, int primAddress)` | forme adresse, celle que le jeu appelle vraiment |
| `AddPrim(int, POLY_FT4Ref)` / `AddPrim(int, POLY_GT4Ref)` | bucket en adresse, paquet nomme |

Les surcharges `Ref` ne sont pas cosmetiques: sans elles, un `readonly struct`
se lie aux surcharges `object` de `SetSemiTrans` et `SetShadeTex`, qui sont les
stubs vides du SDK. Chaque appel n'aurait rien tague, en silence.

`SetShadeTex @ 0x800711B8` est une vraie fonction dans ce build, pas la macro
psyq. Son corps, lu dans l'image:

    beq  a1,zero,+4 / lbu v0,7(a0) / j end / ori v0,v0,1    -- tge != 0
    lbu  v0,7(a0)   / andi v0,v0,0xfe                       -- tge == 0
    end: jr ra      / sb v0,7(a0)

`SetSemiTrans @ 0x80071190` a exactement la meme forme avec `ori 2` / `andi
0xfd`, ce qui confirme la forme tampon deja presente.

## Le tas devait declarer son adresse

`PsxHeap.InitHeap` armait son espace sans le declarer au registre d'adresses de
`LibGpu`. Consequence: une primitive rendue par `malloc` n'avait aucune adresse
que `AddPrim` puisse ecrire dans un bucket, et elle ne se dessinait jamais, en
silence. `TITLE.EXE` tombe exactement dans ce cas, puisque les deux bandes de
fond vivent dans le contexte de tache alloue sur le tas.

`InitHeap` appelle desormais `LibGpu.RamRegion(baseAddress, s_storage)`.

## Ce qui a change dans TITLE.EXE

| Global | Avant | Apres |
|---|---|---|
| `POLY_GT4_800b9518` | objet `POLY_GT4` | `POLY_GT4Ref` sur `RamRegion(0x800B9518, 52)` |
| `POLY_FT4_ARRAY_800a8894` | `POLY_FT4[5]` | `POLY_FT4Ref` sur `RamRegion(0x800A8894, 5 * 40)` |
| `SHORT_ARRAY_801ff000` | `short[0x124]` | fenetre sur `RamRegion(0x801FF000, 0x248)` |

`SharedHighRam` devient de la memoire brute parce que `FUN_80021e28` la lit a
trois largeurs: des octets isoles a `0x58..0x5d`, un mot a `0x68`, et la table
de sauvegardes a `0x200` en `int` comme en `short`. L'etendue `0x248` est
exactement ce que le code touche: `LAB_80021F98` efface
`INT_ARRAY_801ff200[0]` a `[0x11]`, soit `0x200 + 18 * 4`.

Le `BLOCKED` de `FUN_80037388` est ferme: `AddPrim(DAT_800834e0 + 0x206c, ...)`
atteint le bucket `0x7ff`, puisque `DAT_800834e0 + 0x70` est la premiere case et
`(0x206c - 0x70) / 4 = 0x7ff`. Chainage avant: cette case se dessine en dernier,
donc le fondu passe par-dessus tout.

## Les branches carte memoire, mesurees et non devinees

`case 0` commence par la carte memoire, que le SDK ne modelise pas:
`FUN_80022630` (bring-up libcard), `FUN_80022780` (sonde), `FUN_80023374`
(balayage des sauvegardes), `FUN_80022680` (teardown). Les quatre sont `BLOCKED`.

Mais leurs retours **choisissent une branche**, donc ils ont ete mesures sur le
vrai jeu dans PCSX-Redux plutot que supposes:

| Point d'arret | Mesure | Consequence |
|---|---|---|
| `0x80021EA8` | `v0 = 0` | la sonde reussit, on entre dans la branche `FUN_80023374` |
| `0x80021EC0` | `v0 = 0` | aucune sauvegarde valide |

Le code pose alors `DAT_801ff068 = 2` et efface la table, ce qui est deja l'etat
dans lequel demarrent les statiques C#. Configuration mesuree: carte memoire par
defaut de l'emulateur, sans sauvegarde DBZ.

## Validation contre la console

PCSX-Redux a ete arrete au premier `AddPrim` du contexte, `0x80022524`, et les
112 octets a `0x80017CB4` ont ete lus. Ce sont les attendus du banc.

Trois mesures de cette capture valident l'infrastructure de rendu:

| Mesure | Valeur | Ce que ca prouve |
|---|---|---|
| `$a0` au premier `AddPrim` | `0x800A6830` | l'adresse exacte de la table d'affichage du portage |
| `$s0` avant l'appel | `0x70` | `(z << 2) + 0x70` avec `z = 0`, soit la case 0 |
| `lhu $v1, 0x0058(s3)` | — | `p[2].x0` est bien a `+0x50 + 8`, la disposition deduite |

Un ecart apparent s'est revele en etre un faux: `p[1].x3` valait `0` dans le
dump alors que le code y stocke `0x280`. Le `sh $v0, 0x0020(s4)` est dans le
**delay slot du `jal`** a `0x80022528`, donc pas encore execute quand le point
d'arret a declenche. Le decompilateur avait raison.

`--validate-title-task` verifie:

| Verification | Resultat |
|---|---|
| l'etat passe a 1 apres l'initialisation | conforme |
| `r0/g0` et `b0/code` valent `0x0280`, `x0` vaut `0x140` | conforme, comme `80 02 80 02 40 01` sur console |
| les deux bandes: longueur 9, code `0x2e`, tpage `0x46`, gris `0x60`, u a 0, v a `0xff` | conforme |
| la CLUT rendue par `GetClut(0x180, 0xfe)` | **`0x3F98`**, la valeur console exacte |
| geometrie des deux bandes | conforme aux 112 octets |
| chainage: case 0 vers `p[1]` vers `p[0]`, longueur 9 preservee des deux cotes | conforme |
| etat 1: le fondu monte de 8 par frame | **16 frames** pour atteindre `0x80` |
| etat 2: `x0` recule de `0x50` par frame et `p[0].x0` suit `-x0` | conforme |

L'adresse du contexte differe de la console (`0x017CD8` contre `0x00017CB4`):
`PsxHeap` modelise le contrat observable de `malloc`, pas sa disposition exacte.
Seuls les 24 bits bas comptent pour un lien de table, donc c'est sans effet.

## Non regression

| Banc | Resultat |
|---|---|
| `--validate-heap` | passe |
| `--validate-tasks` | passe |
| `--validate-title-init` | passe |
| `--validate-title-images` | passe |
| `--validate-render` | passe |
| `--validate-title-task` | passe |
| `--validate-pad-input` | passe (avec `DBZ_PAD_FORCE=0x0800`) |

## Ce qui reste

`FUN_80048f88 @ 0x80048F88`, 1404 octets, appele cinq fois par la tache: c'est
lui qui dessine les groupes de sprites du titre (le logo, le fond, PRESS START).
Il est `BLOCKED` et rend `0`.

Il est entierement portable: ses douze appels sortants sont tous des routines
libgte que le SDK porte deja (`PushMatrix`, `RotMatrix`, `ScaleMatrix`,
`CompMatrix`, `TransMatrix`, `RotAverage4`, `ReadRotMatrix`, `SetRotMatrix`,
`SetTransMatrix`, `RotTrans`, `PopMatrix`) plus un `AddPrim`.

Son retour est le Z que l'appelant transforme en case de la table
(`iVar8 * 4 + 0x70`). Rendre `0` place les deux bandes en case 0, ce qui est ce
que la console faisait sur la frame mesuree.

Reste aussi `FUN_80038684 @ 0x80038684`, 932 octets, la tache qui anime le
fondu, et le portage de la carte memoire si un jour les sauvegardes comptent.
