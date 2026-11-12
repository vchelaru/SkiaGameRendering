using System.Numerics;

namespace Performance
{
    public enum SceneKind
    {
        Sprites,
        MultiTexture,
        Circles,
        Rectangles,
        RoundedRectangles,
        Shapes,
        Gradients,
        Shadows,
        TextSingle,
        TextMixed,
        Svg,
        FillOpaque,
        FillTranslucent,
        Ui,

        // Skia-only "guide" scenes. Each guide page holds sets whose rows share a workload and change one factor
        // (the scene's Variant), so they compare directly. Engine renderers draw nothing for them.
        GuideClip,
        GuidePicture,
        GuideItemAlpha,
        GuideSceneLayer,
        GuideTargetSize,
        GuideDrawOrder,
        GuidePaintReuse,
        GuideFontReuse,
        GuidePathReuse,
        GuideShadow,
        GuideStroke,
        GuideSampling,
    }

    /// <summary>The one factor a guide scene varies; see <see cref="PerfScene.Variant"/>.</summary>
    public enum GuideVariant
    {
        None,
        ClipRect,
        ClipRoundRect,
        ClipPath,
        Direct,
        PictureReplay,
        PaintAlpha,
        SaveLayer,
        Interleaved,
        Grouped,
        NewPerDraw,
        Reused,
        MaskBlur,
        DropShadowFilter,
        NineSlice,
        Fill,
        Stroke1,
        Hairline,
        Stroke4,
        Nearest,
        Linear,
        LinearMipmaps,
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

        /// <summary>What a guide scene varies; <see cref="GuideVariant.None"/> for the other scenes.</summary>
        public GuideVariant Variant { get; private init; }

        /// <summary>
        /// For <see cref="SceneKind.GuideTargetSize"/>: the render target size the host draws into, with the scene
        /// scaled to fill it. Zero everywhere else, meaning the window-sized target.
        /// </summary>
        public int TargetWidth { get; private init; }
        public int TargetHeight { get; private init; }

        /// <summary>True for the guide scenes, which only the Skia renderers draw.</summary>
        public bool SkiaOnly => Page.StartsWith("guide-", StringComparison.Ordinal);

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

            foreach (var (name, count) in new[] { ("Circles 500", 500), ("Circles 10k", 10_000) })
                scenes.Add(Formula("shapes", name, SceneKind.Circles, count, width, height, 6f, 18f, 10f, 30f));
            foreach (var (name, count) in new[] { ("Rectangles 500", 500), ("Rectangles 10k", 10_000) })
                scenes.Add(Formula("shapes", name, SceneKind.Rectangles, count, width, height, 6f, 18f, 10f, 30f));
            foreach (var (name, count) in new[] { ("Rounded rectangles 500", 500), ("Rounded rectangles 10k", 10_000) })
                scenes.Add(Formula("shapes", name, SceneKind.RoundedRectangles, count, width, height, 6f, 18f, 10f, 30f));
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

            foreach (var (name, count) in new[] { ("UI 50", 50), ("UI 1k", 1_000) })
                scenes.Add(Formula("ui", name, SceneKind.Ui, count, width, height, 40f, 60f, 3f, 8f));

            AddGuideScenes(scenes, width, height);
            return scenes.ToArray();
        }

        /// <summary>
        /// The guide pages: Skia-only sets that each answer one "which way is faster" question. Within a set every
        /// row draws the same items and changes only <see cref="Variant"/>.
        /// </summary>
        private static void AddGuideScenes(List<PerfScene> scenes, int width, int height)
        {
            PerfScene Guide(string page, string name, SceneKind kind, GuideVariant variant, int count,
                float minSize, float maxSize, int targetWidth = 0, int targetHeight = 0, int sizeClasses = 0) =>
                new(page, name, kind, false, Formula(page, name, kind, count, width, height, minSize, maxSize, 10f, 30f, sizeClasses).Items, width, height)
                {
                    Variant = variant,
                    TargetWidth = targetWidth,
                    TargetHeight = targetHeight,
                };

            // Each item saves, clips to a region smaller than its circle, draws the circle and restores. "None" still saves and restores.
            foreach (var (name, v) in new[] { ("Clip none 2k", GuideVariant.None), ("ClipRect 2k", GuideVariant.ClipRect),
                         ("ClipRoundRect 2k", GuideVariant.ClipRoundRect), ("ClipPath 2k", GuideVariant.ClipPath) })
                scenes.Add(Guide("guide-clip", name, SceneKind.GuideClip, v, 2_000, 6f, 18f));

            // Axis-aligned circles, rects and rounded rects frozen at t = 0: drawn call by call, or recorded once into an SKPicture and replayed.
            scenes.Add(Guide("guide-picture", "Static shapes direct 2k", SceneKind.GuidePicture, GuideVariant.Direct, 2_000, 8f, 20f));
            scenes.Add(Guide("guide-picture", "Static shapes SKPicture 2k", SceneKind.GuidePicture, GuideVariant.PictureReplay, 2_000, 8f, 20f));

            // Half-transparent two-shape items: alpha on each paint, or opaque paints inside a SaveLayer with alpha per item.
            scenes.Add(Guide("guide-savelayer", "Item alpha paint 250", SceneKind.GuideItemAlpha, GuideVariant.PaintAlpha, 250, 8f, 20f));
            scenes.Add(Guide("guide-savelayer", "Item alpha SaveLayer 250", SceneKind.GuideItemAlpha, GuideVariant.SaveLayer, 250, 8f, 20f));
            // Opaque circles drawn straight to the target, or inside one SaveLayer (alpha 200) around the whole scene.
            scenes.Add(Guide("guide-savelayer", "Scene no layer 2k", SceneKind.GuideSceneLayer, GuideVariant.None, 2_000, 6f, 18f));
            scenes.Add(Guide("guide-savelayer", "Scene one SaveLayer 2k", SceneKind.GuideSceneLayer, GuideVariant.SaveLayer, 2_000, 6f, 18f));

            // The same axis-aligned shapes, animated and scaled to fill render targets of different sizes.
            foreach (var (w, h) in new[] { (800, 800), (1600, 900), (3840, 2160) })
                scenes.Add(Guide("guide-target-size", $"Target {w}x{h}", SceneKind.GuideTargetSize, GuideVariant.None, 2_000, 8f, 20f, w, h));

            // Circle, rect, text, image per item: in item order (i % 4), or every circle first, then the rects, text and images.
            scenes.Add(Guide("guide-draw-order", "Interleaved 2k", SceneKind.GuideDrawOrder, GuideVariant.Interleaved, 2_000, 6f, 16f));
            scenes.Add(Guide("guide-draw-order", "Grouped by type 2k", SceneKind.GuideDrawOrder, GuideVariant.Grouped, 2_000, 6f, 16f));

            scenes.Add(Guide("guide-reuse", "SKPaint new per draw 2k", SceneKind.GuidePaintReuse, GuideVariant.NewPerDraw, 2_000, 6f, 18f));
            scenes.Add(Guide("guide-reuse", "SKPaint reused 2k", SceneKind.GuidePaintReuse, GuideVariant.Reused, 2_000, 6f, 18f));
            scenes.Add(Guide("guide-reuse", "SKFont new per label 500", SceneKind.GuideFontReuse, GuideVariant.NewPerDraw, 500, 0f, 0f));
            scenes.Add(Guide("guide-reuse", "SKFont cached 500", SceneKind.GuideFontReuse, GuideVariant.Reused, 500, 0f, 0f));
            // A star per item at the item's size: built with SKPathBuilder every frame, or built once and kept.
            scenes.Add(Guide("guide-reuse", "SKPath rebuilt per frame 1k", SceneKind.GuidePathReuse, GuideVariant.NewPerDraw, 1_000, 8f, 20f));
            scenes.Add(Guide("guide-reuse", "SKPath cached 1k", SceneKind.GuidePathReuse, GuideVariant.Reused, 1_000, 8f, 20f));

            // Panels in five sizes, as a UI reuses a few, with the same soft shadow made three ways.
            foreach (var (name, v) in new[] { ("Shadow MaskFilter blur 100", GuideVariant.MaskBlur),
                         ("Shadow DropShadow filter 100", GuideVariant.DropShadowFilter), ("Shadow nine-slice image 100", GuideVariant.NineSlice) })
                scenes.Add(Guide("guide-shadows", name, SceneKind.GuideShadow, v, 100, 40f, 60f, sizeClasses: 5));

            // Alternating circles and rounded rectangles.
            foreach (var (name, v) in new[] { ("Fill 2k", GuideVariant.Fill), ("Stroke 1px 2k", GuideVariant.Stroke1),
                         ("Hairline 2k", GuideVariant.Hairline), ("Stroke 4px 2k", GuideVariant.Stroke4) })
                scenes.Add(Guide("guide-strokes", name, SceneKind.GuideStroke, v, 2_000, 6f, 18f));

            // DrawAtlas of a 256 px image drawn at 16-48 px, so every sprite is downscaled 5x to 16x.
            foreach (var (name, v) in new[] { ("Sampling nearest 10k", GuideVariant.Nearest), ("Sampling linear 10k", GuideVariant.Linear),
                         ("Sampling linear mipmaps 10k", GuideVariant.LinearMipmaps) })
                scenes.Add(Guide("guide-sampling", name, SceneKind.GuideSampling, v, 10_000, 8f, 24f));
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

        /// <summary>Corner radius of a <see cref="SceneKind.RoundedRectangles"/> item: 25% of its short side, s * 1.4.</summary>
        public static float CornerRadius(float size) => size * 1.4f * 0.25f;

        public static int MixedFontSize(int index) => MixedFontSizes[index % MixedFontSizes.Length];
    }
}
