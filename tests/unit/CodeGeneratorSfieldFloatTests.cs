using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using System.Text.RegularExpressions;
using Xunit;

//------------------------------------------------------------------------------
// CodeGeneratorSfieldFloatTests -- STATIC double/float fields must keep their
// real storage width.
//
// MetadataLoader.DecodeExtendedStaticType (the STATIC field arm of
// DecodeFieldSignature) classified ELEMENT_TYPE_R4 (0x0C) / R8 (0x0D) as
// FieldTypeKind.Int32, so TypeEmitter.StaticFieldSemantics fell through to
// StaticFieldSemantic.Value and BOTH the .cpp definition and the generated.h
// extern declared int32_t storage. Every store/load then truncated the value:
//
//     static double D = 3.5;  D++;   ->  D == 3   (net48: 4.5)
//     static float  F = 2.5f; F++;   ->  F == 3   (net48: 3.5)
//
// probe-sfieldfloat RED (before the fix): 23 of 29 records differed from the
// net48 oracle (probe-sfieldfloat.cs). The instance-field and local paths were
// already width-correct (FieldTypeKind.Float/Double), which is why only the
// static surface broke.
//
// Fixture: the net48 csc-built probe-sfieldfloat case exe (CaseExePath
// convention -- scripts/build-unit-fixtures.ps1 discovers it automatically).
//------------------------------------------------------------------------------
public class CodeGeneratorSfieldFloatTests
{
    [Fact]
    public void StaticStorage_DoubleField_DeclaresDouble()
    {
        var cpp = Generate();

        Assert.Contains("double Cases_SFieldFloat_sf_D = 0;", cpp);
        Assert.DoesNotContain("int32_t Cases_SFieldFloat_sf_D", cpp);
        // initializer / readonly / cross-method accumulator fields too.
        Assert.Contains("double Cases_SFieldFloat_sf_DInit = 0;", cpp);
        Assert.Contains("double Cases_SFieldFloat_sf_DRO = 0;", cpp);
        Assert.Contains("double Cases_SFieldFloat_sf_s_acc = 0;", cpp);
    }

    [Fact]
    public void StaticStorage_FloatField_DeclaresFloat()
    {
        var cpp = Generate();

        Assert.Contains("float Cases_SFieldFloat_sf_F = 0;", cpp);
        Assert.DoesNotContain("int32_t Cases_SFieldFloat_sf_F", cpp);
        Assert.Contains("float Cases_SFieldFloat_sf_FInit = 0;", cpp);
        Assert.Contains("float Cases_SFieldFloat_sf_s_facc = 0;", cpp);
    }

    [Fact]
    public void StaticStorage_IntControl_StaysInt32()
    {
        // The seed must not widen every static field: the int static keeps the
        // int32 slot (and the long static its int64 slot).
        var cpp = Generate();

        Assert.Contains("int32_t Cases_SFieldFloat_sf_I = 0;", cpp);
        Assert.Contains("int64_t Cases_SFieldFloat_sf_SL = 0;", cpp);
    }

    [Fact]
    public void GeneratedHeader_ExternMatchesStorageWidth()
    {
        // StaticFieldCppType feeds the generated.h extern; it must agree with
        // the EmitStaticStorage definition (decimalcase cmake FAIL rule:
        // redefinition with a different type).
        var (cpp, header) = GenerateWithHeader();

        Assert.Contains("extern double Cases_SFieldFloat_sf_D;", header);
        Assert.Contains("extern float Cases_SFieldFloat_sf_F;", header);
        Assert.Contains("extern int32_t Cases_SFieldFloat_sf_I;", header);
        Assert.Contains("double Cases_SFieldFloat_sf_D = 0;", cpp);
    }

    [Fact]
    public void PostfixIncrementSnapshotTemp_DeclaresDouble()
    {
        // `D++` snapshots the pre-store value into a tsnap temp (StackFold
        // SnapshotStackReads). The temp must be declared with the field's real
        // width -- an int32_t temp truncated the preserved old value (3.5 -> 3).
        var cpp = Generate();

        var read = Regex.Match(cpp, @"(?m)^\s*(tsnap\d+) = Cases_SFieldFloat_sf_D;");
        Assert.True(read.Success, "expected the postfix-increment snapshot temp for D");
        var temp = read.Groups[1].Value;
        Assert.Contains($"double {temp};", cpp);
        Assert.DoesNotContain($"int32_t {temp};", cpp);

        var readF = Regex.Match(cpp, @"(?m)^\s*(tsnap\d+) = Cases_SFieldFloat_sf_F;");
        Assert.True(readF.Success, "expected the postfix-increment snapshot temp for F");
        Assert.Contains($"double {readF.Groups[1].Value};", cpp);
    }

    [Fact]
    public void StaticDoubleOperand_Division_KeepsFloatingWidth()
    {
        // A static field read stays UN-materialized on the IR stack: `D / k`
        // (non-constant int divisor) must be a floating division. The int32
        // checked_div path truncates both operands (net48: 3.5/2 = 1.75).
        var cpp = Generate();

        Assert.DoesNotContain("il2cpp::il2cpp_checked_div(Cases_SFieldFloat_sf_D", cpp);
        Assert.DoesNotContain("il2cpp::il2cpp_checked_div(Cases_SFieldFloat_sf_F", cpp);
        Assert.Contains("static_cast<double>(Cases_SFieldFloat_sf_D) / static_cast<double>(loc2)", cpp);
    }

    //--------------------------------------------------------------------------
    // Test harness (the CodeGeneratorSfieldWidthTests CaseExePath convention).
    //--------------------------------------------------------------------------
    private static string Generate() => GenerateWithHeader().Cpp;

    private static (string Cpp, string Header) GenerateWithHeader()
    {
        var exe = CaseExePath("probe-sfieldfloat");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_SFieldFloat.cpp"));
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            return (cpp, header);
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
