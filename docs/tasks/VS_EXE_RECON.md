# VS.EXE - reconnaissance

## Le renversement, et il va dans le bon sens

La reconnaissance de SELECT.EXE avait detruit l'hypothese de depart: on croyait cet overlay
recompile depuis TITLE.EXE, c'etait un autre moteur. La lecon retenue etait de ne rien supposer par
analogie.

Appliquee a VS.EXE, la meme methode donne le resultat inverse:

**VS.EXE roule le moteur de TITLE.EXE.** Meme ordonnanceur a 21 listes, meme libgpu direct, meme
table d'ordonnancement de 0x800 entrees, meme `InitHeap(0x10000, 0x10000)`. C'est **SELECT.EXE qui
est l'exception** dans cette image, pas VS.EXE.

| | TITLE.EXE | SELECT.EXE | **VS.EXE** |
|---|---|---|---|
| couche graphique | libgpu direct | libgs | **libgpu direct, zero libgs** |
| ordonnanceur | 21 listes | aucun | **21 listes** |
| tas | `InitHeap(0x10000, 0x10000)` | jamais utilise | **identique** |
| entree | libetc `PadRead` | pad BIOS | **libetc** |

L'absence de libgs est fermee deux fois: aucun symbole `Gs*` parmi les 1356 fonctions, et les 175
sites d'appel libgpu du code jeu enumeres un par un.

## Le second bloc .text est un fantome

`get-memory-blocks` annonce deux `.text`, 651 212 octets au total. C'est faux, et la correction
retire 268 844 octets du perimetre:

- le second bloc, `0x800C3DD4-0x801057FF`, contient **zero fonction**, zero `jal`, zero pointeur de
  code. 97,2 % de zeros;
- sa frontiere basse `0x800C3DD4` est `_end`, prouve par la boucle d'effacement du BSS du crt0 en
  `0x80072F50-0x80072F6C`. L'etiquette `.text` de Ghidra est fausse; la frontiere est juste;
- ce qu'il contient reellement: du remplissage a zero, puis **16 384 octets de donnees** a l'ORG fixe
  `0x80101800`, byte-identiques dans TITLE / VS / SP / GAME.

**Le code reel de VS.EXE fait 382 368 octets.**

### Une consequence qui corrige une conclusion anterieure

Il avait ete mesure, et rapporte, que VS.EXE, SP.EXE et GAME.EXE font exactement 942 080 octets et
sont identiques a partir de l'offset `0x6DAAD` — pres d'un demi-megaoctet commun — et il en avait ete
tire que porter l'un donnerait un levier sur les deux autres.

**C'est faux.** Ce demi-megaoctet est du remplissage a zero. Le seul contenu reellement partage est
la section de donnees de 16 Ko. Il n'existe **aucun module de code commun a extraire de la queue**.
La mutualisation entre les trois gros overlays, si elle existe, est entierement dans le premier
`.text` et reste a mesurer.

## La repartition du code, mesuree

Sur les 382 368 octets reels:

| poste | part |
|---|---:|
| **code jeu** | **70,4 %** (269 016 o, 301 fonctions) |
| libspu + libsnd | 16,9 % |
| libgpu | 4,4 % |
| libcd | 3,6 % |
| libgte | 3,5 % |
| libetc | 0,9 % |
| libapi | 0,35 % |
| crt0 | 0,10 % |

Le code jeu pese **1,44 fois tout le `.text` de SELECT.EXE**, tous postes confondus.

## Ce qui ne s'applique pas, et qu'il ne faudra donc pas ecrire

Verifie, pas suppose. Chacun de ces points etait un blocage anticipe:

- **libgs**: absente. Le prealable annonce par le rapport SELECT.EXE - « le SDK libgs, sans quoi
  rien ne s'affiche » - ne concerne pas VS.EXE.
- **MDEC et libpress**: absents. `MDEC_REG0` et `MDEC_REG1` a zero reference, valide contre
  `GPU_REG0` qui en compte 17 - la meme methode que pour SELECT.EXE.
- **libhmd, libmath, streaming CD-XA, tas PSYQ**: symboles inexistants.
- **flottant logiciel libgcc**: aucune entree liee. Le trou connu sur `__adddf3` est sans objet ici.
- **pad BIOS**: VS.EXE utilise le pad libetc, pas le pad BIOS de SELECT.EXE. Un blocage de moins.

## Ce qui se reutilise

Deux couches, et c'est la difference majeure avec SELECT.EXE.

**Le SDK, tel quel.** 47 routines Sony sur 48 sont identiques modulo relocation: les trois overlays
lient les memes objets PSYQ, les adresses changent par groupe, jamais le code. `LibGpu`, `LibGte`,
`LibCd`, `LibApi`, `LibEtc`, `LibMcrd` servent VS.EXE sans modification. Seul `_spu_init` diverge et
demande verification avant reutilisation du portage libspu.

**L'infrastructure C# de TITLE_EXE**, ce que SELECT.EXE avait fait craindre impossible:

| brique | VS.EXE | existant C# | verdict |
|---|---|---|---|
| systeme de taches | noeud 0x18, callback +0x04, workspace +0x08, 21 listes | `TITLE_EXE/TaskSystem.cs`, memes offsets, `new [21]` | memes offsets, meme cardinalite |
| entree manette | `FUN_80061800`, remap 14 entrees | `TITLE_EXE/PadInput.cs`, meme boucle | meme algorithme, seules les destinations different |
| boucle de frame | libgpu direct, double tampon, OT 0x800 | `TITLE_EXE/FrameLoop.cs`, `PrimitivePools.cs` | meme forme |
| RAM haute partagee | 0x801FF020/03C lus, 0x801FF100 lu 13 fois | `SharedHighRam` | deja satisfait |

Le contrat entre overlays se voit maintenant **des deux cotes**: les tables de remap de boutons que
SELECT.EXE ecrit a `0x801FF020` / `0x801FF03C` sont lues par VS.EXE dans `FUN_80061800`.

## Le coeur du combat, ferme

- **Structure de combattant: 0x240 octets**, espace de travail de tache, creee a `0x80051324`,
  callback `LAB_80050AE4`, liste 10. **Six instances** dans un tableau de 12 emplacements a
  `contexte+0x1520`: 3 contre 3, emplacements 0/1/2 et 6/7/8.
- **Contexte de combat: 0x3034 octets**, tache 0x51 sur la liste 9. Jauge centrale a `+0x302C`,
  bornee a +/-30000. Jauge de ki a `+0x15B4`, plafond 16000. Enregistrements de 0x14 octets par
  emplacement a `+0x15B0`. Index de cible a `+0x15C0`.
- **Ordre du pas de combat**: liste 20, puis 0 a 19 - donc gestionnaire (9), combattants (10),
  scene et rendu (12) dans la meme frame, avant `DrawOTag`.
- La fonction la plus appelee du programme est `FUN_80052DB4` @ 0x80052DB4 avec **139 sites**, contre
  64 pour la suivante. C'est le `DrawSpriteGroup` de TITLE.EXE.

### La veine de nommage

Le programme porte **une VM de script de 51 opcodes** - table de gestionnaires a `0x800822F4`,
**table de noms a `0x800823C0`**. C'est le coeur du rendu des personnages, et les opcodes sont
**nommes dans l'image**. C'est la seule veine de nommage du programme, et elle vaut plus que tous
les `printf` de debug reunis: la translitteration part avec la semantique en main.

## Les donnees

- **136 chemins CD distincts**, tous localises. **135 presents dans `data/`**; le seul manquant est
  `\CH_BIN2\CH_NO.BIN` @ `0x80081E88`.
- **Identifiants de personnage: 1 a 38**, ferme deux fois independamment - par la taille de `FACE.B`
  (76 secteurs, 2 par portrait, 38 portraits) et par le cardinal de la table AT (38 entrees).
- **15 tables fermees aux deux bouts**, dont `BGM.B` (220 secteurs, derniere entree de la table
  d'offsets = 219).
- **`0x801FF100` n'est pas un identifiant** mais un mot de mode et de resultat: trois valeurs en
  entree, ecriture de 3/4/5 en sortie. Les six identifiants du roster sont a `0x801FF102-10C`, et
  leur unique consommateur est `FUN_8005CBE0`.

## Un faux positif a corriger, dans l'existant

Le label `SpuInit @ 0x800617E0` est **faux**: il enveloppe `FUN_80061800`, qui est le
`ProcessPadInput` de TITLE.EXE au mot pres et lit les tables de remap. Le vrai wrapper libspu est
`FUN_8006DC54`.

**TITLE.EXE porte le meme faux positif a `0x80057888`** - donc l'erreur est deja dans le portage
existant, pas seulement dans VS.EXE.
