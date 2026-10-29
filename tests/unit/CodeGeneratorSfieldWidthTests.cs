using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using System.Text.RegularExpressions;
using Xunit;

//------------------------------------------------------------------------------
// CodeGeneratorSfieldWidthTests -- a compiler temp that receives a STATIC
// FIELD read must be declared with the field's real width.
//
// StackFold materializes the pre-store snapshot temp for a static-field
// postfix increment (`ldsfld SF; dup; ldc.i4.1; add; stsfld SF`) as
// `tN = <static field read>;` (StackFold.SnapshotStackReads). The emitter's
// locKind pre-seed loop classified StaticFieldExpr as the "val" default
// (int32_t) except for ref / Guid / decimal / IntPtr / fnptr fields, so a
// 64-bit static field's preserved old value was truncated:
//     int32_t t1; t1 = Cases_PostInc2_sf_BIG;   // 5000000000 -> 705032704
// probe-postfixinc2 RED: [sfieldl-big-assign] / [sfieldl-big-arg] differed
// from net48 while every small-value shape already matched.
// Fixture: the net48 csc-built probe-postfixinc2 case exe.
//------------------------------------------------------------------------------
public class CodeGeneratorSfieldWidthTests
{
    [Fact]
    public void Emit_LongStaticFieldSnapshotTemp_DeclaresInt64()
    {
        var body = GenerateMainBody();

        var read = Regex.Match(body, @"(?m)^\s*(tsnap\d+) = Cases_PostInc2_sf_BIG;");
        Assert.True(read.Success, "expected the postfix-increment snapshot temp for BIG");
        var temp = read.Groups[1].Value;
        Assert.Contains($"int64_t {temp};", body);
        Assert.DoesNotContain($"int32_t {temp};", body);
    }

    [Fact]
    public void Emit_IntStaticFieldSnapshotTemp_StaysInt32()
    {
        // Control: the int32 field's snapshot temp keeps the int32 slot (the
        // seed must not widen every static-field read).
        var body = GenerateMainBody();

        var read = Regex.Match(body, @"(?m)^\s*(tsnap\d+) = Cases_PostInc2_sf_SF;");
        Assert.True(read.Success, "expected the postfix-increment snapshot temp for SF");
        var temp = read.Groups[1].Value;
        Assert.Contains($"int32_t {temp};", body);
    }

    //--------------------------------------------------------------------------
    // Test harness (the CodeGeneratorI64SeedTests CaseExePath convention).
    //--------------------------------------------------------------------------
    private static string GenerateMainBody()
    {
        var exe = CaseExePath("probe-postfixinc2");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_PostInc2.cpp"));
            var sig = Regex.Match(cpp, @"IL2CPP_NOINLINE \w+ \w*Cases_PostInc2_Main\w*\(");
            Assert.True(sig.Success, "Cases.PostInc2::Main not found in the generated translation unit");
            var open = cpp.IndexOf('{', sig.Index);
            Assert.True(open > 0);
            var end = cpp.IndexOf("\n}", open, StringComparison.Ordinal);
            Assert.True(end > open);
            return cpp.Substring(open, end - open);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string CaseExePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "cases", name, "obj", "Release", "net48", name + ".exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"fixture {name}.exe not found; build the case first (dotnet build tests/cases/{name}/{name}.csproj -c Release)");
    }
}
