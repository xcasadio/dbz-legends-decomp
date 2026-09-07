# VS.EXE — prompt de reprise pour une nouvelle session

Ce fichier est le message a coller au demarrage d'une session neuve. Il se suffit
a lui-meme : tout ce qui suit a ete mesure, pas suppose, et chaque affirmation
renvoie a une adresse ou a une commande reproductible.

**Reecrit le 2026-09-07.** La version precedente disait que l'unique maillon
manquant etait l'etape 9.3 du `FighterTask`. C'etait faux, et la mesure l'a montre
en une commande : l'etape 9.3 n'etait jamais atteinte. Ce fichier raconte la vraie
chaine, et ce qui reste.

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
PORTED / STUB / ABSENT en distinguant un vrai corps d'un `_ = param;`, et exclut
le SDK. Au dernier passage : **264 fonctions portees, 148519 octets, 90,1 %** du
code de jeu ; 22 souches, 21 absentes (4284 octets).

Le script classe aussi les absentes par taille : c'est la file de travail.

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

Elle fait tout : build, les treize bancs, les cinq verificateurs de couture, et le
temoin `--diag-select 400`. Criteres, tous les quatre a chaque etape :

- build propre ;
- **13/13** bancs ;
- **5/5** verificateurs de couture ;
- `--diag-select 400` = **49396 pixels**, inchange.

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
   trois sessions. `check_task_registration.py` liste l'etat : VS_EXE est a 11/13.
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

`vs_port_coverage.py --list ABSENT` et `--list STUB` donnent la liste exacte. Les
gros morceaux au dernier passage :

- `FUN_80058338` @ 0x80058338, 2440 octets — souche dans BattleManager.cs ; sa
  propre note explique pourquoi (deux tables de pointeurs sans travee
  auto-referentielle a incorporer).
- la famille des effets, autour de `0x80042054` et `0x80045130`.
- le reste du module son, autour de `0x8006071C`.
- deux entrees de tache encore non enregistrees : `LAB_80029200` et
  `LAB_80026888`.

Et la question ouverte que la mesure pose maintenant : la chaine de jauge
(`AddSlotGaugeContribution`) n'est toujours jamais atteinte, parce qu'aucun mot de
commande d'attaque (0x1C, 0x23..0x28) n'est produit. Le decodeur pad et l'IA
tournent tous les deux ; ce qui manque est soit une entree de joueur reelle, soit
une condition d'etat que rien de porte ne leve encore. C'est la prochaine question
a poser au banc, pas au raisonnement.
