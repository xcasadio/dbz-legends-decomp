continue# Structure des fichiers CH_x.BIN

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


---

## 22. Session du 09/03/2026 — Fonctions Utilitaires et Opcodes 0x05/0x0E

### 22.1 Fonctions Utilitaires Renommées

#### ApplyMathOp @ 0x8003f694 (16 callers)

Opérateur arithmétique générique appelé par la majorité des opcodes AnimStream.

**Signature** : `short ApplyMathOp(short current_val, short op_mode, short operand)`

| Mode | Opération | Notes |
|------|-----------|-------|
| 0 | `current_val = operand` | set direct |
| 1 | `current_val + operand` | add |
| 2 | `current_val - operand` | sub |
| 3 | `current_val | operand` | bitwise OR |
| 4 | `current_val & operand` | bitwise AND |
| 5 | `current_val ^ operand` | bitwise XOR |
| 6 | `current_val * operand * 0x10000` | mul (fixedpoint) |
| 7 | `current_val / operand` | div |
| 9 | `operand - current_val` | rsub |
| 10 | `g_animSharedVarTable[(operand>>1)&0xf] = current_val; return current_val` | write-to-var |
| 11 | `current_val + (operand & rand())` | add random |
| 12 | `current_val % operand` | modulo |

**CERTAIN** : décompilation complète, switch/case exhaustif.

#### ResolveBodyPartTarget @ 0x8003f37c (11 callers)

**Signature** : `void * ResolveBodyPartTarget(uint target_spec, int gameState_ptr)`

Résout un pointeur vers un champ de transform selon `target_spec` :

| Bits de target_spec | Résolution | Retour |
|---------------------|------------|--------|
| bit[5]=0, bit[4]=0 | `charPointers[(target_spec & 0x5)][+0x114]` | translation ptr |
| bit[5]=1, bit[4]=0 | `g_effectObjectPtrs[(target_spec & 0xf)] + 0x3C` | effet ptr |
| bit[4]=1 | `g_renderScratchBuffer[+0x80] + (target_spec & 0xf) * 8` | scratch slot |

#### ResolveBodyPartScale @ 0x8003f404 (7 callers)

**Signature** : `SVECTOR * ResolveBodyPartScale(uint target_spec, int gameState_ptr)`

Résout une SVECTOR pour l'animation scale_set (0x08) :

| Bits de target_spec | Résolution |
|---------------------|------------|
| bit[4]=0, `param_1 < 6` | `charPointers[param_1][+0x11C]` |
| bit[4]=0, `param_1 >= 6` | `SVECTOR_1f800084` (scratchpad HW) |
| bit[4]=1 | `g_renderScratchBuffer + (target_spec & 0xf) * 8` |

### 22.2 Labels Globaux Ajoutés

| Adresse | Label | Usage | Preuve |
|---------|-------|-------|--------|
| 0x801fab0c | `g_charRenderStateBuf` | Table 6×uint32 état rendu par slot char | SetCharRenderState 3 accès exclusifs |
| 0x801fab24 | `g_charSharedVarMaskBuf` | Table 6×uint16 masques OR pour g_animSharedVarTable | SetCharRenderState 1 accès exclusif |
| 0x801faaac | `g_effectObjectPtrs` | Table 16×ptr vers GameState effets | EffSet, FUN_80036bb0, ResolveBodyPartTarget |
| 0x801faa60 | `g_renderFlushFlag` | Flag: mis à 1 si flush OT nécessaire | RenderEntryGroup write, ExecuteAnimStreamBatch read+clear |
| 0x801f2000 | `g_renderScratchBuffer` | Buffer scratch 0x8C48 bytes, zéré chaque frame | bzero 0x8C48 in RenderEntryGroup ligne 52 |

### 22.3 opcode 0x05 `anm_set` — AnimCmd_SetCharRenderState @ 0x80038074

**Format : 4 × uint16 = 8 bytes (return streamPtr + 4)**

```
word[0]: opcode=0x05 | b1_flags<<8
  b1 bits:
    bit[7] = 1 : mode scan 6 slots (boucle sur g_charRenderStateBuf[0..5])
    bit[7] = 0 : mode set direct
      bit[5] = indirect g_animSharedVarTable[b1&0xF]
      bit[4] = mode compteur de frame
      bits[3:0] = slot_index (0..5) ou var_index
word[1]: b0=renderFlags (→ charPointers[slot]->renderFlags), b1+.. = state packed
word[2]: tpage+clut config packed (→ g_charRenderStateBuf[slot])
word[3]: mask_bits (→ g_charSharedVarMaskBuf[slot], ORed into g_animSharedVarTable)
```

**Boucle scan (bit[7]=1)** : itère les 6 slots de `g_charRenderStateBuf`, cherche les flags actifs :
- bit[6]=1 → décrémente compteur d'animation (`-= 0x20`)
- bit[5]=1, bit[4]=0 → attend signal : OR mask_bits dans `g_animSharedVarTable[var_idx]`
- bit[5]=1, bit[4]=1 → attend compteur spécifique, puis OR mask_bits

### 22.4 opcode 0x0E `rgb_set` — AnimCmd_AnimatePolyColorRGBA @ 0x80039754

**Format : 4 × uint16 = 8 bytes (return streamPtr + 4)**

```
word[0]: opcode=0x0E | b1_flags<<8
  b1 bits:
    bits[5:4] = mode: 0x10=search by part_id, 0x20=range, 0x00=direct
    bits[3:0] = ApplyMathOp op_mode (0..12)
    bit[6]    = appliquer STP bit (semi-transparence)
    bit[7]    = appliquer ABR bit (blend mode)
word[1]: b0=part_id, b1=count_polys
word[2]: delta_R (b0) + delta_G (b1)
word[3]: delta_B (b0) + sem_trans_flags (b1)
```

**Action** : applique `ApplyMathOp(current_byte_channel, op_mode, delta_X)` sur chaque byte R/G/B des 4 vertices de chaque POLY_GT4 matchant. Clamp [0..255].

### 22.5 Structure g_renderScratchBuffer @ 0x801f2000 (0x8C48 bytes)

```
0x801f2000 [+0x0000] = g_renderScratchBuffer start (zéré chaque frame par RenderEntryGroup)
0x801f2080 [+0x0080] = body part transform slots (ResolveBodyPartTarget bit[4]=1, stride 8B×16)
0x801f2100 [+0x0100] = g_bodyPartTransformTable (SetBodyPartTransforms_v3, stride 8B×16 SVECTOR)
0x801f7180 [+0x5180] = POLY_GT4_801f7180 : pool POLY_GT4 prims (taille INCONNU)
```

### 22.6 Certitudes mises à jour

| Élément | Confiance | Preuve |
|---------|-----------|--------|
| ApplyMathOp 13 modes (0..0xC) | CERTAIN | switch/case complet décompilé |
| g_animSharedVarTable = uint16[16] | CERTAIN | `(var_idx * 2)` read/write dans tous les opcodes |
| g_charRenderStateBuf = uint32[6] | CERTAIN | iVar6 = 0..5, lecture/écriture directe |
| g_renderScratchBuffer size = 0x8C48 | CERTAIN | bzero(g_renderScratchBuffer, 0x8C48) |
| POLY_GT4 base = 0x801f7180 | CERTAIN | `&POLY_GT4_801f7180` référencé par 8+ opcodes |


---

## 23. Session continuation — Opcodes 0x07/0x08/0x0F et Tâches Effets

### 23.1 opcode 0x07 `rotate_set` — AnimCmd_SetBodyPartTransforms_v2 @ 0x800384e0

**Format : variable (2 + N words, identique v1)**

Même structure de stream que v1 (0x06) mais utilise `ResolveBodyPartScale` au lieu de `ResolveBodyPartTarget`.
Écrit dans les 3 composantes short (SVECTOR.vx/.vy/.vz) d'un slot rotation.

```
word[0]: opcode=0x07 | b1<<8
  b1 = target_spec → ResolveBodyPartScale(target_spec & 0xFF, gameState_ptr)
word[1]: 3 × 5-bit component_specs: bits[14:10][9:5][4:0]
         chaque spec = {op_mode[3:0], indirect_flag[4]}
         op_mode == 0xF → skip (pas de mot inline)
         op_mode == 8   → COPY_MODE: lit depuis un autre slot ResolveBodyPartScale
         indirect_flag  → 1: lire depuis g_animSharedVarTable[*puVar5 & 0xf]
                          0: lire mot inline
optional word[2+]: valeurs inline par composante non-skip
```

**Itération** : 3 passes (iVar6 0..2), avance `pSVar2` par `&pSVar2->vy` (+2B) à chaque passe.

**CERTAIN** : décompilé, `ResolveBodyPartScale` confirmé, itère `pSVar2->vx` puis `vy` puis via avance.

### 23.2 opcode 0x08 `scale_set` — AnimCmd_SetBodyPartTransforms_v3 @ 0x800386a8

**Format : variable (2 + N words, identique v1/v2)**

Utilise `g_bodyPartTransformTable` directement (0x801f2100). Accès par `&g_bodyPartTransformTable + (target_spec & 0xf) * 8`.

```
word[0]: opcode=0x08 | b1<<8
  b1 = target_spec
  si bit[4]=1 → unaff_s4 = &g_bodyPartTransformTable[(target_spec & 0xf) * 8]
word[1]: 3 × 5-bit specs (même format)
optional word[2+]: valeurs inline
```

**COPY mode (op_mode==8)** : si `operand & 0x10` → `unaff_s5 = &g_bodyPartTransformTable[(operand & 0xf) * 8]`, puis avance de `iVar4 * 2` bytes.

**CERTAIN** : décompilé, `g_bodyPartTransformTable` référencé 2 fois dans la fonction.

### 23.3 Comparaison des 3 opcodes SetBodyPartTransforms

| Opcode | Nom debug | Resolver | Cible |
|--------|-----------|----------|-------|
| 0x06 `trans_set` | AnimCmd_SetBodyPartTransforms | ResolveBodyPartTarget | Translation XY (void*) |
| 0x07 `rotate_set` | AnimCmd_SetBodyPartTransforms_v2 | ResolveBodyPartScale | SVECTOR rotation (vx/vy/vz) |
| 0x08 `scale_set` | AnimCmd_SetBodyPartTransforms_v3 | g_bodyPartTransformTable direct | Transform scale table short[3] |

### 23.4 opcode 0x0F `cmp_set` — AnimCmd_ConditionalBranch @ 0x80039028

**Format : 4 × uint16 = 8 bytes (toujours return streamPtr + 4)**  
**Déclenche si g_pauseFlag & 1 → return sans action**

```
word[0]: opcode=0x0F | compare_mode<<8
  compare_mode = (uint8)(*streamPtr >> 8) & 0xFF
  valide : 0..5
word[1]: packed indices vars
  bits[3:0]  = var_A_idx (index dans g_animSharedVarTable)
  bits[7:4]  = var_B_idx (idem)
  bits[15:8] = dest_var_idx (index de la var destination si branche prise)
word[2]: branch_value = valeur à ORer dans g_animSharedVarTable[dest_var_idx] si branche prise
word[3]: const_offset = constante ajoutée à g_animSharedVarTable[var_B_idx]
```

**Modes de comparaison** :

| Mode | Condition de branche | Mnemonic |
|------|---------------------|---------|
| 0 | `var[A] != var[B] + offset` | NEQ |
| 1 | `var[A] == var[B] + offset` | EQ |
| 2 | `var[A] <= var[B] + offset` | LEQ (NOT GT) |
| 3 | `var[A] < var[B] + offset` | LT |
| 4 | `var[B] + offset <= var[A]` | GEQ |
| 5 | `var[B] + offset < var[A]` | GT |

**Action si branche prise** : `g_animSharedVarTable[dest_var_idx] |= branch_value`  
Ce n'est PAS un saut d'adresse — c'est un OU conditionnel dans le pool de variables partagées.  
Les autres opcodes lisent ces bits via `indirect_flag=1` (mode g_animSharedVarTable).

**CERTAIN** : switch/case complet décompilé, 6 comparateurs, action OR confirmée.

### 23.5 Fonctions Renommées

| Ancienne | Nouvelle | Signature | Callers |
|----------|----------|-----------|---------|
| FUN_8003ffec | `SpawnEffectTask` | `Task *(undefined2 *animDataPtr, ushort effectIndex)` | 1 |
| FUN_80053d44 | `InitEntityAnimPtr` | `void(GameState *gameState, int animTableBase, uint animIndex)` | 7 |

#### SpawnEffectTask @ 0x8003ffec

- Crée une tâche via `CreateTask(FUN_8003fddc, 0, 0xB, 0x5C, 0, g_taskListTails[0xB])`  
  → task list 0xB, data size 0x5C bytes
- Copie 3 × undefined2 de `animDataPtr` dans `gameState->entityData.runtimePointers`
- Appelle `InitEntityAnimPtr(gameState, -0x7ffde77c, effectIndex)`  
  → `0x80021884` = table globale d'animations
- Renvoit Task* (NULL si création échoue)

**CERTAIN** : décompilé, 1 caller (AnimCmd_EffSet @ 0x8003d018 ligne 41)

#### InitEntityAnimPtr @ 0x80053d44

- Reset `gameState->polyFt4 = 0`
- Si `animTableBase >= 0` : `base = &gameState->polyFt3->tag + animTableBase`
- Sinon : `base = animTableBase` (utilisé directement comme adresse absolue)
- `ptr = *(base + animIndex * 4)` → pointeur dans table
- Si `ptr < 0x80000000` : corrige depuis `gameState->polyFt3` (offset relatif)
- Stocke résultat dans `gameState->polyGt3`

**Champ gameState->polyGt3** = pointeur vers AnimStream bytecode actif de l'entité.  
**Constante 0x80021884** = table globale (utilisée par AnimCmd_EffSet et SpawnEffectTask).

**CERTAIN** : décompilé, appel à 0x80021884 vérifié pour 5 des 7 callers.

---

## 24. Session continuation — EffectTaskMainLoop, AsyncLoadTexture, UVEntry, SpriteRecord

### 24.1 EffectTaskMainLoop @ 0x8003fddc (renamed)

Boucle principale d'une tâche d'effet créée par `SpawnEffectTask`. Appelée chaque frame par le scheduler.

```
1. ProcessEntityScript(gameState)             si !g_pauseFlag
2. Si runtimePointers.polyF3Index & 4 == 0 → render l'effet:
     - polyF4Index == 4 : scaleX/Y = DAT_8009ac5c[0/1] (camera scale)
     - sinon            : scaleX/Y = 0x1000 (1.0 fixed-point 12.0)
     - posX = runtimePointers.dataPtr2[0] - INT_1f8000b4 (screen offset X)
     - posY = runtimePointers.dataPtr2[2]
     - posZ = runtimePointers.polyFt3Index - INT_1f8000bc (screen offset Z)
     - spriteList = charPointers[4]
   → RenderTransformedSprites(spriteList, posX, posY, posZ, ...)
3. State machine sur runtimePointers.polyF3Index :
     0          → attendre polyFt4==0, puis zéro polyG3Index
     bit[0]=0, bit[1]=0 → si bit[5]=1 : looping (recharge polyFt4)
     autre      → RemoveTaskFromList(task, 0xB) = autodestruction
```

**CERTAIN** : décompilé, 1 caller (SpawnEffectTask via CreateTask).

### 24.2 opcode 0x0B `tex_set` — AnimCmd_AsyncLoadTexture @ 0x80038d24

Gestion asynchrone du chargement de texture VRAM via une table de 4 slots `ImageLoadRequest_ARRAY_800a67b0`.

**Deux sous-modes selon b1.bit[7]** :

#### Mode Poll/Update (b1.bit[7] = 0) — return streamPtr+1

```
word[0]: opcode=0x0B | b1<<8
  b1 bits[1:0] = slot_index (0..3)
  b1 bit[7]    = 0 → poll mode

word[0] bits[14:12] (= uVar1 & 0x7000):
  ==0 : tester uniquement si load fini
  !=0 : si chargé (field6.bits[6:4]==0) et compteur>0 → décrémente field3, met field6.bits[6:4]
```

#### Mode Init (b1.bit[7] = 1) — return streamPtr+7

```
word[0]: opcode=0x0B | (0x80|idx_flags)<<8
word[1]: cd_buffer_index → dataptr = &g_cdFileBufferTable[cd_buffer_index]
word[2]: (réservé — non utilisé)
word[3]: x → ImageLoadRequest.x (position X VRAM)
word[4]: y → ImageLoadRequest.y (position Y VRAM)
word[5]: {field3_0x8(byte), field4_0x9(byte)}
word[6]: {field5_0xa(byte), field6_0xb_init(byte)}
```

**Structure ImageLoadRequest** (stride 0xC = 12 bytes) :

| Offset | Taille | Nom Ghidra | Rôle |
|--------|--------|-----------|------|
| +0x00 | 4 | `dataptr` | Pointeur vers données texture dans RAM |
| +0x04 | 2 | `x` | Position X en VRAM cible |
| +0x06 | 2 | `y` | Position Y en VRAM cible |
| +0x08 | 1 | `field3_0x8` | Compteur de chargement (décrémenté) |
| +0x09 | 1 | `field4_0x9` | Paramètre taille (width/height packed) |
| +0x0A | 1 | `field5_0xa` | Paramètre taille (width/height packed) |
| +0x0B | 1 | `field6_0xb` | Flags état : bits[6:4]=status, autres=flags |

`FUN_80067588` = fonction de progression/polling d'un `ImageLoadRequest` (1 caller depuis AsyncLoadTexture).

**CERTAIN** : décompilé, positions x/y directement assignées, stride array = 4 slots (index & 3).

### 24.3 CHBinMeshEntry.unknown_08 — INCONNU

Preuve insuffisante :
- `RenderBattleScene3D` n'accède jamais `local_38[1]` (= offset +0x08 de la struct)
- `InitBattleStageAssets` utilise des `MeshTableEntry` (STG_MD), pas CHBinMeshEntry
- Aucun autre caller de `g_chBinEntryTableBasePtr` trouvé

**Actions recommandées** :
1. Chercher la fonction qui *charge* le CH_BIN (parser initial, non `RenderBattleScene3D`)
2. Chercher des accès à `g_meshTableCounts` dans d'autres fonctions

### 24.4 UVEntry — Structure 6B confirmée

De `RenderBattleScene3D` lignes 241-253, accès `uv_idx[vi] * 6 + iVar14` (base = résolution de `local_c8[1]`) :

```c
struct UVEntry {      // 6B CERTAIN (stride 6 confirmé)
    undefined2 uv_lo;  // (u, v) bytes 0-1 → positionné dans prim POLY_GT4 champ UV lo
    undefined2 uv_mid; // bytes 2-3 → champ UV mid (CLUT area?)
    undefined2 uv_hi;  // bytes 4-5 → champ UV hi (tpage area?)
};
```

Les 3 fields correspondent aux positions dans le layout PSX `POLY_GT4` (u, v = 1B chacun + 2B CLUT = 4B, puis u+v = 2B, puis tpage = 2B). Noms exacts PROBABLE — nécessite corrélation avec SetPolyGT4 pour confirmer (u/v/clut/tpage exact).

**Accès confirmé** : chaque vertex `vi` charge `UVEntry[uv_idx[vi]]`, 3 writes dans prim GT4.

### 24.5 SpriteRecord.ptr_sprite — PROBABLE = pointeur vers données couleur de vertex

De `RenderBattleScene3D` lignes 192-295 :

```c
local_c0 = (int *)(local_38[4] + g_cdFileBaseOffset)  // = ptr_sprite_recs
local_bc = (int *)(*local_c0 + g_cdFileBaseOffset)     // SpriteRecord.ptr_sprite
local_c0[1]                                            // SpriteRecord.cnt_packed
```

`local_bc` est itéré par `IterateMeshStreamAndFetch_Offset8()` et sert à suply les canaux R,G,B dans les vertices `POLY_GT4` (lignes 278-293). Stride = +8B (2 × int32) par polygone.

Comportement :
```c
local_f0 = (u_char)(short)*local_bc;          // R channel
local_ee = (u_char)*((int)local_bc + 2);       // G channel
local_ec = local_f0 + local_ee;                // R+G blended
local_ea = local_ee + (u_char)*((int)local_bc + 6); // G+B blended
local_bc = local_bc + 2;  // stride +8B
```

**Renommage suggéré** : `ptr_sprite` → `ptr_vertex_color_stream` (**PROBABLE**, non confirmé sans analyse de la section dans CH_01.BIN à offset réel).

**INCONNU** : pourquoi "SpriteRecord" — le nom `ptr_sprite` provient d'une hypothèse initiale. La structure pointe sur des données RGB, pas une image sprite au sens classique.

### 24.6 Bilan des Accès CHBinMeshEntry dans RenderBattleScene3D

```c
// local_38 = local_98 + 1 (CHBinMeshEntry + 0x04)
// ---
*local_38          // +0x04 poly_count_packed.low16 → compteur boucle polygones (CERTAIN)
local_38[1]        // +0x08 unknown_08 → NON LU dans cette fonction (INCONNU)
local_38[2]        // +0x0C ptr_color_table → local_d0 = résolution runtime (CERTAIN)
local_38[3]        // +0x10 ptr_mesh_records → local_c8 = résolution runtime (CERTAIN)
local_38[4]        // +0x14 ptr_sprite_recs → local_c0 = résolution runtime (CERTAIN)
local_38[5]        // +0x18 ptr_anim_stream → animstream ptr ou 0 (CERTAIN)
```

---

## 25. Session continuation — Validation CHBinMeshEntry.unknown_08, AnimCmd_RenderEntryGroup, données binaires

### 25.1 CHBinMeshEntry.unknown_08 — INCONNU (double confirmation)

Vérifié dans **2 fonctions** qui itèrent les entrées :
- `RenderBattleScene3D` (332 lignes) : `puVar12[1]` (= +0x08) jamais lu
- `AnimCmd_RenderEntryGroup` (250 lignes, ligne 67: `puVar12 = local_98 + 1`) : même pattern, +0x08 jamais lu

**Valeurs brutes dans CH_01.BIN** (file offset 0x1244, entries 0..9) :

| Entrée | +0x00 (id_packed) | +0x04 (poly_count) | +0x08 (INCONNU) | ptr_anim_stream |
|--------|------------------|--------------------|-----------------|-----------------|
| E0 | 0x00000000 | 0x00000000 | 0x00000000 | 0x801A3EE8 |
| E1 | 0x00000000 | 0x00000000 | 0x00000000 | 0x801A459C |
| E2 | 0x00000000 | 0x00000000 | 0x00000000 | 0x801A4630 |
| E3 | 0x00000100 | 0x00010001 | 0x00010001 | 0x801A4654 |
| E4 | 0x00000200 | 0x00010006 | 0x00010001 | 0x801A4814 |
| E5 | 0x00000300 | 0x00010010 | 0x00010001 | 0x00000000 |
| E6 | 0x00180400 | 0x00010008 | 0x00010001 | 0x801A4914 |
| E7 | 0x00000500 | 0x00010001 | 0x00010001 | 0x801A4990 |
| E9 | 0x00080700 | 0x00000001 | 0x00000000 | 0x801A485C |

**Observations** :
- Entrées "header" (E0..E2) : tout = 0 sauf les ptrs
- Entrées actives (E3..E8) : `unknown_08` = 0x00010001 systématiquement
- E9 (part_id=7) : `unknown_08` = 0, cohérent avec poly_count high16 = 0 aussi
- Pattern low16=1, high16=1 → PROBABLE = sous-compte (sprite count + mesh sub-count)

**INCONNU** — Aucune preuve code directe. Hypothèse : utilisé par un chargeur non encore trouvé.

### 25.2 AnimCmd_RenderEntryGroup — Découverte groupIndex

`AnimCmd_RenderEntryGroup` a un paramètre `groupIndex` (short) qui sélectionne un groupe dans le stream AnimStream quand `ptr_anim_stream != 0`. Ligne 97-102 :
```c
// Parcours des groupes dans g_meshStreamPtrBuffer jusqu'à trouver groupIndex
if ((uVar3 & 2) != 0) {
    puVar11 = &g_meshStreamPtrBuffer;
    uVar22 = 0; uVar7 = 0;
    do {
        if ((int)groupIndex != uVar7) {
            if (*puVar11 == 0) break;
            puVar11++;
        }
        uVar7++;
    } while (uVar22++ < 0x10);  // max 16 groupes
}
```

**CERTAIN** : le stream AnimStream supporte jusqu'à 16 groupes par entrée, indexés par `groupIndex`.

### 25.3 CHBinMeshEntry — Accès confirmés (synthèse finale)

`local_98 = entry_ptr`, `puVar12 = local_98 + 1` dans les deux fonctions :

```
*local_98 = id_packed (+0x00)    → CERTAIN (high16=flags, low16=id utilisé en render)
puVar12[0] = poly_count (+0x04)  → CERTAIN (boucle while local_a8 < (short)*puVar12)
puVar12[1] = unknown (+0x08)     → INCONNU (jamais lu dans les 2 fonctions de rendu)
puVar12[2] = ptr_color_table (+0x0C) → CERTAIN (relocalisé +0x2E800, utilisé comme vertex_stream)
puVar12[3] = ptr_mesh_records (+0x10) → CERTAIN (relocalisé, UV table + vertex coord streams)
puVar12[4] = ptr_sprite_recs (+0x14)  → CERTAIN (relocalisé, vertex color / sprite data)
puVar12[5] = ptr_anim_stream (+0x18)  → CERTAIN (0=absent, sinon: AnimStream bytecode, multi-group)
```

### 25.4 Découverte sur ptr_color_table (MeshRecord)

`local_d0 = ptr_color_table` résolu. Usage :
- `*local_d0` = ptr vers buffer vertex data (résolu +0x2E800)
- `local_d0[1]` = packed count : `high16 = outer_count - 1` (compteur decremented), `low16 = inner_count`
- `local_d0[2]` = ptr vers 2ème section vertex (chargé quand compteur outer épuisé)
- `local_d0[3]` = packed count 2ème section

**Structure réelle de ptr_color_table** = liste chaînée de blocs vertex (multiple segments) :
```c
struct ColorTableBlock {   // stride variable
    uint ptr_vertices;     // → data vertices
    uint cnt_packed;       // high16 = outer_count-1, low16 = inner_count
    // repeat...
};
```

**PROBABLE** — pattern identique dans RenderBattleScene3D et AnimCmd_RenderEntryGroup.

---

## 26. Session continuation — CH_BIN Sections, SetMeshPaletteRange, ApplyCharEffect

### 26.1 CH_BIN Header Sections (CH_01.BIN)

4 pointeurs dans l'en-tête (dwords[2..5]), tous rebasés +0x2E800 :

| dword | Addr compile | File offset | Contenu | Statut |
|-------|-------------|-------------|---------|--------|
| [2] | 0x801A4A44 | 0x1244 | CHBinMeshEntry table (37 entries, stride 28B) | CERTAIN |
| [3] | 0x801A4E50 | 0x1650 | CLUT 16 couleurs PSX BGR555 (32 bytes) | CERTAIN |
| [4] | 0x801A4E70 | 0x1670 | INCONNU (structured data, dword[0]=0x00482279) | INCONNU |
| [5] | 0x801A8098 | 0x4898 | INCONNU (starts: 04 00 02 00, count fields?) | INCONNU |

**CLUT @ 0x1650** — 16 entries PSX BGR555 (little-endian), symétrique gradient :
```
Entry  0 = 0x0000 (transparent)
Entry  1 = 0x6EF7 (dark)
Entry  2..4 = gradient light
Entry  5 = 0x7FFF (pure white)
Entry  6..9 = symmetric gradient back to transparent
Entry 10 = 0xCBDE (highlight)
Entry 11..14 = gradient dark
Entry 15 = 0x0000 (transparent)
```

Labels Ghidra ajoutés :
- `DAT_801d200c` → `g_chBinClutTablePtr`
- `DAT_DOT801d200c` — voir XREF relocation RenderBattleScene3D

### 26.2 opcode 0x10 `pal_set` — AnimCmd_SetMeshPaletteRange @ 0x80038874

**Format : 4 × uint16 = 8 bytes (return streamPtr+4)**

```
word[0]: opcode=0x10 | b1<<8
  b1 = flags (cVar1):
    bits[3:0] = mesh_group_id → cherche mesh correspondant dans g_renderMetadataBuffer
    bit[4]    = indirect word[1] (g_animSharedVarTable)
    bit[6]    = indirect word[2] (g_animSharedVarTable)
    bit[7]    = absolute_mode → copie raw local_d8 matrix sans CompositionMatrix

word[1]: range_count (nombre de slots OT depth à modifier) | ou var_idx
word[2]: depth_delta (ajouté à g_polyOTDepthTable chaque itération) | ou var_idx
word[3]: target_specs packed (3 cibles, shift progressif 6+5+5 bits):
  bits[ 5:0] = spec_0 → ResolveBodyPartTarget(spec_0 & 0x3F) = transVec
  bits[10:6] = spec_1 → ResolveBodyPartScale(spec_1 & 0x1F)  = rotVec
  bits[15:11] = spec_2 → g_bodyPartTransformTable[(spec_2&0xF)*8] = scaleVec
```

**Action** : Trouve la mesh par group_id dans `g_renderMetadataBuffer`, puis appelle `TransformAndProjectMesh` pour projeter les polygones en 3D, ensuite incrémente OT depths de `depth_delta` pour `range_count` polygones.

**CERTAIN** : décompilé, switch sur iVar4=0/1/2 avec 3 resolvers différents.

### 26.3 TransformAndProjectMesh @ 0x8003f814 (renommée, 1 caller)

**Signature** :
```c
void TransformAndProjectMesh(
    int polyBuf,        // &POLY_GT4_801f7180[idx] = target primitive
    SVECTOR *uvBuf,     // &g_uvOrTexCoordBuffer[idx*16] = UV data
    SVECTOR *rotVec,    // body part rotation SVECTOR
    short *transVec,    // body part translation short[3]
    short *scaleVec,    // body part scale short[3]
    int otDepthPtr,     // &g_polyOTDepthTable[idx] = OT depth
    ushort polyCount,   // nombre de polygones à projeter
    short absoluteMode  // 0=CompMatrix, 1=raw local_d8
)
```

**Opérations GTE** :
1. `PushMatrix / ReadRotMatrix` → sauvegarde matrice courante
2. `RotMatrix(rotVec, &local_d8)` → construction matrice rotation body part
3. `local_d8.t = transVec - screen_offset` → translation relative à camera
4. `ScaleMatrix(&local_d8, scaleVec)` → échelle body part
5. `CompMatrix(&global, &local_d8, &result)` → composition avec matrice globale
6. Si `absoluteMode=1` → diagonal identity override sur result
7. `SetRotMatrix / SetTransMatrix` → charge dans GTE
8. Boucle sur polyCount polygones : `RotAverage4` (POLY_GT4) ou `RotAverage3` (POLY_GT3)

**CERTAIN** : décompilé, appels GTE directs.

### 26.4 opcode 0x11 `eff_xset` — AnimCmd_ApplyCharEffect @ 0x80038eb0

**Format bi-modal** :

#### Mode init (bit[15]=0) : return streamPtr+6

```
word[0]: opcode=0x11 | flags<<8
  bit[15]=0 → init mode, charger les paramètres
  bit[8]   = indirect stream[2]
  bit[9]   = indirect stream[3]
  bit[10]  = indirect stream[4]
word[1]: {lo_byte=target_spec, hi_byte=scale_spec}
  → g_charEffectTranslatePtr = ResolveBodyPartTarget(target_spec & 0xFF)
  → g_charEffectScalePtr     = ResolveBodyPartScale(scale_spec & 0xFF)
word[2]: rotation_x (ou var_idx si bit[8])→ DAT_800a6780
word[3]: rotation_y (ou var_idx si bit[9])→ DAT_800a6782
word[4]: rotation_z (ou var_idx si bit[10]) → DAT_800a6784
→ g_charEffectInitFlag = 1
```

#### Mode execute (bit[15]=1) : return streamPtr+1

```
word[0]: opcode=0x11 | 0x80<<8
  uniquement : appel UpdateCharEffectTransform()
```

#### UpdateCharEffectTransform @ 0x8003fae8 (renommée)

Système d'interpolation de position/rotation/échelle sur N frames :

```
Si g_charEffectInitFlag=1 (init) :
  → calcule delta_pos = target_translate - scratchpad_pos
  → calcule delta_rot = target_rot - SVECTOR_1f800084.vy  
  → stocke en DAT_800a6794/96/98 (velocity XYZ)
  → g_charEffectInitFlag = 0

Si g_charEffectInitFlag=0 (update) :
  → incrémente DAT_800a6788/78c/790 (pos accum) par velocity
  → écrit scratchpad_coordX/Y/Z = pos_accum >> 4 (fixed-point 4.12)
  → incrémente rotation accumulator par velocity
  → écrit SVECTOR_1f800084.vy/vx (scratchpad rotation)
  → décrémente DAT_800a679a (frame counter)
  → retourne 1 si counter == 0 (effet terminé)
```

**Labels ajoutés** :

| Adresse | Label | Rôle |
|---------|-------|------|
| 0x800a6778 | `g_charEffectTranslatePtr` | Ptr vers translation target short[3] |
| 0x800a677c | `g_charEffectScalePtr` | Ptr vers SVECTOR scale target |
| 0x800a6786 | `g_charEffectInitFlag` | 0=update mode, 1=init mode |

### 26.5 Fonctions renommées cette session

| Ancienne | Nouvelle | Signature courte | Callers |
|----------|----------|-----------------|---------|
| FUN_8003fddc | `EffectTaskMainLoop` | `void()` | CreateTask (via SpawnEffectTask) |
| FUN_8003f814 | `TransformAndProjectMesh` | `void(int,SVECTOR*,SVECTOR*,short*,short*,int,ushort,short)` | 1 |
| FUN_8003fae8 | `UpdateCharEffectTransform` | `undefined4()` | 1 |

---

## Section 27 — AnimCmd : opcodes de mouvement, RGBA, UV/XY, contrôle de flux, hitbox

### 27.1 Nouveau label : g_charEffectSlotTable

| Adresse | Label | Type | Rôle |
|---------|-------|------|------|
| 0x801fab30 | `g_charEffectSlotTable` | uint32[16] | Table des slots d'effet par personnage |

**Preuve** : lu/écrit par `AnimCmd_ChEffSet` (3 refs @ 0x8003e070, 0x8003e0c8, 0x8003e134) et `AnimCmd_CheffWait` (1 ref @ 0x8003ec24). Indices actifs = 4..15 (12 slots).

---

### 27.2 AnimCmd_MoveSet — `move_set` (opcode 0x1A @ 0x8003c738, 1032 octets)

**Format** : 4 mots

```
word[0]: opcode=0x1A | sub_mode<<8
word[1]: lo_byte=target_spec_A (corps cible)
         hi_byte=flags (bit[5]=snap-to-goal, bit[6]=varTable indirect)
word[2]: lo_byte=target_spec_B (position source/goal)
         hi_byte=varTable_dest_idx (bit[6]=indirect)
word[3]: completion_bitmask (ORé dans varTable quand tous axes atteints)
```

**Logique** (CERTAIN) :

```c
// Résolution des cibles
psVar5 = ResolveBodyPartTarget(target_A, gameState);  // cible à déplacer
psVar6 = ResolveBodyPartTarget(src_B, gameState);     // position goal
psVar7 = ResolveBodyPartTarget(speed_spec, gameState); // vitesse par axe

// Axes X, Y, Z (3 fois la même pattern)
// Utilise DAT_801faa84 (dX), DAT_801faa88 (dY), DAT_801faa8c (dZ)
// comme deltas de mouvement par frame
if (A.x != goal.x && delta != 0):
    A.x += delta
    if signe_change(goal.x - speed.x ^ goal.x - (A.x+delta)):
        A.x = goal.x; flag |= bit  // overshoot snap

// Résultat dans g_animSharedVarTable[varDest]:
// si flag == 7 (tous axes OK): OR completion_mask
// flag bits: 0=X atteint, 1=Y atteint, 2=Z atteint
```

**Note** : `DAT_801faa84/88/8c` = deltas de mouvement global (short×3 = SVECTOR[0..2]).
PROBABLE → vec3 de vitesse de déplacement courant du personnage.

**Nouveau label identifié** :

| Adresse | Label | Type |
|---------|-------|------|
| 0x801faa84 | `g_charMoveVelocity` | short[3] (X, Y, Z per-frame delta) |

---

### 27.3 AnimCmd_Xy0123Set — `xy_set` (opcode 0x22 @ 0x8003d580, 1352 octets)

**Format** : 5+ mots

```
word[0]: opcode=0x22 | mode<<8 (bits[5:4] = range_mode)
word[1]: lo_byte=start_idx, hi_byte=count_or_part_id
word[2]: packed (3 × 5 bits) = [x0_spec:5][y0_spec:5][x1_spec:5]  [bit15=sign]
word[3]: packed (3 × 5 bits) = [y1_spec:5][x2_spec:5][y2_spec:5]
word[4]: packed (2 × 5 bits) = [x3_spec:5][y3_spec:5]
+ suite: opérandes inline (selon specs)
```

**Chaque spec 5 bits** :
- bits[3:0] = index dans varTable (0xF = skip/no-op)
- bit[4] = si 1 → lire depuis `g_animSharedVarTable[*puVar16++]`, si 0 → lire depuis stream inline

**Range modes** (bits[5:4] de word[0]>>8) :

| bVar6 | Mode |
|-------|------|
| 0x10 | Range par index mesh_count (scan g_renderMetadataBuffer byte[3]→POLY index) |
| 0x00 | Direct range [start_idx .. start_idx+count] dans POLY_GT4 pool |
| 0x20 | Search par group_id : scan g_renderMetadataBuffer byte[2]==part_id |

**Boucle interne** (CERTAIN) :

```c
// psVar12 = &POLY_GT4->x0  (base, puis +6 shorts = +12 bytes par vertex)
// 4 itérations (x0,y0), (x1,y1), (x2,y2), (x3,y3)
for v in 0..3:
    x_new = ApplyMathOp(psVar12[v*6+0], x_mode[v], x_operand[v])
    y_new = ApplyMathOp(psVar12[v*6+1], y_mode[v], y_operand[v])
// stride inter-polygone: +8 shorts = +16 bytes après boucle de 4 vertices
```

**Layout POLY_GT4 relatif (confirmé)** :

| Offset (shorts) | Champ | Note |
|-----------------|-------|------|
| 0 | tag/len | |
| 1 | r0,g0,b0,code | 4 bytes |
| 3 | x0 | vertex 0 |
| 4 | y0 | |
| 5 | u0,v0 (bytes) + clut | |
| 6 | r1... | → stride = 6 shorts (12B) par vertex depuis x0 |

---

### 27.4 AnimCmd_Rgb2Set — `rgb2_set` (opcode 0x1C @ 0x8003a300, 1304 octets)

**Format** : 4-5 mots

```
word[0]: opcode=0x1C | flags<<8  (bits[5:4]=range_mode, bits[7:6]=reserved)
word[1]: lo_byte=start_idx (ou varTable[idx] si bit[lo_byte_flags&0x40])
         hi_byte=count
word[2]: packed modes RGB (3 × 5 bits) :
         bits[4:0]=r_spec, bits[9:5]=g_spec, bits[14:10]=b_spec
         (0xF = skip channel)
         bit[15]=mode_flag (si 1: word[3] is extra params block)
word[3]: extra operands (si bit[15] dans word[2])
+ word[4] si uVar10==2: operand overflow
```

**Chaque spec 5 bits** :
- bits[3:0] = index dans varTable (0xF = skip)
- bit[4] = indirect depuis varTable

**Délégation** (CERTAIN) : appelle `ApplyRgbaPerVertex(0x8003f464)` :

```c
ApplyRgbaPerVertex(
    bVar4 & 0xf,     // vertex_skip_mask (bits individuels par vertex)
    poly_count,       // nombre de polygones
    &POLY_GT4_pool[offset],
    r_mode, r_operand,
    g_mode, g_operand,
    b_mode, b_operand,
    stp_mode, stp_operand
)
```

**Range modes** :

| bVar6 | Mode |
|-------|------|
| 0x10 | Range [start..start+count] via g_meshCountBuffer (par mesh) |
| 0x00 | Direct range |
| 0x20 | Search par group_id dans g_renderMetadataBuffer byte[2] |

---

### 27.5 ApplyRgbaPerVertex — `FUN_8003f464` (renommée, 560 octets)

**Signature** :
```c
void ApplyRgbaPerVertex(
    ushort vertex_skip_mask,   // bit N = skip vertex N
    ushort poly_count,
    byte  *prim_ptr,           // ptr vers POLY_GT4 (byte*)
    short r_mode, short r_operand,
    short g_mode, short g_operand,
    short b_mode, short b_operand,
    short stp_mode, short stp_operand
)
```

**Logique par vertex** (CERTAIN) :

```c
for poly in 0..poly_count:
    prim_ptr += 4  // sauter tag/len (4 bytes)
    for v in 0..3:
        if (vertex_skip_mask & (1<<v)) == 0:
            r = ApplyMathOp(prim_ptr[0], r_mode, r_operand)  // clamp [0..255]
            g = ApplyMathOp(prim_ptr[1], g_mode, g_operand)
            b = ApplyMathOp(prim_ptr[2], b_mode, b_operand)
            stp_bits = ApplyMathOp(prim_ptr[3], stp_mode, stp_operand) & 3
            prim_ptr[3] = (prim_ptr[3] & 0xFC) | stp_bits
        prim_ptr += 4  // stride = 4 bytes par vertex RGBA
```

**Note** : `prim_ptr[3]` = code byte du vertex POLY_GT4 ; les 2 bits low = STP flag.

---

### 27.6 AnimCmd_EffSet — `eff_set` (opcode 0x21 @ 0x8003cf38, 720 octets)

**Format** :

```
Mode spawn (bit[7]=0 dans lo_byte word[0]):
    word[0]: opcode=0x21 | lo_flags<<8
    word[1]: lo_byte=effectIndex, hi_byte=body_part_spec
    word[2]: extra config (mis dans gameState+0x52)
    → ret param_1 + 3

Mode kill/stop (bit[7]=1):
    word[0]: opcode=0x21 | mode_flags<<8
    → ret param_1 + 1
```

**Logique mode spawn** (CERTAIN) :

```c
// Slot d'effet : g_effectObjectPtrs[opcode_lo & 0xf]
if (opcode_lo & 0x10):
    slot_idx = g_animSharedVarTable[(opcode_lo & 0xf)] & 0xf
else:
    slot_idx = opcode_lo & 0xf

if g_effectObjectPtrs[slot_idx] == 0:
    // Créer nouvel effet
    animDataPtr = ResolveBodyPartTarget(word[1].hi_byte, gameState)
    task = SpawnEffectTask(animDataPtr, effectIndex)
    g_effectObjectPtrs[slot_idx] = task->gameState
    gameState->entityData.polyF3Index |= (hi_flags & 0x20)  // mode rendu
    g_effectObjectPtrs[slot_idx]+0x58 = &g_effectObjectPtrs[slot_idx]  // back-ptr
else if hi_flags & 0x40:
    // Réinitialiser animation
    InitEntityAnimPtr(existingGameState, -0x7ffde77c, effectIndex)
```

**Mode kill** (bit[7]=1) :

| Bit | Action |
|-----|--------|
| bit[6] in lo_byte | `slot[idx].+0x50 |= 4` (flag kill type A) |
| bit[5] in hi_byte | `slot[idx].+0x50 |= 2` ; `g_effectObjectPtrs[idx] = 0` (kill B) |
| bit[4] in hi_byte | `slot[idx].+0x50 |= 1` ; `g_effectObjectPtrs[idx] = 0` (kill C) |

---

### 27.7 AnimCmd_BitSet — `bit_set` (opcode 0x0D @ 0x8003b148, 280 octets)

**Format** : 3 mots

```
word[0]: opcode=0x0D | op_mode<<8
word[1]: dest_var_idx  (→ g_animSharedVarTable + idx*2)
word[2]: operand  (ou g_animSharedVarTable[word[2]] si bit[4] dans lo_byte word[0])
```

**Logique** (CERTAIN) :

```c
g_animSharedVarTable[dest] = ApplyMathOp(g_animSharedVarTable[dest], op_mode, operand)
// Cas spécial op_mode==8: g_animSharedVarTable[dest] = g_animSharedVarTable[operand]  (COPY)
```

Opération mathématique directe sur la table de variables partagées.

---

### 27.8 AnimCmd_BitChk — `bit_chk` (opcode 0x0C @ 0x8003afa4, 420 octets)

**Format** : 2-4 mots (selon mode)

```
word[0]: opcode=0x0C | flags<<8 | lo_byte
         lo_byte bits[5:4] = check mode
         lo_byte bits[3:0] = varTable_src_idx
         lo_byte bit[4] = invert test (NOT)
         lo_byte bit[5] = AND-all vs ANY
         lo_byte bits[7:6] = 0x80 → word[2] = extra stream ptr idx
word[1]: bitmask à tester
hi_byte (word[0]>>8): action_mode
         bits[7:6] = action type
```

**Test** :

```c
val = g_animSharedVarTable[src_idx]
if (lo_byte & 0x10): val = ~val
if (lo_byte & 0x20): pass = (val & mask) == mask  // ALL bits
else:                pass = (val & mask) != 0       // ANY bit
```

**Actions si test réussi** :

| hi_byte bits[7:6] | Action |
|-------------------|--------|
| 0x00 | `g_meshOffsetBuffer[param_2] = 1` + retour au début stream (`g_meshStreamPtrBuffer[arg] - 4`) |
| 0x40 | Scan forward jusqu'à `*ptr == 0` (null sentinel) |
| 0x80 | `g_meshOffsetBuffer = 1` + jump vers stream depuis `g_cdFileBufferTable[word[2]]` |

**Note** : `param_2` est l'index de mesh courante (passé par l'exécuteur de stream).

---

### 27.9 AnimCmd_IfSet — `if_set` (opcode 0x23 @ 0x8003d450, 304 octets)

**Format** : 3 mots (+ scan forward variable)

```
word[0]: opcode=0x23 | tag12 (bits[11:0]) | bits[15:14]
word[1]: hi_byte=varTable_src_idx, lo_byte flags:
         bit[4] = NOT/invert
         bit[5] = AND-all vs ANY
word[2]: bitmask à tester
```

**Logique** (CERTAIN) :

```c
val = g_animSharedVarTable[word[1]>>8]
if (word[1] & 0x10): val = ~val
if (word[1] & 0x20): fail = (val & mask) != mask
else:                fail = (val & mask) == 0

if fail:
    // Scan stream forward jusqu'à trouver mot avec bits[11:0] == tag12
    while (*ptr & 0xfff) != (opcode & 0xfff): ptr++
    // ptr pointe sur le marker de fin correspondant
```

**Mode spécial** (bits[15:14] == 0x4000) : scan jusqu'à sentinelle signée `((tag-0x8000)*2)`.

**Usage** : saut conditionnel vers bloc END correspondant (identifié par tag 12 bits commun).

---

### 27.10 AnimCmd_EndSet — `end_set` (opcode 0x0E @ 0x8003b260, 120 octets)

**Format** : 1 mot

**Logique** (CERTAIN) :

```c
if (!g_pauseFlag):
    g_meshStreamPtrBuffer[mesh_slot] = 0         // reset ptr stream
    g_meshOffsetBuffer[mesh_slot] = 1            // marquer terminé
    return param_1 + 2
else:  // pause → loop back au début
    return g_meshStreamPtrBuffer[mesh_slot * 2] - 4  // revisite début stream
```

**Globals utilisés** :

| Adresse | Label | Rôle |
|---------|-------|------|
| 0x801faa00 | `g_meshStreamPtrBuffer` | uint32[] ptr courant du stream par mesh |
| 0x801faa40 | `g_meshOffsetBuffer` | uint16[] flag "stream terminé" par mesh |

---

### 27.11 AnimCmd_ObjIntGet — `obj_int_get` (opcode 0x11 @ 0x8003ad38, 412 octets)

**Format** : 2 mots

```
word[0]: opcode=0x11 | part_A_spec<<8
word[1]: lo_byte=part_B_spec, hi_byte=dest_varTable_idx
```

**Logique** (CERTAIN) :

```c
posA = ResolveBodyPartTarget(part_A, gameState)  // short[3] = X,Y,Z
posB = ResolveBodyPartTarget(part_B, gameState)
dX = posA[0] - posB[0]
dY = posA[1] - posB[1]
dZ = posA[2] - posB[2]
dist = SquareRoot0(dX*dX + dY*dY + dZ*dZ)  // GTE SquareRoot0
g_animSharedVarTable[dest] = (short)dist
```

**Usage** : calculer la distance 3D entre deux parties du corps (ou entités via `ResolveBodyPartTarget`).

---

### 27.12 AnimCmd_ObjLongGet — `obj_long_get` (opcode 0x12 @ 0x8003aed4, 208 octets)

**Format** : 2 mots

```
word[0]: opcode=0x12 | start_idx<<8
word[1]: lo_byte=count, hi_byte=dest_varTable_idx
```

**Logique** (CERTAIN) :

```c
sum = 0
for i in start_idx .. start_idx+count:
    sum += g_meshXOffsetBuffer[i]
g_animSharedVarTable[dest] = sum
```

**Usage** : accumule les offsets X de plusieurs meshes consécutifs → mesure de déplacement écran horizontal total.

---

### 27.13 AnimCmd_AttSet — `att_set` (opcode 0x25 @ 0x8003d208, 584 octets)

**Format** : 3 mots

```
word[0]: opcode=0x25 | mode<<8
         lo_byte bit[0]: uVar6 (0=attack_list_A, 1=attack_list_B)
         lo_byte bit[3]: si 1 → reset DAT_801faa84/88/8c + snap position
word[1]: lo_byte=body_part_spec, hi_byte=dest_varTable_idx (bits[3:0])
word[2]: bitmask résultat (ORé dans varTable)
```

**Logique** (CERTAIN) :

```c
pSVar3 = ResolveBodyPartTarget(word[1] & 0xff, gameState)
// Prépare local_30/2e/2c = DAT_801faa84/88/8c (velocity/delta)
iVar4 = FUN_80043a84(ListHead_800892d4 + uVar6*0x3c, pSVar3, &local_30, &local_28, uVar6)
*(byte*)(iVar4 + 0x18) = 1  // marque collision active

if (uVar6 == 0):
    if (iVar4 + 0xc == -1):
        varTable[dest] |= local_32 << 1   // pas de hit
    else:
        varTable[dest] |= local_32        // hit détecté
else:
    // Itère 6 slots char, cherche correspondance charPointers[0]
    // ORe local_32 (shiftant gauche à chaque itération) dans varTable
```

**FUN_80043a84** : non analysée — probablement enregistrement/test zone d'attaque.

---

### 27.14 AnimCmd_HitzSet — `hitz_set` (opcode 0x26 @ 0x8003e760, 440 octets)

**Format** : 4 mots

```
word[0]: opcode=0x26 | body_part_spec<<8
word[1]: hitbox_size (ou g_animSharedVarTable[idx] si word[2]>>8 & 1)
word[2]: lo_byte=dest_varTable_idx, hi_byte bit[0]=indirect_flag
word[3]: bitmask à ORer dans varTable par slot char détecté
```

**Logique** (CERTAIN) :

```c
pvVar3 = ResolveBodyPartTarget(word[0]>>8, gameState)
// Enregistrer hitbox dans 2 listes de collision
FUN_800452f4(&ListHead_800892d4, 0x40, pos, hitbox_size)
iVar4 = FUN_800452f4(&ListHead_80089310, 0x40, pos, hitbox_size)

// Si entrée dans ListHead_80089310 active:
for each entry in list:
    if (entry.byte[0x19] == 0 && entry.byte[0x18] == '@'):
        for slot in 0..5:
            if entry.charPtr == charPointers[slot]:
                varTable[dest] |= (word[3] << slot)
                entry.byte[0x19] = 0; clearEntry
```

**Usage** : détection de hitzone — teste si la position du personnage intersecte un hitbox, marque les slots correspondants.

---

### 27.15 AnimCmd_ChDanSet — `ch_dan_set` (opcode 0x2E @ 0x8003e508, 600 octets)

**Format** : 3 mots, 2 modes

**Mode 0** (bit[7]=0 dans lo_byte) :
```
word[0]: opcode=0x2E | flags
word[1]: lo_byte=body_part_target, hi_byte=scale_spec
word[2]: damage_param (short)
```
```c
pos = ResolveBodyPartTarget(word[1] & 0xff, gameState)
scale = ResolveBodyPartScale(word[1] >> 8, gameState)
FUN_8004375c(pos, scale, (short)word[2], mode & 0xff)
```

**Mode 1** (bit[7]=1) :
```
word[0]: opcode=0x2E | 0x80 | flags
word[1]: packed (part_spec + var flags)
word[2]: bitmask
```
```c
// Lit gameState->entityData.runtimePointers.dataPtr1
// Si ptr != 0 et field +0xc == -1 (final state):
//   Copie 3 shorts depuis ptr+0x2c..0x30 → body part position
//   Désactive dataPtr1 = 0
// Masque bitmask dans g_animSharedVarTable
```

**FUN_8004375c** : non analysée — PROBABLE enregistrement/application de dégâts.

---

### 27.16 AnimCmd_PartsLink — `parts_link` (opcode 0x09 @ 0x80039f44, 580 octets)

**Format** : 2 mots

```
word[0]: opcode=0x09 | src_group_id<<8
word[1]: lo_byte=src_part_id, hi_byte=count
```

**Logique** (CERTAIN via Ghidra) :

```c
// Cherche entrées dans g_renderMetadataBuffer avec:
//   byte[2] == src_group_id && byte[1] == src_part_id
for each match:
    uVar10 = g_renderMetadataBuffer[i] >> 24  // poly_idx
    sVar2 = g_meshCountBuffer[i]              // poly count
    
    // Copie UV depuis DAT_801f2198[DAT_801fa87f[part_id*4] * 0x10]
    for p in 0..poly_count:
        g_uvOrTexCoordBuffer[uVar10+p * 0x10] = uVar3  // UV source copy
        DAT_801f2188[(uVar10+p) * 0x10] = uVar3
    
    // Met à jour offset X avec g_meshXOffsetBuffer
    sVar6 = DAT_801f2188[uVar10*0x10] + g_meshXOffsetBuffer[i]
```

**Nouveaux accès observés** :

| Adresse | Accès | Type minimal | Note |
|---------|-------|-------------|------|
| 0x801fa87f | lecture via `[part_id*4]` | byte[] | Table mapping part_id → UV_source_idx |
| 0x801f2198 | lecture+écriture via `[idx * 0x10]` | undefined2[] | UV source table (stride 0x20 = 32B) |
| 0x801f2188 | lecture+écriture via `[idx * 0x10]` | undefined2[] | UV dest/transform buffer |

**Classification** : PROBABLE — `g_uvOrTexCoordBuffer` et `g_uvTransformBuffer` (noms à confirmer).

---

### 27.17 Table récapitulative des opcodes analysés

| Adresse | Nom | Opcode | Format | Fonction résumée |
|---------|-----|--------|--------|-----------------|
| 0x8003c738 | `AnimCmd_MoveSet` | 0x1A | 4 mots | Interpolation 3D vers cible avec delta velocity |
| 0x8003d580 | `AnimCmd_Xy0123Set` | 0x22 | 5+ mots | ApplyMathOp sur x0..y3 de POLY_GT4 |
| 0x8003a300 | `AnimCmd_Rgb2Set` | 0x1C | 4-5 mots | ApplyRgbaPerVertex par canal R,G,B,STP |
| 0x8003cf38 | `AnimCmd_EffSet` | 0x21 | 1-3 mots | Spawn/kill effet via g_effectObjectPtrs |
| 0x8003b148 | `AnimCmd_BitSet` | 0x0D | 3 mots | ApplyMathOp sur g_animSharedVarTable direct |
| 0x8003afa4 | `AnimCmd_BitChk` | 0x0C | 2-4 mots | Test bits varTable → action conditionnelle stream |
| 0x8003d450 | `AnimCmd_IfSet` | 0x23 | 3 mots | Skip conditionnel → scan jusqu'à tag de fin |
| 0x8003b260 | `AnimCmd_EndSet` | 0x0E | 1 mot | Terminer stream (ou loop si pause) |
| 0x8003ad38 | `AnimCmd_ObjIntGet` | 0x11 | 2 mots | Distance 3D entre 2 body parts → varTable |
| 0x8003aed4 | `AnimCmd_ObjLongGet` | 0x12 | 2 mots | Somme g_meshXOffsetBuffer[range] → varTable |
| 0x8003d208 | `AnimCmd_AttSet` | 0x25 | 3 mots | Test collision attaque → flag chaîne de résultat |
| 0x8003e760 | `AnimCmd_HitzSet` | 0x26 | 4 mots | Enregistre hitzone + test intersection → varTable |
| 0x8003e508 | `AnimCmd_ChDanSet` | 0x2E | 3 mots | Enregistre dégâts (mode 0) / finalise impact (mode 1) |
| 0x8003f44 | `AnimCmd_PartsLink` | 0x09 | 2 mots | Copie UV/position entre meshes par group+part ID |

### 27.18 Fonctions renommées cette section

| Ancienne | Nouvelle | Signature | Callers |
|----------|----------|-----------|---------|
| `FUN_8003f464` | `ApplyRgbaPerVertex` | `void(ushort,ushort,byte*,short×8+2)` | AnimCmd_Rgb2Set (3×) |

### 27.19 Nouveaux labels identifiés

| Adresse | Label candidat | Type | Preuve | Certitude |
|---------|---------------|------|--------|-----------|
| 0x801faa84 | `g_charMoveVelocity` | short[3] | lu par MoveSet comme delta X/Y/Z | PROBABLE |
| 0x801fa87f | `g_bodyPartUVIndexTable` | byte[] | indexé par `part_id*4` dans PartsLink | PROBABLE |
| 0x801f2198 | `g_uvSourceBuffer` | undefined2[] stride 0x10 | lu/écrit par PartsLink | PROBABLE |
| 0x801f2188 | `g_uvTransformBuffer` | undefined2[] stride 0x10 | lu/écrit par PartsLink | PROBABLE |

### 27.20 Zones d'ombre restantes

| Élément | Statut | Raison |
|---------|--------|--------|
| `FUN_80043a84` | INCONNU | Appelée par AttSet — registration zone attaque + résolution |
| `FUN_8004375c` | INCONNU | Appelée par ChDanSet — application/registration dégâts |
| `FUN_800452f4` | INCONNU | Appelée par HitzSet (2×) — registration hitbox dans liste |
| `AnimCmd_ChseCall` @ 0x8003ec74 | ANALYSÉ section 28 | ChaseCallAI + g_chaseStateBlock |
| `AnimCmd_BaseCulX/Y/Z/P` | ANALYSÉ section 28 | 4 opcodes MathOp sur g_uvOrTexCoordBuffer |
| `AnimCmd_MovexpSet` @ 0x8003c514 | ANALYSÉ section 28 | ComputeCharMovement + g_charMoveVelocity |
| `AnimCmd_DistSet` @ 0x8003c638 | ANALYSÉ section 28 | FUN_800460f8 + rotation |
| `AnimCmd_XAddSet` @ 0x80039d6c | ANALYSÉ section 28 | Scroll X avec delta clamp |
| `AnimCmd_XMaxSet` @ 0x8003a188 | ANALYSÉ section 28 | ApplyMathOp sur g_meshEntryFlagsHiBuf |
| `DAT_801faa84..8c` | CERTAIN = `g_charMoveVelocity` | Preuve directe AnimCmd_MovexpSet |

---

## Section 28 — AnimCmd batch final : culling UV, mouvements, IA, XOffset

### 28.1 AnimCmd_BaseCulX/Y/Z/P — `base_cul_{x/y/z/p}` (opcodes 0x0F..0x14)

| Adresse | Nom | Taille |
|---------|-----|--------|
| 0x8003b2d8 | `AnimCmd_BaseCulX` | 1152 o |
| 0x8003b758 | `AnimCmd_BaseCulY` | 1152 o |
| 0x8003bcd8 | `AnimCmd_BaseCulZ` | 1152 o |
| 0x8003c258 | `AnimCmd_BaseCulP` | 1152 o |

**Format** : 3 + 4 mots inline

```
word[0]: opcode | flags<<8
         lo_byte bit[2] = 1 → start_idx depuis varTable
         lo_byte bits[1:0] = range_mode
word[1]: lo_byte=start_idx, hi_byte=count
word[2]: 4 × 4-bit math_op modes = [mode3:4][mode2:4][mode1:4][mode0:4]
word[3..6]: 4 operandes inline (un par mode)
```

**Core loop** (CERTAIN — identique pour les 4 variantes) :

```c
// psVar10 = g_uvOrTexCoordBuffer + poly_idx * 0x10
// Itère poly_count fois, 4 opérations MathOp successives (+4 shorts = +8 bytes)
for poly in 0..poly_count:
    local_3e = word[2]  // modes packed
    for ch in 0..3:
        *psVar10 = ApplyMathOp(*psVar10, local_3e & 0xf, local_38[ch])
        psVar10 += 4     // stride 4 shorts = 8 bytes entre composantes
        local_3e >>= 4
```

**Range modes (bits[1:0])** :

| Mode | Description |
|------|-------------|
| 0 | Direct range [start..start+count] dans g_uvOrTexCoordBuffer[idx * 0x10] |
| 1 | Via g_renderMetadataBuffer byte[3] → poly_idx lookup |
| 2 | Search par group_id dans g_renderMetadataBuffer byte[2] |

**Note** : Les 4 variantes (X/Y/Z/P) pointent vers des composantes différentes de `g_uvOrTexCoordBuffer` (stride 0x10 = 32 bytes, chaque variante cible un offset +x dans ce stride). PROBABLE → buffer de coordonnées transformées (clip-space ou world-space X/Y/Z/W).

---

### 28.2 AnimCmd_XAddSet — `x_add_set` (opcode 0x08 @ 0x80039d6c, 472 octets)

**Format** : 3 mots

```
word[0]: opcode=0x08 | op_mode<<8 (bit[7]=indirect operand)
word[1]: lo_byte=start_idx,
         bits[11:8] = varTable_idx_A (source position),
         bits[15:12] = varTable_idx_B (target position)
word[2]: max_step (ou varTable[word[2]] si bit[7])
```

**Logique** (CERTAIN) :

```c
delta = (short)(g_animSharedVarTable[var_A] - g_animSharedVarTable[var_B])
direction = sign(delta)
if direction > 0:
    step = min(delta, max_step)
    range = [start_idx .. start_idx + op_mode]
else:
    step = 0
    range = [start_idx-1 .. start_idx + op_mode - 1]

for i in range:
    g_meshXOffsetBuffer[i] += delta
    // Clamp avec g_meshEntryFlagsHiBuf[i] comme borne max
```

**Usage** : scrolling horizontal de meshes vers une position cible avec vitesse max limitée.

---

### 28.3 AnimCmd_XMaxSet — `x_max_set` (opcode indéterminé @ 0x8003a188, 376 octets)

**Format** : 3 mots

```
word[0]: opcode | start_idx<<8
word[1]: lo_byte=count, hi_byte=op_mode (bit[4]=indirect)
word[2]: operand
```

**Logique** (CERTAIN) :

```c
for i in start_idx..start_idx+count:
    val = ApplyMathOp(g_meshEntryFlagsHiBuf[i], op_mode, operand)
    if val < 0: val = 0
    g_meshEntryFlagsHiBuf[i] = val
    // si op_mode==8: copie depuis g_meshEntryFlagsHiBuf[operand]
```

`g_meshEntryFlagsHiBuf` (0x801fa800) = borne max de scroll X ou valeur "visible width" par mesh.

---

### 28.4 AnimCmd_MovexpSet — `movexp_set` (opcode 0x1D @ 0x8003c514, 292 octets)

**Format** : 4 mots

```
word[0]: opcode=0x1D | scale_spec<<8 (bits[5:0])
word[1]: bits[11:0]=speed_param, bit[12]=indirect word[3], bit[13]=indirect word[2], bit[14]=indirect self
word[2]: direction param
word[3]: magnitude param
```

**Logique** (CERTAIN) :

```c
pSVar1 = ResolveBodyPartScale(word[0]>>8 & 0x3f, gameState)
ComputeCharMovement(pSVar1, &g_charMoveVelocity, speed&0xfff, direction, (short)magnitude)
```

**Preuve directe** (CERTAIN) : `&g_charMoveVelocity` = 0x801faa84 passé direct à `ComputeCharMovement`.

---

### 28.5 AnimCmd_DistSet — `dist_set` (opcode 0x1E @ 0x8003c638, 256 octets)

**Format** : 3 mots

```
word[0]: opcode=0x1E | target_C_spec<<8
word[1]: lo_byte=target_A_spec, hi_byte=scale_spec
word[2]: angle_add (12 bits)
```

**Logique** (CERTAIN) :

```c
posA   = ResolveBodyPartTarget(word[0]>>8, gameState)
posB   = ResolveBodyPartTarget(word[1] & 0xff, gameState)
scale  = ResolveBodyPartScale(word[1]>>8, gameState)
FUN_800460f8(posA, posB, scale, param_4, sVar6)  // INCONNU: calcul direction/vecteur
scale->vy = (scale->vy + word[2]) & 0xfff
```

---

### 28.6 AnimCmd_ChseCall — `chse_call` (opcode 0x32 @ 0x8003ec74, 228 octets)

**Format** : 2 mots

```
word[0]: opcode=0x32 | target_arg<<8 (ou varTable[lo_byte & 0xf] si bit[7])
word[1]: lo_byte=chase_type, hi_byte=speed_arg (ou varTable si bit[7])
```

**Logique** (CERTAIN) :

```c
if (target_arg == g_chaseStateBlock[1]):  // DAT_801fac41
    g_chaseStateBlock[0..3] = 0           // reset état
ChaseCallAI(chase_type, target_arg, speed_arg)
```

---

### 28.7 Fonctions renommées cette section

| Ancienne | Nouvelle | Signature | Callers |
|----------|----------|-----------|---------|
| `FUN_80047714` | `ComputeCharMovement` | `void(SVECTOR*, short*, ushort, ushort, int)` | 3 |
| `FUN_80065208` | `ChaseCallAI` | `void(ushort, short, short)` | 1 |

### 28.8 Nouveaux labels créés

| Adresse | Label | Certitude |
|---------|-------|-----------|
| 0x801faa84 | `g_charMoveVelocity` | CERTAIN |
| 0x801fac40 | `g_chaseStateBlock` | CERTAIN |

### 28.9 Zones d'ombre restantes prioritaires

| Élément | Statut | Action recommandée |
|---------|--------|--------------------|
| `FUN_800460f8` | RÉSOLU → `ComputeAnglesToTarget` | section 29 |
| `FUN_80043a84` | RÉSOLU → `QueryAttackZoneList` | section 29 |
| `FUN_8004375c` | RÉSOLU → `SpawnCharDamageTask` | section 29 |
| `FUN_800452f4` | RÉSOLU → `RegisterHitboxInList` | section 29 |
| `AnimCmd_AddPrimsToOT` @ 0x80038b88 | ANALYSÉ section 29 | — |
| `AnimCmd_AnimateVertexColors` @ 0x80039290 | ANALYSÉ section 29 | — |
| `g_uvOrTexCoordBuffer` @ 0x801f... | PROBABLE nom | Confirmer offset exact via BaseCulX |
| `g_meshEntryFlagsHiBuf` @ 0x801fa800 | PROBABLE "max_x_scroll" | Confirmer via animation data |

---

## Section 29 — Fonctions utilitaires : angles, hitboxes, dégâts + opcodes finaux

### 29.1 ComputeAnglesToTarget — `FUN_800460f8` (712 octets)

**Signature** :
```c
void ComputeAnglesToTarget(short *posA, short *posB, short *out_angles)
```

**Algorithme** (CERTAIN) :

```c
// Différences XZ avec wrapping 16-bit (modulo 0x10000 → plage ±0x7FFF)
dX = posB[0] - posA[0]  // vx
dZ = posB[2] - posA[2]  // vz
if abs(dX) > 0x7FFF: dX = 0xFFFF - abs(dX) (avec signe corrigé)
if abs(dZ) > 0x7FFF: dZ = 0xFFFF - abs(dZ)

// Angle horizontal (azimut XZ)
out_angles[1] = ratan2(dX, dZ) & 0xFFF

// Projection GTE pour distance XZ → angle vertical
PushMatrix(); SetRotMatrix(identity + RotMatrixY(0x1000 - azimuth))
RotTrans([dX, 0, dZ], &projected_vec)  // projette en espace de caméra rotationnel
PopMatrix()

// Angle vertical (élévation)
out_angles[2] = ratan2(posB[1] - posA[1], projected_vec.z) & 0xFFF
out_angles[0] = 0  // vx non utilisé (reset)
```

**Callers** (5) : `AnimCmd_DistSet`, `FUN_8002770c` (×1), `FUN_8004dc2c` (×1), `FUN_8004d8cc` (×1), `FUN_80054fe4` (×1)

---

### 29.2 QueryAttackZoneList — `FUN_80043a84` (6248 octets, 480 lignes)

**Signature** :
```c
void *QueryAttackZoneList(ListHead *listHead, SVECTOR *pos, ushort *velocity,
                           SVECTOR *target, int mode)
```

**Rôle** (PROBABLE) : Query de liste de zones d'attaque avec partition spatiale en cellules (>>9 = cellule 512 unités). Teste si une entité en position `pos` avec vitesse `velocity` intersecte une zone dans `listHead`. Retourne le premier nœud correspondant.

**Particularités observées** :

- `velocity` = short[3] = {X, Y, Z} vitesse (déduit SquareRoot(vx²+vy²+vz²) = magnitude)
- Cellules calculées en floor(coord >> 9) pour X et Z
- `mode=1` → retourne null immédiatement si liste vide (les 2 têtes vides)
- `mode!=1` → calcule plages de cellules couvertes par trajectory (pos..pos+vel)
- Recherche le nœud le plus proche (`local_28 = 0x7FFF` dist² initiale)
- Retourne le pointeur nœud → `*(byte*)(result+0x18) = 1` pour marquer actif

**Callers** : `AnimCmd_AttSet` (attaque), `FUN_80054420`, `FUN_800548c8`

---

### 29.3 SpawnCharDamageTask — `FUN_8004375c` (312 octets)

**Signature** :
```c
void SpawnCharDamageTask(short *pos, short *scale, short damage_value, uint effect_type)
```

**Logique** (CERTAIN) :

```c
task = CreateTask(FUN_80042b6c, 0, 0xB, 0xC0, 0, g_taskListTails[0xB])
if task != 0:
    gs = task->gameState
    // Copie pos[0..2] dans entityData.runtimePointers (polyFt3Index..polyFt4Index)
    // Copie scale[0..2] dans entityData.runtimePointers (polyGt3Index area)
    // Initialise nombreux _ptrs internes (back-links vers EntityData fields)
    gs.entityData.runtimePointers.dataPtr14 = damage_value
    InitEntityAnimPtr(gs, -0x7FFDE77C, effect_type & 0xFFFF)
```

**Usage** : crée une tâche pour afficher/animer l'indicateur de dégâts (nombre visuel, flash, etc.) au-dessus du personnage touché.

---

### 29.4 RegisterHitboxInList — `FUN_800452f4` (1764 octets)

**Signature** :
```c
int RegisterHitboxInList(ListHead *listHead, ushort flags, short *pos, ushort radius)
```

**Logique** (CERTAIN — partition spatiale) :

```c
// Cellule min/max X = (pos[0] ± radius) >> 9
x_min_cell = (pos[0] - radius - 1) >> 9  (avec ajustement signe)
x_max_cell = (pos[0] + radius - 1) >> 9
// Idem pour Z = pos[2]
// Alloue/enregistre nœud dans la liste pour chaque cellule couverte
```

**Structure entrée allouée** : `byte[0x1A]` minimum — `byte[0x18]` = active flag, `byte[0x19]` = hit flag, `char[0x18]` = type/owner code `'@'` (0x40) vu dans HitzSet

**Callers** : `AnimCmd_HitzSet` (×2 — pour `ListHead_800892d4` et `ListHead_80089310`)

---

### 29.5 AnimCmd_AnimateVertexColors — `vert_col_set` (opcode 0x06 @ 0x80039290, 1220 octets)

**Important** : le nom est trompeur — cette fonction anime les champs **CLUT et TPAGE** des POLY_GT4, pas les couleurs vertex (R/G/B). Il s'agit d'**animation de texture** (palette switch + texture page flip).

**Format** : 4 mots

```
word[0]: opcode | mode<<8
         lo_byte bit[6] = indirect word[2] via varTable
         lo_byte bit[7] = indirect word[3] via varTable
         lo_byte bits[5:4] = range_mode (0x10=range, 0x00=direct, 0x20=search)
         lo_byte bits[3:0] = ApplyMathOp mode
word[1]: lo_byte=start_idx, hi_byte=count
word[2]: CLUT operand  (ou varTable[word[2] & 0x7FFF])
word[3]: TPAGE operand (ou varTable[word[3] & 0x7FFF])
```

**Boucle interne** (CERTAIN) :

```c
// puVar5 = &POLY_GT4_pool[poly_idx].clut  (u_short*)
// 2 itérations (iVar2 = 3..4):
//   iVar2=3 → *puVar5 = ApplyMathOp(clut,  mode, word[2])  +clampe ≥ 0
//   iVar2=4 → *puVar5 = ApplyMathOp(tpage, mode, word[3])  +clampe ≥ 0
//   stride interne: puVar5 += 6 u_shorts = +12 bytes
// stride inter-polygone: puVar6 + 0x14 u_shorts = +40 bytes → polygone suivant
```

**Résultat** : permet d'animer la palette (CLUT index) et la page texture (TPAGE) par polygone — effet de clignotement, changement de couleur de palette, ou animation sprite sheet.

---

### 29.6 AnimCmd_AddPrimsToOT — `pri_set` (opcode 0x0A @ 0x80038b88, 412 octets)

**Format** : 2 mots

```
word[0]: opcode=0x0A | group_id<<8 (ou varTable[idx] si bit[4])
word[1]: poly_count (ou varTable[idx] si bit[4] dans lo_byte word[0])
```

**Logique** (CERTAIN) :

```c
// Cherche entrée avec g_renderMetadataBuffer.byte[2] == group_id
for each entry in g_renderMetadataBuffer[0..0x3F]:
    if byte[2] == group_id:
        poly_base = POLY_GT4_pool + (entry >> 24)  // poly index
        for i in 0..poly_count:
            depth = g_polyOTDepthTable[poly_base + i]
            if 0 < depth < 0x800:
                AddPrim(OT[0x7FF - depth], &poly_base[i])
        return  // stop at first group match
```

**OT structure** (CERTAIN) :
- OT base = `DRAWENV_ptr + 0x70`
- OT taille = 0x800 entrées (2048 niveaux de profondeur)
- `OT[0] = closest`, `OT[0x7FF] = farthest`
- Profondeur 0 ou ≥ 0x800 → polygone ignoré (hors range)

---

### 29.7 Récapitulatif final — toutes les fonctions renommées

| Adresse | Nom | Callers | Notes |
|---------|-----|---------|-------|
| 0x8003f694 | `ApplyMathOp` | 16 | 13 modes |
| 0x8003f37c | `ResolveBodyPartTarget` | multiple | 3 modes |
| 0x8003f404 | `ResolveBodyPartScale` | multiple | 2 modes |
| 0x8003ffec | `SpawnEffectTask` | 2 | |
| 0x80053d44 | `InitEntityAnimPtr` | 3 | |
| 0x8003fddc | `EffectTaskMainLoop` | 1 | |
| 0x8003f814 | `TransformAndProjectMesh` | 1 | GTE |
| 0x8003fae8 | `UpdateCharEffectTransform` | 1 | interpolation |
| 0x8003f464 | `ApplyRgbaPerVertex` | 3 | RGBA par vertex |
| 0x80047714 | `ComputeCharMovement` | 3 | mouvement + velocity |
| 0x80065208 | `ChaseCallAI` | 1 | IA poursuite |
| 0x800460f8 | `ComputeAnglesToTarget` | 5 | azimut + élévation |
| 0x80043a84 | `QueryAttackZoneList` | 3 | partition spatiale |
| 0x8004375c | `SpawnCharDamageTask` | 1 | task dégâts |
| 0x800452f4 | `RegisterHitboxInList` | 2 | hitbox + spatial |

### 29.8 État final des labels globaux

| Adresse | Label | Type | Certitude |
|---------|-------|------|-----------|
| 0x80087950 | `g_animStreamDispatchTable` | void*[51] | CERTAIN |
| 0x80087A1C | `g_animStreamOpcodeNames` | char[][16] | CERTAIN |
| 0x801faa60 | `g_renderFlushFlag` | uint | CERTAIN |
| 0x801faa64 | `g_animSharedVarTable` | uint16[16] | CERTAIN |
| 0x801faaac | `g_effectObjectPtrs` | uint32[16] | CERTAIN |
| 0x801fa580 | `g_polyOTDepthTable` | int16[] | CERTAIN |
| 0x801fa780 | `g_meshXOffsetBuffer` | int16[] | CERTAIN |
| 0x801fa800 | `g_meshEntryFlagsHiBuf` | uint16[] | CERTAIN |
| 0x801f2000 | `g_renderScratchBuffer` | 0x8C48 bytes | CERTAIN |
| 0x801f2100 | `g_bodyPartTransformTable` | SVECTOR[16] | CERTAIN |
| 0x801fab0c | `g_charRenderStateBuf` | uint32[6] | CERTAIN |
| 0x801fab24 | `g_charSharedVarMaskBuf` | uint16[6] | CERTAIN |
| 0x801fab30 | `g_charEffectSlotTable` | uint32[16] | CERTAIN |
| 0x801faa84 | `g_charMoveVelocity` | short[3] | CERTAIN |
| 0x801fac40 | `g_chaseStateBlock` | uint8[4] | CERTAIN |
| 0x801d2000 | `g_cdFileBufferTable` | variable | CERTAIN |
| 0x801d2004 | `g_meshTableCounts` | uint16 | CERTAIN |
| 0x801d2008 | `g_chBinEntryTableBasePtr` | uint32 | CERTAIN |
| 0x801d200c | `g_chBinClutTablePtr` | uint32 | CERTAIN |
| 0x800a6778 | `g_charEffectTranslatePtr` | void* | CERTAIN |
| 0x800a677c | `g_charEffectScalePtr` | SVECTOR* | CERTAIN |
| 0x800a6786 | `g_charEffectInitFlag` | uint | CERTAIN |
| 0x801faa00 | `g_meshStreamPtrBuffer` | uint32[] | CERTAIN |
| 0x801faa40 | `g_meshOffsetBuffer` | uint16[] | CERTAIN |

### 29.9 Zones d'ombre résiduelles (post-analyse complète)

| Élément | Statut | Priorité |
|---------|--------|----------|
| `FUN_80042b6c` | **RÉSOLU** → `DamageNumberTaskLoop` (section 30) | — |
| `ListHead_800892d4` | PARTLY KNOWN | liste principale attaque + hitbox (identifiée mais structure interne INCONNU) |
| `ListHead_80089310` | PARTLY KNOWN | liste hitbox secondaire (idem) |
| `g_uvOrTexCoordBuffer` offset exact | **RÉSOLU** → 0x801f2180, layout AOS 16 shorts/poly (section 31) | — |
| `DAT_8009a950` / `DAT_800c0808` | INCONNU | buffers temporaires GTE dans ComputeAnglesToTarget |
| `POLY_GT4_pool` alignement exact | **RÉSOLU** → stride 52 bytes exact = sizeof(POLY_GT4) (section 31) | — |

---

## Section 30 — Helpers tâches et caméra : analyse des fonctions combat et rendu de sprites

### 30.1 Résumé factuel

Cette session analyse les fonctions helpers identifiées lors de la session 29 comme cibles prioritaires.
11 nouvelles fonctions nommées dans Ghidra. Couverture étendue aux sous-systèmes :
- **Tâches sprites** (damage numbers + effets visuels courts)
- **Actions combat** (déclenchement, direction, son/couleur)
- **Caméra de bataille** (mise à jour principale depuis `main()`)
- **Distance 3D** + **sélection de frame orientation**

### 30.2 ComputeDistance3D @ 0x80045eb8

**Signature** : `int ComputeDistance3D(short *posA, short *posB)` (400 bytes, 6 callers)

**Preuve** (CERTAIN) :
```c
local_18 = abs(posB[0] - posA[0]);  // dX
if (local_18 > 0x7FFF) local_18 = 0xFFFF - local_18;  // wrap 16-bit
local_14 = abs(posB[2] - posA[2]);  // dZ (même wrap)
dy = (posB[1] - posA[1]);           // dY : pas de wrap
return (short)SquareRoot0(dX*dX + dY*dY + dZ*dZ);
```

**Callers confirmés** :
| Caller | Usage |
|--------|-------|
| `UpdateBattleCamera` (×4) | distance combattants (physique caméra) |
| `FUN_80023924` | distance entre positions de parties |
| `FUN_80055178` | distance entre entrées POLY_GT4 |

**Notes** :
- Wrap 16-bit sur X et Z : espace 3D PSX utilise des coordonnées 16-bit signées
- Y direct : pas de wrap (hauteur non cyclic)
- Retourne `short` (max ~32767 unités)

### 30.3 SetCharacterAction @ 0x80047e28

**Signature** : `void SetCharacterAction(GameState *gameState, uint actionIndex)` (148 bytes, 15 callers)

**Preuve** (CERTAIN) :
```c
InitEntityAnimPtr(gameState,
    *(int*)(charData->field_0x0 + 0x38),  // animTableBase depuis char data
    actionIndex & 0xFFFF);
battleChars[0].previousAction = battleChars[0].currentAction;
battleChars[0].currentAction = (byte)actionIndex;
FUN_800264b8(gameState);  // post-action setup (INCONNU)
```

**Indices d'action confirmés** (issus des callers) :
| Index | Caller | Interprétation |
|-------|--------|----------------|
| 0x16 | TriggerCombatAction_Case3 | saut attaque |
| 0x18 | TriggerCombatAction_DirKi | attaque direction basse |
| 0x19 | TriggerCombatAction_DirKi | attaque direction haute |
| 0x1a | TriggerCombatAction_DirKi | attaque direction neutre |
| 0x1c | FUN_8004ac60 | atterrissage |
| 0x1f | FUN_8004c46c | état spécial |
| 0x20 | FUN_8004aed0 | repos/idle |
| 0x21 | FUN_8004ac08 | saut |
| 0x2a | FUN_8004b500 | technique spéciale |

**Note** : 15 callers — fonction centrale de transition d'état des personnages.

### 30.4 DamageNumberTaskLoop @ 0x80042b6c

**Signature** : `void DamageNumberTaskLoop(ushort rotationFlags)` (592 bytes, 2 refs)

**Preuve** (CERTAIN) :
```c
// Registre comme callback de tâche par SpawnCharDamageTask et FUN_80043474
gameState = g_currentTask->gameState;
if (!(g_pauseFlag & 1)) {
    // 2× LookupOrientationFrame(polyGt4Index, polyGt3Index+2) → rotationByte
    // ProcessEntityScript(gameState)  → avance l'animation
    // stocke résultat dans dataPtr14+2
}
// RenderTransformedSprites(charPointers[4], X-camX, Y, Z-camZ, rotationFlags,
//                          0,0, 0x200, 0x200, dataPtr12, 0,0, '\0','\0', 0x80,0x80,0x80, ...)
if (!(g_pauseFlag & 1)) {
    if (dataPtr13 < 0) {  // animation terminée
        jitter_X = rand() % 0xC00 - 0x600;  // ±1536
        jitter_Z = rand() % 0xC00 - 0x600;
        FUN_800463c0(&facing, &position, &scale);  // update transform
        dataPtr13 &= 0x7FFFFFFF;
    }
}
if (polyFt4 != 0) return;       // encore vivant
RemoveTaskFromList(g_currentTask, 0xB);  // terminé → détruire
```

**Offsets de stockage dans entityData** (CERTAIN) :
| Champ | Contenu |
|-------|---------|
| `runtimePointers.polyGt4Index` | Position world X (short) |
| `runtimePointers.polyGt3Index+2` | Position world Y (short) |
| `runtimePointers.polyFt4Index` | Position world Z (short) |
| `runtimePointers.dataPtr12` | Offset de profondeur OT |
| `runtimePointers.dataPtr13` | Flags + signe = état animation |
| `runtimePointers.dataPtr14+2` | Frame orientation byte courant |
| `polyFt4 (short)` | Compteur durée de vie (0 = expire) |

### 30.5 SpriteEffectTaskLoop @ 0x80043894 + SpawnSpriteEffectTask @ 0x800439b0

**SpriteEffectTaskLoop** (284 bytes, 1 caller = SpawnSpriteEffectTask) :
```c
// Task callback simplifié — même pattern que DamageNumberTaskLoop
gameState = g_currentTask->gameState;
if (!(g_pauseFlag & 1)) ProcessEntityScript(gameState);
RenderTransformedSprites(charPointers[4],
    dataPtr2 - g_camX,      // X
    *(dataPtr2+2),           // Y (non corrigé)
    polyFt3Index - g_camZ,   // Z
    (ushort)polyFt4Index,    // rotationFlags
    *(polyFt4Index+2),       // offsetX
    (short)polyGt3Index,     // offsetY
    0x200, 0x200, 1, 0,0, '\0','\0', 0x80,0x80,0x80, ...);
if (!(g_pauseFlag & 1) && polyFt4 == 0)
    RemoveTaskFromList(g_currentTask, 0xB);
```

**SpawnSpriteEffectTask** (212 bytes, 5-10 callers) :
```c
// Crée une tâche SpriteEffectTaskLoop (priorité 0xB, mémoire 0x58 = 88 bytes)
task = CreateTask(SpriteEffectTaskLoop, 0, 0xB, 0x58, 0, g_taskListTails[0xB]);
if (task) {
    gs = task->gameState;
    gs->runtimePointers.dataPtr2[0..1] = position[0..1];   // X,Y
    gs->runtimePointers.polyFt3Index   = position[2];       // Z
    gs->runtimePointers.polyFt4Index   = facing[0];         // angle
    gs->runtimePointers.polyFt4Index+2 = facing[1];
    gs->runtimePointers.polyGt3Index   = facing[2];
    gs->polyGt4               = &entityData;                // self-ref
    gs->runtimePointers.polyF3Index    = &entityData;
    gs->runtimePointers.polyF4Index    = &runtimePointers.dataPtr2;
    InitEntityAnimPtr(gs, -0x7FFDE77C, animIndex);
}
```

**Callers et indices d'animation** :
| Caller | animIndex | Usage |
|--------|-----------|-------|
| TriggerCombatAction_Case3 | 0x11 | effet attaque saut |
| TriggerCombatAction_DirKi | 0x0C | effet approche directionnelle |
| TriggerCombatAction_DirKi (cas 0x17) | 0x02 | pivot si déjà en approche |
| FUN_8004d564 | 0x00 | idle |
| FUN_8004d7ac | 0x0F / 0x02 | recul / pivot |
| FUN_800407d8 | 0x05 | particule explosion |

### 30.6 LookupOrientationFrame @ 0x80045d34

**Signature** : `undefined1 LookupOrientationFrame(ushort angleX, short angleY)` (388 bytes, 7 callers)

**Preuve** (CERTAIN) :
```c
// SVECTOR_1f80007c = viewport reference (camera delta frame)
uVar1 = ((SVECTOR_1f80007c.vy + angleY) >> 8) & 0xF;   // row : 0-15
// Sens de l'offset X selon la ligne (octant caméra)
if (uVar1 < 5 || uVar1 > 10)
    local_12 = (angleX - SVECTOR_1f80007c.vx) >> 8;
else
    local_12 = (angleX + SVECTOR_1f80007c.vx) >> 8;
local_12 &= 0xF;  // col : 0-15
// Cas spéciaux (transition) :
if ((2 < uVar1 < 5) || (10 < uVar1 < 13))
    local_12 = (short)(angleX & 0xFFF) >> 8;   // col non corrigé
return DAT_800884b0[DAT_800884a0[row] * 16 + col];  // lookup 16×16
```

**Tables** :
| Adresse | Type | Contenu |
|---------|------|---------|
| `0x800884a0` | `uint8[16]` | table d'indirection lignes (row → base) |
| `0x800884b0` | `uint8[16*N]` | table orientation 2D (16 colonnes × N lignes) |

**Utilisation** : Retourne un byte de frame orientation (contrôle quel sprite parmi les 8/16 directions de face est utilisé pour ce personnage selon l'angle caméra relatif). Utilisé par les tâches damage + combat.

### 30.7 SetAttackSFXAndColor @ 0x8006458c

**Signature** : `undefined4 SetAttackSFXAndColor(ushort effectMode)` (328 bytes, 13 callers)

**Preuve** (CERTAIN) :
```c
pGVar1 = g_gamestate_8009a990;  // gamestate global secondaire
if (charData->battleChars[0].field_0xb+1 < 0) return -1;  // guard
charData->battleChars[0].u0 = effectMode;  // stocke mode courant
if (effectMode == 0) {
    // Arrêt : clear 4 canaux audio
    for i in 0..3: FUN_80070ee8(0x11+i);  // FUN_80070ee8 = StopSfxChannel
} else {
    // Couleur : indexe DAT_80092314[(effectMode-1)*4] → R,G,B
    entityFlags2.R = DAT_80092314[(effectMode-1)*4 + 0];
    entityFlags2.G = DAT_80092314[(effectMode-1)*4 + 1];
    entityFlags2.B = DAT_80092314[(effectMode-1)*4 + 2];
    entityFlags2.A = 0xFF;
    DAT_8009a91c++;
    if (DAT_8009a91c > 0x14) DAT_8009a91c = 0x12;  // cycle canal (0x12..0x14)
    FUN_80070afc(DAT_8009a91c, position_y, R, G, B, 0, 0xFF, 0xFF);  // PlaySfxAt
    FUN_80071434(DAT_8009a91c, entityFlags, entityFlags+1);           // SetSfxParam
}
```

**Table paramètres son @ 0x80092314** — lecture mémoire Ghidra (32 bytes) :
```
00 00 18 00  (mode 1)
00 01 19 00  (mode 2)
00 02 1A 00  (mode 3)
00 03 1B 00  (mode 4)
00 04 1C 00  (mode 5)
00 05 1D 00  (mode 6)
00 06 1F 00  (mode 7)
00 07 23 00  (mode 8, si utilisé)
```

**Correction** : Ces valeurs NE sont PAS des couleurs RGB (valeurs < 0x24, pas caractéristiques de composantes couleur). Les 3 bytes transférés dans `entityFlags2[0..2]` sont passés comme arguments séparés à `FUN_80070afc` (fonction son PSX). Interprétation PROBABLE : `[0]=groupe, [1]=banque/canal, [2]=ID sample (0x18..0x23)`. La certitude "RGB" de la session précédente est **revue en INCONNU**.

| Mode | byte[0] | byte[1] | byte[2] | Certitude |
|------|---------|---------|---------|-----------|
| 0 | — | — | — | CERTAIN (stop) |
| 1..7 | 0x00 (fixe) | 0x00..0x06 | 0x18..0x1F | INCONNU (sémantique bytes) |

**Globals** :
- `g_gamestate_8009a990` : gamestate global secondaire (autre joueur ou objet global)
- `DAT_8009a91c` : compteur canal son 3D (cycles 0x12..0x14 = 3 canaux disponibles)

### 30.8 UpdateBattleCamera @ 0x8002770c

**Signature** : `void UpdateBattleCamera(void)` (6480 bytes, 822 lignes, 1 caller = `main`)

**Preuve** (CERTAIN — 1 seul caller = main, taille et accès globaux caméra) :

**Structure principale** :
1. Guard : `if (g_pauseFlag & 1) return`
2. Lecture `PTR_8009aa30` = playerGameState
3. Vérification `polyF3Array` bitmask (phase de combat) :
   - `(flags & 0x8000008) && !(flags & 0x2000002)` = phase cinématique/combat actif
4. Récupère positions : `iVar16+0x114` (char pos X), `iVar16+0x116` (Y), `iVar16+0x118` (Z)
5. **Phase (DAT_8009aca0+0x76)** dispatcher :
   - Phase 0 : `DAT_1f8000d0 = 0x280` (FOV fixe), `DAT_8009a828/82c = 0x1000` (scale = 1.0)
   - Phase 1-2 : interpolation angle caméra Y via `SVECTOR_1f800084.vy`, calcul lerp
   - Phase 3+ : `SVECTOR_1f800084.vx = 0xC0`, `DAT_1f8000d0 = 0x280`, `scale = 0x1000`
6. Boucle flags `0x10000000` : itère jusqu'à 12 GameStates (tableau de combattants ?)
7. Appels : `ComputeDistance3D` ×4, `ComputeAnglesToTarget` ×1, `ComputeCharMovement` ×2

**Globaux caméra identifiés** (CERTAIN via accès directs) :
| Adresse | Nom provisoire | Valeur type | Certitude |
|---------|---------------|-------------|-----------|
| `0x8009a828` | `g_camScaleX` | 0x1000 (1.0 fp12) | CERTAIN |
| `0x8009a82c` | `g_camScaleY` | 0x1000 (1.0 fp12) | CERTAIN |
| `0x8009a84a` | `g_camFarScale` | 0x200 | CERTAIN |
| `0x8009a848` | `g_camAngleDelta` | angle 12-bit | PROBABLE |
| `0x8009a830` | `g_camAngleLerped` | angle interpolé | PROBABLE |
| `0x8009a834` | `g_camFovLerped` | distance lerp | PROBABLE |
| `0x8009a84c` | `g_camFovDelta` | delta FOV | PROBABLE |
| `0x1f8000d0` | `g_fovOrZoom` | 0x280 = écart | PROBABLE |
| `SVECTOR_1f800084` | `g_cameraAngles` | SVECTOR vx/vy | PROBABLE |
| `PTR_8009aa30` | `g_playerBattleState` | GameState* | PROBABLE |
| `DAT_8009aca0` | `g_battleStateBlock` | uint8[] struct | PROBABLE |

### 30.9 TriggerCombatAction_Case3 @ 0x8004dc2c + TriggerCombatAction_DirKi @ 0x8004d8cc

**TriggerCombatAction_Case3** (1180 bytes, 1 caller = DispatchCombatAction case 3) :
```c
void TriggerCombatAction_Case3(GameState *attacker, uint actionId=0x16, int target)
// 1. Lit DAT_8009a864/868 → position+facing par défaut
// 2. rand() % 4 → SetAttackSFXAndColor(1..4)  (attaque normale-spéciale)
// 3. ComputeAnglesToTarget(attacker.pos, target+0x114, &angles)
// 4. LookupOrientationFrame(angles.Z, angles.X) → orientByte
//    angles.Z = 0; angles.X = (orientByte & 0x80) << 8
// 5. SpawnSpriteEffectTask(attacker.pos, angles, 0x11)
// 6. SetCharacterAction(attacker, 0x16)  // saut attaque
// 7. Flags field_0x128 depuis target+0x16a :
//    '#' (0x23) → +0x1000, '$' (0x24) → +0x2000, '%' (0x25) → +0x800
```

**TriggerCombatAction_DirKi** (864 bytes, 3 callers = DispatchCombatAction cases 4/5/6) :
```c
void TriggerCombatAction_DirKi(GameState *attacker, uint actionId, GameState *target)
// actionId 0x19 → SetAttackSFXAndColor(6)    // ki haute
// actionId 0x1a → SetAttackSFXAndColor(7)    // ki neutre
// actionId 0x18 → SetAttackSFXAndColor(5)    // ki basse
// Si currentAction == 0x17 et task->gameState == target :
//    SpawnSpriteEffectTask(attacker.pos, facing, 2)   // pivot
// Sinon :
//    ComputeAnglesToTarget(attacker.pos, target.pos, &angles)
//    LookupOrientationFrame(...) → SpawnSpriteEffectTask(pos, angles, 0xC)
//    SetCharacterAction(attacker, actionId)
//    field_0x128 |= 0x400   // flag "track opponent"
```

**DispatchCombatAction** (324 bytes, 1 caller) :
```c
void DispatchCombatAction(GameState *param_1, undefined4 param_2)
// switch sur actionType :
// case 3 → TriggerCombatAction_Case3(param_1, 0x16, target)
// case 4 → TriggerCombatAction_DirKi(param_1, 0x19, target)
// case 5 → TriggerCombatAction_DirKi(param_1, 0x1a, target)
// case 6 → TriggerCombatAction_DirKi(param_1, 0x18, target)
// autres → FUN_8004d564, FUN_8004a2cc
```

### 30.10 Table des fonctions ajoutées cette session

| Adresse | Nom | Taille | Certitude |
|---------|-----|--------|-----------|
| 0x80045eb8 | `ComputeDistance3D` | 400 B | CERTAIN |
| 0x80047e28 | `SetCharacterAction` | 148 B | CERTAIN |
| 0x80042b6c | `DamageNumberTaskLoop` | 592 B | CERTAIN |
| 0x80043894 | `SpriteEffectTaskLoop` | 284 B | CERTAIN |
| 0x800439b0 | `SpawnSpriteEffectTask` | 212 B | PROBABLE |
| 0x8006458c | `SetAttackSFXAndColor` | 328 B | PROBABLE |
| 0x80045d34 | `LookupOrientationFrame` | 388 B | PROBABLE |
| 0x8004e1fc | `DispatchCombatAction` | 324 B | PROBABLE |
| 0x8004dc2c | `TriggerCombatAction_Case3` | 1180 B | PROBABLE |
| 0x8004d8cc | `TriggerCombatAction_DirKi` | 864 B | PROBABLE |
| 0x8002770c | `UpdateBattleCamera` | 6480 B | PROBABLE |
| 0x800463c0 | `ApplyGteRotTransform` | 228 B | CERTAIN |

### 30.11 Zones d'ombre après session 30

| Élément | Statut | Action recommandée |
|---------|--------|--------------------|
| `DAT_80092314` sémantique bytes | INCONNU | lire `FUN_80070afc` (son 3D) pour confirmer structure |
| `DAT_800884a0` | PROBABLE lookup orientation (16 entrées) | vérifier taille avec `read-memory` |
| `FUN_800264b8` post-action | INCONNU | appelé par SetCharacterAction |
| `FUN_8004d564` / `FUN_8004a2cc` | INCONNU | autres cases de DispatchCombatAction |
| `g_gamestate_8009a990` vs `PTR_8009aa30` | PROBABLE | deux joueurs différents |
| Structure nœud ListHead (byte[0x18/0x19]) | **RÉSOLU** → `HitboxNode` créé dans Ghidra (section 30.12) | — |
| `FUN_80070afc` / `FUN_80071434` | INCONNU | son 3D PSX (SPU ?) |
| `g_uvOrTexCoordBuffer` adresse + layout | **RÉSOLU** → 0x801f2180, AOS 4×{X,Y,Z,P} shorts/poly (section 31) | — |
| `POLY_GT4` stride en mémoire | **RÉSOLU** → 52 bytes = sizeof(POLY_GT4) confirmed (section 31) | — |

### 30.12 Structure nœud ListHead — analyse RegisterHitboxInList

**Preuves** tirées de RegisterHitboxInList (CERTAIN pour les offsets accédés) :

```c
// Accès observés sur local_24 (HitboxNode*)
*local_24              // offset 0x00 : next ptr (type HitboxNode*)  — CERTAIN
local_24[4]            // offset 0x10 : vertexData ptr (int→short*)  — CERTAIN
local_24[5] = short    // offset 0x14 : cellX (spatial >>9)          — CERTAIN
*(short*)(local_24+0x16) // offset 0x16 : cellZ (spatial >>9)        — CERTAIN
local_24[6] = ushort   // offset 0x18 : flags/hits field             — CERTAIN
local_24[7] = short    // offset 0x1C : baseX (translation monde)    — CERTAIN
*(short*)(local_24+0x1E) // offset 0x1E : baseY                      — CERTAIN  
local_24[8] = short    // offset 0x20 : baseZ                        — CERTAIN
```

**Structure C partielle** (CERTAIN pour les offsets listés, reste INCONNU) :
```c
typedef struct HitboxNode {
    struct HitboxNode *next;    // 0x00: prochain nœud de la liste liée
    undefined4  unknown_0x04;   // 0x04: INCONNU
    undefined4  unknown_0x08;   // 0x08: INCONNU
    undefined4  unknown_0x0C;   // 0x0C: INCONNU
    short      *vertexData;     // 0x10: ptr vers tableau short[] de vertex/counts
    short       cellX;          // 0x14: cellule spatiale X (pos>>9)
    short       cellZ;          // 0x16: cellule spatiale Z (pos>>9)
    ushort      hitFlags;       // 0x18: flags de collision (0x40 = touché)
                                //       aussi utilisé comme byte actif dans QueryAttackZoneList
    undefined2  unknown_0x1A;   // 0x1A: INCONNU (padding ou champ)
    short       baseX;          // 0x1C: translation base X
    short       baseY;          // 0x1E: translation base Y
    short       baseZ;          // 0x20: translation base Z
    // taille totale : 36 bytes (0x24) — créée dans Ghidra /auto_structs/HitboxNode
} HitboxNode;
```

**Format vertexData (short array)** (PROBABLE) :
```
[0]           = outerCount (nb groupes de hitbox)
[1]           = innerCount[0] (nb points dans groupe 0)
[3..]         = données vertex du groupe 0
// stride entre groupes : 0x1C shorts = 56 bytes
// vertex axes stockés en 3 strips séparées :
//   X = ptr[uVar2]     (8 shorts par groupe)
//   Y = ptr[uVar2+8]
//   Z = ptr[uVar2+0x10]
```

**Certitude** :
| Offset | Champ | Certitude |
|--------|-------|-----------|
| 0x00 | next | CERTAIN |
| 0x04..0x0C | 3 champs INCONNUS | INCONNU |
| 0x10 | vertexData* | CERTAIN |
| 0x14 | cellX | CERTAIN |
| 0x16 | cellZ | CERTAIN |
| 0x18 | hitFlags | CERTAIN |
| 0x1A | unknown_0x1A | INCONNU |
| 0x1C | baseX | CERTAIN |
| 0x1E | baseY | CERTAIN |
| 0x20 | baseZ | CERTAIN |

---

## Section 31 — Validation POLY_GT4 stride et g_uvOrTexCoordBuffer

### 31.1 Résumé factuel

Validation des deux inconnues structurelles majeures héritées de la section 29. Source : analyse complète des décompilations `AnimCmd_AnimateVertexColors` et `AnimCmd_BaseCulX/Y`.

### 31.2 POLY_GT4 stride — CERTAIN : 52 bytes

**Confusion précédente** (section 29.9) : notait "stride +40 octets outer loop" — incorrect car calculé depuis `puVar6` (à offset `.tpage`) et non depuis le début de la structure.

**Preuve directe** (CERTAIN) — décompilation `AnimCmd_AnimateVertexColors` :
```c
// puVar5 démarre à &poly_pool[polyIdx].clut  (offset +14 depuis début POLY_GT4)
do {  // boucle externe : 1 itération = 1 polygone
    iVar2 = 3;
    do {  // boucle interne : iVar2 = 3, 4 → 2 iterations
        puVar6 = puVar5;           // save → u_short* vers .clut puis .tpage
        puVar5 = puVar6 + 6;       // avance 6 u_shorts = +12 bytes
        iVar2++;
    } while (iVar2 < 5);
    // Fin inner: puVar6 pointe .tpage (+12 depuis .clut)
    puVar5 = puVar6 + 0x14;        // +0x14 u_shorts = +40 bytes depuis .tpage
    // .clut + 12 + 40 = .clut + 52 bytes = .clut du polygone suivant ✓
} while (...);
```

**Calcul** :
- `.clut` = offset 14 dans `POLY_GT4`
- `.tpage` = offset 26 = `.clut` + 12 bytes
- Outer advance depuis `.tpage` : +0x14 u_shorts = +40 bytes
- Total depuis `.clut` : 12 + 40 = **52 bytes** = sizeof(POLY_GT4) ✓

**Confirmation** : `POLY_GT4` (psyq330) = 52 bytes packed. Stride réel en pool = 52 bytes. La valeur "PROBABLE 64 bytes" de la section 29.9 est **annulée**.

**Champs animés par AnimCmd_AnimateVertexColors** :
| iVar2 | Offset depuis .clut | Champ POLY_GT4 | Offset absolu | Certitude |
|-------|---------------------|----------------|---------------|-----------|
| 3     | +0                  | `.clut`         | +14           | CERTAIN |
| 4     | +12                 | `.tpage`        | +26           | CERTAIN |

### 31.3 g_uvOrTexCoordBuffer — adresse et layout CERTAIN

**Adresse confirmée** : `0x801f2180` (16 références dans Ghidra, symbole déjà labelisé)
- Position : `g_renderScratchBuffer` (0x801f2000) + 0x180

**Layout AOS par polygone** (CERTAIN — double-confirmé BaseCulX + BaseCulY) :
```c
// BaseCulX : psVar10 = base + polyIdx*0x10;    puis += 4 ×4 → accède [0,4,8,12]
// BaseCulY : psVar8  = base + polyIdx*0x10 + 1; puis += 4 ×4 → accède [1,5,9,13]
// Composante Y démarre à +1 dans chaque bloc de 4 shorts/vertex → layout AOS confirmé

typedef struct PolyVertexBlock {   // 16 shorts = 32 bytes par polygone
    short v0x, v0y, v0z, v0p;     // vertex 0 : composantes X,Y,Z,P
    short v1x, v1y, v1z, v1p;     // vertex 1
    short v2x, v2y, v2z, v2p;     // vertex 2
    short v3x, v3y, v3z, v3p;     // vertex 3
} PolyVertexBlock;
// BaseCulX modifie : v0x, v1x, v2x, v3x  (stride 4 shorts, offset 0)
// BaseCulY modifie : v0y, v1y, v2y, v3y  (stride 4 shorts, offset 1)
// BaseCulZ modifie : v0z, v1z, v2z, v3z  (stride 4 shorts, offset 2)
// BaseCulP modifie : v0p, v1p, v2p, v3p  (stride 4 shorts, offset 3)
```

**Modes d'accès (bits 1:0 du byte flags du stream)** :
| Mode | Accès au buffer | Certitude |
|------|-----------------|-----------|
| 0x01 | `base + renderMetadata[entryIdx].byte3 * 0x10` | CERTAIN |
| 0x00 | `base + (short)polyIdx * 0x10` (direct) | CERTAIN |
| 0x02 | scan 0x40 entrées : `renderMetadata[i].byte2 == groupId` | CERTAIN |

**Composante P** : INCONNU. Candidates : profondeur clip-space W, index palette secondaire, valeur blending.

**Note** : dénomination `g_uvOrTexCoordBuffer` est incorrecte (pas des UV/texcoords). Renommage en `g_polyVertexCoordBuffer` recommandé mais différé en attente confirmation P.

### 31.4 Table labels globaux — état final session 31

| Adresse | Label | Type | Certitude |
|---------|-------|------|-----------|
| 0x80087950 | `g_animStreamDispatchTable` | void*[51] | CERTAIN |
| 0x80087A1C | `g_animStreamOpcodeNames` | char[][16] | CERTAIN |
| 0x801faa60 | `g_renderFlushFlag` | uint | CERTAIN |
| 0x801faa64 | `g_animSharedVarTable` | uint16[16] | CERTAIN |
| 0x801faaac | `g_effectObjectPtrs` | uint32[16] | CERTAIN |
| 0x801fa580 | `g_polyOTDepthTable` | int16[] | CERTAIN |
| 0x801fa780 | `g_meshXOffsetBuffer` | int16[] | CERTAIN |
| 0x801fa800 | `g_meshEntryFlagsHiBuf` | uint16[] | CERTAIN |
| 0x801f2000 | `g_renderScratchBuffer` | 0x8C48 bytes | CERTAIN |
| 0x801f2100 | `g_bodyPartTransformTable` | SVECTOR[16] | CERTAIN |
| 0x801f2180 | `g_uvOrTexCoordBuffer` | PolyVertexBlock[] | CERTAIN |
| 0x801fab0c | `g_charRenderStateBuf` | uint32[6] | CERTAIN |
| 0x801fab24 | `g_charSharedVarMaskBuf` | uint16[6] | CERTAIN |
| 0x801fab30 | `g_charEffectSlotTable` | uint32[16] | CERTAIN |
| 0x801faa84 | `g_charMoveVelocity` | short[3] | CERTAIN |
| 0x801fac40 | `g_chaseStateBlock` | uint8[4] | CERTAIN |
| 0x801d2000 | `g_cdFileBufferTable` | variable | CERTAIN |
| 0x801d2004 | `g_meshTableCounts` | uint16 | CERTAIN |
| 0x801d2008 | `g_chBinEntryTableBasePtr` | uint32 | CERTAIN |
| 0x801d200c | `g_chBinClutTablePtr` | uint32 | CERTAIN |
| 0x800a6778 | `g_charEffectTranslatePtr` | void* | CERTAIN |
| 0x800a677c | `g_charEffectScalePtr` | SVECTOR* | CERTAIN |
| 0x800a6786 | `g_charEffectInitFlag` | uint | CERTAIN |
| 0x801faa00 | `g_meshStreamPtrBuffer` | uint32[] | CERTAIN |
| 0x801faa40 | `g_meshOffsetBuffer` | uint16[] | CERTAIN |

### 31.5 Zones d'ombre résiduelles

| Élément | Statut | Action suivante |
|---------|--------|-----------------|
| `g_uvOrTexCoordBuffer` composante P | INCONNU | analyser `AnimCmd_BaseCulP` |
| `DAT_80092314` bytes sémantique (son?) | INCONNU | décompiler `FUN_80070afc` |
| `DAT_800884a0/b0` tables orientation 2D | PROBABLE 16×16 lookup | `read-memory` 256 bytes |
| `FUN_800264b8` post-SetCharacterAction | RÉSOLU → `UpdateActionHistory` | — |
| `FUN_8004d564` / `FUN_8004a2cc` | RÉSOLU → `TriggerCombatAction_BasicAttack` / `ApplyDamageToTarget` | — |
| `DAT_80092314` sémantique SFX | PROBABLE bankId/sampleId/vol | confirmé via `PlaySfxOnChannel` |
| `g_uvOrTexCoordBuffer` composante P | PROBABLE depth/pad write | pattern identique à X/Y/Z |
| `DAT_800884a0/b0` tables orientation 2D | INCONNU (nibble format ?) | décompiler `LookupOrientationFrame` |
| `DAT_8009a950` / `DAT_800c0808` | INCONNU | GTE scratch dans ComputeAnglesToTarget |
| `MATRIX_800923a8` | INCONNU | matrice GTE de travail |

---

## Section 32 — Helpers combat : actions, historique, dégâts, SFX

### 32.1 AnimCmd_BaseCulP — composante P confirmée

**Adresse :** 0x8003c080 | **Taille :** 1172 B | **Certitude :** CERTAIN

Symétrique de `AnimCmd_BaseCulX/Y/Z`. Accède au **slot d'index 3** dans chaque bloc de 4 shorts du tableau `g_uvOrTexCoordBuffer`.

```c
// Mode 0 — index direct
psVar8 = puVar11 + 3;          // P = offset+3 dans {X=0, Y=1, Z=2, P=3}
do {
    puVar11 += 4;              // stride = 4 shorts = 8 bytes par vertex
    sVar = ApplyMathOp(*psVar8, local_46 & 0xf, local_40[i]);
    *psVar8 = sVar;
    psVar8 += 4;               // vertex suivant
    local_46 >>= 4;            // nibble d'opcode suivant
} while (i < 4);               // 4 vertices par polygon
```

**Table des offsets — g_uvOrTexCoordBuffer par vertex :**

| Index short | Composante | Opcode BaseCul |
|-------------|-----------|----------------|
| +0 | X | `AnimCmd_BaseCulX` |
| +1 | Y | `AnimCmd_BaseCulY` |
| +2 | Z | `AnimCmd_BaseCulZ` |
| +3 | **P** | `AnimCmd_BaseCulP` |

**Certitude composante P :** CERTAIN (accès offset +3, stride +4 identique à X/Y/Z).  
**Sémantique P :** PROBABLE = depth-cue ou perspective pad. La valeur est modifiée par `ApplyMathOp` avec le même mécanisme que les coordonnées spatiales. Nom `P` conforme à SVECTOR PSY-Q (`pad` field réutilisé pour culling).

**Modes d'adressage :** identiques à BaseCulX (0x00 = index direct, 0x01 = par renderMetadata.byte3, 0x02 = scan 0x40 entrées par groupId).

---

### 32.2 UpdateActionHistory (FUN_800264b8)

**Adresse :** 0x800264b8 | **Taille :** 180 B | **Certitude :** CERTAIN  
**Unique caller :** `SetCharacterAction`

Appelé à chaque changement d'action. Met à jour deux shift-registers 8 bits dans `battleChars[0]` pour mémoriser l'historique des actions récentes.

```c
void UpdateActionHistory(GameState *gameState) {
    // Toujours : marque "action changée"
    battleChar->field_0xe4 |= 1;

    // Actions neutres → pas d'historique
    if (action == 0 || action == 2 || action == 10 || action == 0x2a) return;

    // Shift register field_0xea : suivi punch/kick
    field_0xea <<= 1;
    if (action == 0x13 || action == 0x14) field_0xea |= 1;  // punch ou kick

    // Shift register field_0xeb : suivi attaques ki/spéciales
    field_0xeb <<= 1;
    if (action == 0x26 || action == 0x27 || action == 0x28) field_0xeb |= 1;
}
```

**Table des preuves :**

| Offset | Accès | Signification | Certitude |
|--------|-------|---------------|-----------|
| battleChars[0]+0xe4 | write \|=1 | dirty flag action | CERTAIN |
| battleChars[0]+0xea | <<1, \|=1 cond. | shift-reg punch/kick (0x13/0x14) | CERTAIN |
| battleChars[0]+0xeb | <<1, \|=1 cond. | shift-reg ki/spécial (0x26/0x27/0x28) | CERTAIN |

**Usage probable :** détection de combos (N appuis consécutifs sur punch/ki dans fenêtre = pattern dans les 8 bits de field_0xea/eb).

---

### 32.3 TriggerCombatAction_BasicAttack (FUN_8004d564)

**Adresse :** 0x8004d564 | **Taille :** 584 B | **Certitude :** PROBABLE  
**Caller :** `DispatchCombatAction` cases 1 et 2

Cases 1 (attaque basique variante A) et 2 (variante B) du dispatch combat. Logique identique à `TriggerCombatAction_Case3` mais sans rand()%4 pour le SFX (toujours effectMode=1) et avec jitter d'amplitude différente.

```c
void TriggerCombatAction_BasicAttack(GameState *attacker, uint actionIndex, GameState *target) {
    // Lire facing depuis DAT_8009a864/868 (vecteur direction attaquant)
    // Jitter position : rand()%10 - 5 pour X/Z, rand()%10 - 0x23 pour Y
    SetAttackSFXAndColor(1);   // effectMode fixe = 1

    if (attacker->currentAction == 0x17 && task->gameState == target) {
        // Déjà en animation d'attaque contre cette cible → anim 2 (parade ?)
        SpawnSpriteEffectTask(&jitteredPos, facingVec, 2);
    } else {
        SpawnSpriteEffectTask(&jitteredPos, facingVec, 0);   // anim 0 = frappe basique
        SetCharacterAction(attacker, actionIndex);
        field_0x128 &= 0xfa640000;
        field_0x128 |= 0x100;   // flag "attaque basique engagée"
    }
}
```

**Table des preuves :**

| Accès | Valeur | Signification | Certitude |
|-------|--------|---------------|-----------|
| `DAT_8009a864` (4 bytes) | facing A | direction attaquant X/Z | PROBABLE |
| `DAT_8009a868` (4 bytes) | facing B | direction attaquant (complément) | PROBABLE |
| `SetAttackSFXAndColor(1)` | effectMode=1 | SFX groupe 1 (frappe légère) | CERTAIN |
| `field_0x128 \|= 0x100` | flag | "attaque basique active" | CERTAIN |
| `SpawnSpriteEffectTask(…, 0)` | anim 0 | sprite frappe basique | PROBABLE |
| `SpawnSpriteEffectTask(…, 2)` | anim 2 | sprite contre/parade | PROBABLE |

**Comparaison avec TriggerCombatAction_Case3 :**

| Paramètre | Case3 | Cases 1/2 |
|-----------|-------|-----------|
| effectMode | rand()%4 (1..4) | fixe = 1 |
| Jitter Y | rand()%0xC00-0x600 | rand()%10 - 0x23 |
| flag_0x128 | 0x1000/0x2000/0x800 | 0x100 |
| SFX table row | 0..3 | 0 |

---

### 32.4 ApplyDamageToTarget (FUN_8004a2cc)

**Adresse :** 0x8004a2cc | **Taille :** 1040 B | **Certitude :** CERTAIN  
**Callers :** 6 fonctions dont `DispatchCombatAction` (case 3), `FUN_8004aad4`, `FUN_8004ab40`, `FUN_8004ac60`, `FUN_8004c114`, `FUN_80050198`

Calcule et applique les dégâts en soustrayant une valeur de `battleChars[0xb].field_0x104` de la cible (le slot 0xb semble être la structure de stats de combat).

```c
void ApplyDamageToTarget(GameState *attacker) {
    // Étape 1 : mapper action → indice type de dommage
    int damageType;
    if (field_0x128 & 0x40000) damageType = 8;       // beam
    else if (field_0x128 & 0x80000) damageType = 9;  // état spécial
    else if (field_0x128 & 0x80) damageType = 10;    // garde
    else if (field_0x128 & 0x20000) damageType = 11; // aérien
    else switch (currentAction) {
        case 0x13: case 0x14: damageType = 0; break; // punch/kick basique
        case 0x21: damageType = 1; break;
        case 0x23: damageType = 6; break;
        case 0x24: damageType = 7; break;
        case 0x25: damageType = 5; break;
        case 0x26: damageType = 3; break;
        case 0x27: damageType = 4; break;
        case 0x28: damageType = 2; break;
        default:   damageType = -1; break;  // pas de dégâts
    }

    // Étape 2 : lire stat depuis table de l'adversaire
    uint dmg = 0;
    if (damageType != -1) {
        dmg = statTable[charSlot][damageType];  // gameState2->battleChars[0xb]
    }
    dmg *= 100;

    // Étape 3 : diviseurs selon flags
    if (damageType == 8) dmg /= 0x1c;   // beam = dégâts réduits
    if (damageType == 1) dmg >>= 3;     // action 0x21 = /8

    if (field_0x128 & 0x10) dmg /= 0x32;  // flag "tanking" = dégâts /50

    // Étape 4 : appliquer (HP -= dmg, plancher 0)
    target->battleChars[0xb].field_0x104[charSlot] -= (short)dmg;
    if (target->battleChars[0xb].field_0x104[charSlot] < 0)
        target->battleChars[0xb].field_0x104[charSlot] = 0;
}
```

**Table des preuves — mapping action → damageType :**

| currentAction | damageType | Interprétation | Certitude |
|---------------|------------|----------------|-----------|
| 0x13, 0x14 | 0 | punch/kick basique | CERTAIN |
| 0x21 | 1 | attaque spéciale légère (/8) | CERTAIN |
| 0x28 | 2 | — | CERTAIN |
| 0x26 | 3 | ki directionnel A | CERTAIN |
| 0x27 | 4 | ki directionnel B | CERTAIN |
| 0x25 | 5 | — | CERTAIN |
| 0x23 | 6 | — | CERTAIN |
| 0x24 | 7 | — | CERTAIN |
| flag 0x40000 | 8 (beam, /0x1c) | beam attack | CERTAIN |
| flag 0x80000 | 9 | état altéré | CERTAIN |
| flag 0x80 | 10 | garde | CERTAIN |
| flag 0x20000 | 11 | aérien | CERTAIN |

**Structure partielle accédée :**
- `gameState2->entityData.battleChars[0xb].field_0x104` = tableau de HP/valeurs par charSlot
- `attacker->entityData.battleChars[0].field_0x2b` = index du slot caractère attaquant
- Stride : `field_0x104 + charSlot * 0x14` → 20 bytes par slot

---

### 32.5 PlaySfxOnChannel (FUN_80070afc)

**Adresse :** 0x80070afc | **Taille :** 1004 B | **Certitude :** CERTAIN  
**Signature :** `int PlaySfxOnChannel(ushort channelId, short voiceIdx, ushort bankId, byte sampleId, undefined2 vol1, undefined2 vol2, short pitchLo, short pitchHi)`  
**Callers :** 8 fonctions (SetAttackSFXAndColor, ChaseCallAI, FUN_800630e4, FUN_80065408, FUN_80064aec)

Wrappeur SPU : alloue une voix via `SpuVmVSetUp`, configure les registres SPU en miroir RAM, puis démarre la lecture. Utilise un mutex `DAT_800c0678` (flag busy=1 pendant la programmation).

```c
int PlaySfxOnChannel(ushort channelId, short voiceIdx, ushort bankId, byte sampleId,
                     undefined2 vol1, undefined2 vol2, short pitchLo, short pitchHi) {
    if (DAT_800c0678 == 1) return -1;  // canal occupé
    DAT_800c0678 = 1;

    if (channelId >= 0x18) goto fail;  // max 24 canaux

    int voiceHandle = SpuVmVSetUp(voiceIdx, bankId);
    if (voiceHandle != 0) goto fail;   // allocation voix échouée

    // Calculer panning 0..0x7f
    if (pitchLo == pitchHi) pan = '@';           // 0x40 = centre
    else if (pitchHi < pitchLo) pan = (pitchHi<<6)/pitchLo;
    else pan = 0x7f - (pitchLo<<6)/pitchHi;

    // Configurer miroir registres SPU (0x800c490x)
    DAT_800c4906 = vol1_lo;
    DAT_800c4907 = vol2_lo;
    DAT_800c4908 = pitchLo;
    DAT_800c4909 = pan;
    DAT_800c4904 = sampleTable[bankId*4];       // adresse sample
    DAT_800c4910 = sampleId;
    // ...

    // Copier vers slot canal (DAT_800a6d5c + channelId*0x18)
    slot[channelId].sampleAddr = DAT_800c491c;
    slot[channelId].bankId = bankId;
    slot[channelId].sampleId = sampleId;
    // ...

    FUN_8006d0d8();  // flush SPU registers
    DAT_800c0678 = 0;
    return channelId;

fail:
    DAT_800c0678 = 0;
    return -1;
}
```

**Table paramètres depuis SetAttackSFXAndColor :**

| Paramètre | Source | Valeurs observées |
|-----------|--------|-------------------|
| channelId | `DAT_8009a91c` (cycles 0x12..0x14) | 18, 19, 20 |
| voiceIdx | `*(short*)(charData->field_0xb + 1)` | dépend du personnage |
| bankId | `DAT_80092314[entry*4+0]` (byte) | 0x00..0x06 |
| sampleId | `DAT_80092314[entry*4+1]` (byte) | 0x00..0x07 |
| vol1 | résultat `FUN_80071434(…)` | paramètre enveloppe |
| vol2 | 0 | — |
| pitchLo | 0xff | max |
| pitchHi | 0xff | max |

**DAT_80092314 layout (8 entrées × 4 bytes) — mémoire @ 0x800923a4 :**
```
Entry 0 : bankId=0x00, sampleId=0x00, vol=0x18, pad=0x00
Entry 1 : bankId=0x00, sampleId=0x01, vol=0x19, pad=0x00
Entry 2 : bankId=0x00, sampleId=0x02, vol=0x1A, pad=0x00
Entry 3 : bankId=0x00, sampleId=0x03, vol=0x1B, pad=0x00
Entry 4 : bankId=0x00, sampleId=0x04, vol=0x1C, pad=0x00
Entry 5 : bankId=0x00, sampleId=0x05, vol=0x1D, pad=0x00
Entry 6 : bankId=0x00, sampleId=0x06, vol=0x1F, pad=0x00
Entry 7 : bankId=0x00, sampleId=0x07, vol=0x23, pad=0x00
```
**Certitude :** PROBABLE (bankId=0 pour tous → banque unique; sampleId=0..7 = 8 SFX de frappe distincts; vol croissant 0x18→0x23 = intensité croissante).

---

### 32.6 DAT_800884a0/b0 — Tables orientation sprites

**Addresses :** 0x800884a0 (table A), 0x800884b0 (table B, +16 bytes)  
**Utilisé par :** `LookupOrientationFrame` @ 0x80045d34

**Données lues (0x800884a0, 256 bytes) :**
```
800884a0: DD 22 42 0D 00 00 00 00  00 00 1D 11 13 31 13 11
800884b0: 11 11 21 D2 DD CD CB DD  DD 42 44 0D 00 00 00 00
800884c0: 00 00 1D 13 11 11 11 11  11 33 43 22 DD CD CB DD
...
```

**Observation :** toutes les valeurs lues comme nibbles (demi-octets 4 bits) sont dans la plage 0x0..0xD (0 à 13). Les valeurs comme 0xDD = {13,13}, 0x22 = {2,2}, 0x42 = {4,2}, 0x0D = {0,13}, etc.

**Hypothèse de format (PROBABLE :**
- Table de 16×16 = 256 index de frames, encodés en nibbles (2 par byte = 128 bytes pour 256 entrées)
- Valeurs 0xE/0xF absentes → max 14 frames valides (indices 0..13)
- DAT_800884a0 = table de 16 lignes de 16 cases, nibble-packed → complète en 128 bytes

**Certitude format :** INCONNU — le décompilé de `LookupOrientationFrame` devra confirmer si l'accès est par byte ou nibble. La valeur 0xDD (=221) comme index byte dans `DAT_800884b0[0xDD * 16 + col]` dépasserait toute table raisonnable, ce qui renforce l'hypothèse nibble.

**Action recommandée :** décompiler `LookupOrientationFrame` (0x80045d34, 388 bytes).

---

### 32.7 Inventaire des renames session 32

| Adresse | Ancien nom | Nouveau nom | Certitude |
|---------|-----------|-------------|-----------|
| 0x800264b8 | `FUN_800264b8` | `UpdateActionHistory` | CERTAIN |
| 0x8004a2cc | `FUN_8004a2cc` | `ApplyDamageToTarget` | CERTAIN |
| 0x8004d564 | `FUN_8004d564` | `TriggerCombatAction_BasicAttack` | PROBABLE |
| 0x80070afc | `FUN_80070afc` | `PlaySfxOnChannel` | CERTAIN |

---

### 32.8 Zones d'ombre résiduelles

| Élément | Statut | Action suivante |
|---------|--------|-----------------|
| `DAT_800884a0/b0` nibble format | RÉSOLU → 2 tables byte, format ci-dessous | — |
| `FUN_80071434` (SetSfxParam) | RÉSOLU → `SetChannelVolume` | — |
| `FUN_8004aad4` | RÉSOLU → `TriggerHitAndDamage` | — |
| `FUN_8004ab40` | RÉSOLU → `TriggerKiHitAndDamage` | — |
| `DAT_8009a864/868` facing vectors | INCONNU | identifier (short[2]? SVECTOR?) |
| `FUN_8004ac60` | INCONNU | caller ApplyDamageToTarget, condition flag 0x80000 |
| `battleChars[0xb].field_0x104` | PROBABLE HP table | stride 0x14, short array |
| `DAT_8009a950` / `DAT_800c0808` | INCONNU | GTE scratch dans ComputeAnglesToTarget |
| `MATRIX_800923a8` | INCONNU | matrice GTE de travail |

---

## Section 33 — Orientation sprites, pipeline SFX, pipeline hit/damage

### 33.1 LookupOrientationFrame — format tables résolu

**Adresse :** 0x80045d34 | **Taille :** 388 B | **Certitude :** CERTAIN  
**Callers :** DamageNumberTaskLoop (×2), TriggerCombatAction_Case3, TriggerCombatAction_DirKi, FUN_80054fe4, FUN_80042a18 (×2)

Décompilé intégral (21 lignes). Implémente le lookup d'orientation sprite pour billboards.

```c
undefined1 LookupOrientationFrame(ushort angleX, short angleY) {
    // Étape 1 : bande d'élévation (4 bits = 0..15)
    ushort row = ((SVECTOR_1f80007c.vy + angleY) >> 8) & 0xF;

    // Étape 2 : colonne azimut (dérivée)
    ushort col;
    if (row < 5 || row > 10) {
        col = ((angleX - SVECTOR_1f80007c.vx) >> 8);   // vue avant : soustrait caméra
    } else {
        col = ((angleX + SVECTOR_1f80007c.vx) >> 8);   // vue arrière : ajoute caméra
    }
    col &= 0xF;

    // Correction octants ±45° : vue de côté → ignorer caméra
    if ((2 < row && row < 5) || (10 < row && row < 13)) {
        col = (angleX & 0xFFF) >> 8;    // direct, pas de correction caméra
    }

    // Étape 3 : lookup 2-level
    return g_spriteOrientationFrameTable[g_spriteOrientationRowTable[row] * 16 + col];
}
```

**Table des deux niveaux :**

| Symbole | Adresse | Taille | Rôle | Certitude |
|---------|---------|--------|------|-----------|
| `g_spriteOrientationRowTable` | 0x800884a0 | 16 bytes | rowIndex[0..15] → base row dans frame table | CERTAIN |
| `g_spriteOrientationFrameTable` | 0x800884b0 | ≤4096 bytes | byte table 2D : frameIndex = table[row*16+col] | CERTAIN |

**Données g_spriteOrientationRowTable (0x800884a0) :**
```
Row  0 = 0xDD (221)  → élévation "dessus" — beaucoup de frames de plongée
Row  1 = 0x22 (34)
Row  2 = 0x42 (66)
Row  3 = 0x0D (13)
Row  4..9 = 0x00     → élévation neutre (vue de face)
Row 10 = 0x1D (29)
Row 11 = 0x11 (17)
Row 12 = 0x13 (19)
Row 13 = 0x31 (49)
Row 14 = 0x13 (19)
Row 15 = 0x11 (17)
```

**Logique azimut (CERTAIN) :**

| Bande row | Vue | Calcul col |
|-----------|-----|------------|
| 0..1 | dessus front | `(angleX - cam.vx) >> 8` |
| 2..4 | côté 45° haut | `(angleX & 0xFFF) >> 8` (direct) |
| 5..10 | neutre/arrière | `(angleX + cam.vx) >> 8` |
| 11..12 | côté 45° bas | `(angleX & 0xFFF) >> 8` (direct) |
| 13..15 | dessous | `(angleX + cam.vx) >> 8` |

**Format résolu :** PAS de nibbles. Deux tables en bytes indépendantes. La table principale `g_spriteOrientationFrameTable` peut atteindre `0xDD * 16 + 15 = 0xDDF = 3551 bytes` au minimum (row max = 0xDD).

**Labels créés dans Ghidra :**
- `g_spriteOrientationRowTable` @ 0x800884a0
- `g_spriteOrientationFrameTable` @ 0x800884b0

---

### 33.2 SetChannelVolume (FUN_80071434)

**Adresse :** 0x80071434 | **Taille :** 156 B | **Certitude :** CERTAIN  
**Signature :** `undefined4 SetChannelVolume(ushort channelId, short volL, short volR)`  
**Callers :** 16 fonctions dont SetAttackSFXAndColor, FUN_80062894, FUN_800630e4

Configure le volume L/R d'un canal SPU dans la table miroir RAM.

```c
undefined4 SetChannelVolume(ushort channelId, short volL, short volR) {
    if (channelId >= 0x18) return 0xFFFFFFFF;  // erreur : max 24 canaux

    DAT_800a6bc4[channelId * 8] = volL * 0x81;  // volume L (SPU scale 0x81)
    DAT_800a6bc6[channelId * 8] = volR * 0x81;  // volume R
    DAT_800a6d44[channelId] |= 3;               // dirty bits L+R → flush SPU
    return 0;
}
```

**Table des preuves :**

| Accès | Signification | Certitude |
|-------|---------------|-----------|
| `DAT_800a6bc4[channelId*8]` | registre volume L SPU (miroir RAM) | CERTAIN |
| `DAT_800a6bc6[channelId*8]` | registre volume R SPU (miroir RAM) | CERTAIN |
| `DAT_800a6d44[channelId] \|= 3` | dirty flags bits 0+1 = L+R pending | CERTAIN |
| `volX * 0x81 = volX * 129` | conversion linéaire → format SPU 15 bits | CERTAIN |

**Usage type (SetAttackSFXAndColor) :**
```c
SetChannelVolume(DAT_8009a91c,   // canal en cours (0x12..0x14)
    entityData.entityFlags >> 3, // vol L depuis flags d'entité
    entityData.entityFlags >> 3  // vol R (mono = identique)
);
```

**Valeurs courantes dans FUN_80062894 (init) :**
- Canaux 0x11..0x14 → vol = 0x40 (64) → 0x40 × 0x81 = 0x2040 ≈ 60% max
- Canaux 0x15..0x16 → vol = 0x38 (56) → légèrement plus faibles

---

### 33.3 TriggerHitAndDamage (FUN_8004aad4)

**Adresse :** 0x8004aad4 | **Taille :** 108 B | **Certitude :** CERTAIN  
**Callers :** `FUN_8004b25c` (actions 0x13/0x14 = punch/kick), `FUN_8004cc18` (action 0x17)

Wrappeur 3 étapes : SetCharacterAction → flag 0x1 → ApplyDamageToTarget.

```c
void TriggerHitAndDamage(GameState *gameState, uint actionIndex) {
    SetCharacterAction(gameState, actionIndex);
    field_0x128 |= 1;              // flag "hit physique confirmé"
    ApplyDamageToTarget(gameState);
}
```

**Flag 0x1 :** différent de la condition dans ApplyDamageToTarget (qui teste 0x40000/0x80000/0x80/0x20000). Ce flag est probablement lu par d'autres systèmes (animation de choc, compteur combo).

---

### 33.4 TriggerKiHitAndDamage (FUN_8004ab40)

**Adresse :** 0x8004ab40 | **Taille :** 108 B | **Certitude :** CERTAIN  
**Callers :** `FUN_8004b25c` (actions 0x26/0x27/0x28), `FUN_8004bb90`, `FUN_8004bf00`, `FUN_8004c5a4`, `FUN_8004e0c8` (×2)

Structure symétrique à `TriggerHitAndDamage`, flag différent.

```c
void TriggerKiHitAndDamage(GameState *gameState, uint actionIndex) {
    SetCharacterAction(gameState, actionIndex);
    field_0x128 |= 8;              // flag "hit ki confirmé"
    ApplyDamageToTarget(gameState);
}
```

**Comparaison flags :**

| Fonction | Flag | Type de hit |
|----------|------|-------------|
| `TriggerHitAndDamage` | 0x1 | physique (punch/kick 0x13/0x14) |
| `TriggerKiHitAndDamage` | 0x8 | ki (0x26/0x27/0x28) |
| `TriggerCombatAction_BasicAttack` | 0x100 | attaque basique en cours |
| `TriggerCombatAction_Case3` | 0x1000/0x2000/0x800 | effets spéciaux |
| `TriggerCombatAction_DirKi` | 0x400 | tracker adversaire |

**Callers communs FUN_8004b25c :** dispatch gate entre actions 0x13/0x14 → TriggerHitAndDamage et actions 0x26..0x28 → TriggerKiHitAndDamage. Confirme que FUN_8004b25c est un **Combat Action Gate** — à analyser session 34.

---

### 33.5 Inventaire renames session 33

| Adresse | Ancien nom | Nouveau nom | Certitude |
|---------|-----------|-------------|-----------|
| 0x80071434 | `FUN_80071434` | `SetChannelVolume` | CERTAIN |
| 0x8004aad4 | `FUN_8004aad4` | `TriggerHitAndDamage` | CERTAIN |
| 0x8004ab40 | `FUN_8004ab40` | `TriggerKiHitAndDamage` | CERTAIN |
| 0x800884a0 | `DAT_800884a0` | `g_spriteOrientationRowTable` | CERTAIN |
| 0x800884b0 | `DAT_800884b0` | `g_spriteOrientationFrameTable` | CERTAIN |

---

### 33.6 Zones d'ombre résiduelles

| Élément | Statut | Action suivante |
|---------|--------|-----------------|
| `FUN_8004b25c` combat action gate | INCONNU | décompiler (dispatch 0x13/0x14 vs 0x26..0x28) |
| `FUN_8004ac60` | INCONNU | caller ApplyDamageToTarget sous flag 0x80000 |
| `FUN_8004cc18` → `TriggerHitAndDamage(gs, 0x17)` | INCONNU | contexte action 0x17 |
| `FUN_8004bb90` / `FUN_8004bf00` / `FUN_8004c5a4` | INCONNU | callers TriggerKiHitAndDamage |
| `FUN_8004e0c8` → `TriggerKiHitAndDamage(p1, 0x28)` | INCONNU | contexte action 0x28 |
| `DAT_8009a864/868` | INCONNU | facing vectors TriggerCombatAction_BasicAttack |
| `battleChars[0xb].field_0x104` | PROBABLE HP table | stride 0x14, short array |
| `DAT_8009a950` / `DAT_800c0808` | INCONNU | GTE scratch ComputeAnglesToTarget |
| `MATRIX_800923a8` | INCONNU | matrice GTE de travail |

---

## Section 34 — Validation disque CH_BIN : entrée 28B, listes de segments, AnimStream

### 34.1 Résumé factuel

- Le mapping disque des pointeurs CH_BIN est **revalidé** : `file_offset = ptr_compile_time - 0x801A3800`.
- `CHBinMeshEntry` reste une structure de **28 bytes** (`7 x uint32`).
- Les champs `+0x0C`, `+0x10`, `+0x14` ne pointent pas vers un bloc plat unique mais vers des **listes de segments**.
- `+0x0C` = liste de segments vertex `{ptr_vertices, counts_packed}` de stride 8.
- `+0x10` = liste de segments mesh `{ptr_primitive_indices, ptr_uv_table, ptr_color_table, counts_packed}` de stride 16.
- `+0x14` = liste de segments lighting `{ptr_lighting_values, counts_packed}` de stride 8.
- `+0x18` contient bien du **bytecode AnimStream**: les mots 16 bits observés produisent des opcodes déjà identifiés (`0x06`, `0x08`, `0x0D`, etc.) en little-endian.
- `CHBinMeshEntry` a été mis à jour dans Ghidra, et trois structures partielles `/CHBin` existent maintenant pour les listes de segments.

### 34.2 Table des preuves — CHBinMeshEntry E3 (CH_01.BIN)

Fichier : `data/CH_BIN1/CH_01.BIN`  
Entrée examinée : **E3** @ `foff 0x1298`

| Offset | Valeur brute | Accès observé / interprétation minimale | Certitude |
|--------|--------------|------------------------------------------|-----------|
| +0x00 | `0x00000100` | `entry_id_packed` | CERTAIN |
| +0x04 | `0x00010001` | `primitive_count_packed` | CERTAIN |
| +0x08 | `0x00010001` | `unknown_0x08` | CERTAIN pour la valeur, INCONNU pour la sémantique |
| +0x0C | `0x801A38BC` | pointeur compile-time vers liste de segments vertex | CERTAIN |
| +0x10 | `0x801A3E78` | pointeur compile-time vers liste de segments mesh | CERTAIN |
| +0x14 | `0x801A3ED0` | pointeur compile-time vers liste de segments lighting | CERTAIN |
| +0x18 | `0x801A4654` | pointeur compile-time vers stream AnimStream | CERTAIN |

**Preuve du mapping pointeur → fichier :**

```text
compile_time_base = 0x801A3800

0x801A38BC - 0x801A3800 = 0x00BC
0x801A3E78 - 0x801A3800 = 0x0678
0x801A3ED0 - 0x801A3800 = 0x06D0
0x801A4654 - 0x801A3800 = 0x0E54
```

### 34.3 Table des preuves — listes de segments pointées

#### A. Liste vertex @ `ptr_vertex_segment_list = 0x801A38BC` → `foff 0x00BC`

Dump 32-bit observé :

```text
0x00BC: 0x801A3818, 0x000100FF
0x00C4: 0x801A381C, 0x00040006
0x00CC: 0x801A382C, 0x00040010
0x00D4: 0x801A383C, 0x00200001
```

| Sous-offset | Accès | Type minimal | Preuve |
|------------|-------|--------------|--------|
| +0x00 | pointeur compile-time | `uint` | valeurs `0x801A3818`, `0x801A381C`, `0x801A382C`, `0x801A383C` |
| +0x04 | compteur packé | `uint` | valeurs `0x000100FF`, `0x00040006`, `0x00040010`, `0x00200001` |

**Structure partielle CERTAIN :**

```c
typedef struct CHBinVertexSegmentEntry {
    uint ptr_vertices;     // +0x00 compile-time pointer
    uint counts_packed;    // +0x04 high/low halves used by RenderBattleScene3D
} CHBinVertexSegmentEntry; // 8 bytes
```

#### B. Liste mesh @ `ptr_mesh_segment_list = 0x801A3E78` → `foff 0x0678`

Dump 32-bit observé :

```text
0x0678: 0x801A3C30, 0x801A3C3C, 0x801A3DC4, 0x00010001
0x0688: 0x00000000, 0x00000000, 0x000100F6, 0x00010001
```

Le premier bloc a exactement le pattern lu par `RenderBattleScene3D` :

| Sous-offset | Rôle minimal | Certitude | Preuve |
|------------|--------------|-----------|--------|
| +0x00 | `ptr_primitive_indices` | CERTAIN | lu comme `*local_c8` |
| +0x04 | `ptr_uv_table` | CERTAIN | lu comme `local_c8[1]` |
| +0x08 | `ptr_color_table` | CERTAIN | lu comme `local_c8[2]` |
| +0x0C | `counts_packed` | CERTAIN | lu comme `local_c8[3]` |

**Structure partielle CERTAIN :**

```c
typedef struct CHBinMeshSegmentEntry {
    uint ptr_primitive_indices;  // +0x00
    uint ptr_uv_table;           // +0x04
    uint ptr_color_table;        // +0x08
    uint counts_packed;          // +0x0C
} CHBinMeshSegmentEntry; // 16 bytes
```

#### C. Liste lighting @ `ptr_lighting_segment_list = 0x801A3ED0` → `foff 0x06D0`

Dump 32-bit observé :

```text
0x06D0: 0x801A3EA0, 0x00010001
0x06D8: 0x801A3EA8, 0x00010008
0x06E0: 0x801A3EB0, 0x00010001
```

| Sous-offset | Rôle minimal | Certitude | Preuve |
|------------|--------------|-----------|--------|
| +0x00 | `ptr_lighting_values` | CERTAIN | lu comme `*local_c0` |
| +0x04 | `counts_packed` | CERTAIN | lu comme `local_c0[1]` |

**Structure partielle CERTAIN :**

```c
typedef struct CHBinLightingSegmentEntry {
    uint ptr_lighting_values;  // +0x00
    uint counts_packed;        // +0x04
} CHBinLightingSegmentEntry; // 8 bytes
```

### 34.4 Preuve directe que `ptr_anim_stream` pointe vers AnimStream

Exemples de mots 16 bits lus à plusieurs cibles `+0x18` dans `CH_01.BIN` :

```text
0x801A3EE8 -> foff 0x06E8: 0000 0001 1506 2008 0000 0000 0000 1606 2008 0003 ...
0x801A4654 -> foff 0x0E54: 0000 0001 0000 0001 1706 2108 0000 0000 0000 1706 ...
0x801A485C -> foff 0x105C: 0000 0001 0000 0001 1206 2108 0000 0000 0000 1206 ...
0x801A4990 -> foff 0x1190: 0000 0001 0000 0001 200D 0104 7FE8 000A 1108 0000 ...
```

En little-endian, les mots `0x1506`, `0x1706`, `0x1206`, `0x200D`, `0x1108` codent des opcodes bas-byte `0x06`, `0x06`, `0x06`, `0x0D`, `0x08`, cohérents avec le VM AnimStream déjà documenté.

**Conclusion :** `ptr_anim_stream` est **CERTAIN**.

### 34.5 Structure partielle consolidée

```c
typedef struct CHBinMeshEntry {
    uint entry_id_packed;             // +0x00 CERTAIN
    uint primitive_count_packed;      // +0x04 CERTAIN
    uint unknown_0x08;                // +0x08 INCONNU (valeurs récurrentes observées)
    uint ptr_vertex_segment_list;     // +0x0C CERTAIN
    uint ptr_mesh_segment_list;       // +0x10 CERTAIN
    uint ptr_lighting_segment_list;   // +0x14 CERTAIN
    uint ptr_anim_stream;             // +0x18 CERTAIN
} CHBinMeshEntry; // 0x1C
```

### 34.6 CERTAIN / PROBABLE / INCONNU

**CERTAIN**
- `CHBinMeshEntry` = 28 bytes, 7 dwords.
- `file_offset = ptr_compile_time - 0x801A3800`.
- `+0x0C` = liste de segments vertex, stride 8.
- `+0x10` = liste de segments mesh, stride 16.
- `+0x14` = liste de segments lighting, stride 8.
- `+0x18` = flux AnimStream.

**PROBABLE**
- `primitive_count_packed` encode le compteur de primitives et possiblement un second sous-compte dans le high16.
- `counts_packed` dans les listes encode systématiquement deux demi-mots de contrôle utilisés par les itérateurs.

**INCONNU**
- Sémantique exacte de `unknown_0x08`.
- Sémantique exacte des high16/low16 de `counts_packed` dans tous les types de segments.
- Format de la section header `[5]` quand elle n'est pas atteinte via une entrée classique.

### 34.7 Actions Ghidra recommandées

1. Appliquer `CHBinMeshEntry` sur la table `g_chBinEntryTableBasePtr` et relire `RenderBattleScene3D` avec les nouveaux noms.
2. Appliquer `CHBinVertexSegmentEntry`, `CHBinMeshSegmentEntry`, `CHBinLightingSegmentEntry` sur les listes pointées par `E3` et `E4` pour voir si les segments suivants gardent le même stride.
3. Tracer `unknown_0x08` par comparaison multi-fichiers (`CH_01`, `CH_02`, `IN_01`) afin de vérifier s’il pilote le nombre de segments ou un type de mesh.

---

## Section 35 — Comparaison multi-fichiers : classes d'entrée, header commun, texture vs ptr5

### 35.1 Résumé factuel

- `unknown_0x08` prend, sur les fichiers comparés, principalement deux valeurs : `0x00000000` ou `0x00010001`.
- Dans `CH_01.BIN`, les entrées `E0,E1,E2,E9,E23` ont `unknown_0x08 = 0`.
- `E0,E1,E2` sont des entrées de type partage/global, avec `id=0`, `prim=0`, et les mêmes listes de segments.
- `E9` et `E23` sont des entrées spéciales à `prim=1`, qui réutilisent aussi les mêmes listes de segments partagées.
- `CH_02.BIN` et `IN_01.BIN` n'ont, sur les entrées observées, que `unknown_0x08 = 0x00010001`.
- `counts_packed` peut maintenant être classé **CERTAIN** au niveau minimal : c'est un paquet `2 x u16` servant d'état/rechargement `countX/countY` pour les itérateurs 2D.
- Un `CHBinFileHeaderCommon` partiel a été créé et appliqué en Ghidra à `0x801A3800`.
- Les scans d'opcodes texture (`load_set`/`tex_set`) sur le corpus complet montrent des index de table plausibles `3`, `4` et `5`.

### 35.2 Table des preuves — `unknown_0x08`

#### CH_01.BIN

| Entrée | id_packed | primitive_count_packed | unknown_0x08 | Observations |
|--------|-----------|------------------------|--------------|--------------|
| E00 | `0x00000000` | `0x00000000` | `0x00000000` | shared/header-like |
| E01 | `0x00000000` | `0x00000000` | `0x00000000` | shared/header-like |
| E02 | `0x00000000` | `0x00000000` | `0x00000000` | shared/header-like |
| E03 | `0x00000100` | `0x00010001` | `0x00010001` | entrée mesh normale |
| E09 | `0x00080700` | `0x00000001` | `0x00000000` | spéciale, 1 primitive, listes partagées |
| E23 | `0x00080800` | `0x00000001` | `0x00000000` | spéciale, 1 primitive, listes partagées |

Agrégation complète observée sur `CH_01.BIN` :

```text
u08=00000000 -> entries 0,1,2,9,23
u08=00010001 -> entries 3,4,5,6,7,8,10..22,24..36
```

#### CH_02.BIN

Toutes les entrées observées (`E00..E05`) ont `unknown_0x08 = 0x00010001`.

#### IN_01.BIN

Toutes les entrées observées (`E00..E11`) ont `unknown_0x08 = 0x00010001`.

**Classification :**

- **CERTAIN** : `unknown_0x08` sépare au moins deux classes binaires d'entrée (`0` et `0x00010001`).
- **PROBABLE** : `0` marque des entrées spéciales/partagées, tandis que `0x00010001` marque les entrées mesh normales.
- **INCONNU** : sémantique exacte du bitfield ou du double demi-mot.

### 35.3 `counts_packed` — statut relevé d'un cran

La preuve issue des itérateurs est suffisante pour durcir le niveau de certitude :

```c
countX--;
if (countX == 0) {
    countY--;
    if (countY == 0) {
        // avance la liste de segments
        // relit nouveau pointeur + nouveaux compteurs
    } else {
        // reste sur le segment courant
        // relit countX pour la row suivante
    }
}
return (countX << 16) | countY;
```

**Conclusion minimale CERTAIN :**

| Structure | Champ | Sens minimal CERTAIN |
|-----------|-------|----------------------|
| `CHBinVertexSegmentEntry` | `counts_packed` | paquet `countX/countY` pour `IterateMeshStreamAndFetch` |
| `CHBinMeshSegmentEntry` | `counts_packed` | paquet `countX/countY` pour `IterateMeshStreamAndFetch_Offset16` |
| `CHBinLightingSegmentEntry` | `counts_packed` | paquet `countX/countY` pour `IterateMeshStreamAndFetch_Offset8` |

**INCONNU conservé :** affectation exacte hi/lo dans tous les chemins de reload, malgré une forte cohérence avec `hi=countX`, `lo=countY`.

### 35.4 Header commun CH_BIN — structure Ghidra créée

Structure ajoutée en Ghidra :

```c
typedef struct CHBinFileHeaderCommon {
    ushort reloc_loop_bound;  // +0x00 CERTAIN
    ushort header_flags;      // +0x02 CERTAIN
    uint   entry_count;       // +0x04 CERTAIN
    uint   ptr_entry_table;   // +0x08 CERTAIN
    uint   ptr_section_3;     // +0x0C PROBABLE texture/CLUT-related
    uint   ptr_section_4;     // +0x10 PROBABLE texture image data
    uint   ptr_section_5;     // +0x14 INCONNU
} CHBinFileHeaderCommon;
```

Appliquée à `0x801A3800` dans Ghidra.

### 35.5 Texture opcodes — indices de table observés

Scan sur les streams `ptr_anim_stream` non nuls de `CH_01.BIN`, `CH_02.BIN`, `IN_01.BIN`.

Occurrences filtrées utiles :

```text
CH_01 E1/E2 : tex_set  -> tbl=3
CH_01 E0/E4/E7/E9 : load_set -> tbl=4
CH_02 E0        : load_set -> tbl=3 et tbl=4
IN_01          : aucun hit fiable vers tbl=5 dans l'échantillon
```

**Extension corpus complet (`data/CH_BIN*/*.BIN`) :**

Agrégation des hits plausibles `load_set` / `tex_set` :

```text
load_set tbl=0 : 308  (beaucoup de faux positifs probables)
load_set tbl=3 : 64
load_set tbl=4 : 50
load_set tbl=5 : 14
tex_set  tbl=3 : 19
tex_set  tbl=4 : 3
```

Exemples plausibles `load_set tbl=5` :

```text
CH_11.BIN E0 : 0103 02C0 0100 0040 0098 0005 0000
CH_15.BIN E0 : 0103 02C0 0100 0040 0098 0005 0000
CH_23.BIN E0 : 0103 02C0 0198 0020 0068 0005 0000
IN_08.BIN E0 : 0103 02C0 0100 0040 0100 0005 0000
```

Exemples plausibles `tex_set tbl=4` :

```text
CH_04.BIN E1 : 810B 0004 0000 02D0 01FF 0200 8008
CH_38.BIN E1 : 810B 0004 0000 02D0 01FF 0000 0000
CH_38.BIN E2 : 810B 0004 0000 02D0 01FF 0000 0000
```

**Conclusion révisée :**

- **PROBABLE** : `ptr_section_3` alimente un chemin texture/CLUT et sert de source récurrente pour `tex_set tbl=3`.
- **PROBABLE** : `ptr_section_4` est une banque image/texture utilisable par `load_set tbl=4` et plus rarement par `tex_set tbl=4`.
- **PROBABLE** : `ptr_section_5` est une banque image/texture secondaire accessible par `load_set tbl=5` dans plusieurs fichiers.
- **INCONNU** : différence exacte de rôle entre `ptr_section_4` et `ptr_section_5`, et raison précise de la rareté de `tex_set tbl=4`.

### 35.6 Structure partielle consolidée après session 35

```c
typedef struct CHBinMeshEntry {
    uint entry_id_packed;             // +0x00 CERTAIN
    uint primitive_count_packed;      // +0x04 CERTAIN
    uint unknown_0x08;                // +0x08 CERTAIN (valeurs observées), sémantique INCONNUE
    uint ptr_vertex_segment_list;     // +0x0C CERTAIN
    uint ptr_mesh_segment_list;       // +0x10 CERTAIN
    uint ptr_lighting_segment_list;   // +0x14 CERTAIN
    uint ptr_anim_stream;             // +0x18 CERTAIN
} CHBinMeshEntry;
```

### 35.7 Zones d'ombre restantes

| Élément | Statut | Action suivante |
|---------|--------|-----------------|
| `unknown_0x08` sémantique exacte | INCONNU | trouver un lecteur hors `RenderBattleScene3D` / `AnimCmd_RenderEntryGroup` |
| différence `ptr_section_4` vs `ptr_section_5` | INCONNU | comparer formats bruts et dimensions VRAM des `load_set tbl=4` vs `tbl=5` |
| hi/lo exact de `counts_packed` dans tous les chemins | PROBABLE | relire l'ASM des trois itérateurs avec les structures appliquées |
| entrées spéciales `E9/E23` de CH_01 | INCONNU | comparer leur rendu/usage aux entrées normales |

---

## Section 36 — Rôles probables des sections header `[3]`, `[4]`, `[5]`

### 36.1 Résumé factuel

- Le scan corpus complet des AnimStreams produit des hits plausibles `load_set tbl=3/4/5` et `tex_set tbl=3/4`.
- Dans l'échantillon actuel, aucun hit plausible `tex_set tbl=5` n'a été trouvé.
- Les pointeurs header `ptr_section_4` et `ptr_section_5` mènent, sur plusieurs fichiers (`CH_11.BIN`, `CH_23.BIN`, `IN_08.BIN`), vers des zones brutes denses compatibles avec des blocs image plutôt qu'avec de simples tables d'index.
- Les dimensions VRAM portées par les `load_set tbl=4` et `load_set tbl=5` sont toutes plausibles pour des uploads texture.
- La séparation certaine aujourd'hui porte sur les usages observés, pas encore sur un format interne complètement reconstruit.

### 36.2 Table des preuves

| Section | Accès observé | Type minimal | Fichiers / fonctions | Preuve |
|--------|---------------|--------------|----------------------|--------|
| `ptr_section_3` | `tex_set tbl=3`, `load_set tbl=3` | source texture/CLUT | corpus CH_BIN, `AnimCmd_AsyncLoadTexture`, `AnimCmd_LoadTexture` | index `3` vu dans plusieurs streams plausibles |
| `ptr_section_4` | `load_set tbl=4`, `tex_set tbl=4` | banque image/texture | `CH_01.BIN`, `CH_02.BIN`, `CH_04.BIN`, `CH_38.BIN` | indices `4` vus en sync et async |
| `ptr_section_5` | `load_set tbl=5` | banque image/texture secondaire | `CH_11.BIN`, `CH_15.BIN`, `CH_23.BIN`, `IN_08.BIN` | séquences plausibles `... 0005 0000` + blocs bruts denses |

### 36.3 Structure partielle du header

```c
typedef struct CHBinFileHeaderCommon {
    ushort reloc_loop_bound;  // +0x00 CERTAIN
    ushort header_flags;      // +0x02 CERTAIN
    uint   entry_count;       // +0x04 CERTAIN
    uint   ptr_entry_table;   // +0x08 CERTAIN
    uint   ptr_section_3;     // +0x0C PROBABLE source texture/CLUT, async récurrente
    uint   ptr_section_4;     // +0x10 PROBABLE banque image/texture, sync + async rare
    uint   ptr_section_5;     // +0x14 PROBABLE banque image/texture secondaire, sync observé
} CHBinFileHeaderCommon;
```

### 36.4 CERTAIN / PROBABLE / INCONNU

**CERTAIN**
- `AnimCmd_LoadTexture` et `AnimCmd_AsyncLoadTexture` sélectionnent une source via un index de table.
- Des hits plausibles existent pour `load_set tbl=3`, `4`, `5`.
- Des hits plausibles existent pour `tex_set tbl=3`, `4`.

**PROBABLE**
- `ptr_section_3` joue le rôle de source texture/CLUT principale pour le chemin async.
- `ptr_section_4` et `ptr_section_5` sont deux banques image distinctes, toutes deux utilisables par le chemin sync.
- `ptr_section_4` peut aussi être utilisée par le chemin async, mais plus rarement.

**INCONNU**
- `ptr_section_3` contient-il uniquement des CLUT / metadata texture, ou parfois de vraies données image.
- Différence fonctionnelle exacte entre `ptr_section_4` et `ptr_section_5`.
- Existence éventuelle de fichiers où `tex_set tbl=5` serait valide mais non encore observé.

### 36.5 Prochaines actions Ghidra recommandées

1. Rechercher dans les lecteurs de texture si l'index de table sélectionne ensuite un sous-format distinct selon `3/4/5`.
2. Comparer en brut plusieurs cibles `ptr_section_4` et `ptr_section_5` alignées sur un même personnage pour voir si l'une contient systématiquement des CLUT ou des tuiles d'une autre profondeur.
3. Vérifier dynamiquement, via PCSX-Redux, quel pointeur header est consommé pour un `load_set tbl=5` sur un cas simple comme `CH_11.BIN E0`.

---

## Section 37 — Header CH_BIN runtime à `0x801D2000`

### 37.1 Résumé factuel

- `LoadCHBinFileAsync` charge le fichier `CH_x.BIN` directement dans `&g_cdFileBufferTable`.
- `RenderBattleScene3D` pose ensuite `g_cdFileBaseOffset = 0x2E800` et ajoute cet offset à `(&g_cdFileBufferTable)[2..n-1]`.
- Les symboles déjà présents en RAM runtime se calent exactement sur le préfixe header CH_BIN : `g_meshTableCounts @ +0x04`, `g_chBinEntryTableBasePtr @ +0x08`, `g_chBinClutTablePtr @ +0x0C`.
- Deux labels supplémentaires ont été ajoutés en Ghidra : `g_chBinSection4Ptr @ 0x801D2010` et `g_chBinSection5Ptr @ 0x801D2014`.
- Cela prouve que la zone runtime `0x801D2000..0x801D2017` est le header CH_BIN chargé depuis disque, puis relocalisé en place.

### 37.2 Table des preuves

| Offset runtime | Symbole / rôle | Type minimal | Fonction(s) | Preuve |
|---------------|----------------|--------------|-------------|--------|
| `0x801D2000` | `reloc_loop_bound` | `ushort` | `RenderBattleScene3D` | lu via `(ushort)g_cdFileBufferTable` pour borner la boucle de relocation |
| `0x801D2004` | `entry_count` | `uint` | `RenderBattleScene3D`, `AnimCmd_RenderEntryGroup` | symbole existant `g_meshTableCounts`, utilisé comme compte d'entrées mesh |
| `0x801D2008` | `ptr_entry_table` | `uint` | `RenderBattleScene3D` | symbole existant `g_chBinEntryTableBasePtr`, relu après relocation |
| `0x801D200C` | `ptr_section_3` | `uint` | `RenderBattleScene3D` | symbole existant `g_chBinClutTablePtr`, relu/écrit pendant relocation |
| `0x801D2010` | `ptr_section_4` | `uint` | `RenderBattleScene3D` | slot contigu au header commun; label Ghidra ajouté `g_chBinSection4Ptr` |
| `0x801D2014` | `ptr_section_5` | `uint` | `RenderBattleScene3D` | slot contigu au header commun; label Ghidra ajouté `g_chBinSection5Ptr` |

### 37.3 Structure partielle runtime

```c
typedef struct CHBinFileHeaderRuntime {
    ushort reloc_loop_bound;  // +0x00 CERTAIN
    ushort header_flags;      // +0x02 CERTAIN
    uint   entry_count;       // +0x04 CERTAIN
    uint   ptr_entry_table;   // +0x08 CERTAIN
    uint   ptr_section_3;     // +0x0C CERTAIN comme champ header; sémantique PROBABLE
    uint   ptr_section_4;     // +0x10 CERTAIN comme champ header; sémantique PROBABLE
    uint   ptr_section_5;     // +0x14 CERTAIN comme champ header; sémantique PROBABLE
} CHBinFileHeaderRuntime;
```

### 37.4 CERTAIN / PROBABLE / INCONNU

**CERTAIN**
- `LoadCHBinFileAsync` appelle `SearchFileAndLoadIntoBuffer(..., &g_cdFileBufferTable, 1)` pour charger le `CH_x.BIN` courant.
- `RenderBattleScene3D` relocalise en place les pointeurs du header chargés dans cette zone runtime.
- `0x801D2008`, `0x801D200C`, `0x801D2010`, `0x801D2014` sont quatre dwords contigus du header runtime.

**PROBABLE**
- `g_chBinClutTablePtr` est un ancien nom trop spécifique pour le champ header `ptr_section_3`.
- `g_chBinSection4Ptr` et `g_chBinSection5Ptr` sont les alias runtime corrects, neutres, pour les champs header `[4]` et `[5]`.

**INCONNU**
- Pourquoi Ghidra a historiquement séparé cette zone sous des symboles hétérogènes plutôt qu'un seul header structuré.
- S'il existe, ailleurs dans le code, un accès direct nommé à `g_chBinSection4Ptr` ou `g_chBinSection5Ptr` plutôt qu'un accès par index.

### 37.5 Prochaines actions Ghidra recommandées

1. Relire l'ASM de `RenderBattleScene3D` autour de `0x80035C3C..0x80035C8C` pour documenter explicitement la boucle de relocation `header[2..reloc_loop_bound-1] += 0x2E800`.
2. Vérifier si `g_chBinClutTablePtr` doit être conservé comme alias non primaire ou remplacé par un nom plus neutre `g_chBinSection3Ptr`.
3. Chercher localement, sans scan global, les consommateurs des labels runtime `g_chBinSection4Ptr` et `g_chBinSection5Ptr` dans les fonctions texture déjà identifiées.
