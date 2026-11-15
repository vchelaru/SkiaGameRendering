import OpenGL
func tryFmt(_ name: String, _ attrs: [CGLPixelFormatAttribute]) {
    var pix: CGLPixelFormatObj? = nil
    var n: GLint = 0
    let e = CGLChoosePixelFormat(attrs + [CGLPixelFormatAttribute(rawValue: 0)], &pix, &n)
    print("\(name): err=\(e.rawValue) npix=\(n)")
    if let pix = pix {
        var ctx: CGLContextObj? = nil
        CGLCreateContext(pix, nil, &ctx)
        if let ctx = ctx {
            CGLSetCurrentContext(ctx)
            if let v = glGetString(GLenum(GL_VERSION)), let r = glGetString(GLenum(GL_RENDERER)) {
                print("  GL_VERSION=\(String(cString: v)) GL_RENDERER=\(String(cString: r))")
            }
        }
    }
}
let core = [kCGLPFAOpenGLProfile, CGLPixelFormatAttribute(rawValue: UInt32(kCGLOGLPVersion_3_2_Core.rawValue))]
tryFmt("accelerated core", [kCGLPFAAccelerated] + core)
tryFmt("any core", core)
tryFmt("software core", [kCGLPFARendererID, CGLPixelFormatAttribute(rawValue: UInt32(kCGLRendererGenericFloatID))] + core)
tryFmt("any legacy", [])
