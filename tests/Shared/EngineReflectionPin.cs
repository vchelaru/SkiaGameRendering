using System.Reflection;
using Xunit.Sdk;

namespace Tests.Shared;

/// <summary>
/// Assertion helpers for the per-backend tests that pin the private engine members each desktop
/// backend reaches by string. Linked into every backend's test project rather than packaged, since
/// MonoGame.Framework.DesktopGL, MonoGame.Framework.WindowsDX and nkast.Xna.Framework.Graphics all
/// define <c>Microsoft.Xna.Framework.Graphics.GraphicsDevice</c> and so cannot share one project.
///
/// Everything here reads metadata only - no GraphicsDevice, no window, no GPU - so the pins run on
/// any runner in milliseconds.
///
/// xunit's Assert.NotNull carries no message overload, and "Value is null" does not say which member
/// of which engine type vanished. These throw with the full name instead.
/// </summary>
internal static class EngineReflectionPin
{
    internal static Assembly RequireAssembly(string simpleName)
    {
        try
        {
            return Assembly.Load(simpleName);
        }
        catch (Exception ex)
        {
            throw new XunitException($"Assembly '{simpleName}' could not be loaded: {ex.Message}");
        }
    }

    internal static Type RequireType(Assembly assembly, string fullName) =>
        assembly.GetType(fullName)
            ?? throw new XunitException($"Type '{fullName}' not found in {assembly.GetName().Name}.");

    internal static Type RequireNestedType(Type declaringType, string name) =>
        declaringType.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new XunitException($"Nested type '{declaringType.FullName}.{name}' not found.");

    internal static FieldInfo RequireField(Type type, string name, BindingFlags flags) =>
        type.GetField(name, flags)
            ?? throw new XunitException($"Field '{type.FullName}.{name}' not found.");

    internal static PropertyInfo RequireProperty(Type type, string name, BindingFlags flags) =>
        type.GetProperty(name, flags)
            ?? throw new XunitException($"Property '{type.FullName}.{name}' not found.");

    /// <summary>
    /// Pins a property along with the type callers unbox its value to. A reflected read casts the
    /// boxed result, so a widened property type is an InvalidCastException at runtime, not a
    /// compile error.
    /// </summary>
    internal static PropertyInfo RequirePropertyOfType(Type type, string name, BindingFlags flags, Type propertyType)
    {
        var property = RequireProperty(type, name, flags);

        if (property.PropertyType != propertyType)
            throw new XunitException(
                $"Property '{type.FullName}.{name}' is {property.PropertyType.Name}, expected {propertyType.Name}.");

        return property;
    }

    internal static MethodInfo RequireMethod(Type type, string name, BindingFlags flags) =>
        type.GetMethod(name, flags)
            ?? throw new XunitException($"Method '{type.FullName}.{name}' not found.");

    /// <summary>
    /// Pins a delegate-typed field along with the argument count callers pass through it. Backends
    /// invoke these with a fixed-length <c>object[]</c>, so a changed arity is a TargetParameterCount
    /// exception at runtime, not a compile error.
    /// </summary>
    internal static FieldInfo RequireDelegateField(Type type, string name, BindingFlags flags, int parameterCount)
    {
        var field = RequireField(type, name, flags);

        if (!typeof(Delegate).IsAssignableFrom(field.FieldType))
            throw new XunitException(
                $"Field '{type.FullName}.{name}' is {field.FieldType.Name}, expected a delegate type.");

        RequireParameterCount(RequireMethod(field.FieldType, "Invoke", BindingFlags.Public | BindingFlags.Instance),
            parameterCount);

        return field;
    }

    internal static MethodInfo RequireParameterCount(MethodInfo method, int parameterCount)
    {
        var actual = method.GetParameters().Length;
        if (actual != parameterCount)
            throw new XunitException(
                $"'{method.DeclaringType?.FullName}.{method.Name}' takes {actual} parameters, expected {parameterCount}.");

        return method;
    }

    /// <summary>
    /// Every concrete type in <paramref name="assembly"/> that a live <paramref name="strategyType"/>
    /// instance could be. Backends reach these through <c>strategy.GetType()</c>, so the pin has to
    /// find them the same structural way instead of hardcoding a name the backend never spells.
    /// </summary>
    internal static IReadOnlyList<Type> RequireImplementations(Assembly assembly, Type strategyType)
    {
        var implementations = LoadableTypes(assembly)
            .Where(t => !t.IsAbstract && !t.IsInterface && strategyType.IsAssignableFrom(t))
            .ToArray();

        if (implementations.Length == 0)
            throw new XunitException(
                $"No concrete {strategyType.Name} implementation found in {assembly.GetName().Name}.");

        return implementations;
    }

    /// <summary>
    /// A platform assembly references types (SharpDX, SDL) that need not resolve for a metadata-only
    /// pin, so a partial load is expected rather than a failure.
    /// </summary>
    static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Select(t => t!);
        }
    }
}
