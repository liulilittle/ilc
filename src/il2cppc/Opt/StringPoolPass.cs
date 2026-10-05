using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Opt;

//------------------------------------------------------------------------------
// String-pooling optimization -- layer ① (design:
// docs/architecture/string-pooling-design.md §2.1).
//
//   ① Compile-time dedup (StringPool): the assembly-level literal pool.
//      Every StringConstExpr occurrence (site) registers its content; IDENTICAL
//      content maps to ONE id, across methods AND types. The pool is the
//      input of the T3 static-table emission (g_strPool[N] -- a CodeGenerator
//      wiring task, out of this file's scope). .NET interned semantics are
//      untouched: the runtime pool stays the identity authority.
//
//   CONSTANT CONCAT FOLDING (former layer ②) IS REMOVED: folding
//   String.Concat("a","b") into a StringConstExpr breaks reference identity.
//   string.Concat returns a FRESH (non-interned) instance while a literal is
//   the intern-pool instance -- ReferenceEquals observes the difference
//   (net48 probe: ReferenceEquals(string.Concat("intern-","chain"),
//   "intern-chain") == false; the compiler-folded `"intern-" + "chain"`
//   literal == true). A folded product becomes an interned g_strPool[id]
//   load, flipping the comparison (internchain matrix-5 regression). The
//   runtime icall il2cpp_string_concat_many (concat.cpp: il2cpp_string_new)
//   already allocates a fresh instance, so every Concat call is preserved.
//
// This file is pass-only: it never touches StackFold/CodeGenerator. The
// AUTHORITATIVE pool is built by CodeGenerator.Generate's in-loop
// collect (D1 single writer -- the ldstr emitter resolves ids from
// that same pool). ScanAssembly below is an independent diagnostic/reference
// API (kept for tooling), NOT a second writer into emission: it re-reads
// method IR with the SAME pipeline pieces Generate uses
// (MethodBodyReader -> IlDecoder -> StackFold), so its pool reflects what
// the emitter saw; the report is diagnostic output, the generated code is
// unchanged.
//------------------------------------------------------------------------------
public sealed class StringPool
{
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    private readonly List<string> _literals = new();

    /// <summary>Distinct literal count (N -- the static-table size for T3).</summary>
    public int LiteralCount => _literals.Count;

    /// <summary>Every ldstr site (duplicates included).</summary>
    public int TotalSites { get; private set; }

    /// <summary>Pool id of a content; identical content always returns the SAME id.</summary>
    public int IdOf(string content)
    {
        if (!_ids.TryGetValue(content, out var id))
        {
            id = _literals.Count;
            _ids[content] = id;
            _literals.Add(content);
        }
        return id;
    }

    public string LiteralAt(int id) => _literals[id];

    /// <summary>Contents in id order (the T3 table's row list).</summary>
    public IReadOnlyList<string> Literals => _literals;

    /// <summary>Register one ldstr site in the pool.</summary>
    public void AddSite(string content)
    {
        TotalSites++;
        IdOf(content);
    }
}

public static class StringPoolPass
{
    // String.Concat(string, string[, ..]) resolves to this runtime icall
    // (MethodMap.String.cs Concat case). KEPT as documentation/contract: the
    // concat call is preserved and emitted to it (fresh allocation -- the
    // pass never folds Concat into a pooled literal).
    public const string ConcatManyCppName = "il2cpp::runtime::il2cpp_string_concat_many";

    //--------------------------------------------------------------------------
    // Constant concat pass -- IDENTITY (folding removed).
    //
    // .NET identity semantics: string.Concat(a, b) returns a FRESH instance
    // (never the intern pool's), while a literal IS the intern-pool instance
    // (ldstr). ReferenceEquals observes the difference (net48 probe:
    // ReferenceEquals(string.Concat("intern-","chain"), "intern-chain") ==
    // false). Folding Concat("a","b") -> StringConstExpr("ab") would make the
    // site an interned g_strPool[id] load and BREAK identity (internchain
    // matrix-5 regression). The runtime icall il2cpp_string_concat_many
    // (concat.cpp: il2cpp_string_new) already yields the fresh allocation, so
    // the call is preserved as-is. This identity transform keeps the pipeline
    // shape (callers still get the statement list; the fold count is 0).
    //--------------------------------------------------------------------------
    public static (List<Stmt> Stmts, int Folded) FoldConcatConstants(List<Stmt> stmts)
    {
        return (new List<Stmt>(stmts), 0);
    }

    //--------------------------------------------------------------------------
    // Layer ①: assembly-level dedup collection (T412 -- design §2.1).
    //--------------------------------------------------------------------------
    public static void Collect(StringPool pool, List<Stmt> stmts)
    {
        foreach (var s in stmts)
        {
            switch (s)
            {
                case AssignStmt a: WalkExpr(pool, a.Value); break;
                case ExprStmt e: WalkExpr(pool, e.Value); break;
                case RetStmt r: if (r.Value != null) WalkExpr(pool, r.Value); break;
                case BranchStmt b: if (b.Condition != null) WalkExpr(pool, b.Condition); break;
                case CallStmt c:
                    // excext (m7m): throw_ args are the managed exception
                    // message/paramName, rendered INLINE as C literals by the
                    // throw emitter (il2cpp_throw_new takes const char*; a
                    // pooled load is an Il2CppString*) -- never pooled sites.
                    if (!c.Function.StartsWith("throw_", StringComparison.Ordinal))
                        foreach (var a in c.Args) WalkExpr(pool, a);
                    break;
                case FieldAssignStmt f: WalkExpr(pool, f.Object); WalkExpr(pool, f.Value); break;
                // LabelStmt carries no expression.
            }
        }
    }

    private static void WalkExpr(StringPool pool, Expr e)
    {
        switch (e)
        {
            case StringConstExpr s: pool.AddSite(s.Value); break;
            case BinaryExpr b: WalkExpr(pool, b.Left); WalkExpr(pool, b.Right); break;
            case UnaryExpr u: WalkExpr(pool, u.Operand); break;
            case CallExpr c: foreach (var a in c.Args) WalkExpr(pool, a); break;
            case VirtualCallExpr v: foreach (var a in v.Args) WalkExpr(pool, a); break;
            case DelegateInvokeExpr d: foreach (var a in d.Args) WalkExpr(pool, a); break;
            case DelegateNewExpr dn:
                // The emitter recurses into dn.Target (CodeGenerator's
                // EmitExpr DelegateNewExpr arm) -- a bound-instance literal
                // (`new Action<string>("abc".Trim)`) IS a real ldstr site.
                // Pooling it here keeps the pool fully built at COLLECT time
                // (D1: the emitter must never grow the pool -- id == table row).
                WalkExpr(pool, dn.Target); break;
            case MarshalUtf8Expr m: WalkExpr(pool, m.Arg); break;
            case FieldExpr f: WalkExpr(pool, f.Object); break;
            case FieldAddrExpr fa: WalkExpr(pool, fa.Object); break;
            // IntConstExpr / Int64ConstExpr / DoubleConstExpr / NullExpr /
            // LocExpr / LocAddrExpr / StaticFieldExpr / NewObjExpr /
            // ValueTypeNewExpr / FuncPtrExpr / DelegateNewExpr / ExceptionEntryExpr
            // carry no string content.
        }
    }

    //--------------------------------------------------------------------------
    // Layer ③: static table emission (T412 -- design §2.3). Renders the pool
    // data file include/internal/string-pool.h: g_strPool[N] (C++17 inline
    // variable -- cxx_std_17 is enforced on il2cpp_gen/runtime) + the eager
    // fill. Row i == pool id i (C8: index == id, so the future ldstr wiring
    // emits g_strPool[id] trivially).
    //
    //   * fill: one il2cpp_string_intern_literal(utf8) call per row -- table
    //     entries ARE the intern-pool instances (identity with String.Intern
    //     preserved); the pool is a GC root, so the table needs no root of
    //     its own (design §2.3 -- the "no GC root" simplification).
    //   * the header is self-contained (only il2cpp_runtime.h) -- the runtime
    //     fill call site (il2cpp_runtime_init, entry.cpp) and the ldstr
    //     emission (StringConstExpr -> g_strPool[id]) are CodeGenerator
    //     wiring tasks (docs/plans/t3-stringpool-emission.md).
    //   * EMPTY pool -> no file at all (N == 0 renders nothing; a zero-size
    //     array would be ill-formed).
    //--------------------------------------------------------------------------
    public const string PoolHeaderPath = "include/internal/string-pool.h";

    /// <summary>
    /// C++ string-literal escaping for the pool table rows. MUST stay
    /// byte-identical with CodeGenerator.EscapeC (5 replacements) so the
    /// fill content equals what per-site intern_literal calls would have
    /// seen (semantics: the future ldstr rewrite must be transparent).
    /// </summary>
    public static string EscapeCppLiteral(string s)
    {
        // D5 pcprobe (charset-proof literals): beyond the classic 5 escapes,
        // every non-ASCII (and non-printable) char is emitted as its UTF-8
        // bytes in 3-DIGIT OCTAL escapes. Raw >=0x80 bytes in the generated
        // header were being TRANSCODED by the host compiler (MinGW on a GBK
        // locale reads the UTF-8 source as GBK: the two bytes of U+00A4
        // decoded as one Han char), corrupting every pooled non-ASCII
        // literal. Octal escapes survive any input/exec charset pair; exactly
        // three digits each means a following literal digit can never be
        // absorbed into the escape (the \xNN hazard).
        var sb = new System.Text.StringBuilder(s.Length + 16);
        // msvc C4125 (parseint): a 3-digit octal escape followed by a RAW
        // decimal digit ("\01342") makes MSVC warn "decimal digit terminates
        // octal escape sequence" (the value is correct, the style is not).
        // When the next character after an octal run is a digit, close and
        // reopen the C++ literal ("\013" "42") -- adjacent-literal
        // concatenation is byte-identical at runtime and visibly terminates
        // the escape. (The comment above still holds: the 3-digit form already
        // prevents ABSORPTION; this split additionally silences the warning.)
        var lastWasOctal = false;
        // charconv/charstr (surrogate-pair fix): a UTF-16 supplementary
        // character ("\uD83D\uDE00" = U+1F600) is encoded as ONE code point.
        // The old per-char walk fed each surrogate HALF separately to
        // Encoding.UTF8.GetBytes -> the encoder replaced each unpaired half
        // with U+FFFD, emitting EF BF BD EF BF BD instead of F0 9F 98 80.
        // Index-based loop: a high+low pair is encoded together, everything
        // else keeps the per-unit path (a LONE surrogate still degrades to
        // U+FFFD -- invalid UTF-16 has no faithful UTF-8 form).
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); lastWasOctal = false; break;
                case '"': sb.Append("\\\""); lastWasOctal = false; break;
                case '\n': sb.Append("\\n"); lastWasOctal = false; break;
                case '\r': sb.Append("\\r"); lastWasOctal = false; break;
                case '\t': sb.Append("\\t"); lastWasOctal = false; break;
                default:
                    if (ch >= 0x20 && ch < 0x7F)
                    {
                        if (lastWasOctal && ch >= '0' && ch <= '9')
                            sb.Append("\" \""); // C4125: a raw digit after an octal escape needs the literal split
                        sb.Append(ch);
                        lastWasOctal = false;
                    }
                    else
                    {
                        string unit;
                        if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                        {
                            unit = s.Substring(i, 2);
                            i++;
                        }
                        else
                        {
                            unit = ch.ToString();
                        }
                        foreach (var b in System.Text.Encoding.UTF8.GetBytes(unit))
                        {
                            sb.Append('\\');
                            sb.Append((char)('0' + ((b >> 6) & 7)));
                            sb.Append((char)('0' + ((b >> 3) & 7)));
                            sb.Append((char)('0' + (b & 7)));
                        }
                        lastWasOctal = true;
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Render the pool data file contents (empty pool -> empty string).</summary>
    public static string RenderPoolHeader(StringPool pool)
    {
        if (pool.LiteralCount == 0) return "";
        var w = new CppWriter();
        w.Line("#pragma once");
        w.Line("// generated by il2cppc - do not edit");
        w.Line("// String-pool layer ③ static table (design: string-pooling-design.md §2.3).");
        w.Line($"// g_strPool[id] -- single-load ldstr target; {pool.LiteralCount} distinct literal(s).");
        w.Line("// Entries are intern-pool instances (il2cpp_string_intern_literal), so the");
        w.Line("// table itself needs no GC root (the intern pool is one).");
        w.Line("#include \"il2cpp_runtime.h\"");
        w.Line("namespace il2cpp {");
        w.Line($"inline Il2CppObject* g_strPool[{pool.LiteralCount}];");
        w.Line("inline void il2cpp_string_pool_fill() {");
        for (var i = 0; i < pool.LiteralCount; i++)
            w.Line($"    g_strPool[{i}] = il2cpp_string_intern_literal_n(\"{EscapeCppLiteral(pool.LiteralAt(i))}\", {System.Text.Encoding.UTF8.GetByteCount(pool.LiteralAt(i))});");
        w.Line("}");
        w.Line("} // namespace il2cpp");
        return w.ToString();
    }

    /// <summary>Write the pool data file into the gen output dir (no-op when the pool is empty).</summary>
    public static void WritePoolHeader(StringPool pool, string outDir)
    {
        var text = RenderPoolHeader(pool);
        if (text.Length == 0) return;
        var path = Path.Combine(outDir, PoolHeaderPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    //--------------------------------------------------------------------------
    // Pipeline: collect (①) only -- the concat pass (FoldConcatConstants) is
    // an identity transform, so there is no fold step before dedup; every
    // literal site (including Concat call ARGS, which are real ldstr sites)
    // is pooled, never a folded product.
    //--------------------------------------------------------------------------
    public static (StringPool Pool, int Folded) Optimize(IEnumerable<List<Stmt>> methods)
    {
        var pool = new StringPool();
        var folded = 0;
        foreach (var stmts in methods)
        {
            var (f, n) = FoldConcatConstants(stmts);
            folded += n;
            Collect(pool, f);
        }
        return (pool, folded);
    }

    //--------------------------------------------------------------------------
    // Diagnostic/reference API: re-scan an assembly's method IR independently
    // of Generate. Since T3 wiring, Generate is the AUTHORITATIVE pool (its
    // in-loop fold->collect feeds both the ldstr emitter and the pool table);
    // this method is kept for standalone tooling/verification only and never
    // feeds emission (D1 -- no second writer).
    //--------------------------------------------------------------------------
    public static (StringPool Pool, int Folded, int Methods) ScanAssembly(
        IReadOnlyList<TypeDef> types, string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var accessors = AccessorAnalyzer.Scan(pe, md, types);

        var pool = new StringPool();
        var folded = 0;
        var methods = 0;
        foreach (var t in types)
        {
            if (t.IsInterface) continue;
            foreach (var m in t.Methods)
            {
                if (m.IsPInvoke) continue; // no IL body (CodeGenerator.cs:208)
                var bodyInfo = MethodBodyReader.Read(pe, md, t, m);
                // Iron rule 2j: folded accessors are never emitted -- skip
                // their bodies too (CodeGenerator.cs:223).
                if (accessors.ContainsKey(bodyInfo.MethodToken)) continue;
                var insns = IlDecoder.DecodeMethod(bodyInfo.IlBytes, md);
                var stmts = StackFold.Fold(insns, new MethodContext
                {
                    ArgCount = bodyInfo.ArgCount,
                    LocNames = bodyInfo.LocNames,
                    LocalKinds = bodyInfo.LocalKinds,
                    ReturnsVoid = bodyInfo.ReturnsVoid,
                    ExceptionRegions = bodyInfo.ExceptionRegions,
                    CctorOwnerFullName = m.Name == ".cctor" ? t.FullName : null,
                    // Tail-call optimization (recursion-audit §6a): current method's C++
                    // name + static determination + param types -- a self-recursive tail
                    // call becomes a goto loop. The same encoding as the emitter
                    // (CodeGenerator), so the string-pool collection path's fold results
                    // stay consistent.
                    MethodCppName = MethodRefResolver.UserMethodCppName(t.FullName, m.Name, m.ParamTypes),
                    IsStatic = m.IsStatic,
                    ParamTypeNames = m.ParamTypes.Split(','),
                }, md, types, accessors, pe);
                var (f, n) = FoldConcatConstants(stmts);
                folded += n;
                Collect(pool, f);
                methods++;
            }
        }
        return (pool, folded, methods);
    }
}
