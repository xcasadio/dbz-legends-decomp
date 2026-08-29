# Structure CH_BIN

Document de reference compact pour le format CH_BIN.

Les journaux de session, scans corpus detailles et hypotheses intermediaires ont ete deplaces dans [structure-ch-bin-files.history.md](structure-ch-bin-files.history.md).

## Politique de maintenance

- Ce fichier ne garde que les proprietes stables du format, les structures minimales utiles, et les zones d'ombre encore actives.
- Les dumps bruts, listes exhaustives d'occurrences et anciennes hypotheses vont dans le fichier history.
- Toute nouvelle preuve doit mettre a jour une section existante plutot que recreer un journal chronologique.

## 1. Resume factuel

- `CH_01.BIN` a `CH_29.BIN` = `CH_BIN1`
- `CH_30.BIN` a `CH_50.BIN` + `CH_32_1..3` = `CH_BIN2`
- `IN_01.BIN` a `IN_10.BIN` + `IN_IN.BIN`, `IN_OT2.BIN`, `IN_OUT.BIN` = `CH_BIN3`
- Ces fichiers contiennent les ressources de rendu de combat : table d'entrees, listes de segments vertex/mesh/lighting, streams d'animation, et banques texture/CLUT.
- Le pipeline runtime pertinent existe dans `GAME.EXE` et `VS.EXE`.
- Les structures minimales du header, des entrees et des listes de segments sont prouvees.
- Les formats exacts de `AnimCmd_LoadTexture` et `AnimCmd_AsyncLoadTexture` sont prouves.
- Le framing des batches `AnimStream` est prouve.
- Les inconnues restantes sont localisees a `primitive_count_packed.high16`, `unknown_0x08`, et au partage semantique exact entre les slots header texture une fois presents; leur validite structurelle depend maintenant certainement de `reloc_loop_bound`.

## 2. Vue d'ensemble runtime

- `LoadCHBinFileAsync` est le handler `state1` du dispatcher battle-scene: il utilise `runtimePointers.dataPtr13.low16` comme sous-automate, délègue les sous-états `< 8` à `FUN_80064d70`, lance la lecture asynchrone du `CH_BIN` au sous-état `8`, puis poll `CdReadSync` au sous-état `9` jusqu'au passage en `state2`.
- `RenderBattleScene3D` applique le header runtime a `0x801D2000`, relocalise les pointeurs compile-time, puis parcourt la table `CHBinMeshEntry`.
- Les metadonnees utiles sont recopiees dans des buffers derives : `g_renderMetadataBuffer`, `g_meshEntryFlagsHiBuf`, `g_meshCountBuffer`, `g_meshStreamPtrBuffer`, `g_meshOffsetBuffer`.
- Les handlers `AnimStream` consomment ensuite surtout ces buffers derives, pas la table `CHBinMeshEntry` brute.
- Dans `GAME.EXE` comme dans `VS.EXE`, aucun lecteur brut supplementaire de `g_chBinEntryTableBasePtr` n'est prouve hors `RenderBattleScene3D` sur les chemins de combat deja relies.

## 3. Prefixe header relocalisable

```c
typedef struct CHBinFileHeaderPrefix {
    ushort reloc_loop_bound;  // +0x00 CERTAIN
    ushort header_flags;      // +0x02 CERTAIN
    uint   entry_count;       // +0x04 CERTAIN
    uint   ptr_entry_table;   // +0x08 CERTAIN
    uint   ptr_section_3;     // +0x0C si reloc_loop_bound > 3
    uint   ptr_section_4;     // +0x10 si reloc_loop_bound > 4
    uint   ptr_section_5;     // +0x14 si reloc_loop_bound > 5
    // d'autres slots relocalises peuvent suivre tant que l'indice dword < reloc_loop_bound
} CHBinFileHeaderPrefix;
```

### 3.1 Roles minimaux des sections 3/4/5

| Slot | Acces observes | Lecture minimale | Statut |
|------|----------------|------------------|--------|
| `ptr_section_3` | `tex_set tbl=3`, `load_set tbl=3` | banque texture/CLUT primaire | PROBABLE |
| `ptr_section_4` | `load_set tbl=4`, `tex_set tbl=4` | slot header runtime valide seulement si `reloc_loop_bound > 4`; banque image/texture, async-capable dans le corpus | PROBABLE |
| `ptr_section_5` | `load_set tbl=5` | slot header runtime valide seulement si `reloc_loop_bound > 5`; banque image/texture secondaire, sync-only dans le corpus atteint | PROBABLE |

### 3.2 Proprietes prouvees

- Les dwords runtime valides du header sont exactement les indices `2 .. reloc_loop_bound-1` relocalises par `RenderBattleScene3D`.
- `tbl=3/4/5` selectionne structurellement les slots header `dword[3/4/5]` quand ils sont relocalises; donc `+0x10` n'est valide que si `reloc_loop_bound > 4`, et `+0x14` que si `reloc_loop_bound > 5`.
- Les contre-exemples minimaux sont maintenant bornes: `CH_39.BIN` et `CH_44.BIN` ont `reloc_loop_bound = 4`, donc `+0x10` n'y est pas un pointeur header runtime; `CH_20.BIN`, `CH_24.BIN`, `CH_NO.BIN`, `IN_IN.BIN`, `IN_OT2.BIN`, `IN_OUT.BIN` ont `reloc_loop_bound = 5`, donc `+0x14` n'y est pas un slot runtime valide.
- `ptr_section_3` montre souvent un contenu compatible CLUT/palette 16-bit.
- `ptr_section_4` et `ptr_section_5`, quand ils sont valides, pointent souvent vers des blocs plus denses compatibles image/donnees texture.
- Sur les cas decompresses prouvables atteints, `ptr_section_4` et `ptr_section_5` partagent le meme framing LZSS : `load_set tbl=4` LZ observe `24` fois, `load_set tbl=5` LZ observe `10` fois.
- Le slot `4` est le seul des deux avec des hits `tex_set` async atteints dans le corpus courant (`2` cas); le slot `5` n'apparait pour l'instant que via `load_set` sync (`10` LZ + `1` raw `16x1`).
- Aucune difference de consommateur n'est prouvee dans le code: les slots valides passent par le meme couple `DecompressLZSS -> LoadImage_ReturnTPageOrClutId` ou par l'init async homologue quand `tbl=4` est choisi.
- `+0x14` n'est pas la fin structurelle du header: des slots etendus `tbl=6/7/8/9` sont atteints par `load_set` dans `CH_04.BIN` et `CH_49.BIN`, ce qui prouve que `reloc_loop_bound` ouvre un tableau de slots relocalises au-dela de `ptr_section_5`.
- `ptr_section_5` n'est pas purement "image compressee": au moins un cas raw CLUT-like en `16x1` est observe (`CH_04.BIN`, `load_set tbl=5`).

### 3.3 Extension observee sur `CH_BIN3`

- Le prefixe `dw[0..4]` reste compatible avec `CHBinFileHeaderPrefix`, mais `reloc_loop_bound` n'est pas fixe par famille : le corpus `IN_xx` observe `5`, `13`, `14`, `15`, `16`, `17`, `22`, `24`, `25`, `27`.
- `IN_IN.BIN`, `IN_OT2.BIN`, `IN_OUT.BIN` ont `reloc_loop_bound = 5` et `entry_count = 1` : seuls `dw[2..4]` sont relocalises; `dw[5]` n'est pas un slot runtime prouve sur ce sous-groupe.
- `IN_01.BIN` a `IN_10.BIN` gardent `ptr_entry_table -> CHBinMeshEntry[entry_count]`. Verification disque sur les `13` fichiers `IN_xx` : la table tient entiere dans le fichier, tous les `ptr_vertex_segment_list`, `ptr_mesh_segment_list`, `ptr_lighting_segment_list` retombent dans le fichier, et les premiers records pointes reutilisent les layouts `8/16/8` deja prouves.
- Sur `IN_xx`, `ptr_anim_stream` est soit un pointeur in-file, soit `0`; les cas nuls sont courants dans les grands fichiers.
- Les slots etendus de `CH_BIN3` ne sont pas morts : `AnimCmd_ChEffSet` lit un slot header indexe par `param_1[1].high8`, et des sequences init propres dans `IN_08.BIN` utilisent `0x16..0x1A`, soit les slots runtime `22..26`.
- Le format structurel du blob `AnimCmd_ChEffSet` est maintenant borne plus finement :

```c
typedef struct CHBin3ChEffBlobGroupHeader {
    uint8_t record_count;   // low8 de hdr0, CERTAIN
    uint8_t group_flags;    // high8 de hdr0, bit7=terminator apres ce groupe, CERTAIN
    uint8_t delay_add;      // low8 de hdr1, CERTAIN
    uint8_t reserved_0x03;  // high8 de hdr1, non lu; observe a 0 sur le sous-corpus strict, PROBABLE
} CHBin3ChEffBlobGroupHeader;

typedef struct CHBin3ChEffBlobRecord {
    uint16_t control_0x00;      // low5 = delta CLUT applique a init.word4; bits10..11 = permutation/orientation XY; bits6..7 observes aussi, sens exact INCONNU
    int8_t   signed_0x02_lo;    // low8 de word1, signe, CERTAIN
    int8_t   signed_0x02_hi;    // high8 de word1, signe, CERTAIN
    uint8_t  size_0x04_lo;      // low8 de word2, largeur/portee XY, CERTAIN
    uint8_t  size_0x04_hi;      // high8 de word2, hauteur/portee XY, CERTAIN
    int8_t   delta_uv_0x06_lo;  // low8 de word3, ajoute au U de base init.word3.low8, CERTAIN
    int8_t   delta_uv_0x06_hi;  // high8 de word3, ajoute au V de base init.word3.high8, CERTAIN
    int8_t   span_uv_0x08_lo;   // low8 de word4, ajoute apres delta_uv_0x06_lo pour former U1/U3, CERTAIN
    int8_t   span_uv_0x08_hi;   // high8 de word4, ajoute apres delta_uv_0x06_hi pour former V2/V3, CERTAIN
} CHBin3ChEffBlobRecord; // 5 mots / 10 bytes
```

- Les blobs peuvent chainer plusieurs groupes consecutifs jusqu'a un groupe dont `hdr0.bit15 = 1`; exemples certains : `IN_01.BIN` slots `16/17/18/20`, `IN_03.BIN` slot `14`, `IN_04.BIN` slots `8/9/13/16`, `IN_06.BIN` slots `22/24`, `IN_07.BIN` slots `21/23`.
- `record_count` observe dans les blobs valides : `1`, `2`, `4`, `5`.
- `delay_add` observe : `1`, `2`, `3`, `4`, `5`, `8`.
- Dans les `init` `AnimCmd_ChEffSet` plausibles actuellement observes sur `CH_BIN3`, `word2.low8` vaut toujours `0`; le delai effectif provient donc entierement de `delay_add` au niveau blob, a preuve actuelle.
- `AnimCmd_ChEffSet` decode maintenant aussi `init.word3` et `init.word4` avec certitude : `init.word3.low8/high8` fournissent le couple UV de base, et `init.word4` fournit la base de CLUT a laquelle `record.control_0x00.low5` ajoute un delta.
- `group_header.reserved_0x03` n'est pas lu par le decodeur actuel; sur le sous-corpus strict des blobs `ChEff` valides, il est toujours observe a `0`.
- `record.control_0x00.bits6..7` sont observes dans le corpus (`mask 0x00C0`), mais aucun chemin code actuellement relie dans `AnimCmd_ChEffSet` ne les consomme.
- Des groupes terminaux peuvent contenir uniquement des records nuls; le cas le plus net est `IN_07.BIN` slot `21`, groupe final `0x8005` avec `5` records `0`.

## 4. Table des entrees

```c
typedef struct CHBinMeshEntry {
    uint entry_id_packed;             // +0x00 CERTAIN
    uint primitive_count_packed;      // +0x04 CERTAIN
    uint unknown_0x08;                // +0x08 valeur CERTAINE, sens INCONNU
    uint ptr_vertex_segment_list;     // +0x0C CERTAIN
    uint ptr_mesh_segment_list;       // +0x10 CERTAIN
    uint ptr_lighting_segment_list;   // +0x14 CERTAIN
    uint ptr_anim_stream;             // +0x18 CERTAIN
} CHBinMeshEntry; // 0x1C
```

### 4.1 Proprietes minimales prouvees

| Offset | Champ | Propriete minimale prouvee |
|--------|-------|----------------------------|
| `+0x00` | `entry_id_packed` | `low16` copie dans `g_renderMetadataBuffer`, `high16` copie dans `g_meshEntryFlagsHiBuf` |
| `+0x04` | `primitive_count_packed` | `low16` copie dans `g_meshCountBuffer`; `high16=0` n'apparait qu'avec `low16 ∈ {0,1}`, alors que `high16=1` n'apparait jamais avec `low16=0`; le meme triplet `(vertex,mesh,light)` peut reapparaitre avec plusieurs `primitive_count_packed`, donc le champ ne selectionne pas a lui seul un layout de segments |
| `+0x08` | `unknown_0x08` | `high16 = primitive_count_packed.high16` sur `790/792` entrees (exceptions `CH_09.BIN E6/E7`); couples observes `(low16, prim.high16)` limites a `(0,0)`, `(1,1)`, `(1,3)`, `(2,1)`; le meme triplet `(vertex,mesh,light)` peut reapparaitre avec plusieurs `unknown_0x08`; champ non lu dans les chemins principaux prouves |
| `+0x0C` | `ptr_vertex_segment_list` | liste stride `8` |
| `+0x10` | `ptr_mesh_segment_list` | liste stride `16` |
| `+0x14` | `ptr_lighting_segment_list` | liste stride `8` |
| `+0x18` | `ptr_anim_stream` | pointe vers le framing `AnimStream` documente ci-dessous |

### 4.2 Cas rares encore ouverts

- `primitive_count_packed.high16 > 1` n'apparait actuellement que dans `CH_09.BIN E6/E7` et `CH_31.BIN E4`.
- `primitive_count_packed.high16 = 0` n'apparait actuellement qu'avec `primitive_count_packed.low16 = 0` ou `1`; inversement, la classe `high16 = 1` n'apparait jamais avec `low16 = 0`.
- En transitions adjacentes, cela donne trois arrivees rares : `CH_09 E5->E6`, `CH_09 E6->E7`, `CH_31 E3->E4`.
- `unknown_0x08.low16` observe ne prend que les classes `0`, `1`, `2`.
- `unknown_0x08.low16 = 0` est la seule classe observee avec `primitive_count_packed.high16 = 0`; elle couvre aussi toutes les entrees `primitive_count_packed.low16 = 0`, alors que `unknown_0x08.low16 = 1` n'apparait jamais avec `primitive_count_packed.low16 = 0`.
- Le meme triplet exact `(ptr_vertex_segment_list, ptr_mesh_segment_list, ptr_lighting_segment_list)` peut reapparaitre dans un meme fichier avec des couples `(primitive_count_packed, unknown_0x08)` differents; exemples certains: `CH_04.BIN E00/E06/E0C` et `CH_05.BIN E0B/E0C/E0D/E0E`. Donc ni `primitive_count_packed` ni `unknown_0x08` ne codent a eux seuls un layout de segments unique.
- Dans `25` fichiers, la classe `0x00000000` commence par un prefixe de `3` entrees `entry_id_packed = 0`, `primitive_count_packed = 0`, `unknown_0x08 = 0`, partageant les memes pointeurs `vertex/mesh/light`; seuls `CH_06.BIN`, `IN_04.BIN`, `IN_08.BIN` annulent le troisieme `ptr_anim_stream`.
- Une trace runtime `VS.EXE` sur `CH_10.BIN` confirme ce comportement de prefixe: a l'entree de `ExecuteAnimStreamBatch`, les trois premiers slots issus des entrees `low16=0` gardent `g_meshCountBuffer[0..2] = 0`, alors que `g_meshStreamPtrBuffer[0..2]` est non nul. Les debuts de streams observes sur ces trois slots sont des opcodes de controle (`0x06 = trans_set`, `0x23 = if_set`, puis batch vide suivi de `0x19 = end_set`), pas des commandes de rendu de primitives. Dans ce cas reel, ces entrees alimentent donc des streams actifs sans contribuer directement de primitives au rendu principal.
- Sur le sous-corpus disque `CH_BIN1/CH_BIN2`, les `23` prefixes `low16=0` observes n'ouvrent jamais par `0x02 table_set` ni par `0x0A pri_set`: leur premier opcode effectif, apres saut d'eventuels batches vides, reste borne a `0x06`, `0x07`, `0x0B`, `0x19`, `0x23` ou `0x2F`.
- Sur ce meme sous-corpus, en ajoutant les formats exacts de `0x14 utylty` (2 mots, sauf mode `2` -> `3`), de `0x17 bit_chk`, et la correction `0x0C eye_set` init -> `streamPtr + 5`, un parseur du premier batch effectif couvre maintenant les `64` entrees de prefixe `low16=0` ayant un `ptr_anim_stream != 0` sans inconnu et sans rencontrer `0x02 table_set` ni `0x0A pri_set`.
- `0x2F voice_call` n'est pas de taille fixe: la famille `cmd.high8 & 0xC0 = 0x80` retourne `streamPtr + 1`, alors que les familles `0x00/0x40/0xC0` retournent `streamPtr + 2`. Cette fermeture elimine le faux positif tardif `CH_31.BIN E0` sur `0x02`.
- En exploration stream complete recalee sur le framing runtime (`streamBase+2` = countdown initial, batch suivant a `terminator + 2`) et en traitant `0x19 end_set` comme terminaison reelle, les deux hits apparents `0x02 table_set` de `CH_19.BIN E1/E2` tombent: ils sont situes apres un `end_set` reachable et ne sont donc pas executes. Le troisieme hit apparent `CH_31.BIN E0` tombe aussi apres fermeture de `0x2F voice_call`: en mode `cmd.high8 & 0xC0 = 0x80`, `AnimCmd_VoiceCall` retourne `streamPtr + 1`, donc le batch tardif `802F 022D 4002 0000` se relit en `voice_call(1 mot)` puis `chse_call(022D 4002)`, pas en `table_set`. Le seul cas prefixe `low16=0` actuellement prouve au-dela du premier batch reste `CH_14.BIN E0`, dont le batch tardif se relit maintenant proprement en `040A 0001` (`pri_set group_id=4, poly_count=1`), `0318 0000 0002` (`bit_set`, 3 mots), `032D 4002` (`chse_call`), `F92E 2040` (`chse_vol`); `group_id=4` y correspond a l'unique entree ordinaire `entry_id_packed.low16 = 0x0400` (`E6`, `ptr_anim_stream = 0`).
- La famille transform `0x06/0x07/0x08` n'est plus un goulot d'etranglement de taille: les trois handlers lisent `word1` comme `3` specs de `5` bits (`[4:0]`, `[9:5]`, `[14:10]`) et consomment exactement `2 + N` mots, avec `N = nombre de specs dont `low4 != 0xF``.
- `0x23 if_set` est maintenant ferme structurellement: le mode `00` consomme `3` mots (`var_idx`, flags `bit4=NOT`, `bit5=AND-all`, `mask`) puis retourne soit `streamPtr + 3`, soit le mot suivant le premier tag partageant le meme `low12`; les modes `01/11` sont des sauts forward `1` mot vers la sentinelle `0x8000 | tag12`; le mode `10` est un marqueur `1` mot qui retourne `streamPtr + 1`. Sur les `22` hits prefixes `low16=0` atteints dans `CH_BIN1/CH_BIN2`, la forme d'ouverture est toujours `0023 0000 4000`, avec des paires observees `4023 -> 8023` et `4123 -> 8123`.
- Les anciens bloqueurs rares de taille sont fermes eux aussi: `0x12 x_max_set` retourne `param_1 + 3`, `0x1B base_culY` retourne `param_1 + 7`, et `0x2C cheff_wait` retourne `param_1 + 1`. `ch_eff_set` avance en plus via un tick global `AnimCmd_ChEffSet(0x8000)` appele a chaque frame par `ExecuteAnimStreamBatch`, donc `cheff_wait` n'est pas son seul mecanisme d'avancement. Le residu whole-stream vient donc surtout du branchement dependant de `g_animSharedVarTable`, pas d'une taille encore inconnue sur ces opcodes.
- Sur `CH_10.BIN`, les inits `ch_eff_set` charges en `E05/B03` et `E05/B04` fixent tous deux `base_clut = 0x78C0`; comme `AnimCmd_ChEffSet` n'ajoute que `record.word0 & 0x1F`, la CLUT valide reste bornee a `0x78C0..0x78DF`. Le residu viewer `CBA 801A/0000` observe initialement sur `F09..F12` venait d'un clear local errone qui restaurait l'etat statique du modele quand un groupe plus court arrivait ou quand un slot se terminait; apres suppression de ce clear, toutes les primitives touchees par `ch_eff_set` restent a `CBA 78D1` sur `F09..F12`. En parallele, `CH_10 E03/B02 = 0x050D 0x0102 0x0000 0x0060` explique CERTAINEMENT le seul ecart material residuel: la primitive 2 seule commute son `TPAGE` entre `001B` et `007B` par XOR `0x0060`, sans changer la CLUT. `B05/B06` ne peuvent toujours pas ecrire ces CBA: `0x09` reprojette des meshes via `TransformAndProjectMesh` puis ajuste l'OT, et `0x0A` ne fait qu'ajouter des primitives deja preparees a l'OT. Enfin, `eff_set` reste un pipeline separe task+sprites FT4 via `SpawnEffectTask` / `EffectTaskMainLoop` / `RenderTransformedSprites`, distinct du pool mesh principal.
- Le cycle runtime autour de ces buffers est maintenant ferme lui aussi: le dispatcher battle scene enchaine `state0 -> FUN_80035054`, `state1 -> LoadCHBinFileAsync`, `state2 -> RenderBattleScene3D`, `state3 -> ExecuteAnimStreamBatch`, `state4 -> FUN_80036bb0`. `RenderBattleScene3D` reconstruit les buffers derives de scene (`POLY_GT4_801f7180`, `g_uvOrTexCoordBuffer`, couleurs, metadata, mesh streams) puis passe en `state3`; pendant `state3`, `ExecuteAnimStreamBatch` appelle globalement `AnimCmd_ChEffSet(0x8000)` mais ne fait aucun clear global de ces buffers. Les ecritures deja posees par `ch_eff_set` persistent donc jusqu'a un nouvel ecrasement prouve par `table_set`, `ch_eff_set`, `tpclut_set` ou par une nouvelle reconstruction `RenderBattleScene3D`.
- Le bloc suivant `FUN_80036bb0` est maintenant borne localement: il s'agit du handler `state4` du meme dispatcher, pilote par `runtimePointers.dataPtr13.low16` comme sous-automate `0 -> 1 -> 2 -> 3`. Les sous-etats `0/1/2` avancent par un increment final commun de `dataPtr13`; le sous-etat `2` ne fait que rebrancher des pointeurs de primitives secondaires; le sous-etat `3` restaure des couleurs puis soit reboucle le dispatcher principal en ecrivant `runtimePointers.dataPtr12.high16 = 0`, soit termine explicitement la task courante via `RemoveTaskFromList`.
- Le bloc `FUN_80035054` est lui aussi ferme localement: c'est le handler `state0` du meme dispatcher. Il remet `runtimePointers.dataPtr13.low16 = 0`, peut faire progresser `FUN_80064d70(runtimePointers.dataPtr12.low16, 0)` quand `g_fileLoadFlags & 0x0C` est actif, gate l'entree sur un masque de drapeaux de 12 battle tasks, vide `g_renderScratchBuffer`, reconstruit `battleGlobalState.charPointers[0..5]` et les liens de primitives associes, calcule un nouvel index `uVar18`, l'ecrit dans `runtimePointers.dataPtr12.low16` et `PTR_8009aa30->polyFt4.low16`, snapshotte les couleurs courantes et le cache `DAT_8009ac5c`, puis passe au `state1`. `FUN_80064d70` est maintenant prouve comme une machine de chargement `CD + VAB` reposant sur `CdControl/CdSync/CdRead/CdReadSync` et `SsVabOpenHeadSticky/SsVabTransBody/SsVabTransCompleted`, donc `uVar18` est un index de ressource chargee sur ce chemin. Les tables `DAT_80087838` et `DAT_800877c0` sont des tables locales de selection latched par tag qui alimentent une partie de ce calcul.
- `LoadCHBinFileAsync` est maintenant ferme localement lui aussi: si `FUN_8006578c()` retourne `0`, il traite `runtimePointers.dataPtr13.low16` comme sous-etat de chargement; les valeurs `< 8` ne font que propager `FUN_80064d70(runtimePointers.dataPtr12.low16, substate)`, `8` lance `SearchFileAndLoadIntoBuffer(g_ch_bin_filenames[dataPtr12.low16], &g_cdFileBufferTable, 1)`, et `9` attend `CdReadSync(1)` avant d'ecrire `runtimePointers.dataPtr12.high16 = 2`. La garde locale `if (uVar3 == 0xffffffff)` reste presente mais n'est pas produite par le chaînage courant `SearchFileAndLoadIntoBuffer -> LoadFileIntoBuffer(mode=1)`, qui retourne directement `0` apres lancement de la lecture async.
- Le gate voisin n'est plus opaque non plus: `DAT_8009aa94` est maintenant borne comme une machine de streaming `CD/SPU`. `FUN_800655c8` la lance en ecrivant `DAT_8009aa94 = 1` et `g_fileLoadFlags |= 0x20`; `FUN_800630e4` en est l'updater principal via `CdSync/CdRead/CdReadSync/SpuStTransfer`; `FUN_80065798` ne fait que poser le bit `0x40` pour la branche alternative consommee en etats `9/0x10`; `FUN_80064154` force `0x8B`, que `FUN_800630e4` normalise ensuite vers le cleanup `0x0B`; enfin `FUN_8006571c` et `FUN_80065948` sont deux sondes inverses de disponibilite autour de cette meme machine.
- Le writer runtime le plus direct de `g_animSharedVarTable` sur ce sous-chemin est maintenant borne lui aussi: `AnimCmd_FUN_0x32` est le seul appelant prouve de `FUN_800657b0`, qui pilote un sous-automate local `battleChars[0].field_0x14 = 0 -> 1 -> 2`, choisit une valeur dans `DAT_800921d8[tag].word[0..2]`, delegue l'execution a `FUN_800655c8`, puis, dans sa forme `mode 0x80`, OR le mot suivant dans `g_animSharedVarTable[cmd.high4]` une fois la sequence terminee.
- Le contexte d'appel du streamer audio est maintenant ferme lui aussi: `FUN_80062834` sert de wrapper `init/update` via `entityNode.y.high16`, `FUN_80062894` initialise le sous-systeme et cherche `CR.B` dans `runtimePointers.polyGt3Index`, et `FUN_800660f4` draine proprement le streamer en rappelant `FUN_800630e4` tant que `g_fileLoadFlags & 0x20` reste actif. Les champs voisins ont maintenant un role minimal prouve: `field_0x128` sert d'index de requete pour calculer l'offset dans `CR.B`, `field_0x12e` est un compteur restant consomme par tranches, `runtimePointers.dataPtr1` est la position `CdlLOC` courante, et `runtimePointers.dataPtr10` est une base alternative derivee localement de `dataPtr1 + 2 secteurs`.
- `g_animSharedVarTable` n'est plus une boite noire de branchement: la passe locale des XREFs directs est maintenant classée. Les writers prouvés comprennent `AnimCmd_ConditionalBranch`, `AnimCmd_SetCharRenderState`, `AnimCmd_VoiceCall`, `AnimCmd_FUN_0x32`, `AnimCmd_ChDanSet`, `AnimCmd_AttSet`, `AnimCmd_HitzSet`, `AnimCmd_MoveSet`, `AnimCmd_BitSet`, `AnimCmd_ObjIntGet`, `AnimCmd_ObjLongGet`, ainsi que `ApplyMathOp(mode 0x0A)`. Les autres handlers XREFés dans cette zone (`AnimCmd_IfSet`, `AnimCmd_ChEffSet`, `AnimCmd_AnimateVertexColors`, `AnimCmd_SetBodyPartTransforms*`, `AnimCmd_SetMeshPaletteRange`, `AnimCmd_AddPrimsToOT`, `AnimCmd_ApplyCharEffect`, `AnimCmd_XAddSet`, `AnimCmd_XMaxSet`, `AnimCmd_Rgb2Set`, `AnimCmd_BaseCul*`, `AnimCmd_MovexpSet`, `AnimCmd_Xy0123Set`, `AnimCmd_Uv0123Set`, `AnimCmd_OtZSet`, `AnimCmd_EffSet`, `AnimCmd_AutoOtz`, `AnimCmd_AutoRgb`, `AnimCmd_ChseCall`, `AnimCmd_ChseVol`, `AnimCmd_AtseCall`, `AnimCmd_BitChk`) sont des lecteurs seuls d'opérandes. Ce qui reste ouvert n'est donc plus la grammaire locale, mais quelle combinaison runtime de writers pose effectivement les masques observés (`0x4000`, `0x2000`, `0x1000`, `0x0800`, `0x0400`) dans chaque combat.
- Les writers `OR` restants n'introduisent pas de table de masques cachée dans l'EXE: `ConditionalBranch`, `VoiceCall`, `FUN_0x32`, `AttSet`, `HitzSet`, `MoveSet` et `ChDanSet` partent tous d'un mot immédiat du flux, et `SetCharRenderState` OR plus tard un masque simplement relatché depuis `streamPtr[3]`. L'incertitude restante sur `0x4000/0x2000/0x1000/0x0800/0x0400` bascule donc des XREFs code vers les données de streams CH_BIN effectivement jouées.
- Le travail restant sur les branches runtime est maintenant borne a quelques gabarits statiques: les `22` streams prefixes `low16=0` qui ouvrent par `if_set` testent tous `g_animSharedVarTable[0]` avec `mask=0x4000`, puis souvent `0x2000` sous un tag imbrique `0x123`; les variantes plus tardives vues dans ce sous-corpus ne portent plus que sur `0x1000`, `0x0800` ou `0x0400`. Ce qui manque est le choix runtime de branche, pas la grammaire du stream.
- Cote corpus statique, les ecritures associees a ces gabarits sont elles aussi bornees: dans les streams prefixes `low16=0` deja recales, elles passent surtout par `bit_set` sur `var 0` avec `0x9FFF`, `0x2000`, `0x4000`, et sur `var 4` avec `0x0002`, `0x0003`, `0x0008`, `0x0010`, `0x0020` selon le fichier. Le residu n'est donc plus l'origine statique des masques, mais l'etat runtime exact qui fait tomber sur telle ou telle branche.
- Les `12` cas non-prefixe `unknown_0x08.low16 = 0` et `primitive_count_packed = 0x00000001` gardent `entry_id_packed.low16 = 0xXX00` et sont suivis immediatement d'une entree `low16 = 1` dont `entry_id_packed.low16` reste dans la meme famille `0xXX**`.
- Sur le meme sous-corpus, les cas non-prefixe `low16=0` avec `primitive_count_packed = 1` et `ptr_anim_stream != 0` ouvrent eux aussi uniquement par `0x06` ou `0x2F`.
- Le meme parseur lineaire couvre aussi les `7` cas non-prefixe `low16=0`, `primitive_count_packed = 1`, `ptr_anim_stream != 0` sans rencontrer `0x02 table_set` ni `0x0A pri_set`; en ajoutant le format deja borne de `0x13 rgb2_set` (`4-5` mots selon `word2.bit15`), les `7/7` atteignent directement le terminator.
- `unknown_0x08.high16` recopie `primitive_count_packed.high16` sur `790/792` entrees du corpus courant; seuls `CH_09.BIN E6/E7` gardent `u08.high16 = 1` alors que `prim.high16 = 3`.
- La classe dominante de `unknown_0x08` reste `0x00010001`.
- La signature de segments `vertex.counts=0x00010040`, `mesh.counts=0x00010001`, `lighting.counts=0x00010010` n'apparait actuellement que sur `CH_26.BIN E12/E13`, c'est-a-dire exactement les deux cas `unknown_0x08.low16 = 2`.
- Parmi les couples adjacents animes `prim=0x00010010` (deux streams non nuls), il n'existe actuellement que `CH_04.BIN E4/E5` et `CH_26.BIN E12/E13`. Le couple ordinaire `CH_04.BIN E4/E5` utilise la signature `vertex.counts=0x00400001`, `mesh.counts=0x00100001`, `lighting.counts=0x00010010`, alors que le couple `low16=2` `CH_26.BIN E12/E13` utilise la signature compacte `vertex.counts=0x00010040`, `mesh.counts=0x00010001`, `lighting.counts=0x00010010`.
- `CH_09.BIN E6/E7` ne montrent pas un nouveau layout de segments : ils reutilisent les memes listes `mesh/light` que `CH_09.BIN E5`, avec seulement un decalage d'un segment dans la liste vertex; `E6` garde un stream non nul, `E7` non.
- `CH_09.BIN E7` est un miroir immediat de `E6` (meme geometrie, meme `primitive_count_packed`, meme `unknown_0x08`, seul `ptr_anim_stream` passe de non nul a nul). Le motif plus general "meme geometrie/metadata, stream non nul -> nul" apparait `20` fois dans le corpus; `E7` n'etablit donc pas a lui seul une nouvelle sous-classe structurelle.
- `CH_31.BIN E4` reutilise aussi un suffixe de `CH_31.BIN E3` : les pointeurs `vertex/mesh/light` avancent chacun d'un segment. Donc `mesh.counts_packed = 0x00120001` n'est pas unique a `CH_31.BIN E4`; il apparait deja comme deuxieme segment mesh de `CH_31.BIN E3`.
- Dans le motif adjacent courant `vertex+8, mesh+16, light+8`, `CH_31.BIN E4` est aussi le seul cas actuellement observe ou la destination passe a `unknown_0x08 = 0x00030001`; les `92` autres destinations de ce motif gardent `0x00010001`.
- A ce stade, les cas `prim.high16 = 3` bornent des vues speciales sur des listes deja existantes, pas un nouveau format de segment prouve.
- Bornage final structurel actuel : `primitive_count_packed.low16` suffit au pipeline d'overlay prouve; `primitive_count_packed.high16` reste une metadonnee opaque d'entree, sans lecteur prouve ni nouveau format de segment associe dans les chemins de combat relies.
- Le motif adjacent `vertex+0, mesh+0, light+0` apparait `261` fois; seule la transition `CH_09 E6->E7` y aboutit a une destination `prim.high16 > 1`.
- Le suffix-reuse lui-meme est courant dans le corpus : le motif `vertex+8, mesh+0, light+0` apparait `21` fois et le motif `vertex+8, mesh+16, light+8` apparait `93` fois. Parmi ces `114` paires adjacentes, seuls `CH_09.BIN E5->E6` et `CH_31.BIN E3->E4` aboutissent a `prim.high16 > 1`.

## 5. Listes de segments

```c
typedef struct CHBinVertexSegmentEntry {
    uint ptr_vertices;    // +0x00 CERTAIN
    uint counts_packed;   // +0x04 CERTAIN: high16=countX, low16=countY
} CHBinVertexSegmentEntry; // 0x08

typedef struct CHBinMeshSegmentEntry {
    uint ptr_primitive_indices; // +0x00 CERTAIN
    uint ptr_uv_table;          // +0x04 CERTAIN
    uint ptr_color_table;       // +0x08 CERTAIN
    uint counts_packed;         // +0x0C CERTAIN: high16=countX, low16=countY
} CHBinMeshSegmentEntry; // 0x10

typedef struct CHBinLightingSegmentEntry {
    uint ptr_lighting_values; // +0x00 CERTAIN
    uint counts_packed;       // +0x04 CERTAIN: high16=countX, low16=countY
} CHBinLightingSegmentEntry; // 0x08
```

### 5.1 Sens minimal de `counts_packed`

| Structure | Lecteur iterateur | Propriete prouvee |
|-----------|-------------------|-------------------|
| `CHBinVertexSegmentEntry` | `IterateMeshStreamAndFetch` | `high16 = countX`, `low16 = countY` |
| `CHBinMeshSegmentEntry` | `IterateMeshStreamAndFetch_Offset16` | `high16 = countX`, `low16 = countY` |
| `CHBinLightingSegmentEntry` | `IterateMeshStreamAndFetch_Offset8` | `high16 = countX`, `low16 = countY` |

Lecture minimale :

- `countX` pilote le compteur interne de la ligne/bloc courant.
- `countY` pilote le nombre de lignes/blocs restants avant passage au segment suivant.
- La semantique geometrique exacte de `countX/countY` selon le payload reste partiellement ouverte, mais leur role de double compteur est prouve.

## 6. Framing `AnimStream`

```c
typedef struct AnimStreamBatchFraming {
    uint16_t zero_0x00;            // observe a 0x0000 sur 223/223 streams non nuls vus
    uint16_t initial_countdown;    // copie dans g_meshOffsetBuffer
    uint16_t batch_words[];        // commandes jusqu'au terminator 0x0000
    // repetition implicite:
    // 0x0000, next_countdown, next_batch_words...
} AnimStreamBatchFraming;
```

### 6.1 Proprietes prouvees

| Element | Propriete minimale |
|---------|--------------------|
| `streamBase + 0x00` | `0x0000` constant sur tout le corpus non nul observe |
| `streamBase + 0x02` | countdown initial du batch |
| `streamBase + 0x04` | debut effectif de l'execution |
| `0x0000` dans le flux | terminator de batch |
| mot apres terminator | prochain countdown |
| deux mots apres terminator | debut du batch suivant |

### 6.2 Consequences runtime

- `RenderBattleScene3D` et `AnimCmd_RenderEntryGroup` initialisent `g_meshStreamPtrBuffer = ptr_anim_stream + 4`.
- `RenderBattleScene3D` et `AnimCmd_RenderEntryGroup` initialisent `g_meshOffsetBuffer = *(u16 *)(ptr_anim_stream + 2)`.
- `ExecuteAnimStreamBatch` execute jusqu'au `0x0000`, decremente `g_meshOffsetBuffer`, puis recharge le countdown du batch suivant quand le compteur atteint `0`.
- Certains streams commencent donc par un batch vide intentionnel.

### 6.3 Format utile de `AnimCmd_RenderEntryGroup` (`table_set`)

Commande de selection/overlay de groupe, opcode `0x02`.

| Mot | Sens minimal | Statut |
|-----|--------------|--------|
| `word0.low8` | opcode `0x02` | CERTAIN |
| `word0.high8` | recopie dans l'octet haut de `g_renderMetadataBuffer` pendant l'overlay | CERTAIN |
| taille minimale | le handler lit `streamPtr[1]` puis retourne `streamPtr + 2` | CERTAIN |
| `word1.high8` | selecteur de source relu depuis `streamPtr[1]` et reinjecte dans les lectures du header runtime | CERTAIN |
| `word1.high8` observe | `2..5` sur les cas relies (`2` = table principale, `3/4/5` = autres slots header) | CERTAIN |
| `word1.low8.bit0` | efface les buffers de rendu temporaires avant overlay | CERTAIN |
| `word1.low8.bit1` | choisit dynamiquement le slot cible en scannant `g_meshStreamPtrBuffer` avant d'y installer le stream du groupe | CERTAIN |
| `word1.low8.bit2` | arme `g_renderFlushFlag` | CERTAIN |

### 6.4 Consequence structurelle pour les entrees `low16=0`

- La piste "entree `low16=0` = pilote de sous-bloc" reste PROBABLE, pas CERTAINE.
- En runtime prouve sur `CH_10.BIN`, les trois entrees prefixe `low16=0` installent bien des streams actifs tout en gardant `g_meshCountBuffer=0`.
- Le scan disque `CH_BIN1/CH_BIN2` borne en plus leur ouverture de stream: aucun prefixe `low16=0` observe ne commence par `table_set` (`0x02`) ni `pri_set` (`0x0A`); les opcodes d'ouverture observes restent `0x06`, `0x07`, `0x0B`, `0x19`, `0x23`, `0x2F`.
- Ce bornage reste descriptif, pas discriminant a lui seul: sur les `223` streams non nuls actuellement observes dans le corpus `CH/IN`, aucun n'ouvre par `0x02` ni `0x0A`; cela ne prouve donc pas l'absence d'un `table_set` plus loin dans le stream.
- En revanche, dans ce combat reel, aucune casse n'a ete observee sur `AnimCmd_RenderEntryGroup` pendant plusieurs secondes; il n'est donc pas encore prouve que ces streams prefixe pilotent eux-memes des `table_set` vers les sous-blocs `0xXX**`.

## 7. Format exact de `AnimCmd_LoadTexture`

Commande sync `load_set`, taille fixe `7` mots.

| Mot | Sens minimal | Statut |
|-----|--------------|--------|
| `word0.low8` | opcode `0x03` | CERTAIN |
| `word0.high8.bit0 = 0` | chemin raw direct vers `LoadImage_ReturnTPageOrClutId(..., isClut=1)` | CERTAIN |
| `word0.high8.bit0 = 1` | chemin decompresse `DecompressLZSS` puis `LoadImage_ReturnTPageOrClutId(..., isClut=0)` | CERTAIN |
| `word1` | `x` | CERTAIN |
| `word2` | `y` | CERTAIN |
| `word3` | `w` | CERTAIN |
| `word4` | `h` | CERTAIN |
| `word5` | index de banque `tbl` (`3/4/5`) | CERTAIN |
| `word6` | zero-fill sur tous les `load_set` valides actuellement prouves; les `2` anciens outliers etaient des faux positifs d'alignement `AnimStream` | PROBABLE |

### 7.1 Notes prouvees

- Le meme couple generique `DecompressLZSS -> LoadImage_ReturnTPageOrClutId` est utilise pour les cas decompresses quel que soit `tbl=3/4/5`.
- Sur `118` `load_set` plausibles, le byte haut de `word0` vaut seulement `0x00` ou `0x01`; aucun autre bit de controle n'est observe a `1`.
- Les deux anciens outliers de scan se resolvent en faux positifs d'alignement dans le bytecode `AnimStream`: `CH_09.BIN E0 @ +0x0304` tombe sur `word1` de `bit_set 0518 0003 0040`, et `CH_42.BIN E0 @ +0x042C` tombe sur le troisieme operande de `trans_set 1206 2108 0003 0003 0003`, juste avant deux `bit_set` consecutifs.
- Sur les `load_set` valides actuellement recales sur un vrai debut de commande, `word6` reste observe a `0x0000`.

## 8. Format exact de `AnimCmd_AsyncLoadTexture`

Commande async `tex_set`, opcode `0x0B`, taille variable.

### 8.1 `word0`

| Bits | Sens minimal | Statut |
|------|--------------|--------|
| `low8` | opcode `0x0B` | CERTAIN |
| `high8.bit7` | choisit `init` (`1`) vs `update` (`0`) | CERTAIN |
| `high8.bits0..1` | index de requete `ImageLoadRequest_ARRAY[index]` | CERTAIN |
| `high8.bits4..6` | etat de reload/countdown recopie dans `palette_flags[6:4]` | CERTAIN |
| `high8.bits2..3` | ignores par le decodeur actuel; observes a `0` sur tous les `init` plausibles actuellement prouves | CERTAIN |

### 8.2 Format `init` (`bit7 = 1`)

| Mot | Sens minimal | Statut |
|-----|--------------|--------|
| `word0` | controle + opcode `0x0B` | CERTAIN |
| `word1` | `tbl_index` | CERTAIN |
| `word2` | zero-fill observe sur tous les `init` plausibles (`26/26`) | PROBABLE |
| `word3` | `vram_x` | CERTAIN |
| `word4` | `vram_y` | CERTAIN |
| `word5` | `cycle_offset | (first_index << 8)` | CERTAIN |
| `word6` | `last_index | (flags << 8)` | CERTAIN |

### 8.3 Format `update` (`bit7 = 0`)

- La commande ne consomme qu'un seul mot : `word0`.
- Elle reutilise la requete selectionnee et ne reinitialise pas la source.

### 8.4 Proprietes prouvees

- Aucun cas plausible `tex_set tbl=5` n'est actuellement prouve dans le corpus.
- `tbl=3` domine fortement les initialisations async plausibles.
- `tbl=4` existe mais reste rare.
- Sur les `26` `tex_set init` plausibles actuellement observes, `word2 = 0` dans tous les cas et `high8.bits2..3 = 0` dans tous les cas.
- Sur ces memes `26` `tex_set init` plausibles, seuls `reqIndex = 0/1` sont observes; aucun `reqIndex = 2` n'est actuellement prouve.
- Le handler async ne lit dans le byte haut de `word0` que `bit7`, `bits0..1`, et `bits4..6`; `bits2..3` sont donc structurellement ignores sur les chemins `init` et `update` actuellement relies.

## 9. Buffers derives et lecteurs prouves

| Buffer derive | Source minimale | Lecteurs prouves | Propriete minimale |
|---------------|-----------------|------------------|--------------------|
| `g_renderMetadataBuffer` | token derive de `entry_id_packed.low16` + slot + base locale de primitives | `AnimCmd_SetMeshPaletteRange`, `AnimCmd_AddPrimsToOT`, `AnimCmd_AnimateVertexColors`, `AnimCmd_AnimatePolyColorRGBA`, `AnimCmd_PartsLink`, `AnimCmd_Rgb2Set`, `AnimCmd_BaseCulX/Y/Z/P`, `AnimCmd_Uv0123Set`, `AnimCmd_Xy0123Set`, `AnimCmd_OtZSet`, `AnimCmd_ChEffSet` | parametre aval par entree |
| `g_meshEntryFlagsHiBuf` | `entry_id_packed.high16` | `AnimCmd_XAddSet`, `AnimCmd_XMaxSet` | etat `short` par entree |
| `g_meshCountBuffer` | `primitive_count_packed.low16` | `AnimCmd_AnimateVertexColors`, `AnimCmd_AnimatePolyColorRGBA`, `AnimCmd_PartsLink`, `AnimCmd_Rgb2Set`, `AnimCmd_BaseCulX/Y/Z/P`, `AnimCmd_Uv0123Set`, `AnimCmd_Xy0123Set`, `AnimCmd_OtZSet` | cardinal aval |
| `g_meshStreamPtrBuffer` | `ptr_anim_stream + 4` | `ExecuteAnimStreamBatch`, `AnimCmd_BitChk`, `AnimCmd_EndSet` | pointeur courant du stream |
| `g_meshOffsetBuffer` | `*(u16 *)(ptr_anim_stream + 2)` | `ExecuteAnimStreamBatch`, `AnimCmd_BitChk`, `AnimCmd_EndSet` | countdown d'avancement |

### 9.1 Propagation minimale par champ `CHBinMeshEntry`

| Champ | Sortie minimale prouvee | Lecteurs prouves | Statut |
|-------|--------------------------|------------------|--------|
| `entry_id_packed.low16` | contribue au token de `g_renderMetadataBuffer` | `AnimCmd_SetMeshPaletteRange`, `AnimCmd_AddPrimsToOT`, `AnimCmd_AnimateVertexColors`, `AnimCmd_AnimatePolyColorRGBA`, `AnimCmd_PartsLink`, `AnimCmd_Rgb2Set`, `AnimCmd_BaseCulX/Y/Z/P`, `AnimCmd_Uv0123Set`, `AnimCmd_Xy0123Set`, `AnimCmd_OtZSet`, `AnimCmd_ChEffSet` | CERTAIN |
| `entry_id_packed.high16` | copie dans `g_meshEntryFlagsHiBuf` | `AnimCmd_XAddSet`, `AnimCmd_XMaxSet` | CERTAIN |
| `primitive_count_packed.low16` | copie dans `g_meshCountBuffer` | `AnimCmd_AnimateVertexColors`, `AnimCmd_AnimatePolyColorRGBA`, `AnimCmd_PartsLink`, `AnimCmd_Rgb2Set`, `AnimCmd_BaseCulX/Y/Z/P`, `AnimCmd_Uv0123Set`, `AnimCmd_Xy0123Set`, `AnimCmd_OtZSet` | CERTAIN |
| `primitive_count_packed.high16` | aucune propagation prouvee dans les overlays relies | aucun lecteur prouve hors relecture brute locale de `entry[1]` avant truncation | INCONNU |
| `unknown_0x08` | aucune propagation prouvee | aucun lecteur prouve dans `RenderBattleScene3D`, `AnimCmd_RenderEntryGroup`, ou les lecteurs des buffers derives relies | INCONNU |
| `ptr_vertex_segment_list` | consommation directe locale par l'overlay | `RenderBattleScene3D`, `AnimCmd_RenderEntryGroup` via `IterateMeshStreamAndFetch` et remplissage du buffer de vertices transformes | CERTAIN |
| `ptr_mesh_segment_list` | consommation directe locale par l'overlay | `RenderBattleScene3D`, `AnimCmd_RenderEntryGroup` via `IterateMeshStreamAndFetch_Offset16`, UVs, indices de primitives et assemblage des polys | CERTAIN |
| `ptr_lighting_segment_list` | consommation directe locale par l'overlay | `RenderBattleScene3D`, `AnimCmd_RenderEntryGroup` via `IterateMeshStreamAndFetch_Offset8` et remplissage du buffer couleur/light | CERTAIN |
| `ptr_anim_stream` | copie dans `g_meshStreamPtrBuffer` et `g_meshOffsetBuffer` | `ExecuteAnimStreamBatch`, `AnimCmd_BitChk`, `AnimCmd_EndSet` | CERTAIN |

### 9.2 Conclusion utile

- Le chemin principal de consommation des metadonnees d'entree passe par des buffers derives, pas par des relectures brutes de `CHBinMeshEntry`.
- Aucun reflet prouve de `primitive_count_packed.high16` n'apparait dans les buffers d'overlay actuellement relies.
- `unknown_0x08` reste negatif sur les chemins principaux relies de `GAME.EXE` et `VS.EXE`.
- La fermeture `champ -> buffer -> lecteur` de `CHBinMeshEntry` est maintenant complete au niveau minimal structurel.
- Au niveau structurel du format, `primitive_count_packed.high16` peut donc etre traite comme une metadonnee opaque d'entree tant qu'aucun lecteur specialise n'est prouve.

## 10. Alignement `VS.EXE`

Les homologues suivants sont deja bornes CERTAINEMENT :

| Adresse `VS.EXE` | Nom |
|------------------|-----|
| `0x800358b8` | `RenderBattleScene3D` |
| `0x80036768` | `ExecuteAnimStreamBatch` |
| `0x800373a0` | `AnimCmd_RenderEntryGroup` |
| `0x80037e30` | `AnimCmd_LoadTexture` |
| `0x80037f20` | `AnimCmd_SetCharRenderState` |
| `0x80034e10` | `DecompressLZSS` |
| `0x80061a60` | `DecompressAndLoadImage` |
| `0x80061b0c` | `LoadImage_ReturnTPageOrClutId` |

Proprietes utiles :

- Le meme framing `AnimStream` s'applique a `VS.EXE`.
- Le meme format `load_set` s'applique a `VS.EXE`.
- `unknown_0x08` n'est pas lu non plus dans les homologues `VS.EXE` de `RenderBattleScene3D` et `AnimCmd_RenderEntryGroup`.
- Les lecteurs `VS.EXE` relies de `g_renderMetadataBuffer` / `g_meshCountBuffer` sont maintenant entierement nommes : `AnimCmd_SetMeshPaletteRange`, `AnimCmd_AddPrimsToOT`, `AnimCmd_AnimateVertexColors`, `AnimCmd_AnimatePolyColorRGBA`, `AnimCmd_PartsLink`, `AnimCmd_Rgb2Set`, `AnimCmd_BaseCulX/Y/Z/P`, `AnimCmd_Uv0123Set`, `AnimCmd_Xy0123Set`, `AnimCmd_OtZSet`, `AnimCmd_ChEffSet`.
- Les familles de lecteurs de `g_renderMetadataBuffer`, `g_meshEntryFlagsHiBuf`, `g_meshCountBuffer`, `g_meshStreamPtrBuffer` et `g_meshOffsetBuffer` concordent avec `GAME.EXE`.

## 11. Statut global des preuves

### 11.1 CERTAIN

- `CHBinFileHeaderPrefix`, `CHBinMeshEntry`, `CHBinVertexSegmentEntry`, `CHBinMeshSegmentEntry`, `CHBinLightingSegmentEntry` sont suffisamment bornes pour une utilisation Ghidra fiable.
- `CH_BIN3` conserve `CHBinMeshEntry` et les listes de segments `8/16/8`; ce qui varie d'un fichier a l'autre est le nombre de slots header relocalises.
- Les slots header runtime valides sont les dwords `2 .. reloc_loop_bound-1`; `+0x10` n'est donc valide que si `reloc_loop_bound > 4`, et `+0x14` que si `reloc_loop_bound > 5`.
- `counts_packed.high16 = countX` et `counts_packed.low16 = countY` dans les trois types de segment.
- `ptr_anim_stream` pointe vers un stream cadre par batches.
- `AnimCmd_LoadTexture` a un format fixe de `7` mots.
- `AnimCmd_AsyncLoadTexture` encode `init/update`, `request index`, et `reload/countdown` dans le byte haut de `word0`.
- `AnimCmd_AsyncLoadTexture.high8.bits2..3` sont ignores par le decodeur actuellement relie.
- `unknown_0x08.high16` suit `primitive_count_packed.high16` sur `790/792` entrees, avec pour seules exceptions `CH_09.BIN E6/E7`.
- Un meme triplet local `(ptr_vertex_segment_list, ptr_mesh_segment_list, ptr_lighting_segment_list)` peut apparaitre avec plusieurs couples `(primitive_count_packed, unknown_0x08)` dans un meme fichier; ces champs ne sont donc pas des selecteurs uniques de layout de segments.
- `AnimCmd_ChEffSet` peut consommer des slots header `CH_BIN3` au-dela de `5`; `IN_08.BIN` prouve des usages jusqu'aux slots `22..26`.
- Des slots header etendus `6..9` sont aussi atteints dans `CH_BIN1/CH_BIN2` (`CH_04.BIN`, `CH_49.BIN`) via `load_set`.
- Le pipeline CH_BIN de combat de `VS.EXE` est homologue a celui de `GAME.EXE` pour les fonctions principales deja alignees.

### 11.2 PROBABLE

- `ptr_section_3` est la banque CLUT principale.
- `ptr_section_4` et `ptr_section_5`, quand ils sont valides, sont deux slots bas d'un tableau runtime plus large de banques image/texture; leur difference restante est une difference d'organisation de contenu/corpus, pas un format distinct prouve.
- Dans le corpus atteint actuel, `ptr_section_4` est la seule des deux banques basses a etre prouvee sur le chemin async, alors que `ptr_section_5` reste essentiellement sync.
- `entry_id_packed.high16` sert d'etat/bound pour les handlers X-* via `g_meshEntryFlagsHiBuf`.
- `primitive_count_packed.high16` est une metadonnee d'entree hors pipeline d'overlay prouve, plutot qu'un compteur necessaire au rendu principal.
- `primitive_count_packed.high16 = 0` marque probablement la sous-famille zero/une primitive des entrees de prefixe ou de tete locale, tandis que `high16 = 1` couvre la famille ordinaire des entrees rendues; `high16 = 3` reste un cas special tres rare.
- `unknown_0x08.low16 = 0` marque probablement une sous-famille d'entrees de controle/tete de bloc: prefixes ou entetes locaux a zero/une primitive qui installent des streams, mais n'alimentent pas directement le rendu principal dans le snapshot runtime prouve `CH_10.BIN`.
- `AnimCmd_LoadTexture.word6` est un mot reserve/non lu par le decodeur actuellement relie, et nul sur tous les `load_set` valides actuellement prouves.

### 11.3 INCONNU

- Role exact de `primitive_count_packed.high16`.
- Role exact de `unknown_0x08`.
- Partage semantique exact entre les banques basses `ptr_section_4` et `ptr_section_5` quand les deux sont presentes.
- Sens semantique exact des bits `6..7` observes dans `CHBin3ChEffBlobRecord.control_0x00`, bien qu'ils ne soient pas consommes par le decodeur actuellement relie.

## 12. Plan actif

| ID | Priorite | Statut | Tache | Critere de fin |
|----|----------|--------|-------|----------------|
| `T1` | `P0` | `DONE` | Expliquer les cas `primitive_count_packed.high16 > 1` (`CH_09 E6/E7`, `CH_31 E4`) | propriete minimale du `high16` prouvee ou bornage final explicite |
| `T2` | `P0` | `DONE` | Finir la fermeture champ -> buffer -> lecteur pour `CHBinMeshEntry` | chaque champ a sa table de propagation minimale |
| `T3` | `P1` | `DONE` | Borner structurellement les slots header bas `+0x10/+0x14` | validite runtime conditionnee par `reloc_loop_bound`, et extension eventuelle au-dela de `+0x14` prouvee |
| `T4` | `P1` | `DONE` | Cartographier les derniers handlers `AnimStream` utiles aux cas rares | chaque handler cible recoit un nom minimal et une lecture structurelle utile |
| `T5` | `P2` | `TODO` | N'utiliser l'emulateur que si la statique n'avance plus | une ambiguite semantique residuelle est tranchee runtime |
| `T6` | `P1` | `DONE` | Resserer les champs residuels des commandes texture (`load_set`, `tex_set init`) | les champs encore opaques sont bornes par code ou corpus |
| `T7` | `P0` | `DONE` | Resserer la sous-classe `unknown_0x08.low16 = 2` (`CH_26 E12/E13`) | une propriete structurelle minimale distingue cette sous-classe des cas ordinaires |
| `T8` | `P1` | `DONE` | Resserer le header et les pointeurs supplementaires des `IN_xx.BIN` (`CH_BIN3`) | une structure minimale du header et des pointeurs etendus est prouvee |
| `T9` | `P1` | `DONE` | Resserer les champs residuels du blob `CH_BIN3` consomme par `AnimCmd_ChEffSet` | le role structurel de `record.control_0x00 bits6..7` et de `group_header.reserved_0x03` est borne |

## 13. Regle de cloture

Si une nouvelle preuve n'ameliore pas directement la structure du format, elle ne doit pas etre ajoutee ici. Elle va dans [structure-ch-bin-files.history.md](structure-ch-bin-files.history.md) ou dans un document specialise.
