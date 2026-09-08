using Xunit;

// Both Stride backends hold their Skia context in a static keyed to one GraphicsDevice
// (SkiaStrideRenderer / SkiaStrideVulkanRenderer), and every device-backed test here creates its own
// device and disposes the renderer afterwards. xunit's default is to run separate test classes in
// parallel, which would let two of those overlap - the second construction throws "already
// initialized ... call Dispose before switching GraphicsDevice", and even without the static there
// is no reason to want two live Stride devices in one process. Serializing the assembly is the
// blunt fix that a future device-backed test class can't forget to opt into.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
