# VS.EXE — tranche 3 : les données de l'image

État : plan, mesuré, non implémenté. Décidé avec l'utilisateur le 2026-09-02 :
**région adossée aux octets de l'image**, et non transcription à la main table par table.

## Le constat qui déclenche la tranche

Aucune donnée `.rodata` ou `.sdata` des trois overlays n'est lue aujourd'hui. Chaque
`LibGpu.RamRegion(adresse, taille)` alloue un `new byte[taille]` à zéro, et il n'existe nulle part
de chemin qui recopie les octets du fichier EXE dedans. Les tables de l'image lisent donc zéro,
en silence, exactement comme les adresses VS.EXE lisaient zéro avant que `PsxSdkBridges` ne
connaisse l'overlay.

Deux conséquences déjà mesurées, et la seconde est plus large que la première :

1. La scène de combat est bloquée dessus. Sa phase 0 choisit un identifiant de scène en indexant
   `0x8008222C`, sa phase 1 charge un fichier en indexant `0x80081A50` ; avec des tables nulles,
   la phase 0 choisit toujours 0 et la phase 1 charge toujours l'index 0.

2. `Roster` a établi que l'original **n'efface pas** l'enregistrement de 0x80083CF0 avant de le
   remplir, et que ses deux séries de drapeaux sont **ORées, pas affectées** — l'image livre un 1v1
   (identifiants 1 et 9) et tout ce qui dépasse le roster garde le contenu livré. Sur une région à
   zéro, ce comportement est strictement inobservable : le OR ne peut rien conserver.

## Ce qui existe déjà et n'est pas à réinventer

`LibGpu.RamRegion(int psxAddress, byte[] buffer)` accepte un tampon **déjà rempli** et le déclare
tel quel. Le mécanisme est donc en place ; seul le remplissage manque.

`Roster.cs` s'en sert déjà, en codant ses deux tables en dur :
`DAT_80083cf0` (300 o) et `DAT_80084184` (144 o). Les 444 octets ont été recomparés à
`data/VS.EXE` : **identiques au bit près**. La transcription à la main marche donc, et surtout
elle se vérifie par script — ce qui reste vrai quel que soit le mécanisme retenu.

## Étendues mesurées

En-tête PSX de `data/VS.EXE` : adresse de chargement `0x80020000`, corps `0xE5800` octets à partir
de l'offset fichier `0x800`. La conversion est donc `offset = adresse - 0x80020000 + 0x800`.

### Le bloc de tables 0x80081A50..0x80082720 — 3280 octets, 28,6 % de bourrage

| adresse | forme | contenu lu dans l'image |
|---|---|---|
| `0x80081A50` | pas 0x1B, chaînes | les 53 chemins `\CH_BIN1..3\*.BIN;1`, complétés de zéros |
| `0x80082164` | pas 3 | paires de petits identifiants (`01 00 00`, `01 02 00`, `31 01 00`…) |
| `0x800821DC` | pas 2 | `40 00` comme sentinelle, sinon des paires (`36 06`, `37 05`, `38 03`) |
| `0x8008222C` | octets | valeurs 0..7, indexées par identifiant de scène |
| `0x80082264` | shorts | 0, 500, 650, 600, 650, 400, 600, 650, 0, 400, 550… — une valeur par personnage |
| `0x800822D0` | pas 6 | triplets de shorts (`400,0,0`), (`500,0,-400`), (`400,0,400`) — des positions |
| `0x800826E0` | 16 demi-mots | palette 16 couleurs, BGR 15 bits (`0000 FFFF 873A 9B5A 	…`) |
| `0x80082700` | 16 demi-mots | seconde palette de même forme |

Attention : `0x800822F0` et au-delà contient `0x80037374`, `0x8003737C`, `0x800373A0` — des
**pointeurs**, donc la table à pas 6 s'arrête avant. L'étendue exacte reste à fermer par l'usage,
pas par la forme.

### Les autres blocs de chemins CD

140 chemins terminés par `;1` au total dans l'image (la reconnaissance en annonçait 136 ; c'est
140, recomptés). Ils forment cinq groupes contigus :

| adresse | nombre | contenu |
|---|---|---|
| `0x80020424` | 9 | `\SUB\*.B` et les cinq `cdrom:\*.EXE` |
| `0x800208E0` | 10 | `\CHR_DATA\*` et `\SOUND\*` |
| `0x80081A50` | 53 | `\CH_BIN1..3\*.BIN`, pas 0x1B — la table des personnages |
| `0x80082B10` | 16 | `\STG\STG1..8MD.B` puis `\STG\STG1..8TX.B`, pas 0x12 |
| `0x80083320` | 38 | `\AT1\*.B` et `\AT2\*.B`, pas 0x12 — une entrée par personnage |

Les 38 entrées AT et les valeurs de `0x80082264` ont le même cardinal, et le plafond
d'identifiant de personnage de 38 que `Roster` a repris de la reconnaissance est fermé une
troisième fois ici. La table `CH_BIN` en compte 53, dont trois `CH_NO.BIN` en position 0, 8 et 40 :
ce sont des emplacements vides, pas des personnages.

## Ce qu'il reste à décider dans l'implémentation

- **Où vit la propriété de l'image.** Un seul fichier doit posséder `data/VS.EXE` et servir les
  tranches d'octets ; aucune tranche de portage ne doit ouvrir le fichier.
- **Pas de région chevauchante.** `LibGpu` apparie les régions par référence de tampon et une
  seconde déclaration sur la même adresse *ajoute une ligne* au lieu de remplacer — la résolution
  peut alors élire le mauvais stockage. L'adossement doit donc **remplir les régions existantes**,
  pas en superposer de nouvelles.
- **Quelles régions adosser.** Seules celles dont l'adresse tombe dans l'étendue du fichier. La RAM
  de travail (0x801Fxxxx, 0x8008Dxxx, le tas) reste à zéro : sur console elle n'est pas initialisée
  par l'image non plus.
- **La même question se pose pour TITLE.EXE et SELECT.EXE**, dont les tables lisent zéro aujourd'hui
  sans que ce soit visible. À traiter comme une conséquence, pas comme une extension de portée.
