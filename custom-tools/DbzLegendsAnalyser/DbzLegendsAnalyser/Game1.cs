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
//
//  Initialization:
//    Game1 : Game, IObservableUpdate
//    → GameRenderHost<Game1> → MainRenderer → MGDesktop
//    → MGWindow (borderless, fills screen) → MGDockPanel (layout)
//    → Desktop.Update() in Update(), Desktop.Draw() in Draw()
//
//  Font setup:
//    FontStashSharp with arial.ttf from MGUI.Core/Content/Fonts/ttf/
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using Color = Microsoft.Xna.Framework.Color;

namespace DbzLegendsAnalyser
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            _graphics.PreferredBackBufferWidth = 1024;
            _graphics.PreferredBackBufferHeight = 768;

            Window.AllowUserResizing = true;
        }

        protected override void Initialize()
        {
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            // TODO: use this.Content to load your game content here
        }

        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            // TODO: Add your update logic here

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            // TODO: Add your drawing code here

            base.Draw(gameTime);
        }
    }
}
