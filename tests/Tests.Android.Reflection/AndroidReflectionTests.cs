using System.Reflection;
using System.Runtime.InteropServices;
using Tests.Shared;
using Xunit;
using Xunit.Sdk;

namespace Tests.Android;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EnginePaths(string nuGetPackageRoot, string kniVersion) : Attribute
{
    public string NuGetPackageRoot { get; } = nuGetPackageRoot;
    public string KniVersion { get; } = kniVersion;
}

/// <summary>
/// Pins the KNI Android and MonoGame Android members that <c>KniAndroidStateInvalidation</c> and
/// <c>MonoGameStateInvalidation</c> reach by string after a back buffer <c>SkiaDirectCanvas</c> pass. None of
/// it is compile-checked. The engine assemblies only ship for android TFMs, so they are read as metadata
/// (MetadataLoadContext) from the downloaded packages, the same names and binding flags the backends use.
/// </summary>
public sealed class AndroidReflectionTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static readonly string[] DirtyFlagNames =
    {
        "_blendStateDirty", "_blendFactorDirty", "_depthStencilStateDirty", "_rasterizerStateDirty",
        "_scissorRectangleDirty", "_vertexBuffersDirty", "_indexBufferDirty", "_vertexShaderDirty", "_pixelShaderDirty",
    };

    static readonly EnginePaths Paths = typeof(AndroidReflectionTests).Assembly.GetCustomAttribute<EnginePaths>()!;
    static readonly MetadataLoadContext Context = CreateContext();

    static string Package(string id, string version, string lib, string dll) =>
        Path.Combine(Paths.NuGetPackageRoot, id, version, "lib", lib, dll);

    static string KniPlatformPath => Package("nkast.kni.platform.android.gl", Paths.KniVersion, "net10.0-android36.0", "Kni.Platform.dll");
    static string KniGraphicsPath => Package("nkast.xna.framework.graphics", Paths.KniVersion, "net8.0", "Xna.Framework.Graphics.dll");
    static string KniFrameworkPath => Package("nkast.xna.framework", Paths.KniVersion, "net8.0", "Xna.Framework.dll");
    static string MonoGamePath => Package("monogame.framework.android", "3.8.4.1", "net8.0-android34.0", "MonoGame.Framework.dll");

    static MetadataLoadContext CreateContext()
    {
        var paths = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll").ToList();
        foreach (var path in new[] { KniGraphicsPath, KniFrameworkPath })
        {
            if (!File.Exists(path))
                throw new XunitException($"{path} not found; did the PackageDownload in the csproj change?");
            paths.Add(path);
        }
        return new MetadataLoadContext(new PathAssemblyResolver(paths));
    }

    static Assembly Load(string path) =>
        File.Exists(path) ? Context.LoadFromAssemblyPath(path)
            : throw new XunitException($"{path} not found; did the PackageDownload in the csproj change?");

    static void RequireFieldOfType(Type type, string name, BindingFlags flags, string typeName)
    {
        FieldInfo? field = null;
        for (var t = type; t != null && field == null; t = t.BaseType)
            field = t.GetField(name, flags | BindingFlags.DeclaredOnly);
        if (field == null)
            throw new XunitException($"Field '{type.FullName}.{name}' not found.");
        // A generic field is named by its definition, e.g. System.Collections.Generic.List`1.
        var fieldType = field.FieldType.IsGenericType ? field.FieldType.GetGenericTypeDefinition() : field.FieldType;
        if (fieldType.FullName != typeName)
            throw new XunitException($"Field '{type.FullName}.{name}' is {field.FieldType.FullName}, expected {typeName}.");
    }

    static void RequireMethod(Type type, string name, params string[] parameterTypes)
    {
        var found = type.GetMethods(Instance).Any(m => m.Name == name
            && m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(parameterTypes));
        if (!found)
            throw new XunitException($"Method '{type.FullName}.{name}({string.Join(", ", parameterTypes)})' not found.");
    }

    [Fact]
    public void Kni_ContextCacheFields_Resolve()
    {
        var context = EngineReflectionPin.RequireType(Load(KniPlatformPath), "Microsoft.Xna.Platform.Graphics.ConcreteGraphicsContext");
        RequireFieldOfType(context, "_shaderProgram", Instance, "Microsoft.Xna.Platform.Graphics.ShaderProgram");
        RequireFieldOfType(context, "_lastVertexAttribs", Instance, "System.Int32");
        RequireFieldOfType(context, "_enabledVertexAttributesSet", Instance, "System.Collections.Generic.HashSet`1");
        RequireFieldOfType(context, "_lastClearColor", Instance, "Microsoft.Xna.Framework.Vector4");
        RequireFieldOfType(context, "_lastClearDepth", Instance, "System.Single");
        RequireFieldOfType(context, "_lastClearStencil", Instance, "System.Int32");
    }

    [Fact]
    public void Kni_DirtyFlags_Resolve()
    {
        var strategy = EngineReflectionPin.RequireType(Load(KniGraphicsPath), "Microsoft.Xna.Platform.Graphics.GraphicsContextStrategy");
        foreach (var name in DirtyFlagNames)
            RequireFieldOfType(strategy, name, Instance, "System.Boolean");
    }

    [Fact]
    public void MonoGame_DeviceCacheFields_Resolve()
    {
        var device = EngineReflectionPin.RequireType(Load(MonoGamePath), "Microsoft.Xna.Framework.Graphics.GraphicsDevice");
        RequireFieldOfType(device, "_shaderProgram", Instance, "Microsoft.Xna.Framework.Graphics.ShaderProgram");
        RequireFieldOfType(device, "_attribsDirty", Static, "System.Boolean");
        RequireFieldOfType(device, "_enabledVertexAttributes", Static, "System.Collections.Generic.List`1");
        RequireFieldOfType(device, "_lastClearColor", Instance, "Microsoft.Xna.Framework.Vector4");
        RequireFieldOfType(device, "_lastClearDepth", Instance, "System.Single");
        RequireFieldOfType(device, "_lastClearStencil", Instance, "System.Int32");
        RequireFieldOfType(device, "_actualDepthStencilState", Instance, "Microsoft.Xna.Framework.Graphics.DepthStencilState");
        RequireFieldOfType(device, "_actualRasterizerState", Instance, "Microsoft.Xna.Framework.Graphics.RasterizerState");
        foreach (var name in DirtyFlagNames)
            RequireFieldOfType(device, name, Instance, "System.Boolean");
    }

    [Fact]
    public void MonoGame_ForcedApplyMethods_Resolve()
    {
        var monoGame = Load(MonoGamePath);
        const string device = "Microsoft.Xna.Framework.Graphics.GraphicsDevice";
        RequireMethod(EngineReflectionPin.RequireType(monoGame, device), "PlatformApplyBlend", "System.Boolean");
        RequireMethod(EngineReflectionPin.RequireType(monoGame, "Microsoft.Xna.Framework.Graphics.DepthStencilState"),
            "PlatformApplyState", device, "System.Boolean");
        RequireMethod(EngineReflectionPin.RequireType(monoGame, "Microsoft.Xna.Framework.Graphics.RasterizerState"),
            "PlatformApplyState", device, "System.Boolean");
        RequireMethod(EngineReflectionPin.RequireType(monoGame, "Microsoft.Xna.Framework.Graphics.TextureCollection"), "Dirty");
    }
}
