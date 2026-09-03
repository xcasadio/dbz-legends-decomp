# VS.EXE — tranche 3 : les données de l'image

État : **implémenté** — `PsxSdkMonogame/PsxExeImage.cs`, chaîné en dernier dans les cinq
`ResolveAddress`, armé par chaque `Activate*` de `PsxSdkBridges`, gardé par
`--validate-exe-image` (14 contrôles, dont deux témoins négatifs). Décision prise avec
l'utilisateur : **région adossée aux octets de l'image**, et non transcription à la main.

Un aléa que le banc a trouvé à son premier passage : une région déclarée par un initialiseur
statique n'existe pas tant que sa classe n'a pas été touchée, et l'image répond dans
l'intervalle — deux stockages pour une adresse, dans cette fenêtre seulement. La convention du
portage (le `main` touche la classe avant de lire) la couvre ; elle est nommée dans
`PsxExeImage.cs` plutôt que fermée, parce que rien là-bas ne peut la fermer.

La mesure a été faite par quatre surfaces indépendantes, puis attaquée par deux réfuteurs par
surface (une lentille « octets », une lentille « usage »), puis relue par un critique de
complétude. **67 réfutations : 4 bloquantes, 27 majeures, 36 mineures.** Aucune des quatre
surfaces n'a survécu intacte. Ce document ne consigne que l'état APRÈS réfutation.

## Le constat qui déclenche la tranche

Aucun chemin du portage n'ouvre un fichier `.EXE` pour en recopier des octets dans une région.
Confirmé deux fois indépendamment.

Attention à la formulation, qui a été corrigée : `LibGpu.RamRegion` a **deux surcharges**. Celles
déclarées `(adresse, longueur)` allouent un tampon à zéro ; celles déclarées `(adresse, tampon)`
portent un littéral transcrit à la main. Deux régions du portage sont dans le second cas —
`Roster.cs` à `0x80083CF0` (300 o) et `0x80084184` (144 o) — et leurs octets ont été recomparés à
l'image : **exacts au bit près**. Dire « toutes les régions sont à zéro » est faux.

Deux conséquences mesurées :

1. La scène de combat est bloquée dessus. Sa phase 0 indexe `0x8008222C` pour choisir une scène,
   sa phase 1 indexe `0x80081A50` pour charger un fichier ; tables nulles, donc scène 0 et
   index 0 toujours.
2. `Roster` n'efface pas son enregistrement de 300 octets avant de le remplir et **ORe** ses
   drapeaux dans le contenu livré. Sur une région à zéro le OR ne peut rien conserver.

## Étendues, en-têtes, et la frontière .bss

Lire `t_addr` à l'offset 0x18 et `t_size` à l'offset 0x1C ; le corps commence à l'offset 0x800.
Pour les neuf images de `data/`, `0x800 + t_size == taille du fichier` exactement.

| image | chargement | corps | étendue |
|---|---|---|---|
| VS.EXE, TITLE.EXE, GAME.EXE, SP.EXE | `0x80020000` | `0x0E5800` | `[0x80020000, 0x80105800)` |
| SELECT.EXE | `0x80020000` | `0x036000` | `[0x80020000, 0x80056000)` |
| MOVIE.EXE | `0x80020000` | `0x020000` | — |
| DEMO.EXE | `0x80020000` | `0x026800` | — |
| SLPS_003.55 | `0x80020000` | `0x02D000` | — |
| **ENDING.EXE** | **`0x80010000`** | `0x049800` | `[0x80010000, 0x80059800)` |

**`ENDING.EXE` ne se charge pas à `0x80020000`.** Toute formule
`offset = adresse - 0x80020000 + 0x800` codée en dur sera fausse pour elle, et son étendue
chevauche celle de `SELECT.EXE`. L'en-tête doit être lu, jamais supposé.

### Les bornes du .bss — corrigées, elles étaient fausses par extension de signe

L'immédiat d'un `addiu` est **signé**. La première mesure a lu `addiu v0,v0,0xD254` comme une
concaténation (`0x8009` | `0xD254`) au lieu d'une addition, ce qui décalait la borne de 64 Ko.
C'est la même faute que celle qui avait donné deux signatures fausses à `FUN_8003f540` en
tranche 2 : un demi-mot signé lu comme non signé.

| image | boucle crt0 | plage effacée |
|---|---|---|
| VS.EXE | PC `0x80072F50` | `[0x8008D254, 0x800C3DD4)` |
| TITLE.EXE | PC `0x80068FF4` | `[0x80083310, 0x800B9EF0)` |
| SELECT.EXE | PC `0x800347C4` | `[0x80055A78, 0x8006929C)` |

Trois corroborations indépendantes pour VS : le dernier octet non nul de la donnée est à
`0x8008D214`, soit `0x3F` octets d'alignement avant la borne ; `gp` vaut `0x8008D0FC`, placement
canonique en tête de `.sdata`/`.sbss` ; et les quatre globales qu'`InitHeap` écrit
(`0x8008D2E0`..`0x8008D2F8`) tombent au-dessus de la borne corrigée et sous l'ancienne.

Il n'existe **pas** de « queue de `.data` nulle » de 64 Ko : c'est `0x3F` octets d'alignement.

**Le nettoyage `.bss` du crt0 est un no-op vis-à-vis des octets d'image, pour les trois overlays.**
VS : 224 128 octets dans l'image sur la plage effacée, **0 non nul**. TITLE : 224 224 octets,
**0 non nul**. SELECT : 1 416 octets tombent dans l'image, **tous nuls**. Un mécanisme
d'adossement n'a donc pas à modéliser ce nettoyage.

La queue de fichier n'est pas du `.bss` : c'est du bourrage de secteur, `0x6BD` octets pour VS et
TITLE, `0x589` pour SELECT — chacun strictement sous 2048, et chaque `t_size` est multiple de
`0x800`.

## Les contraintes de conception, chacune fermée par une mesure

1. **Les octets de l'image sont ÉCRITS par le jeu.** Prouvé sur `0x80082164` : `lbu` / `ori 0x80`
   / `sb` à `0x80035428`-`38`, et le bit est rabaissé par `lbu` / `andi 0x7F` / `sb` à
   `0x800354B4`-`C0`. C'est le verrou « déjà pris » que la scène de combat décrivait.
   → l'adossement doit fournir une **copie mutable, réarmée à chaque `Activate*`**, jamais un
   `static readonly` initialisé une fois. La console recharge l'image à chaque `LoadExec`.

2. **Il y a deux cartes d'adresses, et une seule des cinq les consulte toutes les deux.**
   `VS_EXE_exe.ResolveAddress` appelle `LibGpu.RamResolve` en premier ; ceux de TITLE, SELECT,
   MOVIE et SLPS ne l'appellent **jamais**. Adosser les régions `RamRegion` ne changerait donc
   rien pour quatre overlays sur cinq.
   → l'adossement doit passer par la chaîne `??` de chaque `ResolveAddress`, uniformément.

3. **Dans une chaîne `??` c'est le PREMIER qui gagne ; dans `RamResolve` c'est la BASE LA PLUS
   HAUTE.** Une ligne d'image posée à `0x80020000` se comporterait comme un fond de carte qui ne
   préempte rien dans le registre — mais elle gagnerait sur `FileIo`, `FighterSetup`, `AnimVm`,
   `SharedHighRam` et `PsxHeap`, qui sont chaînés APRÈS `RamResolve` et dont certains spans
   tombent dans l'image (`0x8008DA48`).
   → chaîner l'image **en dernier**, après `PsxHeap`, et **ne pas** la déclarer en `RamRegion`.

4. **Le registre de régions est plafonné à 64 lignes et le dépassement est SILENCIEUX**
   (`LibGpu.cs:1605` et `:1635` — le tampon est renvoyé sans être enregistré, sans exception).
   23 sites d'appel à `RamRegion` aujourd'hui dans tout l'arbre : 16 dans les trois dossiers
   d'overlay, 3 dans `LibGs.cs`, 1 dans `PsxHeap.cs`, 1 dans `SharedHighRam.cs`, 2 dans
   `Validation/`.
   → une ligne par overlay, jamais une par symbole.

5. **Le tas de VS.EXE recouvre le haut de l'image.** `start` arme `[0x800C3DD8, ...)` et `main`
   le réarme à `0x00010000` ; `PsxHeap` n'en tient qu'un. Aucun des deux ne recouvre le bloc de
   tables `0x80081A50..0x80082720`. Mais tant que l'arme de `start` tient, le tas recouvre
   268 840 octets de l'image dont 7 635 non nuls — tout le bloc de queue de 16 Ko.

6. **La bascule d'overlay était rompue sur le seul chemin de jeu vers VS.EXE.** Corrigé et gardé
   par `custom-tools/scripts/check_overlay_handover.py`. Sans cela, tout mécanisme branché sur
   `Activate*` serait resté inerte pour VS.EXE.

## Ce qui est fermé, et où ne pas remettre de travail

- **SELECT.EXE est intégralement clos** : ses douze régions modélisées ont **0 octet** de
  recouvrement avec son étendue d'image. Aucun adossement ne peut y toucher une région déclarée.
- **MOVIE.EXE et SLPS_003.55 sont clos** : seul `g_MovieVlcBuffer0` mord l'image (208 et 1 900
  octets), **tous nuls**.
- **Les régions déclarées de VS et TITLE ne changeraient pas** : sur les 11 qui tombent dans
  l'étendue de leur image, 9 recouvrent des octets tous nuls et les 2 autres portent déjà les
  octets exacts de l'image.

Autrement dit : **l'adossement n'a pas de moitié « pré-remplir les régions déclarées »**. Il se
réduit au repli en fin de chaîne. C'est le contraire de ce que j'avais annoncé avant la mesure.

## Ce qui reste ouvert

- **Volume réel de l'adossement** : plancher mesuré d'adresses formées par le code, tombant dans
  la donnée initialisée non nulle et couvertes par aucune région — VS **436**, TITLE **417**,
  SELECT **369**. Plancher seulement : le décodeur ne suit ni le gp-relatif, ni une base chargée
  depuis la mémoire, ni une construction en plus de deux instructions.
- **Le bloc de 16 Ko `[0x80101800, 0x80105800)`**, identique octet pour octet entre VS, TITLE,
  GAME et SP. Deux sites par image chargent `0x80101BA4` ; la surface qui l'a trouvé le dit
  « vivant », sa réfutation dit que la valeur est rangée et jamais relue. **Non tranché.**
- **Les littéraux transcrits n'ont pas été audités** : 4 vérifiés sur 75 déclarations. Un
  adossement les remplacerait, donc il faut d'abord savoir qu'ils sont tous exacts.
- **Une collision de base latente** : `Validation/RenderPipelineValidation.cs:22` déclare
  `0x800B0000` en la disant « clear of every buffer this port declares », alors que
  `SELECT_EXE/SelectScreen.cs:26` déclare la même base sur `0x50000` octets. Latent parce que les
  deux vivent dans des cartes différentes ; un adossement qui unifie les cartes le réveillerait.

## Les tables, après réfutation

Ce que la réfutation a corrigé sur les formes annoncées :

- `0x80082264` compte **au plus 54 entrées** (`0x80082264`..`0x800822D0`), pas 72 — ce qui
  correspond exactement aux emplacements CH_BIN 0..53. Au-delà les valeurs deviennent négatives
  (`0xFE70` = −400), donc d'un autre type.
- Le **plafond d'identifiant de personnage de 38** ne repose plus que sur la table AT elle-même.
  L'affirmation qu'il pilotait aussi la table d'octets de `0x800835CC` est réfutée : l'index de
  celle-ci est un `u16` lu à l'offset 0 de `*(0x8008D16C)`, sans lien montré avec `+0x15BC`.
- La « structure de caméra » n'a **aucun site de lecture** et les octets montrent que la cible est
  le même objet que celui qui porte `+0x74`. Nom spéculatif, à ne pas reprendre.
- Le balayage exhaustif des 235 008 mots du corps confirme qu'aucun mot ne vaut une adresse de
  `[0x80081900, 0x80082800)` : les tables ne sont atteintes que par construction `lui`/`addiu`.
- Aucun site n'atteint `[0x800823C0, 0x800826E0)` — cette table-là est **morte**.
