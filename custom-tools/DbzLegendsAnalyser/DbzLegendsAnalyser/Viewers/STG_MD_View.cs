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
    ///   Left-drag        — arcball rotate (orbit around target)
    ///   Arrows / WASD    — move camera target (forward/back/strafe)
    ///   Scroll wheel     — zoom (change orbit distance)
    ///   R                — reset view
    ///
    /// List items (shown in image listbox):
    ///   index 0 "Wireframe" — blue edge-only rendering
    ///   index 1 "Solid"     — filled triangles with vertex colors
    /// </summary>
    public class STG_MD_View : IAnalyserView
    {
        // ── Display mode ──────────────────────────────────────────────────────
        private enum DisplayMode { Wireframe, Solid }
        private DisplayMode _displayMode = DisplayMode.Wireframe;

        // ── Model ─────────────────────────────────────────────────────────────
        private StgModelFile? _model;
        private VertexPositionColor[] _lineVerts  = Array.Empty<VertexPositionColor>();
        private VertexPositionColor[] _solidVerts = Array.Empty<VertexPositionColor>();
        private Vec3  _sceneCenter;
        private float _sceneScale = 1f;

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

            _model = StgMdLoader.Load(filePath);
            var worldTris = _model.GetWorldTriangles().ToArray();

            ComputeSceneTransform(worldTris);
            BuildGeometry(worldTris);
            ResetView();
        }

        public string[] GetListItems() => new[]
        {
            _displayMode == DisplayMode.Wireframe ? "[•] Wireframe" : "[ ] Wireframe",
            _displayMode == DisplayMode.Solid     ? "[•] Solid"     : "[ ] Solid",
        };

        public void OnItemSelected(int index)
        {
            _displayMode = index == 1 ? DisplayMode.Solid : DisplayMode.Wireframe;
        }

        public void Update(GameTime gameTime, Rectangle contentBounds)
        {
            _bounds = contentBounds;

            var mouse     = Mouse.GetState();
            var keyboard  = Keyboard.GetState();
            bool inBounds = _bounds.Contains(mouse.Position);
            float dt      = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // ── Left drag → arcball rotate ────────────────────────────────────
            if (mouse.LeftButton == ButtonState.Pressed
             && _prevMouse.LeftButton == ButtonState.Pressed)
            {
                int dx = mouse.X - _prevMouse.X;
                int dy = mouse.Y - _prevMouse.Y;
                _azimuth   -= dx * 0.008f;
                _elevation += dy * 0.008f;
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
            // Project forward onto XZ plane so arrows don't tilt the target vertically
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

            if (_displayMode == DisplayMode.Wireframe && _lineVerts.Length > 0)
            {
                foreach (var pass in _basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    gd.DrawUserPrimitives(PrimitiveType.LineList,
                        _lineVerts, 0, _lineVerts.Length / 2);
                }
            }
            else if (_displayMode == DisplayMode.Solid && _solidVerts.Length > 0)
            {
                foreach (var pass in _basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    gd.DrawUserPrimitives(PrimitiveType.TriangleList,
                        _solidVerts, 0, _solidVerts.Length / 3);
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

        private void BuildGeometry(StgTriangle[] tris)
        {
            var wireColor = new Color(60, 160, 240);

            var lines = new List<VertexPositionColor>(tris.Length * 6);
            var solid = new List<VertexPositionColor>(tris.Length * 3);

            foreach (var tri in tris)
            {
                var p0 = new Vector3(tri.V0.X, tri.V0.Y, tri.V0.Z);
                var p1 = new Vector3(tri.V1.X, tri.V1.Y, tri.V1.Z);
                var p2 = new Vector3(tri.V2.X, tri.V2.Y, tri.V2.Z);

                // Wireframe: 3 edges (6 verts)
                lines.Add(new VertexPositionColor(p0, wireColor));
                lines.Add(new VertexPositionColor(p1, wireColor));
                lines.Add(new VertexPositionColor(p1, wireColor));
                lines.Add(new VertexPositionColor(p2, wireColor));
                lines.Add(new VertexPositionColor(p2, wireColor));
                lines.Add(new VertexPositionColor(p0, wireColor));

                // Solid: filled triangle with brightened vertex/face color
                solid.Add(new VertexPositionColor(p0, Brighten(tri.C0)));
                solid.Add(new VertexPositionColor(p1, Brighten(tri.C1)));
                solid.Add(new VertexPositionColor(p2, Brighten(tri.C2)));
            }

            _lineVerts  = lines.ToArray();
            _solidVerts = solid.ToArray();
        }

        private static Color Brighten(Color c) => new Color(
            Math.Min(255, c.R * 2 + 40),
            Math.Min(255, c.G * 2 + 40),
            Math.Min(255, c.B * 2 + 40));
    }
}
