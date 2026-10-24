using Microsoft.Xna.Framework;

namespace Performance
{
    public enum SceneKind
    {
        Sprites,
        MultiTexture,
        Circles,
        Shapes,
        Gradients,
        Shadows,
        TextSingle,
        TextMixed,
        Svg,
        FillOpaque,
        FillTranslucent,
        Ui,
    }

    /// <summary>
    /// A deterministic set of animated items. Every renderer on every platform builds the same scenes,
    /// so they draw identical counts, positions, sizes, rotations and colors every frame. The sprite
    /// scenes use a seeded <see cref="Random"/>; every other scene derives its values from the item
    /// index with <see cref="Hash01"/>, so nothing depends on a generator's algorithm.
    /// </summary>
    public sealed class PerfScene
    {
        private const int Seed = 12345;
        private const int Margin = 40;

        public readonly struct Item
        {
            public readonly Vector2 BasePosition;
            public readonly float Size;
            public readonly float Amplitude;
            public readonly float Speed;
            public readonly float Phase;
            public readonly float RotationSpeed;

            public Item(Vector2 basePosition, float size, float amplitude, float speed, float phase, float rotationSpeed)
            {
                BasePosition = basePosition;
                Size = size;
                Amplitude = amplitude;
                Speed = speed;
                Phase = phase;
                RotationSpeed = rotationSpeed;
            }
        }

        /// <summary>
        /// Tints are picked by item index. The translucent entries check that the engine's premultiplied
        /// color and Skia's unpremultiplied one blend the same.
        /// </summary>
        public static readonly (byte R, byte G, byte B, byte A)[] TintPalette =
        {
            (255, 90, 90, 255), (255, 200, 60, 255), (90, 230, 110, 255), (80, 220, 255, 255),
            (190, 110, 255, 255), (255, 255, 255, 255), (255, 120, 200, 140), (120, 255, 200, 90),
        };

        /// <summary>Opaque palette for scenes that should not depend on blending.</summary>
        public static readonly (byte R, byte G, byte B)[] SolidPalette =
        {
            (235, 87, 87), (242, 153, 74), (242, 201, 76), (111, 207, 151),
            (86, 204, 242), (47, 128, 237), (155, 81, 224), (187, 107, 217),
        };

        /// <summary>Sizes, in pixels, of the text in <see cref="SceneKind.TextMixed"/>, picked by item index.</summary>
        public static readonly int[] MixedFontSizes = { 12, 16, 20, 28, 40, 64 };

        public const int SingleFontSize = 20;
        public const int TextureCount = 10;

        public string Page { get; }
        public string Name { get; }
        public SceneKind Kind { get; }
        public bool Tinted { get; }
        public Item[] Items { get; }
        public int Count => Items.Length;
        public int Width { get; }
        public int Height { get; }

        private PerfScene(string page, string name, SceneKind kind, bool tinted, Item[] items, int width, int height)
        {
            Page = page;
            Name = name;
            Kind = kind;
            Tinted = tinted;
            Items = items;
            Width = width;
            Height = height;
        }

        public static PerfScene[] BuildAll(int width, int height)
        {
            var scenes = new List<PerfScene>();

            // Page: sprites-single. The 0 row isolates the fixed per-frame cost; the others show how it grows.
            scenes.Add(Sprites("Sprites 0", 0, false, width, height));
            scenes.Add(Sprites("Sprites 500", 500, false, width, height));
            scenes.Add(Sprites("Sprites 2k", 2_000, false, width, height));
            scenes.Add(Sprites("Sprites 10k", 10_000, false, width, height));
            scenes.Add(Sprites("Sprites 50k", 50_000, false, width, height));
            scenes.Add(Sprites("Tinted sprites 10k", 10_000, true, width, height));

            // Page: sprites-textures. Item i uses texture i % 10, submitted in index order: 1..10, 1..10, ...
            foreach (var (name, count) in new[] { ("Textures 0", 0), ("Textures 300", 300), ("Textures 3k", 3_000), ("Textures 30k", 30_000) })
                scenes.Add(Formula("sprites-textures", name, SceneKind.MultiTexture, count, width, height, 8f, 24f, 10f, 30f));

            foreach (var (name, count) in new[] { ("Circles 500", 500), ("Circles 2k", 2_000), ("Circles 10k", 10_000) })
                scenes.Add(Formula("shapes", name, SceneKind.Circles, count, width, height, 6f, 18f, 10f, 30f));
            scenes.Add(Formula("shapes", "Mixed shapes 2k", SceneKind.Shapes, 2_000, width, height, 8f, 20f, 10f, 30f));
            scenes.Add(Formula("shapes", "Mixed shapes 10k", SceneKind.Shapes, 10_000, width, height, 8f, 20f, 10f, 30f));

            foreach (var (name, count) in new[] { ("Gradients 500", 500), ("Gradients 2k", 2_000), ("Gradients 10k", 10_000) })
                scenes.Add(Formula("gradients", name, SceneKind.Gradients, count, width, height, 8f, 22f, 10f, 30f));

            // UI shadows share a handful of panel sizes; the last scene gives every panel its own size.
            foreach (var (name, count) in new[] { ("Shadows 100", 100), ("Shadows 500", 500), ("Shadows 2k", 2_000) })
                scenes.Add(Formula("shadows", name, SceneKind.Shadows, count, width, height, 24f, 56f, 5f, 15f, sizeClasses: 5));
            scenes.Add(Formula("shadows", "Shadows 500 unique sizes", SceneKind.Shadows, 500, width, height, 24f, 56f, 5f, 15f));

            foreach (var (name, count) in new[] { ("Text 50", 50), ("Text 200", 200), ("Text 1k", 1_000) })
                scenes.Add(Formula("text", name, SceneKind.TextSingle, count, width, height, 0f, 0f, 5f, 15f));

            foreach (var (name, count) in new[] { ("Mixed text 50", 50), ("Mixed text 200", 200), ("Mixed text 1k", 1_000) })
                scenes.Add(Formula("text-mixed", name, SceneKind.TextMixed, count, width, height, 0f, 0f, 5f, 15f));

            foreach (var (name, count) in new[] { ("SVG 100", 100), ("SVG 500", 500), ("SVG 2k", 2_000) })
                scenes.Add(Formula("svg", name, SceneKind.Svg, count, width, height, 12f, 48f, 8f, 24f));

            // Count is the number of screen-sized layers.
            foreach (var (name, count) in new[] { ("Opaque 10 layers", 10), ("Opaque 40 layers", 40) })
                scenes.Add(Formula("fill-rate", name, SceneKind.FillOpaque, count, width, height, 0.8f, 1f, 5f, 15f));
            foreach (var (name, count) in new[] { ("Translucent 10 layers", 10), ("Translucent 40 layers", 40) })
                scenes.Add(Formula("fill-rate", name, SceneKind.FillTranslucent, count, width, height, 0.8f, 1f, 5f, 15f));

            foreach (var (name, count) in new[] { ("UI 50", 50), ("UI 200", 200), ("UI 1k", 1_000) })
                scenes.Add(Formula("ui", name, SceneKind.Ui, count, width, height, 40f, 60f, 3f, 8f));

            return scenes.ToArray();
        }

        // Sprite scenes keep their original seeded generation so results recorded before the other
        // scenes existed stay comparable.
        private static PerfScene Sprites(string name, int count, bool tinted, int width, int height)
        {
            const float minSize = 8f, maxSize = 24f;
            var rand = new Random(Seed);
            var items = new Item[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = new Item(
                    new Vector2(
                        Margin + (float)rand.NextDouble() * (width - Margin * 2),
                        Margin + (float)rand.NextDouble() * (height - Margin * 2)),
                    minSize + (float)rand.NextDouble() * (maxSize - minSize),
                    10f + (float)rand.NextDouble() * 20f,
                    0.4f + (float)rand.NextDouble() * 0.8f,
                    (float)(rand.NextDouble() * Math.PI * 2),
                    -1.5f + (float)rand.NextDouble() * 3f);
            }
            return new PerfScene("sprites-single", name, SceneKind.Sprites, tinted, items, width, height);
        }

        private static PerfScene Formula(string page, string name, SceneKind kind, int count, int width, int height,
            float minSize, float maxSize, float minAmplitude, float maxAmplitude, int sizeClasses = 0)
        {
            var items = new Item[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = new Item(
                    new Vector2(
                        Margin + Hash01(i, 1) * (width - Margin * 2),
                        Margin + Hash01(i, 2) * (height - Margin * 2)),
                    sizeClasses > 0
                        ? minSize + (maxSize - minSize) * (i % sizeClasses) / (sizeClasses - 1)
                        : minSize + Hash01(i, 3) * (maxSize - minSize),
                    minAmplitude + Hash01(i, 4) * (maxAmplitude - minAmplitude),
                    0.4f + Hash01(i, 5) * 0.8f,
                    Hash01(i, 6) * MathF.PI * 2f,
                    -1.5f + Hash01(i, 7) * 3f);
            }
            return new PerfScene(page, name, kind, false, items, width, height);
        }

        /// <summary>A stable value in [0, 1) for an item index and a purpose (salt), independent of any runtime's RNG.</summary>
        public static float Hash01(int index, int salt)
        {
            uint x = unchecked((uint)index * 747796405u + (uint)salt * 2891336453u + 1u);
            x ^= x >> 16;
            x = unchecked(x * 2246822519u);
            x ^= x >> 13;
            x = unchecked(x * 3266489917u);
            x ^= x >> 16;
            return (x & 0xFFFFFF) / 16777216f;
        }

        public Vector2 Position(int index, float t)
        {
            ref readonly var s = ref Items[index];
            float angle = t * s.Speed + s.Phase;
            return s.BasePosition + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * s.Amplitude;
        }

        public float Rotation(int index, float t)
        {
            ref readonly var s = ref Items[index];
            return t * s.RotationSpeed + s.Phase;
        }

        /// <summary>The text drawn for item <paramref name="index"/>: 24 or so characters that change with the index.</summary>
        public static string Label(int index) => $"Score {index * 37 % 100000:00000} Lives {index % 9}";

        public static int MixedFontSize(int index) => MixedFontSizes[index % MixedFontSizes.Length];
    }
}
