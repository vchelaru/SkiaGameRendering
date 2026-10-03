using Xunit;

namespace Tests.Raylib.OGL;

/// <summary>
/// Raylib keeps one window per process, so every test that calls <c>InitWindow</c> shares this
/// collection and xUnit runs them one at a time instead of in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RaylibWindowCollection
{
    public const string Name = "Raylib window";
}
