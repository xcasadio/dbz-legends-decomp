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
    ///   Left-drag         — arcball rotate (orbit around target)
    ///   L+R drag          — pan target (horizontal strafe + world-Y lift)
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
                float panSpd = _distance * 0.002f;
                _target -= camRight   * mdx * panSpd;   // horizontal strafe
                _target.Y += mdy * panSpd;              // world-Y lift
            }
            // ── Left-only drag → arcball rotate ───────────────────────────────
            else if (lbHeld && !rbHeld)
            {
                _azimuth   -= mdx * 0.008f;
                _elevation += mdy * 0.008f;
                _elevation  = MathHelper.Clamp(_elevation,
                    -MathHelper.PiOver2 + 0.02f,
                     MathHelper.PiOver2 - 0.02f);
            }

            // ── Scroll → zoom (change orbit distance) ─────────────────────────
            if (inBounds)
            {
                int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
                if (scroll > 0) _distance *= 0.88f;
                else if (scroll < 0) _distance *= 1.12f;
                _distance = MathHelper.Clamp(_distance, 1f, 500000f);
            }

            // ── Arrow / WASD → pan target in horizontal plane ─────────────────
            Vector3 fwd = ComputeForward();
            var fwdXZ = new Vector3(fwd.X, 0f, fwd.Z);
            if (fwdXZ.LengthSquared() > 0.0001f) fwdXZ = Vector3.Normalize(fwdXZ);
            else fwdXZ = Vector3.Forward;

            Vector3 right = Vector3.Normalize(Vector3.Cross(fwdXZ, Vector3.Up));
            float speed = _distance * dt * 1.2f;

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
                    if (_lineVerts.Length >= 2)
                        gd.DrawUserPrimitives(PrimitiveType.LineList,
                            _lineVerts, 0, _lineVerts.Length / 2);
                    if (_floorWireVerts.Length >= 2)
                        gd.DrawUserPrimitives(PrimitiveType.LineList,
                            _floorWireVerts, 0, _floorWireVerts.Length / 2);
                }
            }
            else if (_displayMode == DisplayMode.Solid)
            {
                foreach (var pass in _basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    if (_floorSolidVerts.Length >= 3)
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList,
                            _floorSolidVerts, 0, _floorSolidVerts.Length / 3);
                    if (_solidVerts.Length >= 3)
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList,
                            _solidVerts, 0, _solidVerts.Length / 3);
                }
            }
            else if (_displayMode == DisplayMode.Textured)
            {
                ApplyMatrices(_texEffect, _bounds);
                gd.SamplerStates[0] = SamplerState.LinearClamp;
                // Draw floor first (behind meshes)
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
                // Overlay the floor wireframe grid for orientation
                ApplyMatrices(_basicEffect, _bounds);
                if (_floorWireVerts.Length >= 2)
                    foreach (var pass in _basicEffect.CurrentTechnique.Passes)
                    { pass.Apply(); gd.DrawUserPrimitives(PrimitiveType.LineList, _floorWireVerts, 0, _floorWireVerts.Length / 2); }
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
                    var tx = FindTexture(txEntries, tri.TPageX, tri.TPageY, tri.UV0);
                    if (tx != null)
                    {
                        var uv0 = FlipY(ComputeUV(tx, tri.UV0, tri.TPageX, tri.TPageY));
                        var uv1 = FlipY(ComputeUV(tx, tri.UV1, tri.TPageX, tri.TPageY));
                        var uv2 = FlipY(ComputeUV(tx, tri.UV2, tri.TPageX, tri.TPageY));
                        // UV.y flipped to compensate for the Y-scale(-1) world transform

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
                    var fuv00 = FlipY(ComputeUV(floorTx, new StgUV(uOff,      vOff),      11, 0));
                    var fuv10 = FlipY(ComputeUV(floorTx, new StgUV((byte)(uOff + 31), vOff),      11, 0));
                    var fuv01 = FlipY(ComputeUV(floorTx, new StgUV(uOff,      (byte)(vOff + 31)), 11, 0));
                    var fuv11 = FlipY(ComputeUV(floorTx, new StgUV((byte)(uOff + 31), (byte)(vOff + 31)), 11, 0));

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

            public int PixelX => Is8bpp == 1 ? VramX16 * 2 : VramX16 * 4;
            public int TPageX => PixelX / (Is8bpp == 1 ? 128 : 256);
            public int TPageY => VramY / 256;
        }

        /// <summary>
        /// Find the TX entry that contains the texel (tpageX, tpageY, uv).
        /// Multiple entries can share the same tpage but occupy different Y bands
        /// (e.g. two 128-tall textures at vramY=0 and vramY=128 in tpageY=0).
        /// Using the actual V value avoids returning the wrong sub-texture.
        /// </summary>
        private static TxEntry? FindTexture(List<TxEntry> entries, int tpageX, int tpageY, StgUV uv)
        {
            int absY = tpageY * 256 + uv.V;

            // Pass 1: exact tpageX column + V falls within entry's scanline band
            foreach (var e in entries)
                if (e.TPageX == tpageX
                 && e.VramY <= absY && absY < e.VramY + e.Texture.Height)
                    return e;

            // Pass 2: entry covers the tpage column, relax Y to any overlap
            foreach (var e in entries)
                if (e.TPageX == tpageX && e.TPageY == tpageY)
                    return e;

            // Pass 3: coverage check with bpp-aware X, matching V band
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

            return entries.Count > 0 ? entries[0] : null;
        }

        /// <summary>Convert raw PSX UV byte into [0,1] texture coord.</summary>
        private static Vector2 ComputeUV(TxEntry tx, StgUV uv, int tpageX, int tpageY)
        {
            int pagePixW = tx.Is8bpp == 1 ? 128 : 256;
            int absPixX  = tpageX * pagePixW + uv.U;
            int absPixY  = tpageY * 256       + uv.V;
            float u = (absPixX - tx.PixelX) / (float)Math.Max(1, tx.Texture.Width);
            float v = (absPixY - tx.VramY)  / (float)Math.Max(1, tx.Texture.Height);
            return new Vector2(MathHelper.Clamp(u, 0f, 1f),
                               MathHelper.Clamp(v, 0f, 1f));
        }

        /// <summary>
        /// Load all non-CLUT textures from a STGxTX.B file.
        /// Mirrors the decoding logic in STG_TX_View, but returns TxEntry records
        /// keyed by their VRAM position for UV mapping.
        /// </summary>
        private List<TxEntry> LoadTxTextures(string txPath, GraphicsDevice gd)
        {
            var result = new List<TxEntry>();
            try
            {
                byte[] file = File.ReadAllBytes(txPath);
                if (file.Length < 4) return result;

                uint count = BitConverter.ToUInt32(file, 0);

                // Pass 1: collect CLUTs
                var palettes = new Dictionary<int, PsxImageDecoder.PsxClut>();
                for (int i = 0; i < count; i++)
                {
                    int e = 4 + i * 28;
                    uint dataOff  = BitConverter.ToUInt32(file, e + 4);
                    uint width    = BitConverter.ToUInt32(file, e + 16);
                    uint isClut   = BitConverter.ToUInt32(file, e + 24);
                    if (isClut != 1) continue;
                    int colorCount = (int)width;
                    if ((int)dataOff + colorCount * 2 > file.Length) continue;
                    ushort[] c = BinaryReaderHelper.ReadUShortArrayFast(file, (int)dataOff, colorCount);
                    int cpp = colorCount >= 256 ? 256 : 16;
                    palettes[i] = new PsxImageDecoder.PsxClut(c, cpp);
                }

                var fallback = palettes.Count > 0
                    ? palettes.Values.First()
                    : new PsxImageDecoder.PsxClut(new ushort[16], 16);

                // Pass 2: decode images
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

                        // Find preceding palette
                        PsxImageDecoder.PsxClut pal = fallback;
                        for (int p = i - 1; p >= 0; p--)
                            if (palettes.TryGetValue(p, out var found)) { pal = found; break; }

                        bool is8bpp = pal.ColorsPerPalette == 256;
                        var mode = is8bpp
                            ? PsxImageDecoder.PsxPixelMode.Bpp8
                            : PsxImageDecoder.PsxPixelMode.Bpp4;

                        if (!is8bpp && pal.ColorsPerPalette != 16)
                        {
                            ushort[] c16 = new ushort[16];
                            Array.Copy(pal.ColorsBgr555, c16, Math.Min(16, pal.ColorsBgr555.Length));
                            pal = new PsxImageDecoder.PsxClut(c16, 16);
                        }

                        var tex = PsxImageDecoder.DecodeToTexture2D(gd, imgData,
                            new PsxImageDecoder.PsxImageLayout((int)width, (int)height),
                            new PsxImageDecoder.PsxImageFormat(mode), pal, 0);

                        _ownedTextures.Add(tex);
                        result.Add(new TxEntry
                        {
                            Texture  = tex,
                            VramX16  = (int)vramX,
                            VramY    = (int)vramY,
                            Is8bpp   = is8bpp ? 1 : 0,
                        });
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
