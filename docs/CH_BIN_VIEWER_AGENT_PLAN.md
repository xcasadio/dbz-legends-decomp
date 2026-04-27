# Plan Agent IA - Viewer CH_BIN final-output

## Objectif

Afficher les elements finaux visibles des fichiers `CH_*.BIN` et `IN_*.BIN` dans `DbzLegendsAnalyser`:

- vraies textures / pages texture utiles
- geometrie 3D reconstruite a partir du pipeline runtime prouve
- animation visible liee a la VRAM / CLUT

Le but n'est plus de montrer les donnees brutes sauf si une preuve manque.

## Sources de verite

- Reference compacte: `docs/structure-ch-bin-files.md`
- Chronologie / runtime: `docs/structure-ch-bin-files.history.md`
- Notes repo: `/memories/repo/ch-bin-analysis.md`
- Fonction runtime cle: `RenderBattleScene3D` a `0x80035a04`

## Contraintes non negociables

- Utiliser la base compile-time `0x801A3800` pour convertir les pointeurs en offsets fichier.
- Respecter `CHBinMeshEntry = 7 dwords`.
- Respecter les strides prouves `8 / 16 / 8` pour `vertex / mesh / lighting`.
- Respecter le framing `AnimStream`: `0x0000`, `countdown`, `words...`, `0x0000`, `next_countdown`.
- Ne rien inventer: toute semantique doit etre classee `CERTAIN`, `PROBABLE`, ou `INCONNU`.

## Etat livre

Le viewer CH_BIN final-output est maintenant structure autour de:

- `custom-tools/DbzLegendsAnalyser/PsxTools/ChBinLoader.cs`
	parse CH_BIN, expose les segments, batches d'animation, helpers d'entree
- `custom-tools/DbzLegendsAnalyser/PsxTools/ChBinVisuals.cs`
	reconstruit les modeles renderables, decode les materiaux, maintient une VRAM virtuelle et rejoue `load_set` / `tex_set`
- `custom-tools/DbzLegendsAnalyser/DbzLegendsAnalyser/Viewers/CH_BIN_View.cs`
	affiche une page summary, une galerie de textures et des pages modele 3D
- `custom-tools/DbzLegendsAnalyser/DbzLegendsAnalyser/Game1.cs`
	decouverte automatique des `.BIN` dans `CH_BIN1`, `CH_BIN2`, `CH_BIN3`

## Preuves runtime verrouillees

### CERTAIN

- `meshSegment + 0x04` indexe une table de coordonnees 6 octets par sommet (`x,y,z` en `int16`).
- le stream `lighting` consomme un enregistrement 8 octets par polygone pour former le rectangle UV.
- le stream repete 4 octets fournit les couleurs visibles par sommet.
- `load_set` charge des donnees texture / CLUT visibles dans la VRAM.
- `tex_set` pilote une animation de ligne CLUT / palette, pas un upload image generique.
- les iterateurs runtime reutilisent une meme ligne tant que `countY` n'est pas epuise.

### PROBABLE

- `colorTable.word1 = CBA` et `colorTable.word2 = TPAGE` pour le binding materiau texture.

### INCONNU

- replay complet des transformations de body parts
- replay complet des opcodes XY / UV secondaires non encore relies a un effet visuel exact
- semantique exhaustive de tous les opcodes d'animation encore non prouves

## Ce que le viewer doit montrer

### Minimum attendu

1. Une page `Summary` courte orientee rendu final.
2. Une page `Textures` qui montre les pages texture construites depuis la VRAM virtuelle.
3. Une page 3D par entree renderable avec modes `wireframe`, `solid`, `textured`.
4. Une mise a jour visuelle quand `load_set` / `tex_set` modifient la texture visible.

### Ce qui reste volontairement hors scope tant que non prouve

1. noms semantiques inventes pour les os / parties du corps
2. interpolation d'animation inventee
3. reinterpretation libre des opcodes sans preuve ASM ou XREF

## Sequence de travail pour un agent

1. Relire `docs/structure-ch-bin-files.md` avant toute modification format.
2. Verifier dans Ghidra si la nouvelle semantique est `CERTAIN`, `PROBABLE`, ou `INCONNU`.
3. Modifier `ChBinLoader.cs` uniquement si le parsing ou le sizing d'opcodes change.
4. Modifier `ChBinVisuals.cs` si la reconstruction visuelle change.
5. Modifier `CH_BIN_View.cs` seulement pour exposer le rendu final a l'utilisateur.
6. Compiler avec `dotnet build custom-tools/DbzLegendsAnalyser/DbzLegendsAnalyser.slnx -c Debug`.
7. Si une preuve manque, garder une limitation explicite dans la page summary plutot qu'inventer un rendu faux.

## Prochaines extensions recommandees

1. Valider visuellement plusieurs fichiers `CH_*.BIN` et relever les cas ou le binding texture reste vide.
2. Fermer ou invalider la relation `colorTable -> TPAGE/CBA` sur un corpus plus large.
3. Ajouter le replay des transformations visibles si une preuve directe relie les opcodes d'anim aux matrices / positions ecran.
4. Ajouter la capture d'une frame de reference pour comparer le viewer avec l'affichage runtime.

## Criteres d'acceptation

- La solution compile.
- Les fichiers `CH_BIN1/2/3` apparaissent dans la liste de gauche.
- Le viewer ouvre un `.BIN` sans crash.
- Le viewer affiche au minimum `Summary`, `Textures` et une page modele quand des donnees renderables existent.
- Les limites encore non prouvees sont indiquees explicitement.