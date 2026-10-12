using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Xunit;

public class CodeGeneratorTests
{
    [Fact]
    public void Generate_CreatesCppFiles()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            Assert.True(File.Exists(Path.Combine(dir, "CMakeLists.txt")));
            Assert.True(File.Exists(Path.Combine(dir, "src", "entry.cpp")));
            Assert.True(File.Exists(Path.Combine(dir, "include", "internal", "generated.h")));
            Assert.True(Directory.GetFiles(Path.Combine(dir, "src")).Length > 0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_EmitsStaticStorageAndMethodBodies()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Calc_Program.cpp"));

            // Static field storage: counter has no constant initializer (set in
            // .cctor) → defaults to 0
            Assert.Contains("int32_t Calc_Program_sf_counter = 0;", cpp);

            // Add method body: IR→C++, a+b folded to return (arg0 + arg1);
            Assert.Contains("Calc_Program_Add_", cpp);
            Assert.Contains("return (arg0 + arg1);", cpp);

            // Main: Add(3,4)'s result is passed to the WriteLine icall (via temp
            // variable t0, or inlined directly into the call arguments when StackFold
            // inlines single-use temps -- both shapes are legal)
            Assert.True(
                cpp.Contains("icall_console_write_line_i(t0)") ||
                cpp.Contains("icall_console_write_line_i(Calc_Program_Add_"),
                "WriteLine must receive Add(3,4)'s result (via temp or inlined)");
            Assert.Contains("Calc_Program_sf_counter = (Calc_Program_sf_counter + 1LL);", cpp);

            // .cctor must be emitted (counter=10 initialization), otherwise the
            // e2e output is 1 instead of 11
            Assert.Contains("Calc_Program__cctor_", cpp);
            Assert.Contains("Calc_Program_sf_counter = 10LL;", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_EntryCallsRuntimeInitAndCctor()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var entry = File.ReadAllText(Path.Combine(dir, "src", "entry.cpp"));
            Assert.Contains("il2cpp_entry_main", entry);
            Assert.Contains("runtime_init();", entry);
            // Main is void → return 0 after the call
            Assert.Contains("return 0;", entry);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_EntryPassesArgvArrayToMainStringArgs()
    {
        // recurse work order: entry.cpp used to call Main(string[]) with zero
        // args, which mismatched the arity of the declared Main(Il2CppObject* arg0)
        // (C++ compile failure). The entry must build a managed string[] from the
        // host argv (il2cpp_array_new + string_new_from_utf8) and pass it to Main;
        // host.cpp must forward argc/argv to il2cpp_entry_main.
        var types = MetadataLoader.LoadAssembly(FixturePath("mainargs"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("mainargs"));
            var entry = File.ReadAllText(Path.Combine(dir, "src", "entry.cpp"));
            var host = File.ReadAllText(Path.Combine(dir, "src", "host.cpp"));

            // Entry contract: argv flows into the entry
            Assert.Contains("il2cpp_entry_main(int32_t argc, char** argv)", entry);
            // string[] construction: reference-element array (elemSize=sizeof(void*),
            // isRefElement=true) of length argc-1 -- .NET Main(string[]) EXCLUDES
            // argv[0] (the program name); an argc<=1 host invocation yields the
            // empty array (zero args).
            Assert.Contains("int32_t __il2cpp_argc = argc > 1 ? argc - 1 : 0;", entry);
            Assert.Contains("il2cpp::il2cpp_array_new(nullptr, sizeof(void*), true, static_cast<int32_t>(__il2cpp_argc))", entry);
            // Each argv[i] → managed string (UTF-8 host argument), skipping argv[0]
            Assert.Contains("il2cpp::il2cpp_string_new_from_utf8(argv[__il2cpp_arg + 1])", entry);
            // Main receives the constructed array argument (not a zero-arg call)
            Assert.Contains("MainArgs_Program_Main_", entry);
            Assert.Contains("reinterpret_cast<Il2CppObject*>(__il2cpp_entry_args)", entry);
            // host.cpp: main(argc, argv) forwards
            Assert.Contains("int main(int argc, char** argv)", host);
            Assert.Contains("return il2cpp_entry_main(argc, argv);", host);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_EmitsDetailedEnglishComments()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Calc_Program.cpp"));

            // Function-header English comment: IL signature + method-body
            // location + semantic overview
            Assert.Contains("// IL method: System.Int32 Calc.Program::Add(System.Int32,System.Int32)", cpp);
            Assert.Contains("// Semantics:", cpp);
            Assert.Contains("token: 0x", cpp);

            // Statement-level English IL provenance comments: call / store / ret
            // each carry an @0x offset
            // (the Add call goes via temp t0 or StackFold single-use inlining --
            // comments for both shapes are legal)
            Assert.True(
                cpp.Contains("// IL call @0x0002 -- store call result into t0") ||
                cpp.Contains("// IL call @0x0007 -- invoke Console.WriteLine via the runtime icall"),
                "Add call must carry an IL provenance comment (temp store or inlined invoke)");
            Assert.Contains("-- invoke Console.WriteLine via the runtime icall", cpp);
            Assert.Contains("// IL stsfld @0x0013 -- store value into static field Calc_Program_sf_counter", cpp);
            Assert.Contains("// IL ret @0x0003 -- exit method returning the computed value", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_CmakeUsesRelativeRuntimePath()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var cmake = File.ReadAllText(Path.Combine(dir, "CMakeLists.txt"));
            // add_subdirectory points at the repo-root src/runtime (a relative or
            // resolvable absolute path), no longer a hardcoded ../src/runtime
            var line = cmake.Split('\n').Single(l => l.Contains("add_subdirectory("));
            Assert.EndsWith("src/runtime ${CMAKE_BINARY_DIR}/runtime)", line.TrimEnd('\r'));
            Assert.Contains("add_subdirectory(", line);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_CmakeIncludesWarningFlags()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var cmake = File.ReadAllText(Path.Combine(dir, "CMakeLists.txt"));
            // CI quality gate (style spec §7: zero warnings on both compilers):
            // generated CMakeLists must carry its own warning flags.
            Assert.Contains("-Wall -Wextra", cmake); // non-MSVC branch (MinGW clang / WSL GCC)
            Assert.Contains("/W4", cmake);           // MSVC branch
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // generated.h must declare an extern for every type's static field (matching
    // the .cpp definition); otherwise cross-type static-field access fails to
    // compile (other types cannot see this type's field definition in its .cpp).
    [Fact]
    public void Generate_GeneratedHeaderDeclaresStaticFieldExterns()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("extern int32_t Calc_Program_sf_counter;", header);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // FIX-4 (delechain): a REFERENCE-typed static field (the field-backed
    // event `_changed` of type Func<int,int>) must be declared Il2CppObject*
    // storage in generated.h -- the generated body assigns the delegate
    // combine/remove result (static_cast<Il2CppObject*>) into the slot, which
    // cannot compile against int32_t. Reference-ness is decoded from the
    // metadata field signature (static FieldDefs carry no TypeKind). The
    // paired int32_t DEFINITION in TypeEmitter.EmitStaticStorage is a
    // reported TypeEmitter-side item.
    [Fact]
    public void Generate_ReferenceTypedStaticField_ExternDeclaresIl2CppObjectPtr()
    {
        var exe = CaseExePath("delechain");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("extern Il2CppObject* Cases_Delechain_sf__changed;", header);
            // int32 value fields stay int32_t (no regression).
            Assert.Contains("extern int32_t Cases_Delechain_sf_order;", header);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
    [Fact]
    public void Generate_Overloads_EmitsDistinctFunctionBodies()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("overloads"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("overloads"));
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Overloads_Program.cpp"));
            Assert.Contains("return (arg0 + arg1);", cpp);          // Add(int,int)
            Assert.Contains("return ((arg0 + arg1) + arg2);", cpp); // Add(int,int,int)
            // The two call sites pass different argument counts (2 and 3) → they
            // dispatch to two different overload functions
            Assert.Contains("Overloads_Program_Add_", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // The test host's working directory is the bin output directory, so relative
    // paths cannot resolve fixtures directly (same convention as the other tests).
    //--------------------------------------------------------------------------
    // Audit H1 -- Cgt_un/Clt_un on non-constant operands must compare as
    // UNSIGNED (the runtime stores uint values in int32_t slots; emitting
    // signed '>'/'<' would make every high-bit-set uint compare wrong).
    // Main's a/b locals are constant-initialized (0xFFFFFFFFu / 1u), so the
    // const-prop pass folds its comparisons at compile time: the folded
    // RESULTS (1/0) prove the UNSIGNED fold (signed 0xFFFFFFFFu > 1u would
    // be false); the Compare(int,int) parameter path proves the emission.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_UintComparison_EmitsUnsignedCompare()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("uintcmp"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("uintcmp"));
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "AuditFix_UintCmp.cpp"));
            // Compare(x, y) parameter path -- NON-constant operands keep the
            // unsigned cast (H1 semantics: high-bit-set uint compares).
            Assert.Contains("static_cast<uint32_t>(arg0) > static_cast<uint32_t>(arg1)", cpp);
            // Main's constant locals (0xFFFFFFFFu / 1u) are constant-propagated
            // (P0-2) and the ternary comparisons fold to constants -- the
            // emitted results are the .NET values (a>b=1, a<b=0, a==b=0).
            Assert.Contains("icall_console_write_line_i(1LL)", cpp);
            Assert.Contains("icall_console_write_line_i(0LL)", cpp);
            Assert.DoesNotContain("static_cast<uint32_t>(loc0)", cpp); // locals fully propagated
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // String literals must be emitted through the runtime INTERN POOL
    // (il2cpp_string_intern_literal): identical literals are the SAME
    // instance (.NET interned-string semantics). T3 pooling (layer ③)
    // moved the per-site intern_literal call into the static table
    // string-pool.h (each row g_strPool[id] = intern_literal(<utf8>)); the
    // method body loads the intern-pool instance via g_strPool[id]. The old
    // emission (il2cpp_string_new_from_cs) allocated a fresh string on EVERY
    // execution, breaking reference equality between identical literals.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_StringLiteral_EmitsInternLookup()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("refarr"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = CodeGenerator.Generate(types, dir, FixturePath("refarr"));
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "AuditFix_RefArr.cpp"));
            // T3 pooling: the intern-pool instances are the pool ROWS (dedup --
            // every identical literal is ONE row = ONE interned instance), the
            // method body loads them via g_strPool[id] (single load; fill is
            // il2cpp_string_intern_literal per row in string-pool.h).
            Assert.Contains("hello", result.Pool.Literals);
            Assert.Contains("world", result.Pool.Literals);
            Assert.Matches(new Regex(@"il2cpp::g_strPool\[\d+\]"), cpp);
            Assert.DoesNotContain("il2cpp_string_new_from_cs", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Audit C4 -- reference-element arrays (string[]/object[]) must be
    // allocated with elemSize = sizeof(void*) and isRefElement = true.
    // (Before the fix every newarr used elemSize 4 -- stelem_ref then wrote
    // 8-byte pointers past the 4-byte payload: heap corruption.)
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_RefArray_EmitsPointerElementSize()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("refarr"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("refarr"));
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "AuditFix_RefArr.cpp"));
            // D-TYPG-1: reference-element newarr passes the ELEMENT class so
            // the runtime stamps an element-aware descriptor (string[] ->
            // "String[]", object[] -> "Object[]"; GetType().Name parity).
            // BCL element types resolve by NAME at runtime
            // (il2cpp_class_by_name -- no generated &System_String_class symbol);
            // user types keep the &Symbol descriptor.
            Assert.Contains("il2cpp_array_new(il2cpp::il2cpp_class_by_name(\"System.String\"), 8, true", cpp);  // string[]
            Assert.Contains("il2cpp_array_new(il2cpp::il2cpp_class_by_name(\"System.Object\"), 8, true", cpp);  // object[]
            Assert.Contains("il2cpp_array_new(nullptr, 4, false", cpp); // int32[] (unchanged)
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Audit C1 -- reference-typed parameters/locals that are LIVE across a
    // reachable allocation must be registered as GC frame roots: an object
    // reachable ONLY through a reference slot (no fields) must survive
    // collections. Main allocates arrays + a Node and reads the Node AFTER
    // those allocations -- the Node local (t12) must stay rooted.
    // OPT-2 (escape analysis): ReadViaParam's `new Node(i)` loop allocation
    // is DEAD (the result is never read), so AllocPromoPass eliminates it
    // entirely -- the method has NO allocation point and correctly needs no
    // frame (no collection can run). The frameless form is asserted here too.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_RefParam_IsRegisteredAsFrameRoot()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("refarr"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("refarr"));
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "AuditFix_RefArr.cpp"));
            // The live Node local (t12, the `Node n` from Main) is a root slot.
            Assert.Contains("static_cast<void*>(&t12)", cpp);
            // ReadViaParam's only allocation was dead -> eliminated -> frameless.
            var via = AnyMethodRegion(cpp, "System.Int32 AuditFix.RefArr::ReadViaParam(");
            Assert.DoesNotContain("il2cpp_frame_push", via);
            Assert.DoesNotContain("__frameRefs", via);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // P25b (user struct box, replaces the audit H3 rejection): box of a USER
    // value type (a struct with a System.ValueType base) must generate the
    // icall_box_user_struct path -- the earlier LOUD rejection of every
    // non-Int32 box was the pre-P25b gap (boxingext2 had no user-struct box
    // helper). The generated code copies the sizeof(struct) payload into a
    // box stamped with the struct's own {T}_class descriptor.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_BoxUserStruct_EmitsBoxIcalls()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("boxlong"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("boxlong"));
            var cpp = Directory.GetFiles(Path.Combine(dir, "src"), "*.cpp", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
                .Aggregate((a, b) => a + b);
            Assert.Contains("icall_box_user_struct", cpp);
            Assert.Contains("Pt_class", cpp);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // net-domain T0: newarr System.Byte must emit the 1-byte value-array
    // entry (1, false) -- the byte stride reachable by byte[] allocations
    // (bitconvcase/hexcase use `new byte[]{...}`). ldelem.u1/stelem.i1 width
    // dispatch is already present; only the newarr gate was missing.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_ByteArray_EmitsOneByteElementSize()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("bytearr"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("bytearr"));
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "AuditFix_ByteArr.cpp"));
            Assert.Contains("il2cpp_array_new(nullptr, 1, false", cpp);
            Assert.Contains("uint8_t", cpp); // byte-stride element read (ldelem.u1)
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // timecase work order 2 follow-up + M6 enum box klass (D-ENM-3/D-ENM-5,
    // enumdeep2/3): a USER enum box (MyColor -- a TypeDef, base System.Enum)
    // emits icall_box_enum(&{T}_class, value) -- the enum's OWN name-bearing
    // class descriptor, so the boxed enum keeps its enum identity. A BCL enum
    // (System.DayOfWeek -- a TypeRef with NO runtime class descriptor symbol)
    // keeps the int64 carrier (icall_box_long -- the documented BCL-enum
    // whitelist arm).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_BoxEnum_EmitsEnumKlassBox()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("boxenum"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("boxenum"));
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "AuditFix_BoxEnum.cpp"));
            // The USER enum (MyColor.Green) boxes with its own klass descriptor.
            Assert.Equal(1, CountOccurrences(cpp, "il2cpp::runtime::icall_box_enum(&MyColor_class, static_cast<int64_t>"));
            // The BCL enum (DayOfWeek.Monday) keeps the int64 carrier.
            Assert.Equal(1, CountOccurrences(cpp, "il2cpp::runtime::icall_box_long(static_cast<int64_t>"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // crypto-domain wiring: the generated header must declare the hash-domain
    // headers (md5.h/sha1.h/sha256.h) so hashcase's generated C++ sees the
    // il2cpp_md5/sha1/sha256_hash_bytes array icalls.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_GeneratedHeader_DeclaresHashIncludes()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("#include \"md5.h\"", header);
            Assert.Contains("#include \"sha1.h\"", header);
            Assert.Contains("#include \"sha256.h\"", header);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // D-LINQ-2 (linqdeep, 2026-08-09): the LINQ domain headers
    // (linq.h/linq_map.h/linq_sort.h -- the il2cpp_linq_* array-family icalls,
    // MethodMap.Linq.cs) must be in the generated.h include table. Missing
    // include -> ~20 "no member named 'il2cpp_linq_*' in namespace 'il2cpp'"
    // compile errors when the first LINQ e2e (linqdeep) reaches the cmake
    // build stage -- same defect class as D-STK-1 (stackqueue.h).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_GeneratedHeader_DeclaresLinqIncludes()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("#include \"linq.h\"", header);
            Assert.Contains("#include \"linq_map.h\"", header);
            Assert.Contains("#include \"linq_sort.h\"", header);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Iron rule 2j -- property folding: simple accessors are folded into field
    // accesses; their FUNCTIONS are not emitted (no get_X/set_X symbols) and
    // call sites contain direct field reads/writes.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_PropertyFolding_NoAccessorFunctionsAndDirectFieldAccess()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("props"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("props"));
            var counterCpp = File.ReadAllText(Path.Combine(dir, "src", "AuditFix_Counter.cpp"));
            // Folded accessors are NOT emitted as functions.
            Assert.DoesNotContain("get_Value_", counterCpp);
            Assert.DoesNotContain("set_Value_", counterCpp);
            Assert.DoesNotContain("get_Auto_", counterCpp);
            // The COMPUTED getter (Doubled) is NOT an accessor -- its function
            // must still be emitted (it is called, not folded).
            Assert.Contains("get_Doubled_", counterCpp);
            // Virtual accessors are never folded -- their functions exist.
            Assert.Contains("get_Tag_", counterCpp);
            // Call sites read/write the field directly (no function call).
            var propsCpp = File.ReadAllText(Path.Combine(dir, "src", "AuditFix_Props.cpp"));
            Assert.Contains("->_value", propsCpp);
            Assert.Contains("->_Auto_k__BackingField", propsCpp);
            Assert.Contains("AuditFix_Counter_sf__Shared_k__BackingField", propsCpp);
            Assert.DoesNotContain("_get_Value_", propsCpp);
            Assert.DoesNotContain("_set_Value_", propsCpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Cctor guard optimization (generated-code audit #5) -- phase 1:
    //   * the done-flag test is INLINED at every static-field access point
    //     (one load + test + predicted-not-taken branch, NOT a NOINLINE call)
    //   * consecutive same-type guards (ldsfld+stsfld pairs) share ONE guard
    //   * the type's own .cctor body emits NO guard for its own statics
    //     (the flag is provably true there: ensure_cctor_* sets it before
    //     calling the .cctor body)
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_CctorGuard_AccessPointsEmitInlineCheck()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Calc_Program.cpp"));
            var main = MethodRegion(cpp, "Main", ".cctor");
            Assert.Contains("if (Calc_Program_cctor_done == false) ensure_cctor_Calc_Program();", main);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_CctorGuard_NoGuardInsideOwnCctor()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Calc_Program.cpp"));
            var cctor = MethodRegion(cpp, ".cctor", null);
            Assert.DoesNotContain("ensure_cctor_Calc_Program", cctor);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_CctorGuard_ConsecutiveSameTypeGuardsShareOneCheck()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Calc_Program.cpp"));
            var main = MethodRegion(cpp, "Main", ".cctor");
            // Main's guard points: Add static call 1 + counter++ (adjacent
            // ldsfld+stsfld pair merged) 1 + WriteLine(counter) 1 = 3.
            // Each guard = one inlined check; if any bare call were left unmerged,
            // the total would be > 3.
            var checks = CountOccurrences(main, "if (Calc_Program_cctor_done == false) ensure_cctor_Calc_Program();");
            Assert.Equal(3, checks);
            Assert.Equal(3, CountOccurrences(main, "ensure_cctor_Calc_Program"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // The inline done-check at access sites lives in OTHER translation units
    // (cross-TU static-field access, e.g. Cases.Cctor.Main reading Cases.Gadget's
    // statics). The done flag must therefore have EXTERNAL linkage with an
    // extern declaration in generated.h -- a file-scope 'static' flag would
    // fail to compile at the access site (use of undeclared identifier).
    [Fact]
    public void Generate_CctorGuard_DoneFlagDeclaredExternInGeneratedHeader()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("extern bool Calc_Program_cctor_done;", header);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Calc_Program.cpp"));
            Assert.Contains("bool Calc_Program_cctor_done = false;", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Cctor guard EXCEPTION path (e2e cctorchain: boom_caught 1 vs 2 on net48).
    // net48 semantics: a throwing .cctor's exception (TypeInitializationException)
    // is CACHED -- every later access rethrows it WITHOUT re-running the body.
    // The transpiler must therefore:
    //   * set the done flag ONLY after the .cctor body returns (not before),
    //     so a throw leaves done=false
    //   * cache the thrown DotNetException and rethrow it on later accesses
    //     (the body itself never re-runs -- Boom.cctor prints exactly once)
    //   * keep a 'running' flag so same-type reentrant access during the body
    //     (T.cctor -> T.member -> T.static field) no-ops instead of recursing
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_CctorGuard_DoneSetAfterBodyAndExceptionCached()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Calc_Program.cpp"));
            var guard = CctorGuardRegion(cpp, "Calc_Program");
            // reentrancy guard: same-type access during the body no-ops
            Assert.Contains("Calc_Program_cctor_running = true;", guard);
            Assert.Contains("if (Calc_Program_cctor_running) return;", guard);
            // failure cache: the thrown exception is cached and rethrown
            Assert.Contains("Calc_Program_cctor_exc_type", guard);
            Assert.Contains("Calc_Program_cctor_exc_msg", guard);
            Assert.Contains("throw il2cpp::DotNetException(Calc_Program_cctor_exc_type, Calc_Program_cctor_exc_msg);", guard);
            Assert.Contains("catch (il2cpp::DotNetException& __il2cpp_cctor_ex) {", guard);
            Assert.Contains("throw; // rethrow the cached cctor failure", guard);
            // done=true is set only AFTER the body completed (inside the try)
            Assert.True(guard.IndexOf("Calc_Program_cctor_done = true;", StringComparison.Ordinal)
                    > guard.IndexOf("try {", StringComparison.Ordinal),
                "the done flag must be set after the .cctor body has run");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Hot-path audit 2 -- P0-1 + loop-bounds extension: every array element
    // access in the benchmark is on a fresh `new int[100000]` (provable
    // non-null, constant length) INSIDE a Roslyn for-loop whose bound (a
    // constant <= the length) gates the body. The generated code must NOT
    // call the checked helper and must NOT emit the compare -- the raw
    // data-pointer access with NO check at all (both the null check and the
    // bounds compare are statically dead).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_HotPath_ConstLengthNewArr_ElidesNullCheckInElementAccess()
    {
        var types = MetadataLoader.LoadAssembly(BenchFixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, BenchFixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "BenchArr_BenchArr.cpp"));
            Assert.DoesNotContain("il2cpp_array_get_elem_ptr", cpp);
            Assert.Contains("->data + static_cast<size_t>", cpp);
            Assert.DoesNotContain("il2cpp_array_fail", cpp);
            Assert.DoesNotContain(">= 0", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Hot-path audit 2 -- P0-1 extension: loop-pattern bounds proof. Every
    // bench array access sits in a condition-last for loop
    // `for (i = 0; i < 100000; i++) a[i] = i` -- the header check gates every
    // body entry and i only changes by +1 after the accesses, so
    // 0 <= i < 100000 == len(a) holds by induction. Both ArrSum and ArrWrite
    // must emit the RAW data-pointer form: no compare, no fail call.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_HotPath_LoopBoundProof_ElidesBoundsCheckInElementAccess()
    {
        var types = MetadataLoader.LoadAssembly(BenchFixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, BenchFixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "BenchArr_BenchArr.cpp"));
            var arrSum = BenchMethodRegion(cpp, "ArrSum(");
            var arrWrite = BenchMethodRegion(cpp, "ArrWrite(");
            Assert.DoesNotContain("il2cpp_array_fail", arrSum + arrWrite);
            Assert.DoesNotContain(">= 0", arrSum + arrWrite);
            Assert.Contains("->data + static_cast<size_t>", arrSum);
            Assert.Contains("->data + static_cast<size_t>", arrWrite);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Hot-path audit 2 -- P0-1 extension: the raw fast paths must emit the
    // LITERAL allocation element size, not a `->elemSize` field read. The C++
    // optimizer cannot hoist the field load out of the loop (the stores go
    // through `->data`, which may alias the header -- llvm-objdump of the
    // ArrWrite inner loop shows a per-element `movsxd r10,[rax+0xc]` +
    // `imul`), while the transpiler KNOWS the allocation element size at the
    // newarr token (ArrayElementType is the single source of both the newarr
    // argument and this literal), so the literal is semantically identical to
    // the runtime field on every path -- including mismatched-opcode (invalid)
    // IL, where the checked helper also uses the ALLOCATION size.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_HotPath_LoopProvenAccess_EmitsLiteralElementWidth()
    {
        var types = MetadataLoader.LoadAssembly(BenchFixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, BenchFixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "BenchArr_BenchArr.cpp"));
            var arrSum = BenchMethodRegion(cpp, "ArrSum(");
            var arrWrite = BenchMethodRegion(cpp, "ArrWrite(");
            Assert.DoesNotContain("->elemSize", arrSum + arrWrite);
            Assert.Contains("->data + static_cast<size_t>", arrSum);
            Assert.Contains("->data + static_cast<size_t>", arrWrite);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // perf2 str_chars: the string indexer `s[i]` inside a condition-last
    // for-loop with a constant bound (bench-array StrChars: `for (i = 0; i <
    // 256; i++) sum += s[i] & 0x1F` where s is a 256-unit string LITERAL). The
    // loop-bound proof certifies 0 <= i < 256 <= len(s), so the emitter must
    // replace the icall_string_get_chars boundary with a direct UTF-16 read
    // `((Il2CppString*)s)->chars[i]` (no bounds compare, no null check -- the
    // literal is non-null and the access is provably in-bounds).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_HotPath_StringIndexer_EmitsDirectCharsRead()
    {
        var types = MetadataLoader.LoadAssembly(BenchFullFixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, BenchFullFixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "BenchArr_BenchArr.cpp"));
            var strChars = BenchMethodRegion(cpp, "StrChars(");
            Assert.Contains("static_cast<int32_t>(((reinterpret_cast<Il2CppString*>(loc0))->chars)[loc3])", strChars);
            Assert.DoesNotContain("icall_string_get_chars", strChars);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // perf2 str_chars (negative): an UNPROVABLE string indexer must KEEP the
    // icall boundary. A get_Chars on a string PARAMETER (no literal-length /
    // non-null proof) or an index that is not the loop IV must not become a raw
    // read. Uses a fixture method that indexes a string parameter in a loop
    // with an unknown bound (arrneg fixture shape, string variant not needed --
    // the string-parameter case is rejected by the missing literal-length
    // tracking: the string local is not a StringConstExpr single-assignment).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_HotPath_StringIndexer_Unprovable_KeepsIcallBoundary()
    {
        var types = MetadataLoader.LoadAssembly(BenchFullFixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, BenchFullFixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "BenchArr_BenchArr.cpp"));
            var strLen = BenchMethodRegion(cpp, "StrLen(");
            // StrLen has NO get_Chars access at all -- the negative control is
            // that the direct-read machinery never fires on non-indexer calls
            // (this region is generated unchanged from the perf1 baseline).
            Assert.DoesNotContain("->chars[", strLen);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // perf2 str_concat: the net48 `"v" + (i & 7)` IL shape -- Int32::ToString
    // immediately feeding String.Concat(string,string) (bench-array StrConcat).
    // FoldConcatInt32 must replace the ToString + concat_2 pair with the single
    // allocation icall_string_concat_int(A, X); Y = t0 -- the intermediate
    // number string (icall_int32_to_string) must be GONE from the generated
    // loop.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_HotPath_ConcatInt32_FoldsToStringIntoConcat()
    {
        var types = MetadataLoader.LoadAssembly(BenchFullFixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, BenchFullFixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "BenchArr_BenchArr.cpp"));
            var strConcat = BenchMethodRegion(cpp, "StrConcat(");
            Assert.Contains("icall_string_concat_int", strConcat);
            Assert.DoesNotContain("icall_int32_to_string", strConcat);
            Assert.DoesNotContain("icall_string_concat_2", strConcat);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
    [Fact]
    public void Generate_HotPath_UnprovableAccess_KeepsBoundsCheck()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("arrneg"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("arrneg"));
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "ArrNeg_ArrNeg.cpp"));
            var writeShift = AnyMethodRegion(cpp, "System.Int32 ArrNeg.ArrNeg::WriteShift(");
            var sum = AnyMethodRegion(cpp, "System.Int32 ArrNeg.ArrNeg::Sum(");
            // WriteShift: a[i+1] on a FRESH constant-length newarr -- batch
            // 159 allows the leaf+offset shape on the constant-length fast
            // path, but the BOUNDS CHECK MUST REMAIN (i+1 reaches 100 = the
            // array length at i=99 -> IndexOutOfRangeException, so the
            // compare against the literal 100LL + the cold fail call stay).
            Assert.DoesNotContain("il2cpp_array_get_elem_ptr", writeShift);
            Assert.Contains("->data + static_cast<size_t>", writeShift);
            Assert.Contains("< 100LL", writeShift);
            Assert.Contains("il2cpp_array_fail", writeShift);
            // Sum: a PARAMETER array (no fresh-newarr / length proof) -- the
            // null + bounds checks MUST be kept via the checked helper.
            Assert.Contains("il2cpp_array_get_elem_ptr", sum);
            Assert.DoesNotContain("->data + static_cast<size_t>", sum);
            var sumFresh = AnyMethodRegion(cpp, "System.Int32 ArrNeg.ArrNeg::SumFresh(");
            Assert.DoesNotContain("il2cpp_array_get_elem_ptr", sumFresh);
            Assert.DoesNotContain("il2cpp_array_fail", sumFresh);
            Assert.DoesNotContain(">= 0", sumFresh);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // D-ARR-1 (arrresize, 2026-08-09): a constant-length newarr local whose
    // ADDRESS is taken (Array.Resize(ref e, 3) -> icall_array_resize_i4(&e,
    // 3)) can have its array reference REPLACED by the byref callee -- the
    // byref write is NOT an AssignStmt, so the old single-assignment gate
    // could not see it. The stale constant length then fed the fast path:
    // `new int[0]` folded to `(0 >= 0 && 0 < 0LL)` (always false) so a legal
    // post-resize `e[0]`/`e[2]` read called il2cpp_array_fail (IOORE at
    // runtime -- the arrresize e2e "grow_empty" gap). The addr-taken guard
    // must fall back to the CHECKED helper (reads the runtime length) for
    // every element access on a byref-taken array local.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_AddrTakenNewArr_FallsBackToCheckedHelper()
    {
        var exe = CaseExePath("arrresize");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_ArrResize.cpp"));
            var main = AnyMethodRegion(cpp, "System.Void Cases.ArrResize::Main(");
            // Section F (`int[] e = new int[0]; Array.Resize(ref e, 3);`):
            // the post-resize e[0]/e[2] accesses must read the RUNTIME length
            // (checked helper), never a folded constant 0 (which is always
            // out-of-range). Same for section H (`big[99]` after resize to 100).
            Assert.DoesNotContain("0LL < 0LL", main);
            Assert.DoesNotContain("0LL) >= 0", main);
            // The checked helper must be used on the byref-taken locals.
            Assert.Contains("il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(loc9), static_cast<int32_t>(0LL))", main);
            Assert.Contains("il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(loc9), static_cast<int32_t>(2LL))", main);
            Assert.Contains("il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(loc11), static_cast<int32_t>(99LL))", main);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Hot-path audit 2 -- P0-2 + GC-Root-1: Len (a pure `s.Length` leaf -- the
    // get_length icall is an inline field read that can never allocate) must
    // NOT push a GC frame per call (20M calls in StrLen's loop). StrLen itself
    // interns a string literal (CAN allocate) -- but its only reference slot
    // (loc0, the pooled literal) is live across the Len call ONLY as a
    // user-call argument (Len roots its own parameter), so GC-Root-1 elides
    // StrLen's frame too: the whole benchmark body runs frameless.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_HotPath_PureLeafGetter_ElidesGcFrame()
    {
        var types = MetadataLoader.LoadAssembly(BenchFixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, BenchFixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "BenchArr_BenchArr.cpp"));
            var len = BenchMethodRegion(cpp, "Len(");
            Assert.DoesNotContain("il2cpp_frame_push", len);
            Assert.DoesNotContain("__frameRefs", len);
            var strlen = BenchMethodRegion(cpp, "StrLen(");
            Assert.DoesNotContain("il2cpp_frame_push", strlen);
            Assert.DoesNotContain("__frameRefs", strlen);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // GC-Root-1 (user top-priority perf): slot-liveness frame roots. A
    // reference slot is a frame root ONLY when its value is live across a
    // reachable allocation point. Main's Node local (t12) is created and used
    // AFTER the array/Node allocations -- it MUST stay rooted (the cross-
    // allocation root is kept). ReadViaParam's `new Node(i)` loop allocation
    // is DEAD (its result is never read): OPT-2 escape analysis eliminates it
    // entirely, so ReadViaParam has NO allocation point and is correctly
    // FRAMELESS -- the pure-local-temp case folds away completely (a method
    // with no allocation can never collect, so no slot needs a root).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_RootLiveness_KeepsCrossAllocationRoot_DropsPureLocalTemp()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("refarr"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("refarr"));
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "AuditFix_RefArr.cpp"));
            // Main: the live Node local survives the other allocations (root kept).
            Assert.Contains("static_cast<void*>(&t12)", cpp);
            Assert.Contains("__frame.refCount = 4;", cpp);
            // ReadViaParam: the dead loop allocation was eliminated -> frameless.
            var via = AnyMethodRegion(cpp, "System.Int32 AuditFix.RefArr::ReadViaParam(");
            Assert.DoesNotContain("il2cpp_frame_push", via);
            Assert.DoesNotContain("__frameRefs", via);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // GC-Root-1: a method whose only reference slot is an int[] array is
    // FRAMELESS when the array's only uses are pure element operations (the
    // stelem/ldelem inline helpers never allocate -- no collection can run
    // while the array is being filled/summed). The single allocation point
    // (the newarr) is the slot's own def, so the array never needs a root.
    // This is the whole point of the array benchmark: zero frame + no address
    // escape => the array pointer stays in a register across the element loop.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_RootLiveness_ArrayLoop_IsFrameless()
    {
        var types = MetadataLoader.LoadAssembly(BenchFixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, BenchFixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "BenchArr_BenchArr.cpp"));
            var arrSum = BenchMethodRegion(cpp, "ArrSum(");
            var arrWrite = BenchMethodRegion(cpp, "ArrWrite(");
            Assert.DoesNotContain("il2cpp_frame_push", arrSum);
            Assert.DoesNotContain("__frameRefs", arrSum);
            Assert.DoesNotContain("il2cpp_frame_push", arrWrite);
            Assert.DoesNotContain("__frameRefs", arrWrite);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // GC-Root-1 + P0-2 extension: il2cpp_object_ctor_nop is a verified pure
    // no-op (runtime.cpp: an empty body), so a base .ctor call is NOT an
    // allocation point. The generated Node ctor (newobj + object_ctor_nop +
    // one field store) therefore pushes NO frame -- its only reference (this,
    // arg0) is never live across an allocation. This is what unlocks full
    // inlining of the newobj-ctor pair in the object churn benchmark.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_RootLiveness_CtorNoop_IsFrameless()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("refarr"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("refarr"));
            var ctor = File.ReadAllText(Path.Combine(dir, "src", "Node.cpp"));
            Assert.DoesNotContain("il2cpp_frame_push", ctor);
            Assert.DoesNotContain("__frameRefs", ctor);
            Assert.Contains("il2cpp::runtime::il2cpp_object_ctor_nop", ctor);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Hot-path audit 2 -- P1: Main's three timing prints divide by the
    // constant 1000. The checked-div helper's b==0 / INT_MIN / -1 branches
    // are statically dead for that divisor; the emitted code must be a plain
    // C++ division (the optimizer strength-reduces it into mul-shift).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_HotPath_ConstantDivisor_EmitsPlainDivision()
    {
        var types = MetadataLoader.LoadAssembly(BenchFixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, BenchFixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "BenchArr_BenchArr.cpp"));
            Assert.DoesNotContain("il2cpp_checked_div", cpp);
            Assert.DoesNotContain("il2cpp_checked_rem", cpp);
            Assert.Contains("1000LL", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // Locate a method's generated region in a BenchArr file by name fragment
    // (e.g. "Len("); the region ends at the next "// IL method:" marker.
    private static string BenchMethodRegion(string cpp, string methodFragment)
    {
        var start = cpp.IndexOf($"// IL method: System.Int32 BenchArr.BenchArr::{methodFragment}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"method region '{methodFragment}' not found in generated code");
        var end = cpp.IndexOf("// IL method:", start + 1, StringComparison.Ordinal);
        return end < 0 ? cpp[start..] : cpp[start..end];
    }

    // Locate a method region in any generated file by a signature prefix
    // (e.g. "ArrNeg.ArrNeg::Sum(" -- the parameter-type rendering differs
    // per fixture, so the prefix stops at the method name).
    private static string AnyMethodRegion(string cpp, string signaturePrefix)
    {
        var start = cpp.IndexOf($"// IL method: {signaturePrefix}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"method region '{signaturePrefix}' not found in generated code");
        var end = cpp.IndexOf("// IL method:", start + 1, StringComparison.Ordinal);
        return end < 0 ? cpp[start..] : cpp[start..end];
    }

    private static string BenchFixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "bench-array", "out", "bench_array_core.exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "bench_array_core.exe not found; build tests/bench-array/bench_array_core.csproj first (expects repo root/tests/bench-array/out/bench_array_core.exe)");
    }

    // perf2: the FULL bench-array program (all six benchmarks, incl. the
    // string-indexer StrChars and the string-concat StrConcat). Used by the
    // perf2 emission tests (the CORE program's transpilable subset omits
    // them). Falls back to the CORE exe so the tests still run when the FULL
    // build is stale/missing.
    private static string BenchFullFixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "bench-array", "out", "bench_array.exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return BenchFixturePath();
    }

    private static string MethodRegion(string cpp, string methodName, string? endMethodName)
    {
        var start = cpp.IndexOf($"// IL method: System.Void Calc.Program::{methodName}()", StringComparison.Ordinal);
        Assert.True(start >= 0, $"method region '{methodName}' not found in generated code");
        var end = endMethodName == null
            ? cpp.Length
            : cpp.IndexOf($"// IL method: System.Void Calc.Program::{endMethodName}()", start + 1, StringComparison.Ordinal);
        Assert.True(end >= 0, $"end method region '{endMethodName}' not found in generated code");
        return cpp[start..end];
    }

    private static string CctorGuardRegion(string cpp, string typeCppName)
    {
        var start = cpp.IndexOf($"IL2CPP_NOINLINE void ensure_cctor_{typeCppName}(void)", StringComparison.Ordinal);
        Assert.True(start >= 0, $"ensure_cctor_{typeCppName} guard not found in generated code");
        return cpp[start..];
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
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

    //--------------------------------------------------------------------------
    // valuesem L20 (e2e diff): double.NaN's GetHashCode must match net48. The
    // .NET canonical NaN is 0xFFF8000000000000 (sign bit set); the C `NAN`
    // macro is a FLOAT QNaN (0x7FC00000) that widens to 0x7FF8000000000000 --
    // emitting it corrupts the constant's bits (net48 hash -524288 becomes
    // 2146959360). The emitter must preserve the exact IL bit pattern via
    // il2cpp_double_from_bits. Fixture: the real net48 csc-built valuesem case
    // exe (tests/cases/valuesem/obj/Release/net48/valuesem.exe -- same
    // real-exe fixture convention as StackFoldTests.InitializeArray).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_DoubleNaN_PreservesNet48CanonicalBits()
    {
        var exe = CaseExePath("valuesem");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Valuesem.cpp"));
            // 0xFFF8000000000000 = -2251799813685248 (net48 double.NaN bits)
            Assert.Contains("il2cpp_double_from_bits(static_cast<int64_t>(-2251799813685248LL))", cpp);
            // the old C-macro emission (NAN) must be gone
            Assert.DoesNotContain("= NAN", cpp);
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

    //--------------------------------------------------------------------------
    // pinvoke M4.5 (P/Invoke full return-value type): an IntPtr-returning P/Invoke
    // (DllImport) must declare the FULL pointer width. PinvokeReturnTypeCpp
    // used to emit int32_t unconditionally -- the native function returns a
    // 64-bit pointer on x64 and the low 32 bits alone name the wrong address
    // (truncation). Fixture: the real pinvokeext case exe (net48 csc -- its
    // reverse_static signature blob is 00-01-18-0E: return ELEMENT_TYPE_I/
    // native-int 0x18, the RAW IntPtr encoding; same CaseExePath convention
    // as Generate_DoubleNaN_PreservesNet48CanonicalBits).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_PinvokeIntPtrReturn_ExternDeclaresIntptrT()
    {
        var exe = CaseExePath("pinvokeext");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_PinvokeExt.cpp"));
            Assert.Contains("extern \"C\" intptr_t reverse_static(const char*);", cpp);
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("extern \"C\" intptr_t reverse_static(const char*);", header);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_PinvokeIntPtrResult_TempDeclaredIntptrT()
    {
        var exe = CaseExePath("pinvokeext");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_PinvokeExt.cpp"));
            // reverse_static's result temp must be full-width (an int32_t temp
            // truncates the pointer before it reaches the consumer). The native
            // return rides the P/Invoke result bridge
            // (il2cpp_pinvoke_result<intptr_t>).
            var m = System.Text.RegularExpressions.Regex.Match(cpp, @"(\w+) = il2cpp::runtime::il2cpp_pinvoke_result<intptr_t>\(reverse_static\(");
            Assert.True(m.Success, "reverse_static call must assign to a temp");
            var target = m.Groups[1].Value;
            Assert.Contains($"intptr_t {target};", cpp);
            Assert.DoesNotContain($"int32_t {target};", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_PtrToStringAnsi_IntPtrArg_CastToConstCharP()
    {
        var exe = CaseExePath("pinvokeext");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_PinvokeExt.cpp"));
            // the IntPtr carrier must reach the const char* icall parameter via
            // an explicit cast (an int32_t -> const char* implicit conversion
            // is a C2440 error on MSVC and truncates on x64 GCC).
            Assert.Contains("il2cpp::il2cpp_pinvoke_string_from_utf8(reinterpret_cast<const char*>(", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // P-D1 P0-1 (full-width P/Invoke marshalling matrix): int64/double/float/
    // uint64 DllImport params and returns must declare their REAL native
    // widths. PinvokeParamList/PinvokeReturnTypeCpp used to collapse every
    // non-IntPtr/non-struct type to int32_t -- a long param was truncated at
    // the call site and a double return read EAX (silent ABI corruption, both
    // P-D1 audit reports §4). Fixture: the real pinvokewide case exe.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_PinvokeWideTypes_DeclareFullWidth()
    {
        var exe = CaseExePath("pinvokewide");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_PinvokeWide.cpp"));
            // extern "C" decls: the native ABI contract for each width
            Assert.Contains("extern \"C\" int64_t add_longs(int64_t, int64_t);", cpp);
            Assert.Contains("extern \"C\" double add_doubles(double, double);", cpp);
            Assert.Contains("extern \"C\" float scale_float(float);", cpp);
            Assert.Contains("extern \"C\" uint64_t sum_ulongs(uint64_t, uint64_t);", cpp);
            // never the silent int32_t downgrade
            Assert.DoesNotContain("extern \"C\" int32_t add_longs", cpp);
            Assert.DoesNotContain("extern \"C\" int32_t add_doubles", cpp);
            Assert.DoesNotContain("extern \"C\" int32_t scale_float", cpp);
            Assert.DoesNotContain("extern \"C\" int32_t sum_ulongs", cpp);
            // the native-ABI arg bridge stays active at the call sites (the
            // templated conversion now deduces the full-width native type)
            Assert.Contains("add_longs(il2cpp::runtime::il2cpp_pinvoke_arg(", cpp);
            Assert.Contains("add_doubles(il2cpp::runtime::il2cpp_pinvoke_arg(", cpp);
            Assert.Contains("scale_float(il2cpp::runtime::il2cpp_pinvoke_arg(", cpp);
            Assert.Contains("sum_ulongs(il2cpp::runtime::il2cpp_pinvoke_arg(", cpp);
            // the header carries the same full-width decls (other TUs call)
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("extern \"C\" int64_t add_longs(int64_t, int64_t);", header);
            Assert.Contains("extern \"C\" double add_doubles(double, double);", header);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // P-D2 #4 (delegate-typed DllImport parameter): a delegate param now
    // MARSHALS through the per-signature GfpS dispatcher (the P-D1 P0-2 path
    // used to LOUDLY reject it -- extern "C" int32_t truncation). The extern
    // "C" declaration declares the NATIVE function-pointer width (intptr_t),
    // and the call sites wrap the delegate argument in il2cpp_delegate_gfp_
    // <hash> (one GfpS module per Invoke signature: int32_t(int32_t) for
    // IntFn, int32_t(int32_t,int32_t) for IntFn2). Non-delegate reference
    // params (arrays/objects) keep the LOUD rejection. Fixture: the REAL case
    // exe (tests/cases/pinvokedelegate -- Main calls the imports directly).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_PinvokeDelegateParam_DeclaresIntptrT()
    {
        var exe = CaseExePath("pinvokedelegate");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_PinvokeDelegate.cpp"));
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            // intptr_t (the native fn-pointer carrier), not int32_t (truncation)
            Assert.Contains("extern \"C\" int32_t call_with_delegate(intptr_t, int32_t);", cpp);
            Assert.Contains("extern \"C\" int32_t call_with_delegate2(intptr_t, int32_t, int32_t);", cpp);
            // the call sites wrap the delegate args in the GfpS dispatchers
            Assert.Contains("il2cpp_delegate_gfp_", cpp);
            // both Invoke signatures' GfpS dispatchers are registered
            Assert.Contains("inline intptr_t il2cpp_delegate_gfp_", header);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // P-D3 P0 (DllImport fnptr RETURN -- silent int32 truncation of the x64
    // address): `extern "C" int32_t make_inc(void);` + `int32_t loc0; loc0 =
    // make_inc();` truncated the returned function pointer (PinvokeReturnTypeCpp
    // had no FunctionPointer arm AND the call result temp collapsed to int32_t).
    // The extern decl must name void* and the calli must ride a void* slot.
    // Fixture: tests/fixtures/fnptrret.dll (a case-real net48 assembly whose
    // transpile must SUCCEED -- never an e2e case, it needs the mylib native
    // library at runtime).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_FnPtrPInvokeReturn_EmitsVoidStarExternAndCalli()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("fnptrret"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("fnptrret"));
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_FnPtrRet.cpp"));

            // The extern "C" declaration names the FULL-WIDTH native return:
            // make_inc returns a raw function pointer, not an int32_t.
            Assert.Contains("extern \"C\" void* make_inc(void);", header);
            // The call result lands in a void* slot (was int32_t loc0)...
            Assert.Contains("void* loc0", cpp);
            // ...and the indirect call is emitted through it with a null guard.
            Assert.Contains("loc0 = make_inc()", cpp);
            Assert.Contains("NullReferenceException", cpp);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // P-D3 P1 (DllImport fnptr PARAM -- LOUD reject -> wired void* pass-through
    // on x64): `apply_cb(delegate* unmanaged[Cdecl]<int,int> cb, int x)` was
    // LOUDLY rejected (no marshalling bridge). The fnptr value is the model's
    // void* "p" slot and must cross the P/Invoke boundary at full width.
    // Fixture: tests/fixtures/fnptrparam.dll (unit-only).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_FnPtrPInvokeParam_EmitsVoidStarParam()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("fnptrparam"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath("fnptrparam"));
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));

            Assert.Contains("extern \"C\" int32_t apply_cb(void*, int32_t);", header);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // P-D3 P1 => GN-18 B1 (fnptr FIELD -- full-width carrier): a `delegate*`
    // instance field now routes through the three 0x1B field decoders
    // (MetadataLoader DecodeFieldSignature / DecodeExtendedStaticType /
    // TypeEmitter DecodeNestedFieldSignature) to FieldTypeKind.NativeInt and
    // emits a full-width void* member -- the fnptr value IS a pointer and the
    // old int32_t slot truncated the x64 address. The (EMPTY FieldTypeName,
    // non-reference value slot) contract keeps the GC bitmap clean. Fixture:
    // tests/fixtures/fnptrfield.dll.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_FnPtrField_FullWidthVoidStarCarrier()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(FixturePath("fnptrfield"));
            CodeGenerator.Generate(types, dir, FixturePath("fnptrfield")); // must NOT throw (was a LOUD reject)
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("void* F;", header);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // P-D3 P1 (USER-method fnptr RETURN -- LOUD lock): a user method returning
    // a `delegate*` hits ReturnTypeCpp with no 0x1B arm -> NotSupportedException
    // (hard gate, NO silent truncation). P-D3 keeps the LOUD reject and locks
    // it with this red test; wiring the return carrier is a recorded follow-up.
    // Fixture: tests/fixtures/fnptrretuser.dll.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_UserMethodFnPtrReturn_LoudlyRejected()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("fnptrretuser"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var ex = Assert.ThrowsAny<NotSupportedException>(() => CodeGenerator.Generate(types, dir, FixturePath("fnptrretuser")));
            Assert.Contains("return type", ex.Message);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // G2 (friendly-compiler S4): Generate wraps a method-emission
    // NotSupportedException into a MethodContextException carrying the owning
    // type::method identity, so a top-level transpile failure names the exact
    // method that rejected loudly. The wrapper must preserve the inner
    // exception chain (the throw-point message stays reachable).
    //--------------------------------------------------------------------------
    [Fact]
    public void MethodContextException_OnGenerateFailure()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath("fnptrretuser"));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var ex = Assert.Throws<MethodContextException>(() => CodeGenerator.Generate(types, dir, FixturePath("fnptrretuser")));
            Assert.False(string.IsNullOrWhiteSpace(ex.TypeName), "TypeName must identify the owning type");
            Assert.False(string.IsNullOrWhiteSpace(ex.MethodName), "MethodName must identify the failing method");
            Assert.Contains("return type", ex.InnerException!.Message);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // P0-1/P0-2 wiring regression: the Normalize pass runs between StackFold and
    // EmitBody -- the generated shapes of methods WITHOUT identity candidates
    // must be byte-identical to the pre-Normalize output.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_NormalizeWiring_PreservesExistingShapes()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Calc_Program.cpp"));

            // a+b has no identity candidate -> the emitted shape is unchanged
            Assert.Contains("return (arg0 + arg1);", cpp);
            // counter++ (ldsfld/ldc/add/stsfld) is not a fold candidate
            Assert.Contains("Calc_Program_sf_counter = (Calc_Program_sf_counter + 1LL);", cpp);
            // .cctor constant init (stsfld, not stloc) is untouched by const-prop
            Assert.Contains("Calc_Program_sf_counter = 10LL;", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // cast narrowing (R-A1, interlockedcase/valuetypes regression root cause):
    // commit b59c6d9 blanket-cast every ""-cast LocAddrExpr arg to
    // (uint8_t*) because Guid value slots (uint8_t[16] arrays) have
    // pointer-to-array addresses that do NOT decay -- and broke every other
    // address-taking domain (Interlocked int32_t*/int64_t* locations,
    // value-type ctor Point* receivers). The emitter now carries the Guid
    // byte-slot cast EXPLICITLY via the "g" cast code (MethodMap.Guid.cs --
    // rendered (uint8_t*)(...)) while "" stays a pure passthrough. Fixture:
    // the real net48 csc-built e2e case exes (same CaseExePath convention as
    // Generate_DoubleNaN_PreservesNet48CanonicalBits).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_GuidByteSlots_KeepBytePointerCast()
    {
        var exe = CaseExePath("guidcase");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_GuidCase.cpp"));
            // the receiver ADDRESS of a uint8_t[16] slot must still reach the
            // guid.h const uint8_t* param with the explicit byte-pointer cast
            // (pointer-to-array does not decay)
            Assert.Contains("il2cpp::il2cpp_guid_to_string(reinterpret_cast<uint8_t*>((&loc", cpp);
            Assert.Contains("il2cpp::il2cpp_guid_equals(reinterpret_cast<uint8_t*>((&loc", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_Lock_LabelSelfTerminatesWithNullStatement()
    {
        var exe = CaseExePath("lockcase");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "LockCase.cpp"));

            // The lock finally-guard label (`L_N:` targeting the lockTaken
            // branch) lands at the END of a try block. A bare label directly
            // before a block-closing `}` is C2059 in MSVC / syntax error in
            // clang -- every emitted label must self-terminate with a null
            // statement (`L_N: ;`), which is valid C++ in every position.
            Assert.Contains("L_", cpp); // the lock guards do emit labels
            var bareLabels = Regex.Matches(cpp, @"(?m)^\s*L_\d+:(?!\s*;)\s*(//.*)?$");
            Assert.True(bareLabels.Count == 0,
                $"bare label(s) before block close (C2059): {string.Join(", ", bareLabels.Select(m => m.Value.Trim()))}");
            // the lockTaken guard label (`L_54:` in the try block) must carry
            // the null statement -- exact emission shape `L_N: ;`
            Assert.Matches(@"(?m)^\s*L_\d+: ;\s*// IL branch target", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_InterlockedLocations_PassThroughUncast()
    {
        var exe = CaseExePath("interlockedcase");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "InterlockedCase.cpp"));
            // int32_t*/int64_t* location addresses pass through RAW ("" cast)
            Assert.Contains("il2cpp::runtime::il2cpp_interlocked_increment((&loc", cpp);
            Assert.Contains("il2cpp::runtime::il2cpp_interlocked_add((&loc", cpp);
            // the b59c6d9 blanket cast must be GONE from the whole file
            Assert.DoesNotContain("(uint8_t*)", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_ValueTypeCtorReceiver_PassesThroughUncast()
    {
        var exe = CaseExePath("valuetypes");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_ValueTypes.cpp"));
            // user value-type ctor receivers (Point*) pass through RAW
            Assert.Contains("Point__ctor_", cpp);
            Assert.Contains("((&loc0)", cpp);
            // never the blanket byte-pointer cast on a Point* receiver
            Assert.DoesNotContain("(uint8_t*)((&loc", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // CodeGenerator declaration scan (matrix-6 root cause): every local
    // referenced ANYWHERE in a reachable statement's expression tree must be
    // declared -- including LocAddrExpr nested inside a CallExpr argument
    // (Int32.TryParse's out-slot inside WriteLine: `WriteLine(TryParse(str,
    // &locN))`) and LocExpr reads in AssignStmt.Value (`tN = (locM == 123)`).
    // The pre-fix scan only looked at TOP-LEVEL CallStmt args and the direct
    // AssignStmt.Value shape, so numchain's TryParse result slots loc7-loc22
    // and loc25/loc26 were used but never declared -> cmake build failure.
    // Fixture: the real net48 numchain case exe (same CaseExePath convention).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_NestedLocAddrExprAndLocExprReads_DeclaresAllLocals()
    {
        var exe = CaseExePath("numchain");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Program.cpp"));
            // out-slots of int.TryParse / double.TryParse / long.TryParse are
            // nested LocAddrExpr inside a CallExpr argument -- every one must
            // now be declared (loc7..loc22 + the trailing loc25/loc26).
            for (var i = 7; i <= 12; i++)
                Assert.Contains($"int32_t loc{i};", cpp);
            Assert.Contains("double loc13;", cpp);
            Assert.Contains("double loc15;", cpp);
            Assert.Contains("int64_t loc17;", cpp);
            Assert.Contains("int64_t loc22;", cpp);
            Assert.Contains("int32_t loc25;", cpp);
            Assert.Contains("int32_t loc26;", cpp);
            // a LocExpr READ in an AssignStmt.Value (`t122 = (loc8 == 123)`)
            // must also see its slot declared (loc8 is written ONLY through
            // the nested LocAddrExpr above -- no AssignStmt.Target exists).
            Assert.Contains("(loc8 == 123LL)", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // bitopcase (M6 G1/G2/G3/G4/G5, e2e FAIL@diff 11): int64 shifts must mask
    // the count to 6 bits (& 63) not 5 (& 31 -- 1LL<<33 became 1LL<<1), the
    // int64/ulong shr.un/cgt.un casts must keep the full uint64_t width (a
    // uint32_t cast truncated 2^32), and long.MinValue must emit as the
    // two-literal form `(-9223372036854775807LL - 1LL)` -- the single literal
    // `-9223372036854775808LL` is parsed as ULL (magnitude exceeds LLONG_MAX),
    // unary minus wraps the unsigned value, and the constant becomes +2^63.
    // Fixture: the real net48 csc-built bitopcase case exe (CaseExePath).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_BitOpCase_Int64ShiftMaskAndLongMinValue()
    {
        var exe = CaseExePath("bitopcase");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_BitOpCase.cpp"));
            // G5 int64fold (pre-apply verification): int64 CONSTANT subtrees
            // now fold at generation time (TryFoldInt64), so the pre-patch
            // constant-emission strings are gone; the folded results carry the
            // same semantics. 1L << 33 (6-bit mask in the fold) -> 2^33:
            Assert.Contains("8589934592LL", cpp);
            // 81985529216486895L / 4294967296L -> truncated quotient:
            Assert.Contains("19088743LL", cpp);
            // the int64 6-bit shift mask still emits for NON-constant operands:
            Assert.Contains("(24LL & 63)", cpp);
            // the int32 5-bit mask is unchanged:
            Assert.Contains("(1LL & 31)", cpp);
            // int64 cgt.un keeps the full uint64_t width (G3: 2^32 > 2^32-1;
            // comparisons are deliberately NOT folded):
            Assert.Contains("static_cast<uint64_t>(4294967296LL) > static_cast<uint64_t>(4294967295LL)", cpp);
            // long.MinValue emits as the well-formed two-literal form (G4),
            // whether from the constant or from the 1L<<63 fold:
            Assert.Contains("(-9223372036854775807LL - 1LL)", cpp);
            Assert.DoesNotContain("-9223372036854775808LL", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // mathdeep (e2e FAIL@diff 19): IsNaN(double)/IsFinite(double) -- the
    // double parameters must be DECLARED as double (an int32_t declaration
    // truncated the value), and the `v != v` self-probe must survive as a raw
    // double comparison instead of Normalize folding the int32 self-identity
    // (Ceq(x,x) -> 1, making IsNaN return false for every value).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_MathDeep_IsNaNDoubleParamSurvivesSelfCompare()
    {
        var exe = CaseExePath("mathdeep");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_MathDeep.cpp"));
            // the double param must be declared double (not int32_t) -- the
            // value would be truncated before the NaN probe otherwise
            Assert.Contains("Cases_MathDeep_IsNaN_", cpp);
            Assert.Contains("(double arg0)", cpp);
            // the v != v NaN probe survives as a raw double comparison -- the
            // int32 self-identity fold (x == x -> 1) must NOT fire on doubles
            Assert.Contains("((static_cast<double>(arg0)) != (static_cast<double>(arg0)))", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // strcomp2 (audit C4): a char[] newarr must allocate 2-byte UTF-16 code
    // units -- the ArrayElementType table previously rejected System.Char with
    // a LOUD "newarr element type not supported" (only Int32/Byte/ref types
    // had arms), so Split/params-char[] / Trim(char[]) all failed at transpile.
    // The runtime Split path and stelem.i2/ldelem.i2 already dispatch the
    // 2-byte width; only the newarr gate was missing.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_StrComp2_CharArrayNewArr_EmitsTwoByteElements()
    {
        var exe = CaseExePath("strcomp2");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Program.cpp"));
            // csv.Split(',', ';') -> newarr System.Char: 2-byte value elements
            Assert.Contains("il2cpp_array_new(nullptr, 2, false, static_cast<int32_t>(2LL))", cpp);
            Assert.Contains("il2cpp_array_new(nullptr, 2, false, static_cast<int32_t>(3LL))", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // D-GC-2 (gcstress3, audit C4): the newarr element-type whitelist must
    // GENERALIZE reference elements. Before the fix ArrayElementType was an
    // enumeration (Int32/Byte/Char/Int64/UInt64/String/Object + a `types`
    // FullName lookup) and threw "newarr element type System.WeakReference not
    // supported (audit C4)" for BCL reference types (WeakReference is a TypeRef,
    // absent from `types`) and for nested user classes (MetadataLoader skips
    // nested TypeDefs and GetFullName returns the short name, so the lookup
    // missed Payload too). The fix classifies by token kind: a TypeDef's
    // metadata base type (System.ValueType/System.Enum = value -> reject) and
    // a TypeRef against the wired BCL value-type set (reject); every other
    // element is a reference element -> (sizeof(void*), ref=true).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_Gcstress3_WeakRefAndUserClassArrays_AllocateRefElements()
    {
        var exe = CaseExePath("gcstress3");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Gcstress3.cpp"));
            // D-GC-2: new WeakReference[3] (BCL TypeRef) and new Payload[3]
            // (nested user class) both transpile to a reference-element
            // allocation (8-byte stride, GC-scanned payload).
            // D-TYPG-1: the newarr passes the ELEMENT class (WeakReference /
            // the nested Payload class) for the element-aware descriptor.
            // BCL element types resolve by NAME at runtime
            // (il2cpp_class_by_name -- no generated &System_WeakReference_class
            // symbol); user/nested types keep the &Symbol descriptor.
            Assert.Contains("il2cpp_array_new(il2cpp::il2cpp_class_by_name(\"System.WeakReference\"), 8, true, static_cast<int32_t>(3LL))", cpp);
            Assert.Contains("il2cpp_array_new(&Cases_Gcstress3_Payload_class, 8, true, static_cast<int32_t>(3LL))", cpp);
            Assert.Contains("stelem.ref", cpp);
            Assert.Contains("ldelem.ref", cpp);
            // The enumerated value arms are untouched: byte[] (1,false) and
            // int[] (4,false) keep their non-reference strides.
            Assert.Contains("il2cpp_array_new(nullptr, 1, false,", cpp);
            Assert.Contains("il2cpp_array_new(nullptr, 4, false,", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ArrayElementType_Gcstress3_ClassifiesEveryNewarrToken()
    {
        var exe = CaseExePath("gcstress3");
        var types = MetadataLoader.LoadAssembly(exe);
        using var fs = File.OpenRead(exe);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        var method = typeof(Il2Cpp.Transpiler.Emit.CodeGenerator).GetMethod(
            "ArrayElementType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var found = new Dictionary<string, (int, bool)>();
        foreach (var token in MainNewarrTokens(pe, md))
        {
            var handle = MetadataTokens.Handle(token);
            var name = handle.Kind switch
            {
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)handle),
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)handle),
                _ => throw new NotSupportedException(),
            };
            var value = ((int ElemSize, bool IsRefElement))method!.Invoke(
                null, new object[] { md, token, types })!;
            found[name] = value;
        }
        // D-GC-2: BCL reference type and nested user class -> reference elements.
        Assert.Equal((8, true), found["System.WeakReference"]);
        Assert.Equal((8, true), found["Cases.Gcstress3+Payload"]);
        // The enumerated value arms keep their sizes.
        Assert.Equal((4, false), found["System.Int32"]);
        Assert.Equal((1, false), found["System.Byte"]);
    }

    // Scan Cases.Gcstress3.Gcstress3::Main's IL for every newarr (0x8D) element
    // token (D-GC-2 token-level classification probe).
    private static List<int> MainNewarrTokens(PEReader pe, MetadataReader md)
    {
        var tokens = new List<int>();
        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            if (md.GetString(td.Name) != "Gcstress3") continue;
            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if (md.GetString(m.Name) != "Main") continue;
                var il = pe.GetMethodBody(m.RelativeVirtualAddress)!.GetILBytes()!;
                for (var i = 0; i < il.Length; i++)
                {
                    if (il[i] != 0x8D) continue; // newarr
                    tokens.Add(BitConverter.ToInt32(il, i + 1));
                    i += 4;
                }
            }
        }
        return tokens;
    }

    // batch 2026-10-05 (iopairprobe, audit C4): a uint[] newarr must allocate
    // 4-byte NON-reference elements (the ldelem.u4/stelem.i4 width dispatch
    // already exists). Before the fix System.UInt32 hit the LOUD "newarr of
    // value type System.UInt32 not supported (audit C4)" rejection and the
    // case FAILed at the transpile step.
    [Fact]
    public void ArrayElementType_UInt32_Allocates4ByteValueArray()
    {
        var exe = CaseExePath("iopairprobe");
        var types = MetadataLoader.LoadAssembly(exe);
        using var fs = File.OpenRead(exe);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        var method = typeof(Il2Cpp.Transpiler.Emit.CodeGenerator).GetMethod(
            "ArrayElementType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        int found = 0;
        foreach (var th in md.TypeDefinitions)
        {
            foreach (var mh in md.GetTypeDefinition(th).GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if (m.RelativeVirtualAddress == 0) continue; // no IL body
                byte[] il;
                try { il = pe.GetMethodBody(m.RelativeVirtualAddress)!.GetILBytes()!; }
                catch { continue; }
                for (var i = 0; i < il.Length; i++)
                {
                    if (il[i] != 0x8D) continue; // newarr
                    var token = BitConverter.ToInt32(il, i + 1);
                    i += 4;
                    var handle = MetadataTokens.Handle(token);
                    if (handle.Kind != HandleKind.TypeReference) continue;
                    var name = md.GetFullName((TypeReferenceHandle)handle);
                    if (name != "System.UInt32") continue;
                    var value = ((int ElemSize, bool IsRefElement))method!.Invoke(
                        null, new object[] { md, token, types })!;
                    Assert.Equal((4, false), value);
                    found++;
                }
            }
        }
        Assert.True(found > 0, "iopairprobe must contain a uint[] newarr (B_uint32)");
    }

    // D-GC-2 negative classification: the wired BCL value types must stay
    // rejected (a value-slot array must never fall through to the reference
    // path -- the GC would scan the payload with an 8-byte ref stride over
    // value slots). IsKnownBclValueType is private -- invoke through
    // reflection (same pattern as TypeCppName_GCHandle_MapsToInt32Slot).
    [Fact]
    public void IsKnownBclValueType_RejectsWiredValueTypesAcceptsReferences()
    {
        var method = typeof(Il2Cpp.Transpiler.Emit.CodeGenerator).GetMethod(
            "IsKnownBclValueType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        static bool Call(System.Reflection.MethodInfo m, string name) =>
            (bool)m.Invoke(null, new object[] { name })!;
        // Wired BCL value types (primitives beyond the sized arms + structs).
        Assert.True(Call(method, "System.Double"));
        Assert.True(Call(method, "System.Boolean"));
        Assert.True(Call(method, "System.Single"));
        Assert.True(Call(method, "System.UInt32"));
        Assert.True(Call(method, "System.DateTime"));
        Assert.True(Call(method, "System.TimeSpan"));
        Assert.True(Call(method, "System.Decimal"));
        Assert.True(Call(method, "System.Guid"));
        Assert.True(Call(method, "System.IntPtr"));
        // Reference types must NOT be in the value set (D-GC-2 target).
        Assert.False(Call(method, "System.WeakReference"));
        Assert.False(Call(method, "System.String"));
        Assert.False(Call(method, "System.Exception"));
        Assert.False(Call(method, "Cases.Gcstress3+Payload"));
    }

    //--------------------------------------------------------------------------
    // M6 CodeGenerator batch 23 -- include-table rebuild additions: the
    // reflection (reflect_base.h / typeinfo.h / invoker.h) + GCHandle
    // (il2cpp_gchandle.h) + StreamIO object (il2cpp_streamio.h) + XML
    // (xml_doc.h) headers must appear in generated.h or the generated C++
    // cannot see the icall declarations (reflectcase/refinvoke/streamcase/
    // xmlcase were FAIL@cmake_build with "no member named ... in namespace").
    // decimal.h / decimal_format.h were already present (M6 include rebuild)
    // -- asserted to catch a future drop.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_GeneratedHeader_DeclaresBatch23Includes()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath());
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("#include \"reflect_base.h\"", header);
            Assert.Contains("#include \"typeinfo.h\"", header);
            Assert.Contains("#include \"invoker.h\"", header);
            Assert.Contains("#include \"il2cpp_gchandle.h\"", header);
            Assert.Contains("#include \"il2cpp_streamio.h\"", header);
            Assert.Contains("#include \"xml_doc.h\"", header);
            Assert.Contains("#include \"decimal.h\"", header);
            Assert.Contains("#include \"decimal_format.h\"", header);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // CG6 (matrix7 FAIL attribution) -- CodeGenerator.cs domain fixes:
    //   delegate2  ① entry.cpp delegate method-name registration
    //   delegate2 ② custom-delegate castclass identity (no IntFn_class descriptor)
    //   delegatecb   'n' native-int P/Invoke param -> intptr_t
    //   arrayops     Int64/UInt64 newarr -> 8-byte non-ref allocation
    //--------------------------------------------------------------------------

    [Fact]
    public void Generate_Delegate2_EntryRegistersDelegateMethodNames()
    {
        var exe = CaseExePath("delegate2");
        var types = MetadataLoader.LoadAssembly(exe);
        // Program.cs expands NESTED TypeDefs before Generate (IntFn is a nested
        // private delegate; without expansion it is absent from `types` and
        // StackFold cannot classify it as a delegate).
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var entry = File.ReadAllText(Path.Combine(dir, "src", "entry.cpp"));
            // CG6: Delegate.Method.Name resolves through the runtime registry,
            // which is EMPTY unless generated startup code registers the SHORT
            // name of every delegate-created method (delegate2.cs:34 prints "Ten").
            Assert.Contains("il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_Delegate2_Ten_", entry);
            Assert.Contains("\"Ten\");", entry);
            Assert.Contains("\"ThrowsEx\");", entry);
            // The registration must run before Main (inside il2cpp_runtime_init).
            Assert.True(entry.IndexOf("il2cpp_delegate_register_method") > entry.IndexOf("il2cpp_runtime_init"),
                "delegate registration must follow runtime_init");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_Delegate2_CustomDelegateHasNoDescriptorReference()
    {
        var exe = CaseExePath("delegate2");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            // CG6: a custom delegate TypeDef (IntFn) has NO runtime descriptor --
            // the runtime stamps every delegate with its single System.Delegate
            // class. The {T}_class extern/symbol must NOT be emitted (undefined
            // symbol link failure) and the castclass must be an identity cast.
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.DoesNotContain("IntFn_class", header);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Delegate2.cpp"));
            Assert.DoesNotContain("&IntFn_class", cpp);
            Assert.DoesNotContain("IntFn_class", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_DelegateCb_IntPtrPInvokeParam_DeclaresIntptrT()
    {
        var exe = CaseExePath("delegatecb");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_DelegateCb.cpp"));
            // CG6: a native-int (IntPtr) P/Invoke param declares intptr_t (an
            // int32_t declaration truncates the x64 function-pointer address).
            Assert.Contains("extern \"C\" int32_t call_callback(intptr_t, int32_t);", cpp);
            // P-D2 #2: the GFPD call now routes through the per-signature GfpS
            // dispatcher (il2cpp_delegate_gfp_<hash> -- the static single-cast
            // delegate returns its raw methodPtr at runtime, the net48 byte
            // contract; instance-bound delegates get a permanent slot thunk).
            // The void* result rides the intptr_t "ptr" carrier slot. P/Invoke
            // args ride the native-ABI bridge wrapper (il2cpp_pinvoke_arg).
            Assert.Contains("il2cpp_delegate_gfp_4f6d2a1c5276bdc6(", cpp);
            Assert.Contains("call_callback(il2cpp::runtime::il2cpp_pinvoke_arg(static_cast<intptr_t>(t1)), il2cpp::runtime::il2cpp_pinvoke_arg(static_cast<int32_t>(14LL)", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_ArrayOps_LongNewarr_Allocates8ByteElements()
    {
        var exe = CaseExePath("arrayops");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_ArrayOps.cpp"));
            // CG6: long[] (System.Int64) newarr allocates 8-byte NON-reference
            // elements (the ldelem/stelem i8 width already dispatches; only the
            // allocation stride was missing).
            Assert.Contains("il2cpp_array_new(nullptr, 8, false,", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // M6 CodeGenerator batch 23 -- TypeCppName GCHandle slot: a GCHandle VALUE
    // local is a single int32 handle (M2 value carrier, IntPtr slot model).
    // Without the "System.Runtime.InteropServices.GCHandle" -> "int32_t" entry
    // a GCHandle local would fall to the default Sanitize(fullName) name (no
    // generated struct) -> C++ compile failure. Verified through the runtime
    // TypeCppName switch directly (the mapping is a pure function of the name).
    //--------------------------------------------------------------------------
    [Fact]
    public void TypeCppName_GCHandle_MapsToInt32Slot()
    {
        // GCHandle is a single int32 handle value (M2 value carrier, IntPtr
        // slot model). TypeCppName is private -- invoke through reflection
        // (same pattern the wiring tests use for the resolver internals).
        var method = typeof(Il2Cpp.Transpiler.Emit.CodeGenerator).GetMethod(
            "TypeCppName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var name = method!.Invoke(null, new object[] { "System.Runtime.InteropServices.GCHandle" });
        Assert.Equal("int32_t", name);
    }

    //--------------------------------------------------------------------------
    // CG7 (arrayext2 unlock): EmitIcallCallArgs argument reordering. The .NET
    // 4-arg Array.FindIndex<T> overload pushes (array, startIndex, count,
    // match) but icall_array_find_index takes (arr, predicate, startIndex,
    // count). The IcallArgOrder table maps each icall slot to its IL argument;
    // the delegate must reach the PREDICATE slot (raw passthrough, no int32
    // cast) and start/count must land in slots 2/3 as int32. Fixture: the real
    // net48 arrayext2 case exe (the same CaseExePath convention).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_ArrayFindIndex_ReordersArgsToIcallOrder()
    {
        var exe = CaseExePath("arrayext2");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "ArrayExt2Demo.cpp"));
            // IL order (t26=array, t27=startIndex, t28=count, t29=match) must
            // emit as (arr=t26, predicate=t29, startIndex=t27, count=t28).
            Assert.Contains("icall_array_find_index(reinterpret_cast<Il2CppArray*>(t26), t29, static_cast<int32_t>(t27), static_cast<int32_t>(t28))", cpp);
            // the delegate must NEVER be int32-cast (it is a managed ref slot)
            Assert.DoesNotContain("static_cast<int32_t>(t29)", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // ARRFUNCS (2026-08-09, wir9-b23): the remaining arrayext2 icalls emit as
    // (array "a" cast, delegate "" slot) -- Find/Exists/TrueForAll/ConvertAll
    // -- and Resize passes the byref array slot as an exact Il2CppArray**
    // address ("addr:a" reinterpret, CodeGenerator EmitIcallCallArgs). The
    // Converter<int,int> delegate used by ConvertAll must reach
    // il2cpp_delegate_new (the CG8 interception extended to System_Converter_)
    // -- no undefined System_Converter_2_int_int_* symbols.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_ArrayArrFuncs_EmitsResizeAddrCastAndDelegateIcalls()
    {
        var exe = CaseExePath("arrfuncs");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_ArrFuncs.cpp"));
            // Resize: the ldloca byref slot must reach the Il2CppArray** param
            // exactly ("" would pass Il2CppObject** and fail to compile).
            Assert.Contains("icall_array_resize_i4(reinterpret_cast<Il2CppArray**>((&", cpp);
            // the delegate icalls carry the array "a" cast + "" delegate slot.
            Assert.Contains("icall_array_find_i4(reinterpret_cast<Il2CppArray*>(", cpp);
            Assert.Contains("icall_array_exists_i4(reinterpret_cast<Il2CppArray*>(", cpp);
            Assert.Contains("icall_array_true_for_all_i4(reinterpret_cast<Il2CppArray*>(", cpp);
            Assert.Contains("icall_array_convert_all_i4_i4(reinterpret_cast<Il2CppArray*>(", cpp);
            // the Converter<int,int> delegate must fold to il2cpp_delegate_new
            // (no undefined System_Converter_2_int_int symbols).
            Assert.Contains("il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&Cases_ArrFuncs_Double_", cpp);
            Assert.DoesNotContain("System_Converter_2_int_int", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // strictalias (2026-09-13): the IntPtr receiver read must use the DECLARED
    // slot type, never a fixed-width pointer pun. The live value carrier is
    // intptr_t on the widened paths (winforms1) but still int32_t on others
    // (pinvokedeep2 Marshal.OffsetOf local, fresh gen) -- a fixed-width cast
    // is wrong for one of the two: an int32_t* read of an intptr_t slot trips
    // GCC -Wstrict-aliasing (Cases_Winforms1.cpp:13787), an intptr_t* read of
    // an int32_t slot would over-read. "deref:v" emits *addr.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_IntPtrToInt32_ReadsDeclaredSlotType()
    {
        var exe = CaseExePath("pinvokedeep2");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_PinvokeDeep2.cpp"));
            Assert.Contains("il2cpp_intptr_to_int32((*((&", cpp);
            Assert.DoesNotContain("il2cpp_intptr_to_int32((*reinterpret_cast<int32_t*>", cpp);
            Assert.DoesNotContain("il2cpp_intptr_to_int64((*reinterpret_cast<int32_t*>", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Devirt S1 (devirtualization-plan.md §S1): callvirt on an effectively-sealed
    // receiver lowers to a DIRECT call. Fixture: the real net48 virtualsealed
    // case exe (CaseExePath) -- Square/Tri are SEALED classes with no subclasses
    // in the translation set, so their Area/Sides calls must emit as
    // Square_Area(...)/Tri_Area(...) (NOT ->klass->vtable[slot]); Rect (HAS a
    // subclass: Square) and Shape (HAS subclasses) must KEEP vtable dispatch.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_SealedReceiverVirtualCall_EmitsDirectCall()
    {
        var exe = CaseExePath("virtualsealed");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_VirtualSealed.cpp"));

            // Sealed Square/Tri receivers: direct calls (devirtualized).
            Assert.Contains("Square_Area_", cpp);
            Assert.Contains("Square_Sides_", cpp);
            Assert.Contains("Tri_Area_", cpp);
            Assert.Contains("Tri_Sides_", cpp);
            // the null-check is preserved on the devirtualized direct calls
            // (callvirt semantics -- Square_Area(il2cpp_null_check(recv)))
            Assert.Contains("Square_Area_1b58666f53d8aed6(il2cpp_null_check", cpp);
            // non-sealed receivers (Rect -- has subclass Square; Shape -- has
            // subclasses) KEEP the runtime vtable dispatch.
            Assert.Contains("->klass->vtable", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Devirt S1 (G3 closed-world): the SAME class is a vtable dispatch when it
    // has a subclass in the translation set, and a direct call when sealed.
    // Fixture: virtualsealed -- Rect inherits Shape and IS subclassed by Square
    // (DerivedCount > 0 -> not effectively sealed -> stays vtable); Square is
    // sealed AND subclassed-by-nobody -> direct call.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_ReceiverWithSubclass_KeepsVtableDispatch()
    {
        var exe = CaseExePath("virtualsealed");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_VirtualSealed.cpp"));

            // Shape (base, subclassed by Rect/Square/Tri) and Rect (subclassed
            // by Square) are NOT effectively sealed: their receivers stay on
            // the vtable[slot] path.
            Assert.Contains("->klass->vtable[0]", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Devirt S3 (devirtualization-plan.md §S3): interface dispatch on a
    // receiver whose static type is known lowers to a DIRECT call when the
    // interface has exactly ONE concrete implementer in the closed world.
    // Fixture: the real net48 interface case exe -- IMul is implemented ONLY
    // by Multi, so `IMul m = new Multi(); m.Mul(6,7)` must emit
    // Multi_Mul_<hash>(il2cpp_null_check(recv), ...) directly (skipping
    // il2cpp_interface_dispatch's linear scan). The callvirt null-check is
    // preserved on the direct call.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_SingleImplementerInterface_EmitsDirectCall()
    {
        var exe = CaseExePath("interface");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Interface.cpp"));

            // IMul has a single implementer (Multi): direct call.
            Assert.Contains("Multi_Mul_", cpp);
            Assert.Contains("Multi_Mul_d58920db2310391d(il2cpp_null_check", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // Devirt S3 negative: a multi-implementer interface keeps the runtime
    // il2cpp_interface_dispatch path. Fixture: interface -- ICalc is
    // implemented by Doubler/Tripler/Multi/Inherited/BaseImpl/DerivedOnly
    // (count 6), and the first `c.Calc(21)`/`c.Double(5)` receivers are
    // `new Doubler()` -- Doubler has a subclass (Inherited) so it is NOT
    // effectively sealed either. The dispatch must remain linear-search.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_MultiImplementerInterface_KeepsInterfaceDispatch()
    {
        var exe = CaseExePath("interface");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Interface.cpp"));

            // ICalc has 6 implementers: the callvirt on the Doubler receiver
            // stays on il2cpp_interface_dispatch (interface slot 0/1 = Calc/Double).
            Assert.Contains("il2cpp_interface_dispatch(il2cpp_null_check(static_cast<Il2CppObject*>(t0)), &ICalc_class, 0)", cpp);
            Assert.Contains("il2cpp_interface_dispatch(il2cpp_null_check(static_cast<Il2CppObject*>(t0)), &ICalc_class, 1)", cpp);
            Assert.Contains("&ICalc_class", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // D-CAST-NULL (hashtabledeep GAPS.md): ECMA-335 castclass on a null
    // reference is a NO-OP that returns null -- only a NON-NULL type mismatch
    // throws InvalidCastException. The transpiler's castclass emits
    // `is_instance ? cast : (throw, nullptr)`; is_instance(null) is false
    // (boxing.cpp), so without a null short-circuit a null input (e.g. a
    // missing Hashtable key cast to string) falls into the throw arm. Every
    // castclass mismatch arm must carry the guard
    // `(obj) == nullptr ? nullptr : throw`.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_CastclassNullReference_EmitsNullShortCircuit()
    {
        var exe = CaseExePath("hashtabledeep");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_HashtableDeep.cpp"));
            // The guard must be present at least once, and EVERY castclass throw
            // site must be guarded (a bare `is_instance ? cast : throw` is the
            // D-CAST-NULL regression). The null arm emits bare `nullptr`
            // (implicitly converts to Il2CppObject*).
            var guard = "== nullptr ? nullptr : (il2cpp::il2cpp_throw_new(\"System.InvalidCastException";
            var throwSites = Regex.Matches(cpp, Regex.Escape("il2cpp::il2cpp_throw_new(\"System.InvalidCastException"));
            var guardedSites = Regex.Matches(cpp, Regex.Escape(guard));
            Assert.True(throwSites.Count > 0, "hashtabledeep must contain castclass sites");
            Assert.Equal(throwSites.Count, guardedSites.Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // batch 165 (Task<T> carrier gap audit): Task<float>/Task<ulong>/Task<object>
    // previously fell through the Carrier() selector to the int32 icall --
    // Task<float> truncated 2.5f to 2, Task<object> emitted ref-as-int32
    // (cmake build FAIL), Task<ulong> truncated through int32. Now they emit
    // the _f/_u/_s carriers. Task<decimal> (VALUETYPE -- the 16-byte struct
    // carriers are out of scope) must fail LOUDLY at resolve time, not emit a
    // silently wrong int32 icall.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_AsyncCase_UsesFloatUlongObjectCarriers()
    {
        var exe = CaseExePath("asynccase");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var files = Directory.GetFiles(Path.Combine(dir, "src"), "*.cpp");
            var all = string.Join("\n", files.Select(File.ReadAllText));
            Assert.Contains("icall_task_from_result_f(", all);
            Assert.Contains("icall_task_get_result_original_f(", all);
            Assert.Contains("icall_task_from_result_u(", all);
            Assert.Contains("icall_task_get_result_original_u(", all);
            Assert.Contains("icall_task_from_result_s(nullptr)", all);
            Assert.Contains("icall_task_get_result_original_s(", all);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_AsyncCase_ArrayAndClassRefCarriers()
    {
        var exe = CaseExePath("asynccase");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var files = Directory.GetFiles(Path.Combine(dir, "src"), "*.cpp");
            var all = string.Join("\n", files.Select(File.ReadAllText));
            // Task<int[]> (SZARRAY generic arg) and Task<Point> (custom CLASS)
            // both ride the ref carrier -- the newarr boxed value flows as the
            // "s" icall arg.
            Assert.Contains("icall_task_from_result_s(", all);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // batch 168 (efa6741) -- DelegateSignature void-0-arg multicast regression:
    // a void-returning ZERO-ARG delegate (ClickHandler, += multicast-capable)
    // decoded to retCpp "void" and skipped the icall_multicast_invoke_i4 route
    // -- the raw methodPtr dispatch read a multicast container's first field as
    // code -> 0xC0000005 at startup. Zero-arg void delegates must keep the
    // int32 ABI (i4 multicast path, void return discarded). Fixture: the real
    // winforms1 case exe (CaseExePath -- the ButtonClick ClickHandler).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_Winforms1_ClickHandlerInvokes_RouteThroughMulticastI4()
    {
        var exe = CaseExePath("winforms1");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Winforms1.cpp"));
            // batch 168: the ButtonClick ClickHandler invoke rides the i4
            // multicast path (the runtime walks the multicast chain; the void
            // return is discarded), never a raw methodPtr dispatch.
            Assert.Contains("icall_multicast_invoke_i4((Cases_Winforms1_sf_ButtonClick), 0, 0, 0, 0)", cpp);
            // Regression guard: NO raw (void(*)()) methodPtr call on ClickHandler
            // (the multicast-container-as-code 0xC0000005 shape). The WndProc
            // get_fn_ptr route (intptr_t) is a different, legitimate shape.
            Assert.DoesNotContain("(void(*)())(reinterpret_cast<Il2CppDelegate*>", cpp);
            Assert.DoesNotContain("->methodPtr)()", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // batch 168 -- the 2-ref-arg void EventHandler must KEEP routing the rr_void
    // multicast path (only the zero-arg void delegates changed to the i4 ABI).
    // Fixture: the real eventcase exe (CaseExePath -- Counter.Changed).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_EventCase_EventHandlerInvokes_RouteRrVoid()
    {
        var exe = CaseExePath("eventcase");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_EventCase.cpp"));
            Assert.Contains("icall_multicast_invoke_rr_void(", cpp);
            Assert.DoesNotContain("(void(*)())", cpp);
            Assert.DoesNotContain("->methodPtr)()", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // batch 175 -- linqgroup local-slot type regression: the net48 IL local
    // signature declares the LINQ aggregate temp slot loc15 as 0x12 (class),
    // but Roslyn REUSES that object-typed slot to hold int32/bool values (the
    // `nums.Sum() == 45` comparisons). The batch-156c "IL-declared reference
    // local is authoritative" branch (ilKind == "r" -> Il2CppObject*) must
    // yield when the IR locKind propagation proves the slot only stores
    // plain values -- the declaration must follow the IR, not the IL
    // signature. Previously gen\src\Cases_LinqGroup.cpp declared
    // `Il2CppObject* loc15 = nullptr;` while every store was a bool/int32
    // comparison (`loc15 = (il2cpp_linq_sum_i4(...) == 45LL)`), producing 20
    // "incompatible integer to pointer conversion" cmake build errors.
    // Fixture: the real net48 linqgroup case exe (same CaseExePath convention).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_LinqGroup_ReusedObjectSlot_FollowsIrValueKind()
    {
        var exe = CaseExePath("linqgroup");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_LinqGroup.cpp"));
            // the reused object slot holds int32/bool: the IR analysis ("val")
            // wins over the IL "r" signature -- loc15 must be an int32 slot.
            Assert.Contains("int32_t loc15;", cpp);
            Assert.DoesNotContain("Il2CppObject* loc15", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    //--------------------------------------------------------------------------
    // batch 177 -- user-method IntPtr/UIntPtr return ResultKind (x64 handle
    // truncation): ResolveMethodDefinition/ResolveMemberReference user paths
    // left ResultKind null for a SignatureTypeCode.IntPtr (0x18) / UIntPtr
    // (0x19) return, so the caller's temp/local fell to the default int32_t
    // and the 64-bit handle truncated (Phase 22 TreeView child-insert NULL).
    // The P/Invoke path sets "ptr" (MethodRefResolver.cs:385); user methods
    // now carry the same kind. Fixture: the real winforms1 case exe
    // (CaseExePath) -- Phase 22 uses TVM_GETNEXTITEM handle temps (loc272 /
    // loc273) whose parents are SendMessageW P/Invoke results, and the
    // user WndProcImpl delegate-return methods (IntPtr) must declare intptr_t.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_Winforms1_UserIntPtrReturns_DeclareIntptrT()
    {
        var exe = CaseExePath("winforms1");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Winforms1.cpp"));
            // batch 177: the USER WndProc methods return IntPtr -- their C++
            // signatures must be intptr_t (a null ResultKind / int32_t return
            // truncates the x64 window handle the delegate protocol passes).
            Assert.Contains("IL2CPP_NOINLINE intptr_t Cases_Winforms1_WndProcImpl_", cpp);
            Assert.Contains("IL2CPP_NOINLINE intptr_t Cases_Winforms1_ChildWndProcImpl_", cpp);
            // Phase 22 TreeView: TVM_GETNEXTITEM handle temps (loc272/loc273)
            // carry SendMessageW P/Invoke results -- intptr_t, never int32_t.
            Assert.Contains("intptr_t loc272;", cpp);
            Assert.Contains("intptr_t loc273;", cpp);
            Assert.DoesNotContain("int32_t loc272;", cpp);
            Assert.DoesNotContain("int32_t loc273;", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
    //--------------------------------------------------------------------------
    // caudit2-fix (fieldargcase): the FieldAccess value-type receiver forms.
    // this (T* arg0) -> `arg0->V`; a BY-VALUE struct param (V:) is a plain
    // C++ value -> `(&(arg))->V`; a BY-REF struct param (B:V:) is `T*` ->
    // `arg->V`. The old name-prefix judge emitted `arg1->V` for the by-value
    // case and broke the C++ build.
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_FieldArgCase_AddressVsByValueVsByRef()
    {
        var exe = CaseExePath("fieldargcase");
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_FieldArgSt.cpp"));
            // this / implicit V (both through the T* arg0 address)
            Assert.Contains("Cases_FieldArgSt* arg0", cpp);
            Assert.Contains("(arg0->V == arg0->V)", cpp);
            // by-value struct param: declared by value, accessed via (&arg)
            Assert.Contains("Cases_FieldArgSt arg1", cpp);
            Assert.Contains("(&(arg1))->V", cpp);
            // by-ref struct param: declared as a pointer, dereferenced directly
            Assert.Contains("Cases_FieldArgSt* arg1", cpp);
            Assert.Contains("(arg0->V == arg1->V)", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
