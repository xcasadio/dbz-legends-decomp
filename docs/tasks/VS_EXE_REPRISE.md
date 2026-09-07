# VS.EXE — prompt de reprise pour une nouvelle session

Ce fichier est le message a coller au demarrage d'une session neuve. Il se suffit
a lui-meme : tout ce qui suit a ete mesure, pas suppose, et chaque affirmation
renvoie a une adresse ou a une commande reproductible.

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
  L'auteur a redemande ce point trois fois : ce n'est pas un extra.
- **13** les routines du SDK PSX ne sont pas du runtime de jeu ;
  **14** ne pas simuler le materiel PSX quand un contrat observable equivalent
  suffit.
- Annotations obligatoires : `// GHIDRA: nom @ 0xADDR (VS.EXE)`,
  `// JUSTIFICATION:` (uniquement *backend MonoGame* | *adaptation materiel PSX*
  | *pont langage C#*), `// RELATION:`, `// PARTIAL:`, `// BLOCKED:`,
  `// DEVIATION:`.

**Pas de simulation de lecteur CD.** Un fichier absent leve une exception qui le
nomme ; aucune boucle d'attente ni de reessai. Seule exception admise :
`WaitDiscLoad`.

**Langues** : francais pour les reponses a l'auteur et les plans ; anglais pour
le code, les commentaires, les messages de commit, la doc et les ADR.

---

## OU EN EST LE PORTAGE

`VS_EXE/` compte 24 fichiers et **30 souches** restantes :

```
  FighterCombat.cs   12      FighterTask.cs      4
  AnimCmdSound.cs     5      BattleScene.cs      2
  BattleManager.cs    4      FighterAction.cs    2
  VS_EXE_exe.cs       1
```

Le mode VS **boote, charge, et affiche un ecran bleu clair**. Ce bleu clair est
un progres, pas une regression : `RGB(152, 224, 240)` est l'entree 5 de
`VariantBackgroundColorTable` @ `0x80082C30`, donc `FUN_800414ec` tourne et pose
la vraie couleur de fond. Le defaut code en dur, lui, est `(0, 0, 200)` — bleu
sombre. La scene 3D, elle, n'est jamais construite.

---

## LA CHAINE DE L'ECRAN BLEU, MAILLON PAR MAILLON

Elle est entierement cartographiee **et portee, sauf son declencheur** :

```
  ???  met un combattant en etat d'attaque        <-- LE SEUL MAILLON MANQUANT
   |     FighterTask etape 9.3 : FUN_80049f54 (souche, retourne 0)
   v
  deux routes, les DEUX portees :
   |  FUN_8004e758 ...... derriere un garde dont AUCUN setter du bit 31
   |                       n'existe dans le code decompile statiquement
   |                       (trois recherches exhaustives)
   |  FUN_8004ee48 ...... via CreateAttackEventTask -> UpdateAttackEventTask
   v
  AddSlotGaugeContribution @ 0x8004E108
   |     seul ecrivain de CtxGaugeContribution dans toute l'image
   v
  RunBattleRound @ 0x80055F94    somme les douze, clampe a +/- 30000
   v
  CtxFlags bit 3                 `ori v0,v0,0x8` @ 0x8005633C
   v
  la tache de scene de combat est creee   (uniquement a 0x800563F8)
   v
  RenderBattleScene3D            son SEUL appelant est cette tache
```

**Piege de lecture a connaitre** : Ghidra imprime le poseur du bit 3 comme
`*(uint *)(ctx + 8) | 8`. `ctx` est un `short *`, donc `ctx + 8` **est** l'offset
octet `0x10`. Chercher `"+ 0x10) | 8"` ne trouve rien — c'est ce qui a fait
perdre du temps.

---

## LA CIBLE IMMEDIATE — VAGUE 5

**`FUN_80049f54` @ `0x80049F54` (VS.EXE)**, 388 octets, actuellement une souche
qui retourne 0 dans `VS_EXE/FighterTask.cs`.

C'est l'etape 9.3 du `FighterTask` : *le mot de commande de la frame*. Sa valeur
de retour devient `uStack_10`, qui est passe en `param_2` a **toutes** les
fonctions de l'etape 9.4 portees a la vague 4. Or chaque bras qui declenche une
attaque teste `param_2` contre un opcode non nul — `0x26/0x27/0x28`, `0x21`,
`0x13/0x14`, `0x1c`, et dans les appelees `0x17`, `0x23/0x24/0x25`. Fige a 0,
**tous** prennent leur branche « pas d'attaque, remise a zero », a chaque frame.
C'est la raison mesuree pour laquelle la chaine ci-dessus ne demarre jamais.

Ses dependances, verifiees :

- `FUN_80049e30` (276 o) — **deja portee**, dans `FighterAction.cs`.
- `FUN_80023890` (5096 o) — **manquante**. C'est le gros morceau de la vague.

---

## LA BOUCLE DE VERIFICATION (a lancer avant de declarer quoi que ce soit fait)

```
dotnet build custom-tools/DbzLegendsAnalyser/DbzLegendsAnalyser.sln -v q
dotnet run --project custom-tools/DbzLegendsAnalyser/DbzLegendsRemaster -- --diag-vs 400
dotnet run --project custom-tools/DbzLegendsAnalyser/DbzLegendsRemaster -- --diag-select 400
python custom-tools/scripts/check_duplicate_symbols.py
```

Criteres d'acceptation, tous les quatre a chaque etape :

- build propre ;
- **13/13** bancs d'essai ;
- **4/4** verificateurs de couture ;
- `--diag-select 400` = **49396 pixels**, inchange. Ce chiffre est le temoin de
  non-regression du portage deja acquis : s'il bouge, quelque chose a casse
  ailleurs.

`--diag-vs [frames] [handoverMode]` (dans `Validation/VsBootDiagnostic.cs`) boote
VS.EXE sans fenetre et rapporte : appels du dispatcher de manager, visites de
`CtxState`, `CtxFlags`, **les quatre conditions du bit 3 separement**, les douze
contributions de jauge, le RGB de fond du DRAWENV, et les compteurs de la chaine
de jauge. C'est l'instrument qui a tranche le diagnostic apres deux fausses
pistes — s'en servir plutot que raisonner.

---

## LES QUATRE CLASSES DE DEFAUTS QUI SE REPETENT

Elles ont ete trouvees par revue adverse, jamais par le compilateur. Les mettre
dans chaque brief.

1. **Une adresse Ghidra declaree dans deux fichiers.** C# lie un appel non
   qualifie a la classe englobante d'abord : la souche vide bat le vrai corps.
   Ca compile, ca tourne, et le travail ne se fait pas. Quatre occurrences, trois
   livrees. **Contre-mesure : chercher l'ADRESSE dans tout `VS_EXE/` avant de
   declarer quoi que ce soit.**
2. **Du travail supprime.** Deux stores `SWL/SWR` inconditionnels perdus, une
   boucle de douze iterations disparue, un appel sur deux d'une paire
   (`a1 = 0` puis `a1 = 1`). Chacun verifie octet par octet ; aucun faux positif.
3. **`CreateTask` sans `RegisterCallback`.** `TaskSystem.CreateTask(entry, id,
   list, size, p5, insertPoint)` **doit** etre precede de
   `TaskSystem.RegisterCallback(entry, delegue)`, sinon le noeud ne dispatche
   rien. La boucle de frame parcourt les listes 0..0x13 (`VS_EXE_exe.cs:397`).
4. **Largeur de chargement et signe.** `PsxRam` n'expose que `ReadU8` / `ReadU16`
   / `ReadI32` et `WriteU8` / `WriteU16` / `WriteI32` ; une lecture 16 bits
   signee s'ecrit `(short)PsxRam.ReadU16(addr)`. Et une inversion de bras de
   `if` reste possible meme quand le commentaire d'en-tete decrit la version
   inversee : le commentaire a ete ecrit apres la melecture, il ne la corrige
   pas.

---

## LES CANAUX DE PREUVE

- **ReVa MCP -> Ghidra** — chemin de programme exactement `/VS.EXE`.
- **PCSX-Redux MCP** — pour lire la RAM de l'original en marche.
- `gp = 0x8008D0FC` pour VS.EXE.
- `LibGpu.RamRegion(adressePsx, taille)` declare les octets porteurs ;
  `LibGpu.RamResolve(addr, out byte[] buf, out int off)` fait le pont.
  **Agrandir une region, jamais la retrecir** : `RamRegion` met a jour la ligne
  au lieu d'en ajouter une seconde.

---

## LA DISCIPLINE GIT

- **Ne jamais committer sur `main`** : un hook de niveau utilisateur le refuse,
  et il refuse **tout l'appel Bash**. Donc `git checkout -b` doit etre un appel
  Bash **separe** de `git commit`.
- Sequence : branche -> commit -> `git checkout main` -> `git merge --ff-only`.
- Sous-module `custom-tools/PsxSdkMonogame` : meme hook a l'interieur. Le
  brancher, y committer, fusionner sur son `main`, **puis** bouger le pointeur du
  super-projet.
- Indexer fichier par fichier (`git add <chemin>`), jamais `git add -A` ni `.`.
- **Ne jamais pousser.** L'auteur pousse lui-meme.

---

## ETAT NON POUSSE A LA FIN DE LA SESSION PRECEDENTE

**7 commits** sur le `main` du super-projet et **1** dans le sous-module.
Le dernier : `8edbe84` *feat(vs): port step 9.4 and its subtrees, and name the
one function still in the way*.

---

## LA PREMIERE CHOSE A FAIRE

Porter `FUN_80049f54` @ `0x80049F54`, avec sa dependance manquante
`FUN_80023890` (5096 o), puis mesurer avec `--diag-vs 400` si les contributions
de jauge deviennent non nulles. Et renommer, dans Ghidra comme dans le C#, tout
ce dont la preuve est devenue decisive au passage.
