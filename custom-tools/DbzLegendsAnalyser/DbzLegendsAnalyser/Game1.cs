using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.ImGuiNet;
using System;
using Color = Microsoft.Xna.Framework.Color;

namespace DbzLegendsAnalyser
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        public ImGuiRenderer GuiRenderer;

        public static bool ShowDemo = false;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            _graphics.PreferredBackBufferWidth = 1024;
            _graphics.PreferredBackBufferHeight = 768;

            Window.AllowUserResizing = true; // true;
            //Window.ClientSizeChanged += delegate { WasResized = true; };
        }

        protected override void Initialize()
        {
            GuiRenderer = new ImGuiRenderer(this);
            GuiRenderer.RebuildFontAtlas();

            //ImGui.GetIO().NativePtr->ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            // optionnel (plus compliqué côté backend MonoGame) :
            // io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

            //io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;      // Optionnel : fenêtres détachables OS
            //io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            //
            //// Style recommandé quand Viewports est activé
            //ImGui.StyleColorsDark();
            //var style = ImGui.GetStyle();
            //if ((io.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
            //{
            //    style.WindowRounding = 0.0f;
            //    style.Colors[(int)ImGuiCol.WindowBg].W = 1.0f;
            //}

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

            GuiRenderer.BeginLayout(gameTime);

            //ImGui.DockSpaceOverViewport(ImGui.GetMainViewport().ID);

            DrawMainDockSpace(); 
            DrawMenuBar();

            DrawWindow_Scene();
            DrawWindow_Inspector();
            DrawWindow_Console();

            // Alternative "safe" (marche même si DockSpaceOverViewport n’existe pas) :
            //var flags = ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar |
            //            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize |
            //            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus |
            //            ImGuiWindowFlags.NoNavFocus;
            //
            //ImGui.SetNextWindowPos(Vector2.Zero);
            //ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
            //ImGui.Begin("DockSpaceHost", flags);
            //uint dockspaceId = ImGui.GetID("MyDockSpace");
            //ImGui.DockSpace(dockspaceId, Vector2.Zero);
            //ImGui.End();

            
            if (ShowDemo) 
                ImGui.ShowDemoWindow(ref ShowDemo);;

            GuiRenderer.EndLayout();
        }

        private static void DrawMainDockSpace()
        {
            var dockspaceFlags = ImGuiDockNodeFlags.PassthruCentralNode;

            // Fenêtre "racine" full-screen qui héberge le DockSpace
            var viewport = ImGui.GetMainViewport();

            ImGui.SetNextWindowPos(viewport.WorkPos);
            ImGui.SetNextWindowSize(viewport.WorkSize);
            ImGui.SetNextWindowViewport(viewport.ID);

            var hostWindowFlags =
                ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoBringToFrontOnFocus |
                ImGuiWindowFlags.NoNavFocus |
                ImGuiWindowFlags.MenuBar;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);

            ImGui.Begin("##DockSpaceHost", hostWindowFlags);

            ImGui.PopStyleVar(2);

            uint dockspaceId = ImGui.GetID("MyDockSpace");
            ImGui.DockSpace(dockspaceId, System.Numerics.Vector2.Zero, dockspaceFlags);

            ImGui.End();
        }

        private static void DrawMenuBar()
        {
            // Menu bar dans la fenêtre host (##DockSpaceHost)
            // => elle doit être ouverte (Begin/End) au moment où tu appelles BeginMenuBar.
            // Ici on la met dans le même Begin/End que DrawMainDockSpace pour être strict.
            // Variante simple : déplace DrawMenuBar() dans DrawMainDockSpace().

            // Si tu veux absolument la séparer, tu peux aussi faire une "MainMenuBar" globale :
            // ImGui.BeginMainMenuBar() / EndMainMenuBar().
            if (ImGui.BeginMainMenuBar())
            {
                if (ImGui.BeginMenu("View"))
                {
                    ImGui.MenuItem("ImGui Demo", "", ref ShowDemo);
                    ImGui.EndMenu();
                }
                ImGui.EndMainMenuBar();
            }
        }

        private static void DrawWindow_Scene()
        {
            ImGui.Begin("Scene");
            ImGui.Text("Zone de rendu / scene");

            var io = ImGui.GetIO();
            ImGui.Text($"DockingEnable flag: {((io.ConfigFlags & ImGuiConfigFlags.DockingEnable) != 0)}");
            ImGui.Text($"BackendFlags.HasMouseCursors: {((io.BackendFlags & ImGuiBackendFlags.HasMouseCursors) != 0)}");
            ImGui.Text($"BackendFlags.HasSetMousePos: {((io.BackendFlags & ImGuiBackendFlags.HasSetMousePos) != 0)}");
            ImGui.Text($"IniFilename null: {io.IniFilename == null}");

            ImGui.End();
        }

        private static void DrawWindow_Inspector()
        {
            ImGui.Begin("Inspector");
            ImGui.Text("Propriétés / inspector");
            ImGui.End();
        }

        private static void DrawWindow_Console()
        {
            ImGui.Begin("Console");
            ImGui.TextWrapped("Logs...");
            ImGui.End();
        }
    }
}
