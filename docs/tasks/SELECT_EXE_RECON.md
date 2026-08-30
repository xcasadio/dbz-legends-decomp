# SELECT.EXE - reconnaissance

## Le renversement

L'hypothese de depart etait que SELECT.EXE serait TITLE.EXE recompile: meme ordonnanceur de
taches, memes pools de primitives, meme boucle de frame, donc un portage largement acquis.

**C'est faux. SELECT.EXE est un autre moteur.**

| | TITLE.EXE | SELECT.EXE |
|---|---|---|
| couche graphique | libgpu direct: `DrawOTag`, `PutDrawEnv`, table de `0x800` entrees | **libgs**: `GsInitGraph`, `GsSortSprite`, `GsDrawOt`, `GsSwapDispBuff` |
| appels libgs | **zero** - `GsInitGraph` n'existe meme pas comme symbole | douze, rien que dans le pas de frame |
| ordonnanceur | `CreateTask` / `DeleteTask` / `ExecuteTaskList`, 21 listes | **aucun** |
| allocation | `InitHeap(0x00010000, 0x10000)` deux fois, `malloc` partout | `InitHeap` une seule fois depuis crt0, **jamais utilise**; pas de `malloc` du tout |
| structure des ecrans | callbacks de taches balayes par une boucle de frame | fonctions C bloquantes, chacune possedant son propre `do/while` |
| entree | libetc `PadRead` | **pad BIOS**: `InitPAD`, `StartPAD`, lecture directe du tampon |

L'absence d'ordonnanceur est prouvee trois fois: pas un seul chargement de pointeur de fonction
dans toute la zone de code jeu `0x800213A8-0x800347C3`, aucun `malloc` lie, et un `main` qui
distribue par un `switch` C ordinaire sur quatre fonctions codees en dur.

Il n'y a pas non plus de boucle de frame, seulement un **pas** de frame:
`FUN_800344A4 @ 0x800344A4`, 648 octets, **61 appelants**.

## Ce que cet overlay est reellement

Pas seulement la selection de personnages. C'est le **menu principal / choix de mode**:

- un menu de 3 ou 4 modes, le 4e conditionne par `DAT_801FF018 & 2`
- `FUN_80031E98 @ 0x80031E98`, 6040 octets: une vraie selection **3 contre 3**, six emplacements
  sur 35 portraits 48x48 en 4 bits, lisant la manette 1 et la manette 2 dans la meme frame, et
  exportant six identifiants vers `DAT_801FF102..10C`
- deux selecteurs d'emplacement de sauvegarde, un ecran d'options, un test sonore derriere
  `DAT_801FF018 & 4`, et un flux complet de creation / chargement / sauvegarde de carte

**Ses trois seules sorties** sont `cdrom:\DEMO.EXE;1`, `cdrom:\VS.EXE;1` et `cdrom:\SP.EXE;1`,
par `FUN_8003472C`. **Aucun chemin ne revient vers TITLE.EXE.**

Detail a garder: le `case -1` de `main` est **inatteignable** - `DAT_80055A0C` est borne a
`[0, nbItems-1]` et n'est touche que dans `FUN_800283A0`. Les vraies sorties sont les trois
`LoadExec`.

L'equivalent du `DAT_800835b4` de TITLE.EXE est `DAT_80055B50`, un identifiant d'ecran sur 16 bits
valant `0xFFFF` au boot.

## Le recouvrement, mesure

Le moteur de diff de Ghidra rapporte 679 fonctions appariees sur 1251 / 956. Ce chiffre est
trompeur, et il fallait le verifier.

**Sur les 30 routines deja nommees dans TITLE.EXE, quatre seulement correspondent:**
`ClearVram`, `DecompressLzss`, `InitializeMemoryCard`, `ShutdownMemoryCard`.

Tout le reste - `CreateTask`, `RunFrameLoop`, `ProcessPadInput`, `DrawSpriteGroup`, les quatre
routines de pool, `ControlScreenFade`... - revient **non apparie**.

### Pourquoi le balisage n'a PAS ete transfere

`diff-transfer-markup` aurait ete l'accelerateur evident. Il aurait ete un poison.

Les correlateurs disponibles sont **exact-hash et exact-nom uniquement**, sans correlateur de
similarite approchee. La colonne de similarite affiche donc `1.0` partout et **ne porte aucune
information**. Deux verifications manuelles l'ont montre:

- `main` s'apparie a `1.0` **par le nom seul**, alors que les deux fonctions n'ont rien a voir.
- Trois stubs vides de 8 octets ont ete apparies par `Duplicate Function Instructions Match` sur
  des fonctions de jeu de TITLE.EXE sans rapport.

Et la reciproque est vraie aussi: `ShutdownAndLoadExecutable` est rapporte **non apparie**, alors
que `FUN_8003472C` de SELECT.EXE est demontrablement la meme source avec trois appels ajoutes et
deux reordonnes. « Non apparie » signifie « pas identique a l'instruction pres », pas « code neuf ».

Le residu ne peut donc pas se lire comme surface neuve sans ouvrir les corps.

### La repartition reelle du `.text`

187 276 octets:

| part | poids |
|---|---:|
| code jeu | 42,1 % |
| libsnd + libspu | 34,1 % |
| libgs + libgpu | 9,6 % |
| libcd | 7,6 % |
| libapi + flottant logiciel | 3,0 % |
| libetc | 1,9 % |
| libgte | 1,5 % |

124 fonctions ne sont appariees a rien, soit 75 644 octets: 57 fonctions et 66 196 octets de code
jeu, plus 66 fonctions et 9 416 octets de bibliotheque Sony que TITLE.EXE ne liait pas.

## Trois blocages durs sur le chemin de demarrage

Contrairement a TITLE.EXE, dont le chemin principal ne touchait aucun sous-systeme absent, celui de
SELECT.EXE en heurte trois.

| blocage | effet |
|---|---|
| `LibApi.TestEvent` rend 0 sans condition | `FUN_800221D0` boucle **sans fin** au boot |
| `LibCd.CdControlB` n'accepte que la commande `0x09` | boucles de retry sur `0x0E` et `0x0A` |
| **libgs entierement absent du SDK C#** | c'est le seul endroit ou l'overlay dessine |

Le pad BIOS s'y ajoute comme blocage fonctionnel non bloquant: `InitPAD`, `StartPAD` et
`ChangeClearPAD` sont des no-ops, et le jeu lit directement le tampon a `DAT_80055D6C`.

Deux nuances utiles, contre l'attente:

- **libspu n'est pas uniformement absent.** `SpuSetCommonAttr` est reellement implemente, et c'est
  le **seul** point d'entree libspu que le chemin de boot atteint, via trois enveloppes libsnd
  d'une dizaine de lignes. Le moteur libsnd lourd et le streaming `SpuSt` sont confines a l'etat 3
  du menu.
- **La musique de cet ecran est du CD-DA**, pilote par `CdControl(CdlPlay)` et `CdlPause` a chaque
  frame depuis `FUN_80025788`. « L'ecran de selection avec le son » demande du CD-DA dans libcd,
  pas libsnd.

- **La carte memoire n'est pas optionnelle ici**: elle est sur le chemin de boot, et les `0x80`
  octets lus depuis `bu00:BISLPS-00355DRAGON` atterrissent dans `DAT_801FF018`, que `main` teste
  pour activer ou desactiver une entree de menu. Sa couche fichier est deja reelle via `LibMcrd`;
  seule la poignee de main par evenements bloque.

Ni libpress ni le MDEC ne sont lies - zero reference croisee vers `MDEC_REG0/REG1` et vers les deux
CHCR de DMA MDEC, methode validee contre `GPU_REG0` qui en compte 17.

## Les donnees

**Six fichiers CD**, exactement six sites `CdSearchFile` dans tout le programme:
`\SUB\USAGI.B;1` et `\SOUND\{BGM,ABTL,CR,ATB,CHSE}.B;1`. Tous presents dans `data/`, avec des
tailles qui recoupent les tables d'offsets internes - la table de 40 entrees d'`ATB.B` remplit
exactement ses 348 secteurs, et la table de morceaux d'`USAGI.B` s'arrete a `0x43B18` dans ses
301 056 octets.

Dix tables sont fermees aux deux bouts, dont une **table de sinus de 451 entrees au degre**,
verifiee numeriquement contre `round(4096 * sin)` avec un ecart maximal de 1.

## L'interface entre overlays

SELECT.EXE utilise le meme bloc de RAM haute `0x801FF000` que TITLE.EXE, et n'y touche que six
regions disjointes:

| region | contenu |
|---|---|
| `0x801FF000` | 24 octets de parametres de lancement |
| `0x801FF018` | 64 octets d'options - **contient les tables de remap de boutons a `0x801FF020` et `0x801FF03C`** |
| `0x801FF068` | le code de resultat de la carte memoire |
| `0x801FF100` | 14 octets de roster VS |
| `0x801FF1FC-0x801FF247` | 76 octets de zone d'enregistrement de sauvegarde |
| `0x801FFF00` | le brouillon d'en-tete de `LoadExec` |

Les deux premieres lignes confirment independamment ce que le portage de TITLE.EXE avait mesure:
`0x801FF000 + 0x10*2 = 0x801FF020` et `+ 0x1E*2 = 0x801FF03C` sont bien les tables de remap, et
`0x801FF068` porte le meme resultat de carte avec le meme sens. Le modele `SharedHighRam` du
portage satisfait deja cette interface.

## Consequence pour le portage

L'infrastructure C# de TITLE.EXE - systeme de taches, pools de primitives, boucle de frame - **ne
se reutilise pas**. Le prealable est du travail SDK: libgs, les evenements noyau, les commandes
`CdControlB` et le pad BIOS.

Le point favorable est que **le code libgs est dans l'image**. Il se translittere depuis les
octets, comme `CdIntToPos` et `LoadClut` l'ont ete, et non depuis une documentation.
