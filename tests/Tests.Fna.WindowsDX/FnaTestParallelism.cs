using Xunit;

// SkiaRenderer holds one backend in a static, and OneFrameGame initializes and disposes it inside
// each test's single frame. xunit's default runs separate test classes in parallel, which would let
// two frames overlap - and FNA's SDL window creation is not something to run from two threads
// either. Same blunt fix as tests/Shared/StrideTestParallelism.cs.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
