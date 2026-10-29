using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using Xunit;

public class CodeGeneratorI64SeedTests
{
    //--------------------------------------------------------------------------
    // impl-i64seed (floatfix 7b0ee161 residual): the emitter locKind param
    // seeding loop seeds "d" for float/double params but has NO arm for the
    // int64/uint64 ("l"/"q") by-value params. An unseeded long param falls
    // to the "val" default, ExprIsInt64(LocExpr) is false, and every
    // int32/int64-ambiguous binary path folds at INT32 width:
    //   * param/param div -> il2cpp_checked_div(int32_t,int32_t) truncation;
    //   * param/param rem -> il2cpp_checked_rem, same;
    //   * param shift count -> the 5-bit (31) mask, never 63;
    //   * ulong param >> n -> uint32 cast + 31 mask (bitopcase G2/G5 class);
    //   * ulong param > ulong param -> uint32 compare (bitopcase G3 class);
    //   * unchecked long param + long param -> raw C++ add (no uint64 wrap);
    //   * long param == -1L -> the uint32 negative-literal compare.
    // A long LOCAL is covered (the IL local signature is authoritative "l")
    // and a long constant is recognized directly (Int64ConstExpr), which is
    // why bitopcase/int64deep passed despite the gap. Fixture: the real
    // net48 csc-built probe-i64seed case exe (CaseExePath convention).
    //--------------------------------------------------------------------------
    [Fact]
    public void Emit_LongParamDiv_KeepsFullWidthDivision()
    {
        var cpp = GenerateProbe();

        Assert.Contains("((arg0) / (arg1))", cpp);
        Assert.DoesNotContain("il2cpp::il2cpp_checked_div(arg0, arg1)", cpp);
        Assert.DoesNotContain("il2cpp::il2cpp_checked_div(static_cast<int32_t>(arg0)", cpp);
    }

    [Fact]
    public void Emit_LongParamRem_KeepsFullWidthRemainder()
    {
        var cpp = GenerateProbe();

        Assert.Contains("((arg0) % (arg1))", cpp);
        Assert.DoesNotContain("il2cpp::il2cpp_checked_rem(arg0, arg1)", cpp);
    }

    [Fact]
    public void Emit_LongParamShiftCount_UsesSixBitMask()
    {
        var cpp = GenerateProbe();

        // csc lowers a long shift with its own `and 63` on the count; the
        // emitter wraps ANOTHER mask around it -- the discriminator is the
        // outer mask: 63 for a long left operand, 31 was the int32 fold.
        var shl = ExtractMethodBody(cpp, "ProbeI64Seed_ShlLL");
        Assert.NotNull(shl);
        Assert.Contains("& 63", shl!);
        Assert.DoesNotContain("& 31", shl);
        var shr = ExtractMethodBody(cpp, "ProbeI64Seed_ShrLL");
        Assert.NotNull(shr);
        Assert.Contains("& 63", shr!);
        Assert.DoesNotContain("& 31", shr);
    }

    [Fact]
    public void Emit_UlongParamShrUn_UsesUint64AndSixBitMask()
    {
        var cpp = GenerateProbe();

        Assert.Contains("static_cast<int64_t>(static_cast<uint64_t>(arg0) >>", cpp);
        Assert.DoesNotContain("static_cast<int32_t>(static_cast<uint32_t>(arg0) >>", cpp);
        var body = ExtractMethodBody(cpp, "ProbeI64Seed_ShrULL");
        Assert.NotNull(body);
        Assert.DoesNotContain("& 31", body!);
    }

    [Fact]
    public void Emit_UlongParamUnsignedCompare_UsesUint64()
    {
        var cpp = GenerateProbe();

        Assert.Contains("(static_cast<uint64_t>(arg0) > static_cast<uint64_t>(arg1))", cpp);
        Assert.DoesNotContain("(static_cast<uint32_t>(arg0) > static_cast<uint32_t>(arg1))", cpp);
        Assert.Contains("(static_cast<uint64_t>(arg0) < static_cast<uint64_t>(arg1))", cpp);
        Assert.DoesNotContain("(static_cast<uint32_t>(arg0) < static_cast<uint32_t>(arg1))", cpp);
    }

    [Fact]
    public void Emit_LongParamUncheckedAdd_UsesUint64Wrap()
    {
        var cpp = GenerateProbe();

        Assert.Contains("(static_cast<int64_t>(static_cast<uint64_t>(arg0) + static_cast<uint64_t>(arg1)))", cpp);
    }

    [Fact]
    public void Emit_LongParamEqNegativeLiteral_UsesUint64()
    {
        var cpp = GenerateProbe();

        Assert.Contains("(static_cast<uint64_t>(arg0) == static_cast<uint64_t>(-1LL))", cpp);
        Assert.DoesNotContain("(static_cast<uint32_t>(arg0) == static_cast<uint32_t>(-1LL))", cpp);
    }

    [Fact]
    public void Emit_LongParamPhiMergeCopy_DeclaresInt64()
    {
        // long m = pick ? a : b -- the branch-merge temp copies a long param
        // (`t1 = arg1`); the copy target must declare int64_t, never the
        // "val" fallback int32_t (which truncates the copy).
        var cpp = GenerateProbe();

        var body = ExtractMethodBody(cpp, "ProbeI64Seed_MergeLL");
        Assert.NotNull(body);
        Assert.DoesNotMatch(@"int32_t t\d+;", body!);
        Assert.Matches(@"int64_t t\d+;", body);
    }

    //--------------------------------------------------------------------------
    // Test harness.
    //--------------------------------------------------------------------------
    private static string GenerateProbe()
    {
        var exe = CaseExePath("probe-i64seed");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            return File.ReadAllText(Path.Combine(dir, "src", "Cases_ProbeI64Seed.cpp"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string? ExtractMethodBody(string cpp, string methodName)
    {
        // The method-table row ALSO carries the name -- anchor on the
        // IL2CPP_NOINLINE definition line, never the first occurrence.
        var sig = Regex.Match(cpp, @"IL2CPP_NOINLINE \w+ \w+" + Regex.Escape(methodName) + @"\w*\(");
        if (!sig.Success) return null;
        var open = cpp.IndexOf('{', sig.Index);
        if (open < 0) return null;
        var end = cpp.IndexOf("\n}", open, StringComparison.Ordinal);
        return end < 0 ? null : cpp.Substring(open, end - open);
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
