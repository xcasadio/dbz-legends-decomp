# VS.EXE — prompt de reprise pour une nouvelle session

Ce fichier est le message a coller au demarrage d'une session neuve. Il se suffit
a lui-meme : tout ce qui suit a ete mesure, pas suppose, et chaque affirmation
renvoie a une adresse ou a une commande reproductible.

**Reecrit le 2026-09-07, puis mis a jour le meme jour quand la translitteration
s'est achevee.** La version d'avant disait que l'unique maillon manquant etait
l'etape 9.3 du `FighterTask` ; c'etait faux, et la mesure l'a montre en une
commande. Ce fichier raconte la vraie chaine, l'etat final de la translitteration,
et la seule question qui reste — qui n'est plus une question de code manquant.

---

## LE MANDAT

Translitteration quasi 1:1 PSX -> C# de DRAGON BALL Z LEGENDS, recouvrement
`VS.EXE`. Le contrat est `.github/agents/runtime_port_agent.md` ; les regles qui
mordent le plus souvent :

- **1** ne pas optimiser ; **7** ne pas reordonner le flot de controle ;
  **10** n'inventer aucune semantique ; **12** ne pas corriger les bugs de
  l'original.
- **5** pas de `List<>` / `Dictionary<>` / LINQ dans le coeur.
- **6 / 11** garder `FUN_`, `DAT_`, `param_N`, `iVarN` bruts tant que la preuve
  n'est pas decisive. **Quand elle l'est, renommer — dans Ghidra ET dans le C#.**
- **13** les routines du SDK PSX ne sont pas du runtime de jeu ; dans cet overlay
  la frontiere est **0x800632C4** (`_card_load`, la premiere entree libcard
  nommee) : tout ce qui est au-dessus vit dans `PsxSdkMonogame` et s'APPELLE.
- **14** ne pas simuler le materiel PSX quand un contrat observable suffit.
- Annotations obligatoires : `// GHIDRA: nom @ 0xADDR (VS.EXE)`,
  `// JUSTIFICATION:` (uniquement *backend MonoGame* | *adaptation materiel PSX*
  | *pont langage C#*), `// RELATION:`, `// PARTIAL:`, `// BLOCKED:`,
  `// DEVIATION:`.

**Pas de simulation de lecteur CD.** Un fichier absent leve une exception qui le
nomme ; aucune boucle d'attente ni de reessai. Seule exception : `WaitDiscLoad`.

**Langues** : francais pour les reponses a l'auteur et les plans ; anglais pour
le code, les commentaires, les messages de commit, la doc et les ADR.

---

## OU EN EST LE PORTAGE — CHIFFRE, PAS ESTIME

```
python custom-tools/scripts/vs_port_coverage.py docs/tasks/VS_EXE_FUNCTIONS.tsv
```

`docs/tasks/VS_EXE_FUNCTIONS.tsv` est l'inventaire Ghidra de toutes les fonctions
de VS.EXE (adresse, nom, taille, appelants, appelees). Le script les trie en
PORTED / EMPTY / STUB / ABSENT en distinguant un vrai corps d'un `_ = param;`,
et exclut le SDK.

**VS.EXE EST ENTIEREMENT TRANSLITTERE.** Au dernier passage :

```
fonctions VS.EXE hors SDK   : 307   (164763 octets)
  PORTED   :   306 fonctions   164755 octets  100.0 %
  EMPTY    :     1 fonctions        8 octets    0.0 %
  STUB     :     0 fonctions        0 octets    0.0 %
  ABSENT   :     0 fonctions        0 octets    0.0 %
  CLOS     :   307 fonctions   164763 octets  100.0 %   (PORTED + EMPTY)
```

Le seul EMPTY est `FUN_8005d1f4` @ 0x8005D1F4, dont le corps dans l'image est
`jr ra` et rien d'autre : un corps C# vide est sa translitteration, pas une souche
de celle-ci. Le script le VERIFIE contre `data/VS.EXE` a chaque passage — il lit
les deux premiers mots de la fonction et demande 0x03E00008 puis 0x00000000 — au
lieu de tenir une liste d'adresses excusees, pour que la regle ne pourrisse pas en
alibi. Controle negatif fait : elle repond False pour le corps de 2440 octets, pour
le thunk de 32 octets, pour un corps de 304 octets, et pour une vraie fonction dont
on lui ment la taille a 8 octets.

Ce qui n'est PAS fini pour autant : voir « CE QUI RESTE » a la fin. La
translitteration est complete ; le comportement, non.

---

## LA CHAINE DE DEMARRAGE D'UN ROUND, MAILLON PAR MAILLON

Trois sessions l'ont cherchee par le mauvais bout. Elle est complete et chaque
maillon est mesurable :

1. `FUN_80055EE0` (etat 0) arme le match avec les bits 13, 14, 15 et 31 de
   `CtxFlags`.
2. Dans cet etat le corps de round ne fait **rien** : le bit 14 l'envoie sur le
   bras de recharge de ki, et le bit 13 envoie celui-la directement sur
   `LAB_80056C64`.
3. **Une pression pad (0x800 brut, R1) sur le port 1** declenche la derogation de
   `RunBattleRound` @ `0x80056358`, seul ecrivain de tout l'overlay qui efface les
   bits 12/13 et bascule le 14. Sa porte est
   `(CtxFlags & 0x80008000) == 0x80008000` — **les deux bits**, pas `== 0x8000` :
   Ghidra imprime la constante `&DAT_80008000` et le port l'avait lue 0x8000.
4. Le bras de balayage de legalite marque les dossiers de creneau a 0x280 et pose
   `CtxRoundRequest |= 3`.
5. `RunFighterSubstitution` @ `0x80026D98` (porte sur `CtxRoundRequest & 0x180`)
   appelle `ActivateFighterInSlot` @ `0x80027340`, **seul ecrivain d'un +0x144 non
   nul** — et ce n'est pas un drapeau : c'est le pointeur de donnees de personnage,
   lu dans `ctx + 0x16A0 + creneau*4`.
6. `RunBattleCameraTask` @ `0x80027670` (tache 0x55, liste 0x13) joue un travelling
   d'environ 0x50 frames et, sur son dernier tic, efface `CtxFlags` bit 31
   (`and`/`sw` @ `0x80027A7C`/`0x80027A80`) puis le bit 25 du +0x138 de chaque
   combattant. C'est ce bit 25 qui supprimait l'etape 9.3.

**Le banc doit presser le bouton** :

```
DBZ_PAD_PRESS_MASK=0x800 DBZ_PAD_PRESS_FRAME=300 \
  dotnet run --project custom-tools/DbzLegendsAnalyser/DbzLegendsRemaster -- --diag-vs 900
```

Sans pression, le banc mesure une machine **qui attend**, pas une machine cassee.

---

## LE BANC DOIT AUSSI ENSEMENCER LE RELAIS

`--diag-vs` demarrait VS.EXE avec tout le bloc partage a `0x801FF000` a zero. Deux
morceaux du relais manquaient, chacun suffisant pour tout bloquer :

- **le roster** : `Roster.FUN_8005CBE0` lit six identifiants a
  `0x801FF102..0x801FF10C` (indices courts 0x81..0x86), ecrits par SELECT.EXE.
  A zero, aucun creneau n'est marque et aucun combattant n'est active ;
- **les tables de remappage du pad** : `FUN_8002165C` de SLPS_003.55 en est le seul
  ecrivain, et `VS_EXE/PadInput.cs` les applique au mot que tout le jeu lit — a
  zero, le pad remappe est mort quoi qu'on presse.

Le banc execute maintenant le vrai bootstrap puis ecrit le mode et les six
identifiants comme SELECT.EXE. Les six valent 1..6 par defaut, ce qui est un choix
de fixture et est imprime a chaque run.

---

## LA BOUCLE DE VERIFICATION

```
bash custom-tools/scripts/vs_acceptance.sh
```

Elle fait tout : build, les treize bancs, les cinq verificateurs de couture, le
temoin `--diag-select 400` et, depuis la fin de la translitteration, la chaine de
round elle-meme. Criteres, tous a chaque etape :

- build propre ;
- **13/13** bancs ;
- **5/5** verificateurs de couture ;
- `--diag-select 400` = **49396 pixels**, inchange ;
- `--diag-vs 900` avec R1 a la frame 300 : phases 2..9 a **1200** entrees chacune,
  dessineur de sprites **15120 appels / 7198 quads**.

Les deux variables d'environnement de ce dernier temoin SONT la mesure, pas un
detail : `DBZ_PAD_PRESS_MASK=0x0800` est R1 (bit 11 du pad PSX) et la frame 300
laisse 600 des 900 frames a l'interieur du round. Chaque compteur ci-dessus est
lineaire en cette frame — presser a 360 donne 1080, a 420 donne 960. Changer l'un
des deux nombres change tous les temoins.

Les verificateurs, dans `custom-tools/scripts/` :
`check_duplicate_symbols.py`, `check_overlay_handover.py`,
`check_task_registration.py`, `check_vs_dispatch.py`, et
**`check_function_addresses.py`** (nouveau) qui echoue quand une adresse Ghidra est
DECLAREE dans deux fichiers — il distingue une declaration d'un simple renvoi en
commentaire. Il a trouve un vrai defaut a son premier passage.

`--diag-vs [frames] [mode] [id0..id5]` rapporte : appels du repartiteur, visites de
`CtxState`, `CtxFlags`, les quatre conditions du bit 3, les douze contributions de
jauge, les dossiers de creneau, `CtxRoundRequest`, l'echelle de phases de
`UpdateFighter`, la source du mot de commande de l'etape 9.3, les mots produits, le
dessineur de sprites (appels / quads soumis / pool plein / paquet non resolu), et
la chaine de jauge. **S'en servir plutot que raisonner.**

---

## LES QUATRE CLASSES DE DEFAUTS QUI SE REPETENT

Les mettre dans chaque brief.

1. **Une adresse Ghidra declaree dans deux fichiers.** C# lie un appel non
   qualifie a la classe englobante d'abord : la souche vide bat le vrai corps.
   `check_function_addresses.py` l'attrape maintenant ; il l'a fait pour
   `0x80053970` (corps dans FighterCombat, souche vide dans AnimCmdEffects, et le
   bras de re-armement d'AnimCmd_EffSet appelait la souche) et pour
   `0x80045F34` (`ComputeYawPitchToTarget` porte en entier mais `private`, donc
   deux appelants recevaient un bloc de zeros).
2. **Du travail supprime.** Stores inconditionnels perdus, boucle raccourcie, un
   appel sur deux d'une paire. Compter les sites d'appel par appelee dans l'image
   et confronter.
3. **`CreateTask` sans `RegisterCallback`.** La tache est creee, dispatchee dans
   aucun sens, et rien ne le dit. C'est ce qui a cache la camera de combat pendant
   trois sessions. `check_task_registration.py` liste l'etat : VS_EXE n'a plus
   aucune entree ECHEC — chaque `CreateTask` dont le corps est porte a son
   `RegisterCallback`. Les lignes BLOCKED restantes sont des entrees dont aucun
   corps n'est porte dans CET overlay, et leur absence est attendue.
4. **Largeur de chargement et signe.** `PsxRam` n'expose que `ReadU8`/`ReadU16`/
   `ReadI32` et `WriteU8`/`WriteU16`/`WriteI32` ; une lecture 16 bits signee
   s'ecrit `(short)PsxRam.ReadU16(addr)`. Et **`sltiu` n'est pas `slti`** : la
   comparaison non signee de la porte de rotation du travelling
   (`0x80027A20`) avait ete ecrite signee, ce qui sautait toute la moitie basse du
   cercle. Un bras de `if` peut rester inverse meme quand le commentaire d'en-tete
   decrit la version inversee : le commentaire a ete ecrit apres la melecture.
   Enfin, quand Ghidra imprime une constante nue comme `&DAT_xxxxxxxx`, c'est
   l'ADRESSE, pas un nombre plus petit.

---

## LES CANAUX DE PREUVE

- **ReVa MCP -> Ghidra** — chemin de programme exactement `/VS.EXE`.
- **L'image sur disque**, `data/VS.EXE` : PSX-EXE, chargement `0x80020000`,
  en-tete `0x800`, donc `offset = adresse - 0x80020000 + 0x800`. Decoder les
  instructions avec un petit script Python est le canal le plus sur, et c'est
  celui qui a tranche chacune des corrections ci-dessus.
- **PCSX-Redux MCP** — pour lire la RAM de l'original en marche. `gp = 0x8008D0FC`.
- `LibGpu.RamRegion(adressePsx, taille)` declare les octets porteurs ;
  `LibGpu.RamResolve(addr, out byte[] buf, out int off)` fait le pont.
  **Agrandir une region, jamais la retrecir** — sauf quand une tranche ulterieure
  prouve qu'elle en chevauche une autre, ce qui est arrive au tampon CD
  `0x80110000` : borne a `0x801C1000` puis corrigee a `0x801B6000` quand le pilote
  de son a fait apparaitre trois tampons a l'interieur.
  **Piege C#** : une `RamRegion` ne s'enregistre qu'au constructeur statique de sa
  classe, execute au premier ACCES — pour un corps de tache, la premiere
  distribution, pas le `RegisterCallback`. `VS_EXE_exe.ArmRegions` force celles qui
  en ont besoin.

---

## LA DISCIPLINE GIT

- **Ne jamais committer sur `main`** : un hook de niveau utilisateur le refuse, et
  il refuse **tout l'appel Bash**. Donc `git checkout -b` doit etre un appel Bash
  **separe** de `git commit`.
- Sequence : branche -> commit -> `git checkout main` -> `git merge --ff-only`.
- Sous-module `custom-tools/PsxSdkMonogame` : meme hook a l'interieur. Le brancher,
  y committer, fusionner sur son `main`, **puis** bouger le pointeur du
  super-projet.
- Indexer fichier par fichier (`git add <chemin>`), jamais `git add -A` ni `.`.
- **Ne jamais pousser.** L'auteur pousse lui-meme.

---

## DELEGUER UNE TRANCHE

Le frein du depot interdit la delegation par defaut, mais la translitteration
bornee s'y prete quand le brief a exactement cette forme :

- l'agent ecrit **UN SEUL fichier neuf** et il lui est interdit, en gras, d'en
  toucher un autre ;
- on lui donne le contrat, deux ou trois fichiers a lire pour le style et pour les
  symboles a reutiliser, et la liste des symboles deja portes a appeler qualifies ;
- on lui donne **les quatre classes de defauts mot pour mot** ;
- il doit lancer le build et `check_function_addresses.py` avant de rapporter, et
  s'il reste un conflit qui ne se resout qu'en editant un fichier interdit, il le
  LAISSE et le dit ;
- il rapporte en 25 lignes et ne colle aucun code.

La session principale garde le cablage, la qualification des sites d'appel et
toutes les decisions d'architecture. La liste « ces N adresses demandent une
edition d'une ligne dans un fichier que je n'avais pas le droit de toucher » EST la
tache de cablage.

---

## CE QUI RESTE

**Plus rien a translitterer.** Les 307 fonctions de jeu de VS.EXE ont un corps.
Les trois dernieres etaient bloquees sur des questions, pas sur du code :

- `BuildSlotDigitQuads` @ 0x80058338 (2440 o, ex-`FUN_80058338`) attendait de
  savoir si ses deux tables de pointeurs — `PTR_DAT_80083fb4` et
  `PTR_DAT_80084124` — visaient de la donnee figee ou de l'etat mutable, parce que
  recopier des octets de l'un ou de l'autre aurait ete inventer de la semantique.
  **La question n'avait pas a etre tranchee** : `PsxExeImage` porte deja les
  0xE5800 octets de l'image entiere, les deux tables et leurs cibles sont dedans,
  donc chaque lecture est un `PsxRam.ReadI32` a l'adresse que l'image lit — et si
  une fonction ecrit ces mots a l'execution, elle ecrit LES MEMES OCTETS. Rien
  n'est incorpore. **La lecon generale : avant de se demander s'il faut incorporer
  une travee, verifier si elle est deja portee par l'image.**
- `FUN_800261ec` @ 0x800261EC (304 o) etait la forme courte d'une marche que
  `FighterAi.cs` et `FighterCombat.FUN_80025f38` avaient deja fermee instruction
  par instruction.
- `SpuInit` @ 0x800617E0 etait une **mauvaise etiquette de Ghidra** : 32 octets
  dont tout le corps est `jal ProcessPadInput` avec a0 = 0. Renomme
  `ProcessPadInputPort0` des deux cotes.

**La question qui reste est comportementale, et le banc la pose deja.** Au dernier
passage (`--diag-vs 900`, R1 a la frame 300) :

```
ETAPE 9.3   pad port 1 : 120   pad port 2 : 120   IA : 480   sortie -1 : 0
            mots de commande vus : 0x00 x813   0x02 x283   0x21 x104
IA          entrees:480   echauffement:8   corps atteint:472
JAUGE       UpdateFighter 1788 | racine A 0 | racine B 0 | semeur 0
```

Un piege de sonde a ete corrige en passant, et il vaut d'etre connu :
`CtxRoundRequest cumule` affichait 0x00000000 alors que la porte 0x180 etait
franchie. Les deux ne se contredisaient pas — les bits 0x180 sont poses ET effaces
dans la meme frame (l'effacement est quatre lignes sous la porte), donc un
echantillon pris une fois par frame a l'entree du manager ne pouvait pas les voir.
**Une sonde qui echantillonne au mauvais endroit accuse le portage a tort.**

Tout tourne : l'IA atteint son corps 472 fois sur 480, l'etape 9.3 route les trois
sources, le dessineur soumet 7198 quads. Mais **aucune opcode d'attaque 0x23..0x28
n'est produite**, donc les deux racines de la chaine de jauge — `FUN_8004ee48` et
`FUN_8004e758` — ne sont jamais appelees et `AddSlotGaugeContribution` non plus.

Ce n'est plus « il manque une fonction ». **C'est un etat de combattant qui n'est
jamais atteint, et la chaine est tracee jusqu'a son dernier maillon connu** —
mesuree avec des sondes, pas raisonnee. Le banc imprime maintenant :

```
LA PORTE DU BRAS D ATTAQUE DE L IA (+0x138 bit 0x10, FighterAi.cs:600):
 FUN_8004b9cc appelee : 0   FUN_800261ec appelee : 0   dont -1 precoce : 0
 FUN_8004a9e8 (seul ecrivain du bit 0x10) appelee : 0   dernier local_10 : -1
```

**LA CHAINE, DE L'OPCODE D'ATTAQUE VERS L'AMONT.** Chaque fleche est un seul
ecrivain ou un seul appelant, verifie par `find-cross-references` :

1. La jauge veut une opcode 0x23..0x28. Seul `FUN_8002631c` en produit.
2. Le ladder de l'IA qui l'appelle (`FighterAi.cs:600`) est garde par le **bit 0x10
   du +0x138** du combattant.
3. Le seul ecrivain du bit 0x10 de tout l'overlay est **`FUN_8004a9e8` @ 0x8004A9E8**
   (`FighterAction.cs`). Mesure : appele **0 fois**.
4. Il n'est atteint que depuis `FUN_8004b9cc` (0x8004B9CC), et seulement quand cette
   fonction obtient un `local_10` de 0x23..0x25 — que lui donne `FUN_800261ec`, la
   fonction fermee a la derniere session. Mesure : `FUN_8004b9cc` appelee **0 fois**.
5. `FUN_8004b9cc` a **un seul appelant**, `FUN_8004c198` @ 0x8004C198 (l'etape 9.4 de
   `FighterTask.cs:325`), et il n'y route que quand le **bit 0x20** est pose.
6. Le seul ecrivain du bit 0x20 est **`FUN_8004b33c` @ 0x8004B33C**, atteint depuis
   `FUN_8004bb70` quand la commande vaut 0x2A — et `FUN_8004c198` ne route vers
   `FUN_8004bb70` que quand le **bit 0x08** est pose.
7. Le bit 0x08 vient de `FighterCombat.FUN_8004a97c` @ 0x8004A97C.

**OU CA S'ARRETE, MESURE.** `+0x138 cumules : 0x70040006` : les bits 1 et 2 sont
poses, **et ni 0x08, ni 0x10, ni 0x20 ne le sont jamais**. Donc `FUN_8004c198`
prend systematiquement son dernier bras, `bits 6 != 0` -> `FUN_8004bf50`, et le
combattant tourne en rond dans l'etat « bits 1|2 » sans jamais entrer dans la
sequence d'action.

**LA PROCHAINE QUESTION, precise :** qui pose et qui efface les bits 1 et 2 du
+0x138 (`FighterAction.cs:58` et `:71` sont les ecrivains), et quelle entree fait
sortir un combattant de cet etat. C'est la meme forme de question que la chaine de
demarrage de round : un seul ecrivain, un seul appelant, une sonde par maillon.
**Ne pas raisonner : les sondes de `FighterAction.cs` sont deja en place, en
ajouter une par bras de `FUN_8004c198` et relancer.**

Deux pistes secondaires si celle-la se ferme :

1. **La distance.** `BattleCamera.cs:511` compare une separation a `0x2C1` et les
   combattants demarrent aux bornes de l'arene.
2. **Une vraie entree joueur.** `DBZ_PAD_PRESS_MASK` ne presse qu'un bouton pendant
   deux frames. Une sequence d'attaque du decodeur de `FighterInput.cs` (la boussole
   a huit points des boutons de face) n'a jamais ete jouee au banc.

Le reste du travail utile n'est plus de la translitteration :

- la tache de scene n'a jamais tourne (`appels au repartiteur de scene : 0`), et le
  diagnostic dit ou chercher : creation, enregistrement, ou parcours de liste ;
- les 26 avertissements du build sont des globaux declares et jamais lus, chacun
  attendant un lecteur qui est ailleurs ou nulle part ; les passer en revue une
  fois dirait lesquels sont de vrais trous.

---

## LES RENOMMAGES FAITS DES DEUX COTES

L'instruction permanente est : quand la preuve est decisive, renommer **dans Ghidra
ET dans le C#**. Faits a la derniere session :

| adresse | avant | apres |
|---|---|---|
| 0x80052DB4 | `FUN_80052db4` | `DrawSpriteGroup` |
| 0x80045CF4 | `FUN_80045cf4` | `DistanceBetweenPositions` |
| 0x80058338 | `FUN_80058338` | `BuildSlotDigitQuads` |
| 0x80061800 | `FUN_80061800` | `ProcessPadInput` |
| 0x800617E0 | `SpuInit` (faux) | `ProcessPadInputPort0` |

Verifier l'accord des deux cotes est mecanique : une annotation
`// GHIDRA: nom @ 0xADDR (VS.EXE)` dont le `nom` ne correspond plus au symbole
Ghidra est un desaccord, et `vs_port_coverage.py` ne le voit pas — il apparie sur
l'ADRESSE, pas sur le nom, justement pour que le renommage soit possible.
