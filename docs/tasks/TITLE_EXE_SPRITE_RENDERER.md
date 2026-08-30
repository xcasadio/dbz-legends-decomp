# TITLE.EXE - le rendu de sprites, et l'ecran titre qui dessine enfin

## Objectif

Porter `FUN_80048f88 @ 0x80048F88`, la fonction qui dessine reellement le logo,
le fond et PRESS START. La tache titre l'appelle cinq fois par frame.

Trois defauts ont ete trouves en chemin, dont deux dans du travail deja livre.

## Ce qui bloquait, et qui n'avait pas ete vu

Le commentaire `BLOCKED:` pose lors du portage de `FUN_80021e28` affirmait:

> Every callee is a libgte routine the SDK already carries, so it is portable.

**C'etait faux.** `RotAverage4 @ 0x8006D5D8` n'existait pas dans
`PsxSdkMonogame/LibGte.cs`. Un `rg RotAverage` sur tout le SDK ne rendait rien.
Onze des douze appels sortants etaient bien la; le douzieme, celui qui projette
les quatre sommets et produit le Z de la table d'affichage, manquait.

C'est le seul verrou reel qu'il y avait.

## Trois defauts, tous du meme genre: silencieux

### 1. `RotAverage4` absent

Corps ferme instruction par instruction depuis l'image:

| adresse | operation |
|---|---|
| `0x8006D5D8` | `ldv3 a0,a1,a2` |
| `0x8006D5F4` | `RTPT` - projette les trois premiers |
| `0x8006D604` | `stsxy3` vers les trois destinations |
| `0x8006D610` | `cfc2 v1,$31` - premier FLAG |
| `0x8006D614` | `ldv0 a3` |
| `0x8006D620` | `RTPS` - projette le quatrieme |
| `0x8006D630` | `stsxy` vers la quatrieme destination |
| `0x8006D634` | `cfc2 t0,$31` - second FLAG |
| `0x8006D638` | `stdp` vers `*p` |
| `0x8006D63C` | `or t0,t0,v1` puis `sw` - `*flag = f1 OU f0` |
| `0x8006D644` | `AVSZ4` |
| `0x8006D648` | `mfc2 v0,$7` - rend OTZ |

`ZSF4 = 0x100` est ferme par `InitGeom @ 0x8006E1F0`, qui le charge a
`0x8006E230` dans le meme bloc de sept registres que `ZSF3 = 0x155`.
`0x100 * 4 / 0x1000 = 0.25` exactement: AVSZ4 rend la moyenne des SZ divisee
par quatre, ce qui rejoint `Avsz3` et la convention « OTZ = SZ >> 2 » que le
fichier utilise deja.

**Un piege reel:** `Rtpt` et `Rtps` du portage ecrivent des cases SZ **fixes**
au lieu de modeliser le registre a decalage de profondeur 4 - une simplification
que leurs propres commentaires assument. Un `Rtpt` + `Rtps` + `Avsz4` naif
sommerait donc un SZ0 perime et **perdrait une projection entiere**.
`RotAverage4` reconcilie les quatre cases a l'etat materiel reel avant `Avsz4`,
plutot que de modifier deux pseudo-ops partagees.

*Suivi propose (decision utilisateur):* faire decaler `Rtpt`/`Rtps` pour de bon
serait plus fidele au materiel et est aujourd'hui sans risque - aucun appelant
hors `LibGte.cs`. Mais cela renverse une decision deliberee et documentee.

### 2. `ReadRotMatrix` ne copiait que la 3x3

Le SDK avait:

```csharp
public static void ReadRotMatrix(MATRIX m) => Array.Copy(gteR, m.m, 9);
```

Sans annotation `GHIDRA:`. Or `ReadRotMatrix @ 0x8006D3B4` ecrit **aussi** la
translation:

    0x8006D3DC  gte_stTRX t0
    0x8006D3E0  gte_stTRY t1
    0x8006D3E4  gte_stTRZ t2
    0x8006D3E8  sw t0,0x14(a0)     m->t[0]
    0x8006D3EC  sw t1,0x18(a0)     m->t[1]
    0x8006D3F0  sw t2,0x1c(a0)     m->t[2]

L'effet est concret: `FUN_80048f88` capture la matrice vivante avec
`ReadRotMatrix`, puis la passe a `CompMatrix` comme `m0` - et `CompMatrix` lit
`m0.t`. Sans la translation, **chaque sprite du titre serait compose contre une
translation nulle**.

Un `grep` sur tout le depot a montre zero appelant de `ReadRotMatrix` et de
`ReadTransMatrix`: le corriger ne pouvait rien regresser.

### 3. `RamResolveLink` ne connaissait qu'un seul miroir

Le plus grave, et le plus silencieux. Le rasteriseur parcourt la table
d'affichage et resout chaque lien 24 bits par `RamResolveLink`, qui faisait:

```csharp
return RamResolve((int)(0x80000000u | (link & 0x00ffffff)), out buffer, out offset);
```

Mais `TITLE.EXE` arme son tas a **`0x00010000`** - une adresse KUSEG sans bit de
segment. Ce n'est pas une approximation du portage: c'est ce que fait la console.
Un point d'arret au `AddPrim` de la tache titre avait lu le pointeur de
primitive comme `0x00017CB4`, sans bit de segment.

Sur PSX, `0x00010498` et `0x80010498` sont **la meme RAM physique** - KUSEG et
KSEG0 sont des miroirs, et le DMA du GPU lit l'adresse physique. En n'essayant
que le miroir `0x80000000`, le parcours jetait **toute primitive vivant sur le
tas**. Pour cet overlay, c'etait la totalite: les deux bandes de fond sont dans
le contexte de tache alloue par `malloc`, et les sprites viennent d'un pool
`malloc` aussi.

La table etait correctement chainee, les primitives correctement construites, et
pas un seul pixel n'atteignait l'ecran. Sans le moindre message.

## `FUN_80048f88`: ce que dit la fonction

### Les dix-huit arguments

Chacun ferme contre une instruction decodee, pas deduit. Cinq etaient mal nommes
dans le stub precedent.

| # | type | role | le stub disait |
|---:|---|---|---|
| 1 | `int` | adresse PSX du groupe de sprites | - |
| 2 | `short` | X modele de l'origine du groupe | - |
| 3 | `short` | Y modele | - |
| 4 | `short` | **Z modele** | `scaleX` **faux** |
| 5 | `ushort` | mot compacte drapeaux/angle (voir ci-dessous) | - |
| 6 | `short` | angle de rotation Y | - |
| 7 | `short` | angle de rotation Z | - |
| 8 | `int` | **biais d'echelle X** | `scaleY` **faux** |
| 9 | `int` | **biais d'echelle Y** | `scaleZ` **faux** |
| 10 | `int` | **biais de case** de table d'affichage | `p10` |
| 11 | `short` | biais de CLUT | - |
| 12 | `short` | biais de tpage | - |
| 13 | `char` | biais U | - |
| 14 | `char` | biais V | - |
| 15..17 | `u8` | r0, g0, b0 de chaque `POLY_FT4` | - |
| 18 | `int` | **borne inferieure exclusive** de la case | `z` **faux** |

L'ordre positionnel etait juste, donc aucun site d'appel n'a bouge.

Le mot compacte (argument 5), tenu dans `s7` tout le long:

| bits | role | preuve |
|---|---|---|
| 0..11 | angle de rotation X | `andi v0,s7,0x0fff` @ `0x80049370` |
| 12 | **jamais teste** | aucun `andi ...,0x1000` dans toute la fonction |
| 13 (`0x2000`) | position deja en espace vue: saute `RotTrans` | `0x80049044`, `0x800490A8` |
| 14 (`0x4000`) | miroir en X: `vx = 0x800` soit 180 degres | `0x8004926C`, `0x80049274` |
| 15 (`0x8000`) | miroir en Y: `vy = 0x800` | `0x80049290`, `0x80049298` |

### Le flux d'enregistrements

En-tete de groupe: un `int32` de comptage a `+0x00`, les enregistrements
commencent a `+0x04`.

Tete fixe de 8 octets:

| offset | champ | devient |
|---|---|---|
| `+0x00` | `u` | `prim.u0 = biaisU + u` |
| `+0x01` | `v` | `prim.v0 = biaisV + v` |
| `+0x02` | `localX` | `modelX = octet - 128` |
| `+0x03` | `localY` | `modelY = octet - 128` |
| `+0x04` | `clut` (u16) | `prim.clut = biaisClut + clut` |
| `+0x06` | `packed` (u16) | bits 0..8 tpage, bits 12..15 code de taille |

Bloc de taille **conditionnel**, 4 octets, present seulement quand le code de
taille vaut 0:

| offset | champ |
|---|---|
| `+0x08` | `width` (u16) |
| `+0x0A` | `height` (u16) |

Quand le code de taille est non nul, il sert **directement** de largeur ET de
hauteur - un carre dont le cote est un multiple de 8 entre 0 et 120.

Queue fixe de 8 octets: `rotZ` a `+0x00`, **`+0x02` jamais lu** (prouve
negativement: le curseur va `0 -> +4 -> +6 -> +8` sans acces intermediaire),
`scaleX` signe a `+0x04`, `scaleY` signe a `+0x06`.

**Pas de 16 octets, ou 20 avec le bloc explicite.**

Corroboration independante: les offsets de groupes que
`TITLE_B_FILE_FORMAT_ANALYSIS.md` avait releves tombent tous exactement sur la
forme a 20 octets.

    0xCC - 0xB4   = 0x18 = 4 + 1 * 20
    0xE4 - 0xCC   = 0x18 = 4 + 1 * 20
    0x138 - 0xE4  = 0x54 = 4 + 4 * 20
    0x18C - 0x138 = 0x54 = 4 + 4 * 20
    0x1A4 - 0x18C = 0x18 = 4 + 1 * 20

Cinq confirmations arithmetiques, a partir de la seule table d'offsets.

### La case de la table d'affichage

    otz   = RotAverage4(...)
    base  = 0x800 - otz
    index = base + biaisCase
    si (borneInf < index ET index < 0x800)  AddPrim(DAT_800834e0 + 0x70 + index * 4, prim)
    rend base                                  -- sans le biais

Les **deux** comparaisons sont signees (`slt` @ `0x80049464`, `slti` @
`0x8004946C`). L'axe de profondeur est inverse par rapport a OTZ: un quad plus
proche recoit un index plus petit.

## Trois choses gardees telles quelles

1. **Trois cas de retour distincts**, non interchangeables: `0` si le pool est
   epuise, `-1` si le compte est nul ou si le dernier enregistrement echoue au
   test de plage, sinon `0x800 - OTZ` du dernier ajoute.

2. **L'arithmetique U/V deborde sur 8 bits.** Les quatre ecritures sont des `sb`,
   donc un sprite large de 256 donne `u1 = u0 + 255 mod 256 = u0 - 1`.

3. **Une fuite de pile de matrices, reproduite (regle 12).** La sortie anticipee
   « pool epuise » saute a `0x800494D0`, c'est-a-dire **au-dela** du `PopMatrix`
   exterieur, laissant la pile GTE d'un cran trop profonde. Le portage la
   reproduit et le dit en commentaire. Il ne la repare pas.

## Verification

Deux verificateurs en contexte neuf, adversariaux, ont rendu **CONFIRMED** en
redecodant eux-memes les mots COP2 depuis l'image plutot qu'en lisant les
commentaires du portage.

Le banc `--validate-title-task` a du etre corrige: son assertion « la case 0
pointe sur `p[1]` » datait de l'epoque ou le renderer etait un stub qui
n'ajoutait rien. Il parcourt maintenant la chaine.

**Le compte se recoupe de facon independante.** A l'etat 1, quatre groupes sont
vivants (le cinquieme n'est appele qu'a l'etat 3) et portent respectivement 4, 4,
1 et 1 sprites, soit 10, plus les 2 bandes de fond:

| mesure | valeur |
|---|---|
| primitives dans la chaine de la case 0 | **12** |
| position de `p[1]` | 2 |
| position de `p[0]` | 3 |
| CLUT de `GetClut(0x180, 0xfe)` | `0x3F98` - la valeur console exacte |
| **cellules de VRAM ecrites par la soumission** | **74108** |

Les 74108 cellules sont la preuve de bout en bout: la chaine traverse jusqu'au
framebuffer. C'est precisement ce qui echouait avant le correctif de
`RamResolveLink`, et qui echouait sans un mot.

## Non regression

Les sept bancs passent: `heap`, `tasks`, `title-init`, `title-images`, `render`,
`title-task`, `pad-input`.

## Ce qui reste ouvert

- **La FIFO XY** n'est pas reconciliee alors que la FIFO Z l'est. Non observable
  aujourd'hui - les trois ecritures SXY precedent le `RTPS` - mais a surveiller
  si un futur appelant lit SXY0/SXY1 apres un `RotAverage4`.
- **L'echelle effective des sprites.** Les cinq sites d'appel passent
  `0x1000` comme biais, et la doc de `TITLE.B` releve `0x1000` dans chaque
  enregistrement, ce qui donnerait un facteur 2.0. Les octets de `TITLE.B`
  n'ont pas pu etre relus (le fichier n'est pas versionne).
- **Les quinze autres appelants** de `FUN_80048f88` (139 sites au total) n'ont
  pas ete verifies. Certains passent des combinaisons que l'ecran titre
  n'utilise jamais: rotations non nulles, biais de case non nuls, le mode
  `0x2000`.
