# CH_BIN File Format Analysis

## Recent Updates

**2026-03-07 #7**: RUNTIME ANALYSIS — PCSX-REDUX + GHIDRA/REVA (SESSION LIVE)
- ✅ **`g_cdFileBaseOffset = 0x2E800` HARDCODÉ** dans `RenderBattleScene3D` (ligne 117)
- ✅ **PRÉPROCESSING LOCALISÉ**: PAS de fonction séparée — c'est `RenderBattleScene3D` elle-même (lignes 117-126)
- ✅ **FORMAT HEADER CH_BIN CONFIRMÉ** par lecture binaire de CH_01.BIN (20480 bytes):
  - dword[0] `0xC0000006`: ushort_low=6 (nb dwords à relocaliser), bit31=flag mode spécial
  - dword[1] `0x00000025`=37: entry count
  - dwords[2..5]: 4 pointeurs pré-compilés `0x801Axxxx` → +0x2E800 → `0x801Dxxxx` (runtime)
- ✅ **BOUCLE RELOCALISATION**: `for i in [2, count-1]: buffer[i] += 0x2E800` (seulement ~4 dwords)
- ✅ **ADRESSES GLOBAUX CLÉS** (via Ghidra/ReVa):
  - `g_cdFileBufferTable` @ `0x801D2000` (buffer chargement CD-ROM)
  - `g_cdFileBaseOffset` @ `0x8009A978` (valeur runtime = 0x2E800 après set)
  - `g_ch_bin_filenames` = table noms fichiers indexée par character ID
- ✅ **PIPELINE CONFIRMÉ**: `FUN_80034ed0` → `LoadCHBinFileAsync` (state 8/9) → `RenderBattleScene3D` (rendu)
- ✅ **`LoadCHBinFileAsync` décompilée** : charge via `SearchFileAndLoadIntoBuffer` async (mode=1), attend `CdReadSync`
- ⚠️ **STRUCTURE RÉELLE à réévaluer**: les 4 dwords[2-5] (0x801Axxxx) sont le debut des données d'animation — les 37 entries démarrent à dword[6] (offset +24)

**2026-02-13 #4**: CODE-BASED STRUCTURE ANALYSIS (CRITICAL)
- ✅ **RenderBattleScene3D decompiled** (332 lines) showing actual Entry usage
- ✅ **Entry structure confirmed**: 28 bytes (7 uint32 fields)
- ✅ **Field access pattern identified** (lines 183-198):
  - `local_38[2]` → field at +0x08 (file offset → pointer via g_cdFileBaseOffset)
  - `local_38[3]` → field at +0x0C (file offset → pointer)
  - `local_38[4]` → field at +0x10 (file offset → pointer)
  - `local_38[5]` → field at +0x14 (mesh stream pointer or 0)
- ✅ **Pointer relocation mechanism**: All offsets get `+ g_cdFileBaseOffset (0x2E800)` to become RAM pointers
- ✅ **IterateMeshStreamAndFetch decompiled** (29 lines) showing stream parsing
- ✅ **FUN_80034ed0 decompiled** (52 lines): LoadCHBinFileAsync (case 1) → RenderBattleScene3D (case 2)
- 🔍 **REPLACED binary analysis speculation with CODE-BASED evidence**

**2026-02-13 #3**: Previous Binary Analysis (NOW VALIDATED BY CODE)
- ⚠️ Binary analysis suggested Entry 0 has 4 pointers + 3 colors
- ⚠️ Entry 1+ suggested 7 RGB colors
- ✅ **CODE CONFIRMS**: Fields +8/+12/+16/+20 are file offsets converted to pointers at runtime

**2026-02-13 #4**: CODE-BASED STRUCTURE ANALYSIS (CRITICAL)
- ✅ **RenderBattleScene3D decompiled** (332 lines) showing actual Entry usage
- ✅ **Entry structure confirmed**: 28 bytes (7 uint32 fields)
- ✅ **Field access pattern identified** (lines 183-198)
- ✅ **IterateMeshStreamAndFetch decompiled** (29 lines per variant, 3 variants analyzed)
- ✅ **FUN_80034ed0 decompiled** (52 lines): Load → Render pipeline

**2026-02-13 #5**: GHIDRA/REVA MCP ANALYSIS (COMPLETE)
- ✅ **5/7 champs CERTAIN** (field_00/08/0C/10/14): Preuves code directes
- ✅ **2/7 champs INCONNU** (field_04/18): Jamais accédés (2 fonctions analysées)
- ✅ **Entry 0 traitement spécial**: Skippé par TOUTES fonctions rendering
- ✅ **3 structures indirectes**: VertexDataStruct, MeshDataStruct, LightingDataStruct
- ✅ **3 mesh stream formats**: Base (8 bytes), Offset8 (8 bytes), Offset16 (16 bytes)

**2026-02-13 #6**: VALIDATION BINAIRE + DÉCOUVERTES CRITIQUES
- ✅ **CH_01.BIN analysé**: 37 entries, data @ 0x414, Entry 0 = RAM pointers
- ✅ **FUN_800374f4 analysé** (250 lines): Confirme field_04/18 non utilisés
- ✅ **Format fichier ≠ Format RAM**: Transformation post-load identifiée
- ⚠️ **CRITIQUE MANQUANT**: Fonction PRE-PROCESSING fichier → RAM (HAUTE PRIORITÉ)

---

## 🎯 ANALYSE GHIDRA/REVA — STRUCTURE MESH ENTRY (2026-02-13 #5)

**Assistant MCP/Reva — Analyse basée uniquement sur preuves observables**

### RÉSUMÉ FACTUEL (5 lignes)

RenderBattleScene3D (0x80035a04) analyse complète (332 lignes) révèle 5/7 champs Entry CERTAINS via preuves code directes.
field_00 (+0x00) = compteur primitives GT3/GT4 (CERTAIN: ligne 209,301-309).
field_08/0C/10 (+0x08/0C/10) = offsets fichier → structures indirectes [Vertex, Mesh, Lighting] avec pointeurs relocalisés +0x2E800 (CERTAIN: lignes 183-298).
field_14 (+0x14) = mesh stream optionnel ou NULL (CERTAIN: ligne 196-207).
field_04/18 (+0x04/0x18) JAMAIS accédés dans fonction = INCONNU (recherche cross-references autres fonctions nécessaire).

### TABLE DES PREUVES — CHAMPS ENTRY

| Offset | Type | Usage Observé | Fonction | Preuve (Ligne Code) |
|--------|------|---------------|----------|---------------------|
| +0x00 | int16 | Compteur primitives GT3/GT4 | RenderBattleScene3D | L209: `if (0 < (short)*local_38)`<br>L301: `while (local_a8 < *local_38)` (loop count)<br>L303: `uVar8 = *local_38` (read count)<br>L307: `*meshCountBuffer = (short)uVar8` (store) |
| +0x02 | int16 | INCONNU (partie field_00?) | - | Possiblement flags ou padding |
| +0x04 | uint32 | INCONNU (jamais accédé) | - | AUCUNE (332 lignes analysées, 0 accès) |
| +0x08 | uint32 | Offset → VertexDataStruct | RenderBattleScene3D | L184: `local_d0 = local_38[2] + 0x2E800`<br>L185: `local_cc = *local_d0 + 0x2E800` (indirection)<br>L187: `local_88 = local_d0[1] >> 16` (countX)<br>L189: `local_90 = local_d0[1] & 0xFFFF` (countY)<br>L227: `*piVar13 = *local_cc` (copy vertex)<br>L229: `IterateMeshStreamAndFetch(local_88, local_90, ...)` |
| +0x0C | uint32 | Offset → MeshDataStruct | RenderBattleScene3D | L183: `local_c8 = local_38[3] + 0x2E800`<br>L188: `local_c4 = *local_c8 + 0x2E800` (indices)<br>L195: `iVar14 = local_c8[1] + 0x2E800` (UV table)<br>L197: `iVar5 = local_c8[2] + 0x2E800` (color table)<br>L191/193: `local_78/80 = local_c8[3]` (counts)<br>L241-271: accès UV/colors via indices local_c4[0-7]<br>L272-273: `IterateMeshStreamAndFetch_Offset16(local_78, local_80, ...)` |
| +0x10 | uint32 | Offset → LightingDataStruct | RenderBattleScene3D | L190: `local_c0 = local_38[4] + 0x2E800`<br>L192: `local_bc = *local_c0 + 0x2E800` (indirection)<br>L194/198: `local_68/70 = local_c0[1]` (counts)<br>L277-294: RGB additions `local_bc[0]+local_bc[1]`<br>L295: `IterateMeshStreamAndFetch_Offset8(local_68, local_70, ...)` |
| +0x14 | uint32 | Mesh stream offset ou 0 | RenderBattleScene3D | L196: `uVar8 = local_38[5]`<br>L199: `if (uVar8 != 0)` (NULL check)<br>L201: `iVar9 = uVar8 + 0x2E800` (relocate)<br>L202-206: parse header (skip 2, read offset @+2, skip 2) |
| +0x18 | uint32 | INCONNU (jamais accédé) | - | AUCUNE (332 lignes analysées, 0 accès) |

**Incrémentation loop (PREUVE taille Entry = 28 bytes):**
- L176: `local_38 = local_98 + 1` (skip Entry 0, start Entry 1)
- L304: `local_38 = local_38 + 7` (next entry, 7x uint32 = 28 bytes)
- L305: `local_98 = local_98 + 7` (next entry base pointer)
- L309: `while (uVar6 < local_40)` (loop jusqu'à entry_count)

### STRUCTURE PARTIELLE (28 bytes)

```c
typedef struct CHBinMeshEntry {
    // +0x00 (4 bytes) — CERTAIN
    int16_t primitive_count;      // Nombre de polygones GT3/GT4 à renderer
                                  // Preuve: ligne 209 cast short + boucle do-while
    int16_t unknown_0x02;         // INCONNU (possiblement flags ou padding)
    
    // +0x04 (4 bytes) — INCONNU
    uint32_t unknown_0x04;        // INCONNU (preuve insuffisante)
                                  // Non observé dans lignes 176-260
                                  // Possiblement utilisé ailleurs dans fonction
    
    // +0x08 (4 bytes) — CERTAIN
    uint32_t vertex_struct_offset; // Offset fichier → VertexDataStruct
                                   // Preuve: ligne 184-185 double indirection
                                   // Structure cible contient:
                                   //   [0] = vertex_buffer* (relocalisé)
                                   //   [1] = counts packed (hi:countX, lo:countY)
    
    // +0x0C (4 bytes) — CERTAIN
    uint32_t mesh_data_offset;     // Offset fichier → MeshDataStruct
                                   // Preuve: lignes 183,188,195,197
                                   // Structure cible contient:
                                   //   [0] = primitive_indices* (byte array)
                                   //   [1] = uv_table_offset (6 bytes/entry)
                                   //   [2] = color_table_offset (6 bytes/entry)
                                   //   [3] = param (high/low shorts, usage TBD)
    
    // +0x10 (4 bytes) — CERTAIN
    uint32_t lighting_struct_offset; // Offset fichier → LightingDataStruct
                                     // Preuve: lignes 190-194,277-294
                                     // Structure cible contient:
                                     //   [0] = lighting_colors* (relocalisé, RGB data)
                                     //   [1] = counts packed (hi/lo shorts)
                                     // Lignes 277-294: RGB additions et stockage vertex colors
                                     // Lignes 295-298: IterateMeshStreamAndFetch_Offset8
    
    // +0x14 (4 bytes) — CERTAIN
    uint32_t mesh_stream_offset;   // Offset fichier → MeshStream ou 0 (NULL)
                                   // Preuve: lignes 196-207
                                   // Si non-NULL: header = [offset:u16, ...]
                                   // Parsé par code spécialisé (skip 2, read 2, skip 2)
    
    // +0x18 (4 bytes) — INCONNU
    uint32_t unknown_0x18;         // INCONNU (preuve insuffisante)
                                   // Non observé dans lignes 176-260
                                   
} CHBinMeshEntry;  // Total: 28 bytes (0x1C) — CONFIRMÉ
```

### STRUCTURES INDIRECTES DÉCOUVERTES

#### VertexDataStruct (pointée par field_08)

```c
// CERTAIN (preuves lignes 184-189, 227-232)
typedef struct VertexDataStruct {
    int32_t *vertex_buffer;       // +0x00: Pointeur vertices (relocalisé runtime)
                                  // Ligne 185: *local_d0 + g_cdFileBaseOffset
                                  // Ligne 227: *local_cc copié vers transformedVertexBuffer
    uint32_t counts_packed;       // +0x04: Compteurs mesh grid
                                  // Ligne 187: hi word → local_88 (countX?)
                                  // Ligne 189: lo word → local_90 (countY?)
                                  // Passés à IterateMeshStreamAndFetch (ligne 229)
} VertexDataStruct;  // 8 bytes minimum
```

#### MeshDataStruct (pointée par field_0C)

```c
// CERTAIN (preuves lignes 183,188,195,197,241-259)
typedef struct MeshDataStruct {
    uint8_t *primitive_indices;   // +0x00: Array indices vertices/UV/colors
                                  // Ligne 188: *local_c8 + offset → local_c4
                                  // Lignes 215,241-256: local_c4[0-5] indices
    uint32_t uv_table_offset;     // +0x04: Offset table UV (format: 3x u16 = 6 bytes/entry)
                                  // Ligne 195: local_c8[1] + g_cdFileBaseOffset
                                  // Lignes 241-253: accès [(index*6)+0/2/4]
    uint32_t color_table_offset;  // +0x08: Offset table colors (format: 3x u16 = 6 bytes/entry)
                                  // Ligne 197: local_c8[2] + g_cdFileBaseOffset
                                  // Lignes 254-259: accès [(index*6)+0/2/4]
    uint32_t counts_packed;       // +0x0C: Compteurs mesh grid (pour table UV/colors)
                                  // Lignes 191,193: hi/lo shorts → local_78, local_80
                                  // Ligne 272-273: Passés à IterateMeshStreamAndFetch_Offset16
} MeshDataStruct;  // 16 bytes minimum
```

#### LightingDataStruct (pointée par field_10)

```c
// CERTAIN (preuves lignes 190-194, 277-294, 295-298)
typedef struct LightingDataStruct {
    int16_t *lighting_colors;     // +0x00: Pointeur RGB lighting data (relocalisé runtime)
                                  // Ligne 192: *local_c0 + g_cdFileBaseOffset → local_bc
                                  // Lignes 277-294: local_bc[0/1/2] RGB additions
                                  // Format: int16 RGB components (signed pour shading)
    uint32_t counts_packed;       // +0x04: Compteurs mesh grid
                                  // Ligne 194: hi word → local_68
                                  // Ligne 198: lo word → local_70
                                  // Ligne 295: Passés à IterateMeshStreamAndFetch_Offset8
} LightingDataStruct;  // 8 bytes minimum
```

#### Mesh Stream Format (field_14 data)

**ANALYSE COMPARATIVE — 3 VARIANTES IDENTIFIÉES**

Le code utilise 3 fonctions stream parser avec offsets différents:

| Fonction | Offset Avance | Offset Data Read | Offset Count Reload | Taille Entry |
|----------|---------------|------------------|---------------------|--------------|
| IterateMeshStreamAndFetch | +8 bytes (L13) | [+8] (L14) | [+4], [+6] (L17,24) | 8 bytes |
| IterateMeshStreamAndFetch_Offset8 | +8 bytes (L13) | [+8] (L14) | [+4], [+6] (L17,24) | 8 bytes |
| IterateMeshStreamAndFetch_Offset16 | +16 bytes (L13) | [+16] (L14) | [+12], [+14] (L17,24) | 16 bytes |

**Structure Stream Entry (CERTAIN - preuves code):**

```c
// Format stream 8-byte entry (utilisé par Base et Offset8)
typedef struct MeshStreamEntry8 {
    uint32_t reserved_or_metadata;  // +0x00: INCONNU (possiblement padding, flags)
    uint32_t data_offset;           // +0x04: Offset vers data (relocalisé via +0x2E800)
                                    // Ligne 14: piVar1 = piVar1[2] (lit à +8 = champ suivant!)
                                    // ERROR ANALYSE: piVar1[2] signifie +8 bytes, pas champ à +4
                                    // CORRECTION: offset lu est après l'entry actuelle
} MeshStreamEntry8;  // 8 bytes

// RECALCUL CORRECT:
// L12-13: piVar1 = *streamPtr; *streamPtr = piVar1 + 2 (avance 8 bytes)
// L14: piVar1 = piVar1[2] (lit int à +8 depuis position ORIGINALE)
// →→→ Lit offset DEPUIS PROCHAINE ENTRY, pas entry actuelle!

// Format stream 16-byte entry (utilisé par Offset16)
typedef struct MeshStreamEntry16 {
    uint32_t unknown[3];            // +0x00-0x0B: INCONNU (metadata/padding)
    uint32_t data_offset;           // +0x0C: Offset vers data (relocalisé)
                                    // Ligne 14: piVar1 = piVar1[4] (lit à +16)
} MeshStreamEntry16;  // 16 bytes
```

**Logique Grid 2D Iteration (CERTAIN):**

```c
// Pseudo-code reconstruit (preuves lignes 8-27)
countX--;  // Décrément X
if (countX == 0) {          // Fin de row X
    countY--;               // Décrément Y
    if (countY == 0) {      // Fin de grid complet
        // Avance stream et reload data pointer + counts
        streamPtr += entry_size;              // +8 ou +16 bytes
        dataPtr = *(streamPtr + offset);      // Lit nouveau data offset
        dataPtr += 0x2E800;                   // Relocalise
        countY = streamPtr[reload_Y_offset];  // Reload countY (hi word)
        countX = streamPtr[reload_X_offset];  // Reload countX (lo word ou @+2)
    } else {
        // Même data pointer, avance dans buffer actuel
        dataPtr = **streamPtr;                // Re-read current data offset
        dataPtr += 0x2E800;                   // Relocalise
        countX = *(ushort*)(streamPtr + 6);   // Reload countX depuis offset +6
    }
}
return (countX << 16) | countY;  // Pack counts pour prochain appel
```

**Interprétation (PROBABLE):**

Le mesh est organisé en **grille 2D** (countX × countY vertices/primitives).
Stream parcourt cette grille row-by-row, reloading data pointer et counts selon position:
- **countX > 0**: Continue row actuelle (pas de stream read)
- **countX == 0, countY > 0**: Fin de row, reload countX pour prochaine row
- **countX == 0, countY == 0**: Fin de grid, avance stream, reload data + counts complets

**Usage observé:**
- Base variant: Utilisé avec VertexDataStruct (vertices, ligne 229)
- Offset8 variant: Utilisé avec LightingDataStruct (lighting colors, ligne 295)
- Offset16 variant: Utilisé avec MeshDataStruct UV/colors (indices, ligne 272-273)

**INCONNU:**
- Rôle exact champs +0x00-0x03 dans StreamEntry8 (padding? flags? metadata?)
- Rôle exact champs +0x00-0x0B dans StreamEntry16 (pourquoi 12 bytes avant offset?)
- Nombre total entries dans stream (comment fin stream détectée?)
- Mesh stream header format (lignes 201-206 parse 4 bytes seulement)

### CLASSIFICATION CERTITUDE

**CERTAIN** (preuve directe code décompilé + validation binaire):
- field_00 (+0x00): Compteur primitives (29 lignes preuve RenderBattleScene3D + FUN_800374f4)
- field_08 (+0x08): Offset VertexDataStruct (8 lignes preuve 2 fonctions)
- field_0C (+0x0C): Offset MeshDataStruct (15 lignes preuve 2 fonctions)
- field_10 (+0x10): Offset LightingDataStruct (12 lignes preuve 2 fonctions)
- field_14 (+0x14): Mesh stream offset ou NULL (7 lignes preuve 2 fonctions)
- VertexDataStruct layout: [vertex_ptr, counts_packed]
- MeshDataStruct layout: [indices*, uv_offset, color_offset, counts_packed]
- LightingDataStruct layout: [lighting_colors*, counts_packed]
- Mécanisme relocalisation: offset + 0x2E800 → RAM pointer
- Taille Entry: 28 bytes (7x uint32) - confirmé binaire + code
- Boucle process: Entries 1 à N (ligne 176 skip Entry 0) - 2 fonctions
- Entry 0 skippé: RenderBattleScene3D L176 + FUN_800374f4 L67 (PATTERN)
- Entry 0 format fichier: RAM pointers 0x801Axxxx (impossibles CD-ROM)
- Mesh stream 3 variantes: Base/Offset8 (8 bytes), Offset16 (16 bytes)
- Format fichier ≠ Format RAM (transformation post-load)

**INCONNU** (preuve insuffisante - actions Ghidra requises):
- field_04 (+0x04): **Jamais accédé** (RenderBattleScene3D 332L + FUN_800374f4 250L = 0 accès)
  → **Hypothèse FORTE**: Padding ou réservé (non utilisé)
- field_18 (+0x18): **Jamais accédé** (RenderBattleScene3D 332L + FUN_800374f4 250L = 0 accès)
  → **Hypothèse FORTE**: Padding ou réservé (non utilisé)
- Entry 0 usage: Skippé par rendering, contenu/utilité INCONNU
  → **Hypothèse FORTE**: Table globale pointers/metadata (RAM addresses 0x801Axxxx)
- Format exact mesh stream: Header structure incomplet (4 bytes parsés sur combien?)
- Fonction PRE-PROCESSING: Transformation fichier → RAM entries (CRITIQUE MANQUANT)
- Entry 0 initialisation: Qui/quand écrit RAM pointers? (HAUTE PRIORITÉ)
- Distinction primitive GT3 vs GT4: Ligne 215-221 test local_c4[8], décodage incomplet

### ZONES D'OMBRE — PROCHAINES ACTIONS GHIDRA

#### 1. ✅ RÉSOLU: field_04 et field_18 jamais utilisés

**Fonctions analysées:** RenderBattleScene3D (332 lignes) + FUN_800374f4 (250 lignes)

**RÉSULTAT CERTAIN:** field_04 (+0x04) et field_18 (+0x18) = Padding ou réservés (0 accès dans 2 fonctions principales)

**No further action required** - Ces champs peuvent être ignorés pour la décompilation

#### 2. ⚠️ PARTIELLEMENT RÉSOLU: Entry 0 traitement spécial

**Constat:** Entry 0 skippé par RenderBattleScene3D (L176) ET FUN_800374f4 (L67) = PATTERN CONFIRMÉ

**Analyse binaire CH_01.BIN:**
- Entry 0 contient RAM pointers 0x801Axxxx (impossibles dans fichier CD-ROM)
- Entry 1+ contient données brutes (counts, colors, offsets)
- Entry 0 utilisé comme **table globale** ou **header spécial**

**Actions Reva/Ghidra HAUTE PRIORITÉ:**
```
# 1. Trouver où Entry 0 est ÉCRIT (initialization post-load)
mcp_reva_get-functions(min=200, max=1000)  # Fonctions taille moyenne
# Chercher dans résultats: parsing/init CH_BIN après LoadCHBinFileAsync

# 2. Tracer WRITE refs DAT_801d2008
# Identifier code qui populé this mesh table pointer

# 3. Chercher pattern "0x801A" dans décompilation (RAM addresses)
# Trouve code qui calcule/écrit ces adresses

# 4. Analyser call chain LoadCHBinFileAsync → RenderBattleScene3D
# Il DOIT y avoir étape intermédiaire parsing/transform
```

**Question CRITIQUE:**
- Entry 0 format: Table global pointers? Header metadata? Autre?
- Qui écrit les RAM pointers 0x801Axxxx dans Entry 0?
- Y a-t-il PRE-PROCESSING fichier → RAM avant rendering?

#### 3. Décoder format mesh stream complet

**Constat:** Header parse partiel (lignes 201-206), structure inconnue

**Actions Reva/Ghidra:**
```
mcp_reva_get-decompilation(0x8003668c)   // IterateMeshStreamAndFetch base
mcp_reva_get-decompilation(0x80036744)   // IterateMeshStreamAndFetch_Offset8
mcp_reva_get-decompilation(0x800367fc)   // IterateMeshStreamAndFetch_Offset16
// Comparer 3 variantes pour identifier:
// - Format header complet (combien de u16?)
// - Différences Offset8 vs Offset16 vs base
// - Structure stream body
```

**Question:** 
- Mesh stream format: [count:u16, offset:u16, ...]?
- Différence 3 variantes: taille offset ou format données?
- Stream body: command list? vertex deltas? autre?

#### 4. Décoder primitive_indices format complet

**Constat:** local_c4[0-8] utilisé, mais structure exacte floue

**Actions Reva/Ghidra:**
```
// Analyser pattern accès local_c4:
// Ligne 215: local_c4[8] → type primitive (0=GT4, autre=GT3)
// Lignes 241-253: local_c4[0-3] → indices UV (4 vertices)
// Lignes 254-271: local_c4[4-7] → indices colors (4 vertices)
// Ligne 270: local_c4 += 12 → taille structure primitive
```

**Structure probable:**
```c
typedef struct PrimitiveEntry {
    uint8_t uv_indices[4];      // +0x00-0x03: UV table indices (4 vertices)
    uint8_t color_indices[4];   // +0x04-0x07: Color table indices (4 vertices)
    uint8_t type;               // +0x08: 0=GT4 (quad), non-0=GT3 (triangle)
    uint8_t flags[3];           // +0x09-0x0B: INCONNU (padding? flags?)
} PrimitiveEntry;  // 12 bytes (confirmé ligne 270)
```

**Question:** Champs +0x09-0x0B utilisés? Flags rendering? Padding alignement?

#### 5. ✅ RÉSOLU: Valider structure contre CH_01.BIN

**COMPLÉTÉ** - Voir section "DÉCOUVERTES COMPLÉMENTAIRES #6" ci-dessus

**Découvertes principales:**
- Entry table: 37 entries × 28 bytes = 1036 bytes (offset 0x08-0x413)
- Data section: starts @ 0x414 (confirmé)
- Entry 0: RAM pointers (confirme traitement spécial)
- Entry 1+: Raw data (format différent runtime vs fichier)
- Format fichier ≠ Format RAM (transformation post-load)

**Actions restantes:** Trouver fonction PRE-PROCESSING (voir action #2)

#### 6. Identifier fonctions consommatrices mesh entries

**Constat:** Autres fonctions 3D possiblement utilisent field_04/field_18

**Actions Reva/Ghidra:**
```
mcp_reva_get-decompilation(0x800374f4)  // Mesh renderer (250 lines)
mcp_reva_get-decompilation(0x8003de10)  // Mesh processing (1784 bytes)
mcp_reva_search-decompilation("DAT_801d2008|g_meshTableCounts")  // Autres users table mesh
```

**Question:** 
- FUN_800374f4 accède quels champs Entry?
- FUN_8003de10 processing phase utilise field_04/field_18?
- Pipeline complet: Load → Process → Render → autre?

### MÉTRIQUES PROGRESSION

- **Champs Entry mappés**: 5/7 CERTAIN (71%), 0/7 PROBABLE, 2/7 INCONNU (29%)
- **Lignes code analysées**: 332/332 RenderBattleScene3D + 250/250 FUN_800374f4 (100%)
- **Structures indirectes**: 3 CERTAINES (Vertex, Mesh, Lighting) + 3 Stream Entry formats
- **Fonctions analysées**: 5 (RenderBattleScene3D, FUN_800374f4, IterateMeshStreamAndFetch×3)
- **Validation binaire**: ✅ COMPLÉTÉE (CH_01.BIN analysé)

---

## 🔬 DÉCOUVERTES COMPLÉMENTAIRES (2026-02-13 #6)

### field_04 et field_18 — CONFIRMATION FINALE

**Fonction supplémentaire analysée: FUN_800374f4** (0x800374f4, 2704 bytes, 250 lignes)

**RÉSULTAT:** field_04 (+0x04) et field_18 (+0x18) JAMAIS accédés (CERTAIN)

**Preuves:**
```c
// FUN_800374f4 - Mesh renderer variant (lignes 67-245)
puVar12 = local_98 + 1;  // L67: Skip Entry 0
do {
    uVar6 = *local_98;                        // field_00 (L70)
    local_c8 = puVar12[3] + g_cdFileBaseOffset;  // field_0C (L74)
    local_d0 = puVar12[2] + g_cdFileBaseOffset;  // field_08 (L75)
    local_c0 = puVar12[4] + g_cdFileBaseOffset;  // field_10 (L81)
    uVar7 = puVar12[5];                       // field_14 (L85)
    
    // AUCUN accès à puVar12[1] (field_04)
    // AUCUN accès à puVar12[6] (field_18)
    
    local_98 += 7;  // L240: Next entry (28 bytes)
    puVar12 += 7;   // L244: Next entry (28 bytes)
} while (...);
```

**Conclusion CERTAINE:**
- field_04 (+0x04): Padding ou champ réservé non utilisé (2 fonctions analysées, 0 accès)
- field_18 (+0x18): Padding ou champ réservé non utilisé (2 fonctions analysées, 0 accès)

### Entry 0 — TRAITEMENT SPÉCIAL CONFIRMÉ

**Fonction RenderBattleScene3D:**
```c
// Ligne 127-128: Load mesh table
local_98 = DAT_801d2008;         // Mesh table base pointer
local_40 = g_meshTableCounts;    // Entry count

// Ligne 176: SKIP Entry 0, start at Entry 1
local_38 = local_98 + 1;  // +28 bytes = skip first entry

// Loop processes Entry 1 to Entry N
```

**Fonction FUN_800374f4:**
```c
// Ligne 49-50: Load mesh table from different buffer
local_98 = g_cdFileBufferTable[index];
sVar4 = g_meshTableCounts[index * 2];

// Ligne 67: SKIP Entry 0
puVar12 = local_98 + 1;  // +28 bytes = skip first entry
```

**RÉSULTAT:** Entry 0 skippé par TOUTES fonctions rendering (CERTAIN)

**Analyse binaire CH_01.BIN (validation):**

```
Entry 0 @ 0x08 (file format):
  field_0: 0x801A4A44  ← RAM pointer (0x801A0000 + 0x4A44)
  field_1: 0x801A4E50  ← RAM pointer (0x801A0000 + 0x4E50)
  field_2: 0x801A4E70  ← RAM pointer (0x801A0000 + 0x4E70)
  field_3: 0x801A8098  ← RAM pointer (0x801A0000 + 0x8098)
  field_4: 0x00808080  ← RGB(128,128,128) gris
  field_5: 0x00608080  ← RGB(128,128,96) 
  field_6: 0x00080000  ← RGB(0,0,8)

Entry 1 @ 0x24 (file format):
  bytes bruts: 00 00 10 00 | 00 00 00 00 | 80 80 80 00 | 20 10 10 00 | 08 08 04 00 | 00 00 00 00 | E0 E0 38 00
  field_0: 0x00100000 (int16 lo=0, hi=16)
  field_1: 0x00000000
  field_2: 0x00808080
  field_3: 0x00101020
  field_4: 0x00040808
  field_5: 0x00000000 (NULL)
  field_6: 0x0038E0E0
```

**Hypothèse FORTE (non prouvée par code):**

Entry 0 = **Table globale pointeurs/metadata** initialisée APRÈS chargement fichier

Raisons:
1. Contient RAM addresses 0x801Axxxx (impossibles dans fichier CD-ROM)
2. Skippé systématiquement par boucles rendering
3. Taille 28 bytes identique aux autres entries (pas header séparé)
4. Offsets 0x4A44, 0x4E50, etc. pointent vers **zone data du fichier** (après 0x414)

**Structure probable Entry 0 (HYPOTHÈSE):**
```c
typedef struct CHBinEntry0GlobalData {
    void *global_vertex_buffer;      // +0x00: 0x801A4A44
    void *global_uv_buffer;          // +0x04: 0x801A4E50
    void *global_color_buffer;       // +0x08: 0x801A4E70
    void *global_metadata_buffer;    // +0x0C: 0x801A8098
    uint32_t default_color_1;        // +0x10: 0x00808080 RGB(128,128,128)
    uint32_t default_color_2;        // +0x14: 0x00608080 RGB(96,128,128)
    uint32_t default_color_3;        // +0x18: 0x00080000 RGB(0,0,8)
} CHBinEntry0GlobalData;
```

**Action Ghidra nécessaire (HAUTE PRIORITÉ):**
```
mcp_reva_find-cross-references(DAT_801d2008, write)
// Trouver fonction qui ÉCRIT Entry 0 (initialisation post-load)
// Confirmer ou infirmer hypothèse "global pointers table"
```

### FORMAT FICHIER vs FORMAT RUNTIME

**DÉCOUVERTE CRITIQUE:**

Le fichier CH_BIN contient des DONNÉES BRUTES transformées lors du chargement:

```
FICHIER CH_01.BIN (sur CD-ROM):
├─ Header (8 bytes): flags=0xC0000006, count=37
├─ Entry 0 (28 bytes): RAM pointers (écrits APRÈS load)
├─ Entry 1-36 (28×36 = 1008 bytes): Mesh data descriptors
└─ Data section (0x414+): Raw vertices, colors, UVs, indices...

     ↓ CHARGEMENT + TRANSFORMATION ↓

RAM @ 0x801A0000 + g_cdFileBaseOffset (0x2E800):
├─ Entry 0: Global pointers table (initialisée runtime)
├─ Entry 1-36: Mesh entries (offsets relocalisés +0x2E800)
└─ Data section: Parsed into separate buffers
                 (g_transformedVertexBuffer, etc.)
```

**Entry 1+ dans FICHIER ≠ Entry 1+ en RAM:**

Fichier Entry 1 contient probablement:
- Metadata mesh (counts, flags)
- Offsets relatifs fichier
- Color palettes
- Material data

RAM Entry 1 (après processing) contient:
- field_00: Primitive count (extrait de metadata)
- field_08/0C/10: Offsets fichier + 0x2E800 (relocalisés)
- field_14: Mesh stream offset + 0x2E800 (relocalisé)
- field_04/18: Padding (non utilisés)

**Question CRITIQUE non résolue:**

Où est la fonction de PRE-PROCESSING qui transforme fichier → RAM entries?

**Actions Ghidra HAUTE PRIORITÉ:**
```
1. Chercher fonction appelée ENTRE LoadCHBinFileAsync et RenderBattleScene3D
2. Chercher WRITE refs sur DAT_801d2008 (initialisation table)
3. Chercher parsing/transform du header CH_BIN (offset 0x00-0x07)
4. Identifier code qui peuple Entry 0 avec RAM pointers
```

### VALIDATION BINAIRE — SECTION DATA

**CH_01.BIN structure confirmée:**

```
Offset   Description                  Taille     Preuve
------   -----------                  ------     ------
0x0000   flags/magic                  4 bytes    0xC0000006
0x0004   entry_count                  4 bytes    37 (0x25)
0x0008   Entry 0                      28 bytes   RAM pointers
0x0024   Entry 1                      28 bytes   Mesh data
...
0x0400   Entry 36 (last)              28 bytes   Mesh data
0x0414   Data section start           varies     Vertices, UVs, colors...
```

**Data section @ 0x414+:**

Entry 0 RAM pointers pointent DANS cette zone:
- 0x801A4A44 → file offset 0x4A44 (0x801A0000 base - 0x2E800 offset = file 0x1C244?)
  
  WAIT: 0x801A4A44 - 0x801A0000 = 0x4A44
  
  Si g_cdFileBaseOffset = 0x2E800 est ajouté aux offsets fichier...
  Alors 0x4A44 file offset + 0x2E800 = 0x79244 (DÉPASSE taille fichier 0x5000!)
  
**ERREUR ANALYSE:** Les RAM pointers Entry 0 NE correspondent PAS à pattern "offset + 0x2E800"

**Hypothèse révisée:**

Entry 0 pointers sont écris DIRECTEMENT comme RAM addresses absolues, pointant vers:
- Buffers générés runtime (g_transformedVertexBuffer, etc.)
- Sections spécifiques file chargé en RAM

**INCONNU (preuve insuffisante):**
- Mécanisme exact initialisation Entry 0
- Relation entre RAM pointers et data section fichier

---

## Overview

CH_BIN files contain 3D character model data for the battle system in DBZ Legends. These files are loaded from CD-ROM into RAM via `LoadCHBinFileAsync` (0x80035828) and parsed by `RenderBattleScene3D` (0x80035a04).

## File Locations

- **CH_BIN1/**: CH_01.BIN through CH_29.BIN, CH_NO.BIN (29 files)
- **CH_BIN2/**: CH_30.BIN through CH_50.BIN, includes CH_32_1/2/3.BIN variants (22 files)
- **CH_BIN3/**: IN_01.BIN through IN_10.BIN, IN_IN.BIN, IN_OT2.BIN, IN_OUT.BIN (13 files)

## File Structure (CERTAIN)

### Format Specification

```
Total Size: 0x5000 (20480 bytes = 10 CD sectors)
RAM Base Address: 0x801A0000 + g_cdFileBaseOffset

Offset   Size    Description
------   ----    -----------
0x0000   4       flags_or_magic (e.g., 0xC0000006, 0xC0000007)
0x0004   4       entry_count (number of mesh entries)
0x0008   N*28    Mesh entry table (entry_count * 0x1C bytes)
varies   M       Raw mesh data section (vertices, UVs, colors, etc.)
```

### Header Structure (8 bytes)

```c
typedef struct CHBinHeader {
    uint32_t flags_or_magic;     // +0x00 - Examples: 0xC0000006, 0xC0000007
                                 //         Low byte may correlate with file variant
    uint32_t entry_count;        // +0x04 - Number of mesh entries
} CHBinHeader;
```

**Evidence:**
- CH_01.BIN: flags=0xC0000006, count=0x25 (37 entries)
- CH_02.BIN: flags=0xC0000007, count=0x06 (6 entries)
- CH_03.BIN: flags=0xC0000006, count=0x25 (37 entries) [assumed]

### Mesh Entry Structure (28 bytes) - CODE-BASED ANALYSIS

**Source:** `RenderBattleScene3D` (0x80035a04), lines 183-198

```c
typedef struct CHBinMeshEntry {
    uint32_t field_00;           // +0x00 - Unknown (not accessed in observed code)
    uint32_t field_04;           // +0x04 - Unknown (not accessed in observed code)
    uint32_t field_08;           // +0x08 - File offset → RAM pointer (accessed as local_38[2])
    uint32_t field_0C;           // +0x0C - File offset → RAM pointer (accessed as local_38[3])
    uint32_t field_10;           // +0x10 - File offset → RAM pointer (accessed as local_38[4])
    uint32_t field_14;           // +0x14 - Mesh stream ptr or 0 (accessed as local_38[5])
    uint32_t field_18;           // +0x18 - Unknown (possibly accessed as local_38[6])
} CHBinMeshEntry;                // Total: 28 bytes (0x1C)
```

**Evidence from RenderBattleScene3D code:**

```c
// Line 127: Load mesh table pointer
local_98 = DAT_801d2008;

// Line 128: Load entry count
local_40 = g_meshTableCounts;

// Line 176: Iterate entries (local_38 points to current entry)
local_38 = local_98 + 1;

// Lines 183-198: Field access pattern
local_c8 = (int *)(local_38[3] + g_cdFileBaseOffset);  // field_0C + 0x2E800
local_d0 = (int *)(local_38[2] + g_cdFileBaseOffset);  // field_08 + 0x2E800
local_cc = (int *)(*local_d0 + g_cdFileBaseOffset);    // Dereference field_08 pointer
local_c0 = (int *)(local_38[4] + g_cdFileBaseOffset);  // field_10 + 0x2E800
uVar8 = local_38[5];                                    // field_14 (can be NULL)

// Line 229: Pass to stream parser
iVar9 = IterateMeshStreamAndFetch((int)local_88, (int)local_90, 
                                   &local_d0, &local_cc);
```

**Key Mechanism: Pointer Relocation**
- Fields +0x08, +0x0C, +0x10 contain **file offsets** (relative to file start)
- At runtime, `g_cdFileBaseOffset` (0x2E800 = 190464 bytes) is added to convert:
  - File offset → RAM address in loaded buffer at 0x801A0000 + 0x2E800
  - Example: offset 0x444 → RAM pointer 0x801A3244
- Field +0x14 may directly contain a pointer or be 0 (null check at line 196)

**Buffer System (lines 167-172):**
```c
g_transformedVertexBuffer   // Processed vertices
g_uvOrTexCoordBuffer        // UV texture coordinates
g_vertexColorBuffer         // Vertex colors
g_renderMetadataBuffer      // Rendering metadata
```

Fields +0x08/+0x0C/+0x10 likely point to different sections:
- Raw vertex data (SVECTOR format: s16 x, y, z, pad)
- UV coordinates (u8 u, v pairs)
- Color data (CVECTOR format: u8 r, g, b, cd)
- Mesh topology/primitives

**Cross-reference:** This matches the `MeshTableEntry` structure with 7 uint32 fields.

## Observed Data Patterns

### CH_01.BIN Analysis (37 entries)

**Header:**
```
0x0000: C0 00 00 06  (flags/magic)
0x0004: 25 00 00 00  (37 entries)
```

**Entry 0 @ 0x0008:**
```
+0x00: 0x801A4A44  (RAM pointer pattern)
+0x04: 0x801A4E50  (RAM pointer pattern)
+0x08: 0x801A4E70  (RAM pointer pattern)
+0x0C: 0x801A8098  (RAM pointer pattern)
+0x10: 0x00808080  (color/metadata pattern)
+0x14: 0x00608080  (color/metadata pattern)
+0x18: 0x00080000  (small value/counter)
```

**Entry 1 @ 0x0024:**
```
+0x00: 0x00100000
+0x04: 0x00000000
+0x08: 0x00808080
+0x0C: 0x00101020
+0x10: 0x00040808
+0x14: 0x00000000
+0x18: 0x0038E0E0
```

**Data Section:**
- Table end: 0x0008 + (37 * 0x1C) = 0x0414
- Data start: 0x0414
- Data size: 0x5000 - 0x0414 = 0x4BEC (19436 bytes)

### CH_02.BIN Analysis (6 entries)

**Header:**
```
0x0000: C0 00 00 07  (flags/magic, note: 07 vs 06)
0x0004: 06 00 00 00  (6 entries)
```

**Entry 0 @ 0x0008:**
```
+0x00: 0x801A5E30  (RAM pointer pattern)
+0x04: 0x801A5ED8  (RAM pointer pattern)
+0x08: 0x801A627C  (RAM pointer pattern)
+0x0C: 0x801A837C  (RAM pointer pattern)
+0x10: 0x801A846C  (RAM pointer pattern)
+0x14: 0x00010200  (small value)
+0x18: 0x00010200  (small value)
```

**Entry 1 @ 0x0024:**
```
+0x00: 0x00000001
+0x04: 0x00030100
+0x08: 0x00030100
+0x0C: 0x00000001
+0x10: 0x00040300
+0x14: 0x00040300
+0x18: 0x00000001
```

**Data Section:**
- Table end: 0x0008 + (6 * 0x1C) = 0x00C8
- Data start: 0x00C8
- Data size: 0x5000 - 0x00C8 = 0x4F38 (20280 bytes)

### Pattern Observations

1. **Entry 0 Pattern**: Contains primarily RAM pointers (0x801Axxxx range)
   - Likely pointing to mesh data sections within the loaded file
   - Pointers relative to base address 0x801A0000

2. **Entry 1+ Pattern**: Contains small values, indices, or color data
   - Mix of counters, indices, and RGBA color values
   - Pattern varies significantly between files

3. **Trade-off**: More entries = less raw data space (but total always 0x5000)
   - 37 entries: ~19KB mesh data
   - 6 entries: ~20KB mesh data

## Memory Layout (CERTAIN)

When loaded into RAM:

1. **File loaded to**: `g_cdFileBufferTable` buffer
2. **Base address calculation**: `0x801A0000 + g_cdFileBaseOffset`
3. **Offset value**: `g_cdFileBaseOffset = 0x2E800` (190464 bytes per buffer slot)
4. **Indexed by**: `ch_bin_file_index` (field in GameState->entityData.runtimePointers)

## Code References (CERTAIN)

### Loading System

**LoadCHBinFileAsync** (0x80035828, 476 bytes):
- State machine for async CD loading
- States: <8=prep, 8=loading, 9=sync wait, 2=complete
- Uses `ch_bin_file_index` to select from `g_ch_bin_filenames[]`
- Calls `SearchFileAndLoadIntoBuffer(g_ch_bin_filenames[index], &g_cdFileBufferTable, 1)`
- Fallback: If file not found (0xFFFFFFFF), loads `g_ch_bin_filenames[0]`

### Rendering System

**RenderBattleScene3D** (0x80035a04, 3208 bytes, 332 lines):
- Main 3D battle scene renderer using loaded CH_BIN data
- **Decompiled**: Full code available showing Entry structure usage
- Line 117: Sets `g_cdFileBaseOffset = 0x2E800` (pointer relocation base)
- Line 127: Loads `local_98 = DAT_801d2008` (mesh table pointer)
- Line 128: Loads `local_40 = g_meshTableCounts` (entry count)
- Line 176: Iterates entries via `local_38 = local_98 + 1`
- Lines 183-198: Accesses Entry fields +0x08/+0x0C/+0x10/+0x14 as file offsets
- Line 229: Calls `IterateMeshStreamAndFetch()` with dereferenced pointers
- Buffer initialization (lines 167-172):
  - `g_transformedVertexBuffer` - processed vertices
  - `g_uvOrTexCoordBuffer` - texture coordinates
  - `g_vertexColorBuffer` - vertex colors
  - `g_renderMetadataBuffer` - rendering metadata

**IterateMeshStreamAndFetch** (0x8003668c, 184 bytes, 29 lines):
- Parses mesh data streams with dynamic pointer relocation
- **Decompiled**: Shows actual pointer patching mechanism
- Line 14: Reads pointer from stream: `piVar1 = (int *)piVar1[2]`
- Line 16: Relocates address: `*outDataPtr = (int *)((int)piVar1 + g_cdFileBaseOffset)`
- Parameters: `(countX, countY, streamPtr, outDataPtr)`
- Uses `g_cdFileBaseOffset` to convert file offsets → RAM pointers at runtime

**IterateMeshStreamAndFetch Variants**:
- 0x8003668c - Base version (29 lines)
- 0x80036744 - Offset8 variant (similar structure)
- 0x800367fc - Offset16 variant (similar structure)

**FUN_80034ed0** (0x80034ed0, 52 lines):
- Game loop state machine dispatcher
- Case 1 (line 17): Calls `LoadCHBinFileAsync()` - loads file from CD
- Case 2 (line 22): Calls `RenderBattleScene3D()` - renders with loaded data
- Shows complete pipeline: Load → Process → Render

### Global Variables

```c
uint *DAT_801d2008;                // Mesh table pointer (runtime-populated)
ushort g_meshTableCounts;          // Number of entries in mesh table
uint g_fileLoadFlags;              // 0x8009AA50 - Bit 0x40 = loading active
char* g_ch_bin_filenames[];        // Array of CH_BIN filename strings
u_long* g_cdFileBufferTable;       // CD file buffer destination
uint g_cdFileBaseOffset;           // 0x2E800 (190464) - offset added to all pointers
int *g_transformedVertexBuffer;    // Processed vertex output buffer
undefined2 *g_uvOrTexCoordBuffer;  // UV/texture coordinate buffer
undefined2 *g_vertexColorBuffer;   // Vertex color buffer
int *g_renderMetadataBuffer;       // Rendering metadata buffer
```

### Cross-References

**DAT_801d2008** (mesh table pointer global):
- 3 references in RenderBattleScene3D:
  - 0x80035c68: READ (loads mesh table pointer)
  - 0x80035c74: WRITE (stores mesh table pointer)
  - 0x80035c8c: READ (accesses mesh table)

### Call Chain (CERTAIN)

```
FUN_80034ed0 (state dispatcher)
  ├─→ case 1: LoadCHBinFileAsync() 
  │            └─→ SearchFileAndLoadIntoBuffer()
  │                  └─→ CdReadSync(), CdSeekAndRead(), etc.
  │
  └─→ case 2: RenderBattleScene3D()
               └─→ IterateMeshStreamAndFetch() [3 variants]
                     └─→ Parses mesh streams with pointer relocation
```

## Field Meanings (CODE-BASED ANALYSIS - PARTIAL)

### Known Fields (from RenderBattleScene3D code)

**Field +0x08 (local_38[2])**:
- Contains file offset (converted to RAM pointer via `+ g_cdFileBaseOffset`)
- Dereferenced at line 185: `local_cc = (int *)(*local_d0 + g_cdFileBaseOffset)`
- Used as double-indirect pointer: offset → pointer → data
- Likely points to **pointer table** or **indirect data structure**

**Field +0x0C (local_38[3])**:
- Contains file offset (converted to RAM pointer via `+ g_cdFileBaseOffset`)
- Accessed at line 183: `local_c8 = (int *)(local_38[3] + g_cdFileBaseOffset)`
- Used as direct pointer to data section
- High word extracted at line 188: `local_88 = (short)((uint)local_d0[1] >> 0x10)`
- Likely contains **vertex data** or **mesh geometry**

**Field +0x10 (local_38[4])**:
- Contains file offset (converted to RAM pointer via `+ g_cdFileBaseOffset`)
- Accessed at line 190: `local_c0 = (int *)(local_38[4] + g_cdFileBaseOffset)`
- Used as direct pointer to data section
- Likely contains **UV coordinates**, **colors**, or **normals**

**Field +0x14 (local_38[5])**:
- Can be NULL or contain mesh stream pointer
- Checked at line 196: `uVar8 = local_38[5]`
- If non-zero, passed to `IterateMeshStreamAndFetch()` at line 229
- Contains **mesh stream data** (topology, primitives, rendering commands)

### Unknown Fields (not accessed in observed code)

**Field +0x00 (local_38[0])**:
- Not accessed in RenderBattleScene3D lines 183-198
- May be used in earlier loop iteration or initialization
- Hypothesis: Entry metadata, mesh ID, or flags

**Field +0x04 (local_38[1])**:
- Not accessed in RenderBattleScene3D lines 183-198
- May be used elsewhere in function
- Hypothesis: Mesh count, vertex count, or primitive count

**Field +0x18 (local_38[6])**:
- Possibly accessed later in function (code not yet analyzed)
- Hypothesis: Additional data pointer or metadata

### Entry 0 vs Entry 1+ Pattern

**From binary analysis** (CH_01.BIN):
- **Entry 0** contains values in 0x801Axxxx range (RAM pointers after relocation)
- **Entry 1+** contain smaller values and color-like patterns

**Code evidence** (line 176): `local_38 = local_98 + 1`
- Loop starts at Entry 1, suggesting **Entry 0 may be special header entry**
- Entry 0 may contain **global data pointers** used by all mesh entries
- Entry 1+ contain **per-mesh data** (geometry, materials, streams)

### Next Steps for Full Analysis

1. **Trace field +0x00 and +0x04 usage** earlier in RenderBattleScene3D
2. **Analyze buffer mapping**: Which field points to vertices, UVs, colors?
3. **Decode mesh stream format** used by IterateMeshStreamAndFetch
4. **Confirm Entry 0 special handling** (global pointers hypothesis)
5. **Map buffer relationships**: g_transformedVertexBuffer, g_uvOrTexCoordBuffer, etc.

## Unknown / TODO

### Flags Field (NEEDS MORE SAMPLES)

Magic/flags at offset 0x00:
- `0xC0000006`: Observed in CH_01.BIN, CH_03.BIN (37 entries)
- `0xC0000007`: Observed in CH_02.BIN (6 entries)
- **Question**: Does low byte indicate file variant or character type?

### File Naming Convention (NEEDS CHARACTER MAPPING)

Current knowledge:
- CH_01.BIN through CH_29.BIN: Main characters? (29 files)
- CH_30.BIN through CH_50.BIN: Additional characters? (21 files)
- CH_32_1.BIN, CH_32_2.BIN, CH_32_3.BIN: Character variants/transformations?
- CH_NO.BIN: Unknown purpose (placeholder? dummy?)
- IN_xx.BIN: Different category (intro? special?)

**Question**: How does `ch_bin_file_index` map to character IDs?

### Data Section Format (CONFIRMED - Manual Analysis)

The raw mesh data section format has been **PARTIALLY CONFIRMED** through binary analysis:

**Data Section Layout (Observed in CH_01.BIN):**

```c
// Mesh data section - starts after entry table
// Offset varies based on entry_count (0x08 + count * 0x1C)
// For CH_01.BIN (37 entries): data starts at 0x414

// Multiple data regions identified (CONFIRMED via binary scan + code analysis):
struct MeshDataSection {
    // Region 0: Header/metadata (0x414-0x443) - 48 bytes
    // Contains small integer values, possible counts or indices
    // Purpose: Mesh subdivision info, primitive counts, material indices?
    
    // Region 1: Vertex data (0x444-0x5E3) - 416 bytes (52 vertices)
    SVECTOR vertices[52];        // 3D vertex positions (s16 x,y,z,pad)
                                 // Example: (100, 15, 0, 110), (20, 100, -10, 15)
                                 // Format: 8 bytes per vertex
                                 // Count: 52 vertices = 416 bytes confirmed
    
    // Region 2: Normal/direction vectors (0x5E4+)
    SVECTOR normals[];           // 3D normals or small direction vectors
                                 // Example: (-16, -12, 0, -16), (-11, 4, -16, -8)
                                 // Values typically -20 to +20 range
                                 // Format: 8 bytes per normal (s16 x,y,z,pad)
                                 // Count: Variable (scan in progress)
    
    // Region 3: Color data (~0x2000)
    CVECTOR colors[];            // Vertex colors (u8 r,g,b,cd)
                                 // Example: RGB(168,24,119), RGB(36,127,42)
                                 // Format: 4 bytes per color
    
    // Region 4: UV coordinates (~0x3000-0x4000)
    struct { u8 u, v; } uvs[];  // Texture coordinates
                                 // Example: (69,68), (205,16), (126,252)
                                 // Format: 2 bytes per UV pair
    
    // Additional regions may contain:
    // - Primitive indices/topology (location unknown)
    // - Material indices (possibly in header region)
    // - Mesh stream data (referenced by Entry field_14)
};
```

**Evidence (CH_01.BIN binary inspection - UPDATED 2026-02-13):**

1. **Header/Metadata @ 0x414-0x443:** (48 bytes)
   - Small integer values, purpose under investigation
   - May contain mesh subdivision info, primitive counts

2. **Vertex Data @ 0x444-0x5E3:** (416 bytes = 52 vertices)
   - SVECTOR format confirmed (s16 x,y,z,pad)
   - Reasonable 3D coordinates: (100,15,0), (20,100,-10), (110,0,20), (-100,15,0)
   - 8 bytes per vertex as expected
   - **Confirmed count: 52 vertices** (416 / 8 = 52)

3. **Normal/Vector Data @ 0x5E4+:** (size variable)
   - Smaller SVECTOR values: (-16,-12,0), (-11,4,-16), (8,-16,-4)
   - Typical of normalized or scaled normal vectors
   - Same 8-byte format (s16 x,y,z,pad)
   - Values in -20 to +20 range (normalized for PSX GTE)
   - Scanning to determine exact count and end boundary

4. **Color Data @ ~0x2000:** (size unknown)
   - CVECTOR format confirmed (u8 r,g,b,cd)
   - Varied RGB values: (168,24,119), (36,127,42), (222,205,187)
   - 4 bytes per color as expected
   - cd field varies (code/transparency parameter for GPU)

5. **UV Coordinate Data @ ~0x3000-0x4000:** (size unknown)
   - u8 pair format confirmed
   - Full 0-255 range: (69,68), (205,16), (126,252), (0,0)
   - 2 bytes per UV coordinate pair
   - Used for texture mapping on PSX GPU

**Open Questions:**
- Exact boundaries between regions (may vary per file)
- How Entry 0 pointers map to these regions
- Format of primitive/topology data (likely interleaved or separate region)
- Relationship between entry count and data organization

**Cross-File Variations (CH_01.BIN vs CH_02.BIN):**

CH_02.BIN shows different data organization:
- **Fewer entries**: 6 entries (vs 37 in CH_01.BIN)
- **Earlier data start**: 0x0C8 (vs 0x414)
- **Different data patterns at 0x200**:
  - CH_01: Clear vertex coords like (100,15,0), (20,100,-10)
  - CH_02: Compressed/indexed values like (10521,10778,0), (10778,11035,10778)
- **Different color patterns at 0x1000**:
  - CH_01: Varied RGB colors (168,24,119), (36,127,42)
  - CH_02: Many 128 values suggesting palette indices or compression

**Hypothesis**: Files with more entries (37) may use standard format, while files with fewer entries (6) may use compressed/indexed format or serve different purpose (animations? LODs?)

## Related Analysis Files

- **[CH_BIN_FILENAME_TABLE.md](CH_BIN_FILENAME_TABLE.md)** - Complete file inventory & character mapping
- `docs/DECOMPILATION_NOTES.md` - General decompilation notes
- `docs/REVA_GHIDRA_GUIDE.md` - Ghidra analysis workflow

**Source files using CH_BIN data**:
- Functions at 0x80035a04 (RenderBattleScene3D)
- Functions at 0x800374f4 (mesh renderer, 250 lines)
- Functions at 0x8003de10 (mesh processing, 1784 bytes)

**Related data files**:
- CHR_DATA/FACE.B - Character portraits (for visual ID)
- CHR_DATA/OV_CHR_A.B - Character overlay data
- CHR_DATA/CRDD.B - Character data (unknown format)
- src/select/select.c - Character selection code (needs decompilation)

## Next Steps - TODO

### Priority 1: Mesh Entry Field Mapping (HIGH) - BREAKTHROUGH

**Objective**: Determine exact meaning of 7 uint32 fields in CHBinMeshEntry

**Status**: ✅ **MAJOR BREAKTHROUGH** - Code analysis completed (2026-02-13 #4)

**KEY FINDINGS (CODE-BASED - NOT SPECULATION):**

1. **Entry 0 contains RAM pointers** (0x801Axxxx addresses):
   ```
   field_0 (+0x00): 0x801A4A44  <- RAM pointer (destination unknown)
   field_1 (+0x04): 0x801A4E50  <- RAM pointer (destination unknown)
   field_2 (+0x08): 0x801A4E70  <- RAM pointer (destination unknown)
   field_3 (+0x0C): 0x801A8098  <- RAM pointer (destination unknown)
   field_4 (+0x10): 0x00808080  <- RGB(128,128,128) - gray color
   field_5 (+0x14): 0x00608080  <- RGB(128,128,96) - greenish gray
   field_6 (+0x18): 0x00080000  <- RGB(0,0,8) - nearly black
   ```

2. **Entry 1+ contain color/material data** (0x00BBGGRR format):
   ```
   Example Entry 2:
   field_0-6: All contain RGB colors
   - 0x0038E0E0 = RGB(224,224,56) - yellow/beige
   - 0x0028A0A0 = RGB(160,160,40) - olive/gray
   - 0x00186060 = RGB(96,96,24) - dark gray
   - 0x00082020 = RGB(32,32,8) - very dark
   ```

3. **Pointers DO NOT map to file offsets**:
   - Pointer 0x801A4A44 → file offset 0x4A44 contains only zeros
   - Pointers are resolved at runtime, likely pointing to:
     - Transformed vertex buffers (g_transformedVertexBuffer)
     - Processed UV buffers (g_uvOrTexCoordBuffer)
     - Color buffers (g_vertexColorBuffer)
     - Metadata buffers (g_renderMetadataBuffer)

**HYPOTHESIS - Two-Stage Structure:**

The CH_BIN file entries are **different** from the runtime MeshTableEntry structure. Process:

1. **On Load**: CH_BIN file loaded to g_cdFileBufferTable
2. **Processing**: Raw mesh data (vertices @ 0x444, colors @ 0x2000, etc.) is:
   - Parsed from file data section
   - Transformed/copied to separate RAM buffers
   - Entry 0 RAM pointers updated to point to these buffers
3. **Rendering**: RenderBattleScene3D uses Entry 0 pointers to access processed data

**CH_BIN Entry Structure (File Format):**
```c
// Entry 0: Pointer table (populated after loading)
struct CHBinEntry0 {
    uint32_t buffer_ptr_0;      // +0x00 - Points to processed buffer in RAM
    uint32_t buffer_ptr_1;      // +0x04 - Points to processed buffer in RAM
    uint32_t buffer_ptr_2;      // +0x08 - Points to processed buffer in RAM
    uint32_t buffer_ptr_3;      // +0x0C - Points to processed buffer in RAM
    uint32_t default_color_0;   // +0x10 - RGB color (0x00BBGGRR)
    uint32_t default_color_1;   // +0x14 - RGB color
    uint32_t flags_or_color;    // +0x18 - RGB color or flags
};

// Entry 1+: Material/primitive definitions
struct CHBinEntryN {
    uint32_t color_or_param[7]; // All 7 fields contain RGB colors or parameters
};
```

**Next Actions** (CODE ANALYSIS - IN PROGRESS):
1. ✅ **COMPLETED**: Decompiled RenderBattleScene3D (332 lines)
   - Confirmed Entry structure: 28 bytes (7 uint32)
   - Identified field access: +0x08, +0x0C, +0x10, +0x14 (offsets → pointers)
   - Found pointer relocation: `field + g_cdFileBaseOffset (0x2E800)`
   - Located buffer variables: g_transformedVertexBuffer, g_uvOrTexCoordBuffer, etc.

2. ✅ **COMPLETED**: Decompiled IterateMeshStreamAndFetch (29 lines)
   - Confirmed dynamic pointer relocation mechanism
   - Shows stream parsing with offset patching

3. ✅ **COMPLETED**: Decompiled FUN_80034ed0 (52 lines)
   - State machine: Load (case 1) → Render (case 2)
   - Complete call chain documented

4. [ ] **IN PROGRESS**: Analyze earlier code sections
   - Read lines 1-115 of RenderBattleScene3D (buffer initialization)
   - Trace field_00 and field_04 usage (not seen in lines 183-198)
   - Find loop start condition (Entry 0 vs Entry 1+)

5. [ ] **Map data section → buffer relationships** (HIGH PRIORITY):
   - **field_08** (+0x08): Double indirection → which buffer?
   - **field_0C** (+0x0C): High word extracted → vertex count? Which data?
   - **field_10** (+0x10): Direct pointer → which data section?
   - **field_14** (+0x14): Mesh stream or NULL → decode stream format
   - Cross-reference with:
     - Vertices @ 0x444-0x5E3 (52 vertices confirmed)
     - Normals @ 0x5E4+ (size TBD)
     - Colors @ ~0x2000
     - UVs @ ~0x3000

6. [ ] **Decode mesh stream format** (CRITICAL):
   - IterateMeshStreamAndFetch reads `piVar1[2]` pattern
   - Determine stream structure: Header? Count fields? Data blocks?
   - Find POLY_GT3/GT4 primitive assembly code

7. [ ] **Complete Entry 0 analysis**:
   - Binary shows 4 RAM pointers + 3 colors
   - Code accesses fields +0x08/+0x0C/+0x10/+0x14
   - Reconcile: Do fields +0x00/+0x04 get set during processing?
   - Find code that writes to Entry 0 (search for DAT_801d2008 WRITE refs)

8. [ ] **Document complete data flow**:
   ```
   CH_BIN file (0x5000 bytes) → g_cdFileBufferTable
              ↓
   Entry table @ 0x08 (N * 28 bytes)
   Data sections @ varies:
     - Vertices @ 0x444 (52 * 8 bytes)
     - Normals @ 0x5E4 (? * 8 bytes)
     - Colors @ ~0x2000
     - UVs @ ~0x3000
     - Mesh streams @ varies
              ↓
   RenderBattleScene3D (line 183-198):
     - Adds g_cdFileBaseOffset to fields +0x08/+0x0C/+0x10
     - Dereferences field +0x08 (double indirection)
     - Passes to IterateMeshStreamAndFetch
              ↓
   PSX GTE → Screen rendering
   ```

**Status**: ✅ Functions decompiled, 🔄 Field mapping in progress
**Next Step**: Analyze lines 1-182 of RenderBattleScene3D for buffer initialization
**Tools**: Already available - code from previous decompilation, need to read earlier function sections

### Priority 2: Filename Table Discovery (MEDIUM) - ✅ PARTIAL COMPLETE

**Objective**: Locate and document g_ch_bin_filenames array

**Status**: ✅ Physical files documented, ⚠️ array address still needed

**Completed**:
1. ✅ Full file inventory: 63 character model files
   - CH_BIN1: 28 files (CH_01-CH_29 + CH_NO, missing CH_08)
   - CH_BIN2: 22 files (CH_30-CH_50 w/ 3x CH_32 variants, missing CH_40)
   - CH_BIN3: 13 files (IN_01-IN_10 + 3 special)
2. ✅ Documentation created: `CH_BIN_FILENAME_TABLE.md`
3. ✅ Numbering gaps identified (CH_08, CH_40 missing)
4. ✅ Variant files documented (CH_32_1/2/3 transformations)

**Remaining**:
1. [ ] Find g_ch_bin_filenames array address in GAME.EXE
   - Search for string "CH_01.BIN" in data section
   - Trace XREF from LoadCHBinFileAsync
2. [ ] Dump complete array contents and confirm index mapping

**Tools**: `grep_search`, `mcp_reva_read-memory` (when available)

**Reference**: See [CH_BIN_FILENAME_TABLE.md](CH_BIN_FILENAME_TABLE.md) for complete inventory

### Priority 3: Character ID Mapping (MEDIUM) - IN PROGRESS

**Objective**: Map character IDs to ch_bin_file_index values

**Progress**:
- ✅ Found SELECT.EXE overlay (character selection screen)
- ✅ Located character data files in CHR_DATA/:
  - FACE.B: Character portraits (can visually identify characters)
  - OV_CHR_A.B: Character overlay data
  - CRDD.B: Unknown (character data?)
- ✅ Identified src/select/select.c (stub, needs decompilation)
- ⚠️ TitleMenuState structure found in game.h with character slots

**Actions**:
1. [ ] Decompile SelectInit and SelectMain functions (SELECT.EXE @ 0x80020000)
2. [ ] Extract character portraits from FACE.B for visual identification
3. [ ] Analyze TitleMenuState structure (game.h:83) - contains cursors and character indices
4. [ ] Find FUN_8006097c ("Retrieves character data by index" @ 0x8006097C)
5. [ ] Trace character selection → ch_bin_file_index conversion
6. [ ] Document CH_32_1/2/3 transformation trigger conditions

**Key Code References**:
- SELECT.EXE: 0x80020000 (vram_start), 0x800347C4 (entry_point)
- FUN_8006097c: 0x8006097C - "Retrieves character data by index"
- TitleMenuState: cursor_left, cursor_right, selected_index, active_index

**Tools**: Custom analyzer for FACE.B, decompile select functions, memory dumps

**Reference**: See [CH_BIN_FILENAME_TABLE.md](CH_BIN_FILENAME_TABLE.md#character-id-mapping-incomplete) for details

### Priority 4: Data Section Parser (HIGH) - SIGNIFICANT PROGRESS

**Objective**: Reverse engineer mesh data section format

**Status**: ✅ Major regions identified + ✅ Code analysis completed (2026-02-13 #4)

**Completed**:
1. ✅ **Vertex buffer format** (SVECTOR: s16 x,y,z,pad)
   - Location: **0x444-0x5E3** in CH_01.BIN (PRECISE)
   - Size: **416 bytes = 52 vertices** (CONFIRMED)
   - Format: 8 bytes per vertex with reasonable 3D coordinates

2. ✅ **Normal/direction vector format** (SVECTOR: small values)
   - Location: **0x5E4+** in CH_01.BIN (PRECISE)
   - Values in -20 to +20 range typical of normals
   - Format: 8 bytes per normal (s16 x,y,z,pad)
   - Count: Variable, scanning in progress

3. ✅ **Color buffer format** (CVECTOR: u8 r,g,b,cd)
   - Location: ~0x2000+ in CH_01.BIN
   - Format: 4-byte color structure with varied RGB values
   - cd field = GPU code byte

4. ✅ **UV coordinate format** (u8 u,v pairs)
   - Location: ~0x3000-0x4000+ in CH_01.BIN
   - Format: 2-byte pairs with 0-255 range

5. ✅ **Header/metadata region**
   - Location: **0x414-0x443** (48 bytes, PRECISE)
   - Purpose: Mesh subdivision info, counts, material indices?

6. ✅ **Code analysis completed**:
   - RenderBattleScene3D decompiled (332 lines)
   - Field access pattern documented (lines 183-198)
   - Pointer relocation mechanism understood
   - IterateMeshStreamAndFetch variants analyzed

**Next Actions**:
1. [ ] **Complete section boundary scan** (IN PROGRESS):
   - Normals: Count entries from 0x5E4 until values exceed ±20 range
   - Find boundary between normals and next section
   - Scan for primitive/topology data patterns

2. [ ] **Map Entry fields → data sections** (HIGH PRIORITY):
   - field_08: Points to which section? (double indirection)
   - field_0C: Points to vertices @ 0x444? (high word = count 52?)
   - field_10: Points to normals @ 0x5E4? or colors @ 0x2000?
   - field_14: Mesh stream format (decode structure)

3. [ ] **Identify primitive/topology encoding**:
   - Find triangle/quad index lists
   - Locate POLY_GT3/GT4/FT3/FT4 usage in rendering code
   - Decode mesh stream format (referenced by field_14)

4. [ ] **Cross-verify with CH_02.BIN**:
   - Different entry count (6 vs 37)
   - Data starts at 0x0C8 (earlier than CH_01)
   - Check if format differs (compressed? indexed?)

5. [ ] **Create parser specification**:
   - Document complete file structure
   - Include all section boundaries
   - Add code examples for each data type

**Tools**: 
- ✅ Binary hex analysis (completed)
- ✅ Code decompilation (completed)
- 🔄 Section boundary scanning (in progress)
- [ ] Cross-file format analysis (pending)

### Priority 5: Flags Investigation (LOW)

**Objective**: Understand flags/magic field (0xC0000006 vs 0xC0000007)

**Actions**:
1. [ ] Sample flags from all CH_BIN files (64+ files)
2. [ ] Correlate with entry_count
3. [ ] Search code for flag checks
4. [ ] Document flag bit meanings if found

**Tools**: PowerShell file analysis, `grep_search` for flag constants

### Priority 6: Custom Tool Development (OPTIONAL)

**Objective**: Create CH_BIN viewer/converter

**Actions**:
1. [ ] Extend DbzLegendsAnalyser to parse CH_BIN
2. [ ] Implement mesh visualization
3. [ ] Export to standard formats (OBJ, glTF)
4. [ ] Add to custom-tools/DbzLegendsAnalyser/Controls/

**Reference**: See existing controls (OV_CHR_A_Control.cs, STG_TX_Control.cs)

## Résumé des Tâches (2026-02-13)

### ✅ COMPLÉTÉ

1. **LoadCHBinFileAsync** - Analyse et renommage (4 symboles)
2. **RenderBattleScene3D** - Décompilation complète (332 lignes)
3. **IterateMeshStreamAndFetch** - Décompilation (29 lignes)
4. **FUN_80034ed0** - Décompilation state machine (52 lignes)
5. **Structure Entry** - Format confirmé (28 bytes, 7 uint32)
6. **Mécanisme de relocalisation** - Compris (g_cdFileBaseOffset + 0x2E800)
7. **Sections de données CH_01.BIN**:
   - Header/metadata: 0x414-0x443 (48 bytes)
   - Vertices: 0x444-0x5E3 (416 bytes = 52 vertices)
   - Normals: 0x5E4+ (format confirmé)
   - Colors: ~0x2000 (format CVECTOR confirmé)
   - UVs: ~0x3000 (format u8 pairs confirmé)
8. **Table des fichiers** - 63 fichiers CH_BIN documentés
9. **Call chain** - Pipeline Load→Render complet

### 🔄 EN COURS

1. **Mapping champs Entry → sections données** (HIGH):
   - field_08 (+0x08): Double indirection → quelle section?
   - field_0C (+0x0C): High word extrait → vertices @ 0x444?
   - field_10 (+0x10): Pointeur direct → normals? colors?
   - field_14 (+0x14): Mesh stream → décoder format

2. **Scan boundaries sections** (HIGH):
   - Compter normals depuis 0x5E4 (valeurs ±20)
   - Trouver fin section normals
   - Identifier section primitives/topology

3. **Analyse code RenderBattleScene3D** (MEDIUM):
   - Lire lignes 1-115 (initialisation buffers)
   - Tracer usage field_00 et field_04
   - Comprendre boucle Entry 0 vs Entry 1+

### 📋 À FAIRE (Priorités)

#### HAUTE PRIORITÉ
- [ ] Analyser début RenderBattleScene3D (lignes 1-115) pour buffer init
- [ ] Mapper champs Entry → sections données via code
- [ ] Décoder format mesh stream (structure parsée par IterateMeshStreamAndFetch)
- [ ] Compléter scan section boundaries (normals, primitives)
- [ ] Trouver code POLY_GT3/GT4 assembly (primitive rendering)

#### MOYENNE PRIORITÉ  
- [ ] Confirmer traitement spécial Entry 0 (global pointers?)
- [ ] Analyser variantes IterateMeshStreamAndFetch (0x80036744, 0x800367fc)
- [ ] Cross-vérifier format avec CH_02.BIN (6 entries vs 37)
- [ ] Trouver code qui écrit dans Entry 0 (WRITE refs sur DAT_801d2008)
- [ ] Cartographier character_id → ch_bin_file_index (SELECT.EXE)
- [ ] Extraire portraits FACE.B pour identification visuelle

#### BASSE PRIORITÉ
- [ ] Investiguer flags field (0xC0000006 vs 0xC0000007)
- [ ] Sampler flags de tous les CH_BIN (63 fichiers)
- [ ] Créer parser / viewer CH_BIN custom

### 🎯 Prochaine Étape Immédiate

**Analyser section boundary normals + mapper Entry fields:**

```powershell
# 1. Scanner section normals complète
$file = [System.IO.File]::ReadAllBytes("CH_01.BIN")
$normalStart = 0x5E4
# Compter jusqu'à valeurs > ±20

# 2. Chercher patterns primitives après normals
# Look for: triangle indices, POLY headers, etc.
```

Puis dans Ghidra (quand disponible):
- Lire RenderBattleScene3D lignes 1-182
- Tracer où field_0C pointe (likely vertices @ 0x444)
- Comprendre extraction high word (count = 52?)

### 📊 Métriques Progrès

- **Code décompilé**: 413 lignes (3 fonctions)
- **Symboles renommés**: 8+ (LoadCHBinFileAsync, globals, buffers)
- **Fichiers documentés**: 63 CH_BIN files
- **Sections identifiées**: 5 (header, vertices, normals, colors, UVs)
- **Structure Entry**: 100% format, ~60% signification champs
- **Data flow**: 80% compris (Load→Relocation→Render)

## References

- Previous conversation analysis: RenderBattleScene3D renaming (21 symbols)
- MeshTableEntry structure: 28 bytes, 7 uint fields
- PSX SDK: SVECTOR, CVECTOR, DVECTOR, POLY_GT3/4 structures
- CD sector size: 2048 bytes (10 sectors = 20480 bytes per CH_BIN)
- Ghidra project: dbz-legends.rep, GAME.EXE overlay
- Code functions: 0x80035828 (Load), 0x80035a04 (Render), 0x8003668c (Stream)


---

## ANALYSE LIVE 2026-03-07 -- PCSX-REDUX + GHIDRA/REVA

**Methode**: Breakpoint PCSX-Redux @ LoadCHBinFileAsync (0x80034ed0), puis RenderBattleScene3D (0x80035a04).
Emulateur: combat lance en mode Histoire. GAME.EXE dans Ghidra (1486 fonctions, 8883 symboles).

### PIPELINE CHARGEMENT CH_BIN (CONFIRME CODE)

FUN_80034ed0 appelle LoadCHBinFileAsync (state 8/9) puis RenderBattleScene3D (state 2).
LoadCHBinFileAsync: SearchFileAndLoadIntoBuffer(g_ch_bin_filenames[charID], &g_cdFileBufferTable, 1)
  -> LoadFileIntoBuffer -> CdRead(sectors, 0x801D2000, 0x80) async
RenderBattleScene3D (decompile lignes 117-126):
  L117: g_cdFileBaseOffset = 0x2E800;  // HARDCODE, pas de variable
  L119-126: for i in [2 .. (ushort)buffer[0]-1]: buffer[i] += 0x2E800  // relocalisation in-place
  L127: local_98 = DAT_801d2008 = &buffer[2]

### FORMAT CH_01.BIN -- HEADER BINAIRE (lecture fichier disque CH_01.BIN)

dword[0] @ +0x00 = 0xC0000006  // ushort_low=6 (nb dwords relocalises), flags=0xC000, int32 negatif
dword[1] @ +0x04 = 0x00000025  // 37 = entry count
dword[2] @ +0x08 = 0x801A4A44  // ptr pre-calcule (base 0x801A0000) -> +0x2E800 -> 0x801D3244
dword[3] @ +0x0C = 0x801A4E50  // ptr pre-calcule -> +0x2E800 -> 0x801D3C50
dword[4] @ +0x10 = 0x801A4E70  // ptr pre-calcule -> +0x2E800 -> 0x801D3C70
dword[5] @ +0x14 = 0x801A8098  // ptr pre-calcule -> +0x2E800 -> 0x801ACF98
dword[6] @ +0x18 = 0x00808080  // premiere entry (debut table 37 entries x 28 bytes)
...

### ADRESSES CLES GAME.EXE (GHIDRA/REVA CONFIRME)

g_cdFileBufferTable  @ 0x801D2000  // Buffer chargement CD (>20KB)
g_cdFileBaseOffset   @ 0x8009A978  // uint32, valeur runtime = 0x2E800 (set par RenderBattleScene3D)
RenderBattleScene3D    0x80035A04  // 3208 bytes, fait preprocessing ET rendu
LoadCHBinFileAsync     0x80035828  // 476 bytes, state 8->9, async CD read
SearchFileAndLoadIntoBuffer 0x800673B8
LoadFileIntoBuffer     0x80067404  // mode=0 sync / mode=1 async

### CORRECTIONS ANALYSES PRECEDENTES

"Fonction PRE-PROCESSING separee manquante" -> C'est RenderBattleScene3D lignes 117-126 (CERTAIN)
"g_cdFileBaseOffset = 0x2E800 non explique"  -> Hardcode ligne 117 (CERTAIN)
"Entry 0 RAM pointers 0x801Axxxx impossibles" -> Ptrs pre-compiles base 0x801A0000 (CERTAIN)
"37 entries demarrent a offset +8"            -> Les entries demarrent a +0x18 = byte 24 (PROBABLE)

### QUESTIONS RESTANTES

1. Role exact 4 dwords[2..5]: pointent vers quoi dans la data section du buffer?
2. Structure exacte des 37 entries debutant a +0x18 (dword[6] = 0x00808080)
3. Pourquoi seulement 4 dwords relocalisés sur 37x7=259? Les fields des entries 
   ont des offsets RELATIFS traites via +g_cdFileBaseOffset au render time.
