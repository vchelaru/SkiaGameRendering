using Xunit;

namespace Tests.Shared;

/// <summary>
/// <c>SkiaRenderer</c> holds one backend in a static, so test classes that each initialize it against
/// their own headless device must not run in parallel with each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SkiaRendererCollection
{
    internal const string Name = "SkiaRenderer static state";
}
