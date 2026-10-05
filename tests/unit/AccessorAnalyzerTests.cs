using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// AccessorAnalyzerTests -- property-folding pre-scan (iron rule 2j).
//
// The analyzer recognizes SIMPLE accessor shapes (get: ldarg.0; ldfld; ret /
// set: ldarg.0; ldarg.1; stfld; ret, static variants) on NON-virtual
// get_*/set_* methods whose field lives in the SAME type. Everything else
// (computed getters, virtuals, cross-type fields) must NOT fold -- those
// accessors keep their emitted functions.
//
// The fixture (tests/fixtures/props) contains: manual accessor pair,
// auto property, static auto property, computed getter (Doubled), virtual
// property (Tag, incl. an override), derived non-virtual property (Extra).
//------------------------------------------------------------------------------
public class AccessorAnalyzerTests
{
    // The MetadataReader borrows its backing blocks from the PEReader; call
    // sites keep using `md` after the tuple is destructured, so the handles
    // must outlive the helper (dangling-reader class defect, see
    // TypeEmitterNestedTests.Load). Retain for the process lifetime.
    private static readonly List<PEReader> s_retainedReaders = new();

    private static (PEReader Pe, MetadataReader Md, List<TypeDef> Types) OpenFixture()
    {
        var path = FixturePath();
        var pe = new PEReader(new MemoryStream(File.ReadAllBytes(path), writable: false));
        var md = pe.GetMetadataReader();
        var types = MetadataLoader.LoadAssembly(path);
        lock (s_retainedReaders)
        {
            s_retainedReaders.Add(pe);
        }
        return (pe, md, types);
    }

    [Fact]
    public void Scan_ManualAccessors_AreFolded()
    {
        var (_, md, types) = OpenFixture();
        using var pe2 = new PEReader(File.OpenRead(FixturePath()));
        var accessors = AccessorAnalyzer.Scan(pe2, md, types);
        var counter = types.Single(t => t.FullName == "AuditFix.Counter");
        var getToken = TokenOf(md, counter, "get_Value");
        var setToken = TokenOf(md, counter, "set_Value");
        Assert.True(accessors.ContainsKey(getToken), "get_Value must be recognized as a foldable accessor");
        Assert.True(accessors.ContainsKey(setToken), "set_Value must be recognized as a foldable accessor");
        Assert.True(accessors[getToken].IsGetter);
        Assert.False(accessors[setToken].IsGetter);
        Assert.Equal("_value", accessors[getToken].FieldName);
    }

    [Fact]
    public void Scan_AutoAndStaticProperties_AreFolded()
    {
        var (_, md, types) = OpenFixture();
        using var pe2 = new PEReader(File.OpenRead(FixturePath()));
        var accessors = AccessorAnalyzer.Scan(pe2, md, types);
        var counter = types.Single(t => t.FullName == "AuditFix.Counter");
        Assert.True(accessors.ContainsKey(TokenOf(md, counter, "get_Auto")));
        Assert.True(accessors.ContainsKey(TokenOf(md, counter, "set_Auto")));
        Assert.True(accessors.ContainsKey(TokenOf(md, counter, "get_Shared")));
        Assert.True(accessors[TokenOf(md, counter, "get_Shared")].IsStatic, "static getter must be marked static");
        Assert.True(accessors.ContainsKey(TokenOf(md, counter, "set_Shared")));
    }

    [Fact]
    public void Scan_ComputedGetter_IsNotFolded()
    {
        var (_, md, types) = OpenFixture();
        using var pe2 = new PEReader(File.OpenRead(FixturePath()));
        var accessors = AccessorAnalyzer.Scan(pe2, md, types);
        var counter = types.Single(t => t.FullName == "AuditFix.Counter");
        Assert.False(accessors.ContainsKey(TokenOf(md, counter, "get_Doubled")),
            "a computed getter (ldarg.0; ldfld; ldc; mul; ret) must NOT fold");
    }

    [Fact]
    public void Scan_VirtualAccessors_AreNotFolded()
    {
        var (_, md, types) = OpenFixture();
        using var pe2 = new PEReader(File.OpenRead(FixturePath()));
        var accessors = AccessorAnalyzer.Scan(pe2, md, types);
        var counter = types.Single(t => t.FullName == "AuditFix.Counter");
        var sub = types.Single(t => t.FullName == "AuditFix.SubCounter");
        Assert.False(accessors.ContainsKey(TokenOf(md, counter, "get_Tag")), "virtual getter must NOT fold");
        Assert.False(accessors.ContainsKey(TokenOf(md, counter, "set_Tag")), "virtual setter must NOT fold");
        Assert.False(accessors.ContainsKey(TokenOf(md, sub, "get_Tag")), "override getter must NOT fold");
        // The derived NON-virtual property folds normally.
        Assert.True(accessors.ContainsKey(TokenOf(md, sub, "get_Extra")));
        Assert.True(accessors.ContainsKey(TokenOf(md, sub, "set_Extra")));
    }

    [Fact]
    public void Fold_CallSite_GetterFoldsToFieldExpr()
    {
        // Real end-to-end: fold the Main method's body with the accessor table
        // -- a getter CALL must produce a FieldExpr (no call, no temp).
        var (pe, md, types) = OpenFixture();
        using (pe)
        {
            var accessors = AccessorAnalyzer.Scan(pe, md, types);
            var props = types.Single(t => t.FullName == "AuditFix.Props");
            var main = props.Methods.Single(m => m.Name == "Main");
            var bodyInfo = MethodBodyReader.Read(pe, md, props, main);
            var insns = IlDecoder.DecodeMethod(bodyInfo.IlBytes, md);
            var stmts = StackFold.Fold(insns, new MethodContext
            {
                ArgCount = bodyInfo.ArgCount,
                LocNames = bodyInfo.LocNames,
                ReturnsVoid = bodyInfo.ReturnsVoid,
            }, md, types, accessors);

            // The folded setter `c.Value = 41` becomes a FieldAssignStmt whose
            // target field is the Counter struct's _value member.
            var fieldAssigns = stmts.OfType<FieldAssignStmt>().Where(f => f.FieldCppName == "_value").ToList();
            Assert.NotEmpty(fieldAssigns);
            // The folded getter `c.Value + 1` reads the field -- a FieldExpr
            // appears somewhere inside an expression (no get_Value call name).
            Assert.DoesNotContain(stmts, s => s is CallStmt cs && cs.Function.Contains("get_Value"));
            Assert.DoesNotContain(stmts, s => s is CallStmt cs && cs.Function.Contains("set_Value"));
            // And the folding must NOT swallow the computed property call.
            Assert.Contains(stmts, s => s is CallStmt cs && cs.Function.Contains("call_") && s.SourceOpName == "call");
        }
    }

    [Fact]
    public void Fold_CallSite_StaticSetterFoldsToAssign()
    {
        var (pe, md, types) = OpenFixture();
        using (pe)
        {
            var accessors = AccessorAnalyzer.Scan(pe, md, types);
            var props = types.Single(t => t.FullName == "AuditFix.Props");
            var main = props.Methods.Single(m => m.Name == "Main");
            var bodyInfo = MethodBodyReader.Read(pe, md, props, main);
            var insns = IlDecoder.DecodeMethod(bodyInfo.IlBytes, md);
            var stmts = StackFold.Fold(insns, new MethodContext
            {
                ArgCount = bodyInfo.ArgCount,
                LocNames = bodyInfo.LocNames,
                ReturnsVoid = bodyInfo.ReturnsVoid,
            }, md, types, accessors);

            // `Counter.Shared = 7` folds into a static-field assignment.
            Assert.Contains(stmts, s => s is AssignStmt a && a.Target.Contains("Shared"));
            // No call to the static setter remains.
            Assert.DoesNotContain(stmts, s => s is CallStmt cs && cs.Function.Contains("set_Shared"));
        }
    }

    private static int TokenOf(MetadataReader md, TypeDef t, string methodName)
    {
        foreach (var tdh in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdh);
            if (md.GetFullName(tdh) != t.FullName) continue;
            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if (md.GetString(m.Name) == methodName)
                    return MetadataTokens.GetToken(mh);
            }
        }
        throw new InvalidOperationException($"method {t.FullName}.{methodName} not found");
    }

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "out", "props.dll");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "fixture props.dll not found; run scripts/build-fixtures.ps1 first");
    }
}
