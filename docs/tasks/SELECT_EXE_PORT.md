# SELECT.EXE - le portage

Suite de `SELECT_EXE_RECON.md`, qui etablit que cet overlay n'est pas TITLE.EXE recompile mais un
autre moteur, bati sur libgs, sans ordonnanceur de taches ni `malloc` ni boucle de frame.

## Ce qui tourne

Les deux chemins de sortie principaux sont vivants.

| chemin | etat |
|---|---|
| DEMO | atteint son `LoadExec` de `cdrom:\DEMO.EXE;1` |
| VS | atteint son `LoadExec` de `cdrom:\VS.EXE;1` |
| SP | **code complet**, mais conditionne par un bit de sauvegarde qu'aucune carte de cet hote ne pose |
| etat 3 (test sonore) | `BLOCKED` sur libsnd |

Dix-huit fichiers sous `SELECT_EXE/`: le boot, le pas de frame, la carte memoire, le pilote de
menu, les trois branches de mode, le curseur de liste, l'entree pad, l'intro de menu, la selection
3 contre 3, le module de decoration et les enregistrements de carte.

## Le prealable: du SDK, pas du jeu

Rien de tout cela ne pouvait tourner sans quatre ajouts au SDK, tous **translitteres depuis
l'image de SELECT.EXE** plutot qu'ecrits depuis une documentation.

- **libgs**, absent en totalite. `GsOT`, `GsOT_TAG`, `GsSPRITE`, `GsLINE`, `GsBOXF` et les douze
  routines que `FUN_800344A4` appelle.
- **la table d'evenements noyau**, sans quoi le portage bouclait **sans fin** au boot.
- **les commandes `CdControlB`**, dont les boucles de retry sur `0x0E` et `0x0A`.
- **le pad BIOS**, que cet overlay lit directement au lieu de passer par libetc.

Un detail de methode qui a compte: Ghidra a l'archive `psyq340` appliquee, donc **les noms de
champs que le decompilateur affiche ne sont pas des preuves**. Chaque decalage a ete re-derive
depuis un `lw`/`lh`/`lbu` reel - les dix-huit champs de `GsSPRITE` depuis les lectures de
`GsSortSprite`, son pas de 36 octets mesure deux fois independamment.

Deux faits qui paraissent faux tant qu'on ne les trace pas, et que tout portage de cet overlay doit
respecter:

- **`GsSetDrawBuffOffset` lit l'index de tampon OPPOSE** a `GsSetDrawBuffClip`. Delibere: l'offset
  qu'il publie est consomme par le tri de la frame **suivante**.
- **`GsInit3D` rend les coordonnees 2D relatives au centre de l'ecran.** Les `x`/`y` d'un
  `GsSPRITE` sont des decalages depuis (160,120) plus l'origine VRAM du tampon cible.

## Ce que le portage a ferme, et que la reconnaissance laissait ouvert

### Le chemin de televersement de l'enregistrement 18

`USAGI.B` porte 19 enregistrements; le chargeur en decode 18 vers la VRAM et laisse le dix-neuvieme
a `0x80080000` **sans jamais le televerser**. La reconnaissance ne savait pas qui s'en servait.

C'est la selection de personnages elle-meme qui televerse, tuile par tuile, quand un emplacement
change. Trois accords independants ferment la geometrie:

| accord | valeur |
|---|---|
| le `tpage` du sprite resout en VRAM (960, 256) | et la base `x` du rect est `0x3C0` = 960 |
| `u` et `v` avancent de 48 pixels | soit 12 demi-mots en 4 bits par pixel |
| 35 tuiles x `0x480` | = 40 320, exactement l'etendue que Ghidra type pour le symbole |

### Deux manettes

L'ecran lit la manette 1 **et** la manette 2 dans la meme frame - et deux rendus Ghidra de la meme
ligne se contredisaient. Tranche depuis le flux d'instructions: la manette 1 garde son mot
anti-rebond, la manette 2 est **relue brute**, et cette relecture atterrit dans un registre autre
que celui que la decompilation suggere.

### Deux permutations qui ne sont pas interchangeables

Le roster exporte vers `0x801FF102..10C` prend ses valeurs dans la table de **selection**, pas dans
la table de **tuiles**. Ce sont deux permutations differentes des memes 35 entrees.

## La passerelle qui manquait

Le point le plus consequent, et le plus facile a manquer: **rien n'executait SELECT.EXE**.

`ActivateSelectExe()` n'avait aucun appelant, et le `LoadExec` de TITLE.EXE levait son exception de
transfert sans dispatcher. La chaine s'arretait donc a l'ecran titre - et pire, le resolveur
d'adresses serait reste pointe sur la carte de TITLE.EXE pendant que le code de SELECT.EXE lisait
a travers lui.

C'est cable maintenant, sur le meme motif que `SLPS_003.55 -> MOVIE.EXE -> TITLE.EXE`.

## Ce qui reste bloque, et par quoi

### L'audio: libsnd

`FUN_800315c0`, l'etat 3 du menu. Son blocage a ete **re-mesure** plutot qu'herite: sur ses neuf
callees, cinq sont portees; le verrou est `FUN_80026420` -> `FUN_80022994`, dont la liste de
callees porte `SpuInit`, `SpuStInit`, `SpuSetVoiceAttr`, `SpuMallocWithStartAddr`, `SsSetReservedVoice`,
les trois enregistrements de callback `SpuSt*` et six groupes `CdSearchFile`/`CdRead` - les
chargements de VAB `\SOUND\*.B`.

`LibSnd.cs` compte aujourd'hui **163 declarations, dont 161 methodes a corps vide**.

### La musique CD-DA: pas de TOC

`CdReady` et `CdGetToc` ont maintenant de **vrais corps translittteres**, et tous deux ne
produisent toujours rien - pour une raison documentee chacun, sans TOC fabriquee.

Le contrat de retour de `CD_ready` a du etre decode depuis les octets: Ghidra rend ses quatre
sorties comme des appels a des fragments d'epilogue et masque la valeur entierement.

La TOC du disque n'est **pas recuperable** de ce que ce depot contient, verifie trois fois:

- `LibDs` attribue des LBA **synthetiques** pour l'adressage de fichiers, pas la disposition du
  disque
- `data/tracks/` contient bien les 19 pistes CD-DA, mais elles donnent des **durees**; les
  convertir en positions absolues exigerait la longueur de la piste de donnees 1, absente
- aucun `.cue`, `.toc` ni `.ccd` nulle part

Un piege est enregistre plutot que repare (regle 12): une entree de TOC toute a zero remise a
`CdlSetloc` verrouille un positionnement a LBA -150, qui ne resout vers aucun fichier, et un
`CdRead` suivant tournerait indefiniment. Rien ne l'atteint aujourd'hui.

## Trois erreurs d'annotation corrigees

Toutes du meme genre: une affirmation vraie quand elle a ete ecrite, fausse quand elle a ete lue.

1. Deux notes justifiaient un `BLOCKED` du module carte memoire par « `TestEvent` rend 0 sans
   condition ». Vrai a la reconnaissance, faux des que le SDK a ete fait. L'agent l'avait herite du
   dossier au lieu de lire le SDK.
2. Une note citait `FUN_8002A178` comme stub bloque - alors que **le meme changement** venait de le
   porter.
3. Une note affirmait que la selection de personnages est **le seul** ecrivain du bit que la
   branche VS attend. Il y en a **trois**: `main`, `FUN_8002cc04` et elle. Les trois sont
   translitteres. Ce qui fait dependre la branche de celle-ci, c'est que `main` **efface** le mot a
   chaque tour avant de dispatcher.

Cette derniere venait de mon propre brief, herite d'un resume de reconnaissance plutot que des
references croisees. C'est exactement le motif contre lequel la regle « les trouvailles d'un
eclaireur sont des entrees, pas des resultats verifies » existe - et il s'est repete trois fois
dans ce portage.

## Ce qui n'est pas observe

Aucun banc ne pilote SELECT.EXE. Son exactitude repose sur la relecture octet par octet et sur les
verificateurs en contexte neuf, pas sur une execution. Le premier effort utile serait un banc qui
fait tourner `main` sur plusieurs frames, comme il en manque un pour la fin du chemin de TITLE.EXE.

La geometrie des sprites de la selection est par ailleurs `PARTIAL`: l'ecran anime des positions
que `FUN_8002a178` arme, et les roles inferes pour huit sprites n'ont pas ete re-derives depuis ce
constructeur une fois celui-ci porte.

## Le renommage

80 symboles nommes dans Ghidra **et** dans le portage: 26 fonctions, 54 globales et labels.
Le reste - 55 candidats classes PROBABLE - est differe, et la majorite de l'overlay garde ses
`FUN_`/`DAT_`/`LAB_`.

### Quatre noms qui ne sont pas nouveaux

`ClearVram`, `DecompressLzss`, `InitializeMemoryCard` et `ShutdownMemoryCard` reprennent les noms
que TITLE.EXE porte deja, parce que ce sont **litteralement les memes routines relinkees**.
Verifie octet par octet sur les deux images, pas sur la parole du diff:

| routine | taille | difference |
|---|---:|---|
| `ClearVram` | 76 o | deux mots `jal` relocalises |
| `DecompressLzss` | 156 o | **un seul** mot, un `j` vers son propre epilogue |
| `InitializeMemoryCard` | 80 o | les six `jal` |
| `ShutdownMemoryCard` | 256 o | les `jal` et huit deplacements `gp`, dont les pas intra-groupe correspondent encore |

Le portage garde les deux exemplaires, comme la console: chaque image a le sien, et celle de
TITLE.EXE a disparu quand SELECT.EXE tourne.

Le pas de frame s'appelle `DrawFrame`, **pas** `RunFrameLoop`: celui de TITLE.EXE possede un
`do/while` et un ordonnanceur, celui-ci est un pas unique que soixante et un ecrans bloquants
appellent depuis leurs propres boucles.

### Le piege etait vivant sur treize des vingt-six

La lecon du renommage de TITLE.EXE - `set-function-prototype` installe exactement ce que dit la
chaine, et la signature **stockee** ment des que le decompilateur recupere les parametres
dynamiquement - s'est averee necessaire ici aussi. Sur les 26 fonctions renommees, **13** avaient
un prototype stocke vide alors que leur signature decompilee portait de vrais parametres.

Chaque renommage est donc parti de la signature **decompilee**, et l'acceptation s'est faite
**cote appelant**: les trois appelants de `ShutdownAndLoadExecutable` rendent toujours leur nom
d'overlay, `InitializeSpriteArray` rend toujours ses deux arguments.

Une fonction a ete laissee brute pour la meme raison: Ghidra stocke `FUN_80030a6c` comme `void`
alors que `main` lit sa valeur de retour. Figer ce prototype aurait casse silencieusement la
distribution du `switch`.

### Deux points ouverts

**1. `g_GsLineArray4` @ 0x80065484 - un dommage collateral irreversible.**

Le releve avait explicitement signale cette entree comme demandant une decision separee: ces
adresses servent aussi de **bases choisies par le compilateur pour des decalages de champs
`GsSPRITE`**. L'agent l'a appliquee quand meme, en deduisant l'accord d'un decompte.

Consequence, vivante: une centaine de lignes d'ecriture de sprite se decompilent maintenant en
`g_GsLineArray4 + 0x168` la ou elles designent le `x` du sprite 7. **Le nom est juste pour la
donnee et trompeur pour le code.**

Ce n'est pas annulable par ReVa - il n'y a ni `delete-label` ni `rename-symbol`, et PyGhidra est
indisponible. Le seul remede est l'interface graphique de Ghidra. Le portage C# a ete aligne sur
Ghidra plutot que l'inverse, pour que les annotations `// GHIDRA:` ne nomment pas un symbole
inexistant.

**2. Le bloc partage 0x801FFxxx: les trois overlays ne s'accordent plus.**

Six noms appliques vivent dans le bloc de RAM haute que SLPS_003.55, TITLE.EXE et SELECT.EXE se
partagent: `g_CardProbeResult` (0x801FF068), `g_OptionsRecord64` (0x801FF018),
`g_PadRemapTable0`/`1` (0x801FF020/03C), `g_DemoSaveRecords3` (0x801FF200) et
`g_SpSaveRecords3` (0x801FF218).

SELECT.EXE les porte nommes; les deux autres programmes non. En particulier 0x801FF068 est
`g_CardProbeResult` dans SELECT.EXE et reste `DAT_801ff068` dans TITLE.EXE et dans
`SharedHighRam.cs`, alors que TITLE.EXE y ecrit le retour de la meme sonde.

Restaurer l'accord demande une passe sur `/TITLE.EXE` et `/SLPS_003.55` - ce dernier etant
**versionne et checked out**, ce qui n'est pas la meme operation. Laisse a decider.
