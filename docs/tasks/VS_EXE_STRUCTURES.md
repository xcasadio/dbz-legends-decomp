# VS.EXE — les structures, et pourquoi elles viennent en premier

## LE CONSTAT

Le portage de VS.EXE modélise chaque enregistrement du jeu — combattant, événement
d'attaque, nœud de tâche, contexte de combat — comme un `int` nu, avec un décalage
magique écrit à chaque site d'accès. Ça marche, et le contrat de portage le demande
tant que la preuve n'est pas faite. Mais ça coûte, et le projet l'a payé deux fois
en une seule session :

- un balayage statique des « écritures dans +0x134 » a compté **trois sauvegardes de
  pile** (`sw ...,308($sp)`, des prologues) comme des écritures de combattant : un
  décalage seul ne distingue pas un enregistrement d'un cadre de pile ;
- la signature de `FUN_8004EE48` est restée fausse — un paramètre là où l'image en
  passe quatre — parce que rien ne typait les arguments ; et les trois « manquants »
  se sont révélés porter **la position du coup**.

Un décalage n'est pas un champ. Sans type, rien ne dit la taille d'un
enregistrement, quels décalages sont des champs, quelle largeur ils ont, ni s'ils
sont signés.

## L'OUTIL

`custom-tools/scripts/struct_fields.py` dérive la carte des champs **depuis
l'image**, jamais depuis le portage.

On lui donne une fonction et le registre d'argument qui porte l'enregistrement. Il
parcourt les instructions, suit tout registre qui vaut « base + K » — à travers les
copies de registre, les `addiu`, et les paires déversement/rechargement de pile —
et rapporte chaque lecture et chaque écriture faite à travers un de ces registres,
avec son décalage, sa largeur, et si la lecture était signée. L'union sur un
ensemble de fonctions qui partagent un type d'enregistrement EST la carte de ce
type.

```
python custom-tools/scripts/struct_fields.py --preset fighter
python custom-tools/scripts/struct_fields.py --func 0x8004ee48:a0 --name AttackEventRecord --emit-c
```

**Le code est bâti en `-O0`, et c'est toute la difficulté.** Une fonction ne garde
pas son argument dans `$a0` : elle le déverse immédiatement dans le cadre
(`sw $a0,0x20($s8)` à 0x8004EE58) et le recharge avant chaque usage, **par le
pointeur de cadre `$s8`, pas par `$sp`**. Un suivi qui ne connaît que `$sp` ne voit
rien du tout — la première version de l'outil rapportait une carte vide pour une
fonction de 1292 octets.

**Contrôle.** Passé sur `FUN_800261EC` seule, l'outil rapporte exactement les cinq
champs que la décompilation Ghidra montre — +0xac, +0x138, +0x16b, +0x22c, +0x22d —
et **n'attribue pas** à `param_1` le second `+0x138`, qui passe par un autre
combattant. Le suivi est sain, pas simplement bavard.

## COMMENT LES FONCTIONS DE DEPART SONT CHOISIES

Le portage sert à choisir **quelles** fonctions prennent un combattant ; l'image dit
**quels** sont les champs. Aucune circularité sur les données : un marqueur de
sélection faux ajoute du bruit, il n'invente pas un champ.

Un piège rencontré et corrigé : borner le corps d'une fonction à 400 lignes fait
déborder sur la suivante, ce qui a fait entrer l'enregistrement d'événement
(0x3c, 0x50–0x5f, 0x6c, 0x70, 0x78) dans la carte du combattant. Le corps se borne
au **prochain bloc `// GHIDRA:`**.

## OU LES TYPES VONT, ET COMMENT ILS Y ARRIVENT

**Dans l ARCHIVE `DbzLegendsTypes`, pas dans une categorie du programme.** Une
categorie ne se partage pas ; une archive `.gdt` est ouverte par VS.EXE, TITLE.EXE et
SELECT.EXE ensemble, ce qui est ce qu on veut pour des enregistrements communs.

**Ce que ReVa sait faire, mesure et non suppose :**

- **Mettre a jour un type deja source depuis l archive** : `parse-c-structure` sous
  le meme nom repond « Successfully modified structure », `sourceArchiveName` reste
  `DbzLegendsTypes`, meme id. C est la voie normale pour tout type existant.
- **Creer un type nouveau** : impossible dans l archive (`parse-c-structure` et
  `parse-c-header` n acceptent qu un `programPath` + une categorie ; pas d outil
  `create-archive` ; PyGhidra absent de cette instance). Le type nait dans le
  PROGRAMME, a la categorie `/` pour que le chemin corresponde, et l auteur le
  DEPLACE dans l archive a la main. Une fois deplace, il se met a jour comme
  ci-dessus.

**Le miroir texte.** `docs/types/DbzLegendsTypes.h` n est plus un vehicule
d import : c est le MIROIR relisible de l archive, parce qu un `.gdt` est binaire et
qu un `git diff` dessus ne montre rien. Il est regenere par l outil pour les
structures derivees et ecrit a la main pour les syntheses (les tableaux du contexte,
le noeud lu chez CreateTask). **Quand l archive change, le miroir suit** ; le jour ou
les deux divergent, c est l archive qui a raison.

## CE QUI EST DANS GHIDRA

Catégorie **`/DbzLegendsTypes`** du programme `/VS.EXE` :

| type | taille | source |
|---|---|---|
| `FighterRecord` | 0x23C | union de 73 fonctions prenant un combattant en `$a0` |
| `AttackEventRecord` | 0xC0 | `FUN_8004EE48` seule, la seule à le prendre en argument |

Noms posés uniquement là où la preuve est décisive — `flagsA` (+0x138, 244 accès),
`flagsB` (+0x134), `stateOpcode` (+0x16A), `moveClass` (+0x16B), `slotIndex`
(+0x173), `currentTaskNode` (+0xAC), `inputFlags` (+0x22C), `archetypeIndex`
(+0x22D), `animFrameCounter` (+0x04) ; côté événement `attackerTaskNode` (+0x3C),
`targetTaskNode` (+0x70), `eventType` (+0x6C), `eventFlags` (+0x78). Tout le reste
reste `field_0xNN`, et un champ jamais lu sort en `undefined1/2/4` — écrit quelque
part, mais rien ne dit comment il se lit.

**Ces types sont un PLANCHER, pas un plafond.** `+0x144`, le pointeur de données de
personnage, n'y figure pas : aucune des 73 fonctions balayées ne le touche, il est
écrit par `ActivateFighterInSlot` via un pointeur dérivé en interne que le suivi ne
peut pas atteindre depuis `$a0`.

## CE QUE ÇA CHANGE IMMEDIATEMENT

`FUN_800261EC` décompilée avec le type appliqué :

```c
int FUN_800261ec(FighterRecord *fighter)
{
  pFVar2 = *(FighterRecord **)(fighter->currentTaskNode + 8);
  if (fighter == pFVar2) return -1;
  if ((fighter->flagsA & 0x4000000U) != 0) return -1;
  if ((pFVar2->flagsA & 0x4000000U) != 0) return -1;
  if ((fighter->inputFlags & 0xc0) == 0) { ... fighter->archetypeIndex ... }
```

Ghidra a typé `pFVar2` en `FighterRecord *` tout seul — ce qui **corrobore** que le
`+8` d'un nœud de tâche est un pointeur de combattant, au lieu de le supposer.

## LE PIEGE DES UNITES, PAYE DEUX FOIS

Un decalage imprime par le decompilateur n a de sens qu avec **l unite du pointeur qui
le porte**. Ghidra type volontiers un pointeur d enregistrement en `undefined2 *`, et
alors tout ce qu il imprime est en DEMI-MOTS :

- `FUN_80053330` (CreateTask) ecrit `*(puVar2 + 6) = param_5` : ce n est pas +6, c est
  **+0x0C**. Toute la disposition du noeud tombe une fois les decalages doubles, et
  l allocation `taille + 0x18` confirme la taille d en-tete independamment.
- `RunBattleManagerFrame` lit `*(uint *)(ctx + 0x16b0)` : ce n est pas 0x16B0, c est
  **0x2D60**. J avais bati la queue de BattleContext dessus et signale au passage une
  collision qui n existait pas. L instruction tranche : 0x8005BFF4 est
  `addu $a0,$s2,$zero`, donc le `ctx` du callee EST `$s2`, et le chargement voisin est
  `lw $a1,0x2d60($s2)`. `CtxRoundRequest = 0x2D60` du portage avait raison.

`struct_fields.py` ne peut pas se tromper la-dessus : il lit les instructions, en
octets. **Quand les deux divergent, c est le decompilateur qu il faut convertir.**

## LIRE UN TYPE CHEZ SON CREATEUR, PAS CHEZ SES CONSOMMATEURS

Deux noms que j avais poses depuis un consommateur ont ete refutes par le createur :

- `AttackEventRecord` +0x3C. Je l avais nomme `attackerTaskNode`, puis renomme
  `attackerContext` en raisonnant que `*(cible + 8) = attaquant` ecraserait le champ
  contexte du scheduler. `CreateAttackEventTask` @ 0x80043598 refute le raisonnement
  en une ligne : `*(travail + 0x3C) = DAT_8008d16c`, et DAT_8008d16c est
  g_CurrentTask. C EST un noeud. Nom d origine retabli.
- La meme fonction donne la TAILLE de l enregistrement sans la deduire :
  `CreateTask(UpdateAttackEventTask, 0, 0xb, 0xC0, 0, ...)`. 0xC0, et la carte derivee
  s arretait a 0xBD -- premiere confirmation independante d une taille ici.

D ou la regle : **chercher d abord la fonction qui CREE l enregistrement.** Elle donne
la taille, l initialisation, et le type de chaque champ qu elle remplit ; un
consommateur ne donne que ce qu il lit.

La chaine imbriquee que cela etablit :
`((FighterRecord *)attackerTaskNode->context)->slotIndex`, soit `+8` puis `+0x173` --
une structure qui en contient une autre, le pont etant le champ contexte que
CreateTask range dans le noeud.

## TYPER UN GLOBAL : LES ECRIVAINS D ABORD, PUIS TOUT CE QUI PASSE PAR SA VALEUR

`DAT_8008d16c` a 96 references. Deux preuves, et aucune ne passe par un lecteur :

1. **Ses ecrivains sont tous le scheduler.** `FUN_80053628` y range la tete de liste,
   parcourt les noeuds a travers lui, lit +2 / +4 / +0xC / +0x10 / +0x14 et APPELLE
   `*(+4)` ; `FUN_8005354c` et `FUN_80053840` le font avancer quand ils dechainent le
   noeud courant. Personne d autre ne l ecrit.
2. **Le balayage de tout le programme a travers sa valeur** -- `struct_fields.py`
   amorce sur chaque `lw rt,0x70($gp)` (gp = 0x8008D0FC) -- ne touche que
   +2, +4, +0xC, +0x10, +0x14. Rien hors de l en-tete de 0x18 d un noeud.

Donc `TaskNode *`, nomme `g_CurrentTask` comme dans le portage TITLE.EXE, qui avait
choisi les memes noms a ses propres adresses : `ExecuteTaskList` (0x80053628),
`DeleteTask` (0x8005354c), `DeleteTaskList` (0x80053840), `g_CurrentTaskListIndex`
(0x8008D170, le ushort que le repartiteur ecrit juste a cote). Les trois tables de
listes ont 21 entrees : `0x80083B90 - 0x80083B3C = 0x54`, coherent avec l index 0x14
que `main` passe a CreateTask.

Le repartiteur donne aussi le sens de `+0x0C`, que CreateTask laissait opaque : un
COMPTEUR DE FRAMES. Positif, il est decremente sans que la tache tourne (un delai) ;
nul, la tache tourne a chaque frame ; negatif, elle tourne puis il est incremente, et
a -1 le noeud est libere. C est ecrit dans le miroir, pas dans un nom : `param5`
reste `param5` tant qu un seul appelant n a pas ete relu avec cette grille.

## NOMMER LES CHAMPS : CE QUI PASSE PAR EUX, PAS CE QU ILS CONTIENNENT

Un nom de champ n est decisif que quand une FONCTION lui donne un role. Pour
`AttackEventRecord`, trois fonctions l ont fait, et aucune n etait un consommateur :

- `RotateVectorByAngles` (0x800461FC, ex-FUN_800461fc) : `RotMatrix(angles)` puis
  `RotTrans(in, out)` -- elle TOURNE un vecteur par trois angles d Euler. Son appel
  dans UpdateAttackEventTask est `((speed, 0, 0), &+0x48, +0x60)`. Donc +0x7C est une
  VITESSE (le vx tourne), +0x48..+0x4C une ROTATION (des angles 12 bits, ce que
  confirme le `& 0xFFF` apres le tirage aleatoire de +/-0x600), +0x60 un VECTEUR
  VITESSE de trois longs -- le quatrieme long d un VECTOR serait eventType, donc
  `int velocity[3]` et pas `VECTOR`.
- `LookupSpriteCell` (0x80045B70, ex-FUN_80045b70) : quantifie deux angles
  relatifs a la camera (DAT_1f80007c/7e) en seize secteurs et indexe une table.
  Son resultat va dans +0x7E, masque `& 0x3F`, et ses bits `0xC0` partent a
  DrawSpriteGroup : une CELLULE DE SPRITE, six bits de direction, deux de miroir.
- `DrawSpriteGroup` recoit +0x28 en premier argument et +0x74 en dixieme : le
  GROUPE DE SPRITES et la PROFONDEUR de dessin.

Et le createur donne les types des arguments : `CreateAttackEventTask` recopie
trois demi-mots de chacun de ses deux premiers parametres dans +0x40 et +0x48, deux
SVECTOR ; son troisieme est la vitesse, son quatrieme l index dans
`g_AttackEffectStreams` (0x800217F0, vingt pointeurs de flux -- la table se termine
exactement la ou le premier flux commence, 0x80021840).

## UN PREFIXE PARTAGE : L EN-TETE DE LECTEUR DE FLUX D ANIMATION

Les 0x0C premiers octets de `AttackEventRecord` ET de `FighterRecord` sont la meme
structure, `AnimStreamHeader` -- base, frame, drapeau, curseur de flux. Preuve :

- `BindAnimStream` (0x80053970) ecrit +0x04 = 0, +0x06 = 0, +0x08 = table[index]
  (+ base quand l entree est relative) ;
- `StepAnimStream` (0x800539D0) incremente +0x04, parcourt le flux depuis +0x08 par
  entrees `[debut, fin, longueur | opcode]` et distribue chaque opcode dont la fenetre
  contient la frame via `PTR_LAB_80083C10` -- la table de 51 opcodes que
  `check_vs_dispatch.py` verifie deja ;
- et les DEUX recoivent des combattants : `ActivateFighterInSlot` fait
  `BindAnimStream(fighter, *(fighter->+0x148 + 0x38), 0); StepAnimStream(fighter)`,
  l etape 9.5 d UpdateFighter (FUN_80047688) rappelle `StepAnimStream(fighter)` a
  chaque frame.

Ce que le portage appelait `animFrameCounter` au +0x04 du combattant EST la frame de
ce lecteur. Et +0x148 du combattant pointe la banque de personnage dont le +0x38 est
sa table de flux : `characterBank`.

+0x0C n en fait pas partie -- l evenement y range ses liaisons de VM, et aucun des
deux lecteurs ne le touche.

## LA SUITE

1. **Le contre-contrôle portage/image.** Extraire, pour chaque fonction, les
   décalages que le C# touche via `PsxRam.Read/Write*(param_1 + N)` et les confronter
   à la carte dérivée de l'image. Toute divergence est soit un défaut de l'outil,
   soit un défaut du portage — les deux méritent d'être connus. C'est ce qui
   transformerait l'outil en vérificateur de couture.
2. **Les trois autres enregistrements** : le nœud de tâche, le contexte de combat, le
   sous-enregistrement de créneau à foulée 0x1C0. Le nœud de tâche demande une
   amorce « à telle adresse, tel registre porte l'enregistrement », que l'outil ne
   sait pas encore prendre.
3. **Reporter les types dans le C#**, une fois la carte stable, pour que le portage
   cesse d'écrire des décalages nus.
