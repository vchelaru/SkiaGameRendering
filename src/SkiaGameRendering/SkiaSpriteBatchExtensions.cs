using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SkiaGameRendering
{
    /// <summary>
    /// Lets a <see cref="SkiaRenderTarget2D"/> be passed directly to <see cref="SpriteBatch.Draw"/>,
    /// mirroring its overload set, without making <see cref="SkiaRenderTarget2D"/> a
    /// <see cref="Texture2D"/> subclass (that would let callers call
    /// <see cref="GraphicsDevice.SetRenderTarget(RenderTarget2D)"/> on it directly, which would
    /// fight with its own Begin/End GPU-context management for the same texture).
    /// </summary>
    public static class SkiaSpriteBatchExtensions
    {
        public static void Draw(this SpriteBatch spriteBatch, SkiaRenderTarget2D target, Vector2 position, Color color) =>
            spriteBatch.Draw(target.Texture, position, color);

        public static void Draw(this SpriteBatch spriteBatch, SkiaRenderTarget2D target, Vector2 position,
            Rectangle? sourceRectangle, Color color) =>
            spriteBatch.Draw(target.Texture, position, sourceRectangle, color);

        public static void Draw(this SpriteBatch spriteBatch, SkiaRenderTarget2D target, Vector2 position,
            Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, float scale,
            SpriteEffects effects, float layerDepth) =>
            spriteBatch.Draw(target.Texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);

        public static void Draw(this SpriteBatch spriteBatch, SkiaRenderTarget2D target, Vector2 position,
            Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, Vector2 scale,
            SpriteEffects effects, float layerDepth) =>
            spriteBatch.Draw(target.Texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);

        public static void Draw(this SpriteBatch spriteBatch, SkiaRenderTarget2D target,
            Rectangle destinationRectangle, Color color) =>
            spriteBatch.Draw(target.Texture, destinationRectangle, color);

        public static void Draw(this SpriteBatch spriteBatch, SkiaRenderTarget2D target,
            Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color) =>
            spriteBatch.Draw(target.Texture, destinationRectangle, sourceRectangle, color);

        public static void Draw(this SpriteBatch spriteBatch, SkiaRenderTarget2D target,
            Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color, float rotation,
            Vector2 origin, SpriteEffects effects, float layerDepth) =>
            spriteBatch.Draw(target.Texture, destinationRectangle, sourceRectangle, color, rotation, origin, effects, layerDepth);
    }
}
