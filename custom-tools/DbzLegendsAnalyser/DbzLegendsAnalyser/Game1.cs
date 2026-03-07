// ─────────────────────────────────────────────────────────────────────────────
//  MGUI ↔ WinForms mapping for DbzLegendsAnalyser conversion
//
//  WinForms Control         →  MGUI Equivalent
//  ──────────────────────────────────────────────────────────────
//  MenuStrip                →  MGMenuBar + MGContextMenu
//  SplitContainer           →  MGGrid + GridLength (columns)
//  ListBox                  →  MGListBox<string>
//  Label                    →  MGTextBlock
//  Button                   →  MGButton
//  CheckBox                 →  MGCheckBox
//  FolderBrowserDialog      →  System.Windows.Forms.FolderBrowserDialog (interop)
//  ImageViewerControl       →  Custom SpriteBatch rendering (pan/zoom)
//  GDI+ 3D wireframe        →  MonoGame BasicEffect + LineList primitives
//  AnalyserControl (base)   →  IAnalyserView interface
// ─────────────────────────────────────────────────────────────────────────────

using FontStashSharp;
using MGUI.Core.UI;
using MGUI.Core.UI.Containers;
using MGUI.Core.UI.Containers.Grids;
using MGUI.FontStashSharp;
using MGUI.Shared.Rendering;
using MGUI.Shared.Text;
using MGUI.Shared.Text.Engines;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Color = Microsoft.Xna.Framework.Color;

namespace DbzLegendsAnalyser
{
    public class Game1 : Game, IObservableUpdate
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        // MGUI
        private MainRenderer MGUIRenderer { get; set; }
        internal MGDesktop Desktop { get; set; }

        // Layout
        private MGWindow _mainWindow;
        private MGListBox<string> _fileListBox;
        private MGDockPanel _contentPanel; // right side — content area placeholder

        // Data
        private string _gamePath;
        private readonly List<string> _fileEntries = new();

        // File pattern → viewer type mapping (mirrors WinForms _controlTypes)
        private readonly Dictionary<string, string> _controlTypes = new()
        {
            { @"CHR_DATA\OV_CHR_A.B", "OV_CHR_A" },
            { @"CHR_DATA\LOAD.B", "LOAD_B" },
            { @"CHR_DATA\FACE.B", "FACE_B" },
            { @"CHR_DATA\EFF_AUTO.B", "EFF_AUTO_B" },
            { @"STG\STG1MD.B", "STG_MD" }, { @"STG\STG2MD.B", "STG_MD" },
            { @"STG\STG3MD.B", "STG_MD" }, { @"STG\STG4MD.B", "STG_MD" },
            { @"STG\STG5MD.B", "STG_MD" }, { @"STG\STG6MD.B", "STG_MD" },
            { @"STG\STG7MD.B", "STG_MD" }, { @"STG\STG8MD.B", "STG_MD" },
            { @"STG\STG1TX.B", "STG_TX" }, { @"STG\STG2TX.B", "STG_TX" },
            { @"STG\STG3TX.B", "STG_TX" }, { @"STG\STG4TX.B", "STG_TX" },
            { @"STG\STG5TX.B", "STG_TX" }, { @"STG\STG6TX.B", "STG_TX" },
            { @"STG\STG7TX.B", "STG_TX" }, { @"STG\STG8TX.B", "STG_TX" },
            { @"SUB\TITLE.B", "TITLE_B" }
        };

        // IObservableUpdate
        public event EventHandler<TimeSpan> PreviewUpdate;
        public event EventHandler<EventArgs> EndUpdate;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            _graphics.PreferredBackBufferWidth = 1280;
            _graphics.PreferredBackBufferHeight = 720;

            Window.AllowUserResizing = true;
        }

        protected override void Initialize()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            // Create MGUI renderer and desktop
            MGUIRenderer = new MainRenderer(new GameRenderHost<Game1>(this));
            Desktop = new MGDesktop(MGUIRenderer);

            // Initialize FontStashSharp text engine
            InitializeFonts();

            // Build main UI layout
            BuildMainLayout();

            base.Initialize();
        }

        private void InitializeFonts()
        {
            try
            {
                // Fonts are in MGUI.Core/Content/Fonts/ttf/ — resolve relative to exe
                string ttfDir = Path.GetFullPath(
                    Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\..\MGUI\MGUI.Core\Content\Fonts\ttf"));

                if (!Directory.Exists(ttfDir))
                {
                    // Try alternate path for development
                    ttfDir = Path.GetFullPath(
                        Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\MGUI\MGUI.Core\Content\Fonts\ttf"));
                }

                if (!Directory.Exists(ttfDir))
                {
                    Debug.WriteLine($"[Font] TTF directory not found, using default SpriteFont engine");
                    Desktop.TextEngine = new SpriteFontTextEngine(Desktop.FontManager);
                    return;
                }

                var fssEngine = new FontStashSharpTextEngine();
                const string FamilyName = "Arial";

                byte[] arialBytes = File.ReadAllBytes(Path.Combine(ttfDir, "arial.ttf"));
                FontSystem arialNormal = new FontSystem();
                arialNormal.AddFont(arialBytes);
                fssEngine.AddFontSystem(FamilyName, CustomFontStyles.Normal, arialNormal, arialBytes);

                FontSystem arialBold = new FontSystem();
                arialBold.AddFont(File.ReadAllBytes(Path.Combine(ttfDir, "arialbd.ttf")));
                fssEngine.AddFontSystem(FamilyName, CustomFontStyles.Bold, arialBold);

                FontSystem arialItalic = new FontSystem();
                arialItalic.AddFont(File.ReadAllBytes(Path.Combine(ttfDir, "ariali.ttf")));
                fssEngine.AddFontSystem(FamilyName, CustomFontStyles.Italic, arialItalic);

                fssEngine.MatchSpriteFontSizing(Desktop.FontManager);
                Desktop.TextEngine = fssEngine;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Font] FSS init failed: {ex.Message}");
                Desktop.TextEngine = new SpriteFontTextEngine(Desktop.FontManager);
            }
        }

        private void BuildMainLayout()
        {
            var vp = GraphicsDevice.Viewport;

            // Create a borderless full-screen MGWindow
            _mainWindow = new MGWindow(Desktop, 0, 0, vp.Width, vp.Height);
            _mainWindow.IsTitleBarVisible = false;
            _mainWindow.IsCloseButtonVisible = false;
            _mainWindow.IsUserResizable = false;
            _mainWindow.Padding = new Thickness(0);
            _mainWindow.BorderThickness = new Thickness(0);

            // Root layout: DockPanel (menu bar on top, grid below)
            var dockPanel = new MGDockPanel(_mainWindow, true);

            // ── Menu Bar ──
            var menuBar = new MGMenuBar(_mainWindow);
            menuBar.AddItem("File", item =>
            {
                item.Submenu = new MGContextMenu(_mainWindow);
                item.Submenu.AddButton("Open Folder…", btn => OnOpenFolder());
            });
            dockPanel.TryAddChild(menuBar, Dock.Top);

            // ── Split Grid: file list (left) | content area (right) ──
            var splitGrid = new MGGrid(_mainWindow);
            splitGrid.AddColumn(GridLength.CreatePixelLength(250));   // left: file list
            splitGrid.AddColumn(GridLength.CreateWeightedLength(1));  // right: content
            splitGrid.AddRow(GridLength.CreateWeightedLength(1));     // single row

            // Left: file list box
            _fileListBox = new MGListBox<string>(_mainWindow);
            _fileListBox.SelectionMode = ListBoxSelectionMode.Single;
            _fileListBox.SelectionChanged += OnFileSelected;
            splitGrid.TryAddChild(0, 0, _fileListBox);

            // Right: content placeholder (DockPanel that will hold viewer content)
            _contentPanel = new MGDockPanel(_mainWindow, true);
            var placeholder = new MGTextBlock(_mainWindow,
                "[i]Open a game data folder to begin[/i]", Color.Gray, 12);
            placeholder.HorizontalAlignment = HorizontalAlignment.Center;
            placeholder.VerticalAlignment = VerticalAlignment.Center;
            _contentPanel.TryAddChild(placeholder, Dock.Top);
            splitGrid.TryAddChild(0, 1, _contentPanel);

            dockPanel.TryAddChild(splitGrid, Dock.Top); // fills remaining space (LastChildFill)

            _mainWindow.SetContent(dockPanel);
            Desktop.Windows.Add(_mainWindow);
        }

        private void OnOpenFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select the game data folder";

            // Default to the repo 'data' directory if it exists
            string defaultPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\..\data"));
            if (Directory.Exists(defaultPath))
                dialog.InitialDirectory = defaultPath;

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK
                || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            _gamePath = dialog.SelectedPath;
            Window.Title = $"DBZ Legends Analyser — {_gamePath}";

            // Populate list with files that actually exist on disk
            _fileEntries.Clear();
            foreach (var key in _controlTypes.Keys.OrderBy(k => k))
            {
                string fullPath = Path.Combine(_gamePath, key);
                if (File.Exists(fullPath))
                    _fileEntries.Add(key);
            }

            if (_fileEntries.Count == 0)
            {
                // Show all entries even if files don't exist (lets user see what's expected)
                _fileEntries.AddRange(_controlTypes.Keys.OrderBy(k => k));
            }

            _fileListBox.SetItemsSource(_fileEntries);
        }

        private void OnFileSelected(object sender, ReadOnlyCollection<MGListBoxItem<string>> selection)
        {
            if (selection == null || selection.Count == 0)
                return;

            var selectedFile = selection[0].Data;
            Debug.WriteLine($"[UI] Selected: {selectedFile}");

            // TODO: Task 1.3+ — instantiate the appropriate viewer panel
        }

        protected override void Update(GameTime gameTime)
        {
            PreviewUpdate?.Invoke(this, gameTime.TotalGameTime);

            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            // Keep main window sized to viewport
            if (_mainWindow != null)
            {
                var vp = GraphicsDevice.Viewport;
                if (_mainWindow.WindowWidth != vp.Width || _mainWindow.WindowHeight != vp.Height)
                {
                    _mainWindow.Left = 0;
                    _mainWindow.Top = 0;
                    _mainWindow.WindowWidth = vp.Width;
                    _mainWindow.WindowHeight = vp.Height;
                }
            }

            Desktop.Update();

            base.Update(gameTime);
            EndUpdate?.Invoke(this, EventArgs.Empty);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(30, 30, 40));

            Desktop.Draw();

            base.Draw(gameTime);
        }
    }
}
