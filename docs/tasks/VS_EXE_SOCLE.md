# VS.EXE — le socle, et le point de reprise

Session autonome, mode AUTO annonce. Ce document remplace l'etat de reprise de
`VS_EXE_TRANCHE4.md` pour tout ce qui a bouge; le plan de vagues de ce document-la
reste valable.

## Fait, verifie, committe

| | |
|---|---|
| Outillage de couture | `check_vs_dispatch.py` sort enfin `1` en cas de desaccord; `check_duplicate_symbols.py` rapatrie dans `custom-tools/scripts/` |
| `RotAverage3` | ajoute a `LibGte`, marque `PARTIAL`, banc `--validate-gte-rotavg` |
| Scratchpad GTE | dix-neuf mots partages dans `Scratchpad.cs` a la racine |
| Doublons intra-VS | 0 stockage duplique, 0 type divergent (etait 12 et 2) |
| `SoundState.cs` | le workspace son declare: taille 0x194 fermee deux fois, cinq bancs CD nommes par leurs litteraux |
| `FUN_8005f704` | translittere depuis l'image, banc `--validate-sound-loader` avec temoin negatif |

Acceptation a chaque etape: build propre, douze bancs verts, trois checkers de
couture verts, `--diag-select 400` = **49396** pixels, inchange.

## Ce qui a change par rapport au plan de tranche 4

**Le scratchpad ne se deplace pas en bloc.** Le plan disait que VS utilise « les
memes 124 symboles » et prescrivait un deplacement complet. La verification
adresse par adresse le refute: c'est de la RAM rapide **reutilisable**, et deux
overlays y rangent legitimement des choses differentes. Trois adresses restent
declarees des deux cotes, chacune documentee a son site:

- `0x1F80012C` — TITLE y tient son compteur d'image de chargement 0..2, VS autre chose;
- `0x1F800120` — TITLE y seme un offset geometrique que sa tache camera promeut, VS y calcule un Y d'horizon;
- `0x1F8000D0..E0` — les deux `SetupGeometry` y ecrivent pareil, mais seul TITLE relit; le cote lecture est ce qui fixe le sens.

**Et une adresse n'est pas un doublon mais un defaut**: `0x1F80009C`. TITLE
declare `VECTOR_1f800094`, un `LibGte.VECTOR` de 16 octets — l'octet `0x9C` est
donc son champ `.vz`. VS declare un `int DAT_1f80009c` par-dessus et **le lit
alors que personne ne l'ecrit**. Aliasing, consigne, pas corrige.

**Les 17 « doublons » inter-overlay n'en sont pas.** Chaque paire
`TaskSystem`/`PadInput`/`PrimitivePools` est a une adresse **differente** dans
chaque overlay — `g_TaskListHead` est `0x80079854` dans VS et `0x80083b3c` dans
TITLE. Ce sont des homonymes: deux overlays lies separement, les noms C# empruntes
a TITLE pour la lisibilite, chacun dans son namespace. Les fusionner serait faux.
Le balayage compare desormais des **adresses** et les etiquette comme tels.

## La decouverte qui debloque la suite

`VS_EXE_TRANCHE4.md` note Ghidra comme source de verite, et le serveur ReVa etait
injoignable cette session. **PCSX-Redux, lui, repond, et sa RAM contient VS.EXE
charge.** C'est une source de preuve equivalente et verifiable:

```
mcp__pcsx-redux__pcsx_get_status      -> debugger: true, running: false (en pause, pas vide)
mcp__pcsx-redux__pcsx_disassemble     -> desassemblage MIPS a n'importe quelle adresse
mcp__pcsx-redux__pcsx_read_memory     -> octets bruts, pour les tables
```

Controle de validite fait avant de s'en servir: `FUN_80061ed8 @ 0x80061ED8`
desassemble en exactement 68 octets avec son `beqz $v0` rebouclant sur
`CdSearchFile` — la boucle retiree au lot 1, conforme au commentaire du port.

**`gp = 0x8008D0FC`.** C'est la cle qui manquait: tout acces `0xNNN(gp)` se resout
en symbole. `0x188(gp)` = `0x8008D284`, `0x244(gp)` = `0x8008D340`.

## Ce que le portage de `FUN_8005f704` a etabli

Elle est faite, et elle **ne suffit pas a faire dessiner la scene**. Ce n'est pas
une deception, c'est un resultat: le blocage est ailleurs, et on sait ou.

Ses appelees VAB — `SsVabOpenHeadSticky`, `SsVabTransBody`, `SsVabTransCompleted`,
nommees par la forme de leurs arguments et non par un symbole lu, ce que l'en-tete
du fichier dit — sont des stubs `return default` dans `LibSnd`. En suivant le
trace: la machine atteint l'etat 7 et y reste, parce que le test « pas encore »
de l'etat 7 est exactement `SsVabTransCompleted` rendant 0.

**Le blocage restant est libsnd, pas cette fonction.** Le banc
`--validate-sound-loader` epingle ce blocage comme un fait d'aujourd'hui: quand
quelqu'un implementera libsnd, c'est cette assertion-la qui echouera, et c'est le
signal qu'il faudra la mettre a jour.

Reste aussi le second verrou, independant: `FUN_8005a5b0` (`BattleManager.cs`,
8500 octets), sans lequel aucun combattant ne bouge et la jauge ne monte jamais.

## Le prochain pas, precisement

`FUN_8005f704` bloque **tout** le rendu de scene: tant qu'elle rend 0, la phase 1
du chargeur ne passe jamais, la tache de scene n'est jamais creee, et
`RenderBattleScene3D` — qui est deja porte et fonctionnel — n'est jamais appele.
C'est pour cela que l'ecran est bleu apres la selection des joueurs.

Ce n'est pas une petite fonction. Etendue `0x8005F704`..`~0x8005FB90`, soit
**~1168 octets**, avec une table de saut de huit cas a `0x80020A84`:

| cas | cible | | cas | cible |
|---|---|---|---|---|
| 0 | `0x8005F754` | | 4 | `0x8005F9B0` |
| 1 | `0x8005F784` | | 5 | `0x8005FA34` |
| 2 | `0x8005F870` | | 6 | `0x8005FAEC` |
| 3 | `0x8005F8F4` | | 7 | `0x8005FB54` |

Elle travaille sur le **workspace son** pointe par `0x188(gp)` = `0x8008D284`, aux
offsets `+0x12A`, `+0x12C`, `+0x148`, `+0x158`, et touche `0x244(gp)` =
`0x8008D340`. C'est donc bien le groupe `D_SoundTaskCd` du plan.

**Donc l'ordre est: `SoundState.cs` d'abord** — le point 2 du socle, non fait —
puis la machine a etats. Declarer `DAT_8008d284` et ses offsets avant de porter
la fonction qui les lit est exactement ce que le socle existe pour eviter de
rater. La porter sans eux reviendrait a inventer des noms pour des offsets, ce
que le mandat refuse.

## Ce qui attend toujours l'utilisateur

- **Pousser** le sous-module `PsxSdkMonogame` puis le superprojet. Rien n'a ete
  pousse; un clone frais ne construit pas.
- `AnimVmInterpreter` garde des scalaires prives pour `+0x08` et `+0x14` de la
  region `0x800990C0` que `BattleScene` modelise en une `RamRegion` — deux copies
  des memes octets. Les largeurs divergent en plus (la region documente `+0x08`
  comme un octet, le scalaire est un `int`) et l'original ecrit cette region a
  quatre largeurs differentes. Fermer ca demande l'image; c'est desormais possible
  via PCSX-Redux, ce ne l'etait pas quand le constat a ete fait.
- `data/tracks/` (211 Mo), deux `.palettes.json` de `CH_BIN1` et un fichier de
  `DOC` ne sont pas revenus de la restauration.
