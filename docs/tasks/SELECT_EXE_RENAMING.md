# SELECT.EXE - le renommage, et ce que quatre filtres ont retire

63 symboles nommes: 61 fonctions et 2 globales, dans Ghidra **et** dans le portage C#.

Le nombre est l'information la moins interessante de ce document. Ce qui compte est ce qui a ete
**retire**, et par quoi.

## Ce qui a ete nomme

| famille | noms |
|---|---|
| carte memoire, sauvegarde | `ResetCardOperationState`, `LoadSaveRecords`, `RunSaveLoadFlow`, `RunSaveWriteFlow`, `ProbeMemoryCard`, `RepollMemoryCard`, `QueryCardStatus`, `ShowCardMessage` |
| son | `InitializeSoundSystem`, `UpdateSound`, `StepBgmState`, `RequestBgmPlay`, `RequestBgmStop`, `PlaySoundEffect`, `ShutdownSoundSystem`, `RunSoundTestScreen`, `RunSoundTestMenu` |
| CD-DA | `InitializeCdAudio`, `StopCdAudio`, `PlayCdCurrentTrack` |
| ecrans, paires build / unwind | `BuildDemoSaveSlotScreen` / `UnwindDemoSaveSlotScreen`, `BuildSpSaveSlotScreen` / `UnwindSpSaveSlotScreen`, `BuildOptionsScreen` / `UnwindOptionsScreen`, `BuildButtonConfigScreen` / `UnwindButtonConfigScreen`, `BuildModeMenuScreen` |
| menus | `RunOptionsScreen`, `RunButtonConfigScreen` |
| manette | `InitializePadRemapTablePointers`, `PadMaskToButtonIndex` |
| libsnd | `SsEnd`, `SsSeqOpen`, `SsSetMVol`, `SsSetMono`, `SsSetStereo`, `SsSetSerialVol`, `SsSetSerialAttr`, `SsSetTableSize`, `SsSetTickMode`, `SsUtKeyOnV`, `SsUtKeyOffV`, `SsUtReverbOn`, `SsUtReverbOff`, `SsUtSetReverbType`, `SsUtSetReverbDepth`, `SsUtSetReverbDelay`, `SsUtSetReverbFeedback`, `SsVabClose`, `SsVabOpenHeadSticky`, `SsVabTransBody`, `SsVabTransBodyPartly`, `SsVabTransCompleted` |
| libspu, libgs, noyau | `SpuQuit`, `GsDefDispBuff`, `GsSetWorkBase`, `GetVideoMode`, `EnterCriticalSection`, `ExitCriticalSection` |
| globales | `g_CdSetmodeParam`, `g_OptionsCursor` |

Tout le reste garde son nom brut. C'est le mandat du depot: la ou la semantique n'est pas fermee, un
`FUN_` honnete vaut mieux qu'un nom invente.

## Pourquoi la methode compte plus que le compte

`create-label` n'a pas d'inverse ici. ReVa n'a ni `delete-label`, ni `remove-symbol`, ni
`rename-symbol`, et PyGhidra est indisponible. **Un nom faux est definitif**, retirable seulement
depuis l'interface graphique. Rien n'a donc ete ecrit avant que la preuve ait passe quatre filtres.

### Filtre 1 - sept relevés en lecture seule

Sept surfaces disjointes: l'ecart entre le portage C# et Ghidra, trois plages de code jeu, les
globales, le residu de bibliotheque Sony, les structures. Interdiction nominative d'ecrire.
**170 propositions, dont 121 classees CERTAIN.**

### Filtre 2 - la refutation adverse

Des agents en contexte frais, charges non pas de confirmer mais de **REFUTER**, avec pour consigne
de re-deriver chaque fait depuis Ghidra sans faire confiance au texte de preuve recu, et de refuser
en cas de doute. **19 rejets.** Deux valent d'etre cites:

- **`g_GsLineArray4`**: le refutateur a **reproduit lui-meme** la contamination par base
  compilateur plutot que de la croire sur parole. `search-decompilation` lui a rendu 18 lignes qui
  se separent en deux familles incompatibles.
- **`SpuInitHot`**: le nom etait **deja pose** sur `0x8003a050`. Un doublon definitif evite.

Deux etiquettes psyq ont aussi ete refusees parce qu'attribuees sur le « plateau » de suggestions de
Ghidra pour des corps de seize octets trop peu distinctifs pour trancher.

### Filtre 3 - le jugement de la session principale

Huit retraits de plus, qu'aucun verdict n'imposait:

| retire | motif |
|---|---|
| `g_SoundIsMono` | deux refutateurs en desaccord; un verdict partage est un non |
| `UnwindVsSubMenuScreen` | le mot « SubMenu » reposait sur la fonction *Build* jumelle, que la refutation venait d'ecarter |
| `g_LastVsUnlockTier` | « UnlockTier » empruntait a un symbole retire une heure plus tot |
| `g_UnlockTier` @ 0x801FF002 | RAM haute partagee, modelisee une seule fois en C# pour trois overlays: la nommer dans un seul programme creerait une divergence |
| `UpdateSatelliteSpritePositions` | « Satellite » est une lecture, pas un fait |
| `PlayOverlayHandoffTransition` | affirme une intention |
| `InitializeGraphicsAndSpriteTables` | nom conjonctif et incomplet: la fonction fait aussi `CdInit`, que le nom tait |
| `PrepareAndRunModeMenu` | la preuve ne couvre que huit ecritures de sprites, pas un « Run » |

Tous relevent de la meme classe: **le nom dit plus que la preuve**. C'est le seul defaut qu'aucune
procedure ne rattrape, puisqu'il ne produit aucune erreur - seulement une affirmation fausse qui
survit.

Les deux premiers sont particulierement instructifs: ce sont des **emprunts silencieux**. Un nom qui
tire son sens d'un autre symbole non prouve herite de son incertitude sans le dire.

### Filtre 4 - deux revues de plan, toutes deux justes

**Revue 1, quatre blocages.** Aucun controle de collision n'existait avant la premiere ecriture
irreversible - et l'adresse que j'avais choisie pour la sonde, `EnterCriticalSection`, etait
elle-meme parmi les noms les plus exposes a la collision, ce qui aurait melange deux modes d'echec
dans la meme sonde. Une seule sonde precedait 69 ecritures sans controle intermediaire, alors que le
programme **contient de vrais doublons** (`0x800206A8` et `0x800206CC` portent deux labels chacune):
la preuve etait faite que `create-label` peut en produire.

Le blocage le plus fin portait sur le temoin. `metadata.signature` vaut
`undefined FUN_xxx(void)` avec `parameterCount: 0` sur **toutes** les fonctions de cette image, y
compris saines: un avant/apres identique sur ce champ est compatible avec des parametres effaces. Le
temoin est passe a `decompSignature` et aux listes d'arguments **cote appelant**.

**Revue 2, deux blocages**, tous deux de la classe « le nom dit plus que la preuve ». Les deux
entrees ont ete retirees, pas reecrites: leur inventer un nom plus etroit sans re-derivation aurait
ete exactement l'invention que la regle 11 interdit.

Le lot est passe de **70 a 65 puis a 63**. Aucune entree n'a jamais ete ajoutee en cours de route.

## Le controle prealable, et ce qui en fait une preuve

Aucune collision sur les 70 noms alors vises. Ce negatif vaut quelque chose parce que la methode a
ete **validee contre un oracle**: neuf temoins positifs, dont la collision connue
`SpuInitHot -> 0x8003a050`, qu'elle a correctement detectee. Un negatif prouve, non presume.

Aucun ecart d'etat non plus, et aucune adresse ne portait deja plusieurs symboles - le relevé
aurait su les voir, les onze `caseD_*` empiles sur `0x800215ec` ayant bien ete vus.

## Ce que la sonde a corrige dans son propre plan

Le plan affirmait que le compteur total passerait de 5321 a 5384, `create-label` etant reputé
**ajouter** un symbole.

C'est faux sur un symbole au nom par defaut: il **renomme**. Mesure apres les deux sondes, le total
est reste a **5321** et c'est le compteur non-defaut qui a bouge, de 3153 a 3155.

Le critere d'acceptance aurait donc declenche un **faux arret a la premiere mesure**. C'est
exactement ce a quoi sert une sonde: corriger le plan, pas le confirmer.

Etat final: total **5321**, inchange; non-defaut **3216**, soit 3153 + 63 exactement.

## Les temoins, intacts

C'est le point qui importait le plus, parce que la campagne precedente sur TITLE.EXE renommait par
`set-function-prototype` et avait **efface la liste de parametres de 10 fonctions sur 18**. Cette
campagne ne l'a jamais appele.

| temoin | avant | apres |
|---|---|---|
| `0x80023640` | 3 parametres | `int StepBgmState(CdlLOC *, ushort, short)` |
| `0x80025088` | 3 parametres | `undefined4 PlaySoundEffect(uint, ushort, short)` |
| `0x80039038` | 8 parametres | `int SsUtKeyOnV(...)`, 8 |
| `0x8003af3c` | 2 parametres | `void SsSetMVol(short, short)` |
| `0x8004879c` | 4 parametres | `void GsDefDispBuff(undefined2 x4)` |
| cote appelant | `FUN_80023640(&DAT_80055b88, ..., 0)`, 3 arguments | `StepBgmState(&DAT_80055b88, ..., 0)`, 3 |
| cote appelant | `FUN_80039038(...)`, 8 arguments, deux sites | `SsUtKeyOnV(...)`, 8, deux sites |

## Cote portage

282 occurrences sur 16 fichiers. Chaque annotation `// GHIDRA:` a suivi son symbole - une annotation
qui nomme un symbole que Ghidra ne porte plus est un mensonge que le compilateur ne peut pas
attraper, et ce portage en a deja produit trois. Huit bancs verts.

### Ce que la verification a refute, et qu'il fallait corriger

La verification de resultat a rendu **REFUTED**. Les trois premiers points - noms poses, aucun
doublon, signatures intactes - ont ete confirmes par mesure, dont une enumeration des 63 adresses
une a une plutot qu'un echantillon. Le quatrieme a echoue sur deux defauts, tous deux les miens.

**La purge s'etait arretee a une frontiere de projet.** Le script balayait `DbzLegendsRemaster` et le
SDK; il n'a jamais vu `custom-tools/DbzLegendsAnalyser/DbzLegendsAnalyser/`, ou le visualiseur
gardait **62 occurrences** de sept anciens noms dans ses chaines de provenance. J'avais enonce
« aucun ancien nom ne subsiste » comme un absolu alors que ce n'etait vrai que des repertoires
scannes. Le defaut n'est pas dans le script: il est dans la portee que je lui ai donnee, et dans
l'affirmation que j'en ai tiree.

**Et le renommage a lui-meme fabrique une annotation perimee.** Trois commentaires disaient « Ghidra
leaves it unnamed » a propos de `GsDefDispBuff` et `GsSetWorkBase` - vrai a l'ecriture, faux des que
la campagne les a nommes. Dont un dans un fichier que ce meme commit venait de modifier.

C'est exactement la classe de defaut qui a deja piege ce portage trois fois: **une phrase vraie
quand elle a ete ecrite, fausse quand on la relit**. Le compilateur ne la voit pas, les bancs non
plus. Seule une relecture adverse la voit.

La verification a aussi releve une limite honnete de sa propre mesure: un renommage accidentel d'un
symbole **deja** non-defaut hors liste resterait invisible au controle par compteur, puisqu'il ne
changerait aucun des deux totaux. Non detecte, mais non exclu non plus.

Un controle a ete fait avant d'y toucher: les overlays se chargent a des adresses qui se recouvrent,
donc un `FUN_800213b8` de TITLE.EXE serait une **autre** fonction au meme nom textuel. Un seul
symbole de la liste apparaissait hors du dossier SELECT_EXE de facon dangereuse - `DAT_801ff002`,
justement celui qui a ete retire pour cette raison.

## Ce qui reste ouvert

- **26 applications de type** (`RECT`, `u_char[8]`, `GsOT[2]`, `GsBOXF[5]`, six `CdlFILE`,
  `GsOT_TAG[8]`...): un lot separe, car `apply-data-type` est une operation differente, avec ses
  propres risques - notamment sur une adresse portant deja un label interieur.
- **Les huit retires**, tous nommables plus tard avec la preuve qui manque.
- **`0x800206A8` et `0x800206CC`**, qui portent chacune deux labels, residu de l'ajout non
  destructif: `g_UsagiChunk18TileIndexMap36` et `...35`, `g_UsagiSelectionValueMap36` et `...35`. Le
  C# et le label primaire de Ghidra tranchent tous deux pour la forme « 35 »; les deux « 36 » sont
  obsoletes. C'est un nettoyage, faisable seulement depuis l'interface graphique.
