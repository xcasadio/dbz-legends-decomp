# UV & Texture Analysis Tasks — STGxMD.B Stage Meshes

> **Objectif** : Les textures affichées sur les modèles 3D des fichiers STGxMD.B sont incorrectes dans `custom-tools/DbzLegendsAnalyser`. Ce document liste les tâches d'investigation pour un agent IA disposant de **Ghidra (via ReVa MCP)** et **PCSX-Redux MCP**.

---

## Contexte actuel

| Élément | Détail |
|---------|--------|
| Viewer C# | `DbzLegendsAnalyser/Viewers/STG_MD_View.cs` |
| Loader C# | `PsxTools/StgMdLoader.cs` |
| Doc format | `docs/STG_MD_FILE_FORMAT_ANALYSIS.md` |
| Fonctions clés (Ghidra) | `RenderMesh` @ `0x80051CF4`, `LoadStage` @ `0x80041640`, `SetupParticles` @ `0x800402d8`, `RenderAndUpdateParticles` @ `0x800400D4`, floor tile draw @ `0x80066870` |
| GPU helper décompilé | `src/game/gpu.c` — `LoadImage_ReturnTPageOrClutId` @ `0x80067178` |
| Programme Ghidra | Utiliser le path du programme GAME.EXE dans le projet Ghidra |

### Problèmes connus
1. **FindTexture (Pass 4 fallback)** : le mesh stage référence des TPages VRAM de personnages (CH_BIN) non présents dans STGxTX.B → fallback V-range-only qui associe la mauvaise texture.
2. **ComputeUV fallback** : quand `tx.TPageX != tpageX`, l'UV est normalisé naïvement (`uv.U / pagePixW`) → coordonnées incorrectes.
3. Each primitive in a section can have a **different TSB** (17 unique TPages across 20 GT3 primitives in mesh#0/Sec[0]) — la logique actuelle fait un seul FindTexture par triangle mais le mapping UV est faux.
4. **Mode couleur (4bpp/8bpp/15bpp)** : le `colorMode` du TSB affecte la largeur en pixels d'une TPage (256px en 4bpp, 128px en 8bpp, 64px en 15bpp) — pas sûr que ce soit correctement géré.

---

## Phase 1 — Comprendre le rendu GPU original (Ghidra / ReVa)

### Tâche 1.1 — Décompiler `RenderMesh` (0x80051CF4)
- **Outil** : `mcp_reva_get-decompilation` avec `functionNameOrAddress: "0x80051CF4"`, `includeCallees: true`
- **Objectif** : Obtenir le code complet de RenderMesh. Identifier chaque sous-routine appelée par type de primitive (case 0–7 du switch).
- **Focus** : Comment le TSB (texture status bits) et CBA (clut base address) sont extraits du primitive data et passés aux GPU primitives PSX (`SetDrawTPage`, `SetSemiTrans`, `setUV3`, etc.)
- **Livrable** : Liste des callees avec leur adresse, signature et rôle (setup UV, setup TPage, lighting, AddPrim, etc.)

### Tâche 1.2 — Décompiler les render handlers par type de primitive
- **Outil** : `mcp_reva_get-decompilation` pour chaque callee identifiée en 1.1
- **Focus** :
  - Comment `u0,v0,CBA` et `u1,v1,TSB` sont lus depuis le flux short*
  - Comment TSB est décodé : `tpageX = TSB & 0xF`, `tpageY = (TSB>>4) & 1`, `abr = (TSB>>5) & 3`, `tp = (TSB>>7) & 3`
  - Le TSB est-il utilisé directement via `SetDrawTPage(tsb)` ou reconstruit manuellement ?
  - Les UV bruts (0–255) sont-ils passés tels quels à `setUV3(poly, u0,v0, u1,v1, u2,v2)` ?
- **Livrable** : Pseudo-code clair du pipeline UV pour au moins POLY_GT3 (type 2) et POLY_FT3 (type 0)

### Tâche 1.3 — Décompiler `LoadStage` (0x80041640)
- **Outil** : `mcp_reva_get-decompilation` avec `functionNameOrAddress: "0x80041640"`, `includeCallees: true`
- **Focus** :
  - Comment STGxTX.B est chargé en VRAM (appels à `LoadImage_ReturnTPageOrClutId`)
  - Quelles positions VRAM (x, y, w, h) sont utilisées pour chaque entrée TX
  - Comment les TPages des primitives du mesh sont censées correspondre aux textures chargées
  - Y a-t-il un chargement préalable de CH_BIN textures en VRAM que le stage réutilise ?
- **Livrable** : Table de correspondance : entrée TX index → position VRAM (x,y,w,h) → TPage ID calculé

### Tâche 1.4 — Analyser `FUN_80066870` (floor tile rendering)
- **Outil** : `mcp_reva_get-decompilation` avec `functionNameOrAddress: "0x80066870"`
- **Focus** : Comment le sol utilise tpage=0xB avec des UV (x%2)*32, (z%2)*32 ; confirmer la logique UV du sol
- **Livrable** : Validation ou correction de la logique floor dans STG_MD_View.cs

### Tâche 1.5 — Identifier la fonction de chargement CH_BIN → VRAM
- **Outil** : `mcp_reva_search-decompilation` chercher `LoadImage` ou `0x80106000`
- **Objectif** : Trouver où les textures CH_BIN de personnages sont chargées en VRAM, quelles pages elles occupent, et si le stage mesh peut les référencer
- **Livrable** : Liste des TPages VRAM pré-remplis par CH_BIN avant le rendu du stage

---

## Phase 2 — Capturer l'état VRAM réel (PCSX-Redux)

### Tâche 2.1 — Capturer la VRAM complète pendant le rendu du stage
- **Pré-requis** : Jeu lancé, sur l'écran de sélection du personnage en story mode (stage visible avec caméra en rotation)
- **Outil** : `mcp_pcsx-redux_pcsx_get_vram` pour capturer la VRAM 1024×512
- **Objectif** : Screenshot VRAM pour visualiser quelles textures sont à quelles positions
- **Livrable** : Image VRAM annotée avec les TPages utilisées par les primitives du mesh

### Tâche 2.2 — Lire les primitive data bruts en RAM
- **Outil** : `mcp_pcsx-redux_pcsx_read_memory` à l'adresse du mesh#0 section[0] (RAM `0x80106400`, 1200 bytes = 20 × 60 bytes POLY_GT3)
- **Objectif** : Extraire les 20 TSB (tpage) values réelles et les 20×3 paires UV pour les comparer avec ce que StgMdLoader parse
- **Livrable** : Table des 20 primitives : TSB brut, tpageX, tpageY, colorMode, UV0, UV1, UV2

### Tâche 2.3 — Vérifier les positions VRAM des textures TX chargées
- **Outil** : `mcp_pcsx-redux_pcsx_read_memory` aux adresses des structures TX en RAM
- **Objectif** : Confirmer que les entries TX dans STGxTX.B correspondent aux positions VRAM réelles (vramX, vramY) lues en RAM
- **Livrable** : Validation de la table TX entry ↔ position VRAM

### Tâche 2.4 — Poser un breakpoint sur RenderMesh et capturer les paramètres
- **Outils** : `mcp_pcsx-redux_pcsx_add_breakpoint` à `0x80051CF4`, puis `mcp_pcsx-redux_pcsx_get_registers`
- **Objectif** : Capturer les registres $a0 (meshData ptr) et $a1 (renderFlags) à l'entrée de RenderMesh pour valider le flux
- **Livrable** : Confirmation que les adresses mesh passées en runtime correspondent aux offsets parsés depuis le fichier

---

## Phase 3 — Comparer et diagnostiquer

### Tâche 3.1 — Construire la table de vérité UV
Pour chaque primitive du mesh#0/Section[0] (20 POLY_GT3) :

| # | TSB raw | tpageX | tpageY | colorMode | UV0 (U,V) | UV1 (U,V) | UV2 (U,V) | TX entry attendu | TX entry actuel (FindTexture) | UV calculé correct ? |
|---|---------|--------|--------|-----------|-----------|-----------|-----------|------------------|-------------------------------|---------------------|

- Remplir avec les données brutes (Phase 2.2) et le résultat de FindTexture actuel
- Identifier les primitives qui tombent dans le fallback Pass 4

### Tâche 3.2 — Analyser la relation TPage ↔ texture VRAM
- Pour chaque TSB unique parmi les 17 trouvés, déterminer :
  - Le TPage ID PSX correspondant (x/64 + (y/256)*16)
  - Si ce TPage existe dans STGxTX.B
  - Si ce TPage est pré-chargé par CH_BIN
  - Quelle texture devrait réellement être utilisée
- **Objectif** : Comprendre si les primitives du stage sont censées utiliser des textures du CH_BIN (personnages) mélangées avec des textures du TX (décor)

### Tâche 3.3 — Vérifier le pipeline UV du jeu vs l'outil
Comparer pour 3-5 primitives représentatives :
1. UV bruts (U, V bytes) lus depuis le fichier
2. TSB → TPage ID → position absolue VRAM pixel
3. GPU primitive résultant (setUV3 values) — les UV passés à la PSX sont-ils identiques aux UV bruts ?
4. ComputeUV de l'outil → valeur [0,1] calculée
5. Position réelle dans la texture source → valeur [0,1] attendue

---

## Phase 4 — Corriger le code C#

### Tâche 4.1 — Corriger `FindTexture`
Selon les résultats de 3.2, implémenter la bonne logique de sélection de texture :
- Option A : Charger les atlas CH_BIN en plus de STGxTX.B dans le viewer
- Option B : Composer une VRAM virtuelle 1024×512 et plaquer les textures dessus comme le fait la PSX
- Option C : Mapper directement chaque TPage ID à la bonne sous-texture du TX

### Tâche 4.2 — Corriger `ComputeUV`
Les UV PSX (0–255) sont relatifs à la TPage de 256×256 pixels (en 4bpp) ou 128×256 (8bpp).
La conversion en [0,1] doit prendre en compte :
- La position absolue VRAM du texel : `absPixX = tpageX * pagePixW + U`
- La position et taille de la texture source dans la VRAM
- Le mode couleur (4bpp = 256px wide tpage, 8bpp = 128px, 15bpp = 64px)

### Tâche 4.3 — Valider sur plusieurs stages
Tester le fix avec STG1MD.B, STG2MD.B, STG3MD.B pour confirmer que les textures sont correctes sur tous les stages.

---

## Commandes MCP de référence rapide

```
# Ghidra (ReVa)
mcp_reva_get-decompilation(programPath, functionNameOrAddress, includeCallees, limit)
mcp_reva_search-decompilation(programPath, query)
mcp_reva_find-cross-references(programPath, addressOrSymbol)
mcp_reva_get-data(programPath, address, length)
mcp_reva_get-symbols(programPath, nameFilter)

# PCSX-Redux
mcp_pcsx-redux_pcsx_read_memory(address, size)
mcp_pcsx-redux_pcsx_read_memory_raw(address, size)
mcp_pcsx-redux_pcsx_get_vram()
mcp_pcsx-redux_pcsx_add_breakpoint(address)
mcp_pcsx-redux_pcsx_get_registers()
mcp_pcsx-redux_pcsx_wait_for_break()
mcp_pcsx-redux_pcsx_resume()
mcp_pcsx-redux_pcsx_screenshot()
mcp_pcsx-redux_pcsx_get_status()
```

---

## Notes pour l'agent

1. **Programme Ghidra** : Lister les programmes ouverts avec `mcp_reva_list-open-programs` pour obtenir le `programPath` exact avant toute décompilation.
2. **PCSX-Redux** : Vérifier l'état du jeu avec `mcp_pcsx-redux_pcsx_get_status` avant de lire la RAM — s'assurer que le stage est chargé.
3. **Priorité** : Commencer par Phase 1 (Ghidra) pour comprendre le code original, puis Phase 2 (runtime) pour valider, puis Phase 3 pour diagnostiquer avant de coder en Phase 4.
4. **Les UV PSX sont absolus dans la TPage** : Sur PS1, `setUV3(poly, u0,v0, u1,v1, u2,v2)` prend des coordonnées 0–255 relatives à la TPage définie par `SetDrawTPage`. Le GPU hardware fait le lookup VRAM automatiquement. L'outil doit reproduire ce mapping.
5. **colorMode du TSB** : bits [8:7] = `tp` → 0=4bpp (CLUT 16 couleurs), 1=8bpp (CLUT 256 couleurs), 2=15bpp (direct color). Cela change la largeur pixel d'une TPage : 256, 128, ou 64 pixels respectivement.
