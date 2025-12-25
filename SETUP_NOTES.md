# DBZ Legends - Notes de configuration

Ce fichier documente la structure du projet de décompilation créée le 19 décembre 2025.

## Structure créée

```
dbz-legends/
├── README.md                    # Documentation du projet
├── Makefile                     # Build system
├── mako.sh                      # Script wrapper build
├── diff_settings.py             # Config asm-differ
├── requirements.txt             # Dépendances Python
├── go.work                      # Go workspace
├── .gitignore / .gitmodules / .clang-format
│
├── bin/                         # Binaires compilateur PSX (à télécharger)
├── config/                      # Configuration
│   ├── jp.yaml                  # Config principale (version JP)
│   └── symbols.*.jp.txt         # Fichiers de symboles par overlay
│
├── include/                     # Headers
│   ├── common.h                 # Types/macros communs
│   ├── game.h                   # Types spécifiques DBZ Legends
│   ├── macro.inc / gte.inc      # Macros assembleur
│   └── psxsdk/                  # Headers SDK PSX
│
├── src/                         # Code source (un dossier par overlay)
│   ├── main/, game/, title/, select/, vs/, sp/, demo/, movie/, ending/
│
└── tools/                       # Outils
    ├── m2ctx.py, symbols.py, decompile.py
    └── builder/                 # Outil Go
```

## Overlays du jeu

| Fichier       | Description                    |
|---------------|--------------------------------|
| SLPS_003.55   | Exécutable principal (boot)    |
| GAME.EXE      | Logique de jeu principale      |
| TITLE.EXE     | Écran titre                    |
| SELECT.EXE    | Sélection de personnage        |
| VS.EXE        | Mode versus                    |
| SP.EXE        | Mode spécial                   |
| DEMO.EXE      | Mode démo/attract              |
| MOVIE.EXE     | Lecteur FMV                    |
| ENDING.EXE    | Séquence de fin                |

## Données du jeu (dossier data/)

- `AT1/`, `AT2/` : Données d'attaques
- `CH_BIN1/`, `CH_BIN2/`, `CH_BIN3/` : Binaires personnages
- `CHR_DATA/` : Données personnages
- `MOVIE/` : Vidéos FMV (.STR)
- `SOUND/` : Audio
- `STG/` : Stages (modèles + textures)
- `SUB/` : Démos et sous-titres

## Prochaines étapes

1. **Initialiser les submodules Git** :
   ```bash
   git submodule add https://github.com/simonlindholm/asm-differ.git tools/asm-differ
   git submodule add https://github.com/matt-kempster/m2c.git tools/m2c
   git submodule add https://github.com/mkst/maspsx.git tools/maspsx
   ```

2. **Analyser avec Ghidra** :
   - Trouver les adresses vram_start de chaque overlay
   - Identifier les segments .text, .data, .rodata, .bss
   - Découvrir les fonctions et symboles

3. **Compléter config/jp.yaml** avec les vraies adresses

4. **Calculer les SHA1** des fichiers originaux

## Basé sur

Template inspiré du projet [FF7 Decomp](https://github.com/Xeeynamo/ff7-decomp) (dossier reference/).
