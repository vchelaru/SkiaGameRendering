# Skia Game Rendering

[![NuGet](https://img.shields.io/nuget/v/SkiaGameRendering)](https://www.nuget.org/packages/SkiaGameRendering) [![Join the chat](https://img.shields.io/discord/586997072373481494)](https://discord.gg/tG5RBgw)

Draw SkiaSharp vector art, text and SVG straight into MonoGame, KNI, FNA, raylib, Stride, Godot and
Unity textures on the GPU, with no CPU readback.

## Platform Support

| Engine | Backend | Package | Platforms |
|--------|---------|---------|-----------|
| MonoGame | [3.8.4 DesktopGL, OpenGL](docs/desktop/quickstart.md) | [`SkiaGameRendering`](https://www.nuget.org/packages/SkiaGameRendering) | Windows, Linux, macOS |
|  | [3.8.4 WindowsDX, D3D11](docs/desktop/quickstart.md) | [`SkiaGameRendering.WindowsDX`](https://www.nuget.org/packages/SkiaGameRendering.WindowsDX) | Windows |
|  | [3.8.6 WindowsDX12, D3D12](docs/desktop/quickstart.md) | [`SkiaGameRendering.WindowsDX12`](https://www.nuget.org/packages/SkiaGameRendering.WindowsDX12) | Windows |
|  | [3.8.4 Android, OpenGL ES](docs/android/monogame-quickstart.md) | [`SkiaGameRendering.Android`](https://www.nuget.org/packages/SkiaGameRendering.Android) | Android 7+ |
|  | [3.8.4 iOS, OpenGL ES](docs/ios/quickstart.md) | [`SkiaGameRendering.iOS`](https://www.nuget.org/packages/SkiaGameRendering.iOS) | iOS 15+ |
| KNI | [DesktopGL, OpenGL](docs/desktop/quickstart.md) | [`SkiaGameRendering.Kni.DesktopGL`](https://www.nuget.org/packages/SkiaGameRendering.Kni.DesktopGL) | Windows, Linux, macOS |
|  | [WindowsDX, D3D11](docs/desktop/quickstart.md) | [`SkiaGameRendering.Kni.WindowsDX`](https://www.nuget.org/packages/SkiaGameRendering.Kni.WindowsDX) | Windows |
|  | [Android, OpenGL ES](docs/android/quickstart.md) | [`SkiaGameRendering.Kni.Android`](https://www.nuget.org/packages/SkiaGameRendering.Kni.Android) | Android 7+ |
|  | [iOS, OpenGL ES](docs/ios/kni-quickstart.md) | [`SkiaGameRendering.Kni.iOS`](https://www.nuget.org/packages/SkiaGameRendering.Kni.iOS) | iOS 15+ |
|  | [WebGL (Blazor), WebGL2](docs/webgl/quickstart.md) | [`SkiaGameRendering.Kni.WebGL`](https://www.nuget.org/packages/SkiaGameRendering.Kni.WebGL) | Browser |
| raylib | [OpenGL](docs/raylib/quickstart.md) | [`SkiaGameRendering.Raylib.OGL`](https://www.nuget.org/packages/SkiaGameRendering.Raylib.OGL) | Windows, Linux, macOS |
|  | [WebGL2](docs/raylib/webgl-quickstart.md) | [`SkiaGameRendering.Raylib.WebGL`](https://www.nuget.org/packages/SkiaGameRendering.Raylib.WebGL) | Browser (.NET 10) |
| FNA | [D3D11](docs/fna/quickstart.md) | [`SkiaGameRendering.Fna.WindowsDX`](https://www.nuget.org/packages/SkiaGameRendering.Fna.WindowsDX) | Windows |
|  | [OpenGL](docs/fna/quickstart.md) | [`SkiaGameRendering.Fna.OGL`](https://www.nuget.org/packages/SkiaGameRendering.Fna.OGL) | Windows, Linux, macOS |
|  | [WebGL2](docs/fna/webgl-quickstart.md) | [`SkiaGameRendering.Fna.WebGL`](https://www.nuget.org/packages/SkiaGameRendering.Fna.WebGL) | Browser |
| Stride | [D3D11](docs/stride/quickstart.md) | [`SkiaGameRendering.Stride.D3D11`](https://www.nuget.org/packages/SkiaGameRendering.Stride.D3D11) | Windows |
|  | [Vulkan](docs/stride/vulkan-quickstart.md) | [`SkiaGameRendering.Stride.VK`](https://www.nuget.org/packages/SkiaGameRendering.Stride.VK) | Windows, Linux, macOS |
|  | [D3D12](docs/stride/d3d12-quickstart.md) | [`SkiaGameRendering.Stride.D3D12`](https://www.nuget.org/packages/SkiaGameRendering.Stride.D3D12) | Windows |
| BRUTAL | [Vulkan](docs/brutal/quickstart.md) | [`SkiaGameRendering.Brutal`](https://www.nuget.org/packages/SkiaGameRendering.Brutal) | Windows, Linux |
|  | [OpenGL](docs/brutal/quickstart.md#opengl) | [`SkiaGameRendering.Brutal.OGL`](https://www.nuget.org/packages/SkiaGameRendering.Brutal.OGL) | Windows, Linux |
| Godot 4.7+ | [Vulkan, D3D12, Metal, OpenGL 3.3](docs/godot/quickstart.md) | Source only | Windows, Linux, macOS |
| Unity 6 | [D3D11, D3D12, Vulkan, OpenGL, Metal, OpenGL ES](docs/unity/quickstart.md) | [UPM](docs/unity/quickstart.md) | Windows x64, macOS, Android |

## Quick Start

Skia draws straight into whatever the engine is rendering to:

<img src="docs/images/scene.png" width="600" alt="A red circle, a blue SVG water drop and a pink-to-purple gradient rounded rectangle">

```cs
// once, e.g. in LoadContent
_skia = new SkiaDirectCanvas(GraphicsDevice);
_red = new SKPaint { Color = SKColors.Red, IsAntialias = true };
_drop = new SKSvg(); // Svg.Skia
_drop.Load("drop.svg");
_gradient = new SKPaint
{
    IsAntialias = true,
    Shader = SKShader.CreateLinearGradient(new SKPoint(420, 20), new SKPoint(580, 180),
        new[] { new SKColor(0xF7, 0x25, 0x85), new SKColor(0x72, 0x09, 0xB7) }, SKShaderTileMode.Clamp),
};

// every frame, in Draw
_skia.Begin();
_skia.Canvas.DrawCircle(100, 100, 80, _red);
_skia.Canvas.DrawPicture(_drop.Picture, 220, 20);
_skia.Canvas.DrawRoundRect(new SKRect(420, 20, 580, 180), 32, 32, _gradient);
_skia.End();
```

Setup differs per engine; pick yours from the [table above](#platform-support).

## More

- API: [SkiaRenderTarget2D](docs/documentation/SkiaRenderTarget2D.md), [SkiaDirectCanvas](docs/documentation/SkiaDirectCanvas.md)
- [Samples](samples/README.md)
- [Performance tips](docs/documentation/performance-tips.md) and [measurements](docs/performance.md)
- [Architecture](docs/architecture.md) and [interop notes](SkiaGameRendering-Notes.md)
- [SkiaSharp drawing docs](https://learn.microsoft.com/en-us/previous-versions/xamarin/xamarin-forms/user-interface/graphics/skiasharp/basics/)

## License

MIT License. See [LICENSE.md](LICENSE.md).

## Credits

Originally created by [Miguel Anxo Figueirido](https://github.com/mfigueirido/SkiaMonoGameRendering).
