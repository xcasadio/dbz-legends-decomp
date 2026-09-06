# Retrait des attentes `CdSearchFile` — lot 1

Les sept boucles `do { p = CdSearchFile(...); } while (p == NULL)` du port sont
retirees. Chaque site fait desormais un appel unique et nomme le fichier absent.

Ce lot ne couvre **que** la famille `CdSearchFile`. Les autres attentes disque
(`CdSync`, `CdRead`, `CdReadSync`, `CdControlB`, `CdRead2`, `StGetNext`) sont
inventoriees plus bas et laissees en place pour un lot separe.

## Le motif

Sur la console la boucle a un sens: `CdSearchFile` peut rendre `NULL` pendant que
la lentille cherche encore, et tourner est la maniere dont l'appelant attend que
le lecteur se positionne et que le repertoire soit lu.

Il n'y a pas de lentille ici. Le `CdSearchFile` desktop (`LibCd.cs:1055`) passe a
`LibDs.DsSearchFile`, qui interroge le `DiscFileResolver` installe par
`PsxSdkBridges` — un `File.Exists` sous `<sortie>/data` — et memoise le succes
dans le registre de `LibDs`, LBA de base compris. La reponse est donc une
fonction pure de deux arguments que la boucle ne change jamais et d'un repertoire
que le processus n'ecrit jamais: **un second appel identique ne peut pas rendre
ce que le premier n'a pas rendu**. La boucle n'a que deux vies possibles — sortir
au premier tour, ou ne jamais sortir.

Regle 14 du mandat de portage: *ne pas simuler du materiel PSX pour le plaisir de
l'emulation si un contrat observable equivalent suffit cote desktop*.

## Le gel n'etait pas theorique

Aucun de ces corps de boucle ne contient de `VSync`, donc aucun ne rend le baton
de frame a l'hote: un fichier absent de l'arbre `data/` deploye figeait la
fenetre MonoGame entiere, sans message.

Mesure avant/apres sur le **boot du jeu** — `DbzLegendsRemaster.exe` sans
argument, qui parcourt la chaine `SLPS_003.55` -> `BANDAI.STR` -> `MOVIE.EXE` ->
`DBZ_OP.STR` -> `TITLE.EXE`:

| | Sortie |
|---|---|
| avant | aucune. Tue apres 120 s (`timeout`, code 124), fenetre figee. |
| apres | `System.IO.FileNotFoundException: CdSearchFile could not resolve \CHR_DATA\LOAD.B;1 under the deployed data tree`, a `LoadingScreen.ShowLoadingScreen()`, propagee intacte a travers les deux cadres `LoadExec` imbriques jusqu'a `Game1.Update`. |

Les bancs `--validate-*`, eux, passent tous — avant comme apres. Attention au
piege de `Program.cs`: `--validate-bandai` et `--validate-dbz-op` exigent deux
arguments, `--validate-xa-transition` en exige trois et `--validate-str-v2` deux.
Lances nus ils traversent tous les `else if` et **demarrent le jeu**, ce qui donne
l'illusion d'un banc en echec alors que c'est le boot qui echoue.

Le fichier existe dans le depot (`data/CHR_DATA/LOAD.B`), il n'est simplement pas
dans la liste `Content` de `DbzLegendsRemaster.csproj`. Le retrait des boucles n'a donc
pas casse le chemin de boot: il a nomme un defaut de deploiement que la
boucle rendait invisible depuis le debut. `TITLE_EXE_INIT_RECON.md` avait parie
l'inverse (« elle ne gelerait l'hote que si le fichier etait absent de la sortie
de build »); le pari est perdu et le document est corrige.

## Les sept sites

| Fichier | Fonction | Fichier CD |
|---|---|---|
| `TITLE_EXE/TITLE_EXE_exe.cs` | `WaitSearchFile @ 0x80057F80` | l'appelant decide |
| `TITLE_EXE/TITLE_EXE_exe.cs` | `main @ 0x800581DC` | `\SELECT.EXE;1` |
| `TITLE_EXE/LoadingScreen.cs` | `ShowLoadingScreen @ 0x800583FC` | `\CHR_DATA\LOAD.B;1` |
| `SELECT_EXE/SelectScreen.cs` | `FUN_80030698 @ 0x80030698` | `\SUB\USAGI.B;1` |
| `VS_EXE/FileIo.cs` | `FUN_80061ed8 @ 0x80061ED8` | l'appelant decide |
| `MOVIE_EXE/MOVIE_EXE_exe.cs` | `PlayDbzOpeningMovie @ 0x80020A90` | `\MOVIE\DBZ_OP.STR;1` |
| `SLPS_003_55/SLPS_003_55_exe.cs` | `PlayBandaiMovie @ 0x80020DE8` | `\MOVIE\BANDAI.STR;1` |

Le raisonnement complet est ecrit une fois, en `DEVIATION:` sur
`WaitSearchFile @ 0x80057F80`; les six autres sites y renvoient.

Trois d'entre eux lisent le `CdlFILE` juste apres la recherche — `LoadingScreen`
le passe a `CdPosToInt`, les deux lecteurs de STR dereferencent `movieFile.pos`
sur la ligne suivante, et les deux globales `CdlFILE_800a8860` et
`CdlFILE_80059744` sont relues plus tard par `UpdateTitleScreen @ 0x80021E28` et
`LoadUSAGI_B @ 0x80030908`. Un echec silencieux y produirait un LBA `-150` ou un
`pos` nul, pas un chargement vide: c'est ce qui a fait retenir l'exception plutot
que la chute silencieuse.

`iVar7 = 0` etait dans le corps de la boucle de `FUN_80030698` et reste apres la
recherche, ou le corps le mettait.

## Ce que le lot ne touche pas

`LibCd.WaitDiscLoad` n'est pas une attente de lecteur mais un **modele de
latence mesure** (587,8 ms de cout fixe, 309 036 o/s, soit le 2x reel du
lecteur). Ce lot-ci ne le touche pas.

Il ne survit pas pour autant: le lot suivant le retire entierement, sur decision
de l'utilisateur — le port ne simule pas de temps de chargement — et corrige a
sa place le defaut qu'il masquait, cote entree, par un verrou de manette dans
`PadInputBackend`. Voir `DISC_LOAD_LATENCY.md`.

`data/CHR_DATA/` contient six fichiers, aucun deploye: `CH_EF_P0.B`, `CRDD.B`,
`EFF_AUTO.B`, `FACE.B`, `LOAD.B`, `OV_CHR_A.B`.

## Inventaire du lot 2

Contrats desktop mesures, tous deterministes: `CdSync` rend la constante `2`
(`CdlComplete`), `CdReadSync` la constante `0`, `CdRead` et `CdRead2` rendent `0`
ou `1` selon l'etat disque seul, `CdControl`/`CdControlB` rendent `1` des le
premier essai pour toutes les commandes vivantes. Aucune de ces attentes ne peut
donc aider non plus; chacune est vacante ou bloquante.

Les lignes ci-dessous designent l'**appel** lui-meme, pas l'ouverture de la
boucle, et sont relevees sur l'arbre livre par ce lot.

- vacantes: `CdSync` (`FileIo.cs:453`, `TITLE_EXE_exe.cs:572`,
  `LoadingScreen.cs:115`, `SelectScreen.cs:328`), `CdReadSync` (`FileIo.cs:469`
  et `:473`, `TITLE_EXE_exe.cs:588` et `:592`, `LoadingScreen.cs:124`,
  `SelectScreen.cs:335`), `CdControlB(0x0e)` (`SelectScreen.cs:320`),
  `CdControl(0x15)` (`MOVIE_EXE_exe.cs:356`, `SLPS_003_55_exe.cs:429`);
- bloquantes en cas d'echec de lecture, sans `VSync`: `do { CdRead } while (i != 1)`
  (`FileIo.cs:459`, `TITLE_EXE_exe.cs:578`) et `while (CdRead2(0x1c0) == 0) { }`
  (`MOVIE_EXE_exe.cs:360`, `SLPS_003_55_exe.cs:433`) — pour `CdRead2` un second
  appel apres succes est meme destructeur, il rouvre et rembobine le flux;
- bornees mais gigantesques: les deux sondages `StGetNext` imbriques des lecteurs
  de STR, `0x800000` x `0x800000` appels au pire. Les compteurs sont a
  `MOVIE_EXE_exe.cs:256` (`DecodeNextMovieFrameVlc @ 0x80020F98`) et `:277`
  (`GetNextMovieFrame @ 0x80021020`), `SLPS_003_55_exe.cs:329`
  (`DecodeNextMovieFrameVlc @ 0x800212E4`) et `:350`
  (`GetNextMovieFrame @ 0x8002136C`). Sur console c'est l'IRQ du lecteur qui
  remplit l'anneau pendant que la boucle tourne; ce cote-la n'est pas porte.

Le troisieme compteur `0x800000` de chaque lecteur (`MOVIE_EXE_exe.cs:328`,
`SLPS_003_55_exe.cs:401`) est **hors sujet**: c'est `WaitForMovieFrameUpload`,
une attente de fin d'upload MDEC dont le corps ne contient aucun appel CD.

Ces sites remodelent quatre fonctions plus profondement que le lot 1, d'ou la
separation: `ReadCDData @ 0x80057E40` (TITLE) et son jumeau
`FUN_80061d98 @ 0x80061D98` (VS), `SeekAndStartMovieStream @ 0x80021228` (MOVIE)
et son jumeau `@ 0x80021574` (SLPS).
