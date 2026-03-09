# Structure des fichiers CH_x.BIN

**Date d'analyse :** Mars 2026  
**Sources :** Ghidra/ReVa MCP, PCSX-Redux, analyse binaire directe  
**Fichiers analysés :** CH_01.BIN (CH_BIN1), IN_01.BIN (CH_BIN3)  
**Fonctions Ghidra clés :** `RenderBattleScene3D (0x80035a04)`, `FUN_800374f4 (0x800374f4)`, `LoadCHBinFileAsync (0x80035828)`

---

## 1. Vue d'ensemble

Les fichiers CH_x.BIN sont des **modèles 3D de personnages** contenant géométrie, UV, couleurs, éclairage et données d'animation (keyframes). Chaque fichier correspond à un personnage du jeu.

**Pipeline de chargement :**
```
FUN_80034ed0 (state machine combat)
  └─ case 1 → LoadCHBinFileAsync()        → chargement CD-ROM async à 0x801D2000
  └─ case 2 → RenderBattleScene3D()       → relocalisation + rendu 3D
  └─ case 3 → FUN_800368b4()             → dispatch animation (stream dispatch table)
  └─ case 4 → FUN_80036bb0()             → update animation state
```

**Adresses mémoire clés :**
| Symbole | Adresse RAM | Rôle |
|---|---|---|
| `g_cdFileBufferTable` | `0x801D2000` | Base buffer load CD-ROM (= dword[0] du fichier) |
| `g_meshTableCounts` | `0x801D2004` | entry_count = dword[1] |
| `g_chBinEntryTableBasePtr` | `0x801D2008` | Ptr table d'entrées (dword[2] après reloc) |
| `g_cdFileBaseOffset` | `0x8009A978` | Valeur runtime = 0x2E800 après set |

---

## 2. Bases d'adressage

| Paramètre | Valeur | Preuve |
|---|---|---|
| Adresse de chargement RAM | `0x801D2000` | Fixed, buffer CD-ROM global |
| Base compile-time des pointeurs | `0x801A3800` | Calculé: ptr_entry_table (0x801A4A44) - foff (0x1244) |
| Offset de relocalisation | `+0x2E800` | `RenderBattleScene3D` L117 : `g_cdFileBaseOffset = 0x2e800` |
| Formule file_offset | `ptr_compiletime - 0x801A3800` | Vérifié sur 4 pointeurs CH_01.BIN |
| Formule runtime_addr | `ptr_compiletime + 0x2E800` | Identique à ci-dessus mais exprimé autrement |

> **Note :** Tous les fichiers CH_BIN1, CH_BIN2, **et** CH_BIN3 utilisent la même `compile_time_base = 0x801A3800`.

---

## 3. Format header CH_BIN (24 octets)

```
Offset  Taille  Contenu
──────  ──────  ─────────────────────────────────────────────────────
+0x00   uint16  reloc_loop_bound  = nombre de dwords à relocaliser (4 pour CH_BIN1/2, 22 pour CH_BIN3)
+0x02   uint16  flags             = 0xC000 (bit15 = mode spécial, vérifié L58-73 RenderBattleScene3D)
+0x04   uint32  entry_count       = nombre d'entrées dans la table (37 pour CH_01.BIN, 13 pour IN_01.BIN)
+0x08   uint32  ptr_entry_table   → pointeur compile-time vers la table d'entrées (relocalisé +0x2E800)
+0x0C   uint32  ptr_section_B     → pointeur compile-time (relocalisé) — usage PROBABLE : alt entry group
+0x10   uint32  ptr_section_C     → pointeur compile-time (relocalisé) — usage PROBABLE : alt entry group
+0x14   uint32  ptr_section_D     → pointeur compile-time (relocalisé) — usage PROBABLE : data anim raw
       [+0x18 pour CH_BIN3 : 18 pointeurs supplémentaires (dw[6..23])]
```

**Classification :**
- `reloc_loop_bound` — CERTAIN (RenderBattleScene3D L119-125 : boucle `while (uVar6 < reloc_loop_bound)`)  
- `flags (0xC000)` — CERTAIN (L58 : `(g_cdFileBufferTable & 8) != 0` utilise le flag bit)  
- `entry_count` — CERTAIN (`g_meshTableCounts` @ 0x801D2004, L128 + L309 RenderBattleScene3D)  
- `ptr_entry_table` — CERTAIN (L127 : `local_98 = g_chBinEntryTableBasePtr`)  
- `ptr_section_B/C/D` — PROBABLE (FUN_800374f4 L49-50 : dispatch array indexé sur le paramètre)

**Mécanisme de relocalisation (CERTAIN) :**
```c
// RenderBattleScene3D L117-126
g_cdFileBaseOffset = 0x2e800;
for (int i = 2; i < reloc_loop_bound; i++) {
    g_cdFileBufferTable[i] += 0x2e800;   // transforme compile-time ptr → RAM ptr
}
```

---

## 4. Section data (entre header et entry table)

Pour **CH_01.BIN** : section data de `+0x0018` à `+0x1243` (4652 bytes).

La section data contient 5 sous-sections identifiées, toutes référencées indirectement via les champs des entrées :

### 4.1 Vertex Data (`+0x0018`)

Données brutes vertexes. Pointé par `VertexDataStruct.vertex_buffer_ptr`.

```
Octets @ 0x0018 : 80 80 80 00 80 80 60 00 ...
Format probable : { sint8 dx, sint8 dy, sint8 dz, pad:0x00 } × N (4 bytes/vertex)
```

> **PROBABLE** : compact vertex deltas animés (morphing), encodage 8-bit signé par composante.

### 4.2 VertexDataStruct — stream table (`~+0x00BC`)

Table de descripteurs de blocs vertex. Pointée par `entry.field_08 + 0x2E800`.

```c
// Entrée répétée jusqu'à ptr==0
typedef struct VertexStreamEntry {
    uint32_t vertex_buffer_ptr;   // +0x00: Ptr compile-time → bloc vertex brut
    uint32_t counts_packed;       // +0x04: hi_u16=countX, lo_u16=countY (grille 2D d'itération)
} VertexStreamEntry;  // 8 bytes/entrée
```

Données observées @ 0x00BC  (4 entrées valides + terminateur NULL) :
| idx | ptr_file_off | countX | countY |
|---|---|---|---|
| 0 | 0x0018 | 1 | 255 |
| 1 | 0x001C | 4 | 6 |
| 2 | 0x002C | 4 | 16 |
| 3 | 0x003C | 32 | 1 |
| 4 | NULL (0x00000000) | — | — |

> **CERTAIN** : structure stream 8-bytes prouvée par RenderBattleScene3D L184-189 + IterateMeshStreamAndFetch.

### 4.3 PrimitiveEntry array (`~+0x00DC`)

Array d'indices pour les primitives polygones. Pointé par `MeshDataStruct.indices_ptr`.

```c
typedef struct PrimitiveEntry {
    uint8_t uv_indices[4];    // +0x00-0x03: index UV pour les 4 vertices (GT4) ou 3 (GT3)
    uint8_t color_indices[4]; // +0x04-0x07: index couleur pour les 4 vertices
    uint8_t prim_type;        // +0x08: 0 = GT4 (quad), non-0 = GT3 (triangle)
    uint8_t unknown_0x09[3];  // +0x09-0x0B: INCONNU
} PrimitiveEntry;  // 12 bytes/primitive — CERTAIN (L187 RenderBattleScene3D : local_c4 += 12)
```

### 4.4 UV Table (`~+0x043C`)

Table UV textures 6 bytes/entrée. Pointée par `MeshDataStruct.uv_table_stream_ptr`.  
Accès dans le code : `local_c4[i] × 6 + iVar19 + offset` (i=0..3, offset=0,2,4).

```
Format : { sint16 U, sint16 V, sint16 W? } × N  (6 bytes/entry)
Données @ 0x043C : 00 00 00 00 F6 FF 00 00 0F 00 64 00 ...
Interprétation : U=0x0000=0, V=0x0000=0, ?=0xFFF6=-10 (signed)
```

> **CERTAIN** : stride de 6, prouvé par pattern `× 6 + 0/2/4` dans RenderBattleScene3D L241-253 et FUN_800374f4 L157-168.

### 4.5 Color Table (`~+0x05C4`)

Table couleurs 6 bytes/entrée. Pointée par `MeshDataStruct.color_table_stream_ptr`.  
Format identique à UV (3 × uint16).

> **PROBABLE** : couleurs vertex (R, G, B × 2 bytes chacun).

### 4.6 MeshDataStruct — stream table (`~+0x05E8`)

Pointée par `entry.field_0C + 0x2E800`. Décrit multiple groupes de primitives.

```c
typedef struct MeshStreamGroup {
    uint32_t indices_ptr;         // +0x00: Ptr compile-time → PrimitiveEntry array
    uint32_t uv_table_ptr;        // +0x04: Ptr compile-time → UV table
    uint32_t color_table_ptr;     // +0x08: Ptr compile-time → Color table
    uint32_t                      // INCONNU — pattern vide observé, possiblement padding
    uint32_t counts_mesh;         // +0x0C: hi_u16=?, lo_u16=? (counts mesh)
    // next entry follows at +16 si countX/countY épuisés
} MeshStreamGroup;  // 16 bytes/groupe — PROBABLE (L191-193 : local_c8[3] = counts)
```

Données@ 0x05E8 :
```
[+00]=0x801A38DC → indices_ptr → foff 0x00DC
[+04]=0x801A3C3C → uv_table_ptr → foff 0x043C
[+08]=0x801A3DC4 → color_table_ptr → foff 0x05C4
[+0C]=0x000100FF → counts_packed hi=1, lo=255
[+10]=0x801A38E8 → second group indices_ptr
[+14]=0x801A3C3C → same UV table
...
```

### 4.7 LightingDataStruct — stream table (`~+0x06B8`)

Pointée par `entry.field_10 + 0x2E800`. Couleurs d'éclairage per-vertex.

```c
typedef struct LightingStreamEntry {
    uint32_t lighting_buffer_ptr;  // +0x00: Ptr compile-time → buffer RGB lighting
    uint32_t counts_packed;        // +0x04: hi_u16=countX, lo_u16=countY
} LightingStreamEntry;  // 8 bytes/entrée
```

Le buffer lighting contient des valeurs `int16_t` RGB additionnées en rendu :
```c
// RenderBattleScene3D L277-294
vertexR = local_bc[0] + local_bc[1];  // Add R lighting
vertexG = local_bc[2] + local_bc[3];  // Add G lighting
```

> **CERTAIN** : structure 8-bytes prouvée par L190-194 + IterateMeshStreamAndFetch_Offset8.

### 4.8 AnimStream data (`~+0x06E8` et suivants)

Données d'animation frame par frame. Multiples blocs, un par entrée ayant `stream≠0`.

```c
// Header AnimStream (RenderBattleScene3D L199-206)
// structure: [ skip_u16, offset_count:u16, skip_u16, ... data ]
// Parsé comme : ptr = stream_base+2; count = *(ptr+2); ptr += 4
```

> **INCONNU** : format exact du corps du stream. Probablement commandes/deltas keyframe.

---

## 5. Entry Table

**Localisation :** pointée par `dword[2]` (après relocalisation).  
Pour CH_01.BIN : file offset `0x1244` → 4 bytes padding (0x00000000) + 37 × 28 bytes.

```c
typedef struct CHBinMeshEntry {
    int16_t  prim_count;           // +0x00: Nombre polygones GT3/GT4. 0 = entrée setup (skip rendu)
                                   // CERTAIN: L209 RenderBattleScene3D + L112 FUN_800374f4
    int16_t  unk_02;               // +0x02: 0 pour entrées setup, 1 pour entrées render
                                   // PROBABLE : flag is_renderable
    uint32_t unk_04;               // +0x04: 0x00000000 dans tous les entries analysés
                                   // INCONNU : jamais accédé dans code
    uint32_t vertex_stream_ptr;    // +0x08: Ptr compile-time → VertexDataStruct (stream table)
                                   // CERTAIN: L184 local_d0 = entry[2] + g_cdFileBaseOffset
                                   // Partagé par TOUTES les entrées (même valeur = même géométrie)
    uint32_t mesh_stream_ptr;      // +0x0C: Ptr compile-time → MeshDataStruct (stream table)
                                   // CERTAIN: L183 local_c8 = entry[3] + g_cdFileBaseOffset
                                   // Partagé par TOUTES les entrées
    uint32_t lighting_stream_ptr;  // +0x10: Ptr compile-time → LightingDataStruct (stream table)
                                   // CERTAIN: L190 local_c0 = entry[4] + g_cdFileBaseOffset
                                   // Partagé par TOUTES les entrées
    uint32_t anim_stream_ptr;      // +0x14: Ptr compile-time → AnimStream, ou 0 (NULL = no stream)
                                   // CERTAIN: L196 uVar8 = entry[5]; L199 if(uVar8 != 0)
                                   // VARIE par entrée = clé d'animation différente par frame
    uint32_t unk_18;               // +0x18: {lo_u16 = index séquentiel ?, hi_u16 = stride/taille ?}
                                   // PROBABLE: index dans buffer rendu partagé
                                   // Non accédé dans RenderBattleScene3D, patterne observé:
                                   //   E2=0x00000100, E3=0x00000200, ..., E9=0x0004070A
} CHBinMeshEntry;  // 28 bytes — CERTAIN (L304 RenderBattleScene3D : entry_ptr += 7 dwords)
```

### Distribution des entrées CH_01.BIN (37 total)

| Entrées | prim_count | unk_02 | anim_stream | Description |
|---|---|---|---|---|
| E0–E2 | 0 | 0/0/0 | oui (différents) | Entrées setup — skippées par render |
| E3–E7 | 1,6,16,8,1 | 1 | oui (différents) | Premières entrées render avec stream |
| E8–E36 | 4,1,8,... | 0 ou 1 | non (NULL) | Entrées render sans stream |

> **Observation clé :** Les champs `vertex_stream_ptr`, `mesh_stream_ptr`, `lighting_stream_ptr` ont la **même valeur** dans TOUTES les entrées → la géométrie est fixe, seul `anim_stream_ptr` varie → les streams d'animation déforment/modifient les données de base.

---

## 6. Map des sections CH_01.BIN (fichier complet = 20480 bytes)

```
File offset  Taille    Section
───────────  ────────  ──────────────────────────────────────────────────
0x0000       0x0018    Header (6 dwords : flags, entry_count, 4 ptrs)
0x0018       0x00A4    Vertex raw data (compact, ~4 bytes/vertex)
0x00BC       0x0030    VertexDataStruct stream table (4 entries × 8b + NULL)
0x00DC       ???       PrimitiveEntry array (12 bytes/polygon)
0x043C       ???       UV Table (6 bytes/entry)
0x05C4       ???       Color Table (6 bytes/entry)
0x05E8       ???       MeshDataStruct stream table
0x06B8       ???       LightingDataStruct stream table
0x06E8       ~0xB5B    AnimStream data (entry 0, puis autres)
...          ...       AnimStreams pour entrées E1–E8 (stream≠NULL)
0x1244       0x0004    Préfixe table (0x00000000)
0x1248       0x040C    Entry Table (37 × 28 bytes = 1036 bytes)
0x1650       CLUT      ptr3 target — PROBABLE : CLUT/palette PSX 16 couleurs (uint16 PSX color)
0x1670       IMG       ptr4 target — PROBABLE : données texture image PSX (8bpp/4bpp)
0x4898       0xB68?    ptr5 target — PROBABLE : données animation compressées (keyframes)
```

> Formule conversion pointeur → offset fichier : `file_offset = compile_time_ptr - 0x801A3800`

---

## 7. Sections ptr3, ptr4, ptr5 (voir Section 16 pour analyse live complète)

### ptr3 → file offset 0x1650

Données observées: `{0x6EF70000, 0x777B7339, 0x7FFF7BBD, ...}`  
Ces valeurs sont dans la plage int16 MAX → **PROBABLE : données SVECTOR (vertices 3D complets, pas deltas)**.

> **PROBABLE (Section 16)** : CLUT/palette PSX — 16 × uint16 PSX color. Utilisée par `AnimCmd_LoadTexture` comme source passée à `LoadImage_ReturnTPageOrClutId`.

### ptr4 → file offset 0x1670

Données observées: `{0x00482279, 0x00020074, 0x000000FC, 0x0A24B660, ...}`  
Format non identifié.  

> **PROBABLE (Section 16)** : données texture image PSX (pattern bytes 8bpp/4bpp confirmé en RAM). Même mécanisme que ptr3 pour `LoadImage_ReturnTPageOrClutId`.

### ptr5 → file offset 0x4898

Données observées: `{0x00020004, 0xE8E80010, 0x00003018, 0x04102F17, ...}`  
Pattern au début : deux petites valeurs (`0x0004`, `0x0002`) puis données denses → **PROBABLE : données d'animation compressées (keyframe commands)**.

> **PROBABLE** : stream de keyframes animées, différent des AnimStream entries (peut-être animation globale du personnage vs animation par partie du corps).

---

## 8. Mécanisme de rendu (pipeline complet)

```c
// RenderBattleScene3D : boucle principale (L175-309)
local_98 = g_chBinEntryTableBasePtr;      // ptr vers entry table[0]
local_38 = local_98 + 1;                  // skip Entry 0, commence Entry 1
for (uint i = 0; i < entry_count; i++) {
    // Charger les 3 stream tables depuis l'entrée
    MeshData*    mesh     = entry[3] + 0x2E800;
    VertexData*  vertex   = entry[2] + 0x2E800;
    LightingData* light   = entry[4] + 0x2E800;
    
    // Charger prim_count
    if (prim_count > 0) {
        // Pour chaque primitive :
        for (int p = 0; p < prim_count; p++) {
            // Lire indices depuis PrimitiveEntry (local_c4)
            // Récupérer UV depuis UV table (local_c4[0..3] × 6 + uv_table_ptr)
            // Récupérer colors depuis color table (local_c4[4..7] × 6 + color_table_ptr)
            // Copier vertices depuis vertex buffer
            // Appliquer lighting (additive RGB)
            // Émettre POLY_GT4 ou POLY_GT3 selon prim_type
        }
    }
    
    // Animation stream (si anim_stream_ptr != 0)
    if (entry[5] != 0) {
        anim_stream = entry[5] + 0x2E800 + 2;
        count = *(anim_stream + 2);
        meshStreamPtrBuffer[n] = anim_stream + 4;  // enregistré pour FUN_800368b4
    }
    
    entry += 7;  // stride 28 bytes
}
```

---

## 9. Dispatch d'animation (FUN_800374f4) — PROBABLE

`FUN_800374f4` est enregistrée dans la dispatch table `PTR_FUN_80087950`.  
Elle sélectionne dynamiquement un groupe d'entrées en fonction du paramètre reçu :

```c
// FUN_800374f4 L49-50
uint8_t group_index = param1[1] >> 8;
local_98 = g_cdFileBufferTable[group_index];    // sélectionne ptr_entry_table ou ptr_section_B/C
sVar4    = g_meshTableCounts[group_index * 2];  // sélectionne entry_count correspondant
```

Cela permet à plusieurs groupes d'entrées d'être stockés dans un même fichier et d'être rendus sélectivement par index de groupe.

> **PROBABLE** : les 3 pointeurs `ptr_section_B/C/D` du header sont des tables d'entrées alternatives pour différentes parties du personnage ou différentes LOD.

---

## 10. CH_BIN1/2 vs CH_BIN3 (IN_xx.BIN)

| Critère | CH_BIN1/2 (`CH_xx.BIN`) | CH_BIN3 (`IN_xx.BIN`) |
|---|---|---|
| Fichiers | CH_01..CH_50 + CH_NO | IN_01..IN_10 + IN_IN, IN_OUT, IN_OT2 |
| Taille | 16–34 KB | 12 KB |
| `dword[0].low` (reloc_count) | 6 → relocalize dw[2..5] | 22 → relocalize dw[2..23] |
| `dword[1]` (entry_count) | 37 pour CH_01 | 13 pour IN_01 |
| Header size | 24 bytes (6 dwords) | 88 bytes (22 dwords) |
| Pointeurs supplémentaires | 0 | 18 ptrs extra dw[6..23] → **INCONNU** |
| Format entrées | CHBinMeshEntry 28-bytes | **INCONNU** — non confirmé pour IN_xx |
| Usage probable | Modèles 3D personnages (combat) | Animations intro/outro du combat |
| Compile-time base | 0x801A3800 | 0x801A3800 (même base) |

> **INCONNU** : le contenu exact des 18 pointeurs supplémentaires de CH_BIN3. La structure des entrées n'a pas été vérifiée pour IN_01.BIN.

---

## 11. Table des fichiers (g_ch_bin_filenames @ 0x800870AC)

Format : `char[67][27]` — 67 entrées × 27 bytes (chemin ISO 9660 null-padded).

| Index | Fichier | Dossier |
|---|---|---|
| 0 | CH_NO.BIN | CH_BIN1 (fallback si personnage non trouvé) |
| 1–29 | CH_01.BIN – CH_29.BIN | CH_BIN1 |
| 30–51 | CH_30.BIN – CH_50.BIN + CH_32_1..3 | CH_BIN2 |
| 52–61 | IN_01.BIN – IN_10.BIN | CH_BIN3 |
| 62 | IN_IN.BIN | CH_BIN3 |
| 63 | IN_OUT.BIN | CH_BIN3 |
| 64 | IN_OT2.BIN | CH_BIN3 |

---

## 12. Structures indirectes — résumé certitude

| Structure | Offset | Certitude | Preuve |
|---|---|---|---|
| `CHBinFileHeader.reloc_loop_bound` | +0x00 low | CERTAIN | L119-125 RenderBattleScene3D |
| `CHBinFileHeader.flags` | +0x00 high | CERTAIN | L58 check bit flags |
| `CHBinFileHeader.entry_count` | +0x04 | CERTAIN | g_meshTableCounts, L128+309 |
| `CHBinFileHeader.ptr_entry_table` | +0x08 | CERTAIN | g_chBinEntryTableBasePtr, L127 |
| `CHBinFileHeader.ptr_section_B/C/D` | +0x0C..+0x14 | PROBABLE | FUN_800374f4 L49-50 dispatch |
| `CHBinMeshEntry.prim_count` | +0x00 | CERTAIN | L209 RenderBattleScene3D |
| `CHBinMeshEntry.unk_02` | +0x02 | PROBABLE | pattern 0=setup/1=render |
| `CHBinMeshEntry.unk_04` | +0x04 | INCONNU | jamais accédé, toujours 0 |
| `CHBinMeshEntry.vertex_stream_ptr` | +0x08 | CERTAIN | L184 |
| `CHBinMeshEntry.mesh_stream_ptr` | +0x0C | CERTAIN | L183 |
| `CHBinMeshEntry.lighting_stream_ptr` | +0x10 | CERTAIN | L190 |
| `CHBinMeshEntry.anim_stream_ptr` | +0x14 | CERTAIN | L196-206 |
| `CHBinMeshEntry.unk_18` | +0x18 | PROBABLE | pattern indexation, FUN_800374f4 |
| `VertexStreamEntry` (8 bytes) | varies | CERTAIN | L184-189 |
| `PrimitiveEntry` (12 bytes) | varies | CERTAIN | L187 stride +12 |
| `MeshStreamGroup` (16 bytes) | varies | PROBABLE | L191-193 |
| `LightingStreamEntry` (8 bytes) | varies | CERTAIN | L190-194 |
| UV format (6 bytes/entry) | varies | CERTAIN | L241-253 ×6+0/2/4 |
| Color format (6 bytes/entry) | varies | CERTAIN | L254-259 ×6+0/2/4 |

---

## 13. AnimStream — Bytecode script d'animation

Les données `anim_stream_ptr` dans chaque entrée sont des **scripts bytecode** interprétés par `ExecuteAnimStreamBatch (0x800368b4)` via une **dispatch table à 16 entrées** (`g_animStreamDispatchTable @ 0x80087950`).

### 13.1 Format du stream

```
offset +0x00  uint16  skip_word    (ignoré par RenderBattleScene3D, 0x0000)
offset +0x02  uint16  loop_count   (écrit dans g_meshOffsetBuffer, utilisé pour contrôle anim)
offset +0x04  <stream commands...> (entrée point de ExecuteAnimStreamBatch)
```

### 13.2 Format d'une commande

```
uint16  cmd_word:   [bits 7-0] = opcode (index dans dispatch table 0..15)
                    [bits 15-8] = param_a (passé en paramètre au handler)
uint16  param_word: dépend de l'opcode — présent pour opcodes 2, 5, 6, ...
...                 opérandes variables selon opcode
```

**Règle de fin :** la boucle s'arrête dès que `opcode == 0x00`.

### 13.3 Table des opcodes — complète (51 entrées)

> **Source certifiée** : table dispatch à `g_animStreamDispatchTable (0x80087950)` + table de noms debug `g_animStreamOpcodeNames (0x80087A1C)`, noms engine en clair (16 bytes/entrée).

| Opcode | Nom debug engine | Adresse handler | Certitude | Description |
|---|---|---|---|---|
| `0x00` | `dummy` | `AnimCmd_Nop (0x800374c8)` | CERTAIN | Fin de stream / NOP |
| `0x01` | `nop_set` | `FUN_800374d0 (0x800374d0)` | CERTAIN | Set context byte : retourne `(signed_char)(cmd>>8)` + `ptr+1` |
| `0x02` | `table_set` | `AnimCmd_RenderEntryGroup (0x800374f4)` | CERTAIN | Rendu d'un groupe d'entrées CH_BIN |
| `0x03` | `load_set` | `AnimCmd_LoadTexture (0x80037f84)` | CERTAIN | **LoadTexture** : charge image CD → VRAM. `word[5](int16)` = tbl_idx → `g_cdFileBufferTable[tbl_idx]`, `word[1..4]` = x/y/w/h. Confirmé : `foff=0x077A` CH_01.BIN → x=704, y=256, w=64, h=255, tbl=4 (texture ptr) |
| `0x04` | `dummy` | `AnimCmd_Nop (0x800374c8)` | CERTAIN | Alias NOP |
| `0x05` | `anm_set` | `AnimCmd_SetCharRenderState (0x80038074)` | PROBABLE | Set render flags+palette sur char[index] |
| `0x06` | `trans_set` | `AnimCmd_SetBodyPartTransforms (0x80038308)` | CERTAIN | Set transforms (translation) 3 body parts |
| `0x07` | `rotate_set` | `AnimCmd_SetBodyPartTransforms_v2 (0x800384e0)` | PROBABLE | Set transforms (rotation) 3 body parts |
| `0x08` | `scale_set` | `AnimCmd_SetBodyPartTransforms_v3 (0x800386a8)` | PROBABLE | Set transforms (scale) 3 body parts |
| `0x09` | `cul_set` | `AnimCmd_SetMeshPaletteRange (0x80038874)` | PROBABLE | SetMeshPaletteRange : modes via bits |
| `0x0A` | `pri_set` | `AnimCmd_AddPrimsToOT (0x80038b88)` | CERTAIN | **AddPrimsToOT** : ajoute N `POLY_GT4` dans l'ordering table |
| `0x0B` | `colrol_set` | `AnimCmd_AsyncLoadTexture (0x80038d24)` | CERTAIN | **AsyncLoadTexture** : poll ou init requête async CD |
| `0x0C` | `eye_set` | `AnimCmd_ApplyCharEffect (0x80038eb0)` | PROBABLE | Lie deux personnages + set 3 params effet visuel |
| `0x0D` | `tpclut_set` | `AnimCmd_AnimateVertexColors (0x80039290)` | PROBABLE | Anime les valeurs CLUT d'une plage de `POLY_GT4` |
| `0x0E` | `rgb_set` | `AnimCmd_AnimatePolyColorRGBA (0x80039754)` | PROBABLE | Anime composantes R/G/B/alpha sur vertices `POLY_GT4` |
| `0x0F` | `cmp_set` | `AnimCmd_ConditionalBranch (0x80039028)` | CERTAIN | **ConditionalBranch** : compare variables et saute |
| `0x10` | `x_add_set` | `AnimCmd_XAddSet (0x80039d6c)` | INCONNU | — |
| `0x11` | `parts_link` | `AnimCmd_PartsLink (0x80039f44)` | INCONNU | Liaison de body parts |
| `0x12` | `x_max_set` | `AnimCmd_XMaxSet (0x8003a188)` | INCONNU | — |
| `0x13` | `rgb2_set` | `AnimCmd_Rgb2Set (0x8003a300)` | INCONNU | Seconde couleur RGB |
| `0x14` | `utylty` | `AnimCmd_Utility (0x8003a818)` | INCONNU | Fonction utilitaire générique |
| `0x15` | `objint_get` | `AnimCmd_ObjIntGet (0x8003ad38)` | INCONNU | Lecture entier depuis objet |
| `0x16` | `objlong_get` | `AnimCmd_ObjLongGet (0x8003aed4)` | INCONNU | Lecture long int depuis objet |
| `0x17` | `bit_chk` | `AnimCmd_BitChk (0x8003afa4)` | INCONNU | Vérification d'un bit — branch conditionnel |
| `0x18` | `bit_set` | `AnimCmd_BitSet (0x8003b148)` | INCONNU | Mise à 1/0 d'un bit |
| `0x19` | `end_set` | `AnimCmd_EndSet (0x8003b260)` | INCONNU | Fin de stream / terminaison |
| `0x1A` | `base_culX` | `AnimCmd_BaseCulX (0x8003b2d8)` | INCONNU | Culling base — axe X |
| `0x1B` | `base_culY` | `AnimCmd_BaseCulY (0x8003b758)` | INCONNU | Culling base — axe Y |
| `0x1C` | `base_culZ` | `AnimCmd_BaseCulZ (0x8003bbec)` | INCONNU | Culling base — axe Z |
| `0x1D` | `movexp_set` | `AnimCmd_MovexpSet (0x8003c514)` | INCONNU | Expression de mouvement |
| `0x1E` | `dist_set` | `AnimCmd_DistSet (0x8003c638)` | INCONNU | Distance (LOD ?) |
| `0x1F` | `move_set` | `AnimCmd_MoveSet (0x8003c738)` | INCONNU | Vecteur de déplacement |
| `0x20` | `uv0123_set` | `AnimCmd_Uv0123Set (0x8003cb40)` | INCONNU | Coordonnées UV des 4 vertices d'un quad |
| `0x21` | `eff_set` | `AnimCmd_EffSet (0x8003cf38)` | INCONNU | Set effet |
| `0x22` | `att_set` | `AnimCmd_AttSet (0x8003d208)` | INCONNU | Set attributs |
| `0x23` | `if_set` | `AnimCmd_IfSet (0x8003d450)` | INCONNU | Conditionnel if (variant de cmp_set ?) |
| `0x24` | `dummy` | `AnimCmd_Nop (0x800374c8)` | CERTAIN | Alias NOP (deuxième) |
| `0x25` | `xy0123_set` | `AnimCmd_Xy0123Set (0x8003d580)` | INCONNU | Coords XY écran des 4 vertices d'un quad |
| `0x26` | `ot_z_set` | `AnimCmd_OtZSet (0x8003dac8)` | INCONNU | Valeur Z de tri OT |
| `0x27` | `ch_eff_set` | `AnimCmd_ChEffSet (0x8003de10)` | INCONNU | Set effet personnage |
| `0x28` | `ch_dan_set` | `AnimCmd_ChDanSet (0x8003e508)` | INCONNU | Set état dommage personnage |
| `0x29` | `hitz_set` | `AnimCmd_HitzSet (0x8003e760)` | INCONNU | Set zone de collision Z |
| `0x2A` | `auto_otz` | `AnimCmd_AutoOtz (0x8003e918)` | INCONNU | Calcul automatique OT Z |
| `0x2B` | `auto_rgb` | `AnimCmd_AutoRgb (0x8003e9c0)` | INCONNU | Set couleur RGB automatique |
| `0x2C` | `cheff_wait` | `AnimCmd_CheffWait (0x8003ebcc)` | INCONNU | Attendre fin d'un effet personnage |
| `0x2D` | `chse_call` | `AnimCmd_ChseCall (0x8003ec74)` | INCONNU | **Appel SFX personnage** (son effet) |
| `0x2E` | `chse_vol` | `AnimCmd_ChseVol (0x8003ed58)` | INCONNU | **Volume SFX personnage** |
| `0x2F` | `voice_call` | `AnimCmd_VoiceCall (0x8003eed8)` | INCONNU | **Appel ligne vocale** |
| `0x30` | `atse_call` | `AnimCmd_AtseCall (0x8003f198)` | INCONNU | **Appel son atmosphérique** |
| `0x31` | `base_culP` | `AnimCmd_BaseCulP (0x8003c080)` | INCONNU | Culling base — projection/position |
| `0x32` | *(sans nom debug)* | `AnimCmd_FUN_0x32 (0x8003f058)` | INCONNU | Opcode non documenté dans la table debug |

### 13.4 Opcode 0x06 — SetBodyPartTransforms (ANALYSÉ)

```
uint16 cmd_word:    opcode=0x06, param_a = object_id (index transform lookup via FUN_8003f37c)
uint16 param_word:  bits [4:0]   = type_part0  (0..14 = type; 0xF = skip ce body part)
                    bits [5]     = flag_part0   (0=valeur directe, 1=valeur indirecte via table)
                    bits [9:5]   = type_part1   (identique)
                    bits [10]    = flag_part1
                    bits [14:10] = type_part2
                    bits [15]    = flag_part2
uint16 operand_0:   valeur pour body_part 0   (sauf si type=0xF)
uint16 operand_1:   valeur pour body_part 1   (sauf si type=0xF)
uint16 operand_2:   valeur pour body_part 2   (sauf si type=0xF)
```

**Exemple Entry0 stream `{0x1506, 0x2008, 0x0000, 0x0000, 0x0000}` :**
- object_id = 0x15 = 21 (index dans la table de transforms)
- part0: type=8, flag=0, operand=0 → reset part type 8 to 0
- part1: type=0, flag=0, operand=0 → reset part type 0 to 0  
- part2: type=8, flag=0, operand=0 → reset part type 8 to 0

**Note sur la progression du stream :**  
`AnimCmd_SetBodyPartTransforms` retourne `puVar6` qui a avancé de 2 `uint16` header + N `uint16` opérandes. `ExecuteAnimStreamBatch` met à jour le pointeur courant et lit le prochain opcode.

### 13.5 Exemple CH_01.BIN Entry0 stream complet (foff 0x06E8)

```
+0x00  0x0000  [header: skip]
+0x02  0x0001  [header: loop_count = 1]
+0x04  0x1506  [cmd: opcode=0x06, object_id=0x15]
+0x06  0x2008  [param: parts config {type8, type0, type8}]
+0x08  0x0000  [operand part0: value=0]
+0x0A  0x0000  [operand part1: value=0]
+0x0C  0x0000  [operand part2: value=0]
                 (la suite à +0x0E continue avec d'autres commandes selon le data scan)
```

---

## 14. Symboles Ghidra mis à jour

### 14.1 Labels globaux

| Adresse | Symbole | Certitude | Action |
|---|---|---|---|
| `0x801D2008` | `g_chBinEntryTableBasePtr` | CERTAIN | Label ajouté |
| `0x80087950` | `g_animStreamDispatchTable` | CERTAIN | Label ajouté |
| `0x80087A1C` | `g_animStreamOpcodeNames` | CERTAIN | Table debug 51 noms × 16 bytes |

### 14.2 Fonctions AnimStream — dispatch table complète (51 entrées)

> **CERTAIN** : lu directement depuis `g_animStreamDispatchTable` + confirmé par `g_animStreamOpcodeNames`.

| Index | Adresse | Nom Ghidra | Nom debug engine | Certitude |
|---|---|---|---|---|
| `0x00` | `0x800374c8` | `AnimCmd_Nop` | `dummy` | CERTAIN |
| `0x01` | `0x800374d0` | `FUN_800374d0` | `nop_set` | CERTAIN |
| `0x02` | `0x800374f4` | `AnimCmd_RenderEntryGroup` | `table_set` | CERTAIN |
| `0x03` | `0x80037f84` | `AnimCmd_LoadTexture` | `load_set` | CERTAIN |
| `0x04` | `0x800374c8` | `AnimCmd_Nop` (alias) | `dummy` | CERTAIN |
| `0x05` | `0x80038074` | `AnimCmd_SetCharRenderState` | `anm_set` | PROBABLE |
| `0x06` | `0x80038308` | `AnimCmd_SetBodyPartTransforms` | `trans_set` | CERTAIN |
| `0x07` | `0x800384e0` | `AnimCmd_SetBodyPartTransforms_v2` | `rotate_set` | PROBABLE |
| `0x08` | `0x800386a8` | `AnimCmd_SetBodyPartTransforms_v3` | `scale_set` | PROBABLE |
| `0x09` | `0x80038874` | `AnimCmd_SetMeshPaletteRange` | `cul_set` | PROBABLE |
| `0x0A` | `0x80038b88` | `AnimCmd_AddPrimsToOT` | `pri_set` | CERTAIN |
| `0x0B` | `0x80038d24` | `AnimCmd_AsyncLoadTexture` | `colrol_set` | CERTAIN |
| `0x0C` | `0x80038eb0` | `AnimCmd_ApplyCharEffect` | `eye_set` | PROBABLE |
| `0x0D` | `0x80039290` | `AnimCmd_AnimateVertexColors` | `tpclut_set` | PROBABLE |
| `0x0E` | `0x80039754` | `AnimCmd_AnimatePolyColorRGBA` | `rgb_set` | PROBABLE |
| `0x0F` | `0x80039028` | `AnimCmd_ConditionalBranch` | `cmp_set` | CERTAIN |
| `0x10` | `0x80039d6c` | `AnimCmd_XAddSet` | `x_add_set` | INCONNU |
| `0x11` | `0x80039f44` | `AnimCmd_PartsLink` | `parts_link` | INCONNU |
| `0x12` | `0x8003a188` | `AnimCmd_XMaxSet` | `x_max_set` | INCONNU |
| `0x13` | `0x8003a300` | `AnimCmd_Rgb2Set` | `rgb2_set` | INCONNU |
| `0x14` | `0x8003a818` | `AnimCmd_Utility` | `utylty` | INCONNU |
| `0x15` | `0x8003ad38` | `AnimCmd_ObjIntGet` | `objint_get` | INCONNU |
| `0x16` | `0x8003aed4` | `AnimCmd_ObjLongGet` | `objlong_get` | INCONNU |
| `0x17` | `0x8003afa4` | `AnimCmd_BitChk` | `bit_chk` | INCONNU |
| `0x18` | `0x8003b148` | `AnimCmd_BitSet` | `bit_set` | INCONNU |
| `0x19` | `0x8003b260` | `AnimCmd_EndSet` | `end_set` | INCONNU |
| `0x1A` | `0x8003b2d8` | `AnimCmd_BaseCulX` | `base_culX` | INCONNU |
| `0x1B` | `0x8003b758` | `AnimCmd_BaseCulY` | `base_culY` | INCONNU |
| `0x1C` | `0x8003bbec` | `AnimCmd_BaseCulZ` | `base_culZ` | INCONNU |
| `0x1D` | `0x8003c514` | `AnimCmd_MovexpSet` | `movexp_set` | INCONNU |
| `0x1E` | `0x8003c638` | `AnimCmd_DistSet` | `dist_set` | INCONNU |
| `0x1F` | `0x8003c738` | `AnimCmd_MoveSet` | `move_set` | INCONNU |
| `0x20` | `0x8003cb40` | `AnimCmd_Uv0123Set` | `uv0123_set` | INCONNU |
| `0x21` | `0x8003cf38` | `AnimCmd_EffSet` | `eff_set` | INCONNU |
| `0x22` | `0x8003d208` | `AnimCmd_AttSet` | `att_set` | INCONNU |
| `0x23` | `0x8003d450` | `AnimCmd_IfSet` | `if_set` | INCONNU |
| `0x24` | `0x800374c8` | `AnimCmd_Nop` (alias 2) | `dummy` | CERTAIN |
| `0x25` | `0x8003d580` | `AnimCmd_Xy0123Set` | `xy0123_set` | INCONNU |
| `0x26` | `0x8003dac8` | `AnimCmd_OtZSet` | `ot_z_set` | INCONNU |
| `0x27` | `0x8003de10` | `AnimCmd_ChEffSet` | `ch_eff_set` | INCONNU |
| `0x28` | `0x8003e508` | `AnimCmd_ChDanSet` | `ch_dan_set` | INCONNU |
| `0x29` | `0x8003e760` | `AnimCmd_HitzSet` | `hitz_set` | INCONNU |
| `0x2A` | `0x8003e918` | `AnimCmd_AutoOtz` | `auto_otz` | INCONNU |
| `0x2B` | `0x8003e9c0` | `AnimCmd_AutoRgb` | `auto_rgb` | INCONNU |
| `0x2C` | `0x8003ebcc` | `AnimCmd_CheffWait` | `cheff_wait` | INCONNU |
| `0x2D` | `0x8003ec74` | `AnimCmd_ChseCall` | `chse_call` | INCONNU |
| `0x2E` | `0x8003ed58` | `AnimCmd_ChseVol` | `chse_vol` | INCONNU |
| `0x2F` | `0x8003eed8` | `AnimCmd_VoiceCall` | `voice_call` | INCONNU |
| `0x30` | `0x8003f198` | `AnimCmd_AtseCall` | `atse_call` | INCONNU |
| `0x31` | `0x8003c080` | `AnimCmd_BaseCulP` | `base_culP` | INCONNU |
| `0x32` | `0x8003f058` | `AnimCmd_FUN_0x32` | *(sans nom)* | INCONNU |

### 14.3 Autres fonctions renommées

| Adresse | Nom Ghidra | Certitude |
|---|---|---|
| `0x800368b4` | `ExecuteAnimStreamBatch` | PROBABLE |
| `0x80041640` | `InitBattleStageAssets` | PROBABLE |

---

## 15. Prochaines analyses recommandées

### Complété

- ✅ Document structure-ch-bin-files.md créé
- ✅ Section data map 0x0018..0x1243 cataloguée
- ✅ AnimStream identifié comme VM bytecode — **tous les 51 opcodes identifiés** (noms debug engine)
- ✅ 51 fonctions labels + 3 labels globaux Ghidra ajoutés
- ✅ ptr3/ptr4/ptr5 : relocation loop prouvée ; ptr3 = CLUT palette, ptr4 = texture image (section 16)
- ✅ dword[1] = g_meshTableCounts confirmé par ASM 0x80037590
- ✅ Fichier runtime identifié : `IN_IN.BIN` (CH_BIN3, 2048B, reloc=5, flags=0xC0)
- ✅ Table debug engine `g_animStreamOpcodeNames (0x80087A1C)` : 50 noms × 16 bytes
- ✅ Dispatcher = 51 entrées (0x00..0x32), sentinelle ASCII `"dummy "` à 0x80087A1C+0x33*16
- ✅ 3 alias NOP : opcodes 0x00, 0x04, 0x24 → même fonction `0x800374c8`
- ✅ 6 catégories fonctionnelles identifiées par noms debug :
  - **Transforms** : trans_set(0x06), rotate_set(0x07), scale_set(0x08), x_add_set(0x10), x_max_set(0x12), move_set(0x1F), movexp_set(0x1D)
  - **Couleurs** : rgb_set(0x0E), cul_set(0x09), tpclut_set(0x0D), rgb2_set(0x13), auto_rgb(0x2B), colrol_set(0x0B)
  - **UV/Géométrie** : uv0123_set(0x20), xy0123_set(0x25), ot_z_set(0x26), base_culX/Y/Z/P(0x1A-0x1C, 0x31), hitz_set(0x29), auto_otz(0x2A)
  - **Logique/Contrôle** : cmp_set(0x0F), bit_chk(0x17), bit_set(0x18), if_set(0x23), end_set(0x19), obj*_get(0x15-0x16)
  - **Effets** : eff_set(0x21), ch_eff_set(0x27), eye_set(0x0C), cheff_wait(0x2C)
  - **Audio** : chse_call(0x2D), chse_vol(0x2E), voice_call(0x2F), atse_call(0x30)

### Restant

1. **HAUTE PRIORITÉ** — Décoder les 35 handlers INCONNU (0x10..0x32)  
   → Décompiler/disassembler chaque handler ; le nom debug donne une forte indication  

2. **HAUTE PRIORITÉ** — Confirmer les paramètres VRAM de `AnimCmd_LoadTexture` (opcode 0x03)  
   → Trouver un stream contenant `0x03 XX` dans les CH_BIN1/CH_BIN2 et lire word[1..5]  

3. **MOYENNE PRIORITÉ** — Confirmer `unk_02` et `unk_18` dans `CHBinMeshEntry`  
   → Cross-refs sur `DAT_801fa780`/`DAT_801fa800` (AnimCmd_RenderEntryGroup L71-72)  

4. **MOYENNE PRIORITÉ** — Documenter la structure complète de `IN_IN.BIN`  
   → ptr_A/ptr_B/ptr_C/ptr_D dans l'entry_table : type de mesh data pointé  

5. **BASSE PRIORITÉ** — Créer les structs dans Ghidra (outils désactivés)  
   → `CHBinMeshEntry` (28 bytes), `VertexStreamEntry` (8 bytes), `AnimStreamHeader` (4 bytes)

---

## 16. Analyse live — header runtime (ptr3/ptr4/ptr5)

**Contexte** : breakpoint sur `RenderBattleScene3D (0x80035a04)` avant exécution (g_cdFileBaseOffset = 0 à ce stade).

> **Correction (session courante)** : le fichier chargé à `0x801D2000` est **`IN_IN.BIN`** (CH_BIN3, 2048B, reloc=5, flags=0xC0) — **ce n'est PAS un fichier STG**. Les fichiers STG ont `flags=0x00`. Identifié par matching exact de `dw[2]=0x801A3948` dans tous les CH_BIN.

### 16.1 Structure en RAM @ 0x801D2000 (`g_cdFileBufferTable`) — fichier `IN_IN.BIN`

| Offset | Valeur RAM (file offset) | Signification |
|---|---|---|
| +0x00 | `0xC0000005` (foff 0x00) | uint16 lo = 5 = reloc_count. Flags hi = 0xC0 (CH_BIN type) |
| +0x04 | `0x00000001` (foff 0x04) | entry_count = 1 |
| +0x08 | `0x801A3948` (foff 0x08) | ptr → entry_table @ foff 0x148 (relocalisé → 0x801D2148) |
| +0x0C | `0x801A3964` (foff 0x0C) | ptr3 → CLUT data @ foff 0x164 (relocalisé → 0x801D2164) |
| +0x10 | `0x801A3984` (foff 0x10) | ptr4 → texture data @ foff 0x184 (relocalisé → 0x801D2184) |
| +0x14 | `0x801A3AC4` (foff 0x14) | ptr5 → foff 0x2C4 (NON relocalisé, count_lo=5 → loop [2..4] only) |
| +0x18 | `"000\0"` (foff 0x18) | ID ASCII du slot animation = "000" |
| +0x1C | `0x801A3818` (foff 0x1C) | ptr → foff 0x18 (pointe vers propre label "000\0") |

CH_BIN compile-base `0x801A3800` + reloc offset `0x2E800` = **runtime `0x801D2000`**.

### 16.2 Relocation loop — RenderBattleScene3D L117-125, ASM 0x80035c68

```c
g_cdFileBaseOffset = 0x2E800;
// Boucle i=2 .. < (uint16)g_cdFileBufferTable[0]
// Pour CH_01.BIN (count_field_hi16=0): count_lo16=6 → patches [2],[3],[4],[5]
(&g_cdFileBufferTable)[i] += g_cdFileBaseOffset;   // compile-time → runtime
```

Preuves :
- READ  `lw $v0, 0($v1)` @ 0x80035c68 (cross-ref Ghidra → 0x801D200C)
- WRITE `sw $v0, 0($v1)` @ 0x80035c74 (cross-ref Ghidra → 0x801D200C)
- `g_cdFileBaseOffset` @ 0x8009A978 = 0 avant exécution, 0x2E800 après L117

### 16.3 Sélecteur multiple — AnimCmd_RenderEntryGroup L49-50

```c
local_98 = (uint *)(&g_cdFileBufferTable)[uVar3 >> 8]; // index 2..5
sVar4    = (&g_meshTableCounts)[(uint)(uVar3 >> 8) * 2]; // count pour ce groupe
```

Index → pointeur sélectionné :
- `2` → dw[2] = entry_table principale (37 entries pour CH_01.BIN)
- `3` → dw[3] = ptr3 section
- `4` → dw[4] = ptr4 section
- `5` → dw[5] = ptr5 section

### 16.4 Contenu des sections — données live en RAM (breakpoint 0x80035c8c)

**Entry table @ 0x801D2148 — 1 entry (prim_count=0, stub) :**
| Champ CHBinMeshEntry | Valeur raw | Note |
|---|---|---|
| prim_count (s16) | 0 | Entrée stub — aucun rendu direct déclenché |
| unk_02 (s16) | 0 | — |
| unk_04 (u32) | 0x00010001 | — |
| vertex_stream_ptr | 0x00010001 | NON relocalisé = invalide pour ce stage |
| mesh_stream_ptr | 0x801A381C | compile-time (foff=0x1C) |
| lighting_stream_ptr | 0x801A3840 | compile-time (foff=0x40) |
| anim_stream_ptr | 0x801A3858 | compile-time (foff=0x58) |
| unk_18 | 0x801A3860 | compile-time (foff=0x60) |

**ptr3 @ runtime 0x801D2164 :**
```
00 00 b5 d6 b5 d6 d6 da f7 de f7 de 18 e3 39 e7
5a eb 5a eb 7b ef 9c f3 9c f3 bd f7 de fb ff ff
```
Comme 16 × uint16 PSX color : `0x0000, 0xD6B5, 0xD6B5, 0xDAD6, 0xDEF7, 0xDEF7, 0xE318, 0xE739, 0xEB5A, 0xEB5A, 0xEF7B, 0xF39C, 0xF39C, 0xF7BD, 0xFBDE, 0xFFFF`  
→ **PROBABLE : CLUT/palette PSX 16 entrées**. `0xD6B5` = `{R=21, G=11, B=26}` brun foncé ; `0xFFFF` = blanc max. Gradient continu vers blanc. Utilisé par `AnimCmd_LoadTexture` qui passe `(&g_cdFileBufferTable)[param[5]]` directement à `LoadImage_ReturnTPageOrClutId`.

**ptr4 @ runtime 0x801D2184 :**
```
d2 00 78 00 fc 00 fc 00 fc 00 94 00 11 17 01 84
bc 32 10 66 76 17 bc 33 b6 bb 10 bb bb 1b b4 65
```
Premiers uint16 : `210, 120, 252, 252, 252, 148` — le byte `0xFC=252` répété 3× cohérent avec pixels PSX proches du blanc en 8bpp.  
→ **PROBABLE : données texture PSX (image 8bpp ou 4bpp)**. Utilisé identiquement à ptr3 par `AnimCmd_LoadTexture` comme source passée à `LoadImage_ReturnTPageOrClutId`.

**ptr5 @ 0x801A3AC4 = NON relocalisé :**  
→ **CERTAIN** : count_lo=5 → loop patche uniquement [2],[3],[4]. ptr5=dw[5] n'est PAS patché. L'adresse 0x801A3AC4 pointe dans une zone nulle. Absent pour ce fichier stage (présent uniquement dans les fichiers avec count_lo ≥ 6, comme CH_01.BIN sur disque).

**Modèle d'utilisation des sections :**
- Opcode **0x02** `AnimCmd_RenderEntryGroup` → `dw[N]` = entry table `CHBinMeshEntry[]`
- Opcode **0x03** `AnimCmd_LoadTexture` → `dw[N]` = pointeur vers **données image/CLUT**, passé à `LoadImage_ReturnTPageOrClutId`
- Opcode **0x0B** `AnimCmd_AsyncLoadTexture` → même mécanisme, accès asynchrone

### 16.5 Certitudes finales

| Élément | Certitude | Preuve |
|---|---|---|
| dword[0] uint16 lo = reloc_count | CERTAIN | ASM 0x80035c78 `lhu`; loop condition |
| dword[1] = g_meshTableCounts = count entrées | CERTAIN | Cross-ref 0x801D2004 ; ASM 0x80037590 `lhu $v0, 0x2004` |
| dword[2] = entry_table_ptr CHBinMeshEntry[] | CERTAIN | RenderBattleScene3D L127 + data live |
| dword[3..4] relocalisés (+0x2E800) | CERTAIN | Loop ASM vérifié ; adresses confirmées en RAM |
| ptr3 = CLUT/palette PSX 16 couleurs | PROBABLE | Pattern uint16 PSX color + usage `AnimCmd_LoadTexture` |
| ptr4 = texture image PSX | PROBABLE | Pattern bytes 8bpp + même mécanisme LoadImage |
| ptr5 absent si count_lo ≤ 5 | CERTAIN | RAM = 0 @ 0x801A3AC4 ; loop ne patche pas dw[5] |
| selection via `param_word>>8` | CERTAIN | AnimCmd_RenderEntryGroup L49 décompilé |

---

## 17. Fichier `IN_IN.BIN` — Structure complète (CERTAIN)

**Identification** : `data/CH_BIN3/IN_IN.BIN`, 2048 bytes. Fichier chargé par `InitBattleStageAssets` à `g_cdFileBufferTable (0x801D2000)`.  
**Discriminants** : `dw[0]=0xC0000005`, `dw[2]=0x801A3948`, `dw[6]="000\0"`.  
**Compile-time base** : `0x801A3800`. **Runtime base** : `0x801D2000`. **Offset reloc** : `+0x2E800`.

### 17.1 Layout des données (foff = offset binaire dans le fichier)

```
foff 0x000..0x01F  → CH_BIN Header (8 dwords)
  [0x00] 0xC0000005  — flags=0xC0, reloc_count=5
  [0x04] 0x00000001  — entry_count = 1
  [0x08] 0x801A3948  — ptr → entry_table @ foff 0x148  ← RELOCALISÉ +0x2E800
  [0x0C] 0x801A3964  — ptr → CLUT data @ foff 0x164   ← RELOCALISÉ +0x2E800
  [0x10] 0x801A3984  — ptr → texture data @ foff 0x184 ← RELOCALISÉ +0x2E800
  [0x14] 0x801A3AC4  — ptr5 @ foff 0x2C4               ← NON RELOCALISÉ (count=5)
  [0x18] "000\0"     — ID ASCII du slot animation
  [0x1C] 0x801A3818  — ptr → propre label "000\0" (self-reference foff 0x18)

foff 0x020..0x03F  → Données mesh / inconnu (zeros pour ce fichier)

foff 0x040..0x05F  → Sous-structure mesh (3 ptrs compile-time + dims)
  ptr  0x801A3824 → foff 0x24 (zone zéro)
  ptr  0x801A3830 → foff 0x30 (zone zéro)
  ptr  0x801A3838 → foff 0x38 (zone zéro)
  uint16 {1,1}    — dimensions 1×1

foff 0x060..0x13F  → AnimStream data + mesh sub-data
  foff 0x060..0x06F : stream 1 — debut avec uint16=0x0000 (stream vide pour entry)
  foff 0x070..0x13F : commandes AnimStream et données auxiliaires
    inclut opcode 0x32 à foff 0x138 : [32 80 00 02 17 40 00 02]

foff 0x148..0x163  → CHBinMeshEntry[0] (28 bytes) — entry_table
  [0x000] uint32 = 0x00000000 (flags/type)
  [0x004] uint16 = 1, uint16 = 1 (dims A)
  [0x008] uint16 = 1, uint16 = 1 (dims B)
  [0x00C] ptr_A = 0x801A381C → foff 0x1C (dw[7] header)
  [0x010] ptr_B = 0x801A3840 → foff 0x40 (mesh sub-struct)
  [0x014] ptr_C = 0x801A3858 → foff 0x58
  [0x018] ptr_D = 0x801A3860 → foff 0x60 (stream vide)

foff 0x164..0x183  → CLUT/Palette PSX — 32 bytes = 16 couleurs uint16
  00 00 B5 D6 B5 D6 D6 DA F7 DE F7 DE 18 E3 39 E7
  5A EB 5A EB 7B EF 9C F3 9C F3 BD F7 DE FB FF FF
  → Gradient brun foncé (0x0000) → blanc (0xFFFF), 16 teintes

foff 0x184..0x27F  → Texture image PSX (248 bytes de données visibles)
  D2 00 78 00 FC 00 FC 00 FC 00 94 00 11 17 01 84 ...
  → Pattern 8bpp/4bpp, valeurs de pixels [0..255]

foff 0x2C0..0x2CF  → Cible de ptr5
foff 0x2D0..0x7FF  → Zéros (padding jusqu'à 2048 bytes)
```

### 17.2 Certitudes structure IN_IN.BIN

| Élément | Certitude | Preuve |
|---|---|---|
| Identification fichier = IN_IN.BIN | CERTAIN | Match exact bytes dw[0,2,6] vs tous les CH_BIN |
| CLUT @ foff 0x164 = 16 couleurs PSX | PROBABLE | Pattern uint16 PSX color gradient |
| Texture @ foff 0x184 = données image | PROBABLE | Pattern bytes 8bpp |
| entry_table @ foff 0x148 = CHBinMeshEntry[1] | CERTAIN | Match runtime RAM 0x801D2148 |
| Opcode 0x32 stream @ foff 0x138 | CERTAIN | g_animStreamTable[0]=0x801D2138 runtime |
| ptr5 @ foff 0x2C4 = absent (zone zéro) | CERTAIN | RAM 0x801A3AC4 = all zeros |



---

## 18. Structure CH_01.BIN — Analyse Complète (Sprites 2D)

### 18.1 Header CH_01.BIN

```
foff=0x00: 0xC0000006  dw[0]: flags=0xC0 | reloc_count=6
foff=0x04: 0x00000025  dw[1]: entry_count = 37
foff=0x08: 0x801A4A44  dw[2]: ptr -> entry_table  (foff=0x1244)  [reloc]
foff=0x0C: 0x801A4E50  dw[3]: ptr -> CLUT data    (foff=0x1650)  [reloc]
foff=0x10: 0x801A4E70  dw[4]: ptr -> texture data  (foff=0x1670) [reloc]
foff=0x14: 0x801A8098  dw[5]: ptr -> large data    (foff=0x4898) [reloc]
foff=0x18: 0x00808080  dw[6]: RGB = (128,128,128) gris moyen
foff=0x1C: 0x00608080  dw[7]: RGB = (128,128, 96) gris-brun
foff=0x2C: 0x00808080       : RGB = (128,128,128)
foff=0x3C: 0x0038E0E0       : RGB = (224,224, 56) jaune vif
```

- **base compile-time**: `0x801A3800`
- **reloc_count=6** : patches dw[2..5] + 2 autres lors du chargement

### 18.2 CLUT (Palette) @ foff 0x1650

16 couleurs PSX 15bpp BGR5551 :

| Index | Value | R | G | B | Notes |
|-------|-------|-----|-----|-----|-------|
| 0 | 0x0000 | 0 | 0 | 0 | transparent |
| 1..4 | 0x6EF7..0x7BBD | 184..232 | 184..232 | 210..240 | gradient blanc-bleuté |
| 5 | 0x7FFF | 248 | 248 | 248 | highlight max |
| 6..8 | 0x7BBD..0x7339 | 200..232 | 200..232 | 216..240 | gradient redescendant |
| 9 | 0x0000 | 0 | 0 | 0 | transparent |
| 10..14 | 0xCBDE..0x8B5C | 224..240 | 208..240 | 16..144 | gradient jaune-brun |
| 15 | 0x0000 | 0 | 0 | 0 | transparent |

**CERTAIN** : format PSX 15bpp avéré. Deux gradients : blanc-bleuté (aura?) + jaune-brun (skin?).

### 18.3 CHBinMeshEntry — Structure (28 bytes = 7 x uint32)

```c
typedef struct CHBinMeshEntry {
    uint32_t  id_packed;        // [+0x00] b0=part_id, b1=group_id, b2=render_flags, b3=extra
    uint32_t  unknown_04;       // [+0x04] packed format INCONNU (ex: 0x010001, 0x010008)
    uint32_t  unknown_08;       // [+0x08] meme pattern que unknown_04
    uint32_t *ptr_color_table;  // [+0x0C] -> RgbColorList
    uint32_t *ptr_mesh_records; // [+0x10] -> MeshRecordList[]
    uint32_t *ptr_sprite_recs;  // [+0x14] -> SpriteRecordList[]
    uint32_t *ptr_anim_stream;  // [+0x18] -> AnimStream bytecode (ou 0 si absent)
} CHBinMeshEntry; // 28 bytes CERTAIN
```

Taille confirmee : (0x1650 - 0x1244) / 37 = **28 bytes exactement**.

#### Groupes observes (CH_01.BIN, 37 entrees)

| group_id (b1) | Entrees | Nb entrees | Notes |
|---------------|---------|------------|-------|
| 0 | [0..2] | 3 | part_id=0, d0=0x000000 |
| 1..6 | [3..8] | 1 chacun | groupes unitaires |
| 7 | [9..22] | 14 | part_ids 0..23, b3=0x7F sur [22] |
| 8 | [23..36] | 14 | part_ids 0..37, b3=0x7F sur [36] |

`b3=0x7F` aux entrees [22] et [36] uniquement : marqueurs de fin de groupe (**PROBABLE**).

### 18.4 Color Table (ptr_color_table)

Liste de records terminée par null ptr :

```c
typedef struct RgbColorRecord {
    uint32_t *ptr_rgb_value;  // -> dword RGBA packed dans le header du fichier
    uint32_t  flags_packed;   // ex: 0x000100FF, 0x00040006, 0x00040010, 0x00200001
} RgbColorRecord; // 8 bytes
```

Valeurs observees pour entry[0] :
```
[0] ptr->foff 0x0018 = RGB(128,128,128)  flags=0x000100FF
[1] ptr->foff 0x001C = RGB(128,128, 96)  flags=0x00040006
[2] ptr->foff 0x002C = RGB(128,128,128)  flags=0x00040010
[3] ptr->foff 0x003C = RGB(224,224, 56)  flags=0x00200001
[4] ptr=NULL (terminateur)
```

**PROBABLE** : modificateurs de teinte RGBA pour primitives POLY_GT4 (lie a opcode `cul_set 0x09`).

### 18.5 MeshRecordList (ptr_mesh_records)

Tableau de records de **16 bytes** :

```c
typedef struct MeshRecord {
    uint32_t *ptr_vert_data;    // [+0x00] -> donnees vertex de cette animation
    uint32_t *ptr_uv_shared;    // [+0x04] -> table UV/joints partagee (meme ptr pour TOUS)
    uint32_t *ptr_null_buf;     // [+0x08] -> zone zero (buffer runtime pre-alloue)
    uint32_t  cnt_packed;       // [+0x0C] ex: 0x000100FF, 0x00060001, 0x00100001
} MeshRecord; // 16 bytes
```

- `ptr_uv_shared` = **identique** pour tous les records d'un meme groupe
- `ptr_null_buf` @ foff 0x05C4 = entierement zero (32+ bytes confirms)
- `cnt_packed` : byte0 = count_polys?, byte1 = count_meshes?, bytes 2-3 = INCONNU

#### Shared UV+Bone Table @ foff 0x043C (CH_01.BIN)

```
idx |    x |    y  |   idx |    x |    y
  0 |    0 |    0  |    11 |   10 |   15
  1 |    0 |  -10  |    12 | -110 |    0
  2 |  100 |   15  |    13 |   20 |  -10
  3 |    0 |  110  |    14 |   15 |  100
  4 |   20 |  100  |    15 |    0 |   20
  5 |  -10 |   15  |    16 |  110 |   10
  6 |  110 |    0  |    17 |   15 | -100
  7 |   20 |   10  |    18 |    0 |   20
  8 | -100 |   15  |    19 | -110 |    0
  9 |    0 | -110  |    20 | -128 |   96
 10 |   20 | -100  |
```

**PROBABLE** : coordonnees XY relatives des articulations 2D (pixels PSX, plage [-128..+128]).
Partagée par toutes les animations d'un groupe = table de squelette partagee.

#### VertData par animation

Records de **12 bytes** (6 x int16). Exemple ptr_B[1] @ foff 0xE8 (6 polys) :

```
poly[0]:   256   2   0   0   1   0
poly[1]:   768   4   0   0   1   0
poly[2]:  1280   6   0   0   1   0
poly[3]:  1792   8   0   0   1   0
poly[4]:  2304  10   0   0   1   0
poly[5]:  2816  12   0   0   1   0
```

- col[0] = multiples de 256 = **INCONNU** (offset buffer UV? tpage?)
- col[1] = sequentiel 2,4,6 = **INCONNU** (index clut?)
- col[2..3] = zero
- col[4..5] = constante (1, 0)

**INCONNU** : signification exacte des 12 bytes.

### 18.6 SpriteRecordList (ptr_sprite_recs)

Tableau de records de **8 bytes** :

```c
typedef struct SpriteRecord {
    uint32_t *ptr_sprite;   // [+0x00] -> donnees sprite 8 bytes
    uint32_t  cnt_packed;   // [+0x04] ex: 0x000100FF, 0x00010006, 0x00010008
} SpriteRecord; // 8 bytes
```

Donnees sprite a foff 0x0688 : `00 00 00 00 00 00 00 00 F6 00 01 00 01 00 01 00`  
**INCONNU** : signification exacte.

### 18.7 Opcodes 2D — Formats Confirmes

#### opcode 0x20 `uv0123_set` — AnimCmd_Uv0123Set @ 0x8003CB40

**Format : 6 x uint16 = 12 bytes fixe**

```
word[0]: opcode=0x20 | mode_flags<<8
word[1]: b0=mesh_idx_start | b1=mesh_count (signe)
word[2..5]: UV data (4 x uint16)
```

Modes (bits 4-5 du byte1) :
- `0x00` : UV direct pour mesh_idx
- `0x10` : plage meshes [mesh_idx .. mesh_idx+count]
- `0x20` : recherche par part_id dans g_renderMetadataBuffer

Action : modifie `POLY_GT4_801f7180[mesh_idx].u0/u1/u2/u3`  
**CERTAIN** : `return param_1 + 6` dans decompilation.

#### opcode 0x25 `xy0123_set` — AnimCmd_Xy0123Set @ 0x8003D580

**Format : 5+ words variable**

```
word[0]: opcode=0x25 | mode_flags<<8
word[1]: b0=mesh_idx_start | b1=count (signe)
word[2]: 3 x 5-bit vertex_types: bits [14:10] [9:5] [4:0]
word[3]: 3 x 5-bit vertex_types
word[4]: 2 x 5-bit vertex_types
words[5+]: XY inline ou indices indirects (variable)
```

Encodage vertex_type 5-bit :
- bits[3:0] = vertex_index 0..7, ou 0xF = skip
- bit[4] = indirect (1) : lire de DAT_801faa64[idx]

Action : modifie `POLY_GT4_801f7180[mesh_idx].x0/y0/x1/y1/x2/y2/x3/y3`  
**CERTAIN** : acces `POLY_GT4_801f7180[idx].x0` explicite dans decompilation.

---

## 19. Table des Preuves — Structures Sprite 2D

| Element | Offset | Type minimal | Preuve | Confiance |
|---------|--------|--------------|--------|-----------|
| CHBinMeshEntry taille = 28B | n/a | n/a | (0x1650-0x1244)/37=28 exact | CERTAIN |
| CHBinMeshEntry.id_packed | +0x00 | uint32 | Groupes sequentiels sur 37 entrees | CERTAIN |
| CHBinMeshEntry.ptr_color | +0x0C | ptr[] | Paires (ptr_rgb, flags), RGB valides | PROBABLE |
| CHBinMeshEntry.ptr_mesh | +0x10 | ptr[] | 16B MeshRecord, ptr_uv partage | PROBABLE |
| CHBinMeshEntry.ptr_anim | +0x18 | ptr | Bytecode opcodes valides | CERTAIN |
| UVSharedTable[i].xy | foff 0x043C | int16[2] | 21 entrees XY [-128..+128] coherentes | PROBABLE |
| VertData record 12B | per-entry | int16[6] | Multiples 256 sequentiels | INCONNU format exact |
| CLUT 16 couleurs | foff 0x1650 | uint16[16] | PSX 15bpp confirme | CERTAIN |
| opcode 0x20 = 6 words | n/a | ushort* | return param_1+6 | CERTAIN |
| opcode 0x25 modifie x0/y0 | n/a | short* | POLY_GT4_801f7180[idx].x0 | CERTAIN |

---

## 20. Zones d'Ombre Prioritaires

| Question | Raison | Action recommandee |
|----------|--------|-------------------|
| VertData 12B format | Valeurs enigmatiques (256,768...) | Decompiler AnimCmd_AddPrimsToOT (0x0A @ 0x80038B88) |
| CHBinMeshEntry.unknown_04/08 | Aucun acces confirme | Cross-ref entry_table ptr, lecteurs dw[1]/dw[2] |
| cnt_packed format | Multiples interpretations | Analyser AnimCmd_RenderEntryGroup (0x02 @ 0x8003778C) |
| SpriteRecord ptr_sprite data | Aucun acces confirme | Chercher lecteurs de ptr_sprite_recs |
| Texture @ foff 0x1670 format | Non decode | Comparer header TIM PSX |
| ptr5 @ foff 0x4898 | Usage inconnu, relocatable | Analyser AnimCmd_AsyncLoadTexture (0x0B @ 0x80038A50) |


---

## 21. Mise à Jour — Session du 09/03/2026

### 21.1 Labels Ghidra Ajoutés

| Adresse | Label | Confiance | Preuve |
|---------|-------|-----------|--------|
| 0x801fa580 | `g_polyOTDepthTable` | CERTAIN | Utilisé dans AddPrimsToOT : `DAT_801fa580[polyIdx*2]` = profondeur OT, et OtZSet |
| 0x801faa64 | `g_animSharedVarTable` | CERTAIN | 98+ références dans tous les opcodes comme table d'indirection pour variables partagées |
| 0x801fa780 | `g_meshXOffsetBuffer` | PROBABLE | Zéré par RenderEntryGroup, modifié par XAddSet (opcode 0x10) per mesh |
| 0x801fa800 | `g_meshEntryFlagsHiBuf` | PROBABLE | Stocke `id_packed >> 16` par mesh, lu par XAddSet/XMaxSet |
| 0x801f2100 | `g_bodyPartTransformTable` | PROBABLE | Exclusivement SetBodyPartTransforms_v3 (scale_set), stride 8B indexé par part_id & 0xF |

### 21.2 Structures Créées dans Ghidra (catégorie /CHBin)

Toutes créées et vérifiées dans Ghidra GAME.EXE :

#### VertData (12 bytes = 0xC bytes)

```c
struct VertData {              // /CHBin/VertData
    byte  uv_idx[4];           // [0..3] indices dans UVEntry table pour 4 vertices
    byte  col_idx[4];          // [4..7] indices dans vertex_color_buf pour 4 vertices
    byte  is_gt3;              // [8]  0=POLY_GT4, non-0=POLY_GT3 (local_c4[8] bVar2)
    byte  pad[3];              // [9..11] padding
}; // CERTAIN: local_c4 += 0xC par polygone (RenderEntryGroup ligne 187)
```

#### MeshRecord (16 bytes)

```c
struct MeshRecord {             // /CHBin/MeshRecord
    uint  ptr_vert_data;        // [+0x00] -> VertData[poly_count]
    uint  ptr_uv_table;         // [+0x04] -> UVEntry[n] stride 6B (local_c8[1] = iVar19)
    uint  ptr_vertex_color_buf; // [+0x08] -> runtime color buffer zeroed (local_c8[2] = iVar18)
    uint  cnt_packed;           // [+0x0C] low int16 = polygon count (local_c8[3] = uVar8)
}; // 16B = local_c8 avance par 4 dwords (ligne 196: local_c8 = local_c8 + 4)
```

#### CHBinMeshEntry (28 bytes)

```c
struct CHBinMeshEntry {         // /CHBin/CHBinMeshEntry
    uint  id_packed;            // [+0x00] b0=part_id b1=group_id b2=flags b3=marker
    uint  poly_count_packed;    // [+0x04] (int16)low = polygon count (ligne 238 boucle)
    uint  unknown_08;           // [+0x08] INCONNU
    uint  ptr_color_table;      // [+0x0C] -> RgbColorRecord[] terminé par null
    uint  ptr_mesh_records;     // [+0x10] -> MeshRecord[]
    uint  ptr_sprite_recs;      // [+0x14] -> SpriteRecord[]
    uint  ptr_anim_stream;      // [+0x18] -> AnimStream bytecode (0 si absent)
}; // 28B CERTAIN: local_98 += 7 / puVar12 += 7 (lignes 240-244)
```

#### SpriteRecord (8 bytes)

```c
struct SpriteRecord {           // /CHBin/SpriteRecord
    uint  ptr_sprite;           // [+0x00] -> sprite data
    uint  cnt_packed;           // [+0x04] count/flags
}; // 8B: local_c0 avance par 2 dwords (lignes 226: local_c0 = local_c0 + 2)
```

### 21.3 Preuves Confirmées par AnimCmd_RenderEntryGroup (0x800374f4)

#### Boucle principale — access pattern entry_table

```
// Ligne 49: local_98 = g_cdFileBufferTable[slot] = ptr vers entry_table
// Ligne 67: puVar12 = local_98 + 1 (skip id_packed, point sur poly_count_packed)
// Boucle sur sVar4 = entry_count entrees
//   local_c8 = *(puVar12[3]) = ptr_mesh_records dereference
//   local_c4 = *(local_c8[0]) = ptr_vert_data dereference
//   iVar19 = local_c8[1] = ptr_uv_table
//   iVar18 = local_c8[2] = ptr_vertex_color_buf
// Fin: local_98 += 7 (28B), puVar12 += 7 (28B) => CHBinMeshEntry = 28B CERTAIN
```

#### Inner loop UV/Color setup — format UVEntry (6 bytes)

```c
// Ligne 157: *(word*)((vert_idx * 6) + iVar19) = UVEntry[vert_idx].word0
// Ligne 158: *(word*)((vert_idx * 6) + iVar19 + 2) = UVEntry[vert_idx].word1
// Ligne 159: *(word*)((vert_idx * 6) + iVar19 + 4) = UVEntry[vert_idx].word2
// => UVEntry stride = 6 bytes, 3 x uint16 = CERTAIN
// => probable format: { uint16 uv_packed; uint16 clut_id; uint16 tpage_id; }
```

#### VertData stride

```c
// Ligne 187: local_c4 = local_c4 + 0xc
// => VertData stride = 12 bytes CERTAIN
// Acces par indices byte 0..7 confirmes:
//   local_c4[0..3] -> UV vertex indices
//   local_c4[4..7] -> color vertex indices  
//   local_c4[8]    -> GT3/GT4 flag (0=GT4, non-0=GT3)
```

#### Opcode 0x0A `pri_set` format confirme

```
Format: 2 x uint16 = 4 bytes total
  word[0]: opcode=0x0A | flags<<8  (b1 includes part_id target)
  word[1]: mesh_count               (nombre de polys a ajouter au OT)
Return: streamPtr + 2  (ligne 32 et 48 et 43: puVar7 = streamPtr + 2)
```

- Recherche dans `g_renderMetadataBuffer[i].byte[+2] == part_id_flag`
- `g_polyOTDepthTable[polyIdx * 2]` = int16 profondeur OT (entre 0 et 0x7FF)
- Si 0 < z < 0x800 : AddPrim → ordering table (z-sort)

### 21.4 Objets OpenQuestion mis à jour

| Precedent | Statut | Evidence |
|-----------|--------|----------|
| VertData 12B format exact | RESOLU | 12 bytes: uv_idx[4] + col_idx[4] + is_gt3 + pad[3] |
| cnt_packed format MeshRecord | PARTIEL | low int16 = polygon count (CERTAIN), high = INCONNU |
| CHBinMeshEntry.unknown_08 | INCONNU | Aucun acces observe |
| SpriteRecord .ptr_sprite data | INCONNU | local_c0 derefere mais usage interne INCONNU |
| Format UVEntry 6B exact | PROBABLE | 3 words: uv_packed, clut_id, tpage_id (structure PSX coherente) |
| ptr_color_table = RgbColorRecord | CONFIRME | local_d0 itere par 2 dwords (8B), terminaison null |

### 21.5 Note Check-in

Check-in bloque par TITLE.EXE ouvert en co-edition.
Modifications sauvegardees localement dans GAME.EXE working copy :
- 5 labels renommes
- 4 structures creees (/CHBin)

