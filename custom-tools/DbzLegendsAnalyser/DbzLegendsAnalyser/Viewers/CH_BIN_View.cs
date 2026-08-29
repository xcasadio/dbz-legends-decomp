#pragma warning disable CS8632 // nullable annotation without #nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PsxTools;
using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;

namespace DbzLegendsAnalyser.Viewers
{
    public class CH_BIN_View : IAnalyserView
    {
        private const int PageWidth = 1440;
        private const int PageMaxHeight = 4096;
        private const float AnimationFrameStep = 1f / 60f;

        private enum PageKind
        {
            Summary,
            Materials,
            Composite,
            Model,
        }

        private enum DisplayMode
        {
            Wireframe,
            Solid,
            TexturedRaw,
            TexturedTinted,
        }

        private readonly ImageViewer _imageViewer = new ImageViewer();
        private readonly List<PageDescriptor> _pages = new List<PageDescriptor>();
        private readonly Dictionary<ChBinMaterialKey, CachedTexture> _materialTextures = new Dictionary<ChBinMaterialKey, CachedTexture>();

        private GraphicsDevice _graphicsDevice;
        private ChBinFile? _file;
        private ChBinVisualDocument? _document;
        private Texture2D? _currentImage;
        private BasicEffect? _solidEffect;
        private BasicEffect? _texturedEffect;
        private BasicEffect? _texturedTintedEffect;
        private PageDescriptor? _currentPage;
        private Rectangle _bounds;
        private int _currentPageIndex = -1;
        private int _cachedTextureVersion = -1;
        private float _animationAccumulator;
        private Vector3 _compositeSceneCenter = Vector3.Zero;
        private float _compositeSceneScale = 1f;

        private DisplayMode _displayMode = DisplayMode.TexturedRaw;
        private Vector3 _target = Vector3.Zero;
        private float _azimuth = 0.4f;
        private float _elevation = 0.25f;
        private float _distance = 420f;
        private MouseState _prevMouse;
        private KeyboardState _prevKeyboard;

        public void Initialize(string filePath, GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
            _solidEffect = new BasicEffect(graphicsDevice)
            {
                VertexColorEnabled = true,
                TextureEnabled = false,
                LightingEnabled = false,
            };
            _texturedEffect = new BasicEffect(graphicsDevice)
            {
                VertexColorEnabled = false,
                TextureEnabled = true,
                LightingEnabled = false,
            };
            _texturedTintedEffect = new BasicEffect(graphicsDevice)
            {
                VertexColorEnabled = true,
                TextureEnabled = true,
                LightingEnabled = false,
            };

            _file = ChBinLoader.Load(filePath);
            _document = ChBinVisuals.Build(_file);
            BuildPages();

            if (_pages.Count > 0)
                SelectPage(0);
        }

        public void Update(GameTime gameTime, Rectangle contentBounds)
        {
            _bounds = contentBounds;

            if (_document is not null && _document.HasAnimations)
            {
                _animationAccumulator += (float)gameTime.ElapsedGameTime.TotalSeconds;
                while (_animationAccumulator >= AnimationFrameStep)
                {
                    _animationAccumulator -= AnimationFrameStep;
                    if (_document.AdvanceFrame(out bool textureDirty))
                    {
                        if (textureDirty)
                        {
                            ClearMaterialTextureCache();
                        }

                        if (textureDirty && _currentPage?.Kind == PageKind.Materials)
                            RefreshCurrentImagePage();
                    }
                }
            }

            if (_currentPage?.Kind is PageKind.Model or PageKind.Composite)
            {
                UpdateModelCamera(gameTime);
                return;
            }

            _imageViewer.Bounds = contentBounds;
            _imageViewer.Update(gameTime);
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle contentBounds)
        {
            _bounds = contentBounds;

            if (_currentPage?.Kind == PageKind.Composite && _document is not null)
            {
                DrawComposite(spriteBatch, _document.Models);
                return;
            }

            if (_currentPage?.Kind == PageKind.Model && _document is not null && _currentPage.ModelIndex >= 0)
            {
                DrawModel(spriteBatch, _document.Models[_currentPage.ModelIndex]);
                return;
            }

            _imageViewer.Bounds = contentBounds;
            _imageViewer.Draw(spriteBatch);
        }

        public string[] GetListItems()
            => _pages.Select(static page => page.Label).ToArray();

        public void OnItemSelected(int index)
        {
            if (index < 0 || index >= _pages.Count || index == _currentPageIndex)
                return;

            SelectPage(index);
        }

        public void Dispose()
        {
            _currentImage?.Dispose();
            _currentImage = null;

            foreach (CachedTexture cache in _materialTextures.Values)
                cache.Texture.Dispose();
            _materialTextures.Clear();

            _solidEffect?.Dispose();
            _texturedEffect?.Dispose();
            _texturedTintedEffect?.Dispose();
        }

        private void BuildPages()
        {
            _pages.Clear();
            _pages.Add(new PageDescriptor(PageKind.Summary, "Summary"));

            if (_document is null)
                return;

            RefreshCompositeSceneMetrics();

            if (_document.MaterialKeys.Count > 0)
                _pages.Add(new PageDescriptor(PageKind.Materials, "Textures"));

            if (_document.Models.Count > 0)
                _pages.Add(new PageDescriptor(PageKind.Composite, "Animation"));

            for (int modelIndex = 0; modelIndex < _document.Models.Count; modelIndex++)
            {
                ChBinRenderableModel model = _document.Models[modelIndex];
                string label = model.HasAnimation ? $"{model.Label} anim" : model.Label;
                _pages.Add(new PageDescriptor(PageKind.Model, label, modelIndex));
            }
        }

        private void SelectPage(int index)
        {
            _currentPageIndex = index;
            _currentPage = _pages[index];

            if (_currentPage.Kind is PageKind.Model or PageKind.Composite)
            {
                _currentImage?.Dispose();
                _currentImage = null;
                _imageViewer.Texture = null;
                ResetModelView();
                ChooseDefaultDisplayMode();
                return;
            }

            RefreshCurrentImagePage();
        }

        private void RefreshCurrentImagePage()
        {
            if (_currentPage is null)
                return;

            Texture2D image = _currentPage.Kind switch
            {
                PageKind.Summary => RenderTextPage("CH_BIN Visual Summary", BuildSummaryLines()),
                PageKind.Materials => RenderMaterialGalleryPage(),
                _ => RenderTextPage("Unavailable", new[] { "No renderer available." }),
            };

            Texture2D? previous = _currentImage;
            _currentImage = image;
            _imageViewer.Texture = image;
            previous?.Dispose();
        }

        private IEnumerable<string> BuildSummaryLines()
        {
            if (_file is null || _document is null)
            {
                yield return "Viewer not initialised.";
                yield break;
            }

            yield return "## File";
            yield return $"name                 : {_file.SourceName}";
            yield return $"renderable entries    : {_document.Models.Count}";
            yield return $"texture pages         : {_document.MaterialKeys.Count}";
            yield return $"anim streams          : {(_document.HasAnimations ? "yes" : "no")}";
            yield return string.Empty;

            yield return "## Final Output Focus";
            yield return "- extracted texture pages reconstructed from CH_BIN texture commands";
            yield return "- 3D renderable entries built from the proven 6-byte coordinate table";
            yield return "- VRAM / CLUT plus proven transform and uv0123 replay from previewed AnimStream batches";
            yield return "- composite Animation page draws the assembled renderable set in shared space";
            yield return string.Empty;

            yield return "## Controls";
            yield return "- image pages: left-drag pan, wheel zoom";
            yield return "- model/animation pages: left-drag orbit, wheel zoom, WASD/arrows move target, R reset";
            yield return "- display mode: 1 wireframe, 2 solid, 3 raw texture, 4 texture x vertex color";
            yield return string.Empty;

            yield return "## Proven / Probable";
            yield return "- CERTAIN: meshSegment+0x04 indexes the projected coordinate table (3x int16)";
            yield return "- CERTAIN: lightingSegment payload defines UV rectangles per polygon";
            yield return "- CERTAIN: load_set and tex_set mutate visible VRAM / CLUT state";
            yield return "- CERTAIN: 0x06/0x07/0x08 update body-part transform slots and 0x09 consumes them by group_id";
            yield return "- PROBABLE: colorTable.word1 = CBA and word2 = TPAGE for textured binding";
            yield return string.Empty;

            yield return "## Limits";
            yield return "- palette sidecars are optional fallbacks when CH_BIN-local VRAM lacks a visible CLUT row";
            yield return "- per-polygon tpclut_set retargeting is not fully replayed yet";
            yield return "- textured mode falls back to solid when a material page stays blank";
            yield return "- animation replay currently uses the previewed AnimStream batches only";
        }

        private Texture2D RenderMaterialGalleryPage()
        {
            if (_document is null)
                return RenderTextPage("Textures", new[] { "No document available." });

            const int cellWidth = 320;
            const int cellHeight = 236;
            const int columns = 3;

            int count = Math.Max(1, _document.MaterialKeys.Count);
            int rows = (count + columns - 1) / columns;
            int canvasWidth = columns * cellWidth + 48;
            int canvasHeight = Math.Min(PageMaxHeight, rows * cellHeight + 88);

            using var bitmap = new SD.Bitmap(canvasWidth, canvasHeight, SDI.PixelFormat.Format32bppArgb);
            using var graphics = SD.Graphics.FromImage(bitmap);
            using var titleFont = new SD.Font("Segoe UI", 22, SD.FontStyle.Bold, SD.GraphicsUnit.Pixel);
            using var monoFont = new SD.Font("Consolas", 13, SD.FontStyle.Regular, SD.GraphicsUnit.Pixel);
            using var backgroundBrush = new SD.SolidBrush(SD.Color.FromArgb(14, 18, 24));
            using var cardBrush = new SD.SolidBrush(SD.Color.FromArgb(28, 33, 40));
            using var borderPen = new SD.Pen(SD.Color.FromArgb(68, 78, 90));
            using var textBrush = new SD.SolidBrush(SD.Color.FromArgb(232, 235, 239));
            using var mutedBrush = new SD.SolidBrush(SD.Color.FromArgb(162, 170, 180));
            using var accentBrush = new SD.SolidBrush(SD.Color.FromArgb(214, 162, 89));

            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(SD.Color.FromArgb(14, 18, 24));
            graphics.FillRectangle(backgroundBrush, 0, 0, bitmap.Width, bitmap.Height);
            graphics.FillRectangle(accentBrush, 18, 18, bitmap.Width - 36, 10);
            graphics.DrawString("CH_BIN Texture Pages", titleFont, textBrush, 24, 38);

            Dictionary<ChBinMaterialKey, int> usageCounts = BuildMaterialUsageCounts();

            foreach ((ChBinMaterialKey key, int index) in _document.MaterialKeys.Select((key, index) => (key, index)))
            {
                ChBinTexturePage page = _document.BuildTexturePage(key);
                int usageCount = usageCounts.TryGetValue(key, out int countUsed) ? countUsed : 0;
                int column = index % columns;
                int row = index / columns;
                int x = 24 + column * cellWidth;
                int y = 86 + row * cellHeight;

                graphics.FillRectangle(cardBrush, x, y, cellWidth - 16, cellHeight - 16);
                graphics.DrawRectangle(borderPen, x, y, cellWidth - 17, cellHeight - 17);
                graphics.DrawString(key.Label, monoFont, textBrush, x + 12, y + 10);
                string pageInfo = page.HasVisiblePixels ? $"{page.Width}x{page.Height}" : "no visible pixels";
                graphics.DrawString($"{pageInfo} | used by {usageCount} prims", monoFont, mutedBrush, x + 12, y + 30);

                using SD.Bitmap pageBitmap = CreateBitmap(page);
                SD.Rectangle target = FitInside(page.Width, page.Height, new SD.Rectangle(x + 12, y + 56, cellWidth - 40, cellHeight - 88));
                graphics.DrawImage(pageBitmap, target);
            }

            _cachedTextureVersion = _document.TextureVersion;
            return BitmapToTexture(bitmap);
        }

        private Dictionary<ChBinMaterialKey, int> BuildMaterialUsageCounts()
        {
            var counts = new Dictionary<ChBinMaterialKey, int>();
            if (_document is null)
                return counts;

            foreach (ChBinRenderableModel model in _document.Models)
            {
                foreach (ChBinTexturedPrimitive primitive in model.TexturedPrimitives)
                {
                    ChBinMaterialKey key = _document.GetAnimatedMaterialKey(primitive.GlobalPrimitiveIndex, primitive.BaseMaterialKey);
                    counts.TryGetValue(key, out int currentCount);
                    counts[key] = currentCount + 1;
                }
            }

            return counts;
        }

        private void UpdateModelCamera(GameTime gameTime)
        {
            MouseState mouse = Mouse.GetState();
            KeyboardState keyboard = Keyboard.GetState();
            bool inBounds = _bounds.Contains(mouse.Position);
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            bool leftHeld = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Pressed;
            int deltaX = mouse.X - _prevMouse.X;
            int deltaY = mouse.Y - _prevMouse.Y;

            if (leftHeld && inBounds)
            {
                _azimuth += deltaX * 0.008f;
                _elevation -= deltaY * 0.008f;
                _elevation = MathHelper.Clamp(_elevation, -MathHelper.PiOver2 + 0.02f, MathHelper.PiOver2 - 0.02f);
            }

            if (inBounds)
            {
                int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
                if (scroll > 0)
                    _distance *= 0.88f;
                else if (scroll < 0)
                    _distance *= 1.12f;
                _distance = MathHelper.Clamp(_distance, 12f, 500000f);
            }

            Vector3 forward = ComputeForward();
            Vector3 forwardXZ = new Vector3(forward.X, 0f, forward.Z);
            if (forwardXZ.LengthSquared() > 0.0001f)
                forwardXZ.Normalize();
            else
                forwardXZ = Vector3.Forward;

            Vector3 right = Vector3.Normalize(Vector3.Cross(forwardXZ, Vector3.Up));
            float speed = _distance * dt * 0.35f;

            if (keyboard.IsKeyDown(Keys.Up) || keyboard.IsKeyDown(Keys.W))
                _target += forwardXZ * speed;
            if (keyboard.IsKeyDown(Keys.Down) || keyboard.IsKeyDown(Keys.S))
                _target -= forwardXZ * speed;
            if (keyboard.IsKeyDown(Keys.Left) || keyboard.IsKeyDown(Keys.A))
                _target -= right * speed;
            if (keyboard.IsKeyDown(Keys.Right) || keyboard.IsKeyDown(Keys.D))
                _target += right * speed;

            if (keyboard.IsKeyDown(Keys.R) && _prevKeyboard.IsKeyUp(Keys.R))
                ResetModelView();
            if (keyboard.IsKeyDown(Keys.D1) && _prevKeyboard.IsKeyUp(Keys.D1))
                _displayMode = DisplayMode.Wireframe;
            if (keyboard.IsKeyDown(Keys.D2) && _prevKeyboard.IsKeyUp(Keys.D2))
                _displayMode = DisplayMode.Solid;
            if (keyboard.IsKeyDown(Keys.D3) && _prevKeyboard.IsKeyUp(Keys.D3))
                _displayMode = DisplayMode.TexturedRaw;
            if (keyboard.IsKeyDown(Keys.D4) && _prevKeyboard.IsKeyUp(Keys.D4))
                _displayMode = DisplayMode.TexturedTinted;

            _prevMouse = mouse;
            _prevKeyboard = keyboard;
        }

        private void DrawModel(SpriteBatch spriteBatch, ChBinRenderableModel model)
        {
            if (_solidEffect is null || _texturedEffect is null || _texturedTintedEffect is null || _document is null)
                return;

            GraphicsDevice gd = spriteBatch.GraphicsDevice;
            spriteBatch.End();

            (Viewport oldViewport, RasterizerState? oldRasterizer, DepthStencilState oldDepth, BlendState oldBlend) = Begin3DPass(gd);
            DrawRenderableModel(gd, model, useCompositeScene: false);
            End3DPass(gd, oldViewport, oldRasterizer, oldDepth, oldBlend);

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }

        private void DrawComposite(SpriteBatch spriteBatch, IReadOnlyList<ChBinRenderableModel> models)
        {
            if (_solidEffect is null || _texturedEffect is null || _texturedTintedEffect is null || _document is null)
                return;

            GraphicsDevice gd = spriteBatch.GraphicsDevice;
            spriteBatch.End();

            (Viewport oldViewport, RasterizerState? oldRasterizer, DepthStencilState oldDepth, BlendState oldBlend) = Begin3DPass(gd);
            foreach (ChBinRenderableModel model in models)
                DrawRenderableModel(gd, model, useCompositeScene: true);
            End3DPass(gd, oldViewport, oldRasterizer, oldDepth, oldBlend);

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }

        private (Viewport OldViewport, RasterizerState? OldRasterizer, DepthStencilState OldDepth, BlendState OldBlend) Begin3DPass(GraphicsDevice gd)
        {
            Viewport oldViewport = gd.Viewport;
            RasterizerState? oldRasterizer = gd.RasterizerState;
            DepthStencilState oldDepth = gd.DepthStencilState;
            BlendState oldBlend = gd.BlendState;

            gd.Viewport = new Viewport(_bounds);
            gd.DepthStencilState = DepthStencilState.Default;
            gd.BlendState = BlendState.Opaque;
            gd.RasterizerState = RasterizerState.CullNone;
            gd.Clear(ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);
            return (oldViewport, oldRasterizer, oldDepth, oldBlend);
        }

        private static void End3DPass(GraphicsDevice gd, Viewport oldViewport, RasterizerState? oldRasterizer, DepthStencilState oldDepth, BlendState oldBlend)
        {
            gd.Viewport = oldViewport;
            gd.RasterizerState = oldRasterizer;
            gd.DepthStencilState = oldDepth;
            gd.BlendState = oldBlend;
        }

        private void DrawRenderableModel(GraphicsDevice gd, ChBinRenderableModel model, bool useCompositeScene)
        {
            ApplyMatrices(_solidEffect!, model, useCompositeScene);
            ApplyMatrices(_texturedEffect!, model, useCompositeScene);
            ApplyMatrices(_texturedTintedEffect!, model, useCompositeScene);

            if (_displayMode == DisplayMode.Wireframe)
            {
                foreach (EffectPass pass in _solidEffect!.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    if (model.WireVertices.Length >= 2)
                        gd.DrawUserPrimitives(PrimitiveType.LineList, model.WireVertices, 0, model.WireVertices.Length / 2);
                }

                return;
            }

            if (_displayMode == DisplayMode.Solid)
            {
                foreach (EffectPass pass in _solidEffect!.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    if (model.SolidVertices.Length >= 3)
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList, model.SolidVertices, 0, model.SolidVertices.Length / 3);
                }

                return;
            }

            bool drewTextured = false;
            gd.SamplerStates[0] = SamplerState.PointClamp;
            BasicEffect texturedEffect = _displayMode == DisplayMode.TexturedTinted
                ? _texturedTintedEffect!
                : _texturedEffect!;
            foreach (ChBinTexturedPrimitive primitive in model.TexturedPrimitives)
            {
                ChBinMaterialKey materialKey = _document!.GetAnimatedMaterialKey(primitive.GlobalPrimitiveIndex, primitive.BaseMaterialKey);
                ChBinUvRect uvRect = _document.GetAnimatedUvRect(primitive.GlobalPrimitiveIndex, primitive.BaseUvRect);
                Texture2D? texture = GetOrCreateMaterialTexture(materialKey);
                if (texture is null)
                    continue;

                drewTextured = true;
                texturedEffect.Texture = texture;
                VertexPositionColorTexture[] vertices = _document.TryGetAnimatedQuadOverride(primitive.GlobalPrimitiveIndex, out ChBinQuadVertexOverride quadOverride)
                    ? primitive.GetVertices(materialKey, uvRect, quadOverride)
                    : primitive.GetVertices(materialKey, uvRect);
                foreach (EffectPass pass in texturedEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    if (vertices.Length >= 3)
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
                }
            }

            if (!drewTextured)
            {
                foreach (EffectPass pass in _solidEffect!.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    if (model.SolidVertices.Length >= 3)
                        gd.DrawUserPrimitives(PrimitiveType.TriangleList, model.SolidVertices, 0, model.SolidVertices.Length / 3);
                }
            }
        }

        private void ApplyMatrices(BasicEffect effect, ChBinRenderableModel model, bool useCompositeScene)
        {
            Matrix animationWorld = _document?.GetModelAnimationMatrix(model.EntryIndex) ?? Matrix.Identity;
            Vector3 sceneCenter = useCompositeScene ? _compositeSceneCenter : model.SceneCenter;
            float sceneScale = useCompositeScene ? _compositeSceneScale : model.SceneScale;
            effect.World =
                animationWorld
                * Matrix.CreateTranslation(-sceneCenter)
                * Matrix.CreateScale(sceneScale)
                * Matrix.CreateScale(1f, -1f, 1f);

            effect.View = Matrix.CreateLookAt(ComputeCameraPos(), _target, Vector3.Up);

            float aspect = _bounds.Width > 0 && _bounds.Height > 0
                ? (float)_bounds.Width / _bounds.Height
                : 1f;
            effect.Projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(60f), aspect, 0.1f, 500000f);
        }

        private Vector3 ComputeForward()
        {
            float cosElevation = (float)Math.Cos(_elevation);
            return new Vector3(
                (float)Math.Sin(_azimuth) * cosElevation,
                (float)Math.Sin(_elevation),
                -(float)Math.Cos(_azimuth) * cosElevation);
        }

        private Vector3 ComputeCameraPos() => _target - ComputeForward() * _distance;

        private void ResetModelView()
        {
            _target = Vector3.Zero;
            _azimuth = 0.4f;
            _elevation = 0.25f;
            _distance = 420f;

            if (_currentPage?.Kind == PageKind.Composite)
                _distance = 560f;
        }

        private void ChooseDefaultDisplayMode()
        {
            if (_document is null)
                return;

            if (_currentPage?.Kind == PageKind.Composite)
            {
                _displayMode = _document.Models.Any(static model => model.TexturedPrimitives.Count > 0)
                    ? DisplayMode.TexturedRaw
                    : DisplayMode.Solid;
                return;
            }

            if (_currentPage?.Kind != PageKind.Model || _currentPage.ModelIndex < 0)
                return;

            ChBinRenderableModel model = _document.Models[_currentPage.ModelIndex];
            _displayMode = model.TexturedPrimitives.Count > 0 ? DisplayMode.TexturedRaw : DisplayMode.Solid;
        }

        private void RefreshCompositeSceneMetrics()
        {
            _compositeSceneCenter = Vector3.Zero;
            _compositeSceneScale = 1f;
            if (_document is null || _document.Models.Count == 0)
                return;

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float minZ = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            float maxZ = float.MinValue;
            bool hasPoint = false;

            foreach (ChBinRenderableModel model in _document.Models)
            {
                IncludeBounds(model.WireVertices.Select(static vertex => vertex.Position), ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ, ref hasPoint);
                IncludeBounds(model.SolidVertices.Select(static vertex => vertex.Position), ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ, ref hasPoint);
                foreach (ChBinTexturedPrimitive primitive in model.TexturedPrimitives)
                    IncludeBounds(primitive.Vertices.Select(static vertex => vertex.Position), ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ, ref hasPoint);
            }

            if (!hasPoint)
                return;

            _compositeSceneCenter = new Vector3(
                (minX + maxX) * 0.5f,
                (minY + maxY) * 0.5f,
                (minZ + maxZ) * 0.5f);

            float extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            _compositeSceneScale = extent > 0.01f ? 400f / extent : 1f;
        }

        private static void IncludeBounds(IEnumerable<Vector3> points, ref float minX, ref float minY, ref float minZ, ref float maxX, ref float maxY, ref float maxZ, ref bool hasPoint)
        {
            foreach (Vector3 point in points)
            {
                hasPoint = true;
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
                maxZ = Math.Max(maxZ, point.Z);
            }
        }

        private void ClearMaterialTextureCache()
        {
            foreach (CachedTexture cache in _materialTextures.Values)
                cache.Texture.Dispose();
            _materialTextures.Clear();
            _cachedTextureVersion = -1;
        }

        private Texture2D? GetOrCreateMaterialTexture(ChBinMaterialKey key)
        {
            if (_document is null)
                return null;

            if (_materialTextures.TryGetValue(key, out CachedTexture? cache) && cache.Version == _document.TextureVersion)
                return cache.Texture;

            ChBinTexturePage page = _document.BuildTexturePage(key);
            if (!page.HasVisiblePixels)
                return null;

            Texture2D texture = new Texture2D(_graphicsDevice, page.Width, page.Height);
            texture.SetData(page.Pixels);

            if (_materialTextures.TryGetValue(key, out CachedTexture? previous))
                previous.Texture.Dispose();

            _materialTextures[key] = new CachedTexture(texture, _document.TextureVersion);
            return texture;
        }

        private Texture2D RenderTextPage(string title, IEnumerable<string> lines)
        {
            List<string> lineList = lines.ToList();

            using var measureBitmap = new SD.Bitmap(1, 1, SDI.PixelFormat.Format32bppArgb);
            using var measureGraphics = SD.Graphics.FromImage(measureBitmap);
            using var titleFont = new SD.Font("Segoe UI", 24, SD.FontStyle.Bold, SD.GraphicsUnit.Pixel);
            using var monoFont = new SD.Font("Consolas", 14, SD.FontStyle.Regular, SD.GraphicsUnit.Pixel);

            int lineHeight = (int)Math.Ceiling(monoFont.GetHeight(measureGraphics)) + 4;
            int titleHeight = (int)Math.Ceiling(titleFont.GetHeight(measureGraphics)) + 24;
            int contentTop = titleHeight + 28;
            int maxLines = Math.Max(1, (PageMaxHeight - contentTop - 32) / lineHeight);
            if (lineList.Count > maxLines)
            {
                lineList = lineList.Take(maxLines - 1)
                    .Concat(new[] { "...[page truncated to fit GPU texture limits]" })
                    .ToList();
            }

            int pageHeight = Math.Min(PageMaxHeight, contentTop + lineList.Count * lineHeight + 32);

            using var bitmap = new SD.Bitmap(PageWidth, pageHeight, SDI.PixelFormat.Format32bppArgb);
            using var graphics = SD.Graphics.FromImage(bitmap);
            using var backgroundBrush = new SD.SolidBrush(SD.Color.FromArgb(20, 24, 30));
            using var cardBrush = new SD.SolidBrush(SD.Color.FromArgb(30, 36, 44));
            using var accentBrush = new SD.SolidBrush(SD.Color.FromArgb(214, 162, 89));
            using var textBrush = new SD.SolidBrush(SD.Color.FromArgb(232, 234, 238));
            using var mutedBrush = new SD.SolidBrush(SD.Color.FromArgb(160, 168, 178));
            using var sectionBrush = new SD.SolidBrush(SD.Color.FromArgb(140, 194, 255));
            using var borderPen = new SD.Pen(SD.Color.FromArgb(60, 72, 84));

            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(SD.Color.FromArgb(14, 18, 24));
            graphics.FillRectangle(backgroundBrush, 0, 0, bitmap.Width, bitmap.Height);
            graphics.FillRectangle(cardBrush, 18, 18, bitmap.Width - 36, bitmap.Height - 36);
            graphics.DrawRectangle(borderPen, 18, 18, bitmap.Width - 37, bitmap.Height - 37);
            graphics.FillRectangle(accentBrush, 18, 18, bitmap.Width - 36, 10);
            graphics.DrawString(title, titleFont, textBrush, 34, 36);

            int y = contentTop;
            foreach (string line in lineList)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    graphics.DrawString(line[3..], monoFont, sectionBrush, 34, y);
                }
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    graphics.DrawString(line, monoFont, mutedBrush, 34, y);
                }
                else
                {
                    graphics.DrawString(line, monoFont, textBrush, 34, y);
                }

                y += lineHeight;
            }

            return BitmapToTexture(bitmap);
        }

        private Texture2D BitmapToTexture(SD.Bitmap bitmap)
        {
            SD.Rectangle bounds = new SD.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            SDI.BitmapData bitmapData = bitmap.LockBits(bounds, SDI.ImageLockMode.ReadOnly, SDI.PixelFormat.Format32bppArgb);

            try
            {
                byte[] raw = new byte[bitmapData.Stride * bitmap.Height];
                Marshal.Copy(bitmapData.Scan0, raw, 0, raw.Length);

                var colors = new Color[bitmap.Width * bitmap.Height];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int rowOffset = y * bitmapData.Stride;
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        int pixelOffset = rowOffset + x * 4;
                        colors[y * bitmap.Width + x] = new Color(
                            raw[pixelOffset + 2],
                            raw[pixelOffset + 1],
                            raw[pixelOffset + 0],
                            raw[pixelOffset + 3]);
                    }
                }

                Texture2D texture = new Texture2D(_graphicsDevice, bitmap.Width, bitmap.Height);
                texture.SetData(colors);
                return texture;
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        private static SD.Bitmap CreateBitmap(ChBinTexturePage page)
        {
            var bitmap = new SD.Bitmap(page.Width, page.Height, SDI.PixelFormat.Format32bppArgb);
            SD.Rectangle bounds = new SD.Rectangle(0, 0, page.Width, page.Height);
            SDI.BitmapData bitmapData = bitmap.LockBits(bounds, SDI.ImageLockMode.WriteOnly, SDI.PixelFormat.Format32bppArgb);

            try
            {
                byte[] raw = new byte[bitmapData.Stride * page.Height];
                for (int y = 0; y < page.Height; y++)
                {
                    int rowOffset = y * bitmapData.Stride;
                    for (int x = 0; x < page.Width; x++)
                    {
                        Color color = page.Pixels[y * page.Width + x];
                        int pixelOffset = rowOffset + x * 4;
                        raw[pixelOffset + 0] = color.B;
                        raw[pixelOffset + 1] = color.G;
                        raw[pixelOffset + 2] = color.R;
                        raw[pixelOffset + 3] = color.A;
                    }
                }

                Marshal.Copy(raw, 0, bitmapData.Scan0, raw.Length);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }

        private static SD.Rectangle FitInside(int width, int height, SD.Rectangle bounds)
        {
            float scale = Math.Min(bounds.Width / (float)Math.Max(1, width), bounds.Height / (float)Math.Max(1, height));
            int drawWidth = Math.Max(1, (int)(width * scale));
            int drawHeight = Math.Max(1, (int)(height * scale));
            int x = bounds.X + (bounds.Width - drawWidth) / 2;
            int y = bounds.Y + (bounds.Height - drawHeight) / 2;
            return new SD.Rectangle(x, y, drawWidth, drawHeight);
        }

        private sealed class CachedTexture
        {
            public CachedTexture(Texture2D texture, int version)
            {
                Texture = texture;
                Version = version;
            }

            public Texture2D Texture { get; }
            public int Version { get; }
        }

        private sealed class PageDescriptor
        {
            public PageDescriptor(PageKind kind, string label, int modelIndex = -1)
            {
                Kind = kind;
                Label = label;
                ModelIndex = modelIndex;
            }

            public PageKind Kind { get; }
            public string Label { get; }
            public int ModelIndex { get; }
        }
    }
}