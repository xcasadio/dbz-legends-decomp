# VS.EXE — le socle, et le point de reprise

Session autonome, mode AUTO annonce. Ce document remplace l'etat de reprise de
`VS_EXE_TRANCHE4.md` pour tout ce qui a bouge; le plan de vagues de ce document-la
reste valable.

## Fait, verifie, committe

| | |
|---|---|
| Outillage de couture | `check_vs_dispatch.py` sort enfin `1` en cas de desaccord; `check_duplicate_symbols.py` rapatrie dans `custom-tools/scripts/` |
| `RotAverage3` | ajoutee a `LibGte` reconstruite, **puis remplacee par son vrai corps** `0x800772E4`; banc `--validate-gte-rotavg` |
| `FUN_8003f6c0` | debloquee et transliteree: la transformation GTE par maillage, 724 octets |
| Scratchpad GTE | dix-neuf mots partages dans `Scratchpad.cs` a la racine |
| Doublons intra-VS | 0 stockage duplique, 0 type divergent (etait 12 et 2) |
| `SoundState.cs` | le workspace son declare: taille 0x194 fermee deux fois, cinq bancs CD nommes par leurs litteraux |
| `FUN_8005f704` | translittere depuis l'image, **puis corrige contre Ghidra revenu**; banc `--validate-sound-loader` avec temoin negatif |

Acceptation a chaque etape: build propre, douze bancs verts, trois checkers de
couture verts, `--diag-select 400` = **49396** pixels, inchange.

## Ce qui a change par rapport au plan de tranche 4

**Le scratchpad ne se deplace pas en bloc.** Le plan disait que VS utilise « les
memes 124 symboles » et prescrivait un deplacement complet. La verification
adresse par adresse le refute: c'est de la RAM rapide **reutilisable**, et deux
overlays y rangent legitimement des choses differentes. Trois adresses restent
declarees des deux cotes, chacune documentee a son site:

- `0x1F80012C` — TITLE y tient son compteur d'image de chargement 0..2, VS autre chose;
- `0x1F800120` — TITLE y seme un offset geometrique que sa tache camera promeut, VS y calcule un Y d'horizon;
- `0x1F8000D0..E0` — les deux `SetupGeometry` y ecrivent pareil, mais seul TITLE relit; le cote lecture est ce qui fixe le sens.

**Et une adresse n'est pas un doublon mais un defaut**: `0x1F80009C`. TITLE
declare `VECTOR_1f800094`, un `LibGte.VECTOR` de 16 octets — l'octet `0x9C` est
donc son champ `.vz`. VS declare un `int DAT_1f80009c` par-dessus et **le lit
alors que personne ne l'ecrit**. Aliasing, consigne, pas corrige.

**Les 17 « doublons » inter-overlay n'en sont pas.** Chaque paire
`TaskSystem`/`PadInput`/`PrimitivePools` est a une adresse **differente** dans
chaque overlay — `g_TaskListHead` est `0x80079854` dans VS et `0x80083b3c` dans
TITLE. Ce sont des homonymes: deux overlays lies separement, les noms C# empruntes
a TITLE pour la lisibilite, chacun dans son namespace. Les fusionner serait faux.
Le balayage compare desormais des **adresses** et les etiquette comme tels.

## La decouverte qui debloque la suite

`VS_EXE_TRANCHE4.md` note Ghidra comme source de verite, et le serveur ReVa a ete
injoignable pendant la plus grande partie de cette session. **PCSX-Redux, lui,
repond, et sa RAM contient VS.EXE charge.** C'est une source de preuve
verifiable, et le reste de ce document montre jusqu'ou elle porte:

```
mcp__pcsx-redux__pcsx_get_status      -> debugger: true, running: false (en pause, pas vide)
mcp__pcsx-redux__pcsx_disassemble     -> desassemblage MIPS a n'importe quelle adresse
mcp__pcsx-redux__pcsx_read_memory     -> octets bruts, pour les tables
```

Controle de validite fait avant de s'en servir: `FUN_80061ed8 @ 0x80061ED8`
desassemble en exactement 68 octets avec son `beqz $v0` rebouclant sur
`CdSearchFile` — la boucle retiree au lot 1, conforme au commentaire du port.

**`gp = 0x8008D0FC`.** C'est la cle qui manquait: tout acces `0xNNN(gp)` se resout
en symbole. `0x188(gp)` = `0x8008D284`, `0x244(gp)` = `0x8008D340`.

## Les deux canaux de preuve, et ce que chacun a rate

C'est le resultat le plus utile de la session, et il ne concerne pas une
fonction en particulier. **ReVa est revenu**, et le portage de `FUN_8005f704` a
ete rejoue contre lui. Le verdict est net et il se separe en deux:

- **Le flot de controle a tenu.** Huit cas, les regressions, le compte a rebours
  partage: rien n'a bouge. Le desassemblage brut lu instruction par instruction
  prouve la **forme** d'une fonction, et il l'a prouvee juste.
- **Les liaisons n'ont pas tenu.** Dix appelees etaient des `FUN_` anonymes avec
  des roles devines. Ghidra les nomme toutes, et six sont des routines libcd
  standard: `CdPosToInt`, `CdIntToPos`, `CdControl`, `CdSync`, `CdReadSync`,
  `CdRead`. Ce n'est donc pas « une machine a requetes opaque » mais **un
  chargement CD ordinaire**.

Un de ces ecarts etait un vrai defaut, pas une question de nom: le troisieme
argument de `CdControl`, le bloc de reponse de huit octets, avait ete supprime
comme « un local de pile que ce portage n'a rien pour remplir ». C'est le meme
bloc que tous les autres sites d'appel de ce portage passent deja.

Et deux champs du workspace ont ete renommes par ricochet: `+0xD8` et `+0xDC`
s'appelaient `LoadRequestScratch` et `LoadRequestKind`. Ce sont une `CdlLOC` et
un nombre de secteurs. **La lecon a retenir pour les tranches suivantes**: le
desassemblage seul donne les offsets et les largeurs justes, et des sens
plausibles et faux.

Enfin, trois noms devines se sont averes exacts — `SsVabOpenHeadSticky`,
`SsVabTransBody`, `SsVabTransCompleted`, confirmes par la table des symboles.
Avoir raison par chance n'est pas avoir raison par preuve; c'est pour cela
qu'ils etaient etiquetes comme inference jusqu'a cette verification.

## Ce que le portage de `FUN_8005f704` a etabli

Elle est faite, et elle **ne suffit pas a faire dessiner la scene**. Ce n'est pas
une deception, c'est un resultat: le blocage est ailleurs, et on sait ou.

La moitie libcd est reelle dans `PsxSdkMonogame`, donc la machine marche
vraiment jusqu'a l'etat 7. Ses appelees VAB, elles, sont des stubs
`return default` dans `LibSnd`: la machine atteint l'etat 7 et y reste, parce que
le test « pas encore » de l'etat 7 est exactement `SsVabTransCompleted` rendant 0.

**Le blocage restant est libsnd, pas cette fonction.** Le banc
`--validate-sound-loader` epingle ce blocage comme un fait d'aujourd'hui: quand
quelqu'un implementera libsnd, c'est cette assertion-la qui echouera, et c'est le
signal qu'il faudra la mettre a jour.

Ce banc a d'ailleurs deja fait son travail une fois. Ecrit quand le sondage etait
un stub rendant 0, il affirmait que l'etat 2 **se tenait**. Le vrai `CdSync` rend
`CdlComplete`, l'etat 2 avance, et **le banc a echoue au moment ou la liaison a
ete corrigee** — ce pour quoi il existe. Il affirme desormais la marche complete
0 -> 1 -> 2 -> 3 -> 4 -> 5 -> 7, plus le fait que la position de seek est ecrite
*dans* le workspace et pas seulement dans un objet C#.

Reste aussi le second verrou, independant: `FUN_8005a5b0` (`BattleManager.cs`,
8500 octets), sans lequel aucun combattant ne bouge et la jauge ne monte jamais.

## La meme erreur, deux fois, et ce qu'elle coute

Le retour de Ghidra a corrige `FUN_8005f704`. Appliquer le meme controle a
`RotAverage3` a trouve **exactement le meme defaut**, ce qui en fait une classe
et non un accident.

`RotAverage3` avait ete **reconstruite**, sous l'affirmation ecrite qu'« aucun
symbole `RotAverage3` autonome n'a ete trouve dans les images ». Cette phrase
etait vraie de la recherche faite, et fausse des images: la routine est a
`0x800772E4` dans VS.EXE, 88 octets. Le corps reconstruit avait la bonne
sequence d'operations et la **mauvaise signature**: six parametres au lieu de
huit, les sorties `p` et `flag` ecartees par le raisonnement « l'appelant ne les
garde pas ».

C'est litteralement le defaut de `CdControl`, dont le troisieme argument avait
ete supprime comme « un local de pile que ce portage n'a rien pour remplir ».
**Deux fois, un parametre de sortie a ete argumente hors d'existence a partir de
ce que l'appelant fait, au lieu de ce que la routine ecrit.**

Et il faut dire pourquoi aucun banc ne l'a vu, parce que c'est instructif: le
seul appelant jette les deux sorties, donc l'OTZ etait identique. Le banc
`--validate-gte-rotavg` compare `RotAverage3` a `RotAverage4`; **une comparaison
entre deux routines ne peut pas voir un parametre qu'on ne demande a aucune des
deux.** Un banc borne la correction, il ne borne pas la signature. Seule la
table des symboles repond a ca.

Consequence pratique pour la suite: **avant de porter, demander a Ghidra la
signature de chaque appelee**, meme quand le desassemblage semble suffire. Le
desassemblage seul donne les offsets et les largeurs justes, et des sens
plausibles et faux.

## `FUN_8003f6c0`, debloquee par ricochet

Elle etait marquee `BLOCKED` dans `AnimCmdMesh.cs` pour une seule raison:
« LibGte fournit `RotAverage4` mais pas `RotAverage3` ». Le motif a disparu, et
il valait mieux qu'il disparaisse *apres* la correction: c'est le seul site du
portage qui appelle `RotAverage3`, il aurait donc herite de la signature fausse.

C'est la transformation GTE par maillage, 724 octets, un seul appelant
(`AnimCmd_CulSet` @ `0x80038998`). Elle compose la matrice modele — rotation du
creneau `param_3`, translation de `param_4` biaisee par les deux offsets du
scratchpad, echelle de `param_5` — contre la matrice camera du scratchpad, puis
projette chaque quad et ecrit l'OTZ dans la table Z par primitive.

Le `pad` du premier sommet porte le **genre** de la primitive, lu en demi-mot
signe: `0` -> `RotAverage4`, `1` -> `RotAverage3(v0,v1,v2)`, sinon
`RotAverage3(v0,v2,v3)`. Le troisieme cas est ce qui prouve que ce champ est un
genre et pas un compte de sommets: un quad coupe sur l'autre diagonale fait
toujours trois sommets, mais pas les memes trois.

Le socle a servi exactement a ce pour quoi il a ete fait: `Scratchpad.cs`
fournissait deja `MATRIX_1f800000`, `_DAT_1f8000b4` et `_DAT_1f8000bc`, et
`SpriteRenderer` fournissait le precedent du pont adresse -> (buffer, offset)
via `LibGpu.RamResolve`. Rien n'a eu a etre invente.

## Le prochain pas, precisement

`FUN_8005f704` bloque **tout** le rendu de scene: tant qu'elle rend 0, la phase 1
du chargeur ne passe jamais, la tache de scene n'est jamais creee, et
`RenderBattleScene3D` — qui est deja porte et fonctionnel — n'est jamais appele.
C'est pour cela que l'ecran est bleu apres la selection des joueurs.

Ce n'est pas une petite fonction. Etendue `0x8005F704`..`~0x8005FB90`, soit
**~1168 octets**, avec une table de saut de huit cas a `0x80020A84`:

| cas | cible | | cas | cible |
|---|---|---|---|---|
| 0 | `0x8005F754` | | 4 | `0x8005F9B0` |
| 1 | `0x8005F784` | | 5 | `0x8005FA34` |
| 2 | `0x8005F870` | | 6 | `0x8005FAEC` |
| 3 | `0x8005F8F4` | | 7 | `0x8005FB54` |

Elle travaille sur le **workspace son** pointe par `0x188(gp)` = `0x8008D284`, aux
offsets `+0x12A`, `+0x12C`, `+0x148`, `+0x158`, et touche `0x244(gp)` =
`0x8008D340`. C'est donc bien le groupe `D_SoundTaskCd` du plan.

**Donc l'ordre est: `SoundState.cs` d'abord** — le point 2 du socle, non fait —
puis la machine a etats. Declarer `DAT_8008d284` et ses offsets avant de porter
la fonction qui les lit est exactement ce que le socle existe pour eviter de
rater. La porter sans eux reviendrait a inventer des noms pour des offsets, ce
que le mandat refuse.

## `FUN_8005a5b0`: la reconnaissance, et pourquoi elle precede le portage

C'est le dernier verrou *portable* (libsnd est du travail SDK, pas de la
translitteration). 8500 octets, **1078 lignes** decompilees, quatre appelants
(`FUN_80055f94` deux fois dont une par `LAB_80057794`, `FUN_800578e0`,
`FUN_80057a40`) et seulement cinq appelees: `FUN_80058338`, `FUN_80026d98`,
`FUN_80057a7c`, `FUN_80058120` (deux fois) et `AddPrim` (douze fois).

**Signature: `void FUN_8005a5b0(short *param_1)`.** Un seul parametre, une grande
structure. C'est une machine a etats par combattant, pas un rendu: les douze
`AddPrim` sont la queue.

### Le piege a desamorcer avant tout le reste

Ghidra declare un local `auStack_100b8[32768]` — **64 Ko de pile**. Il n'existe
pas. Le prologue reel, lu en octets a `0x8005A5B0`:

```
3C02800B  lui   v0,0x800B
9442305A  lhu   v0,0x305A(v0)     ; DAT_800b305a, la porte d'entree
27BDFF38  addiu sp,sp,-0xC8       ; la trame fait 0xC8 = 200 octets
```

C'est un artefact du decompilateur. Le seul vrai tableau local est
`local_b8[76]`. Quelqu'un qui porterait depuis le listing allouerait un tampon de
64 Ko et modeliserait une structure qui n'existe pas — exactement la classe
d'erreur que cette session a corrigee trois fois.

### Ce qui est deja lisible de la structure

- `param_1 + 8` — un mot de drapeaux `uint`. Bits vus: `0x10000000` (garde une
  branche entiere), `0x4000`, et `0x10000600` pose en bloc.
- `param_1[0xad8 + n * 10]`, **douze entrees de dix shorts** — l'enregistrement
  par combattant. Les bornes sont explicites et donnent le sens des champs:

  | champ | borne | lecture |
  |---|---|---|
  | `+0` | drapeaux | bits `0x200`, `0x81`, `0x1000` |
  | `+1` | `[0, 0x640]` | 1600 |
  | `+2` | `[0, 16000]` | |
  | `+3` | `[0, 20000]` | |
  | `+5` | `[0, 99]` | un compteur a deux chiffres |

- `param_1[0xa90 + n * 2]` — **un pointeur par entree** vers un bloc de tache:
  le code va chercher `*(bloc + 8) + 0x138` et y pose `0x4000000`. C'est la meme
  forme `+0x08 = contexte` que `SoundState` a etablie pour la tache son.
- `param_1[0x16b2]` / `param_1[0x16b3]`, et une table de `ushort` a l'octet
  `0x2c14` indexee par la meme entree.
- L'entree entiere est gardee par `(DAT_800b305a & 1) == 0`, le meme drapeau
  global que `ExecuteAnimStreamBatch` consulte.

### L'ordre a suivre

**`BattleState.cs` d'abord, la machine ensuite** — exactement l'ordre que ce
document a prescrit pour la tache son, et pour la meme raison. Porter 1078 lignes
qui indexent `0xad8`, `0xa90`, `0x16b2` et `0x2c14` sans avoir nomme ces offsets
reviendrait a inventer des noms au fil de l'eau, ce que le mandat refuse, et a
les inventer differemment a chaque site.

La structure est grande (au-dela de `0x2c14` shorts, soit plus de 22 Ko), donc
`BattleState.cs` doit declarer **ce que cette fonction touche**, pas la structure
entiere, et le dire.

## Ce qui attend toujours l'utilisateur

- **Pousser** le sous-module `PsxSdkMonogame` puis le superprojet. Rien n'a ete
  pousse; un clone frais ne construit pas.
- ~~`AnimVmInterpreter` garde des scalaires prives pour `+0x08` et `+0x14`~~ **fait.**
  L'image a tranche: `lbu`/`sb` a `0x8003698C` et `0x80036998`, donc des OCTETS. Le
  scalaire etait un `int`, et une ecriture 32 bits a `+0x08` aurait ecrase `+0x09`,
  `+0x0A` et `+0x0B` — trois champs que `BattleScene` documente separement. Les deux
  passent desormais par `BattleScene.RAM_800990c0`, ce qui ferme la duplication et
  **arme** la region au passage: `PsxRam` rend 0 en silence sur une adresse qu'aucune
  region ne couvre, donc y acceder par adresse depuis un autre fichier etait une
  hypothese d'ordre d'initialisation, pas une garantie.
- `data/tracks/` (211 Mo), deux `.palettes.json` de `CH_BIN1` et un fichier de
  `DOC` ne sont pas revenus de la restauration.
