# Latence de chargement disque

## Symptome

Un seul appui sur Start passait **les deux** films de demarrage, alors que
l'original en demande un par film.

## Ce qui n'etait pas en cause

Le code translitere. Verifie directement sur la console dans PCSX-Redux:
apres 30 secondes avec Start maintenu depuis le reset, le PC se trouve dans
`TITLE.EXE` (`0x8006E444`) et le tampon pad de cet overlay, `0x800920D4`, vaut
`0xFFFFF7FF`. Or `DBZ_OP.STR` dure 63 secondes: elle a donc bien ete sautee
elle aussi.

**La vraie console passe les deux films si l'on maintient Start.** Les deux
boucles de lecture testent le pad exactement comme le port le fait.

## Ce qui etait en cause

Le temps. Cote port, chaque operation disque etait instantanee:

```
[overlay] t=0ms   LoadExec -> MOVIE.EXE
[overlay] t=66ms  LoadExec -> TITLE.EXE
```

66 millisecondes entre le moment ou un overlay cesse de lire le pad et celui
ou le suivant commence. Un appui humain dure 100 a 300 ms: il couvre les deux.

Sur console, ce meme intervalle contient le chargement de l'overlay, la
recherche du fichier `.STR`, le seek et la mise en tampon du flux.

## Mesure

Le compteur de cycles de PCSX-Redux donne une mesure exacte, independante de
toute latence d'outillage. Points d'arret sur le site d'appel de
`ShutdownAndLoadExecutable` puis sur le `main` de l'overlay charge:

| Overlay | Taille | Cycles | Duree |
|---|---:|---:|---:|
| `MOVIE.EXE` | 133 120 o | 34 496 533 | 1018,5 ms |
| `TITLE.EXE` | 942 080 o | 123 154 369 | 3636,2 ms |

Horloge PSX: 33 868 800 Hz. Les compteurs sont reproductibles au cycle pres
d'une execution a l'autre, la machine etant deterministe.

Mesure complementaire, du `main` de `MOVIE.EXE` au premier test du pad dans
`PlayDbzOpeningMovie`: 20 249 918 cycles, soit **598 ms**. La fenetre reelle
entre les deux films est donc d'environ **1,6 seconde**.

## Modele

Deux mesures, deux inconnues:

```
duree = 587,8 ms + taille / 309 037 o/s
```

Le debit obtenu vaut **309 037 o/s**, soit 301,8 Kio/s. C'est la vitesse reelle
du lecteur en **2x**: `2 x 75 secteurs/s x 2048 octets = 307 200 o/s`. Le modele
retombe donc sur la specification materielle au lieu d'etre ajuste a vue.

Un delai fixe aurait ete faux: `TITLE.EXE` est sept fois plus gros que
`MOVIE.EXE`.


## Le modele est retire

Tout ce qui precede reste vrai de la console et de la mesure. Ce qui suit a
change: `LibCd.WaitDiscLoad` n'existe plus.

La decision est celle de l'utilisateur, et elle est de principe: le port ne
simule pas le lecteur CD. Un temps de chargement modelise est une simulation de
materiel, et la regle 14 du mandat la refuse des lors qu'un contrat observable
equivalent suffit cote desktop. Bruler des frames pour reproduire une attente
qui n'a aucune cause ici — le fichier est simplement lu — tombe exactement
sous cette regle. Retire: la fonction, sa signature, ses deux constantes et ses
cinq sites d'appel `LoadExec`, dont les commentaires affirmaient que la latence
etait ce qui empechait le double saut. `LibDs.DiscFileSize` reste en place mais
n'a plus d'appelant.

## Ce qui le remplace

Le defaut n'etait pas que le chargement soit rapide; il etait qu'un appui
unique soit consomme par deux overlays. C'est un probleme d'entree, corrige
cote entree: `PadInputBackend.MuteUntilRelease()` fait publier a la manette
« aucun bouton enfonce » jusqu'a ce que l'echantillon physique repasse a zero.
Les quatre `PsxSdkBridges.Activate*`, que tout `LoadExec` traverse, l'arment
apres le basculement — donc l'appui qui a **cause** la transition n'est pas
avale, seul son report sur l'overlay suivant l'est.

Trois points ont failli etre manques et meritent d'etre ecrits:

- **La detection de front n'aurait pas suffi.** `TITLE.EXE` et `VS.EXE` en font
  deja (`ProcessPadInput @ 0x800578A8`, `FUN_80061800 @ 0x80061800`), et elle
  est mise en defaut precisement ici: leur etat precedent vaut 0 a la premiere
  frame d'un overlay neuf, donc un bouton encore tenu produit un front montant
  fantome. Le verrou ferme les tests de niveau et ce front d'un seul geste.
- **`Poll()` et le fil runtime tournent en concurrence.** Le baton de frame ne
  serialise que la fenetre de `Draw` — `ReleaseGame()` est la derniere chose que
  fait `Game1.Draw`, et `Poll()` est dans `Game1.Update`. Sans section critique
  partagee, un `Poll` ayant deja echantillonne un mot maintenu pouvait le
  publier apres le retour de `MuteUntilRelease`, soit exactement la frame que le
  verrou existe pour retenir.
- **Un banc qui n'observe que le mot publie ne prouve rien.** Sans touche
  enfoncee, le mot publie vaut `0xFFFFFFFF` que le verrou soit arme ou non. D'ou
  l'accesseur `PadInputBackend.MuteActive` et le banc `--validate-pad-mute`, qui
  assertent la **transition**. Controle negatif execute: neutraliser la ligne
  `s_muteUntilRelease = false;` de `Poll` fait echouer la branche a vide en
  `2 echec(s)`, code de retour 1. Le banc mord.

## Validation

Meme commande avant et apres, `DBZ_OVERLAY_DIAG=1 DBZ_PAD_FORCE=0x0800`, Start
tenu pour la vie du processus:

| | `MOVIE.EXE` | `TITLE.EXE` | ecart |
|---|---:|---:|---:|
| verrou absent | 153 ms | 219 ms | **66 ms** |
| verrou en place | 136 ms | 76 678 ms | **76,5 s** |

Les 66 ms sont exactement le chiffre releve en tete de ce document. Apres, la
seconde video se joue en entier. `t(MOVIE.EXE)` reste petit dans les deux cas:
la premiere video est bien sautee, l'appui qui a cause la transition n'est pas
mange.

**Ce n'est pas un seuil de non-regression.** Seul l'ordre de grandeur porte:
des dizaines de secondes contre des dizaines de millisecondes. La valeur exacte
depend de la vitesse de decodage MDEC de la machine — une seconde execution du
meme binaire a donne 64 069 ms, soit environ 14,8 images/s pour les 945 images
que compte `--validate-dbz-op`. Attendre 60 a 80 s, pas 76 678 ms.

Bancs: les quatorze existants au vert, plus `--validate-pad-mute` dans ses deux
branches.

## L'ecart assume

Sur console, Start **maintenu** de bout en bout saute les deux films; c'est
mesure et c'est ecrit plus haut. Avec ce correctif il n'en saute plus qu'un.

Aucune correction cote entree ne peut faire autrement — `justPressed` aurait
exactement la meme consequence — parce que ce comportement repose entierement
sur le temps qui passe entre les deux overlays. Seul un modele de latence le
rendait, et c'est ce modele qui est refuse.

L'utilisateur a tranche en connaissance de cause: **un appui ne doit sauter
qu'une video a la fois.** C'est la regle du port, et elle prime ici sur la
reproduction du cas « bouton tenu » de la console.
