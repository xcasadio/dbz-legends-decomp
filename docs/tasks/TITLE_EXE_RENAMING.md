# TITLE.EXE - le renommage, et le piege de ReVa qui a coute une reparation

## Ce qui a ete renomme

35 symboles, dans Ghidra **et** dans le portage C#: 18 fonctions, 1 label, 16 globales.

| categorie | noms |
|---|---|
| decompression, rendu, fondu | `DecompressLzss`, `DrawSpriteGroup`, `ControlScreenFade`, `UpdateScreenFade` |
| ecran titre | `SetupTitleScreen`, `UpdateTitleScreen` |
| taches | `DeleteTaskList` |
| pools de primitives | `CreatePrimitivePools`, `AllocatePrimitivePool`, `FreePrimitivePool`, `InitializePrimitivePool`, `ResetPrimitivePoolCursors` |
| images | `LoadCompressedImageInVram`, `LoadImageListInVram` |
| ecrans | `ShowLoadingScreen` |
| carte memoire | `InitializeMemoryCard`, `ShutdownMemoryCard` |
| vecteurs | `CalculateDistance3D`, `CalculateLookAtAngles` |
| globales | `g_CurrentTask`, `g_CurrentTaskListIndex`, `g_ActiveDrawEnvAddress`, `g_PrimitivePoolContext`, `g_PrimitiveSizeTable`, `g_PadButtonMaskTable`, `g_PadNewlyPressed`, `g_PadHoldFrames`, `g_FrameCounter`, `g_ImageDecodeBuffer`, `g_BackgroundColorR/G/B`, `g_StageBackgroundColorTable`, `g_FaceVramCoordTable`, `g_FadeQuad` |

**Tout le reste garde son nom brut**, `FUN_`, `DAT_`, `LAB_`. C'est deliberé: le mandat du depot
dit de garder le nom brut la ou la semantique n'est pas fermee, et la majorite de cet overlay est
dans cet etat.

## Deux propositions rejetees

Les agents de relevé les avaient classees CERTAIN; la session principale a tranche autrement.

- **`g_GeomOffsetX/Y`** (`0x1F800114` / `0x1F800110`). Ce sont des mots du **scratchpad**, et ce
  jeu le reutilise massivement. Les nommer d'apres un unique site d'appel est exactement le risque
  que la regle 11 vise.
- **`LoadExecutableTask`** (`0x800324D8`). Neuf de ses dix selecteurs font un `LoadExec`. Le
  dixieme ne charge rien: il se contente de lever `DAT_800835b4`. Le nom surdit.

## Une speculation a moi, corrigee

Les agents ont **refuse** de nommer `FUN_80058a9c`: « ca installe un ecran mais on ne peut pas dire
lequel ». Ils avaient raison, et ca condamnait un nom que j'avais pose plus tot.

**SELECT.EXE est un overlay separe**, atteint par `LoadExec` depuis l'etat 5 de la tache titre.
Rien a l'interieur de TITLE.EXE ne peut donc construire l'ecran de selection. Le fichier
`SelectScreenSetup.cs` affirmait une connaissance que le portage n'a pas.

Il devient `SecondScreenSetup.cs` - « second » est prouve par la structure de `main`, qui enchaine
deux `RunFrameLoop` - les trois autres commentaires qui parlaient de select screen sont corriges,
et **`FUN_80058a9c` garde son nom brut**, avec un `BLOCKED:` qui enonce ce qui n'est pas ferme.

## La mecanique ReVa: deux pieges, dont un irreversible

### `create-label` ajoute, il ne remplace jamais

Il n'existe ni `delete-label`, ni `remove-symbol`, ni `rename-symbol` dans ReVa, et **PyGhidra est
indisponible** dans ce projet - donc aucune echappatoire par script. Un nom de donnee pose ne peut
plus etre retire par cette voie.

C'est l'argument le plus fort pour la barre CERTAIN: un nom de donnee speculatif ne se rattrape pas.

La procedure appliquee: **une seule sonde** d'abord (`0x800834E0`), puis comptage des symboles.
Un seul symbole -> le mecanisme est propre, on continue. Deux ou plus -> arret immediat.

La sonde etait propre. Les noms `DAT_`, `PTR_`, `INT_ARRAY_`, `POLY_GT4_` sont **synthetises** par
Ghidra, pas stockes, donc creer un vrai label laisse bien un seul symbole.

Un incident au 4e renommage: le compteur de symboles non-defaut n'a pas incremente. L'agent s'est
**arrete et a enquete** plutot que de continuer. Cause: `filterDefaultNames` de ReVa est un filtre
de motif qui ne reconnait que `PREFIX_<hexadecimal pur>`; `PTR_ARRAY_80083228` n'y entre pas a
cause de l'infixe `ARRAY_`. Artefact de comptage, pas doublon - prouve par lecture directe de la
liste des symboles a l'adresse. L'arithmetique ferme exactement: 14 conformes + 3 non conformes = 17.

### `set-function-prototype` n'est pas un outil de renommage

**C'est le piege qui a coute une reparation.** L'outil appelle `setReturnType` et
`replaceParameters` avec `force`: il **installe exactement** ce que dit la chaine de signature.

La procedure semblait sure: lire la signature courante, la reenoncer avec seulement l'identifiant
change, appliquer. Elle ne l'etait pas.

`get-decompilation {signatureOnly:true}` rend le prototype **STOCKE**. Pour une fonction dont
aucun prototype explicite n'a jamais ete valide, ce prototype stocke est **vide** -
`undefined FUN_xxxx(void)`, `parameterCount` 0 - alors que le **decompilateur** retrouve les vrais
parametres dynamiquement a chaque decompilation.

La passe a donc lu `(void)`, l'a fidelement reenonce, et l'appliquer a transforme un prototype
absent en un prototype `USER_DEFINED` vide **avec le stockage des parametres verrouille**.

**10 des 18 fonctions renommees ont perdu leur liste de parametres.** Symptome cote appelant:

    CreatePrimitivePools();                    au lieu de  CreatePrimitivePools(0x14,200,100,...)
    AllocatePrimitivePool();  x8 identiques    au lieu de  (context,0,param_1) ... (context,7,param_8)

L'attribution etait nette: 8 sur 8 des renommees verifiees etaient verrouillees, 0 sur 8 des
temoins non renommees, et les fonctions nommees lors de commits anterieurs etaient intactes.

### Le dégât se dissimulait le long des chaines d'appel

Le point le plus subtil, trouve par l'agent de reparation.

`CreatePrimitivePools` ne lisait **aucun** `in_aN` - ce qui se lit comme « saine, reellement sans
parametre ». Elle ne l'etait pas. Son callee `AllocatePrimitivePool` etant verrouille a zero
parametre, les huit transferts d'arguments qui l'alimentaient devenaient du **code mort**, et le
decompilateur les eliminait. Ils ne sont reapparus qu'apres reparation du callee, revelant alors
`in_a0..in_a3` plus quatre emplacements de pile - huit parametres.

**Toute enquete qui juge le dégât sur « le corps lit-il un `in_aN` » sous-compte**, partout ou
l'unique usage des arguments d'une fonction abimee est de les transmettre a une autre fonction
abimee. La reparation a donc procede **en partant des callees**.

### La reparation

Aucun outil ne peut dé-figer un prototype: `set-function-prototype` est le seul levier, donc la
reparation consiste a poser le **bon** prototype, pas a annuler. Acceptable uniquement parce que le
bon etait enregistre - dans le portage C#, translittere depuis la decompilation d'avant le dégât,
et recoupe contre les registres et emplacements de pile que le corps lit encore.

Le test d'acceptation etait **cote appelant**, jamais l'echo de la signature: les arguments
doivent reapparaitre aux sites d'appel. Les cinq appels de `DrawSpriteGroup` reaffichent leurs
dix-huit arguments, y compris le plancher de table `-6000` et les echelles `0x1000`.

Six fonctions portent encore l'avertissement de verrouillage sans avoir rien perdu: leur liste de
parametres est reellement vide. Classe P3 et laisse tel quel - 31 fonctions du projet portent le
meme avertissement, dont 25 routines PsyQ, c'est l'etat normal de la base pour tout prototype
`(void)` valide.

## A retenir pour la prochaine fois

1. Ne jamais renommer une fonction par `set-function-prototype` sans avoir lu le corps
   **decompile** pour compter les arguments reels. La signature stockee ment.
2. Un renommage de donnee est **a un coup**. La barre CERTAIN n'est pas du zele, c'est la seule
   protection.
3. Un dégât de prototype se propage en amont dans le graphe d'appel et s'y rend invisible.
   Reparer en partant des callees.
4. Verifier **cote appelant**. Une signature qui s'echo correctement ne prouve rien.
