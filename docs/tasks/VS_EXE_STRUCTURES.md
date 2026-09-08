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
