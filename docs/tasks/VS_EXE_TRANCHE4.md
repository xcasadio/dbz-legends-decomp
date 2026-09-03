# VS.EXE — tranche 4 : le reliquat, et ce qu'il reste à faire

État au 2026-09-03 : **inventorié et réfuté, pas commencé.** Aucun code de tranche 4 n'est écrit.
Ce document est le point de reprise. Il a été rédigé sur demande d'arrêt : reprendre ici, dans
l'ordre indiqué, sans re-mesurer ce qui est mesuré.

## Le chiffre qui corrige l'annonce précédente

J'avais annoncé « une centaine de fonctions ». C'est faux d'un facteur presque trois :

| | fonctions | octets MIPS |
|---|---|---|
| portées (corps réel) | 95 | 60 556 |
| souches BLOCKED (corps vide) | 65 | 50 092 |
| absentes du portage | 212 | 132 840 |
| **reliquat** | **277** | **182 932** |
| SDK psyq (à ne pas porter, règle 13) | 694 | — |

Liste de référence : `VS_EXE_TRANCHE4_residu.tsv` (adresse, nom, taille, état, fichier, appelants),
triée par taille. Les 15 plus grosses vont de 8 524 o (`LAB_8002c504`, écran de fin) à 1 908 o.

Méthode du recensement (à connaître pour le relire) : 367 lignes `// GHIDRA:` dans 20 fichiers,
166 adresses de fonctions ; état = corps de la méthode C# qui suit (BLOCKED seul ne fait pas la
souche). Côté image : 1 208 fonctions Ghidra dans l'image plus 120 débuts non promus découverts
dans les octets (`LAB_xxxxxxxx` = étiquette du recensement, pas de Ghidra). Atteignabilité
étendue depuis `main`/`start` avec les racines matérialisées par `lui/addiu` et tables `.data`.

Ce que la réfutation a corrigé et qu'il ne faut pas reprendre tel quel :
- les colonnes appelants/appelées du recensement sont **plafonnées** (4 / 5) ; les vrais fan-in
  sont `FUN_80052db4` 21 fonctions / 139 sites, `FUN_8004a638` 17 / 22, `FUN_80047c64` 15 ;
- 11 tailles de fonctions à frame pointer sont trop courtes de 8 à 48 o (épilogue
  `addu sp,fp,zero`) : prendre l'adresse du début suivant ;
- 12 364 o de code jamais référencé (groupe Y : `0x80058CC0`, `0x8005932C`, `0x8005CFA0`,
  `0x800462E0`, `0x80046614`, `0x80045E84`, `0x8002679C`, `0x80029038..`) — **ne pas porter** ;
- 101 357 o de la zone code ne sont dans aucune fonction Ghidra (queues de handlers AnimCmd de
  taille 1, corps de tâche atteints par pointeur) — la couverture Ghidra est incomplète, les
  octets font foi.

## Règle du projet qui change la taille du travail : les jumelles

290 des 372 fonctions de jeu de VS.EXE sont identiques modulo relocation à une fonction de
TITLE.EXE (moteur commun). **Une fonction identique déjà portée ailleurs se réutilise, ne se
réécrit pas.** Vingt-deux trouvées au passage par la réfutation, deux contre-vérifiées par moi
(0 mot différent après masquage des `jal`/`j`, des `lui` et des immédiats dérivés d'un `lui`) :

| VS.EXE | jumelle portée (corps réel) |
|---|---|
| `FUN_80052db4` 1404 o | `TITLE_EXE/SpriteRenderer.cs` `DrawSpriteGroup` @0x80048F88 — 139 sites d'appel |
| `FUN_80045cf4` 400 o | `TITLE_EXE/Camera.cs` `CalculateDistance3D` @0x8003BEC8 |
| `FUN_80045f34` 712 o | `TITLE_EXE/Camera.cs` `CalculateLookAtAngles` @0x8003C108 |
| `FUN_80047550` 312 o | `TITLE_EXE/Camera.cs` `FUN_8003d724(int, VECTOR, short, short, short)` — **règle le doublon assumé** |
| `FUN_80053840` 304 o | `TITLE_EXE/TaskSystem.cs` `DeleteTaskList` @0x80049A14 |
| `FUN_80061bd8` 372 o | `TITLE_EXE/TitleImages.cs` `LoadImageListInVram` @0x80057C80 |
| `FUN_80026a68` 88 o | `TITLE_EXE/SecondScreenSetup.cs` `FUN_80027354` |
| `LAB_80026888` 416 o | `TITLE_EXE/SecondScreenSetup.cs` `FUN_80027174` |
| `FUN_80034d98` 116 o | `TITLE_EXE/SecondScreenSetup.cs` `FUN_80035700` |
| `FUN_800411b4` 824 o | `TITLE_EXE/TITLE_EXE_exe.cs` `FUN_80037388` (205/206 mots : un `lhu` gp-relatif diffère de 4) |
| `FUN_800414ec` 536 o | `TITLE_EXE/StageBackdrop.cs` `FUN_800376c0` |
| `FUN_80021d44` … `FUN_80022870` (10 fn) | `SELECT_EXE/MemoryCard.cs`, `CardRecords.cs` — carte mémoire, 100 % des mots |

Trois jumelles existent dans TITLE mais y sont BLOCKED (`LAB_80040f78`, `LAB_80041704`,
`FUN_80027670` ~ `LAB_80027f5c`, 1650/1650) : elles ne comptent pas, mais **une seule
transliteration doit servir les deux overlays**.

### Le balayage exhaustif, rendu après la demande d'arrêt

Toutes les fonctions VS sans corps réel contre toutes les fonctions de TITLE/SELECT/MOVIE/SLPS,
masquage strict (celui de ma contre-vérification) et masquage étendu (teinte `lui` propagée à
travers `addu`, offsets `gp`-relatifs). Tables dans `VS_EXE_TRANCHE4_twins.tsv` (768 lignes) et
`VS_EXE_TRANCHE4_twins_jeu.tsv` (zone jeu seule, 310). Scripts reproductibles :
`custom-tools/scripts/twins/` — **à ranger là depuis le scratch de session avant qu'il ne
disparaisse** (`psxfn.py`, `csport.py`, `twins.py`, `verify.py`, `disasm.py`).

| zone jeu, 310 non portées | fonctions | octets |
|---|---|---|
| PORTEE_AILLEURS — jumelle avec corps réel, **à réutiliser** | 31 | 10 776 |
| JUMELLE_NON_PORTEE — jumelle dans TITLE, non portée là non plus | 260 | 153 148 |
| SANS_JUMELLE — propre à VS | 19 | 42 380 |

Ajouts à la liste des réutilisables ci-dessus : `0x80040F30` → `StageBackdrop.FUN_80037104` ;
**`0x80042054` → `TITLE_EXE/DisplayMachine.cs ControlScreenFade`** et `0x800424B0` →
`UpdateScreenFade` (la fonction que trois fichiers portaient en souche vide, et dont le type de
retour a été tranché sur les octets, existe déjà en corps réel dans TITLE — 19 mots `gp`
diffèrent, 0 en masquage étendu, 15/15 appelés cohérents). Réserve sur la carte mémoire : quatre
des dix-sept fonctions ont des appelés qui diffèrent (`_card_clear`, `FUN_800229a4`) — le port
SELECT n'y est pas un remplacement direct, à vérifier site par site.

**La conséquence structurelle** : 153 Ko du reliquat ont une jumelle exacte dans TITLE.EXE, elle
aussi non portée. Chacune de ces fonctions doit être transliterée **une fois pour les deux
overlays**, sinon la tranche 4 fabrique 260 doublons de plus. Cela impose un emplacement partagé
(le moteur commun) avant d'écrire la vague 1 — **décision d'architecture, à prendre avec
l'utilisateur.**

**Règle déjà violée dans le livré : 22 fonctions portées deux fois** (VS_EXE et TITLE_EXE,
aucune référence croisée) : `TaskSystem.CreateTask / DeleteTask / ExecuteTaskList` ;
`PrimitivePools.ResetPrimitivePoolCursors / CreatePrimitivePools / AllocatePrimitivePool /
FreePrimitivePool / InitializePrimitivePool / InitializePolyFt4` ; `FileIo.ReadFile / ReadCDData
/ WaitSearchFile / DecompressLZSS / ClearVram / SetupGeometry / DecompressAndLoadImage /
LoadImage_ReturnTPageOrClutId` ; `FighterSetup.FUN_8003478c / FUN_800511a8 / FUN_800512cc` ;
`PadInput.ProcessPadInput` ; `Heap.ShutdownAndLoadExecutable` — plus `start` (↔ SELECT) et
`LoadExec`. Ce sont les tranches 0 et 2. Résorber = même décision d'emplacement que ci-dessus.

Sans jumelle, propre à VS : les cinq blocs des écrans de fin (`0x80029200`, `0x80029A98`,
`0x8002B1B8`, `0x8002C504`, `0x8002ECC0`, non découpés par Ghidra — nombre réel de fonctions
INCONNU), `FUN_8005a5b0` (8 500 o), `FUN_800594b4`, `FUN_8005a104`, les trois écrans de
démarrage de `main`, et le module son (25 fonctions : ce n'est pas la même version que TITLE).

## Le socle à écrire AVANT tout fan-out

C'est ce qui a manqué en tranche 1 (12 symboles dupliqués, 7 types divergents) et marché en
tranche 2 (`BattleState.cs` d'abord). 228 symboles sont partagés par plus d'un groupe. Le socle
se compose de :

1. **`BattleState.cs`, à compléter.** Manquent : les 12 enregistrements HUD du contexte —
   `CtxHudRecords = 0x20`, pas `0x1C0`, 12 entrées, `0x20 + 12·0x1C0 = 0x1520` (preuves :
   `FUN_800594b4` `local_38 = ctx+0x20 += 0xE0 shorts` ; `FUN_80058338` `param_1+slot*0x1C0+0x20` ;
   `FUN_8005a5b0` `addiu a0,a0,0x1c0` @0x8005B9DC) ; les champs de créneau `+0x15B2` (clamp
   0..0x640), `+0x15B6` (0..20000), `+0x15BA` (0..99), `+0x15BC`, `+0x15C2` ; `ctx+0x1550`
   (triplets de position, **pas à fermer : 6 ou 8**), `+0x16A0`, `+0x2C14` (masques
   0xF9F7/0xFBEF), `+0x2D60/64/66` (mot de manche), `+0x2DC4`, `+0x2F88..0x2FA6`,
   `+0x3024..0x3030` (et corriger « dernier champ » : c'est `0x3030`, pas `0x302C`). Côté
   combattant : `+0x0/+0x4/+0x6/+0x8` (enregistrement de script : base, compteur de frame,
   drapeau fin, curseur), `+0x84/+0x8C/+0x94/+0x98/+0xAA`, `+0xC8..0xCC`, `+0xDC`, `+0xF8`,
   `+0x116/+0x118/+0x11C/+0x11E` (position Y/Z, rotation, angle Y), `+0x134/+0x138` (mots de
   drapeaux, bits documentés dans le rapport), `+0x144/+0x148`, `+0x150..0x15E`, `+0x16A/+0x16B`,
   `+0x1D0`, `+0x220..0x233`. Le workspace de scène : `scn+0x76`.
2. **`SoundState.cs`, nouveau** : déclarer UNE fois `DAT_8008d284` (pointeur du workspace son de
   0x194 o, tâche id 0x57 liste 0x14) et ses offsets `+0xD8/DC/F0/110..128/12A/12C/13C/142/143/
   148/154/158/15A`, `DAT_8008d280`, `DAT_8008d338` (SpuStEnv*), `DAT_8008d384` (retirer la copie
   privée d'`AnimCmdSound.cs`), `DAT_8008d340` (retirer celle de `BattleScene.cs`), `DAT_8008d210`,
   `DAT_800b0ddc/de0/df8`, et **un stockage pour `0x801C1000`** (tampon RAM du stream CD,
   0x1E00 o au moins : curseur comparé à `0x801C2E00`), aujourd'hui sans aucune déclaration.
3. **Le scratchpad GTE : ne pas créer de `GteScratch.cs` pour VS.** `TITLE_EXE/GteScratch.cs`
   déclare déjà les 124 symboles `0x1F8000xx` que VS utilise, et `VS_EXE/FileIo.cs` en redéclare
   22 (`DAT_1f800084/86/88`, `_DAT_1f8000b4/bc/c0`, `DAT_1f8000b8..e0`, `DAT_1f800110..124`),
   plus `AnimCmdTransform.cs` en `const int` (**type divergent** du `short` de TITLE et de
   FileIo). Le scratchpad est un seul kilo-octet physique, partagé par tous les overlays comme
   `SharedHighRam` : le déplacer à la racine (`DbzLegendsRemaster.GteScratch`, sur le modèle de
   `SharedHighRam.cs`) et faire pointer VS et TITLE dessus. **Décision d'emplacement à
   confirmer avec l'utilisateur** — c'est un déplacement, pas une duplication.
4. **Les 12 doublons intra-VS que le premier balayage ne voyait pas** (il ignorait les `const`) :
   `DAT_1f800084`, `DAT_1f80012c` (trois formes : const, scalaire, et `uint` chez TITLE),
   `DAT_800990c0/cc`, `DAT_801d2004`, `DAT_801f4180/5180`, `DAT_801faaac`, `g_cdFileBufferTable`
   (**`int const` contre `byte[]`**), `g_cdFileBufferTableAddress` (×3), `g_meshEntryFlagsHiBuf`,
   `g_renderFlushFlag`. Un propriétaire par symbole : celui qui l'écrit.
5. **`0x80101BA4`** : table de shorts dans le bloc de 16 Ko partagé en haut de l'image, lue par
   `FUN_80045998` (A) avec un `a2` posé par `FUN_80027340` (G) et `FUN_80047688` (J). Lecture
   seule ; désormais servie par `PsxExeImage`, mais à nommer.
6. **L'API de frontière** : les signatures déduites des sites d'appel, pour que chaque groupe
   appelle les autres sans les lire. Liste complète dans le rapport de groupement (section
   « ordre conseillé », point d) ; les plus appelées : `FUN_8005a5b0(ctx)`, `FUN_8005c6e4(ctx)`,
   `FUN_80057a7c(ctx+0x20, short slot)`, `FUN_80058338(ctx, short slot)`,
   `FUN_800539d0(record*)`, `FUN_8004a638(ftr, a1)`, `FUN_80047c64(ftr, a1)`,
   `FUN_80045998(table, ftr+0xF8, a2)`, `FUN_8004e758(ftr, int)`, `FUN_80049e30(ftr, a1)->v0`.
7. **Les rappels de tâche non portés**, à nommer une seule fois et stocker en adresses PSX brutes
   comme `BattleState.FighterEntry` : `0x8003FC88`, `0x800429A8`, `0x80029200`, `0x800436D0`,
   `0x80026888`, `0x80040F78`, `0x80041704`, `0x80041A1C`, `0x8005E88C/E954/EAE8`.

## Les groupes et les vagues

26 groupes (lettres du rapport), toutes tailles **avant** retrait des jumelles :

| vague | groupes | remarque |
|---|---|---|
| 1 — feuilles | Z_DrawHelpers 7,1 Ko · B_FighterHelpers 3,5 · C_SoundApi 3,6 · D_SoundTaskCd 10,4 (contient `FUN_8005f704`, la machine à pas du chargeur qui bloque la phase 1 de la scène) · F_ManagerHud 4,7 · H_ManagerInit 5,1 · M_FighterCmdExec 9,9 · V_FadeAndListZero 2,9 | Z perd 1 404 o (jumelle) ; V perd 824 |
| 2 | K_FighterRender (→B) · P_AttackZones (→A) · A_GeomHelpers (→B,Z,S) · S_EffectScriptVm 10,4 (→A,B,C,P,R ; **surface cachée** : table de 26 handlers à `0x80083C10..0x80083C74`, dispatch par `jalr`) · U_BootScreens 6,5 (→Z ; perd 1 528) · I_CameraTask 6,6 (`LAB_80027670`, la tâche caméra) · G_RoundReload 4,0 | A perd 1 424 |
| 3 | E_ManagerCore 9,8 (**`FUN_8005a5b0` va avec `FUN_8005c6e4` et rien d'autre** : mêmes trois appelants, même `a0 = ctx`) · L_FighterAi 11,2 (`FUN_80023890`, décision IA, 5 096 o) · O_FighterStep96 7,9 · N_FighterActions 8,2 · Q_EffectObjects 3,7 (surface cachée : rappel `0x8003FC88`) · R_ChDanObjects 4,5 (surface cachée : rappel `0x800429A8`) | |
| 4 | J_FighterPhases 5,0 (reconnecte `LAB_80050ae4`), puis **la passe de couture** | |
| hors périmètre, après | X1_ResultDispatch 5,4 (bloque `FUN_800290d0`) · X5_ResultUiLib 7,5 · X2 10,6 · X3 10,2 · X4 11,2 — les écrans de fin de combat, 44,9 Ko | X1 perd 304 |

Dépendances par **pointeur** que le graphe `jal` ne voyait pas (réfutation) : V→X1, X1→X2/X3/X4,
X3→X4 ; V n'est donc pas une feuille pure. Cycles S↔R et A↔S : se résolvent par signatures dans
le socle, pas par lecture croisée. `FUN_8003f6c0` (A, 724 o) est bloquée par le SDK :
`RotAverage3` manque à `PsxSdkMonogame.LibGte` — décision d'architecture, hors portage.

Frontière du groupe S à corriger : remplacer `GAP_80026738` (100 o, boucle morte) par
`LAB_80026784` (260 o, handler de la table `0x80083C6C`) ; recaler `GAP_80055780` sur
`0x8005574C` et `GAP_8005eb18` sur `0x8005EB14`.

## La passe de couture, à chaque vague

Ce qui a coûté trois défauts en tranche 2 et un P1 en tranche 3. Avant tout commit :
`check_task_registration.py`, `check_overlay_handover.py`, `check_vs_dispatch.py`, le balayage
des doublons **avec les `const`** (`scratchpad/dupes2.py` à ranger dans `custom-tools/scripts/`),
les onze bancs, `--diag-select 400` = 49396. Et un témoin négatif pour tout nouveau banc.

## Ce qui n'est pas de la tranche 4 mais attend

- **Pousser le sous-module** `custom-tools/PsxSdkMonogame` (`origin/main` = `4cd2548`, local à
  `35f3b78`, deux commits devant), **avant** le superprojet (dix-neuf commits devant). Un clone
  frais ne construit pas tant que ce n'est pas fait. Action de l'utilisateur.
- **Doublons inter-overlay déjà livrés** (tranche 0) : `VS_EXE/TaskSystem.cs`, `PadInput.cs`,
  `PrimitivePools.cs` redéclarent `g_TaskListHead/Tail/Count`, `g_CurrentTask`,
  `g_PadButtonMaskTable`, `g_PrimitiveSizeTable`… que `TITLE_EXE` porte déjà, pour un moteur
  identique modulo relocation. Pas un fork à l'exécution (jamais vivants ensemble), mais contraire
  à la règle de non-duplication. À décider avec l'utilisateur, pas en silence.
- Différés de tranche 3 : un clone sans `data/` échoue en MSB3030 (préexistant) ; la fenêtre
  `0x800990C0` entre l'image et la région paresseuse de `BattleScene` (valeurs identiques, nommée).
- Plus ancien : les cinq renommages de descripteurs CD (`CdlFILE_80055ba0`…), `g_SoundIsMono`
  @0x801FF01E et `g_UnlockTier` @0x801FF002 (passe `0x801FFxxx` inter-overlay), les doubles
  étiquettes `0x800206A8`/`0x800206CC` (Ghidra GUI seulement), le `.rep` Ghidra dans `git status`.
