using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

public class MethodEmitterTests
{
    [Fact]
    public void SigHash_IsStable()
    {
        Assert.Equal(SigHash.Hash("System.Int32 Add(System.Int32,System.Int32)"),
                     SigHash.Hash("System.Int32 Add(System.Int32,System.Int32)"));
        Assert.NotEqual(SigHash.Hash("System.Int32 Add(System.Int32)"),
                        SigHash.Hash("System.Int32 Add(System.Int32,System.Int32)"));
    }

    [Fact]
    public void EmitMethod_Add_ContainsBinaryOp()
    {
        // Uses the Task 3 fixtures: Calc.Program.Add → "return arg0 + arg1;"
        var src = MethodEmitter.EmitMethod("Calc_Program_Add_" + SigHash.Hash("x"),
            new[] { "arg0", "arg1" }, "int32_t",
            "return arg0 + arg1;");
        Assert.Contains("return arg0 + arg1;", src);
        Assert.Contains("IL2CPP_NOINLINE", src);
    }

    [Fact]
    public void CppWriter_LineAligned_CommentColumnIsFixed()
    {
        // Trailing-comment // always lands at column 99 (1-based), independent of the
        // indentation level (0 and 2-level indents align at the same column).
        foreach (var indentLevel in new[] { 0, 2 })
        {
            var w = new CppWriter();
            for (var i = 0; i < indentLevel; i++) w.Open();
            w.LineAligned("x = 1;", "comment");
            var line = w.ToString().TrimEnd('\r', '\n');
            Assert.Equal(99, line.IndexOf("// comment") + 1);
        }
    }

    [Fact]
    public void Resolve_SanitizesCtorMemberReferenceName()
    {
        using var pe = new PEReader(File.OpenRead(FixturePath()));
        var md = pe.GetMetadataReader();
        foreach (var mrh in md.MemberReferences)
        {
            var mr = md.GetMemberReference(mrh);
            if (md.GetString(mr.Name) != ".ctor") continue;
            var resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(mrh), Array.Empty<TypeDef>());
            var name = resolved.CppName;
            // .ctor → _ctor: after sanitization the method name must not contain illegal
            // C++ identifier characters and must be a valid C++ identifier overall.
            Assert.DoesNotContain(".ctor", name);
            Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", name);
            Assert.DoesNotContain(name, c => "`.+<>,".Contains(c));
            Assert.Contains("_ctor_", name);
            return;
        }
        Assert.Fail("fixture calc.dll has no .ctor MemberReference");
    }

    [Fact]
    public void Resolve_ConsoleWriteLine_MapsToIcall()
    {
        using var pe = new PEReader(File.OpenRead(FixturePath()));
        var md = pe.GetMetadataReader();
        foreach (var mrh in md.MemberReferences)
        {
            var mr = md.GetMemberReference(mrh);
            if (md.GetString(mr.Name) != "WriteLine") continue;
            var resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(mrh), Array.Empty<TypeDef>());
            Assert.Equal("il2cpp::runtime::icall_console_write_line_i", resolved.CppName);
            Assert.True(resolved.IsConsoleWriteLine);
            Assert.Equal(1, resolved.ParamCount);
            return;
        }
        Assert.Fail("fixture calc.dll has no WriteLine MemberReference");
    }

    [Fact]
    public void Resolve_ConsoleWriteAndEmptyWriteLine_MapsToIcalls()
    {
        // convw fixture: Console.Write(int) → icall_console_write_i; the empty
        // Console.WriteLine() → icall_console_write_n.
        // Before the fix Write fell back to the default naming
        // (System_Console_Write_<hash>) → link-time undefined symbol (RED).
        using var pe = new PEReader(File.OpenRead(FixturePath("convw")));
        var md = pe.GetMetadataReader();
        var write = md.MemberReferences.First(h => md.GetString(md.GetMemberReference(h).Name) == "Write");
        var writeLine = md.MemberReferences.First(h => md.GetString(md.GetMemberReference(h).Name) == "WriteLine");

        var w = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(write), Array.Empty<TypeDef>());
        Assert.Equal("il2cpp::runtime::icall_console_write_i", w.CppName);
        Assert.True(w.IsConsoleWriteLine);
        Assert.Equal(1, w.ParamCount);

        var wl = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(writeLine), Array.Empty<TypeDef>());
        Assert.Equal("il2cpp::runtime::icall_console_write_line_n", wl.CppName);
        Assert.True(wl.IsConsoleWriteLine);
        Assert.Equal(0, wl.ParamCount);
    }

    // The test host's working directory is the bin output directory
    // (E:\il2cpp\tests\unit\bin\Debug\net8.0), so relative paths cannot resolve
    // fixtures directly. Walk up from AppContext.BaseDirectory to the repo root to
    // locate the fixed relative path.
    private static string FixturePath(string name = "calc")
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "out", name + ".dll");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"fixture {name}.dll not found; run scripts/build-fixtures.ps1 first (expects repo root/tests/fixtures/out/{name}.dll)");
    }
}
