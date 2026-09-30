namespace Performance
{
    /// <summary>
    /// The one sprite image both apps draw, generated in code so neither app needs a content pipeline.
    /// </summary>
    public static class SpriteTexture
    {
        public const int Size = 64;

        /// <summary>
        /// RGBA8 pixels of a soft-edged disc with a lighter ring, premultiplied so SpriteBatch's
        /// AlphaBlend and Skia's Premul image blend identically.
        /// </summary>
        public static byte[] BuildPixels()
        {
            var pixels = new byte[Size * Size * 4];
            float center = (Size - 1) / 2f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float d = MathF.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / center;
                    float alpha = Math.Clamp((1f - d) * 8f, 0f, 1f);
                    float shade = d > 0.7f ? 1f : 0.55f;
                    int o = (y * Size + x) * 4;
                    pixels[o + 0] = (byte)(90 * shade * alpha);
                    pixels[o + 1] = (byte)(170 * shade * alpha);
                    pixels[o + 2] = (byte)(255 * shade * alpha);
                    pixels[o + 3] = (byte)(255 * alpha);
                }
            }
            return pixels;
        }
    }
}
