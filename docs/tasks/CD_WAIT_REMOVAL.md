# Retrait des attentes disque — lots 1 a 3

Les sept boucles `do { p = CdSearchFile(...); } while (p == NULL)` du port sont
retirees. Chaque site fait desormais un appel unique et nomme le fichier absent.

Le lot 1 ne couvrait que la famille `CdSearchFile`. Le lot 3, plus bas, a ferme
tout le reste: plus aucune construction `while`/`do` du runtime translittere ne
teste la valeur de retour d'une primitive CD.

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

Le fichier existait dans le depot (`data/CHR_DATA/LOAD.B`) sans figurer dans la
liste `Content` de `DbzLegendsRemaster.csproj`. Le retrait des boucles n'a donc
pas casse le chemin de boot: il a **nomme** un defaut de deploiement que la
boucle rendait invisible depuis le debut. `TITLE_EXE_INIT_RECON.md` avait parie
l'inverse (« elle ne gelerait l'hote que si le fichier etait absent de la sortie
de build »); le pari est perdu et le document est corrige.

DEPUIS CORRIGE. Le `.csproj` deploie maintenant `data/CHR_DATA/*.B`,
`data/STG/*.B` et `data/CH_BIN1/*.BIN`, et le chemin de boot va au bout sans
exception. Le diagnostic a valu la peine d'etre fait deux fois: le symptome se
lit naturellement comme un defaut de traitement du nom de fichier — le message
cite `\CHR_DATA\LOAD.B;1`, suffixe ISO compris — alors que la traduction du
nom etait correcte de bout en bout et que seul le fichier manquait. Le resolveur
de `PsxSdkBridges` retire `cdrom:`, tronque au premier `;` et enleve le
separateur de tete avant `Path.Combine`; ce `TrimStart` n'est pas cosmetique,
sans lui `Path.Combine` jetterait la racine devant un second argument absolu.

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

## Lot 3 — le reste des attentes disque

Les sites ne meritaient pas tous le meme traitement, et c'est le tri qui a fait
le travail. Trois politiques, choisies par ce qu'un echec **signifie**.

### Silencieux — la primitive n'a aucune valeur d'echec

`CdSync` est `return CdlComplete;`, une constante 2 (`LibCd.cs:146`).
`CdReadSync` est une constante 0. Leurs boucles etaient tranchees avant de
tourner. `CdControlB(0x0E)` de `LoadUSAGI_B` et `CdControl(3)` du chemin audio
CD sont acceptes au premier essai, et la note `DEVIATION:` de chacun le prouve
par le nombre de parametres de la commande, pas par analogie.

Sites: `FileIo.cs`, `TITLE_EXE_exe.cs`, `LoadingScreen.cs`, `SelectScreen.cs`,
`CdAudio.cs`.

### Levee d'exception — l'echec est reel et le silence corromprait

Le reessai `CdRead` des deux jumeaux `ReadCDData` ne contenait pas de `VSync`:
une lecture ratee figeait la fenetre. Il nomme desormais le nombre de secteurs
et la position. Idem `CdRead2` dans les deux `SeekAndStartMovieStream`, ou
reessayer apres un succes est **destructeur** — un second appel rouvre le flux
et le rembobine.

`CdControl(0x15)` est passe du silencieux au garde pendant la relecture, et
c'est la correction importante du lot: un seek jamais emis laisse
`s_lastSeekTarget` sur la position **precedente**, et le `CdRead2` juste en
dessous arme alors le flux au mauvais endroit et joue les mauvais octets sans
la moindre erreur. C'est le seul echec du lot qui corromprait au lieu d'arreter.

### Contrat preserve, aucune exception — les quatre sondages `StGetNext`

Ici « pas pret » est routinier: **c'est ainsi qu'un film se termine.** Appliquer
le reflexe du lot 1 aurait fait planter une lecture normale. Les sondages
s'effondrent en un appel unique et rendent toujours `0` et `-1` comme
l'original.

`StGetNext` ingere ses secteurs **synchroniquement dans l'appel** — aucune IRQ
du lecteur n'est portee, `StCdInterrupt` est un stub vide — donc un seul appel
fait deja ce que la boucle de `0x800000` attendait. Et un appel refuse a deja
consomme puis jete un vrai secteur video: tourner mangerait le film au lieu de
l'attendre.

### Pieges rencontres

- Deux affectations vivantes dans des corps de boucle: `DAT_80055ae0 = 3`
  (`CdAudio`), sortie du corps ou elle etait; et le `iVar2 = 0` de
  `SelectScreen`, qui **suivait** sa boucle au lieu d'y etre.
- Le `VSync(0)` supprime avec le drain `CdReadSync` des deux `ReadCDData` n'avait
  jamais tourne: `CdReadSync` etant une constante 0, `0 < r` etait faux des la
  premiere evaluation. Aucun rendu de frame n'est perdu.

### Laisses en place, deliberement

Le `CdReadSync` en `if`/`else` de `BattleScene` est une machine a etats pilotee
par la frame, pas une attente; son bras `-1` n'est mort que parce que le stub
est constant, et le supprimer corrigerait l'original (regle 12). Les deux
compteurs `WaitForMovieFrameUpload` sont des attentes d'upload MDEC sans appel
CD. Le drain de `BandaiStrValidation` est un consommateur de test.

### Acceptation

Build propre, aucun avertissement nouveau (208 avant, 208 apres, aucun venant
des sept fichiers). Quinze invocations de bancs vertes.

Et la mesure qui compte pour les FMV: le **journal de decodage complet** d'un
demarrage scripte, capture avec et sans le changement sur la meme machine —
**identique octet pour octet, 862 lignes, meme MD5**. Pas une frame ne bouge.

Le journal, pas l'horodatage. Une premiere tentative comparait l'ecart
`MOVIE.EXE -> TITLE.EXE` en millisecondes et a fait conclure a tort a une
regression de +29 %: la mesure avait ete prise juste apres la reecriture de
157 Mo de donnees, cache disque froid. Les horodatages de ce banc dependent de
la machine; le journal de decodage, non.
