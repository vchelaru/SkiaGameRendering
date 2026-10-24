using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StbTrueTypeSharp;

namespace Performance
{
    /// <summary>
    /// Bakes a <see cref="SpriteFont"/> at one pixel size from a TTF at run time. It is the same
    /// glyph-atlas <see cref="SpriteFont"/> a game gets from the content pipeline (fixed size, drawn by
    /// <see cref="SpriteBatch"/>), built without the pipeline so every platform, including the browser, can use it.
    /// </summary>
    public static unsafe class SpriteFontBaker
    {
        private const int FirstChar = 32;
        private const int LastChar = 126;

        public static SpriteFont Bake(GraphicsDevice device, byte[] ttf, int pixelSize)
        {
            var font = new StbTrueType.stbtt_fontinfo();
            fixed (byte* data = ttf)
            {
                if (StbTrueType.stbtt_InitFont(font, data, 0) == 0)
                    throw new InvalidOperationException("The font could not be read.");

                float scale = StbTrueType.stbtt_ScaleForPixelHeight(font, pixelSize);
                int ascent, descent, lineGap;
                StbTrueType.stbtt_GetFontVMetrics(font, &ascent, &descent, &lineGap);
                int ascentPx = (int)MathF.Ceiling(ascent * scale);
                int lineSpacing = (int)MathF.Ceiling((ascent - descent + lineGap) * scale);

                int atlasWidth = 1024;
                var glyphs = new List<(char C, byte[] Alpha, int W, int H, int X0, int Y0, float Advance)>();
                for (int c = FirstChar; c <= LastChar; c++)
                {
                    int x0, y0, x1, y1;
                    StbTrueType.stbtt_GetCodepointBitmapBox(font, c, scale, scale, &x0, &y0, &x1, &y1);
                    int w = x1 - x0, h = y1 - y0;
                    var alpha = new byte[Math.Max(1, w * h)];
                    if (w > 0 && h > 0)
                    {
                        fixed (byte* a = alpha)
                            StbTrueType.stbtt_MakeCodepointBitmap(font, a, w, h, w, scale, scale, c);
                    }
                    int advance, lsb;
                    StbTrueType.stbtt_GetCodepointHMetrics(font, c, &advance, &lsb);
                    glyphs.Add(((char)c, alpha, Math.Max(w, 0), Math.Max(h, 0), x0, y0, advance * scale));
                }

                // Shelf-pack the glyphs with a 1px gutter.
                var bounds = new List<Rectangle>();
                int penX = 0, penY = 0, rowHeight = 0;
                foreach (var g in glyphs)
                {
                    if (penX + g.W + 1 > atlasWidth)
                    {
                        penX = 0;
                        penY += rowHeight + 1;
                        rowHeight = 0;
                    }
                    bounds.Add(new Rectangle(penX, penY, g.W, g.H));
                    penX += g.W + 1;
                    rowHeight = Math.Max(rowHeight, g.H);
                }
                int atlasHeight = penY + rowHeight + 1;

                // Premultiplied white with the coverage as alpha, so SpriteBatch's tint colors it.
                var pixels = new Color[atlasWidth * atlasHeight];
                for (int i = 0; i < glyphs.Count; i++)
                {
                    var g = glyphs[i];
                    var r = bounds[i];
                    for (int y = 0; y < g.H; y++)
                    {
                        for (int x = 0; x < g.W; x++)
                        {
                            byte a = g.Alpha[y * g.W + x];
                            pixels[(r.Y + y) * atlasWidth + r.X + x] = new Color(a, a, a, a);
                        }
                    }
                }
                var texture = new Texture2D(device, atlasWidth, atlasHeight, false, SurfaceFormat.Color);
                texture.SetData(pixels);

                var cropping = new List<Rectangle>();
                var characters = new List<char>();
                var kerning = new List<Vector3>();
                foreach (var g in glyphs)
                {
                    characters.Add(g.C);
                    cropping.Add(new Rectangle(g.X0, ascentPx + g.Y0, g.W, g.H));
                    // Left bearing 0, width = advance, right bearing 0: the glyph is placed through the cropping offset.
                    kerning.Add(new Vector3(0, g.Advance, 0));
                }

                return new SpriteFont(texture, bounds, cropping, characters, lineSpacing, 0f, kerning, '?');
            }
        }
    }
}
