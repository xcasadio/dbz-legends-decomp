using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DbzLegendsAnalyser
{
    /// <summary>
    /// Interface for all file viewer panels.
    /// Each concrete viewer loads a specific PSX file format and provides
    /// Update/Draw callbacks for the Game1 render loop.
    /// </summary>
    public interface IAnalyserView : IDisposable
    {
        /// <summary>Load and decode the file at <paramref name="filePath"/>.</summary>
        void Initialize(string filePath, GraphicsDevice graphicsDevice);

        /// <summary>Process input (pan, zoom, rotation, etc.).</summary>
        void Update(GameTime gameTime, Rectangle contentBounds);

        /// <summary>Render the viewer content into <paramref name="contentBounds"/>.</summary>
        void Draw(SpriteBatch spriteBatch, Rectangle contentBounds);

        /// <summary>Get the list items to display in the left-hand offset/section list.</summary>
        string[] GetListItems();

        /// <summary>Called when the user selects an item in the offset/section list.</summary>
        void OnItemSelected(int index);
    }
}
