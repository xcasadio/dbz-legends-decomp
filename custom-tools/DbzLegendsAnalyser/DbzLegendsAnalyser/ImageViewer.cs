using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace DbzLegendsAnalyser
{
    /// <summary>
    /// Renders a <see cref="Texture2D"/> with pan (left-drag) and zoom (scroll wheel, +/- keys).
    /// Zoom levels: 0.5x, 1x, 2x, 4x — nearest-neighbor sampling, cursor-stable zoom.
    /// </summary>
    public class ImageViewer
    {
        private static readonly float[] ZoomLevels = { 0.5f, 1f, 2f, 4f };

        private Texture2D _texture;
        private int _zoomIndex = 1; // default 1x
        private Vector2 _translation;
        private Rectangle _bounds; // screen-space area where this viewer renders

        // Drag state
        private bool _dragging;
        private Point _dragStart;
        private Vector2 _translationStart;

        // Input tracking
        private MouseState _prevMouse;
        private KeyboardState _prevKeyboard;

        /// <summary>Gets or sets the texture to display. Setting resets the view.</summary>
        public Texture2D Texture
        {
            get => _texture;
            set
            {
                _texture = value;
                ResetView();
            }
        }

        /// <summary>Current zoom factor.</summary>
        public float Zoom => ZoomLevels[_zoomIndex];

        /// <summary>Screen-space bounds for this viewer. Set from the layout each frame.</summary>
        public Rectangle Bounds
        {
            get => _bounds;
            set
            {
                if (_bounds != value)
                {
                    _bounds = value;
                    // Don't reset view on every resize — just clamp
                    ClampTranslation();
                }
            }
        }

        /// <summary>Centers the view on the image at reset zoom (1x).</summary>
        public void ResetView()
        {
            _zoomIndex = 1;

            if (_texture == null)
            {
                _translation = Vector2.Zero;
                return;
            }

            float z = Zoom;
            float w = _texture.Width * z;
            float h = _texture.Height * z;
            _translation.X = (_bounds.Width - w) / 2f;
            _translation.Y = (_bounds.Height - h) / 2f;
            ClampTranslation();
        }

        /// <summary>Process input for pan and zoom. Call from Game1.Update.</summary>
        public void Update(GameTime gameTime)
        {
            var mouse = Mouse.GetState();
            var keyboard = Keyboard.GetState();

            // Only handle input when the mouse is inside our bounds
            bool mouseInBounds = _bounds.Contains(mouse.Position);

            // ── Drag (pan) ──
            if (mouseInBounds && mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
            {
                _dragging = true;
                _dragStart = mouse.Position;
                _translationStart = _translation;
            }

            if (_dragging)
            {
                if (mouse.LeftButton == ButtonState.Pressed)
                {
                    float dx = mouse.X - _dragStart.X;
                    float dy = mouse.Y - _dragStart.Y;
                    _translation = new Vector2(_translationStart.X + dx, _translationStart.Y + dy);
                    ClampTranslation();
                }
                else
                {
                    _dragging = false;
                }
            }

            // ── Zoom (scroll wheel) ──
            if (mouseInBounds && _texture != null)
            {
                int scrollDelta = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
                if (scrollDelta > 0)
                    SetZoomIndex(_zoomIndex + 1, mouse.Position);
                else if (scrollDelta < 0)
                    SetZoomIndex(_zoomIndex - 1, mouse.Position);
            }

            // ── Zoom (keyboard +/-) ──
            if (_texture != null)
            {
                var center = new Point(_bounds.X + _bounds.Width / 2, _bounds.Y + _bounds.Height / 2);

                if (IsKeyPressed(keyboard, Keys.OemPlus) || IsKeyPressed(keyboard, Keys.Add))
                    SetZoomIndex(_zoomIndex + 1, center);
                if (IsKeyPressed(keyboard, Keys.OemMinus) || IsKeyPressed(keyboard, Keys.Subtract))
                    SetZoomIndex(_zoomIndex - 1, center);
            }

            _prevMouse = mouse;
            _prevKeyboard = keyboard;
        }

        /// <summary>Draw the texture. Call from Game1.Draw with an active SpriteBatch (Begin already called).</summary>
        public void Draw(SpriteBatch spriteBatch)
        {
            if (_texture == null || _bounds.Width <= 0 || _bounds.Height <= 0)
                return;

            float z = Zoom;
            var destRect = new Rectangle(
                (int)(_bounds.X + _translation.X),
                (int)(_bounds.Y + _translation.Y),
                (int)(_texture.Width * z),
                (int)(_texture.Height * z));

            // Clip to bounds via scissor rectangle
            var oldScissor = spriteBatch.GraphicsDevice.ScissorRectangle;
            spriteBatch.End();

            var rasterizerState = new RasterizerState { ScissorTestEnable = true };
            spriteBatch.GraphicsDevice.ScissorRectangle = _bounds;
            spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp, // nearest-neighbor
                null,
                rasterizerState);

            spriteBatch.Draw(_texture, destRect, Color.White);

            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = oldScissor;

            // Restart the caller's batch
            spriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp);
        }

        private void SetZoomIndex(int index, Point focusPoint)
        {
            if (_texture == null) return;
            index = MathHelper.Clamp(index, 0, ZoomLevels.Length - 1);
            if (index == _zoomIndex) return;

            float oldZoom = Zoom;
            float newZoom = ZoomLevels[index];

            // Keep the point under the cursor stable while zooming
            float localX = focusPoint.X - _bounds.X;
            float localY = focusPoint.Y - _bounds.Y;
            float imageX = (localX - _translation.X) / oldZoom;
            float imageY = (localY - _translation.Y) / oldZoom;

            _zoomIndex = index;
            _translation.X = localX - imageX * newZoom;
            _translation.Y = localY - imageY * newZoom;

            ClampTranslation();
        }

        private void ClampTranslation()
        {
            if (_texture == null || _bounds.Width <= 0) return;

            float z = Zoom;
            float imgW = _texture.Width * z;
            float imgH = _texture.Height * z;

            if (imgW <= _bounds.Width)
                _translation.X = (_bounds.Width - imgW) / 2f;
            else
                _translation.X = MathHelper.Clamp(_translation.X, _bounds.Width - imgW, 0f);

            if (imgH <= _bounds.Height)
                _translation.Y = (_bounds.Height - imgH) / 2f;
            else
                _translation.Y = MathHelper.Clamp(_translation.Y, _bounds.Height - imgH, 0f);
        }

        private bool IsKeyPressed(KeyboardState current, Keys key)
        {
            return current.IsKeyDown(key) && _prevKeyboard.IsKeyUp(key);
        }
    }
}
