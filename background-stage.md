Contexte pour l'agent
Ce qu'on sait :

La texture fond est l'unique entrée 8bpp de chaque STGxTX.B : 128×256 px à vramX=960 (mots 16bpp), vramY=0
La moitié haute (rows 0–127) est rendue par DrawBackgroundBillboards à 0x800410cc, qui utilise le template sprite à 0x80087d94 (tpage=0x2E, v0=0, h=40) sur 80 instances positionnées via bgBillboardInstances à 0x80087dac
La moitié basse (rows 128–255) est rendue par un code inconnu — c'est l'objectif de cette recherche
RenderTransformedSprites est la fonction de rendu 3D billboard (RotTrans, RotAverage4, AddPrim)
L'image 8bpp à vramX=960 = tpageX=15 en mode 8bpp (960/64=15), ou tpageX=7 en mode 8bpp alternatif (960/128=7.5 → 7)
Outils : Ghidra (Reva MCP) + PCSX-Redux MCP (émulateur avec lecture RAM, breakpoints)

Liste de tâches
1. Trouver les callers de RenderTransformedSprites
Ghidra : find-cross-references sur RenderTransformedSprites (ou son adresse)
Lister TOUTES les fonctions qui appellent RenderTransformedSprites
DrawBackgroundBillboards (0x800410cc) en fait partie — les AUTRES callers sont les suspects
Pour chaque caller, noter l'adresse et décompiler la signature
2. Analyser chaque caller de RenderTransformedSprites
Pour chaque caller trouvé à l'étape 1 (hors DrawBackgroundBillboards) :
Décompiler la fonction (get-decompilation)
Chercher quel tableau de sprite template est passé en paramètre (adresse du pointeur vers les données sprite)
Chercher si un tableau d'instances (positions X/Y/Z) est utilisé
Noter le tpage, v0, hauteur du sprite template référencé
3. Chercher des sprite templates proches en mémoire
PCSX-Redux : Lire la mémoire autour de 0x80087d94 ± 512 octets pour trouver d'autres structures sprite avec :
tpage=0x2E (même page VRAM) mais v0 ≥ 128
Ou un tpage différent qui pointe vers la même zone VRAM (vramX=960, vramY=128+)
Structure attendue : count(4 bytes) | u0 v0 centerX centerY | clutHi clutLo tpage_lo tpage_hi | width height rotZ pad | scaleX scaleY
Signature à chercher : un octet v0 ≥ 0x80 (128) dans le 2ème octet de chaque bloc de 12 bytes après le count
4. Chercher les références au tpage 0x2E dans le code
Ghidra : search-decompilation pour 0x2e ou 0x2E dans les fonctions proches du module stage (0x80040000–0x80090000)
Aussi chercher 0x4e (=0x2E avec semi-transparency bit différent) et 0x6e, 0x8e (variations semi-trans)
Chaque hit qui n'est PAS DrawBackgroundBillboards est un suspect
5. Chercher les constantes v0=128 dans la zone de données du stage
PCSX-Redux : search-memory pour le pattern hex 80 (=128) aux offset attendus pour v0 dans les structures sprite templates autour de 0x80087000–0x80088000
Affiner : chercher le pattern 2 octets 00 80 (u0=0, v0=128) ou 08 80 (u0=8, v0=128) qui correspondraient aux mêmes UV décalés de 128
6. Analyser la fonction appelante de DrawBackgroundBillboards
Ghidra : find-cross-references sur DrawBackgroundBillboards (0x800410cc) pour trouver qui l'appelle
Décompiler cette fonction parent — elle appelle probablement une AUTRE fonction de rendu fond juste avant ou après DrawBackgroundBillboards
Cette fonction sœur est le candidat principal pour le rendu de la 2ème moitié
7. Vérifier le VRAM en temps réel
PCSX-Redux : Faire un screenshot VRAM (pcsx_get_vram) pendant le gameplay sur un stage
Vérifier visuellement que vramX=960, vramY=0..255 contient bien 2 images distinctes empilées
Identifier visuellement ce que représente chaque moitié (nuages ? montagnes ? ciel ?)
8. Synthèse
Retourner :
L'adresse de la fonction qui rend la 2ème moitié
L'adresse du sprite template utilisé (u0, v0, w, h, tpage, clut)
L'adresse du tableau d'instances (positions) si différent des 80 de bgBillboardInstances
Le nombre d'instances et leur pattern spatial
La décompilation de la fonction

---

## ✅ RÉSULTATS (résolus)

### Constatation principale : les hypothèses initiales étaient erronées

Le contexte original supposait que `DrawBackgroundBillboards` utilise la texture 8bpp de fond (entry[7], VRAM 960,0, tpage=0x8F). C'est **faux**.

**Analyse correcte du tpage=0x2E :**
- bits[3:0]=14 → tpX=14 → base VRAM = 14×64 = 896 (mots 16bpp)
- bit[4]=0     → tpY=0  → base VRAM Y = 0
- bits[8:7]=00 → mode 4bpp
- → Référence **STGxTX.B entry[11]** : texture 4bpp, 104×32 px à VRAM(896, 0) (petits sprites d'objets)

**Texture 8bpp entry[7] (vramX=960, H=256) → tpage=0x8F :** rendue par un système TOTALEMENT DISTINCT.

---

### Vrai système de fond (ciel) : FUN_80041c6c / FUN_80041ee4

| Paramètre | Valeur |
|-----------|--------|
| Fonction init | **`InitSkyBackgroundQuads`** (0x80041c6c), appelée 1×/stage depuis `SkyBackgroundTask` (0x80041be0) |
| Fonction update | **`UpdateSkyBackgroundQuads`** (0x80041ee4), appelée chaque frame |
| Task créé par | `FUN_80041640` (stage init), priorité 0x100, type 1 |
| Primitives GPU | 8 × POLY_FT4 dans le display list (`AddPrim`) |
| tpage | **0x8F** (8bpp, tpX=15, VRAM 960,0) |
| clut | **0x7900** → palette VRAM(0, 484), 256 couleurs = entry[6] |

**Logique UV dans InitSkyBackgroundQuads :**
```c
// Loop i = 0..7 :
uVar3 = (char)i * (-0x80);   // = 0, 128, 0, 128, 0, 128, 0, 128 (mod 256)
v0 = v1 = uVar3;             // top edge de chaque quad
v2 = v3 = uVar3 + 0x7f;     // bottom edge = v0 + 127
// Résultat :
// i pair  (0,2,4,6) → v=0..127   = moitié HAUTE de la texture 128×256
// i impair(1,3,5,7) → v=128..255 = moitié BASSE de la texture 128×256
```

**Conclusion :** Les 8 quads forment un panneau de ciel couvrant l'écran, où la moitié haute (rows 0-127) et la moitié basse (rows 128-255) alternent de gauche à droite. `UpdateSkyBackgroundQuads` fait défiler les panneaux avec l'angle caméra via `RotAverage4`.

---

### Système de billboards (objets de scène) : DrawBackgroundBillboards

| Paramètre | Valeur |
|-----------|--------|
| Fonction | `DrawBackgroundBillboards` (0x800410cc) |
| Texture | **entry[11]** : 4bpp, 104×32 px à VRAM(896,0), tpage=0x2E |
| Palette | entry[10] : 16 couleurs @ VRAM(0, 508) → clut=0x7F00 |
| Template default | u0=8, v0=0, w=96, h=32 (stades 1-26 sauf 26) |
| Template stage26 | u0=0, v0=0, w=88, h=40 |
| 80 instances | `bgBillboardInstances` (0x80087dac) → arbres/rochers/objets décoratifs |

---

### Implémentation C# (STG_MD_View.cs) — commit post-résolution

- `BuildBillboards()` → utilise entry[11] (4bpp, tpX=14), UV u=8..103, v=0..31, taille 480×160 PSX units
- `BuildSkyBackground()` → 8 panneaux en anneau (rayon 7000), alternance moitié haute/basse de entry[7]
- Labels Ghidra : `InitSkyBackgroundQuads`, `UpdateSkyBackgroundQuads`, `SkyBackgroundTask`
Priorité recommandée
Tâche 6 → 1 → 2 est le chemin le plus rapide : remonter depuis le caller de DrawBackgroundBillboards pour trouver la fonction sœur qui dessine la 2ème moitié. Les tâches 3–5 sont des alternatives si le chemin principal ne donne rien.