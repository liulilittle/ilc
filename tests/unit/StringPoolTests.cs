using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Opt;
using Xunit;

//------------------------------------------------------------------------------
// String-pooling pass (design: docs/architecture/string-pooling-design.md
// §2.1 -- layer ① compile-time dedup).
//
// The pass is CONSERVATIVE: it never changes semantics.
//   * Constant concat folding (former layer ②) is REMOVED: folding
//     String.Concat("a","b") into a StringConstExpr breaks reference
//     identity -- string.Concat returns a FRESH (non-interned) instance
//     while a literal is the intern-pool instance. ReferenceEquals observes
//     the difference (net48 probe: ReferenceEquals(string.Concat("intern-",
//     "chain"), "intern-chain") == false). Every Concat call is preserved
//     (emitted to il2cpp_string_concat_many -- fresh allocation).
//   * Collect registers every StringConstExpr occurrence (site) into the
//     assembly-level pool; identical content maps to ONE id.
//------------------------------------------------------------------------------
public class StringPoolTests
{
    private static StringConstExpr S(string v) => new() { Value = v };
    private static LocExpr L(string n) => new() { Name = n };
    private static RetStmt Ret(Expr v) => new() { Value = v };

    private static CallExpr Concat(params Expr[] args)
    {
        var c = new CallExpr
        {
            Function = "call_0x0A00000A", // MemberRef placeholder (StackFold shape)
            ReturnsRef = true,
            Resolved = new ResolvedMethod(
                StringPoolPass.ConcatManyCppName, args.Length, false, true, true,
                args.Select(_ => true).ToArray(), false),
        };
        foreach (var a in args) c.Args.Add(a);
        return c;
    }

    private static CallExpr OtherIcall(params Expr[] args)
    {
        var c = new CallExpr
        {
            Function = "call_0x0A00000B",
            ReturnsRef = true,
            Resolved = new ResolvedMethod(
                "il2cpp::runtime::icall_string_to_upper", args.Length, false, true, true,
                args.Select(_ => true).ToArray(), false),
        };
        foreach (var a in args) c.Args.Add(a);
        return c;
    }

    private static AssignStmt Assign(Expr value, string target = "t1") =>
        new() { Target = target, Value = value };

    private static DelegateNewExpr DelegateNew(Expr target) => new() { MethodToken = 0x06000005, Target = target };

    //--------------------------------------------------------------------------
    // Layer ① -- assembly-level dedup: same content -> single pool entry.
    //--------------------------------------------------------------------------
    [Fact]
    public void SameContent_AcrossMethods_SinglePoolEntry()
    {
        // "hello" appears in two different methods (sites in different stmts).
        var m1 = new List<Stmt> { Assign(S("hello")) };
        var m2 = new List<Stmt> { Ret(S("hello")) };
        var (pool, _) = StringPoolPass.Optimize(new[] { m1, m2 });

        Assert.Equal(1, pool.LiteralCount);
        Assert.Equal(2, pool.TotalSites);
        Assert.Equal(pool.IdOf("hello"), pool.IdOf("hello"));
    }

    [Fact]
    public void DifferentContent_GetsDifferentId()
    {
        var (pool, _) = StringPoolPass.Optimize(new[]
        {
            new List<Stmt> { Assign(S("alpha")) },
            new List<Stmt> { Assign(S("beta")) },
        });

        Assert.Equal(2, pool.LiteralCount);
        Assert.NotEqual(pool.IdOf("alpha"), pool.IdOf("beta"));
    }

    [Fact]
    public void EmptyString_GetsItsOwnPoolEntry()
    {
        var (pool, _) = StringPoolPass.Optimize(new[]
        {
            new List<Stmt> { Assign(S("")) },
            new List<Stmt> { Assign(S("x")) },
        });

        Assert.Equal(2, pool.LiteralCount);
        Assert.Equal("", pool.LiteralAt(pool.IdOf("")));
        Assert.NotEqual(pool.IdOf(""), pool.IdOf("x"));
    }

    [Fact]
    public void Collect_CountsEverySite()
    {
        var (pool, _) = StringPoolPass.Optimize(new[]
        {
            new List<Stmt> { Assign(S("x")), Assign(S("x"), "t2"), Ret(S("y")) },
        });

        Assert.Equal(3, pool.TotalSites);
        Assert.Equal(2, pool.LiteralCount);
    }

    [Fact]
    public void Literals_NestedInsideCallArgs_AreCollected()
    {
        // Console.WriteLine("hi") -> CallExpr arg is the StringConstExpr.
        var (pool, _) = StringPoolPass.Optimize(new[]
        {
            new List<Stmt> { Assign(OtherIcall(S("hi"))) },
        });

        Assert.Equal(1, pool.LiteralCount);
        Assert.Equal("hi", pool.LiteralAt(pool.IdOf("hi")));
    }

    [Fact]
    public void Literals_NestedInDelegateNewTarget_AreCollected()
    {
        // `new Action<string>("abc".Trim)` -> DelegateNewExpr.Target is a
        // StringConstExpr (the bound instance). The emitter recurses into
        // dn.Target (CodeGenerator EmitExpr DelegateNewExpr arm), so the
        // literal IS a real ldstr site -- Collect must pool it (D1: the pool
        // is fully built at collect time, never grown by the emitter).
        var (pool, _) = StringPoolPass.Optimize(new[]
        {
            new List<Stmt> { Assign(DelegateNew(S("abc"))) },
        });

        Assert.Equal(1, pool.LiteralCount);
        Assert.Equal(1, pool.TotalSites);
        Assert.Equal("abc", pool.LiteralAt(pool.IdOf("abc")));
    }

    [Fact]
    public void Literals_NestedInDelegateNewTarget_CrossMethodDedup()
    {
        // Same literal as a delegate target in two methods -> ONE pool entry
        // (cross-method dedup applies to every site, target or argument).
        var m1 = new List<Stmt> { Assign(DelegateNew(S("dup"))) };
        var m2 = new List<Stmt> { Ret(S("dup")) };
        var (pool, _) = StringPoolPass.Optimize(new[] { m1, m2 });

        Assert.Equal(1, pool.LiteralCount);
        Assert.Equal(2, pool.TotalSites);
    }

    [Fact]
    public void Literals_NestedInsideDelegateNewTarget_AreCollectedRecursively()
    {
        // The target itself can be an expression tree (the emitter emits the
        // whole subtree); a literal deeper inside it must still be pooled.
        var (pool, _) = StringPoolPass.Optimize(new[]
        {
            new List<Stmt> { Assign(DelegateNew(Concat(S("a"), S("b")))) },
        });

        Assert.Equal(2, pool.LiteralCount);
        Assert.Equal(2, pool.TotalSites);
    }

    //--------------------------------------------------------------------------
    // Layer ② -- constant concat PRESERVATION (folding removed).
    //
    // Folding String.Concat("a","b") into a StringConstExpr was removed:
    // string.Concat returns a FRESH instance while a literal is the intern
    // pool's instance -- ReferenceEquals distinguishes them (net48 probe:
    // ReferenceEquals(string.Concat("intern-","chain"), "intern-chain") ==
    // false). Folding would rewrite the site into an interned g_strPool[id]
    // load and break identity (internchain matrix-5 regression). The pass is
    // an IDENTITY transform: every Concat call is preserved.
    //--------------------------------------------------------------------------
    [Fact]
    public void ConstantConcat_TwoArgs_NotFolded()
    {
        var folded = StringPoolPass.FoldConcatConstants(
            new List<Stmt> { Assign(Concat(S("a"), S("b"))) }).Stmts;

        var stmt = Assert.IsType<AssignStmt>(folded[0]);
        var call = Assert.IsType<CallExpr>(stmt.Value);
        Assert.Equal(2, call.Args.Count);
        Assert.IsType<StringConstExpr>(call.Args[0]);
        Assert.IsType<StringConstExpr>(call.Args[1]);
    }

    [Fact]
    public void ConstantConcat_FourArgs_NotFolded()
    {
        var folded = StringPoolPass.FoldConcatConstants(
            new List<Stmt> { Assign(Concat(S("a"), S("b"), S("c"), S("d"))) }).Stmts;

        var stmt = Assert.IsType<AssignStmt>(folded[0]);
        var call = Assert.IsType<CallExpr>(stmt.Value);
        Assert.Equal(4, call.Args.Count);
        Assert.IsType<StringConstExpr>(call.Args[0]);
        Assert.IsType<StringConstExpr>(call.Args[3]);
    }

    [Fact]
    public void ConstantConcat_EmptyStringArg_NotFolded()
    {
        var folded = StringPoolPass.FoldConcatConstants(
            new List<Stmt> { Assign(Concat(S("a"), S(""), S("b"))) }).Stmts;

        var stmt = Assert.IsType<AssignStmt>(folded[0]);
        var call = Assert.IsType<CallExpr>(stmt.Value);
        Assert.Equal(3, call.Args.Count);
        Assert.IsType<StringConstExpr>(call.Args[1]);
    }

    [Fact]
    public void ConstantConcat_AsReturnValue_NotFolded()
    {
        var folded = StringPoolPass.FoldConcatConstants(
            new List<Stmt> { Ret(Concat(S("x"), S("y"))) }).Stmts;

        var stmt = Assert.IsType<RetStmt>(folded[0]);
        Assert.IsType<CallExpr>(stmt.Value);
    }

    [Fact]
    public void Concat_WithNonConstantArg_NotFolded()
    {
        var folded = StringPoolPass.FoldConcatConstants(
            new List<Stmt> { Assign(Concat(S("a"), L("v"))) }).Stmts;

        var stmt = Assert.IsType<AssignStmt>(folded[0]);
        var call = Assert.IsType<CallExpr>(stmt.Value);
        Assert.Equal(2, call.Args.Count);
        Assert.IsType<StringConstExpr>(call.Args[0]);
        Assert.IsType<LocExpr>(call.Args[1]);
    }

    [Fact]
    public void NonConcatIcall_NotFolded()
    {
        var folded = StringPoolPass.FoldConcatConstants(
            new List<Stmt> { Assign(OtherIcall(S("a"), S("b"))) }).Stmts;

        var stmt = Assert.IsType<AssignStmt>(folded[0]);
        Assert.IsType<CallExpr>(stmt.Value);
    }

    [Fact]
    public void Fold_ReturnsStatementsUnchanged()
    {
        var input = new List<Stmt>
        {
            new AssignStmt { Target = "t9", Value = Concat(S("a"), S("b")), SourceOffset = 0x10, SourceOpName = "call" },
        };
        var (stmts, folded) = StringPoolPass.FoldConcatConstants(input);

        Assert.Equal(0, folded);
        var stmt = Assert.IsType<AssignStmt>(stmts[0]);
        Assert.Equal("t9", stmt.Target);
        Assert.Equal(0x10, stmt.SourceOffset);
        Assert.Equal("call", stmt.SourceOpName);
    }

    //--------------------------------------------------------------------------
    // Pipeline: collect (①) only -- no fold step. Concat CALL ARGS are real
    // ldstr sites (interned literals); the concat RESULT is a fresh instance.
    // The pool must hold the args' contents -- never a folded product.
    //--------------------------------------------------------------------------
    [Fact]
    public void ConcatCall_ArgsCollected_NoFoldedProduct()
    {
        // Method 1: t = Concat("a", "b") -- call preserved, args pooled.
        // Method 2: return "ab" -- its own literal.
        var m1 = new List<Stmt> { Assign(Concat(S("a"), S("b"))) };
        var m2 = new List<Stmt> { Ret(S("ab")) };
        var (pool, folded) = StringPoolPass.Optimize(new[] { m1, m2 });

        Assert.Equal(0, folded);              // no folding anymore
        Assert.Equal(3, pool.LiteralCount);   // "a", "b", "ab" -- one each
        Assert.Equal(3, pool.TotalSites);
        Assert.Equal("ab", pool.LiteralAt(pool.IdOf("ab")));
    }

    [Fact]
    public void Optimize_ReportsZeroFolded()
    {
        var m1 = new List<Stmt> { Assign(Concat(S("a"), S("b"))), Assign(Concat(S("a"), L("v"))) };
        var (_, folded) = StringPoolPass.Optimize(new[] { m1 });

        Assert.Equal(0, folded); // no constant concat is ever folded
    }

    //--------------------------------------------------------------------------
    // Layer ③ -- static table emission (design §2.3): RenderPoolHeader renders
    // the pool data file (include/internal/string-pool.h): the g_strPool[N]
    // inline variable + the eager fill (intern_literal_n per row). Row order
    // MUST be pool id order (C8: row i == LiteralAt(i)) so the future ldstr
    // emission maps id -> g_strPool[id] trivially.
    //--------------------------------------------------------------------------
    private static StringPool PoolOf(params string[] contents)
    {
        var pool = new StringPool();
        foreach (var c in contents) pool.AddSite(c);
        return pool;
    }

    [Fact]
    public void RenderPoolHeader_EmptyPool_RendersEmpty()
    {
        Assert.Equal("", StringPoolPass.RenderPoolHeader(PoolOf()));
    }

    [Fact]
    public void RenderPoolHeader_SingleLiteral_TableSizeOne()
    {
        var header = StringPoolPass.RenderPoolHeader(PoolOf("hello"));

        Assert.Contains("inline Il2CppObject* g_strPool[1];", header);
        Assert.Contains("il2cpp_string_pool_fill", header);
        Assert.Contains("g_strPool[0] = il2cpp_string_intern_literal_n(\"hello\", 5);", header);
    }

    [Fact]
    public void RenderPoolHeader_MultipleLiterals_RowOrderIsIdOrder()
    {
        var header = StringPoolPass.RenderPoolHeader(PoolOf("alpha", "beta"));

        Assert.Contains("inline Il2CppObject* g_strPool[2];", header);
        var alphaRow = header.IndexOf("g_strPool[0] = il2cpp_string_intern_literal_n(\"alpha\", 5);", StringComparison.Ordinal);
        var betaRow = header.IndexOf("g_strPool[1] = il2cpp_string_intern_literal_n(\"beta\", 4);", StringComparison.Ordinal);
        Assert.True(alphaRow >= 0 && betaRow >= 0 && alphaRow < betaRow);
    }

    [Fact]
    public void RenderPoolHeader_DuplicateContent_SingleRowOnly()
    {
        var header = StringPoolPass.RenderPoolHeader(PoolOf("dup", "dup", "dup"));

        Assert.Contains("inline Il2CppObject* g_strPool[1];", header);
        Assert.Equal(1, CountOccurrences(header, "g_strPool[0]"));
    }

    [Fact]
    public void RenderPoolHeader_EscapesQuotesAndBackslashes()
    {
        var header = StringPoolPass.RenderPoolHeader(PoolOf("a\"b\\c"));

        Assert.Contains("g_strPool[0] = il2cpp_string_intern_literal_n(\"a\\\"b\\\\c\", 5);", header);
    }

    [Fact]
    public void RenderPoolHeader_EscapesNewlineTabCr()
    {
        var header = StringPoolPass.RenderPoolHeader(PoolOf("l1\nl2\tl3\rl4"));

        Assert.Contains("g_strPool[0] = il2cpp_string_intern_literal_n(\"l1\\nl2\\tl3\\rl4\", 11);", header);
    }

    [Fact]
    public void RenderPoolHeader_HasSelfContainedShape()
    {
        var header = StringPoolPass.RenderPoolHeader(PoolOf("x"));

        Assert.StartsWith("#pragma once", header);
        Assert.Contains("#include \"il2cpp_runtime.h\"", header);
        Assert.Contains("namespace il2cpp {", header);
        Assert.Contains("} // namespace il2cpp", header);
        Assert.Contains("il2cpp_string_pool_fill()", header);
    }

    [Fact]
    public void WritePoolHeader_WritesToIncludeInternalDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "il2cppc-stringpool-" + Guid.NewGuid().ToString("N"));
        try
        {
            StringPoolPass.WritePoolHeader(PoolOf("x"), tempDir);

            var path = Path.Combine(tempDir, "include", "internal", "string-pool.h");
            Assert.True(File.Exists(path));
            Assert.Contains("inline Il2CppObject* g_strPool[1];", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    //--------------------------------------------------------------------------
    // Resource-pool independence (RES-1 / iron rule 18): resource-pool.h
    // (Program.RenderResourcePoolHeader) is a SEPARATE T3-pattern table with
    // its OWN symbols (g_resPool / il2cpp_resource_pool_fill / g_resPool_data_i)
    // -- it must never collide with the string pool's symbols (g_strPool /
    // il2cpp_string_pool_fill). The two headers coexist in generated.h and are
    // filled in separate entry.cpp calls; any shared symbol would be an ODR /
    // duplicate-definition break.
    //--------------------------------------------------------------------------
    [Fact]
    public void RenderPoolHeader_AndResourcePoolHeader_SymbolsDisjoint()
    {
        var stringHeader = StringPoolPass.RenderPoolHeader(PoolOf("x"));
        var resourceHeader = Program.RenderResourcePoolHeader(new[]
        {
            new ManifestResourceDef("res.data", new byte[] { 0x01, 0x02 }),
        });

        // Symbol-level disjointness (ODR safety): the string pool declares
        // g_strPool[] + il2cpp_string_pool_fill(); the resource pool declares
        // g_resPool[] + il2cpp_resource_pool_fill(). Comment text may mention
        // the other pattern by name (documentation) -- assert on the code
        // SHAPES, not the raw identifiers.
        Assert.Contains("inline Il2CppObject* g_strPool[", stringHeader);
        Assert.Contains("il2cpp_string_pool_fill()", stringHeader);
        Assert.DoesNotContain("g_resPool[", stringHeader);
        Assert.DoesNotContain("il2cpp_resource_pool_fill", stringHeader);

        Assert.Contains("g_resPool[", resourceHeader);
        Assert.Contains("il2cpp_resource_pool_fill()", resourceHeader);
        Assert.DoesNotContain("g_strPool[", resourceHeader);
        Assert.DoesNotContain("il2cpp_string_pool_fill", resourceHeader);
    }

    [Fact]
    public void RenderPoolHeader_AndResourcePoolHeader_BothSelfContained()
    {
        var stringHeader = StringPoolPass.RenderPoolHeader(PoolOf("x"));
        var resourceHeader = Program.RenderResourcePoolHeader(new[]
        {
            new ManifestResourceDef("res.data", new byte[] { 0x01 }),
        });

        // Both files are #pragma-once self-contained headers in namespace
        // il2cpp -- the generated.h include + entry.cpp fill order is
        // independent of which pool is empty (CliTests covers the combined
        // emission); here we pin the no-cross-dependency shape.
        Assert.StartsWith("#pragma once", stringHeader);
        Assert.StartsWith("#pragma once", resourceHeader);
        Assert.Contains("namespace il2cpp {", stringHeader);
        Assert.Contains("namespace il2cpp {", resourceHeader);
        Assert.Contains("} // namespace il2cpp", stringHeader);
        Assert.Contains("} // namespace il2cpp", resourceHeader);
    }

    [Fact]
    public void WritePoolHeader_EmptyPool_WritesNoFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "il2cppc-stringpool-" + Guid.NewGuid().ToString("N"));
        try
        {
            StringPoolPass.WritePoolHeader(PoolOf(), tempDir);

            var path = Path.Combine(tempDir, "include", "internal", "string-pool.h");
            Assert.False(File.Exists(path));
        }
        finally
        {
            // An empty pool writes no directories (WritePoolHeader returns early) —
            // tempDir may never have been created.
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    //--------------------------------------------------------------------------
    // T3 emission wiring (docs/plans/t3-stringpool-emission.md): Generate is
    // the AUTHORITATIVE pool builder (D1 -- its in-loop fold->collect feeds
    // BOTH the ldstr emitter and the pool table); Program.cs consumes the
    // returned GenerateResult. capicalc's single literal "boom" lives in
    // Throw4's new Exception("boom") -- a THROW path, which the emitter
    // renders as il2cpp_throw_new (canned message, plan §3: throw path
    // untouched), so capicalc proves pool/table wiring but CANNOT show the
    // ldstr->load emission. refarr's plain ldstr sites ("hello"/"world")
    // are the load-emission probe; calc has NO string literal (empty-pool
    // zero-change path).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_Capicalc_ReturnsPoolWithBoom()
    {
        var exe = FixturePath("capicalc");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = CodeGenerator.Generate(types, dir, exe);

            Assert.Equal(1, result.Pool.LiteralCount);
            Assert.Equal("boom", result.Pool.LiteralAt(result.Pool.IdOf("boom")));
            Assert.Equal(1, result.Pool.TotalSites);
            Assert.True(result.Methods >= 1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_Capicalc_WritesPoolHeaderAndTable()
    {
        var exe = FixturePath("capicalc");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = CodeGenerator.Generate(types, dir, exe);
            // The table data file is written by the Program.cs consumption path
            // (design §6: WritePoolHeader stays at the Program layer, Generate only
            // builds the pool without persisting) — the unit test mirrors that
            // consumption path.
            StringPoolPass.WritePoolHeader(result.Pool, dir);

            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "string-pool.h"));
            Assert.Contains("g_strPool[1]", header);
            Assert.Contains("intern_literal_n(\"boom\", 4)", header);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_Capicalc_GeneratedHIncludesPoolAndEntryFills()
    {
        var exe = FixturePath("capicalc");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);

            var generated = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("#include \"string-pool.h\"", generated);
            var entry = File.ReadAllText(Path.Combine(dir, "src", "entry.cpp"));
            Assert.Contains("il2cpp::il2cpp_string_pool_fill();", entry);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_Refarr_LdstrEmitsSinglePoolLoad()
    {
        var exe = FixturePath("refarr");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);

            // refarr's ldstr sites are plain assignment sites
            // (names[0]="hello"; names[1]="world") — ldstr lowers to a single
            // g_strPool[id] load; per-site intern_literal calls must disappear from
            // method bodies (table rows are exclusively filled by string-pool.h's
            // fill). capicalc's "boom" lives on a throw path (il2cpp_throw_new canned
            // message, §3 don't touch) — hence the refarr probe.
            var program = string.Join("\n",
                Directory.GetFiles(Path.Combine(dir, "src"), "*.cpp").Select(File.ReadAllText));
            Assert.Contains("il2cpp::g_strPool[0]", program);
            Assert.Contains("il2cpp::g_strPool[1]", program);
            Assert.DoesNotContain("il2cpp_string_intern_literal(\"hello\")", program);
            Assert.DoesNotContain("il2cpp_string_intern_literal(\"world\")", program);
            Assert.DoesNotContain("il2cpp_string_intern_literal_n(\"hello\"", program);
            Assert.DoesNotContain("il2cpp_string_intern_literal_n(\"world\"", program);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_Calc_EmptyPool_NoPoolFileNoIncludeNoFill()
    {
        var exe = FixturePath("calc");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = CodeGenerator.Generate(types, dir, exe);

            Assert.Equal(0, result.Pool.LiteralCount);
            Assert.False(File.Exists(Path.Combine(dir, "include", "internal", "string-pool.h")));
            var generated = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.DoesNotContain("string-pool.h", generated);
            var entry = File.ReadAllText(Path.Combine(dir, "src", "entry.cpp"));
            Assert.DoesNotContain("il2cpp_string_pool_fill", entry);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

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
