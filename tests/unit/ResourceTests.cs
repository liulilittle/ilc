using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

// RES-1 (iron rule 18): embedded-manifest-resource transpile-chain unit tests.
//
//   * LoadManifestResources_* -- MetadataLoader reads the resx fixture's
//     ManifestResource table: name enumeration + the exact payload content
//     (the .resources blob magic + hello.bin bytes), embedded-only filtering
//     and error tolerance (no resource section -> empty).
//   * RenderResourcePoolHeader_* -- the T3 g_strPool-pattern emission: the
//     data file carries one static byte array per payload, a g_resPool[]
//     entry table (name/data/length) and the il2cpp_resource_pool_fill()
//     registration call. The runtime half of the chain is verified by
//     tests/runtime/test_resources.cpp against these same bytes.
public class ResourceTests
{
    [Fact]
    public void LoadManifestResources_EnumeratesNames()
    {
        var resources = MetadataLoader.LoadManifestResources(FixtureResxPath());
        Assert.Equal(new[] { "resx.Strings.resources", "hello.bin" },
                     resources.Select(r => r.Name));
    }

    [Fact]
    public void LoadManifestResources_ExactPayloadContent()
    {
        var resources = MetadataLoader.LoadManifestResources(FixtureResxPath());
        var strings = resources.Single(r => r.Name == "resx.Strings.resources");
        var hello = resources.Single(r => r.Name == "hello.bin");

        // The .resources blob starts with the 0xBEEFCACE magic (LE CE CA EF BE).
        Assert.Equal(265, strings.Length);
        Assert.Equal(new byte[] { 0xCE, 0xCA, 0xEF, 0xBE }, strings.Data.Take(4));

        // hello.bin is the raw 27-byte payload.
        Assert.Equal(27, hello.Length);
        Assert.Equal("HELLO WORLD BINARY RESOURCE",
                     System.Text.Encoding.ASCII.GetString(hello.Data));
    }

    [Fact]
    public void LoadManifestResources_PayloadIsTheBlobPayloadNotTheFramedBlob()
    {
        // The PE resource blob is int32 length prefix + payload; the returned
        // data MUST be the payload (what GetManifestResourceStream returns) --
        // it must NOT start with the 4-byte length (265 = 0x00000109 -> 09 01 00 00).
        var resources = MetadataLoader.LoadManifestResources(FixtureResxPath());
        var strings = resources.Single(r => r.Name == "resx.Strings.resources");
        Assert.NotEqual(new byte[] { 0x09, 0x01, 0x00, 0x00 }, strings.Data.Take(4));
        Assert.Equal(unchecked((int)0xBEEFCACE),
                     System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(strings.Data));
    }

    [Fact]
    public void RenderResourcePoolHeader_EmitsTableAndFill()
    {
        var resources = new[]
        {
            new ManifestResourceDef("resx.Strings.resources", new byte[] { 0xCE, 0xCA, 0xEF, 0xBE }),
            new ManifestResourceDef("hello.bin", new byte[] { 0x48, 0x49 }),
        };
        var text = Program.RenderResourcePoolHeader(resources);

        Assert.Contains("#include \"il2cpp_resources.h\"", text);
        Assert.Contains("inline const uint8_t g_resPool_data_0[] = { 0xCE,0xCA,0xEF,0xBE };", text);
        Assert.Contains("inline const uint8_t g_resPool_data_1[] = { 0x48,0x49 };", text);
        Assert.Contains("\"resx.Strings.resources\", g_resPool_data_0, 4", text);
        Assert.Contains("\"hello.bin\", g_resPool_data_1, 2", text);
        Assert.Contains("inline constexpr int32_t g_resPoolCount = 2;", text);
        Assert.Contains("il2cpp_resources_register(g_resPool, g_resPoolCount);", text);
    }

    [Fact]
    public void RenderResourcePoolHeader_EmptyPoolRendersNothing()
    {
        Assert.Equal("", Program.RenderResourcePoolHeader(Array.Empty<ManifestResourceDef>()));
    }

    [Fact]
    public void RenderResourcePoolHeader_EscapesNames()
    {
        var resources = new[]
        {
            new ManifestResourceDef("quote\"name", new byte[] { 0x01 }),
        };
        var text = Program.RenderResourcePoolHeader(resources);
        Assert.Contains("{ \"quote\\\"name\", g_resPool_data_0, 1 },", text);
    }

    // The test host's working directory is the bin output directory; walk up
    // from AppContext.BaseDirectory to the repo root to locate the fixture
    // (same convention as MetadataLoaderTests.FixturePath).
    private static string FixtureResxPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "out", "resx.exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "fixture resx.exe not found; run scripts/build-fixtures.ps1 first (expects repo root/tests/fixtures/out/resx.exe)");
    }
}
