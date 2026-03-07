using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace DbzLegendsAnalyser
{
    /// <summary>
    /// Base class for image-based viewers that follow the pattern:
    /// decode file → dictionary of named images → ListBox selects → ImageViewer displays.
    /// Subclasses override <see cref="LoadImages"/> to populate <see cref="Images"/>.
    /// </summary>
    public abstract class ImageAnalyserView : IAnalyserView
    {
        protected GraphicsDevice GraphicsDevice { get; private set; }
        protected ImageViewer Viewer { get; } = new ImageViewer();

        /// <summary>Ordered list of (label, texture) pairs loaded from the file.</summary>
        protected List<(string Label, Texture2D Texture)> Images { get; } = new();

        private int _selectedIndex = -1;

        public void Initialize(string filePath, GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;
            LoadImages(filePath);

            if (Images.Count > 0)
            {
                _selectedIndex = 0;
                Viewer.Texture = Images[0].Texture;
            }
        }

        /// <summary>Override to load and decode the file, populating <see cref="Images"/>.</summary>
        protected abstract void LoadImages(string filePath);

        public string[] GetListItems()
        {
            var items = new string[Images.Count];
            for (int i = 0; i < Images.Count; i++)
                items[i] = Images[i].Label;
            return items;
        }

        public void OnItemSelected(int index)
        {
            if (index < 0 || index >= Images.Count) return;
            _selectedIndex = index;
            Viewer.Texture = Images[index].Texture;
        }

        public void Update(GameTime gameTime, Rectangle contentBounds)
        {
            Viewer.Bounds = contentBounds;
            Viewer.Update(gameTime);
        }

        public void Draw(SpriteBatch spriteBatch, Rectangle contentBounds)
        {
            Viewer.Bounds = contentBounds;
            Viewer.Draw(spriteBatch);
        }

        public virtual void Dispose()
        {
            foreach (var (_, tex) in Images)
                tex?.Dispose();
            Images.Clear();
        }
    }
}
