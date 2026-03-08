#pragma warning disable CS8632 // nullable annotation without #nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PsxTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DbzLegendsAnalyser.Viewers
{
    /// <summary>
    /// 3D viewer for STG\STGxMD.B stage mesh files.
    ///
    /// Controls:
    ///   Left-drag         — FPS look-around (pivot on camera position, not target)
    ///   L+R drag          — pan camera (horizontal strafe + world-Y lift)
    ///   Arrows / WASD     — move camera target (forward/back/strafe)
    ///   Scroll wheel      — zoom (change orbit distance)
    ///   R                 — reset view
    ///
    /// List items (shown in image listbox):
    ///   index 0 "Wireframe" — blue edge-only rendering
    ///   index 1 "Solid"     — filled triangles with vertex colors
    /// </summary>
    public class STG_MD_View : IAnalyserView
    {
        // ── Display mode ──────────────────────────────────────────────────────
        private enum DisplayMode { Wireframe, Solid, Textured }
        private DisplayMode _displayMode = DisplayMode.Wireframe;

        // ── Model ─────────────────────────────────────────────────────────────
        private StgModelFile? _model;
        private VertexPositionColor[]   _lineVerts      = Array.Empty<VertexPositionColor>();
        private VertexPositionColor[]   _solidVerts     = Array.Empty<VertexPositionColor>();
        private List<(Texture2D Tex, VertexPositionTexture[] Verts)> _texGroups = new();
        private Vec3  _sceneCenter;
        private float _sceneScale = 1f;

        // ── Floor grid ────────────────────────────────────────────────────────
        // 23×23 flat tiles at Y=0, 256 PSX units each (tpage=0x000B from FUN_80041640)
        private VertexPositionColor[]   _floorWireVerts = Array.Empty<VertexPositionColor>();
        private VertexPositionColor[]   _floorSolidVerts = Array.Empty<VertexPositionColor>();
        private List<(Texture2D Tex, VertexPositionTexture[] Verts)> _floorTexGroups = new();

        // ── Background billboard sprites ──────────────────────────────────────
        // 80 world-space billboard instances from DrawBackgroundBillboards (0x800410cc).
        // Sprite texture: STGxTX.B entry[11] — 4bpp, 104×32 px at VRAM(896, 0).
        //   tpage=0x2E (tpX=14, 4bpp, ABR=1), clut=0x7F00 → palette at VRAM(0, 508) [16-color].
        //   Default template: u0=8, v0=0, w=96, h=32 (visible area u=8..103, v=0..31).
        //   Stage-26 template: u0=0, v0=0, w=88, h=40.
        // Parallax wrapping: posX/Z snap to floor(cam/2000)*2000 at runtime.
        private VertexPositionColor[]   _bgWireVerts  = Array.Empty<VertexPositionColor>();
        private VertexPositionColor[]   _bgSolidVerts = Array.Empty<VertexPositionColor>();
        private List<(Texture2D Tex, VertexPositionTexture[] Verts)> _bgTexGroups = new();

        // ── Sky background panorama ───────────────────────────────────────────
        // Rendered by FUN_80041c6c (0x80041c6c, init) + FUN_80041ee4 (0x80041ee4, per-frame).
        // 8 screen-space POLY_FT4 quads; GPU: tpage=0x8F (8bpp, VRAM 960,0), clut=0x7900.
        //   → palette VRAM(0, 484) [256-color] = STGxTX.B entry[6].
        // Sky texture: STGxTX.B entry[7] — 8bpp, 128×256 px at VRAM(960, 0).
        //   The 128×256 image contains TWO independent 128×128 sub-textures stacked:
        //     top half    (rows   0–127): even quads  (i=0,2,4,6)  → v0=0,   v3=127
        //     bottom half (rows 128–255): odd  quads  (i=1,3,5,7)  → v0=128, v3=255
        //   FUN_80041ee4: uVar3 = (char)i * (-128) mod 256 → 0,128,0,128,… alternates halves.
        //   RotAverage4 + camera-angle scroll; 8 quads span -512..+511 screen-X at width=128.
        private VertexPositionColor[]   _skyWireVerts  = Array.Empty<VertexPositionColor>();
        private VertexPositionColor[]   _skySolidVerts = Array.Empty<VertexPositionColor>();
        private List<(Texture2D Tex, VertexPositionTexture[] Verts)> _skyTexGroups = new();

        // ── Arcball camera ────────────────────────────────────────────────────
        // Camera orbits _target in spherical coordinates.
        // _azimuth   : horizontal angle around Y (radians)
        // _elevation : vertical angle above horizon (radians)
        // _distance  : orbit radius (scaled scene units)
        private Vector3 _target    = Vector3.Zero;
        private float   _azimuth   = 0.4f;
        private float   _elevation = 0.25f;
        private float   _distance  = 800f;

        // ── State ─────────────────────────────────────────────────────────────
        private Rectangle _bounds;

        // ── MonoGame resources ────────────────────────────────────────────────
        private GraphicsDevice _graphicsDevice;
        private BasicEffect    _basicEffect;
        private BasicEffect    _axesEffect;
        private BasicEffect    _texEffect;   // textured mode
        private List<Texture2D> _ownedTextures = new(); // textures to dispose

        // ── Input ─────────────────────────────────────────────────────────────
        private MouseState    _prevMouse;
        private KeyboardState _prevKeyboard;

        // ─────────────────────────────────────────────────────────────────────
        // IAnalyserView
        // ─────────────────────────────────────────────────────────────────────

        public void Initialize(string filePath, GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;

            _basicEffect = new BasicEffect(graphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled    = false,
                TextureEnabled     = false,
            };
            _axesEffect = new BasicEffect(graphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled    = false,
                TextureEnabled     = false,
            };
            _texEffect = new BasicEffect(graphicsDevice)
            {
                VertexColorEnabled = false,
                LightingEnabled    = false,
                TextureEnabled     = true,
            };

            _model = StgMdLoader.Load(filePath);
            var worldTris = _model.GetWorldTriangles().ToArray();

            ComputeSceneTransform(worldTris);

            // Try to load the companion TX file: STG1MD.B → STG1TX.B
            string txPath = Path.Combine(
                Path.GetDirectoryName(filePath) ?? string.Empty,
                Path.GetFileName(filePath).Replace("MD.B", "TX.B", StringComparison.OrdinalIgnoreCase));
            var txEntries = File.Exists(txPath)
                ? LoadTxTextures(txPath, graphicsDevice)
                : new List<TxEntry>();

            BuildGeometry(worldTris, txEntries);
            BuildFloor(txEntries);
            BuildBillboards(txEntries);
            BuildSkyBackground(txEntries);
            ResetView();
        }

        public string[] GetListItems()
        {
            bool hasTex = _texGroups.Count > 0;
            return new[]
            {
                _displayMode == DisplayMode.Wireframe ? "[\u2022] Wireframe" : "[ ] Wireframe",
                _displayMode == DisplayMode.Solid     ? "[\u2022] Solid"     : "[ ] Solid",
                hasTex
                    ? (_displayMode == DisplayMode.Textured ? "[\u2022] Textured" : "[ ] Textured")
                    : "[ ] Textured (no TX file)",
            };
        }

        public void OnItemSelected(int index)
        {
            _displayMode = index switch
            {
                1 => DisplayMode.Solid,
                2 when _texGroups.Count > 0 => DisplayMode.Textured,
                _ => DisplayMode.Wireframe,
            };
        }

        public void Update(GameTime gameTime, Rectangle contentBounds)
        {
            _bounds = contentBounds;

            var mouse     = Mouse.GetState();
            var keyboard  = Keyboard.GetState();
            bool inBounds = _bounds.Contains(mouse.Position);
            float dt      = (float)gameTime.ElapsedGameTime.TotalSeconds;

            bool lbHeld = mouse.LeftButton  == ButtonState.Pressed && _prevMouse.LeftButton  == ButtonState.Pressed;
            bool rbHeld = mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Pressed;
            int  mdx    = mouse.X - _prevMouse.X;
            int  mdy    = mouse.Y - _prevMouse.Y;

            // ── L+R drag → pan target (up/down world-Y, left/right camera-right) ─
            if (lbHeld && rbHeld)
            {
                Vector3 fwdPan = ComputeForward();
                var fwdXZp = new Vector3(fwdPan.X, 0f, fwdPan.Z);
                if (fwdXZp.LengthSquared() > 0.0001f) fwdXZp = Vector3.Normalize(fwdXZp);
                else fwdXZp = Vector3.Forward;
                Vector3 camRight = Vector3.Normalize(Vector3.Cross(fwdXZp, Vector3.Up));
                float panSpd = _distance * 0.0005f;
                _target -= camRight   * mdx * panSpd;   // horizontal strafe
                _target.Y += mdy * panSpd;              // world-Y lift
            }
            // ── Left-only drag → FPS look-around (pivot around camera position) ──
            else if (lbHeld && !rbHeld)
            {
                // Keep camera world position fixed; only change viewing direction.
                Vector3 camPos = ComputeCameraPos();
                _azimuth   += mdx * 0.008f;
                _elevation -= mdy * 0.008f;
                _elevation  = MathHelper.Clamp(_elevation,
                    -MathHelper.PiOver2 + 0.02f,
                     MathHelper.PiOver2 - 0.02f);
                // Recompute target so camera stays in place and only looks elsewhere
                _target = camPos + ComputeForward() * _distance;
            }

            // ── Scroll → zoom (change orbit distance) ─────────────────────────
            if (inBounds)
            {
                int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
                if (scroll > 0) _distance *= 0.44f;
                else if (scroll < 0) _distance *= 1.12f;
                _distance = MathHelper.Clamp(_distance, 1f, 500000f);
            }

            // ── Arrow / WASD → pan target in horizontal plane ─────────────────
            Vector3 fwd = ComputeForward();
            var fwdXZ = new Vector3(fwd.X, 0f, fwd.Z);
            if (fwdXZ.LengthSquared() > 0.0001f) fwdXZ = Vector3.Normalize(fwdXZ);
            else fwdXZ = Vector3.Forward;

            Vector3 right = Vector3.Normalize(Vector3.Cross(fwdXZ, Vector3.Up));
            float speed = _distance * dt * 0.3f;

            if (keyboard.IsKeyDown(Keys.Up)    || keyboard.IsKeyDown(Keys.W)) _target += fwdXZ * speed;
            if (keyboard.IsKeyDown(Keys.Down)  || keyboard.IsKeyDown(Keys.S)) _target -= fwdXZ * speed;
            if (keyboard.IsKeyDown(Keys.Left)  || keyboard.IsKeyDown(Keys.A)) _target -= right * speed;
            if (keyboard.IsKeyDown(Keys.Right) || keyboard.IsKeyDown(Keys.D)) _target += right * speed;

            // ── R → reset ─────────────────────────────────────────────────────
            if (keyboard.IsKeyDown(Keys.R) && _prevKeyboard.IsKeyUp(Keys.R))
                ResetView();

            _prevMouse    = mouse;
            _prevKeyboard = keyboard;
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle contentBounds)
        {
            _bounds = contentBounds;
            if (_lineVerts.Length == 0 && _solidVerts.Length == 0) return;

            var gd = spriteBatch.GraphicsDevice;
            spriteBatch.End();

            var oldViewport   = gd.Viewport;
            var oldRasterizer = gd.RasterizerState;
            var oldDepth      = gd.DepthStencilState;
            var oldBlend      = gd.BlendState;

            // ── Main 3D scene viewport ────────────────────────────────────────
            gd.Viewport          = new Viewport(_bounds);
            gd.DepthStencilState = DepthStencilState.Default;
            gd.BlendState        = BlendState.Opaque;
            gd.RasterizerState   = RasterizerState.CullNone;
            gd.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);

            ApplyMatrices(_basicEffect, _bounds);

            if (_displayMode == DisplayMode.Wireframe)
            {
                foreach (var pass in _basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    if (_skyWireVerts.Length >= 2)
                        gd.DrawUserPrimitives(PrimitiveType.LineList,
                            _skyWireVerts, 0, _skyWireVerts.Length / 2);
                    if (_lineVerts.Length >= 2)
                        gd.DrawUserPrimitives(PrimitiveType.LineList,
                            _lineVerts, 0, _lineVerts.Length / 2);
                    if (_floorWireVerts.Length >= 2)
                        gd.DrawUserPrimitives(PrimitiveType.LineList,
                            _floorWireVerts, 0, _floorWireVerts.Length / 2);
                    if (_bgWireVerts.Length >= 2)
                        gd.DrawUserPrimitives(PrimitiveType.LineList,
                            _bgWireVerts, 0, _bgWireVerts.Length / 2);
                }
            }
            else if (_displayMode == DisplayMode.Solid)
            {
                foreach (var pass in _basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    if (_skySolidVerts.Length >= 3)
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList,
                            _skySolidVerts, 0, _skySolidVerts.Length / 3);
                    if (_floorSolidVerts.Length >= 3)
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList,
                            _floorSolidVerts, 0, _floorSolidVerts.Length / 3);
                    if (_solidVerts.Length >= 3)
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList,
                            _solidVerts, 0, _solidVerts.Length / 3);
                    if (_bgSolidVerts.Length >= 3)
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList,
                            _bgSolidVerts, 0, _bgSolidVerts.Length / 3);
                }
            }
            else if (_displayMode == DisplayMode.Textured)
            {
                ApplyMatrices(_texEffect, _bounds);
                gd.SamplerStates[0] = SamplerState.LinearWrap;
                // Draw sky panorama first (furthest back).
                // Sky panels: draw texture opaque — the texture IS the sky background.
                // (Additive or semi-transparent effects are for layers on TOP, not the base.)
                if (_skyTexGroups.Count > 0 || _skySolidVerts.Length >= 3)
                {
                    ApplyMatrices(_texEffect, _bounds);
                    gd.BlendState = BlendState.Opaque;
                    if (_skyTexGroups.Count > 0)
                    {
                        foreach (var (tex, verts) in _skyTexGroups)
                        {
                            if (verts.Length < 3) continue;
                            _texEffect.Texture = tex;
                            foreach (var pass in _texEffect.CurrentTechnique.Passes)
                            { pass.Apply(); gd.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3); }
                        }
                    }
                    else
                    {
                        // Fallback: solid colour when no sky texture available
                        ApplyMatrices(_basicEffect, _bounds);
                        foreach (var pass in _basicEffect.CurrentTechnique.Passes)
                        {
                            pass.Apply();
                            if (_skySolidVerts.Length >= 3)
                                gd.DrawUserPrimitives(PrimitiveType.TriangleList,
                                    _skySolidVerts, 0, _skySolidVerts.Length / 3);
                        }
                    }
                }
                // Draw background billboard sprites on top of sky
                foreach (var (tex, verts) in _bgTexGroups)
                {
                    if (verts.Length < 3) continue;
                    _texEffect.Texture = tex;
                    foreach (var pass in _texEffect.CurrentTechnique.Passes)
                    { pass.Apply(); gd.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3); }
                }
                gd.SamplerStates[0] = SamplerState.LinearClamp;
                // Draw floor next
                foreach (var (tex, verts) in _floorTexGroups)
                {
                    if (verts.Length < 3) continue;
                    _texEffect.Texture = tex;
                    foreach (var pass in _texEffect.CurrentTechnique.Passes)
                    { pass.Apply(); gd.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3); }
                }
                // Draw meshes on top
                foreach (var (tex, verts) in _texGroups)
                {
                    if (verts.Length < 3) continue;
                    _texEffect.Texture = tex;
                    foreach (var pass in _texEffect.CurrentTechnique.Passes)
                    { pass.Apply(); gd.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3); }
                }
                // Overlay wireframe grid + billboard outlines for orientation
                ApplyMatrices(_basicEffect, _bounds);
                foreach (var pass in _basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    if (_floorWireVerts.Length >= 2)
                        gd.DrawUserPrimitives(PrimitiveType.LineList, _floorWireVerts, 0, _floorWireVerts.Length / 2);
                    if (_skyTexGroups.Count == 0 && _skyWireVerts.Length >= 2)
                        gd.DrawUserPrimitives(PrimitiveType.LineList, _skyWireVerts, 0, _skyWireVerts.Length / 2);
                    if (_bgTexGroups.Count == 0 && _bgWireVerts.Length >= 2)
                        gd.DrawUserPrimitives(PrimitiveType.LineList, _bgWireVerts, 0, _bgWireVerts.Length / 2);
                }
            }

            // ── XYZ axes gizmo ────────────────────────────────────────────────
            DrawAxesOverlay(gd);

            // ── Restore ───────────────────────────────────────────────────────
            gd.Viewport          = oldViewport;
            gd.RasterizerState   = oldRasterizer;
            gd.DepthStencilState = oldDepth;
            gd.BlendState        = oldBlend;

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.AlphaBlend, SamplerState.PointClamp);
        }

        public void Dispose()
        {
            _basicEffect?.Dispose();
            _axesEffect?.Dispose();
            _texEffect?.Dispose();
            foreach (var t in _ownedTextures) t?.Dispose();
            _ownedTextures.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Camera helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Unit vector from camera toward target (into scene).</summary>
        private Vector3 ComputeForward()
        {
            float cosEl = (float)Math.Cos(_elevation);
            // Azimuth=0 → looking along -Z (standard MonoGame convention)
            return new Vector3(
                 (float)Math.Sin(_azimuth) * cosEl,
                 (float)Math.Sin(_elevation),
                -(float)Math.Cos(_azimuth) * cosEl);
        }

        private Vector3 ComputeCameraPos() => _target - ComputeForward() * _distance;

        private void ApplyMatrices(BasicEffect fx, Rectangle viewport)
        {
            // World: center scene + uniform scale + flip PSX Y-down → Y-up
            fx.World =
                Matrix.CreateTranslation(-_sceneCenter.X, -_sceneCenter.Y, -_sceneCenter.Z)
                * Matrix.CreateScale(_sceneScale)
                * Matrix.CreateScale(1f, -1f, 1f);   // PSX Y-down fix

            fx.View = Matrix.CreateLookAt(ComputeCameraPos(), _target, Vector3.Up);

            float aspect = viewport.Width > 0 && viewport.Height > 0
                ? (float)viewport.Width / viewport.Height : 1f;
            fx.Projection = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(60f), aspect, 0.1f, 500000f);
        }

        private void DrawAxesOverlay(GraphicsDevice gd)
        {
            const int size   = 80;
            const int margin = 12;

            var vp = new Viewport(
                _bounds.X + margin,
                _bounds.Y + _bounds.Height - size - margin,
                size, size);

            gd.Viewport          = vp;
            gd.DepthStencilState = DepthStencilState.Default;
            gd.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);

            // Mini camera: same orientation as main camera, fixed 2.5-unit orbit
            var axesCamPos = -ComputeForward() * 2.5f;
            _axesEffect.World      = Matrix.Identity;
            _axesEffect.View       = Matrix.CreateLookAt(axesCamPos, Vector3.Zero, Vector3.Up);
            _axesEffect.Projection = Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(55f), 1f, 0.01f, 100f);

            // Axis lines + small arrowhead ticks (each axis: shaft + 2 tick lines = 3 line segs = 6 verts)
            var verts = new VertexPositionColor[]
            {
                // X — Red
                new(Vector3.Zero,               Color.Red),
                new(Vector3.UnitX,              Color.Red),
                new(Vector3.UnitX,              Color.Red),
                new(new Vector3(0.8f, 0.1f, 0), Color.Red),
                new(Vector3.UnitX,              Color.Red),
                new(new Vector3(0.8f,-0.1f, 0), Color.Red),

                // Y — Lime
                new(Vector3.Zero,               Color.Lime),
                new(Vector3.UnitY,              Color.Lime),
                new(Vector3.UnitY,              Color.Lime),
                new(new Vector3( 0.1f,0.8f, 0), Color.Lime),
                new(Vector3.UnitY,              Color.Lime),
                new(new Vector3(-0.1f,0.8f, 0), Color.Lime),

                // Z — CornflowerBlue
                new(Vector3.Zero,               Color.CornflowerBlue),
                new(Vector3.UnitZ,              Color.CornflowerBlue),
                new(Vector3.UnitZ,              Color.CornflowerBlue),
                new(new Vector3(0.1f, 0,0.8f),  Color.CornflowerBlue),
                new(Vector3.UnitZ,              Color.CornflowerBlue),
                new(new Vector3(-0.1f,0,0.8f),  Color.CornflowerBlue),
            };

            foreach (var pass in _axesEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.LineList, verts, 0, verts.Length / 2);
            }
        }

        private void ResetView()
        {
            _target    = Vector3.Zero;
            _azimuth   = 0.4f;
            _elevation = 0.25f;
            _distance  = 800f;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Geometry builders
        // ─────────────────────────────────────────────────────────────────────

        private void ComputeSceneTransform(StgTriangle[] tris)
        {
            if (tris.Length == 0) { _sceneScale = 1f; _sceneCenter = default; return; }

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            foreach (var t in tris)
            foreach (var v in new[] { t.V0, t.V1, t.V2 })
            {
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
                if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
            }

            _sceneCenter = new Vec3(
                (minX + maxX) / 2f,
                (minY + maxY) / 2f,
                (minZ + maxZ) / 2f);

            float extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            _sceneScale  = extent > 0 ? 400f / extent : 1f;
        }

        private void BuildGeometry(StgTriangle[] tris, List<TxEntry> txEntries)
        {
            var wireColor = new Color(60, 160, 240);

            var lines = new List<VertexPositionColor>(tris.Length * 6);
            var solid = new List<VertexPositionColor>(tris.Length * 3);

            // Textured: group by texture
            var texBuckets = new Dictionary<Texture2D, List<VertexPositionTexture>>();

            foreach (var tri in tris)
            {
                var p0 = new Vector3(tri.V0.X, tri.V0.Y, tri.V0.Z);
                var p1 = new Vector3(tri.V1.X, tri.V1.Y, tri.V1.Z);
                var p2 = new Vector3(tri.V2.X, tri.V2.Y, tri.V2.Z);

                // Wireframe
                lines.Add(new VertexPositionColor(p0, wireColor));
                lines.Add(new VertexPositionColor(p1, wireColor));
                lines.Add(new VertexPositionColor(p1, wireColor));
                lines.Add(new VertexPositionColor(p2, wireColor));
                lines.Add(new VertexPositionColor(p2, wireColor));
                lines.Add(new VertexPositionColor(p0, wireColor));

                // Solid
                solid.Add(new VertexPositionColor(p0, Brighten(tri.C0)));
                solid.Add(new VertexPositionColor(p1, Brighten(tri.C1)));
                solid.Add(new VertexPositionColor(p2, Brighten(tri.C2)));

                // Textured — use UV0 as representative to pick the correct sub-texture
                if (txEntries.Count > 0 && tri.HasUV)
                {
                    var tx = FindTexture(txEntries, tri.TPageX, tri.TPageY, tri.UV0, tri.CBA);
                    if (tx != null)
                    {
                        var uv0 = ComputeUV(tx, tri.UV0, tri.TPageX, tri.TPageY);
                        var uv1 = ComputeUV(tx, tri.UV1, tri.TPageX, tri.TPageY);
                        var uv2 = ComputeUV(tx, tri.UV2, tri.TPageX, tri.TPageY);

                        if (!texBuckets.TryGetValue(tx.Texture, out var bucket))
                            texBuckets[tx.Texture] = bucket = new List<VertexPositionTexture>();
                        bucket.Add(new VertexPositionTexture(p0, uv0));
                        bucket.Add(new VertexPositionTexture(p1, uv1));
                        bucket.Add(new VertexPositionTexture(p2, uv2));
                    }
                }
            }

            _lineVerts  = lines.ToArray();
            _solidVerts = solid.ToArray();
            _texGroups  = texBuckets.Select(kv => (kv.Key, kv.Value.ToArray())).ToList();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Floor grid builder
        // ─────────────────────────────────────────────────────────────────────
        // 23×23 POLY_FT4 tiles at Y=0, 256 PSX-units per tile.
        // Source: FUN_80041640 → FUN_80066870(tpage=0xb, uv=(x%2)*32,(z%2)*32, size=31×31)
        // TX tile texture: entry[9] vramX=704 words → 4bpp → 64 px wide × 64 px tall
        // UV mapping: tile(ix,iz) →  u=[ix%2 * 32 .. +31], v=[iz%2 * 32 .. +31]
        // ─────────────────────────────────────────────────────────────────────
        private void BuildFloor(List<TxEntry> txEntries)
        {
            const int TILES    = 23;
            const float TILE_W = 256f;                    // PSX units
            const float HALF   = TILES * TILE_W / 2f;    // 2944 — center the grid at origin

            var floorWire = new Color(50, 80, 50);
            var floorFill = new Color(40, 60, 40);

            var wire  = new List<VertexPositionColor>(TILES * TILES * 8);
            var solid = new List<VertexPositionColor>(TILES * TILES * 6);
            var texBuckets = new Dictionary<Texture2D, List<VertexPositionTexture>>();

            // Representative UV to find the correct TxEntry (tpageX=11, tpageY=0)
            var floorTx = FindTexture(txEntries, 11, 0, new StgUV(0, 0));

            for (int iz = 0; iz < TILES; iz++)
            for (int ix = 0; ix < TILES; ix++)
            {
                float x0 = ix * TILE_W - HALF;
                float x1 = x0 + TILE_W;
                float z0 = iz * TILE_W - HALF;
                float z1 = z0 + TILE_W;

                var v00 = new Vector3(x0, 0f, z0);
                var v10 = new Vector3(x1, 0f, z0);
                var v01 = new Vector3(x0, 0f, z1);
                var v11 = new Vector3(x1, 0f, z1);

                // ── Wireframe: 4 edges per tile ────────────────────────────
                wire.Add(new VertexPositionColor(v00, floorWire));
                wire.Add(new VertexPositionColor(v10, floorWire));
                wire.Add(new VertexPositionColor(v10, floorWire));
                wire.Add(new VertexPositionColor(v11, floorWire));
                wire.Add(new VertexPositionColor(v11, floorWire));
                wire.Add(new VertexPositionColor(v01, floorWire));
                wire.Add(new VertexPositionColor(v01, floorWire));
                wire.Add(new VertexPositionColor(v00, floorWire));

                // ── Solid: 2 tris ──────────────────────────────────────────
                solid.Add(new VertexPositionColor(v00, floorFill));
                solid.Add(new VertexPositionColor(v10, floorFill));
                solid.Add(new VertexPositionColor(v01, floorFill));
                solid.Add(new VertexPositionColor(v10, floorFill));
                solid.Add(new VertexPositionColor(v11, floorFill));
                solid.Add(new VertexPositionColor(v01, floorFill));

                // ── Textured: 2 tris with 2×2 tiling UV ───────────────────
                if (floorTx != null)
                {
                    // UV origin for this tile (2×2 repeating, 32-texel tiles within 64×64 sheet)
                    byte uOff = (byte)((ix % 2) * 32);
                    byte vOff = (byte)((iz % 2) * 32);

                    // PSX UV layout per POLY_FT4 winding (v00,v10,v01,v11)
                    // u1=u0+31 per FUN_80066870 (last inclusive texel of the 32-wide tile)
                    var fuv00 = ComputeUV(floorTx, new StgUV(uOff,      vOff),      11, 0);
                    var fuv10 = ComputeUV(floorTx, new StgUV((byte)(uOff + 31), vOff),      11, 0);
                    var fuv01 = ComputeUV(floorTx, new StgUV(uOff,      (byte)(vOff + 31)), 11, 0);
                    var fuv11 = ComputeUV(floorTx, new StgUV((byte)(uOff + 31), (byte)(vOff + 31)), 11, 0);

                    if (!texBuckets.TryGetValue(floorTx.Texture, out var bucket))
                        texBuckets[floorTx.Texture] = bucket = new List<VertexPositionTexture>();

                    bucket.Add(new VertexPositionTexture(v00, fuv00));
                    bucket.Add(new VertexPositionTexture(v10, fuv10));
                    bucket.Add(new VertexPositionTexture(v01, fuv01));
                    bucket.Add(new VertexPositionTexture(v10, fuv10));
                    bucket.Add(new VertexPositionTexture(v11, fuv11));
                    bucket.Add(new VertexPositionTexture(v01, fuv01));
                }
            }

            _floorWireVerts  = wire.ToArray();
            _floorSolidVerts = solid.ToArray();
            _floorTexGroups  = texBuckets.Select(kv => (kv.Key, kv.Value.ToArray())).ToList();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Background billboards
        // ─────────────────────────────────────────────────────────────────────
        // 80 billboard world positions decoded from INT_ARRAY_80087dac in RAM.
        // Format: (posX, posY, posZ) in PSX units (Y negative = above floor).
        // The 80 instances form a 4-fold XZ-symmetric array around the origin:
        //   Group 0 (0-19):  X<0 Z<0 | Group 1 (20-39): X<0 Z>0
        //   Group 2 (40-59): X>0 Z<0 | Group 3 (60-79): X>0 Z>0
        private static readonly (short X, short Y, short Z)[] BgBillboardPositions =
        {
            ( -600, -100,  -600), (-1800, -200,  -400), ( -800, -400, -1200), (-1600, -600, -1000),
            (-2600, -100,  -800), (-1400, -300, -1400), ( -400, -600, -1800), (-3800, -700,  -400),
            (-3400, -500, -1000), ( -400, -100, -3400), (-1000, -900, -2600), (-1200, -500, -3600),
            (-1800, -200, -2400), (-1800, -600, -3200), (-2200, -900, -1800), (-3000, -400, -1600),
            (-3600, -300, -2200), (-2800, -800, -2800), (-2800, -400, -3600), (-3800, -200, -3600),
            ( -600, -100,   600), (-1800, -200,   400), ( -800, -400,  1200), (-1600, -600,  1000),
            (-2600, -100,   800), (-1400, -300,  1400), ( -400, -600,  1800), (-3800, -700,   400),
            (-3400, -500,  1000), ( -400, -100,  3400), (-1000, -900,  2600), (-1200, -500,  3600),
            (-1800, -200,  2400), (-1800, -600,  3200), (-2200, -900,  1800), (-3000, -400,  1600),
            (-3600, -300,  2200), (-2800, -800,  2800), (-2800, -400,  3600), (-3800, -200,  3600),
            (  600, -100,  -600), ( 1800, -200,  -400), (  800, -400, -1200), ( 1600, -600, -1000),
            ( 2600, -100,  -800), ( 1400, -300, -1400), (  400, -600, -1800), ( 3800, -700,  -400),
            ( 3400, -500, -1000), (  400, -100, -3400), ( 1000, -900, -2600), ( 1200, -500, -3600),
            ( 1800, -200, -2400), ( 1800, -600, -3200), ( 2200, -900, -1800), ( 3000, -400, -1600),
            ( 3600, -300, -2200), ( 2800, -800, -2800), ( 2800, -400, -3600), ( 3800, -200, -3600),
            (  600, -100,   600), ( 1800, -200,   400), (  800, -400,  1200), ( 1600, -600,  1000),
            ( 2600, -100,   800), ( 1400, -300,  1400), (  400, -600,  1800), ( 3800, -700,   400),
            ( 3400, -500,  1000), (  400, -100,  3400), ( 1000, -900,  2600), ( 1200, -500,  3600),
            ( 1800, -200,  2400), ( 1800, -600,  3200), ( 2200, -900,  1800), ( 3000, -400,  1600),
            ( 3600, -300,  2200), ( 2800, -800,  2800), ( 2800, -400,  3600), ( 3800, -200,  3600),
        };

        /// <summary>
        /// Build billboard geometry at the 80 background instance positions.
        /// Each billboard is a vertical quad oriented radially outward from origin.
        /// Sprite texture: STGxTX.B entry[11] — 4bpp, 104×32 px at VRAM(896,0),
        ///   tpage=0x2E (tpX=14, 4bpp, ABR semi-trans 1), clut=0x7F00 → VRAM(0,508) palette.
        ///   Visible sprite area: u=8..103, v=0..31 (96×32 px region of the 104×32 sheet).
        /// World size per quad: 480×160 PSX units (96px×5, 32px×5).
        /// The sky background (8bpp entry[7]) is built separately in BuildSkyBackground.
        /// </summary>
        private void BuildBillboards(List<TxEntry> txEntries)
        {
            // Billboard sprite: STGxTX.B entry[11] — 4bpp, 104×32 px, VRAM(896,0), tpX=14.
            //   Template default: u0=8, v0=0, w=96, h=32 → visible area u=8..103, v=0..31.
            //   Clut=0x7F00 → clutVramY=(0x7F00>>6)&0x1FF=508 → VRAM(0,508) 16-color palette.
            var bgTx = FindTexture(txEntries, 14, 0, new StgUV(8, 0));

            // World size: 96px×5 wide, 32px×5 tall (5 PSX-pixel-to-viewer-unit scale)
            const float W = 96f * 5f;    // 480 PSX units wide
            const float H = 32f * 5f;    // 160 PSX units tall

            var fillColor = new Color(160, 140, 110);    // earthy/bark tone (fallback)
            var wireColor = new Color(140, 120, 80);     // dark outline

            var wire     = new List<VertexPositionColor>(BgBillboardPositions.Length * 8);
            var solid    = new List<VertexPositionColor>(BgBillboardPositions.Length * 6);
            var texVerts = bgTx != null ? new List<VertexPositionTexture>(BgBillboardPositions.Length * 6) : null;

            // UV corners: sprite area u=8..103, v=0..31 within the 104×32 sprite sheet.
            // v00/v10 = bottom edge of billboard in view  = v=31
            // v01/v11 = top    edge of billboard in view  = v=0
            Vector2 uvBL = Vector2.Zero, uvBR = Vector2.Zero, uvTL = Vector2.Zero, uvTR = Vector2.Zero;
            if (bgTx != null)
            {
                uvBL = ComputeUV(bgTx, new StgUV(8,   31), 14, 0);  // bottom-left  (u=8,  v=31)
                uvBR = ComputeUV(bgTx, new StgUV(103, 31), 14, 0);  // bottom-right (u=103,v=31)
                uvTL = ComputeUV(bgTx, new StgUV(8,   0),  14, 0);  // top-left     (u=8,  v=0)
                uvTR = ComputeUV(bgTx, new StgUV(103, 0),  14, 0);  // top-right    (u=103,v=0)
            }

            foreach (var (px, py, pz) in BgBillboardPositions)
            {
                var center = new Vector3(px, py, pz);

                // Orient the quad to face radially outward from origin.
                // right = cross(worldUp, radial).  If on the Y-axis, fall back to +X.
                var radial = new Vector3(px, 0f, pz);
                float rLen = radial.Length();
                Vector3 right = rLen > 1f
                    ? Vector3.Cross(Vector3.UnitY, radial / rLen)
                    : Vector3.UnitX;
                right = Vector3.Normalize(right);
                // PSX Y-down: negative Y = above floor.
                // The world matrix flips Y, so build quads in PSX-space with -UnitY as "up".
                var up = -Vector3.UnitY;

                // Quad corners (CCW from front)
                var v00 = center - right * (W * 0.5f);                   // bottom-left
                var v10 = center + right * (W * 0.5f);                   // bottom-right
                var v01 = center - right * (W * 0.5f) + up * H;         // top-left
                var v11 = center + right * (W * 0.5f) + up * H;         // top-right

                // ── Wireframe: 4 edges ──────────────────────────────────────
                wire.Add(new VertexPositionColor(v00, wireColor));
                wire.Add(new VertexPositionColor(v10, wireColor));
                wire.Add(new VertexPositionColor(v10, wireColor));
                wire.Add(new VertexPositionColor(v11, wireColor));
                wire.Add(new VertexPositionColor(v11, wireColor));
                wire.Add(new VertexPositionColor(v01, wireColor));
                wire.Add(new VertexPositionColor(v01, wireColor));
                wire.Add(new VertexPositionColor(v00, wireColor));

                // ── Solid: 2 tris ───────────────────────────────────────────
                solid.Add(new VertexPositionColor(v00, fillColor));
                solid.Add(new VertexPositionColor(v10, fillColor));
                solid.Add(new VertexPositionColor(v01, fillColor));
                solid.Add(new VertexPositionColor(v10, fillColor));
                solid.Add(new VertexPositionColor(v11, fillColor));
                solid.Add(new VertexPositionColor(v01, fillColor));

                // ── Textured: 2 tris (v00/v10 = bottom of view = bottom of texture V≈1) ──
                if (texVerts != null)
                {
                    texVerts.Add(new VertexPositionTexture(v00, uvBL));
                    texVerts.Add(new VertexPositionTexture(v10, uvBR));
                    texVerts.Add(new VertexPositionTexture(v01, uvTL));
                    texVerts.Add(new VertexPositionTexture(v10, uvBR));
                    texVerts.Add(new VertexPositionTexture(v11, uvTR));
                    texVerts.Add(new VertexPositionTexture(v01, uvTL));
                }
            }

            _bgWireVerts  = wire.ToArray();
            _bgSolidVerts = solid.ToArray();
            _bgTexGroups  = bgTx != null && texVerts!.Count > 0
                ? new List<(Texture2D, VertexPositionTexture[])> { (bgTx.Texture, texVerts!.ToArray()) }
                : new List<(Texture2D, VertexPositionTexture[])>();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Sky background panorama
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Build the sky panorama from the 8bpp 128×256 sky texture (STGxTX.B entry[7]).
        ///
        /// The game (FUN_80041c6c / FUN_80041ee4) renders this as 8 screen-space POLY_FT4 quads:
        ///   GPU tpage=0x8F (8bpp, VRAM 960,0), clut=0x7900 → palette VRAM(0,484) [256-color].
        ///   Loop i=0..7: vTop=(i%2)*128   →  0, 128, 0, 128, 0, 128, 0, 128
        ///                vBot= vTop+127   → 127, 255, 127, 255, 127, 255, 127, 255
        ///   Even quads (i=0,2,4,6): TOP    half of the sky texture (rows   0–127).
        ///   Odd  quads (i=1,3,5,7): BOTTOM half of the sky texture (rows 128–255).
        ///
        /// In the viewer we place 8 large panels in a ring at radius 7000 PSX units,
        /// each panel 640 wide × 2000 tall, alternating the two UV bands.
        /// </summary>
        private void BuildSkyBackground(List<TxEntry> txEntries)
        {
            // tpageX=15 (8bpp at VRAM 960,0), tpageY=0; clut clutVramY=484 = entry[6]
            var skyTx = FindTexture(txEntries, 15, 0, new StgUV(0, 0));

            const float R       = 7000f;   // ring radius (well outside the stage)
            // Y values from InitSkyBackgroundQuads (Ghidra):
            //   PSX Y_top = 0xff84 = -124   → viewer Y = +(-124 × -7) = +868  (Y-flip, scale=7)
            //   PSX Y_bot = 0x0004 =   +4   → viewer Y = +(  4 × -7) =  -28
            const float PanelYTop = 868f;  // = -(-124) × 7
            const float PanelYBot = -28f;  // = -(   4) × 7
            const float PanelH  = PanelYTop - PanelYBot;   // 896  = 128 px × 7
            // World matrix has CreateScale(1,-1,1) → screen_Y = -code_Y.
            // So code_Y must be NEGATIVE to appear above floor on screen.
            // PanelYC = -420 → screen centre = +420 (above floor)
            const float PanelYC = -((PanelYTop + PanelYBot) * 0.5f);  // -420

            // Sky blue: matches the background clear colour used on PSX stages.
            var wireColor  = new Color(140, 160, 220);
            var solidColor = new Color( 60,  90, 160);   // opaque sky blue

            var wire     = new List<VertexPositionColor>(8 * 8);
            var solid    = new List<VertexPositionColor>(8 * 6);
            var texVerts = skyTx != null ? new List<VertexPositionTexture>(8 * 6) : null;

            // Place panel corners ON the arc so adjacent panels touch with no gap.
            // Each panel spans TwoPi/8 radians; left edge at angle - pi/8, right at angle + pi/8.
            for (int i = 0; i < 8; i++)
            {
                float angleC = i * MathHelper.TwoPi / 8f;
                float halfStep = MathHelper.TwoPi / 16f;   // half of TwoPi/8

                // Left and right edge positions on the ring (Y = ±PanelH/2)
                float aL = angleC - halfStep;
                float aR = angleC + halfStep;
                var edgeL = new Vector3(-(float)Math.Sin(aL) * R, PanelYC, -(float)Math.Cos(aL) * R);
                var edgeR = new Vector3(-(float)Math.Sin(aR) * R, PanelYC, -(float)Math.Cos(aR) * R);

                // Quad corners: v00/v10 = bottom (−H/2), v01/v11 = top (+H/2)
                var v00 = edgeL - Vector3.UnitY * PanelH * 0.5f;
                var v10 = edgeR - Vector3.UnitY * PanelH * 0.5f;
                var v01 = edgeL + Vector3.UnitY * PanelH * 0.5f;
                var v11 = edgeR + Vector3.UnitY * PanelH * 0.5f;

                // ── Wireframe ───────────────────────────────────────────────
                wire.Add(new VertexPositionColor(v00, wireColor));
                wire.Add(new VertexPositionColor(v10, wireColor));
                wire.Add(new VertexPositionColor(v10, wireColor));
                wire.Add(new VertexPositionColor(v11, wireColor));
                wire.Add(new VertexPositionColor(v11, wireColor));
                wire.Add(new VertexPositionColor(v01, wireColor));
                wire.Add(new VertexPositionColor(v01, wireColor));
                wire.Add(new VertexPositionColor(v00, wireColor));

                // ── Solid ───────────────────────────────────────────────────
                solid.Add(new VertexPositionColor(v00, solidColor));
                solid.Add(new VertexPositionColor(v10, solidColor));
                solid.Add(new VertexPositionColor(v01, solidColor));
                solid.Add(new VertexPositionColor(v10, solidColor));
                solid.Add(new VertexPositionColor(v11, solidColor));
                solid.Add(new VertexPositionColor(v01, solidColor));

                // ── Textured: FUN_80041c6c UV alternation ──────────────────
                // Even quads → top half    (vTop=0,  vBot=127): rows   0–127 of 256-tall image
                // Odd  quads → bottom half (vTop=128, vBot=255): rows 128–255 of 256-tall image
                //
                // UV inversion fix: world matrix flips Y (PSX Y-down → MG Y-up).
                //   v00/v10 are built at -UnitY*H/2 → PSX Y small (above ground) → renders at SCREEN TOP.
                //   v01/v11 are built at +UnitY*H/2 → PSX Y large (below ground) → renders at SCREEN BOTTOM.
                //   Therefore v00 (screen top)    must receive uvTL/uvTR (vTop = PSX texture top).
                //             v01 (screen bottom) must receive uvBL/uvBR (vBot = PSX texture bottom).
                if (texVerts != null && skyTx != null)
                {
                    byte vTop = (byte)((i % 2) * 128);          // 0 or 128
                    byte vBot = (byte)(vTop + 127);              // 127 or 255
                    var uvBL = ComputeUV(skyTx, new StgUV(  0, vBot), 15, 0);  // bottom of texture sub-band
                    var uvBR = ComputeUV(skyTx, new StgUV(127, vBot), 15, 0);
                    var uvTL = ComputeUV(skyTx, new StgUV(  0, vTop), 15, 0);  // top of texture sub-band
                    var uvTR = ComputeUV(skyTx, new StgUV(127, vTop), 15, 0);

                    // v00/v10 = screen top → vTop;  v01/v11 = screen bottom → vBot
                    texVerts.Add(new VertexPositionTexture(v00, uvTL));
                    texVerts.Add(new VertexPositionTexture(v10, uvTR));
                    texVerts.Add(new VertexPositionTexture(v01, uvBL));
                    texVerts.Add(new VertexPositionTexture(v10, uvTR));
                    texVerts.Add(new VertexPositionTexture(v11, uvBR));
                    texVerts.Add(new VertexPositionTexture(v01, uvBL));
                }
            }

            _skyWireVerts  = wire.ToArray();
            _skySolidVerts = solid.ToArray();
            _skyTexGroups  = skyTx != null && texVerts!.Count > 0
                ? new List<(Texture2D, VertexPositionTexture[])> { (skyTx.Texture, texVerts!.ToArray()) }
                : new List<(Texture2D, VertexPositionTexture[])>();
        }

        private static Vector2 FlipY(Vector2 uv) => new Vector2(uv.X, 1f - uv.Y);

        private static Color Brighten(Color c) => new Color(
            Math.Min(255, c.R * 2 + 40),
            Math.Min(255, c.G * 2 + 40),
            Math.Min(255, c.B * 2 + 40));

        // ─────────────────────────────────────────────────────────────────────
        // TX texture loading + UV math
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Decoded texture + its position in PSX VRAM (16bpp word units).</summary>
        private sealed class TxEntry
        {
            public Texture2D Texture;
            public int VramX16;        // VRAM X in 16bpp word units
            public int VramY;          // VRAM Y in scanlines (= pixel Y)
            public int Is8bpp;         // 1 = 8bpp, 0 = 4bpp (affects U→pixel conversion)
            /// <summary>
            /// VRAM X (16bpp units) of the sub-palette used to decode this texture copy.
            /// Matches the ClutVramX property of primitives that reference this sub-palette
            /// via their CBA field: CBA.ClutVramX = (CBA &amp; 0x3F) * 16.
            /// </summary>
            public int SubPaletteX;
            /// <summary>VRAM Y (scanline) of the CLUT this sub-palette belongs to.</summary>
            public int SubPaletteY;

            public int PixelX => Is8bpp == 1 ? VramX16 * 2 : VramX16 * 4;
            public int TPageX => PixelX / (Is8bpp == 1 ? 128 : 256);
            public int TPageY => VramY / 256;
        }

        /// <summary>
        /// Find the TX entry that contains the texel (tpageX, tpageY, uv) for the given CBA sub-palette.
        ///
        /// Priority order:
        ///   Pass 1 — exact tpageX + V-range + sub-palette VRAM XY match (preferred: correct colors)
        ///   Pass 2 — exact tpageX + V-range (any sub-palette — floor/fallback)
        ///   Pass 3 — tpageX + tpageY only
        ///   Pass 4 — bpp-aware X coverage + V-range
        ///   Pass 5 — V-range-only last-resort
        /// </summary>
        private static TxEntry? FindTexture(List<TxEntry> entries, int tpageX, int tpageY, StgUV uv, ushort cba = 0)
        {
            int absY      = tpageY * 256 + uv.V;
            int clutVramX = (cba & 0x3F) * 16;
            int clutVramY = (cba >> 6) & 0x1FF;

            // Pass 1: exact tpageX + V-range + sub-palette VRAM position match
            foreach (var e in entries)
                if (e.TPageX == tpageX
                 && e.VramY <= absY && absY < e.VramY + e.Texture.Height
                 && e.SubPaletteX == clutVramX && e.SubPaletteY == clutVramY)
                    return e;

            // Pass 2: exact tpageX + V-range (any sub-palette — floor, fallback, unindexed)
            foreach (var e in entries)
                if (e.TPageX == tpageX
                 && e.VramY <= absY && absY < e.VramY + e.Texture.Height)
                    return e;

            // Pass 3: entry covers the tpage column, relax Y to any overlap
            foreach (var e in entries)
                if (e.TPageX == tpageX && e.TPageY == tpageY)
                    return e;

            // Pass 4: coverage check with bpp-aware X, matching V band
            foreach (int pagePixW in new[] { 256, 128 })
            {
                int absPixX = tpageX * pagePixW + uv.U;
                foreach (var e in entries)
                {
                    int pw  = e.Is8bpp == 1 ? 128 : 256;
                    int epx = e.TPageX * pw;
                    if (epx <= absPixX && absPixX < epx + e.Texture.Width
                     && e.VramY <= absY  && absY  < e.VramY + e.Texture.Height)
                        return e;
                }
            }

            // Pass 5: V-range-only fallback
            TxEntry? best = null;
            foreach (var e in entries)
                if (e.VramY <= absY && absY < e.VramY + e.Texture.Height)
                    if (best == null || e.Texture.Height < best.Texture.Height)
                        best = e;
            return best;
        }

        /// <summary>Convert raw PSX UV byte into [0,1] texture coord.</summary>
        private static Vector2 ComputeUV(TxEntry tx, StgUV uv, int tpageX, int tpageY)
        {
            int pagePixW = tx.Is8bpp == 1 ? 128 : 256;
            int absPixY  = tpageY * 256 + uv.V;
            float v = (absPixY - tx.VramY) / (float)Math.Max(1, tx.Texture.Height);

            float u;
            if (tx.TPageX == tpageX)
            {
                // Normal path: compute U from the primitive's absolute VRAM pixel X.
                // absPixX = tpageX * pagePixW + U  (e.g. 12*256+16 = 3088)
                // tx.PixelX = VramX16 * 4            (e.g. 768*4  = 3072)
                // u = (3088-3072)/256 = 0.0625  → pixel 16 inside the 256-px-wide texture
                int absPixX = tpageX * pagePixW + uv.U;
                u = (absPixX - tx.PixelX) / (float)Math.Max(1, tx.Texture.Width);
            }
            else
            {
                // Pass-4 fallback: tpageX mismatch (should not normally occur after the
                // ReadUVs parser fix).  Normalise U within the page width as a best effort.
                u = uv.U / (float)pagePixW;
            }

            return new Vector2(MathHelper.Clamp(u, 0f, 1f),
                               MathHelper.Clamp(v, 0f, 1f));
        }

        /// <summary>
        /// Load all non-CLUT textures from a STGxTX.B file.
        /// Each 4bpp image is decoded once per 16-color sub-palette packed in its associated
        /// CLUT block, so that FindTexture can select the exact sub-palette indicated by each
        /// primitive's CBA field.
        ///
        /// Example: STG1TX CLUT #0 has 80 colors = 5 sub-palettes at VRAM X = 0, 16, 32, 48, 64.
        /// Mesh primitives with CBA=0x7942 use sub-palette at X=32 (clutX=(2)*16=32).
        /// Each image is decoded 5 times; FindTexture picks the copy whose SubPaletteX matches.
        /// </summary>
        private List<TxEntry> LoadTxTextures(string txPath, GraphicsDevice gd)
        {
            var result = new List<TxEntry>();
            try
            {
                byte[] file = File.ReadAllBytes(txPath);
                if (file.Length < 4) return result;

                uint count = BitConverter.ToUInt32(file, 0);

                // Pass 1: collect all CLUT entries with their raw color arrays and VRAM positions
                var clutByIndex = new Dictionary<int, (ushort[] Colors, int VramX, int VramY, int ColorCount)>();
                for (int i = 0; i < count; i++)
                {
                    int e = 4 + i * 28;
                    if (BitConverter.ToUInt32(file, e + 24) != 1) continue; // isClut check
                    uint dataOff = BitConverter.ToUInt32(file, e + 4);
                    uint vramX   = BitConverter.ToUInt32(file, e + 8);
                    uint vramY   = BitConverter.ToUInt32(file, e + 12);
                    uint width   = BitConverter.ToUInt32(file, e + 16);
                    int  colCnt  = (int)width;
                    if ((int)dataOff + colCnt * 2 > file.Length) continue;
                    ushort[] c = BinaryReaderHelper.ReadUShortArrayFast(file, (int)dataOff, colCnt);
                    clutByIndex[i] = (c, (int)vramX, (int)vramY, colCnt);
                }

                var fallbackClut = clutByIndex.Count > 0
                    ? clutByIndex.Values.First()
                    : (Colors: new ushort[16], VramX: 0, VramY: 0, ColorCount: 16);

                // Pass 2: decode images — one TxEntry copy per sub-palette
                for (int i = 0; i < count; i++)
                {
                    int e = 4 + i * 28;
                    try
                    {
                        uint compType = BitConverter.ToUInt32(file, e + 0);
                        uint dataOff  = BitConverter.ToUInt32(file, e + 4);
                        uint vramX    = BitConverter.ToUInt32(file, e + 8);
                        uint vramY    = BitConverter.ToUInt32(file, e + 12);
                        uint width    = BitConverter.ToUInt32(file, e + 16);
                        uint height   = BitConverter.ToUInt32(file, e + 20);
                        uint isClut   = BitConverter.ToUInt32(file, e + 24);
                        if (isClut != 0) continue;

                        int absOff  = (int)dataOff;
                        int dataSize = i + 1 < count
                            ? (int)(BitConverter.ToUInt32(file, 4 + (i + 1) * 28 + 4) - dataOff)
                            : file.Length - absOff;
                        if (dataSize <= 0) continue;

                        byte[] imgData = compType == 0
                            ? LzssDecompressor.Decompress(file[absOff..(absOff + dataSize)])
                            : file[absOff..(absOff + dataSize)];

                        // Find the preceding CLUT entry for this image
                        var pal = fallbackClut;
                        for (int p = i - 1; p >= 0; p--)
                            if (clutByIndex.TryGetValue(p, out var found)) { pal = found; break; }

                        int  cpp    = pal.ColorCount >= 256 ? 256 : 16;
                        bool is8bpp = cpp == 256;
                        var  mode   = is8bpp ? PsxImageDecoder.PsxPixelMode.Bpp8 : PsxImageDecoder.PsxPixelMode.Bpp4;
                        var  layout = new PsxImageDecoder.PsxImageLayout((int)width, (int)height);
                        var  fmt    = new PsxImageDecoder.PsxImageFormat(mode);

                        // For 4bpp: decode once per 16-color sub-palette packed in this CLUT.
                        // For 8bpp: only a single 256-color palette.
                        int numSubs = is8bpp ? 1 : Math.Max(1, pal.ColorCount / 16);

                        for (int sp = 0; sp < numSubs; sp++)
                        {
                            PsxImageDecoder.PsxClut subPal;
                            if (!is8bpp)
                            {
                                int srcOff = sp * 16;
                                int avail  = Math.Max(0, pal.Colors.Length - srcOff);
                                var c16    = new ushort[16];
                                if (avail > 0) Array.Copy(pal.Colors, srcOff, c16, 0, Math.Min(16, avail));
                                subPal = new PsxImageDecoder.PsxClut(c16, 16);
                            }
                            else
                            {
                                ushort[] c256 = new ushort[256];
                                Array.Copy(pal.Colors, c256, Math.Min(256, pal.Colors.Length));
                                subPal = new PsxImageDecoder.PsxClut(c256, 256);
                            }

                            var tex = PsxImageDecoder.DecodeToTexture2D(gd, imgData, layout, fmt, subPal, 0);
                            _ownedTextures.Add(tex);

                            // Sub-palette VRAM position: base X + sp*16 (each sub-palette is 16 entries wide)
                            int subPalVramX = pal.VramX + (is8bpp ? 0 : sp * 16);
                            result.Add(new TxEntry
                            {
                                Texture     = tex,
                                VramX16     = (int)vramX,
                                VramY       = (int)vramY,
                                Is8bpp      = is8bpp ? 1 : 0,
                                SubPaletteX = subPalVramX,
                                SubPaletteY = pal.VramY,
                            });
                        }
                    }
                    catch { /* skip bad entry */ }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TX] Failed to load: {ex.Message}");
            }
            return result;
        }
    }
}
