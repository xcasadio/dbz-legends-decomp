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
    /// 3D wireframe viewer for STG\STGxMD.B stage mesh files.
    ///
    /// Controls:
    ///   Left-drag   — rotate (yaw + pitch)
    ///   Right-drag  — pan
    ///   Scroll      — zoom
    ///   R key       — reset view
    /// </summary>
    public class STG_MD_View : IAnalyserView
    {
        // ── Model ─────────────────────────────────────────────────────────────
        private StgModelFile? _model;
        private VertexPositionColor[] _lineVerts = Array.Empty<VertexPositionColor>();
        private VertexPositionColor[] _lineVertsColorized = Array.Empty<VertexPositionColor>();

        private Vec3 _sceneCenter;
        private float _sceneScale = 1f;

        // ── Camera ────────────────────────────────────────────────────────────
        private float _yaw   = 0.4f;
        private float _pitch = 0.3f;
        private float _zoom  = 1f;
        private float _panX  = 0f;
        private float _panY  = 0f;

        // ── Viewer state ──────────────────────────────────────────────────────
        private bool _colorizeByType;
        private string _stats = string.Empty;
        private Rectangle _bounds;

        // ── MonoGame resources ────────────────────────────────────────────────
        private GraphicsDevice _graphicsDevice;
        private BasicEffect _basicEffect;

        // ── Input ─────────────────────────────────────────────────────────────
        private MouseState _prevMouse;
        private KeyboardState _prevKeyboard;

        // ── IAnalyserView ─────────────────────────────────────────────────────

        public void Initialize(string filePath, GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
            _basicEffect = new BasicEffect(graphicsDevice)
            {
                VertexColorEnabled = true,
                LightingEnabled = false,
                TextureEnabled = false
            };

            _model = StgMdLoader.Load(filePath);
            var worldTris = _model.GetWorldTriangles().ToArray();

            ComputeSceneTransform(worldTris);
            BuildLineGeometry(worldTris);

            int meshCount = _model.MeshEntries.Count(m => m.FileOffset > 0);
            _stats = $"{Path.GetFileName(filePath)}  |  {meshCount} meshes  " +
                     $"|  {_model.Particles.Count} particles  " +
                     $"|  {worldTris.Length:N0} tris  " +
                     $"[L-drag: rotate  R-drag: pan  Scroll: zoom  R: reset]";
        }

        public string[] GetListItems()
        {
            if (_model == null) return Array.Empty<string>();
            var items = new List<string>();
            items.Add("All meshes (world)");
            items.Add(_colorizeByType ? "[x] Colorize by type" : "[ ] Colorize by type");
            return items.ToArray();
        }

        public void OnItemSelected(int index)
        {
            if (index == 1)
            {
                _colorizeByType = !_colorizeByType;
            }
        }

        public void Update(GameTime gameTime, Rectangle contentBounds)
        {
            _bounds = contentBounds;

            var mouse = Mouse.GetState();
            var keyboard = Keyboard.GetState();
            bool inBounds = _bounds.Contains(mouse.Position);

            // Left drag → rotate
            if (inBounds && mouse.LeftButton == ButtonState.Pressed
                         && _prevMouse.LeftButton == ButtonState.Released)
            {
                // drag start captured implicitly via delta
            }

            if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Pressed)
            {
                int dx = mouse.X - _prevMouse.X;
                int dy = mouse.Y - _prevMouse.Y;
                if (inBounds || (dx != 0 || dy != 0))
                {
                    _yaw   += dx * 0.008f;
                    _pitch += dy * 0.008f;
                    _pitch  = MathHelper.Clamp(_pitch,
                        -MathHelper.PiOver2 + 0.05f,
                         MathHelper.PiOver2 - 0.05f);
                }
            }

            // Right drag → pan
            if (mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Pressed)
            {
                _panX += mouse.X - _prevMouse.X;
                _panY += mouse.Y - _prevMouse.Y;
            }

            // Scroll → zoom
            if (inBounds)
            {
                int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
                if (scroll > 0) _zoom *= 1.12f;
                else if (scroll < 0) _zoom *= 0.89f;
                _zoom = MathHelper.Clamp(_zoom, 0.05f, 50f);
            }

            // R key → reset
            if (keyboard.IsKeyDown(Keys.R) && _prevKeyboard.IsKeyUp(Keys.R))
                ResetView();

            _prevMouse = mouse;
            _prevKeyboard = keyboard;
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle contentBounds)
        {
            _bounds = contentBounds;
            if (_lineVerts.Length == 0) return;

            var gd = spriteBatch.GraphicsDevice;

            // End the caller's SpriteBatch so we can use 3D
            spriteBatch.End();

            // Protect state
            var oldViewport = gd.Viewport;
            var oldRasterizer = gd.RasterizerState;
            var oldDepth = gd.DepthStencilState;
            var oldBlend = gd.BlendState;

            // Set viewport to our content bounds
            gd.Viewport = new Viewport(_bounds);
            gd.DepthStencilState = DepthStencilState.Default;
            gd.BlendState = BlendState.Opaque;
            gd.RasterizerState = RasterizerState.CullNone;

            // Clear this viewport area
            gd.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);

            // Set up BasicEffect matrices
            var rot = Matrix.CreateRotationY(_yaw) * Matrix.CreateRotationX(_pitch);
            var panOffset = new Vector3(_panX, -_panY, 0f);

            _basicEffect.World = Matrix.CreateTranslation(
                -_sceneCenter.X, -_sceneCenter.Y, -_sceneCenter.Z)
                * Matrix.CreateScale(_sceneScale)
                * rot;

            _basicEffect.View = Matrix.CreateLookAt(
                panOffset + new Vector3(0, 0, -600f / _zoom),
                panOffset,
                Vector3.Up);

            float fovAngle = MathHelper.Clamp(MathHelper.ToRadians(60f), 0.01f, MathHelper.Pi - 0.01f);
            float aspect = _bounds.Width > 0 && _bounds.Height > 0
                ? (float)_bounds.Width / _bounds.Height
                : 1f;
            _basicEffect.Projection = Matrix.CreatePerspectiveFieldOfView(
                fovAngle, aspect, 0.1f, 200000f);

            var verts = _colorizeByType ? _lineVertsColorized : _lineVerts;

            foreach (var pass in _basicEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.LineList, verts, 0, verts.Length / 2);
            }

            // Restore state
            gd.Viewport = oldViewport;
            gd.RasterizerState = oldRasterizer;
            gd.DepthStencilState = oldDepth;
            gd.BlendState = oldBlend;

            // Restart SpriteBatch for the caller
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }

        public void Dispose()
        {
            _basicEffect?.Dispose();
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void ResetView()
        {
            _yaw = 0.4f; _pitch = 0.3f; _zoom = 1f; _panX = 0f; _panY = 0f;
        }

        private void ComputeSceneTransform(StgTriangle[] tris)
        {
            if (tris.Length == 0) { _sceneScale = 1f; _sceneCenter = default; return; }

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            foreach (var t in tris)
            {
                foreach (var v in new[] { t.V0, t.V1, t.V2 })
                {
                    if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                    if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
                    if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
                }
            }

            _sceneCenter = new Vec3((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f);
            float extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            _sceneScale = extent > 0 ? 400f / extent : 1f;
        }

        private void BuildLineGeometry(StgTriangle[] tris)
        {
            // Default color: cyan-blue wireframe
            var defaultColor = new Color(60, 160, 240);

            var defaultLines = new List<VertexPositionColor>(tris.Length * 6);
            var colorLines = new List<VertexPositionColor>(tris.Length * 6);

            foreach (var tri in tris)
            {
                var p0 = new Vector3(tri.V0.X, tri.V0.Y, tri.V0.Z);
                var p1 = new Vector3(tri.V1.X, tri.V1.Y, tri.V1.Z);
                var p2 = new Vector3(tri.V2.X, tri.V2.Y, tri.V2.Z);

                Color avgColor = tri.AverageColor;
                // Boost brightness for colorized mode
                avgColor = new Color(
                    Math.Min(255, avgColor.R * 2 + 40),
                    Math.Min(255, avgColor.G * 2 + 40),
                    Math.Min(255, avgColor.B * 2 + 40));

                // Default (all cyan-blue)
                defaultLines.Add(new VertexPositionColor(p0, defaultColor));
                defaultLines.Add(new VertexPositionColor(p1, defaultColor));
                defaultLines.Add(new VertexPositionColor(p1, defaultColor));
                defaultLines.Add(new VertexPositionColor(p2, defaultColor));
                defaultLines.Add(new VertexPositionColor(p2, defaultColor));
                defaultLines.Add(new VertexPositionColor(p0, defaultColor));

                // Colorized (by vertex color)
                colorLines.Add(new VertexPositionColor(p0, tri.C0));
                colorLines.Add(new VertexPositionColor(p1, tri.C1));
                colorLines.Add(new VertexPositionColor(p1, tri.C1));
                colorLines.Add(new VertexPositionColor(p2, tri.C2));
                colorLines.Add(new VertexPositionColor(p2, tri.C2));
                colorLines.Add(new VertexPositionColor(p0, tri.C0));
            }

            _lineVerts = defaultLines.ToArray();
            _lineVertsColorized = colorLines.ToArray();
        }
    }
}
