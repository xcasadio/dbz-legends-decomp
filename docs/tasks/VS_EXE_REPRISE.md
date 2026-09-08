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

**LA CHAINE, CORRIGEE PAR LA MESURE.** Une version precedente de ce paragraphe
donnait une chaine ou `FUN_8004c198` prenait toujours son dernier bras et ou le
bit 0x20 passait par `FUN_8004bb70` puis `FUN_8004b33c`. **Les deux etaient
faux**, et c'est instructif : ils avaient ete deduits d'un etat cumule
(`+0x138 = 0x70040006`) plutot que comptes. Six investigations en contexte neuf,
chacune attaquee par deux refutateurs, n'ont trouve **aucun defaut de
translittération** sur six surfaces ; tout est soit du comportement fidele, soit
une entree que le banc ne savait pas produire.

Ce que les compteurs disent vraiment :

- `FUN_8004b098` est appele **997 a 1104 fois**, pas zero. Le masque
  `+0x138 & 0x200FF` vaut zero a chaque visite ou presque ; les bits « jamais
  clairs » sont `0x00000`. Le routeur n'est pas le blocage.
- **La commande 0x2A est morte dans cette image.** Son unique porte est le bit
  0x100000 du `+0x138` d'un combattant, et rien ne le pose : les onze
  `lui rt,0x0010` du code de jeu sont tous des `and` sauf celui de 0x80056EFC, qui
  fait `sw v0,0x10(s2)` — le mot de CONTEXTE, pas un combattant. Donc le maillon
  `FUN_8004bb70 -> FUN_8004b33c -> bit 0x20` est une impasse, et le bit 0x20 aussi.
- **Le vrai amorcage est le bras par defaut de `FUN_8004b098`** : commande
  0x26/0x27/0x28 -> `FighterCombat.FUN_8004a97c` -> `+0x138` bit 0x08.
- **La distance n'est pas en cause.** La formation de depart gravee en ROM a
  0x80083DBA donne 320 unites de separation pour l'appariement par defaut, 480 au
  maximum sur toutes les paires, contre un seuil de 0x2C1 = 705. La porte passe
  des la premiere frame, sans aucun deplacement.

**CE QUI A ETE OBTENU.** Le banc ne pouvait exprimer qu'un seul appui de deux
frames. Or les deux evenements d'un combat sont a des centaines de frames l'un de
l'autre et aucun ne se deplace : R1 doit tomber tot (la derogation de round a
0x80056358), l'attaque bien plus tard (un combattant n'est pilote au pad qu'une
fois le mot de contexte a ctx+0x10 porteur du bit 0x100000, pose par le bras de
round a 0x80056EFC). Mesure : appels pad **0 a 500 frames, 4 a 550, 54 a 700, 120
a 900**, tous avec le meme appui a la frame 300.

`DBZ_PAD_SCRIPT` remplace donc l'appui unique par une suite `frame:masque` :

```
DBZ_PAD_SCRIPT="300:800,540:1020,542:0" \
  dotnet run --project custom-tools/DbzLegendsAnalyser/DbzLegendsRemaster -- --diag-vs 900
```

En tapant RIGHT|TRIANGLE toutes les huit frames de 540 a 890, **la commande 0x26
apparait 112 fois** — une valeur que ce portage n'avait jamais produite — et
`FUN_8004a97c` s'execute **5 fois avec l'opcode 0x26**. La sequence d'action est
amorcee pour la premiere fois.

**LA CHAINE COMPLETE VERS LA JAUGE, TRACEE JUSQU AU BOUT.** Elle a DEUX racines et
elles ne partagent aucune porte. Une version precedente de ce fichier n'en voyait
qu'une et en tirait une conclusion trop forte ; voici les deux, chacune mesuree.

**Racine B, `FUN_8004e758`** — ses QUATRE sites d'appel (l'etape 9.6 de
`FighterTask.cs`, et deux sites plus un dans `FUN_80050824`) sont derriere **la
meme** porte : `+0x134 & 0x80000000`. Or ce bit n'est jamais pose. La preuve est
statique et complete :

- 31 instructions `sw rt,0x134(rs)` dans l'image ; **3 sont des sauvegardes de
  pile** (`sw ...,308($sp)` dans des prologues) et ne concernent aucun combattant.
- Des 28 restantes : **2** ecrivent `$zero` ; **19** sont des
  lectures-modifications-ecritures du MEME mot (`lw ...,0x134` puis `and` avec un
  masque) — elles preservent le bit 31, elles ne peuvent pas le creer ; **7** sont
  des `or` dont les masques sont 0x02000000, 0x20000000, 0x08000000 et 0x04000000,
  **aucun n'atteint le bit 31** ; la derniere preserve les 24 bits hauts et n'ecrit
  qu'un octet bas.
- Et aucune ecriture etroite ne l'atteint non plus : **zero** `sb` sur +0x137,
  **zero** `sh` sur +0x136, **zero** `swl`/`swr` chevauchant 0x134..0x137.

Donc la racine B est du code mort dans ce build. **Cette conclusion prouve trop si
on s'arrete la** — un jeu commercialise dont la jauge ne peut pas se remplir n'a
pas de sens — et c'est justement ce qui a fait chercher l'autre racine.

**Racine A, `FUN_8004ee48` — c'est le vrai chemin.** Elle n'a qu'UN appelant,
`UpdateAttackEventTask` @ 0x800429A8, garde par `*(int*)(record + 0x78) < 0`, ce
qui n'a **rien a voir** avec `+0x134`. La chaine complete, chaque maillon compte
par une sonde :

```
AnimCmd_ChDanSet (opcode 40 de la VM d animation) jouee : 0   <-- LE FRONT
  -> CreateAttackEventTask appelee : 0
  -> UpdateAttackEventTask appelee : 0
  -> +0x78 negatif : 0
  -> FUN_8004ee48 (racine A) : 0
  -> AddSlotGaugeContribution (le semeur) : 0
  -> jauge centrale jamais a +/-30000 -> le round ne finit pas
  -> la tache de scene n a pas lieu d exister
```

`AnimCmd_ChDanSet` (`AnimCmdEffects.cs`) est le SEUL appelant de
`CreateAttackEventTask`, qui est le SEUL producteur de la tache d'evenement dont le
`+0x78` negatif est la SEULE porte de la racine A. Tout tient a une question :
**l'interpreteur d'animation ne joue jamais l'opcode 40.**

**A FAIRE ENSUITE, dans cet ordre :**

1. Le combattant atteint bien l'etat 0x26 (mesure : `FUN_8004a97c` s'execute 5 fois
   avec cet opcode). **La question est de savoir si atteindre l'etat 0x26 change
   reellement le flux d'animation joue.** Sonder l'interpreteur : quel flux tourne,
   pour quel etat, et le flux d'attaque du personnage contient-il l'opcode 40.
2. C'est une question de DONNEES autant que de code : les flux viennent des CH_BIN.
   `docs/structure-ch-bin-files.md` decrit le format. Si le flux d'attaque n'est
   jamais charge, le probleme est en amont du VM.
3. Cinq executions de `FUN_8004a97c` sur 112 commandes 0x26, c'est peu : la
   coincidence exigee entre << le routeur laisse passer >> et << la commande vaut
   0x26 >> merite d'etre comprise. Un appui plus dense, ou tenu, changerait
   peut-etre le compte.
4. `MatchFacingFaceThenOppositeFace` veut deux fronts de boutons de face DIFFERENTS
   espaces de 1 a 4 frames, pour produire 0x28. Le script d'appui sait l'exprimer ;
   personne ne l'a essaye.

**UNE LECON DE METHODE, deux fois payee cette session.** Deux chaines ecrites ici
etaient fausses parce qu'elles avaient ete DEDUITES d'un mot de drapeaux cumule au
lieu d'etre COMPTEES : `FUN_8004b098` tourne mille fois et non zero, et la commande
0x2A est morte (rien ne pose le bit 0x100000 d'un `+0x138`). Un OR cumule dit qu'un
bit a ete vu, jamais combien de fois ni ou. **Compter avant de conclure.**

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
