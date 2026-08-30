# TITLE.EXE - infrastructure de rendu

## Objectif

Rendre operante la chaine de dessin dont `RunFrameLoop @ 0x800587A8` depend.
Le portage precedent tournait sa boucle de frame complete, executait ses 21
listes de taches et soumettait sa table d'affichage, mais l'ecran restait noir
sans qu'aucun diagnostic ne le signale.

Cette tache ne porte aucune fonction du jeu. Elle ferme deux trous du SDK et
deplace la table d'affichage de TITLE.EXE en memoire reelle.

## Le diagnostic

La chaine de dessin de la PSX compte cinq maillons:

```
ClearOTag  ->  AddPrim  ->  DrawOTag  ->  rasteriseur  ->  VRAM
```

Trois etaient deja en place dans `PsxSdkMonogame`: `AddPrim(byte[], int, byte[], int)`,
`RasterizeOrderingTable(byte[], int)`, et le registre d'adresses
`RamRegion` / `RamAddressOf` / `RamResolve` qui traduit une adresse PSX en
`(tampon, decalage)`.

Deux etaient inertes, chacun silencieusement:

1. **`ClearOTag` n'existait qu'en forme inverse.** Le SDK portait `ClearOTagR`,
   qui chaine chaque case vers sa **precedente**, donc un parcours part de la
   queue. `RunFrameLoop` appelle `ClearOTag`, la forme **avant**, qui chaine
   chaque case vers sa **suivante** et termine la derniere. Seul un stub
   `ClearOTag(OT_TYPE)` repondait. La table n'etait donc jamais chainee: chaque
   case valait zero, c'est-a-dire la valeur de fin de chaine du rasteriseur, et
   le parcours s'arretait sur la premiere.

2. **`DrawOTag(int)` ne faisait rien sans gestionnaire installe.** Le corps
   valait `DrawOTagIntHandler?.Invoke(otagBase)`. Un jeu pouvait soumettre sa
   table entiere et n'obtenir ni pixel ni message.

Les deux ensemble expliquent l'ecran noir sans erreur.

## Ce qui a ete ajoute au SDK

`PsxSdkMonogame/LibGpu.cs`:

| Ajout | Forme | Role |
|---|---|---|
| `ClearOTag(byte[] ot, int baseOffset, int n)` | tampon + decalage | chaine avant, ecrit dans chaque case l'adresse PSX de la suivante, masquee sur 24 bits |
| `ClearOTag(byte[] ot, int n)` | tampon | delegue a la precedente avec `baseOffset = 0` |
| `ClearOTag(int otagBase, int n)` | adresse PSX | resout via `RamResolve` puis delegue |
| repli de `DrawOTag(int otagBase)` | adresse PSX | sans gestionnaire installe, resout la table et appelle `RasterizeOrderingTable`, ce que fait le materiel avec le meme argument |

Chaque mot conserve son octet de poids fort, qui porte la longueur de l'entree
et reste nul pour une case vide. La derniere case recoit `0x00ffffff`, la marque
de fin de chaine.

## Ce qui a change dans TITLE.EXE

`TITLE_EXE/FrameLoop.cs`. La table d'affichage etait un objet C#; elle devient
de la memoire reelle, parce que le rasteriseur parcourt les primitives par
**adresse PSX** et qu'une case sans adresse ne peut rien pointer.

```csharp
private const int Ot800a6830Address = unchecked((int)0x800A6830);
internal static readonly byte[] OT_800a6830 = new byte[0x800 * 4];
```

`DAT_800834e0` porte desormais **l'adresse** du DRAWENV actif, non plus une
reference vers l'objet. C'est ce qui permet de garder verbatim l'arithmetique de
l'original au moment de la soumission:

```csharp
DrawOTag(DAT_800834e0 + 0x70);
```

`0x800A67C0 + 0x70 = 0x800A6830`: la table d'affichage se trouve exactement
derriere le DRAWENV, et l'original la joint par ce calcul plutot que par son
adresse propre. La transliteration le conserve.

L'enregistrement de l'adresse se fait a l'entree de la boucle, via un pont
`DeclareOrderingTableAddress` marque `JUSTIFICATION: PSX hardware adaptation only`.
`RamRegion` est idempotent, donc le rappeler a chaque entree est sans effet.

## Preuve

`Validation/RenderPipelineValidation.cs`, execute par `--validate-render`.

Le banc construit **une TILE a la main** a l'adresse PSX `0x800B0000`, la chaine
dans la case 0 de la table de TITLE.EXE, soumet la table, puis compte les pixels
de la VRAM.

Ce qu'il verifie:

| Verification | Resultat |
|---|---|
| la premiere case pointe vers la suivante apres `ClearOTag` | `0x00A6834`, soit la case 1 |
| la derniere case termine la chaine | `0x00ffffff` |
| la TILE est taguee 3 mots, code `0x60` | conforme |
| `AddPrim` met la primitive en tete de la case 0 | la case porte `0x0B0000` |
| la primitive conserve sa longueur dans son octet de poids fort | 3 |
| le rectangle 50x30 couvre ses pixels | **1500 pixels** |
| rien n'est ecrit hors du rectangle | **0 pixel** |

`1500 = 50 * 30` exactement, et zero debordement: la primitive traverse les cinq
maillons et atterrit au bon endroit de la VRAM, aux bonnes dimensions.

## Non regression

Les six bancs passent apres le changement:

| Banc | Resultat |
|---|---|
| `--validate-heap` | passe |
| `--validate-tasks` | passe |
| `--validate-title-init` | passe |
| `--validate-title-images` | passe |
| `--validate-pad-input` | passe (avec `DBZ_PAD_FORCE=0x0800`) |
| `--validate-render` | passe |

## Ce qui reste

L'infrastructure dessine, mais **rien ne lui donne encore de primitives**. Les
deux taches qui en produisent ne sont pas portees:

| Fonction | Taille | Role |
|---|---:|---|
| `FUN_80021e28 @ 0x80021E28` | 2056 octets | la tache de l'ecran titre; construit et soumet ses primitives |
| `FUN_80038684 @ 0x80038684` | - | la tache d'animation du fondu |

Point d'architecture a trancher avant de les porter: `POLY_GT4_800b9518` et les
cinq `POLY_FT4` de `DisplayMachine.cs` sont aujourd'hui des objets C#. Le
rasteriseur parcourant les primitives par adresse PSX, il faudra les basculer en
memoire comme la table d'affichage vient de l'etre.
