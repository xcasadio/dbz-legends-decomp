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
using MGUI.FontStashSharp;
using MGUI.Shared.Rendering;
using MGUI.Shared.Text;
using MGUI.Shared.Text.Engines;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Diagnostics;
using System.IO;
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

        // IObservableUpdate
        public event EventHandler<TimeSpan> PreviewUpdate;
        public event EventHandler<EventArgs> EndUpdate;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            _graphics.PreferredBackBufferWidth = 1088;
            _graphics.PreferredBackBufferHeight = 672;

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

        protected override void Update(GameTime gameTime)
        {
            PreviewUpdate?.Invoke(this, gameTime.TotalGameTime);

            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

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
