# Conversion Tasks: DbzLegendsAnalyserWinForms → MonoGame + MGUI

> **Objectif** : Convertir l'application WinForms `DbzLegendsAnalyserWinForms` en application MonoGame avec MGUI dans le projet `DbzLegendsAnalyser`.  
> **Règle** : L'agent IA doit **commiter après chaque tâche** terminée.  
> **Projet cible** : `custom-tools/DbzLegendsAnalyser/DbzLegendsAnalyser/`  
> **Projet source (référence)** : `custom-tools/DbzLegendsAnalyser/DbzLegendsAnalyserWinForms/`  
> **Librairie partagée WinForms** : `custom-tools/DbzLegendsAnalyser/PsxTools2/` (PsxToolsWinforms)  
> **Librairie MonoGame** : `custom-tools/DbzLegendsAnalyser/PsxTools/`  
> **UI Framework** : `custom-tools/MGUI/` (MGUI.Core, MGUI.Shared, MGUI.FontStashSharp)

---

## Phase 0 — Préparation

### Tâche 0.1 — Analyser MGUI et comprendre son API
- Lire `MGUI/MGUI.Samples/` pour comprendre comment initialiser MGUI dans un projet MonoGame.
- Identifier les composants MGUI équivalents aux contrôles WinForms utilisés :
  - `MenuStrip` → MGUI menu bar
  - `SplitContainer` → MGUI layout panels (DockPanel, Grid)
  - `ListBox` → MGUI ListBox/ListView
  - `Label` → MGUI TextBlock
  - `FolderBrowserDialog` → solution alternative (text input ou intégration native)
  - Panel de rendu custom (pour images et 3D wireframe)
- Documenter les correspondances dans un commentaire en haut de `Game1.cs`.
- **Commit** : `chore: document MGUI API mapping for WinForms conversion`

### Tâche 0.2 — Adapter PsxTools pour supporter Texture2D en sortie
- Dans `PsxTools/`, créer les équivalents MonoGame des décodeurs de `PsxTools2/` qui retournent `Texture2D` au lieu de `System.Drawing.Bitmap` :
  - `PsxImageDecoder.cs` — équivalent de `PsxImageLoader.cs` (4bpp/8bpp/16bpp → `Texture2D`)
  - `LzssDecompressor.cs` — copier depuis PsxTools2 (pas de dépendance GDI+, réutilisable tel quel)
  - `StgMdLoader.cs` — copier depuis PsxTools2 (modèle de données pur, pas de dépendance GDI+)
  - `StgTxDecoder.cs` — équivalent de `StgTxLoader.cs` retournant `Texture2D`
- Les classes de modèle de données (`StgModelFile`, `StgMeshEntry`, `StgParticle`, `StgTriangle`, `Vec3`, etc.) peuvent être copiées telles quelles.
- **Commit** : `feat(PsxTools): add MonoGame Texture2D decoders for PSX image formats`

---

## Phase 1 — Infrastructure MGUI dans Game1

### Tâche 1.1 — Initialiser MGUI dans Game1.cs
- Configurer le `MGDesktop` (ou équivalent MGUI root) dans `Game1.Initialize()`.
- Ajouter le chargement de la police (FontStashSharp) dans `LoadContent()`.
- Brancher `MGDesktop.Update()` dans `Update()` et `MGDesktop.Draw()` dans `Draw()`.
- Vérifier que la fenêtre affiche un fond vide avec MGUI actif (plus d'écran bleu).
- **Commit** : `feat: initialize MGUI framework in Game1`

### Tâche 1.2 — Créer le layout principal (MenuBar + SplitPanel)
- Reproduire la structure WinForms `MainForm` :
  - **Menu bar** en haut : `File → Open`
  - **Panel gauche** : liste de fichiers (`ListBox`)
  - **Panel droit** : zone de contenu (vide pour l'instant)
- Câbler le menu `File → Open` pour ouvrir un dialogue de sélection de dossier (utiliser `System.Windows.Forms.FolderBrowserDialog` via interop ou un champ texte MGUI + bouton Browse).
- **Commit** : `feat: implement main layout with menu bar and split panel`

### Tâche 1.3 — Implémenter le chargement du dossier de données et la liste de fichiers
- Reproduire la logique de `MainForm.LoadGameData()` :
  - Scanner le dossier sélectionné pour les fichiers supportés (CHR_DATA, STG, SUB).
  - Peupler la `ListBox` MGUI avec les chemins relatifs.
- Implémenter le dictionnaire de mapping `_controlTypes` (fichier → type de viewer).
- **Commit** : `feat: implement game data folder scanning and file list population`

---

## Phase 2 — Viewer d'images (base commune)

### Tâche 2.1 — Créer un composant ImageViewer MonoGame/MGUI
- Équivalent de `PsxTools2/ImageViewerControl.cs` mais en rendu MonoGame :
  - Affichage d'une `Texture2D` dans une zone du panel droit.
  - **Pan** (clic gauche + drag).
  - **Zoom** (molette, niveaux 0.5x/1x/2x/4x, nearest-neighbor).
  - Zoom centré sur le curseur.
- Peut être un composant MGUI custom ou un rendu SpriteBatch dans une zone dédiée.
- **Commit** : `feat: implement ImageViewer component with pan and zoom`

### Tâche 2.2 — Créer la classe de base pour les viewers (IAnalyserView)
- Équivalent de `AnalyserControl.cs` :
  - Interface ou classe abstraite `IAnalyserView` avec méthode `Initialize(string gamePath)`.
  - Méthodes `Update(GameTime)` et `Draw(SpriteBatch)`.
  - Pattern d'affichage commun : ListBox à gauche (offsets/sections) + ImageViewer à droite.
- **Commit** : `feat: create IAnalyserView base interface for file viewers`

---

## Phase 3 — Implémentation des viewers d'images

### Tâche 3.1 — Viewer OV_CHR_A (sprites personnages)
- Convertir `OV_CHR_A_Control.cs` :
  - Charger `OV_CHR_A.B`, décompresser LZSS.
  - Parser les CLUTs (128 et 256 couleurs).
  - Extraire les 4 régions d'images aux offsets hardcodés (0x0300, 0x2300, 0x4600, 0x6400).
  - Décoder chaque région en `Texture2D` avec variations de palettes (jusqu'à 8).
  - UI : ListBox (offsets) + ImageViewer.
- **Commit** : `feat: implement OV_CHR_A viewer (character sprites)`

### Tâche 3.2 — Viewer LOAD_B (écrans de chargement)
- Convertir `LOAD_B_Control.cs` :
  - Charger `LOAD.B` (sections de 20480 bytes, 10 secteurs CD).
  - Par section : 512 bytes palette + données LZSS → image 8bpp 320×240.
  - UI : ListBox (sections) + ImageViewer.
- **Commit** : `feat: implement LOAD_B viewer (loading screens)`

### Tâche 3.3 — Viewer FACE_B (portraits)
- Convertir `FACE_B_Control.cs` :
  - Charger `FACE.B` (sections de 0x1000 bytes).
  - Par section : CLUT 16 couleurs + 3 images face (4bpp, 12×48).
  - UI : ListBox (sections) + ImageViewer.
- **Commit** : `feat: implement FACE_B viewer (character face portraits)`

### Tâche 3.4 — Viewer EFF_AUTO_B (effets spéciaux)
- Convertir `EFF_AUTO_B_Control.cs` :
  - Charger `EFF_AUTO.B` : palette 80 couleurs (5 × 16 sub-palettes).
  - 2 images LZSS compressées (4bpp 256×256).
  - Rendu avec 5 variations de palette.
  - UI : ListBox + ImageViewer.
- **Commit** : `feat: implement EFF_AUTO_B viewer (effect sprites)`

### Tâche 3.5 — Viewer TITLE_B (écran titre)
- Convertir `TITLE_B_Control.cs` :
  - Charger `TITLE.B` (6 entrées : 3 images + 3 palettes).
  - Décoder Image 1 (LZSS 4bpp 256×256) avec 5 variations de palette.
  - UI : ListBox + ImageViewer.
- **Commit** : `feat: implement TITLE_B viewer (title screen)`

### Tâche 3.6 — Viewer STG_TX (textures de stage)
- Convertir `STG_TX_Control.cs` :
  - Charger `STGxTX.B` : header (textureCount) + table d'entrées (28 bytes chacune).
  - Deux passes : extraction CLUTs puis décodage images.
  - Support LZSS compressé (type 0) et raw (type 1), auto-detect 4bpp/8bpp.
  - UI : ListBox (textures) + ImageViewer.
- **Commit** : `feat: implement STG_TX viewer (stage textures)`

---

## Phase 4 — Viewer 3D wireframe (stage meshes)

### Tâche 4.1 — Implémenter le rendu 3D wireframe en MonoGame
- Convertir `STG_MD_Control.cs` :
  - Charger `STGxMD.B` via `StgMdLoader`.
  - Rendu wireframe avec `BasicEffect` + `PrimitiveType.LineList` (ou triangles en wireframe).
  - Projection perspective avec calcul automatique de la scène AABB.
  - Back-face culling + painter's algorithm (tri par profondeur).
- **Commit** : `feat: implement 3D wireframe rendering for stage meshes`

### Tâche 4.2 — Ajouter les contrôles interactifs au viewer 3D
- Contrôles souris/clavier :
  - **Clic gauche + drag** : rotation (yaw/pitch).
  - **Clic droit + drag** : pan.
  - **Molette** : zoom.
  - **Touche R** : reset de la vue.
- Checkbox "Colorize by type" pour colorer le wireframe par couleurs de vertex.
- Axe helper XYZ dans le coin bas-gauche.
- Barre de statut : mesh count, particle count, triangle count, hint contrôles.
- **Commit** : `feat: add interactive camera controls to 3D wireframe viewer`

---

## Phase 5 — Sélection dynamique des viewers

### Tâche 5.1 — Câbler la sélection de fichier → instanciation du viewer
- Reproduire la logique de `MainForm.lstFiles_SelectedIndexChanged()` :
  - Quand un fichier est sélectionné dans la ListBox, instancier le viewer correspondant.
  - Afficher le viewer dans le panel droit.
  - Disposer proprement le viewer précédent (libérer les `Texture2D`).
- **Commit** : `feat: implement dynamic viewer switching on file selection`

---

## Phase 6 — Polish et finalisation

### Tâche 6.1 — Gestion de la fenêtre redimensionnable
- S'assurer que tous les viewers se redimensionnent correctement avec la fenêtre.
- Recalculer les layouts MGUI au resize.
- **Commit** : `fix: handle window resize for all viewers and layouts`

### Tâche 6.2 — Fix du bug écran bleu
- Vérifier que `Game1.Draw()` rend correctement le contenu MGUI.
- S'assurer que `GraphicsDevice.Clear()` est suivi du rendu MGUI et du rendu des viewers.
- Tester le lancement : la fenêtre doit montrer l'UI MGUI (menu + panels) et non un écran bleu.
- **Commit** : `fix: resolve blue screen issue - render MGUI content properly`

### Tâche 6.3 — Nettoyage et documentation
- Supprimer le code mort et les TODO obsolètes.
- Ajouter des commentaires XML sur les classes et méthodes publiques.
- Mettre à jour le README si nécessaire.
- **Commit** : `chore: cleanup dead code and add XML documentation`

---

## Résumé des commits attendus (16 commits)

| # | Message de commit |
|---|---|
| 1 | `chore: document MGUI API mapping for WinForms conversion` |
| 2 | `feat(PsxTools): add MonoGame Texture2D decoders for PSX image formats` |
| 3 | `feat: initialize MGUI framework in Game1` |
| 4 | `feat: implement main layout with menu bar and split panel` |
| 5 | `feat: implement game data folder scanning and file list population` |
| 6 | `feat: implement ImageViewer component with pan and zoom` |
| 7 | `feat: create IAnalyserView base interface for file viewers` |
| 8 | `feat: implement OV_CHR_A viewer (character sprites)` |
| 9 | `feat: implement LOAD_B viewer (loading screens)` |
| 10 | `feat: implement FACE_B viewer (character face portraits)` |
| 11 | `feat: implement EFF_AUTO_B viewer (effect sprites)` |
| 12 | `feat: implement TITLE_B viewer (title screen)` |
| 13 | `feat: implement STG_TX viewer (stage textures)` |
| 14 | `feat: implement 3D wireframe rendering for stage meshes` |
| 15 | `feat: add interactive camera controls to 3D wireframe viewer` |
| 16 | `feat: implement dynamic viewer switching on file selection` |
| 17 | `fix: handle window resize for all viewers and layouts` |
| 18 | `fix: resolve blue screen issue - render MGUI content properly` |
| 19 | `chore: cleanup dead code and add XML documentation` |
