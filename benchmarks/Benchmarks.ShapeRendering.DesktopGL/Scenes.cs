using Microsoft.Xna.Framework;

namespace Benchmarks.ShapeRendering
{
    /// <summary>
    /// Which shape a given instance draws. When a <see cref="Scene"/>'s own <see cref="Scene.Kind"/>
    /// is <see cref="Mixed"/>, each instance carries its own per-shape kind instead.
    /// </summary>
    public enum ShapeKind
    {
        Circle,
        RoundedRect,
        Line,
        Triangle,
        Mixed,
        // Appended after Mixed so Mixed's `(ShapeKind)(i % 4)` round-robin (Circle..Triangle) is unaffected.
        Gradient,
        // Textured quads, compared against SpriteBatch instead of Apos.Shapes.
        Sprite,
    }

    /// <summary>
    /// One shape's static generation data plus the parameters driving its per-frame animation.
    /// Both renderers read the same instances, so they draw identical shape counts, positions,
    /// sizes and motion every frame - only the draw API differs.
    /// </summary>
    public struct ShapeInstance
    {
        public ShapeKind Kind;
        public Vector2 BasePosition;
        public Vector2 LineOffset;
        public float Size;
        public float Thickness;
        public float AmplitudeX;
        public float AmplitudeY;
        public float Speed;
        public float Phase;
        public float RotationSpeed;
        public Color FillColor;
        public Color BorderColor;
    }

    /// <summary>
    /// A fixed, seeded set of shape instances plus the animation formulas both renderers evaluate
    /// per frame. Regenerating a scene always produces the same data (same <see cref="Random"/> seed),
    /// so Skia and Apos.Shapes are always comparing against literally the same workload.
    /// </summary>
    public sealed class Scene
    {
        private const int Seed = 12345;
        private const int Margin = 60;

        public string Name { get; }
        public ShapeKind Kind { get; }
        public ShapeInstance[] Shapes { get; }
        public int Count => Shapes.Length;

        /// <summary>
        /// The renderers this scene is compared across. Sprite scenes pit Skia's two textured-quad
        /// APIs against SpriteBatch; every other scene is Skia vs Apos.Shapes.
        /// </summary>
        public RendererKind[] Renderers => Kind == ShapeKind.Sprite ? SpriteSceneRenderers : ShapeSceneRenderers;

        private static readonly RendererKind[] SpriteSceneRenderers = { RendererKind.Skia, RendererKind.SkiaAtlas, RendererKind.SpriteBatch };
        private static readonly RendererKind[] ShapeSceneRenderers = { RendererKind.Skia, RendererKind.AposShapes };

        private int[]? _allIndices;
        private readonly Dictionary<ShapeKind, int[]> _kindIndices = new();

        private Scene(string name, ShapeKind kind, ShapeInstance[] shapes)
        {
            Name = name;
            Kind = kind;
            Shapes = shapes;
        }

        // 0..Count-1 - every shape's kind never changes at runtime, so this (and IndicesOfKind
        // below) is computed once and cached rather than rebuilt every frame by the atlas renderer.
        public int[] AllIndices => _allIndices ??= Enumerable.Range(0, Count).ToArray();

        public int[] IndicesOfKind(ShapeKind kind)
        {
            if (!_kindIndices.TryGetValue(kind, out var indices))
            {
                indices = Enumerable.Range(0, Count).Where(i => Shapes[i].Kind == kind).ToArray();
                _kindIndices[kind] = indices;
            }
            return indices;
        }

        public static Scene[] BuildAll(int viewportWidth, int viewportHeight) => new[]
        {
            Generate("Circles", ShapeKind.Circle, 3000, viewportWidth, viewportHeight),
            Generate("Rounded rects (fill+border)", ShapeKind.RoundedRect, 2000, viewportWidth, viewportHeight),
            Generate("Lines", ShapeKind.Line, 4000, viewportWidth, viewportHeight),
            Generate("Mixed shapes", ShapeKind.Mixed, 3000, viewportWidth, viewportHeight),
            Generate("Gradients (linear, 2-stop)", ShapeKind.Gradient, 1500, viewportWidth, viewportHeight, minSize: 20f, maxSize: 50f),
            Generate("Transparency: overlapping translucent circles", ShapeKind.Circle, 1500, viewportWidth, viewportHeight, minSize: 40f, maxSize: 90f, translucent: true),
            Generate("Fillrate: large overlapping circles", ShapeKind.Circle, 800, viewportWidth, viewportHeight, minSize: 120f, maxSize: 220f),
            Generate("Draw-call bound: 50k tiny particles", ShapeKind.Circle, 50000, viewportWidth, viewportHeight, minSize: 2f, maxSize: 6f),
            // The sprite count sweep separates the fixed per-frame cost (the 0-sprite row: Skia
            // target switch, flush and blit) from the per-sprite cost (how the rows grow with count).
            Generate("Sprites: 0 (fixed per-frame cost)", ShapeKind.Sprite, 0, viewportWidth, viewportHeight),
            Generate("Sprites: 500", ShapeKind.Sprite, 500, viewportWidth, viewportHeight, minSize: 8f, maxSize: 24f),
            Generate("Sprites: 2k", ShapeKind.Sprite, 2000, viewportWidth, viewportHeight, minSize: 8f, maxSize: 24f),
            Generate("Sprites: 10k", ShapeKind.Sprite, 10000, viewportWidth, viewportHeight, minSize: 8f, maxSize: 24f),
        };

        private static Scene Generate(string name, ShapeKind kind, int count, int width, int height,
            float minSize = 6f, float maxSize = 24f, bool translucent = false)
        {
            // Fixed seed - regenerating (e.g. on window resize) never changes the workload.
            var rand = new Random(Seed);
            var shapes = new ShapeInstance[count];

            for (int i = 0; i < count; i++)
            {
                var pos = new Vector2(
                    Margin + (float)rand.NextDouble() * (width - Margin * 2),
                    Margin + (float)rand.NextDouble() * (height - Margin * 2));

                var perInstanceKind = kind == ShapeKind.Mixed ? (ShapeKind)(i % 4) : kind;

                float size = perInstanceKind == ShapeKind.Line
                    ? 2f + (float)rand.NextDouble() * 4f
                    : minSize + (float)rand.NextDouble() * (maxSize - minSize);

                var lineAngle = (float)(rand.NextDouble() * Math.PI * 2);
                var lineLength = perInstanceKind == ShapeKind.Line ? 15f + (float)rand.NextDouble() * 45f : 0f;

                byte alpha = translucent ? (byte)rand.Next(70, 180) : (byte)255;

                shapes[i] = new ShapeInstance
                {
                    Kind = perInstanceKind,
                    BasePosition = pos,
                    LineOffset = new Vector2(MathF.Cos(lineAngle), MathF.Sin(lineAngle)) * lineLength,
                    Size = size,
                    Thickness = 1.5f + (float)rand.NextDouble() * 2.5f,
                    AmplitudeX = 10f + (float)rand.NextDouble() * 20f,
                    AmplitudeY = 10f + (float)rand.NextDouble() * 20f,
                    Speed = 0.4f + (float)rand.NextDouble() * 0.8f,
                    Phase = (float)(rand.NextDouble() * Math.PI * 2),
                    RotationSpeed = -1.5f + (float)rand.NextDouble() * 3f,
                    FillColor = RandomColor(rand, alpha),
                    BorderColor = RandomColor(rand, alpha),
                };
            }

            return new Scene(name, kind, shapes);
        }

        // Kept dark so the white HUD text in the top-left corner (see Game1.DrawHud) stays readable
        // even where shapes overlap it.
        private static Color RandomColor(Random rand, byte alpha = 255) =>
            new Color(rand.Next(20, 130), rand.Next(20, 130), rand.Next(20, 130), (int)alpha);

        public Vector2 AnimatedPosition(int index, float t)
        {
            ref readonly var s = ref Shapes[index];
            return s.BasePosition + new Vector2(
                MathF.Cos(t * s.Speed + s.Phase) * s.AmplitudeX,
                MathF.Sin(t * s.Speed + s.Phase) * s.AmplitudeY);
        }

        public float AnimatedRotation(int index, float t)
        {
            ref readonly var s = ref Shapes[index];
            return t * s.RotationSpeed + s.Phase;
        }
    }
}
