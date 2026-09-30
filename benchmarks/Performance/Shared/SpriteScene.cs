using Microsoft.Xna.Framework;

namespace Performance
{
    /// <summary>
    /// A seeded set of animated sprites. The Raw and Skia apps build the same scenes from the same
    /// seed, so they draw identical counts, positions, sizes, rotations and tints every frame.
    /// </summary>
    public sealed class SpriteScene
    {
        private const int Seed = 12345;
        private const int Margin = 40;
        private const float MinSize = 8f;
        private const float MaxSize = 24f;

        public readonly struct Sprite
        {
            public readonly Vector2 BasePosition;
            public readonly float Size;
            public readonly float Amplitude;
            public readonly float Speed;
            public readonly float Phase;
            public readonly float RotationSpeed;

            public Sprite(Vector2 basePosition, float size, float amplitude, float speed, float phase, float rotationSpeed)
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
        /// Tints are picked by sprite index. The translucent entries check that the Raw app's
        /// premultiplied color and Skia's unpremultiplied one blend the same.
        /// </summary>
        public static readonly (byte R, byte G, byte B, byte A)[] TintPalette =
        {
            (255, 90, 90, 255), (255, 200, 60, 255), (90, 230, 110, 255), (80, 220, 255, 255),
            (190, 110, 255, 255), (255, 255, 255, 255), (255, 120, 200, 140), (120, 255, 200, 90),
        };

        public string Name { get; }
        public bool Tinted { get; }
        public Sprite[] Sprites { get; }
        public int Count => Sprites.Length;

        private SpriteScene(string name, bool tinted, Sprite[] sprites)
        {
            Name = name;
            Tinted = tinted;
            Sprites = sprites;
        }

        // The 0-sprite row isolates the fixed per-frame cost (for Skia: context switch, flush and
        // composite); the others show how that cost grows per sprite.
        public static SpriteScene[] BuildAll(int width, int height) => new[]
        {
            Generate("Sprites 0", 0, false, width, height),
            Generate("Sprites 500", 500, false, width, height),
            Generate("Sprites 2k", 2_000, false, width, height),
            Generate("Sprites 10k", 10_000, false, width, height),
            Generate("Sprites 50k", 50_000, false, width, height),
            Generate("Tinted sprites 10k", 10_000, true, width, height),
        };

        private static SpriteScene Generate(string name, int count, bool tinted, int width, int height)
        {
            var rand = new Random(Seed);
            var sprites = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                sprites[i] = new Sprite(
                    new Vector2(
                        Margin + (float)rand.NextDouble() * (width - Margin * 2),
                        Margin + (float)rand.NextDouble() * (height - Margin * 2)),
                    MinSize + (float)rand.NextDouble() * (MaxSize - MinSize),
                    10f + (float)rand.NextDouble() * 20f,
                    0.4f + (float)rand.NextDouble() * 0.8f,
                    (float)(rand.NextDouble() * Math.PI * 2),
                    -1.5f + (float)rand.NextDouble() * 3f);
            }
            return new SpriteScene(name, tinted, sprites);
        }

        public Vector2 Position(int index, float t)
        {
            ref readonly var s = ref Sprites[index];
            float angle = t * s.Speed + s.Phase;
            return s.BasePosition + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * s.Amplitude;
        }

        public float Rotation(int index, float t)
        {
            ref readonly var s = ref Sprites[index];
            return t * s.RotationSpeed + s.Phase;
        }
    }
}
