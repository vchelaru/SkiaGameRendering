using System.Reflection;

namespace Performance
{
    /// <summary>
    /// The images and files both renderers draw, generated in code or embedded in the assembly, so no
    /// platform needs a content pipeline or a file system.
    /// </summary>
    public static class PerfAssets
    {
        public const int Size = 64;

        /// <summary>
        /// RGBA8 pixels of a soft-edged disc with a lighter ring, premultiplied so SpriteBatch's
        /// AlphaBlend and Skia's Premul image blend identically.
        /// </summary>
        public static byte[] BuildPixels() => BuildPixels(0, ring: 0.7f, color: (90, 170, 255));

        /// <summary>Ten distinct images for the multi-texture scene: different color and ring position each.</summary>
        public static byte[] BuildTexturePixels(int variant)
        {
            var (r, g, b) = PerfScene.SolidPalette[variant % PerfScene.SolidPalette.Length];
            float ring = 0.45f + 0.05f * (variant % 6);
            return BuildPixels(variant, ring, (r, g, b));
        }

        private static byte[] BuildPixels(int variant, float ring, (int R, int G, int B) color)
        {
            var pixels = new byte[Size * Size * 4];
            float center = (Size - 1) / 2f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float d = MathF.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / center;
                    float alpha = Math.Clamp((1f - d) * 8f, 0f, 1f);
                    float shade = d > ring ? 1f : 0.55f;
                    int o = (y * Size + x) * 4;
                    pixels[o + 0] = (byte)(color.R * shade * alpha);
                    pixels[o + 1] = (byte)(color.G * shade * alpha);
                    pixels[o + 2] = (byte)(color.B * shade * alpha);
                    pixels[o + 3] = (byte)(255 * alpha);
                }
            }
            return pixels;
        }

        /// <summary>The font every text scene uses (Noto Sans, SIL OFL), embedded from samples/Sample.Gum.Kni.WebGL/Assets.</summary>
        public static byte[] FontBytes => ReadEmbedded("Performance.NotoSans.ttf");

        /// <summary>The icon the SVG scenes draw (samples/Shared/Drop.svg: a path with a linear gradient and a stroke).</summary>
        public static byte[] SvgBytes => ReadEmbedded("Performance.Icon.svg");

        private static byte[] ReadEmbedded(string name)
        {
            using var stream = typeof(PerfAssets).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded resource {name} is missing.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
