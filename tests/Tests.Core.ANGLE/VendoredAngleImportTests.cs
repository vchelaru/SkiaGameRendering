using System.Reflection.PortableExecutable;
using System.Text;
using Xunit;

namespace Tests.CoreAngle;

/// <summary>
/// Every DLL a vendored ANGLE binary imports must either ship in the same runtimes/&lt;rid&gt;/native
/// folder or be part of Windows (or the VC++ redistributable). A non-vendored import only loads when
/// some copy happens to be on PATH, so it passes on one machine and fails with DllNotFoundException on
/// the next. This reads the import tables straight from the files, so it runs on any OS.
/// </summary>
public sealed class VendoredAngleImportTests
{
    // Windows system DLLs, plus the VC++ redistributable (VCRUNTIME/MSVCP), which ANGLE's
    // dynamic-CRT vcpkg triplets link against and which consumers are expected to have installed.
    private static readonly HashSet<string> ProvidedByWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        "KERNEL32.dll", "USER32.dll", "GDI32.dll", "dxgi.dll", "d3d9.dll", "d3d11.dll",
        "VCRUNTIME140.dll", "VCRUNTIME140_1.dll", "MSVCP140.dll",
    };

    public static TheoryData<string> Rids => new() { "win-x64", "win-arm64" };

    [Theory]
    [MemberData(nameof(Rids))]
    public void EveryImport_IsVendoredOrProvidedByWindows(string rid)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native");
        var vendored = Directory.GetFiles(dir, "*.dll").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        // The folder also holds SkiaSharp's own natives; only ANGLE's imports are this package's concern.
        foreach (var dll in new[] { "libEGL.dll", "libGLESv2.dll" }.Select(f => Path.Combine(dir, f)))
        {
            foreach (var import in ReadImports(dll))
            {
                if (vendored.Contains(import) || ProvidedByWindows.Contains(import) ||
                    import.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase))
                    continue;
                missing.Add($"{Path.GetFileName(dll)} imports {import}");
            }
        }

        Assert.True(missing.Count == 0, $"{rid}: not vendored: {string.Join(", ", missing)}");
    }

    private static List<string> ReadImports(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var table = pe.PEHeaders.PEHeader!.ImportTableDirectory;
        var names = new List<string>();
        if (table.Size == 0)
            return names;

        // Each IMAGE_IMPORT_DESCRIPTOR is 20 bytes; the DLL name RVA is at offset 12, and an
        // all-zero descriptor ends the table.
        for (var rva = table.RelativeVirtualAddress; ; rva += 20)
        {
            var descriptor = pe.GetSectionData(rva).GetReader();
            descriptor.Offset = 12;
            var nameRva = descriptor.ReadInt32();
            if (nameRva == 0)
                return names;

            var name = pe.GetSectionData(nameRva).GetReader();
            var bytes = new List<byte>();
            for (byte b; (b = name.ReadByte()) != 0;)
                bytes.Add(b);
            names.Add(Encoding.ASCII.GetString(bytes.ToArray()));
        }
    }
}
