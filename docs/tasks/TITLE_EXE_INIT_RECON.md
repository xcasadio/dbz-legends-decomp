# TITLE.EXE - reconnaissance de l'initialisation

## Objectif

Fermer par preuve le chemin d'ouverture de `main @ 0x800581DC` avant d'ecrire la
moindre ligne de C#, pour que le decoupage des lots suivants repose sur des
faits et non sur une hypothese d'architecture.

Aucun code n'a ete ecrit pour cette etape. Aucun renommage n'a ete fait dans
Ghidra.

## main @ 0x800581DC

`main` est entree depuis `start @ 0x80068FF4` par le `jal` de `0x80069090`, et
ne retourne jamais. Decompilation annotee:

```c
void main(void)
{
  __main();
  FUN_80070b64();                        // syscall(0), EnterCriticalSection
  ResetCallback();
  ResetGraph(0);
  InitGeom();
  SetDispMask(0);
  FUN_80057508();                        // efface toute la VRAM
  PadInit(0);
  CdInit();
  do {
    pCVar1 = CdSearchFile(&DAT_800a8860, "\SELECT.EXE;1");
  } while (pCVar1 == NULL);              // attente disque, sans VSync
  ReadFile("\SUB\TITLE.B;1", &DAT_80110000, 0);
  InitHeap(0x10000, 0x10000);
  srand(0x10000);
  FUN_80070e44();                        // syscall(0), ExitCriticalSection
  FntLoad(0x3c0, 0x100);
  DAT_80083498 = FntOpen(0x10, 0x10, 0x100, 200, 0, 0x200);
  DAT_8008344c = 0;
  DAT_80083450 = 0;
  DAT_80083448 = 0;
  FUN_80057674(0xa8, 0x80, 0x1000, 0,0,0, 0x1000, 0,0,0);  // setup GTE
  FUN_80049504(FUN_80037388, 0,0,0,0, _DAT_80079854);       // enregistre une tache
  FUN_80037388();
  FUN_80056dc0(0x14, 200, 100, 0x15e, 0x14, 0x14, 0, 0);
  DAT_80083544 = 0;
  FUN_80038228(8, 0);
  FUN_80058d64();
  do {                                   // boucle principale
    if (2 < DAT_801ff10e) { DAT_801ff10e = 0; }
    DAT_1f80012c = (uint)DAT_801ff10e;
    DAT_801ff100 = 2;
    DAT_801ff10e = DAT_801ff10e + 1;
    FUN_80038228(8, 0);
    DAT_800835b4 = 1;
    FUN_80021dd0();
    FUN_800587a8();
    FUN_80058a9c();
    FUN_80038228(2, 4);
    DAT_80083504 = 0;
    FUN_800587a8();
  } while( true );
}
```

`InitHeap(0x10000, 0x10000)` et `srand(0x10000)` ne sont pas une confusion du
decompilateur: l'ASM porte bien `lui $a0, 0x0001` et `lui $a1, 0x0001` en
`0x8005825C` et `0x80058264`, puis reutilise `$a0` intact pour `srand`. Le heap
fait donc 64 Kio a `0x00010000`, miroir KUSEG de `0x80010000`, et la graine du
generateur pseudo-aleatoire est constante.

## Semantiques fermees

| Adresse | Symbole | Contenu prouve |
|---|---|---|
| `0x80070B64` | `FUN_80070b64` | `syscall(0)`, marquee *Possible A36.OBJ/EnterCriticalSection*. 9 appelants. |
| `0x80070E44` | `FUN_80070e44` | `syscall(0)`, marquee *Possible A37.OBJ/ExitCriticalSection*. 10 appelants. |
| `0x80057508` | `FUN_80057508` | `ClearImage({0,0,0x400,0x200}, 0,0,0)` puis `DrawSync(0)`: efface la VRAM entiere en noir. 2 appelants. |
| `0x80057DF4` | `ReadFile` | `WaitSearchFile(fileName, &cdlFile)` puis `ReadCDData(&cdlFile, buffer, mode)`. 11 appelants. |
| `0x80057F80` | `WaitSearchFile` | `do { CdSearchFile(cdlFile, fileName); } while (result == NULL)`. **Sans `VSync`.** |
| `0x80057E40` | `ReadCDData` | `sectors = (size + 0x7FF) >> 11`, puis `CdControl(0x02, cdlFile, result)`, attente `CdSync`, reessai sur statut 5, `CdRead(sectors, buffer, 0x80)`, et si `mode == 0` attente `CdReadSync` **avec `VSync(0)`**. |
| `0x80057674` | `FUN_80057674` | Setup GTE: `SetGeomOffset`, `SetGeomScreen`, `SetFarColor(0x80,0x80,0x80)`, `SetBackColor(0x80,0x80,0x80)`, matrice couleur, `RotMatrix`/`SetLightMatrix`, `RotMatrix`/`SetRotMatrix`, puis ecriture directe de registres COP2 en `0x1F8000xx`. 3 appelants. |
| `0x80058D64` | `FUN_80058d64` | Initialise 5 `POLY_FT4` consecutifs a partir de `DAT_800A8894` via `SetPolyFT4`/`SetShadeTex`/`SetSemiTrans`, avec des coordonnees et une tpage constantes. |

Point important pour le portage: `WaitSearchFile` et la boucle `CdSearchFile`
de `main` ne contiennent **aucun `VSync`**. Sur le baton de frame desktop, ces
deux boucles ne rendraient jamais la main a l'hote si le fichier n'etait pas
resolu. `ReadCDData`, lui, appelle `VSync(0)` et ne pose pas ce probleme.

## Architecture du runtime

`FUN_80049504 @ 0x80049504`, 540 octets et **42 appelants**, est la fonction
structurante de l'overlay. Elle `malloc` un bloc dimensionne par `arg3` arrondi
a 4 octets plus `0x18`, y ecrit `arg1` en tete, le pointeur de contexte, le
`callback` et `arg4`, met le contexte a zero, puis insere le bloc dans une liste
chainee choisie par `arg2` via la table `DAT_800798FC`.

C'est donc un allocateur d'objets-taches a callback, range par categorie. Tout
l'ecran titre est construit dessus: `main`, `FUN_80021dd0`, `FUN_80056dc0`,
`FUN_80058a9c` et `FUN_800376c0` en dependent tous.

`FUN_80038228 @ 0x80038228`, 1116 octets et 11 appelants, est une machine a
etats pilotee par `param_1` avec `DAT_80083454` comme etat courant et
`_DAT_800834B4` comme parametre de vitesse. Les cas observes enchainent
`SetDispMask(0)` et `SetDispMask(1)`, changent la `tpage` de
`POLY_GT4_800B9518` et enregistrent `FUN_80038684` via `FUN_80049504`. Sa
semantique complete n'est pas fermee.

## Couverture du SDK C#

Sur tout le chemin d'init, trois fonctions seulement manquent a
`PsxSdkMonogame`:

| Manquante | Famille |
|---|---|
| `InitGeom` | libgte |
| `SetFarColor` | libgte |
| `srand` | libc |

Sont deja presentes et utilisables: `ResetCallback`, `ResetGraph`,
`SetDispMask`, `PadInit`, `CdInit`, `CdSearchFile`, `CdControl`, `CdSync`,
`CdRead`, `CdReadSync`, `ClearImage`, `DrawSync`, `InitHeap`, `FntLoad`,
`FntOpen`, `SetGeomOffset`, `SetGeomScreen`, `SetBackColor`, `SetColorMatrix`,
`SetLightMatrix`, `SetRotMatrix`, `RotMatrix`, `SetPolyFT4`, `SetPolyGT4`,
`SetShadeTex`, `SetSemiTrans`, `LoadClut`, `VSync`.

## Restant ouvert

| Adresse | Symbole | Role apparent, non ferme |
|---|---|---|
| `0x80049504` | `FUN_80049504` | allocateur d'objets-taches, 42 appelants |
| `0x80038228` | `FUN_80038228` | machine a etats d'affichage/fondu, 11 appelants |
| `0x80037388` | `FUN_80037388` | tache enregistree puis appelee directement par `main` |
| `0x80056DC0` | `FUN_80056dc0` | appelee avec 8 arguments, enregistre `LAB_80056D84` |
| `0x80021DD0` | `FUN_80021dd0` | premiere fonction de la boucle principale; interprete le script de chargement d'images de `TITLE.B` selon `docs/TITLE_B_FILE_FORMAT_ANALYSIS.md` |
| `0x800587A8` | `FUN_800587a8` | appelee deux fois par tour de boucle |
| `0x80058A9C` | `FUN_80058a9c` | charge `CHR_DATA/EFF_AUTO.B` et `CHR_DATA/CH_EF_P0.B` |

`TITLE.EXE` compte 1 251 fonctions, dont 860 deja nommees et environ 391 encore
brutes.

## Blocages

- `BLOCKED`: la semantique de `FUN_80049504` doit etre fermee avant tout portage
  du corps de `main`, puisque l'init comme la boucle principale en dependent.
- `BLOCKED`: `FUN_80038228` conditionne l'affichage; son etat `DAT_80083454`
  n'est pas ferme.
- Les deux boucles d'attente disque sans `VSync` ne posent pas de probleme de
  fidelite: l'adaptation desktop de `CdSearchFile` repond immediatement, donc la
  boucle sort au premier tour des que le fichier est resolu. Elle ne gelerait
  l'hote que si le fichier etait absent de la sortie de build. `data/SELECT.EXE`
  et `data/SUB/TITLE.B` existent bien, `TITLE.B` faisant exactement les `0x25000`
  octets annonces par `TITLE_B_FILE_FORMAT_ANALYSIS.md`, mais le `.csproj` ne
  copie aujourd'hui que les deux `.STR`. Le prochain lot devra etendre la copie.
