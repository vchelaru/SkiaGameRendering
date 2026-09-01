using Microsoft.Xna.Framework.Graphics;

namespace SkiaGameRendering
{
    /// <summary>
    /// Holds the shared <see cref="SkiaBackend"/> for a <see cref="GraphicsDevice"/>. Most code
    /// never calls this directly — constructing a <see cref="SkiaRenderTarget2D"/> auto-initializes
    /// it. Call <see cref="Initialize(SkiaBackend, GraphicsDevice)"/> explicitly only to force a
    /// specific backend (e.g. in tests) instead of auto-detection.
    /// </summary>
    public static partial class SkiaRenderer
    {
        private static SkiaBackend? _backend;
        private static GraphicsDevice? _graphicsDevice;

        // Set by a platform package whose backend can't be auto-constructed via a parameterless
        // constructor (e.g. SkiaWebGlBackend, which needs a host - see
        // SkiaGameRendering.Kni.WebGL's SkiaRenderer.Web.cs partial). Null everywhere else.
        private static Func<SkiaBackend>? _ambientBackendFactory = null;

        // Same idea, for readiness: null everywhere except a platform whose backend needs an
        // async-created context (WebGL). Defaulting to "always ready" is correct for every other
        // platform's backend, whose context already exists by construction - see SkiaBackend.Ready.
        private static Func<bool>? _ambientReadyCheck = null;

        public static bool IsInitialized => _backend != null;

        /// <summary>
        /// The currently initialized backend, or null if none. Downcast to a specific backend type
        /// (e.g. <c>SkiaWebGlBackend</c>) to reach members beyond this platform-agnostic base -
        /// useful for code (like a platform-specific sample) that wants diagnostics or other
        /// backend-specific access without holding its own reference from construction time.
        /// </summary>
        public static SkiaBackend? CurrentBackend => _backend;

        /// <summary>
        /// True once <see cref="Initialize(GraphicsDevice)"/> is safe to call. Always true unless a
        /// platform package that needs an async-created context (currently just
        /// <c>SkiaGameRendering.Kni.WebGL</c>) has attached one that isn't ready yet - poll this
        /// from <c>Update</c>/<c>Draw</c> instead of awaiting anything. Written this way, the same
        /// <c>Game</c> code works unchanged on every platform this library supports: desktop
        /// backends are ready on the very first check, so initialization just happens immediately.
        /// </summary>
        public static bool IsReady => _ambientReadyCheck?.Invoke() ?? true;

        public static void Initialize(SkiaBackend backend, GraphicsDevice graphicsDevice)
        {
            ArgumentNullException.ThrowIfNull(backend);
            ArgumentNullException.ThrowIfNull(graphicsDevice);

            if (_backend != null)
            {
                if (ReferenceEquals(_backend, backend) && ReferenceEquals(_graphicsDevice, graphicsDevice))
                    return;

                throw new InvalidOperationException(
                    "SkiaRenderer is already initialized. Call SkiaRenderer.Dispose() before changing the backend or GraphicsDevice.");
            }

            backend.Initialize(graphicsDevice);
            _backend = backend;
            _graphicsDevice = graphicsDevice;
        }

        /// <summary>
        /// Auto-detects the correct backend for the current MonoGame platform.
        /// Explicit initialization is recommended for trimmed applications.
        /// </summary>
        public static void Initialize(GraphicsDevice graphicsDevice)
        {
            if (_ambientBackendFactory != null)
            {
                Initialize(_ambientBackendFactory(), graphicsDevice);
                return;
            }

            var backendType = FindBackendType()
                ?? throw new InvalidOperationException(
                    "Could not auto-detect a SkiaBackend. Reference the platform package or initialize an explicit backend.");

            SkiaBackend backend;
            try
            {
                backend = (SkiaBackend?)Activator.CreateInstance(backendType)
                    ?? throw new InvalidOperationException($"Could not create backend '{backendType.FullName}'.");
            }
            catch (MissingMethodException exception)
            {
                throw new InvalidOperationException(
                    $"'{backendType.FullName}' has no public parameterless constructor, so it can't be " +
                    "auto-detected. This backend requires extra setup (e.g. a host object) - construct it " +
                    $"explicitly and pass it to SkiaRenderer.Initialize(SkiaBackend, GraphicsDevice) instead.",
                    exception);
            }

            Initialize(backend, graphicsDevice);
        }

        /// <summary>
        /// Called by <see cref="SkiaRenderTarget2D"/>'s constructor. Auto-initializes against
        /// <paramref name="graphicsDevice"/> if nothing has initialized the renderer yet.
        /// </summary>
        internal static SkiaBackend EnsureInitialized(GraphicsDevice graphicsDevice)
        {
            if (_backend == null)
            {
                Initialize(graphicsDevice);
            }
            else if (!ReferenceEquals(_graphicsDevice, graphicsDevice))
            {
                throw new InvalidOperationException(
                    "A SkiaRenderTarget2D was constructed with a different GraphicsDevice than the one " +
                    "SkiaRenderer is currently initialized with.");
            }

            return _backend!;
        }

        private static Type? FindBackendType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (!type.IsAbstract && type.IsSubclassOf(typeof(SkiaBackend)))
                            return type;
                    }
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                }
            }

            return null;
        }

        /// <summary>
        /// Disposes the shared backend. Dispose any live <see cref="SkiaRenderTarget2D"/> instances
        /// first — this does not track or dispose them for you.
        /// </summary>
        public static void Dispose()
        {
            if (_backend == null)
                return;

            var backend = _backend;
            _backend = null;
            _graphicsDevice = null;
            backend.Dispose();
        }
    }
}
