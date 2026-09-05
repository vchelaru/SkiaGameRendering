using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Tests.Core.ANGLE")]
// SkiaGameRendering.Stride.D3D11 needs D3D11Com.GetImmediateContext/Release directly: Stride's own
// immediate-context accessor isn't public (unlike its device), so the adapter asks the device for
// it over COM instead of reflecting a private Stride field - see SkiaStrideContext.Initialize.
[assembly: InternalsVisibleTo("SkiaGameRendering.Stride.D3D11")]
