using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap dispatch tests: prove the BclResolveCore owner->domain switch
// (the "#region BCL domain dispatch" in MethodRefResolver.cs) routes an
// EXISTING mapping to its MethodMap.<Domain>.cs TryMap method, and that a
// dispatch MISS falls back to the generated-name path.
//
// Coverage contract: every wired domain gets at least one positive dispatch
// assertion (Math/Console/Convert+BitConverter/Format/ValueType/String/
// Object/IConvertible/MemberInfo/StringBuilder/Hashtable/Random/Environment/
// IO/GC/Delegate/Array/DateTime/TimeSpan/Thread/CultureInfo/WeakReference/
// Exception), plus loud-rejection assertions for known wiring gaps
// (NotSupportedException with the "{owner}::{name}" prefix -- see
// BclResolve's wrap).
//
// No fixture calls these methods, and tests/fixtures is read-only for this
// task, so the PEs are SYNTHESIZED with System.Reflection.Metadata's own
// writer (MetadataBuilder + ManagedPEBuilder -- part of the net8.0 shared
// framework, no new packages): a real ECMA-335 assembly whose only
// MemberReference is the member under test (respectively Acme.Util::Nope for
// the fallback). The reader side (PEReader + MetadataReader) is the same
// pipeline the transpiler uses. Signature blobs are built with Sig(ret,
// params...) -- the raw bytes RawSignature consumes (0x08=i4, 0x0A=i8,
// 0x0D=f8, 0x0E=string, 0x02=bool, 0x03=char, 0x1C=object, 0x1D=array).
public class MethodRefResolverDispatchTests
{
    [Fact]
    public void Resolve_MathAbs_MapsToIcall_ThroughMethodMapDispatch()
    {
        // Given: an assembly whose only MemberReference is Math.Abs(int32)
        using var pe = new PEReader(Synthesize("System", "Math", "Abs",
            new byte[] { 0x00, 0x01, 0x08, 0x08 })); // static, 1 param, i4->i4
        var md = pe.GetMetadataReader();

        // When: resolved through the public Resolve entry point
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == "Abs");
        var resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());

        // Then: the Math domain mapping (MethodMap.Math.cs -> TryMapMath) hits
        Assert.Equal("il2cpp::runtime::icall_math_abs_i", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_UnmappedOwner_FallsBackToGeneratedName()
    {
        // Given: a MemberReference on an owner with no MethodMap domain
        using var pe = new PEReader(Synthesize("Acme", "Util", "Nope",
            new byte[] { 0x00, 0x01, 0x08, 0x08 }));
        var md = pe.GetMetadataReader();

        // When: resolved through the public Resolve entry point
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == "Nope");
        var resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());

        // Then: dispatch miss -> generated-name path (owner_sanitized_name_<hash>)
        Assert.StartsWith("Acme_Util_Nope_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Static/instance classification of the FALLBACK path (work order #44).
    // The name heuristic (`get_*` => static, everything else => instance) is
    // wrong for the CLI: instance getters are static by name but their
    // signature header carries HAS_THIS (0x20). The header bit is
    // authoritative -- an instance getter with 0x20 classifies as INSTANCE.
    // (The batch-A probe used System.Delegate::get_Method RID 19, but M7
    // mapped that member LOUD in MethodMap.Delegate.cs -- this test now uses
    // a synthetic unmapped instance getter to keep covering the fallback.)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_InstanceGetter_HasThisHeader_ClassifiesInstance()
    {
        // Given: an unmapped owner's instance getter -- a MemberRef whose
        // header carries HAS_THIS (0x20) and which has NO MethodMap mapping
        // (falls to the heuristic)
        using var pe = new PEReader(Synthesize("Acme", "Util", "get_Status",
            new byte[] { 0x20, 0x00, 0x08 })); // HAS_THIS, 0 params, int32 return
        var md = pe.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == "get_Status");

        // When: resolved through the public Resolve entry point
        var resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());

        // Then: the HAS_THIS header classifies the instance getter as INSTANCE
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.IsConsoleWriteLine);
        Assert.StartsWith("Acme_Util_get_Status_", resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_StaticOperator_NoHasThisHeader_ClassifiesStatic()
    {
        // Given: an unmapped owner's op_Equality -- the enumdeep shape, a
        // static operator whose MemberRef header carries NO HAS_THIS (0x00)
        using var pe = new PEReader(Synthesize("Acme", "Util", "op_Equality",
            new byte[] { 0x00, 0x02, 0x02, 0x08, 0x08 })); // static bool op_Equality(int32, int32)
        var md = pe.GetMetadataReader();

        // When: resolved through the public Resolve entry point
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == "op_Equality");
        var resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());

        // Then: the absence of HAS_THIS classifies the static operator as STATIC
        Assert.False(resolved.IsInstance);
        Assert.StartsWith("Acme_Util_op_Equality_", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Math domain (MethodMap.Math.cs) -- a second member with a non-int result
    // kind, proving the icall name AND the ResultKind both propagate.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_MathSqrt_MapsDoubleResult()
    {
        var resolved = ResolveMember(Synthesize("System", "Math", "Sqrt", Sig(0x0D, 0x0D)), "Sqrt");

        Assert.Equal("il2cpp::runtime::icall_math_sqrt_d", resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Console domain (MethodMap.Console.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Console_WriteLineString_MapsToWriteLineString()
    {
        var resolved = ResolveMember(Synthesize("System", "Console", "WriteLine", Sig(0x01, 0x0E)), "WriteLine");

        Assert.Equal("il2cpp::runtime::icall_console_write_line_s", resolved.CppName);
        Assert.True(resolved.IsConsoleWriteLine);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Console_WriteLineObject_TrackedBox_MapsToUnboxedIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Console", "WriteLine", Sig(0x01, 0x1C)), "WriteLine",
            arg0Type: "System.Int32");

        Assert.Equal("il2cpp::runtime::icall_console_write_line_i", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Console_WriteLineObject_UntrackedBox_MapsToObjectPrinter()
    {
        // hashtable2/hashtablecase (2026-08-09): WriteLine(object) with an
        // UNTRACKED box -- the runtime object printer
        // icall_console_write_line_object (il2cpp_console.h:89) wraps
        // icall_object_to_string and writes (boxed primitives print their
        // VALUE text, strings print themselves, other objects the type name,
        // null prints only the line terminator).
        var resolved = ResolveMember(Synthesize("System", "Console", "WriteLine", Sig(0x01, 0x1C)), "WriteLine");

        Assert.Equal("il2cpp::runtime::icall_console_write_line_object", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.False(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Convert domain (MethodMap.Convert.cs -- System.Convert + System.BitConverter)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Convert_ToInt32String_MapsToStringParseIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Convert", "ToInt32", Sig(0x08, 0x0E)), "ToInt32");

        Assert.Equal("il2cpp::runtime::icall_convert_to_int32_string", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // M7 parsecase (2026-08-08): Convert.ToInt32(string, fromBase) -- the
    // convt2 audit added the THROWING icall_convert_to_int32_string_base
    // (il2cpp_convert.h) reproducing the net48 ParseNumbers.StringToInt
    // contract (base check -> null -> empty -> sign -> 0x prefix -> digits;
    // FormatException/OverflowException/ArgumentException per radix rule), so
    // the 2-arg form now maps to it instead of failing loud.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Convert_ToInt32StringFromBase_MapsToThrowingIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Convert", "ToInt32", Sig(0x08, 0x0E, 0x08)), "ToInt32");

        Assert.Equal("il2cpp::runtime::icall_convert_to_int32_string_base", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Convert_ToStringIntBase_MapsToThrowingIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Convert", "ToString", Sig(0x0E, 0x08, 0x08)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_convert_to_string_i4_base", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "i", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_BitConverter_DoubleToInt64Bits_MapsToBitsIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "BitConverter", "DoubleToInt64Bits", Sig(0x0A, 0x0D)),
            "DoubleToInt64Bits");

        Assert.Equal("il2cpp_double_bits", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
        Assert.True(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_Convert_ToCharString_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Convert", "ToChar", Sig(0x03, 0x0E)), "ToChar"));

        Assert.Contains("System.Convert::ToChar", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Format domain (MethodMap.Format.cs -- Int32/Int64/Double ToString(format);
    // the dispatch switch tries Format BEFORE ValueType for these owners).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Int32_ToStringWithFormat_MapsToFormatDomain()
    {
        var resolved = ResolveMember(Synthesize("System", "Int32", "ToString", Sig(0x0E, 0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_format_int32_custom", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // ValueType domain (MethodMap.ValueType.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Int32_ToStringParameterless_MapsToValueTypeDomain()
    {
        var resolved = ResolveMember(Synthesize("System", "Int32", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_int32_to_string", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Byte_ToStringParameterless_MapsToInt32Formatter()
    {
        // M8 parsecase unblock (2026-08-08): Byte.ToString() -- a byte lives in
        // an INT32 slot, so the parameterless form formats the slot's value via
        // icall_int32_to_string (byte range 0..255 -> identical decimal output).
        var resolved = ResolveMember(Synthesize("System", "Byte", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_int32_to_string", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Int32_TryParse_MapsToTryParseIcall()
    {
        // bool TryParse(string, out int): 0x10 BYREF consumes the element byte
        // (ps carries 0x08), so the M7 ref/out wiring selects
        // icall_try_parse_int32 (il2cpp_parse.h); the out address passes
        // through with the typed "addr:i" cast.
        var resolved = ResolveMember(Synthesize("System", "Int32", "TryParse",
            new byte[] { 0x00, 0x02, 0x02, 0x0E, 0x10, 0x08 }), "TryParse");

        Assert.Equal("il2cpp::runtime::icall_try_parse_int32", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        // numchain fix (2026-08-07): the try_parse text param is Il2CppObject*
        // (il2cpp_parse.h:75), NOT Il2CppString* -- "" passthrough, never "s".
        Assert.Equal(new[] { "", "addr:i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // String domain (MethodMap.String.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_String_StartsWith_MapsToStartsWithIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "StartsWith", Sig(0x02, 0x0E)), "StartsWith");

        Assert.Equal("il2cpp::runtime::icall_string_startswith", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_String_OpInequality_MapsToNegatedEqualsIcall()
    {
        // string != string: the runtime has no inequality icall -- the
        // mapping emits the ordinal equality icall under a C++ logical-NOT
        // (CppName is placed in front of the argument list, so the prefix
        // binds to the whole call: !f(a, b) == !(f(a, b))).
        var resolved = ResolveMember(Synthesize("System", "String", "op_Inequality", Sig(0x02, 0x0E, 0x0E)), "op_Inequality");

        Assert.Equal("!il2cpp::runtime::icall_string_equals_static", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { false, false }, resolved.ParamIsRef);
    }

    //--------------------------------------------------------------------------
    // Object domain (MethodMap.Object.cs -- System.Object / System.IConvertible /
    // System.Reflection.MemberInfo)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Object_GetHashCode_MapsToHashCodeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Object", "GetHashCode", Sig(0x08)), "GetHashCode");

        Assert.Equal("il2cpp::runtime::icall_object_get_hash_code", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_MemberInfo_GetName_OnTypeReceiver_MapsToShortNameIcall()
    {
        // MemberInfo::get_Name on a GetType() result: the receiver is the
        // FULL-name string the GetType icall produces; .NET Type.Name is the
        // SHORT name, so the mapping routes to the runtime truncation
        // primitive (il2cpp_type_get_short_name, il2cpp_typecheck.h) -- not
        // the old identity (the boxing2/boxdeep GetType mismatch).
        var resolved = ResolveMember(Synthesize("System.Reflection", "MemberInfo", "get_Name", Sig(0x0E)),
            "get_Name", receiverType: "System.Type");

        Assert.Equal("il2cpp::runtime::il2cpp_type_get_short_name", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // M7 gcext (2026-08-09): System.Object::Finalize -- the implicit base call
    // every C# destructor emits at the end of its Finalize body. .NET
    // Object.Finalize is an empty virtual no-op; the runtime ships the
    // receiver-accepting no-op il2cpp_object_ctor_nop (runtime.cpp:102, the
    // documented Object::.ctor target), so the Finalize base call folds to it
    // (the receiver passes through the "" slot; the icall ignores it -- the
    // empty-finalizer semantic).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Object_Finalize_MapsToNoOpIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Object", "Finalize", Sig(0x01)), "Finalize");

        Assert.Equal("il2cpp::runtime::il2cpp_object_ctor_nop", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // StringBuilder domain (MethodMap.StringBuilder.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_StringBuilder_AppendString_ReturnsStringBuilder()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "Append", Sig(0x0E, 0x0E)), "Append");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_s", resolved.CppName);
        Assert.Equal("System.Text.StringBuilder", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Hashtable domain (MethodMap.Hashtable.cs -- parameterless ctor injects a
    // zero capacity argument: AppendZeroArg -- M7R3, the runtime signature is
    // icall_hashtable_ctor(Il2CppObject* self, int32_t capacity), so the
    // injected 0 must follow the receiver, not precede it).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Hashtable_CtorParameterless_AppendsZeroArgument()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Hashtable", ".ctor", Sig(0x01)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_hashtable_ctor", resolved.CppName);
        Assert.False(resolved.PrependZeroArg);
        Assert.True(resolved.AppendZeroArg);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    // M7 hash2 (2026-08-08): Hashtable.Keys / Hashtable.Values -> the runtime
    // snapshot icalls (il2cpp_hashtable.h get_keys/get_values): reference
    // arrays in the table's stable enumeration order, Keys[i]<->Values[i]
    // aligned. Ret is object[] (0x1D SZARRAY + 0x1C object element).
    [Fact]
    public void Resolve_Hashtable_GetKeys_MapsToKeysIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Hashtable", "get_Keys",
            new byte[] { 0x00, 0x00, 0x1D, 0x1C }), "get_Keys");

        Assert.Equal("il2cpp::runtime::icall_hashtable_get_keys", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Hashtable_GetValues_MapsToValuesIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "Hashtable", "get_Values",
            new byte[] { 0x00, 0x00, 0x1D, 0x1C }), "get_Values");

        Assert.Equal("il2cpp::runtime::icall_hashtable_get_values", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    // M7 hash2 (2026-08-08): SortedList indexer SET -> il2cpp_sortedlist_set_by_key
    // (sortedlist.h:136) -- replaces an existing key's value or inserts at the
    // sorted position (never throws for a missing key, unlike Add).
    [Fact]
    public void Resolve_SortedList_SetItem_MapsToSetByKeyIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Collections", "SortedList", "set_Item", Sig(0x01, 0x1C, 0x1C)),
            "set_Item");

        Assert.Equal("il2cpp::runtime::il2cpp_sortedlist_set_by_key", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Random domain (MethodMap.Random.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Random_Next_MapsToNextIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Random", "Next", Sig(0x08)), "Next");

        Assert.Equal("il2cpp::runtime::icall_random_next", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Environment domain (MethodMap.Environment.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Environment_GetNewLine_MapsToNewLineIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Environment", "get_NewLine", Sig(0x0E)), "get_NewLine");

        Assert.Equal("il2cpp::runtime::icall_environment_get_newline", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // IO domain (MethodMap.IO.cs -- multi-owner: File/Directory/Path)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_File_Exists_MapsToExistsIcall()
    {
        var resolved = ResolveMember(Synthesize("System.IO", "File", "Exists", Sig(0x02, 0x0E)), "Exists");

        Assert.Equal("il2cpp::runtime::icall_file_exists", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Directory_Delete_MapsToDeleteIcall()
    {
        // Directory.Delete(string) is wired on the IO domain
        // (icall_directory_delete, il2cpp_io.h) -- static, status int
        // discarded (ReturnsValue=false).
        var resolved = ResolveMember(Synthesize("System.IO", "Directory", "Delete", Sig(0x01, 0x0E)), "Delete");

        Assert.Equal("il2cpp::runtime::icall_directory_delete", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // GC domain (MethodMap.GC.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_GC_CollectParameterless_MapsToCollectIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "GC", "Collect", Sig(0x01)), "Collect");

        Assert.Equal("il2cpp::runtime::icall_gc_collect", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_GC_CollectWithGeneration_DropsInt32GenerationParam()
    {
        // GC.Collect(int generation): the runtime has only the parameterless
        // full-collection icall (icall_gc_collect, gcapi.cpp), so the int32
        // generation argument is dropped and a full collection runs.
        // CG6 fix (gcops, 2026-08-09): ParamCount MUST be 1 -- the fold pops
        // ParamCount slots at the call, so 0 stranded the int on the shadow
        // stack (cg6 stack-desync); the "drop" cast skips it at emission.
        var resolved = ResolveMember(Synthesize("System", "GC", "Collect", Sig(0x01, 0x08)), "Collect");

        Assert.Equal("il2cpp::runtime::icall_gc_collect", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_GC_GetTotalMemory_ReturnsLongResult()
    {
        var resolved = ResolveMember(Synthesize("System", "GC", "GetTotalMemory", Sig(0x0A, 0x02)), "GetTotalMemory");

        Assert.Equal("il2cpp::runtime::icall_gc_get_total_memory", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // M7 gcext (2026-08-08): GC.WaitForPendingFinalizers() -> the direct
    // finalizer trigger (icall_gc_finalize_trigger, il2cpp_gc_ext.h). Static
    // void 0-param -- the same shape as the parameterless Collect; the runtime
    // surface was verified present (gcext.cpp:181, test_gcext.cpp §9) before
    // wiring.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_GC_WaitForPendingFinalizers_MapsToFinalizeTriggerIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "GC", "WaitForPendingFinalizers", Sig(0x01)),
            "WaitForPendingFinalizers");

        Assert.Equal("il2cpp::runtime::icall_gc_finalize_trigger", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Delegate domain (MethodMap.Delegate.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Delegate_Combine_MapsToCombineIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Delegate", "Combine", Sig(0x1C, 0x0E, 0x0E)), "Combine");

        Assert.Equal("il2cpp::runtime::icall_delegate_combine", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Delegate_RemoveAll_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Delegate", "RemoveAll", Sig(0x1C, 0x0E, 0x0E)), "RemoveAll"));

        Assert.Contains("System.Delegate::RemoveAll", ex.Message);
    }

    //--------------------------------------------------------------------------
    // M7 delegate2 (2026-08-08): Delegate::get_Method -- the deleg2 audit
    // added icall_delegate_get_method_name (delegateext.cpp): flattens the
    // multicast chain to the LAST leaf, looks up its SHORT method name in the
    // name registry, and returns a managed System.String (the SAME HAS_THIS
    // MemberRef shape as before -- instance getter, 0 params). The string
    // result feeds the follow-up `.Name` fold (MemberInfo::get_Name on a
    // "System.String" receiver is the IdentityMarker).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Delegate_GetMethod_MapsToMethodNameIcall()
    {
        using var pe = new PEReader(SynthesizeDelegateGetMethod());
        var md = pe.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == "get_Method");

        var resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());

        Assert.Equal("il2cpp::runtime::icall_delegate_get_method_name", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Array domain (MethodMap.Array.cs) -- non-generic Clear + the generic
    // MethodSpecification path (Array.Sort<string>(string[])).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Array_Clear_MapsToClearIcall()
    {
        // void Clear(Array, int, int): the 0x1D SZARRAY param consumes its
        // element byte (0x08), so the count byte is 3 for 4 param bytes
        var resolved = ResolveMember(Synthesize("System", "Array", "Clear",
            new byte[] { 0x00, 0x03, 0x01, 0x1D, 0x08, 0x08, 0x08 }), "Clear");

        Assert.Equal("il2cpp::runtime::icall_array_clear_int32", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(3, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Array_SortGenericString_MapsThroughMethodSpecification()
    {
        // Given: Array.Sort<string>(string[]) -- a MethodSpecification whose
        // generic argument is string (0x0E)
        using var pe = new PEReader(SynthesizeGenericSort());
        var md = pe.GetMetadataReader();

        // When: resolved through the public Resolve entry point on the MethodSpec
        // token (row 1 -- the synthesized PE holds exactly one MethodSpec row)
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        var resolved = MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>());

        // Then: the generic argument dispatch (genArgs[0] == 0x0E) selects the string sort
        Assert.Equal("il2cpp::runtime::icall_array_sort_strings", resolved.CppName);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // M6 (task): user generic method MethodSpec concrete expansion -- the
    // genlist blocker. Id<int>(42) carries a MethodSpec token the BCL-only
    // path could not resolve; the real genlist shape (dump of genlist.exe:
    // MethodSpec sig 0A-01-08, method kind MethodDefinition) is Roslyn's
    // same-assembly form -- a MethodSpec on the MethodDefinition.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_UserGenericMethod_MethodSpecOnMethodDef_ExpandsToDefinitionName()
    {
        // Given: Cases.GenList::Id<T> (static, T->int32) called as Id<int>
        using var pe = new PEReader(SynthesizeUserGenericMethod(memberRefForm: false, genericArg: 0x08));
        var md = pe.GetMetadataReader();
        var defToken = MetadataTokens.GetToken(MetadataTokens.MethodDefinitionHandle(1));
        var specToken = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));

        // When: resolved through the public Resolve entry point
        var def = MethodRefResolver.Resolve(md, defToken, Array.Empty<TypeDef>());
        var spec = MethodRefResolver.Resolve(md, specToken, Array.Empty<TypeDef>());

        // Then: the call-site C++ name equals the definition's generated
        // name (the definition body transpiles as T -> int32_t and the
        // signature encoder maps both VAR and MVAR to "i", so the hash
        // inputs coincide); the method is STATIC (no this pop at the call
        // site); one int32 parameter.
        Assert.Equal(def.CppName, spec.CppName);
        Assert.StartsWith("Cases_GenList_Id_", spec.CppName);
        Assert.False(spec.IsInstance);
        Assert.True(spec.ReturnsValue);
        Assert.Equal(1, spec.ParamCount);
    }

    [Fact]
    public void Resolve_UserGenericMethod_MethodSpecNonInt32Arg_ThrowsLoud()
    {
        // Given: Id<long>("...") -- the model generates only the T->int32
        // default instantiation (the definition body transpiles with T =
        // int32_t) plus the M7 STRING (0x0E) variant (genlist); any other
        // concrete type (int64 here) has no generated function.
        using var pe = new PEReader(SynthesizeUserGenericMethod(memberRefForm: false, genericArg: 0x0A));
        var md = pe.GetMetadataReader();

        // When/Then: loud, self-identifying NotSupportedException
        var ex = Assert.Throws<NotSupportedException>(() =>
            MethodRefResolver.Resolve(md, MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1)), Array.Empty<TypeDef>()));
        Assert.Contains("Cases.GenList", ex.Message);
        Assert.Contains("Id", ex.Message);
    }

    [Fact]
    public void Resolve_UserGenericMethod_IdString_MapsToStringVariant()
    {
        // Given: Id<string>("ok") -- the genlist case. M7 wires a STRING
        // (0x0E) generic argument to the definition's emitted `_string`
        // pass-through variant (CodeGenerator emits `{defCppName}_string`
        // with Il2CppObject* param/return for pass-through generic bodies).
        using var pe = new PEReader(SynthesizeUserGenericMethod(memberRefForm: false, genericArg: 0x0E));
        var md = pe.GetMetadataReader();

        // When: resolved through the public Resolve entry point
        var spec = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1)), Array.Empty<TypeDef>());

        // Then: routes to the `_string` variant -- reference-typed params and
        // return, the "System.String" result type, same static shape.
        Assert.StartsWith("Cases_GenList_Id_", spec.CppName);
        Assert.EndsWith("_string", spec.CppName);
        Assert.False(spec.IsInstance);
        Assert.True(spec.ReturnsValue);
        Assert.True(spec.ReturnsRef);
        Assert.Equal(1, spec.ParamCount);
        Assert.True(spec.ParamIsRef[0]);
        Assert.Equal("System.String", spec.ReturnTypeName);
    }

    [Fact]
    public void Resolve_UserGenericMethod_MethodSpecOnMemberRef_ExpandsToDefinitionName()
    {
        // Given: the MemberRef form of the same call (the shape the genlist
        // probe documented): a MethodSpec on a MemberReference whose parent
        // is the user TypeDef. The definition lives in this assembly, so the
        // expansion must bind to its generated name with the definition's
        // static/instance shape (the name heuristic would misclassify the
        // static Id as instance).
        using var pe = new PEReader(SynthesizeUserGenericMethod(memberRefForm: true, genericArg: 0x08));
        var md = pe.GetMetadataReader();
        var defToken = MetadataTokens.GetToken(MetadataTokens.MethodDefinitionHandle(1));
        var specToken = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));

        var def = MethodRefResolver.Resolve(md, defToken, Array.Empty<TypeDef>());
        var spec = MethodRefResolver.Resolve(md, specToken, Array.Empty<TypeDef>());

        Assert.Equal(def.CppName, spec.CppName);
        Assert.False(spec.IsInstance);
        Assert.Equal(1, spec.ParamCount);
    }

    //--------------------------------------------------------------------------
    // DateTime domain (MethodMap.DateTime.cs -- value type, "dt" result kind)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DateTime_GetUtcNow_ReturnsDateTimeResult()
    {
        var resolved = ResolveMember(Synthesize("System", "DateTime", "get_UtcNow", Sig(0x0A)), "get_UtcNow");

        Assert.Equal("il2cpp::runtime::icall_datetime_utc_now", resolved.CppName);
        Assert.Equal("dt", resolved.ResultKind);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // TimeSpan domain (MethodMap.TimeSpan.cs -- value-type ctor returns the value)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_TimeSpan_CtorTicks_ResultIsReceiver()
    {
        var resolved = ResolveMember(Synthesize("System", "TimeSpan", ".ctor", Sig(0x01, 0x0A)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_timespan_from_ticks", resolved.CppName);
        Assert.True(resolved.ResultIsReceiver);
        Assert.Equal("ts", resolved.ResultKind);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Theory]
    [InlineData("get_TotalMinutes", "il2cpp::runtime::icall_timespan_get_total_minutes")]
    [InlineData("get_TotalHours", "il2cpp::runtime::icall_timespan_get_total_hours")]
    [InlineData("get_TotalDays", "il2cpp::runtime::icall_timespan_get_total_days")]
    [InlineData("get_TotalMilliseconds", "il2cpp::runtime::icall_timespan_get_total_milliseconds")]
    public void Resolve_TimeSpan_TotalGetters_MapToDoubleIcalls(string name, string expectedIcall)
    {
        var resolved = ResolveMember(Synthesize("System", "TimeSpan", name, Sig(0x0D)), name);

        Assert.Equal(expectedIcall, resolved.CppName);
        Assert.Equal("d", resolved.ResultKind);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_TimeSpan_ToString_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "TimeSpan", "ToString", Sig(0x0E)), "ToString"));

        Assert.Contains("System.TimeSpan::ToString", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Thread domain (MethodMap.Thread.cs)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Thread_Sleep_MapsToSleepIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Threading", "Thread", "Sleep", Sig(0x01, 0x08)), "Sleep");

        Assert.Equal("il2cpp::runtime::icall_thread_sleep", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Thread_GetCurrentThread_MapsToIcall()
    {
        // get_CurrentThread: CLASS return (0x12 + the TypeDefOrRef coded
        // index of the owner TypeRef, row 1 -> 0x05), zero params -- the
        // threadobj domain icall (MethodMap.Thread.cs).
        var resolved = ResolveMember(Synthesize("System.Threading", "Thread", "get_CurrentThread",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "get_CurrentThread");

        Assert.Equal("il2cpp::runtime::icall_threadobj_get_current_thread", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Threading.Thread", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // CultureInfo domain (MethodMap.CultureInfo.cs -- C42 cultname-b23: the
    // get_CurrentCulture fold became a runtime icall reading the system
    // culture -- zh-CN on a zh-CN box, not a fixed literal)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_CultureInfo_GetCurrentCulture_MapsToIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Globalization", "CultureInfo", "get_CurrentCulture", Sig(0x1C)),
            "get_CurrentCulture");

        Assert.Equal("il2cpp::runtime::il2cpp_culture_get_current", resolved.CppName);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // WeakReference domain (MethodMap.WeakReference.cs -- the gcext object
    // closure: .ctor(object) is wired)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_WeakReference_Ctor_MapsToCtorIcall()
    {
        // .ctor(object target): the handle-table object model owns the
        // weak-target handle (il2cpp_gc_ext.h) -- icall_weakref_ctor.
        var resolved = ResolveMember(Synthesize("System", "WeakReference", ".ctor", Sig(0x01, 0x1C)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_weakref_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Exception domain (MethodMap.Exception.cs)
    //--------------------------------------------------------------------------
    // get_Message (excext wiring, 2026-08-07): the callvirt FIRST resolution
    // has no receiver hint -> DEFER (null, generated-name fallback) so the
    // receiver-aware re-resolve can dispatch the catch-variable shape.
    [Fact]
    public void Resolve_Exception_GetMessage_NoReceiverHint_DefersToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System", "Exception", "get_Message", Sig(0x0E)), "get_Message");

        Assert.StartsWith("System_Exception_get_Message_", resolved.CppName);
    }

    // get_Message on a catch-variable receiver (tracked as "System.Type"):
    // reads the DotNetException carrier's message through the generated
    // catch parameter (__il2cpp_ex.message_text()), interned as a managed
    // string; the receiver slot is dropped.
    [Fact]
    public void Resolve_Exception_GetMessage_OnCatchVariable_ReadsCarrierMessage()
    {
        var resolved = ResolveMember(Synthesize("System", "Exception", "get_Message", Sig(0x0E)),
            "get_Message", receiverType: "System.Type");

        Assert.Equal("il2cpp::il2cpp_string_intern_literal(__il2cpp_ex.message_text()", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "drop" }, resolved.ParamTypeNames);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    // get_Message on a NON-catch receiver (a plain new Exception() object --
    // the BCL exception ctor is a no-op carrier in M3) has no reachable
    // message -> LOUD.
    [Fact]
    public void Resolve_Exception_GetMessage_OnNonCatchReceiver_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Exception", "get_Message", Sig(0x0E)),
                "get_Message", receiverType: "System.DivideByZeroException"));

        Assert.Contains("System.Exception::get_Message", ex.Message);
    }

    [Fact]
    public void Resolve_Exception_GetType_OnTypeReceiver_IsIdentity()
    {
        var resolved = ResolveMember(Synthesize("System", "Exception", "GetType", Sig(0x1C)),
            "GetType", receiverType: "System.Type");

        Assert.Equal(ResolvedMethod.IdentityMarker, resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction
    //--------------------------------------------------------------------------

    // Resolves the single MemberReference named `memberName` through the same
    // pipeline the transpiler uses. receiverType/arg0Type exercise the
    // fold-tracking overloads of Resolve.
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName,
        string? receiverType = null, string? arg0Type = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, arg0Type, null);
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), param count, return
    // byte, then one byte per parameter (the raw bytes RawSignature decodes).
    private static byte[] Sig(byte ret, params byte[] ps)
    {
        var bytes = new byte[3 + ps.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret;
        Array.Copy(ps, 0, bytes, 3, ps.Length);
        return bytes;
    }

    private static MemoryStream Synthesize(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        return Serialize(mdb);
    }

    // System.Delegate::get_Method(): the delegate2 MemberRef shape (0x0A000013
    // -- RID 19). Signature: header 0x20 (HAS_THIS = instance), 0 params,
    // return 0x12 CLASS followed by the TypeDefOrRef coded index of the second
    // TypeRef (System.Reflection.MethodInfo, row 2 -> (2 << 2) | 1 = 0x09).
    private static MemoryStream SynthesizeDelegateGetMethod()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var delRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Delegate"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Reflection"), mdb.GetOrAddString("MethodInfo"));
        mdb.AddMemberReference(delRef, mdb.GetOrAddString("get_Method"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x12, 0x09 }));
        return Serialize(mdb);
    }

    // Array.Sort<string>(string[]): a MemberReference on System.Array plus a
    // MethodSpecification carrying the GENERICINST signature (0x0A header, 1
    // generic argument, 0x0E = string) -- the generic dispatch path in Resolve.
    private static MemoryStream SynthesizeGenericSort()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Array"));
        var memberRef = mdb.AddMemberReference(typeRef, mdb.GetOrAddString("Sort"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x0E }));
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(new byte[] { 0x0A, 0x01, 0x0E }));
        return Serialize(mdb);
    }

    private static MemoryStream Serialize(MetadataBuilder mdb)
    {
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }

    // M6: Cases.GenList::Id<T> -- a static generic method (def signature
    // header 0x10 static|generic -- the ECMA generic bit is 0x10 -- 1 gen
    // param, 1 param, T return/param as MVAR 0x1E + compressed index 0x00;
    // the real genlist.exe blob, verified by byte dump) plus a MethodSpec
    // (0x0A GENERICINST, 1 generic argument byte). memberRefForm = true uses
    // the MemberReference + VAR (0x13) MemberRef signature shape (the probe's
    // documented call form); false uses Roslyn's same-assembly shape
    // (MethodSpec directly on the MethodDefinition, genlist evidence).
    private static MemoryStream SynthesizeUserGenericMethod(bool memberRefForm, byte genericArg)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var methodDef = mdb.AddMethodDefinition(
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            System.Reflection.MethodImplAttributes.IL,
            mdb.GetOrAddString("Id"),
            mdb.GetOrAddBlob(new byte[] { 0x10, 0x01, 0x01, 0x1E, 0x00, 0x1E, 0x00 }),
            0, default(ParameterHandle));
        var typeDef = mdb.AddTypeDefinition(
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("GenList"),
            objRef, MetadataTokens.FieldDefinitionHandle(1), methodList: methodDef);
        EntityHandle specTarget = methodDef;
        if (memberRefForm)
        {
            specTarget = mdb.AddMemberReference(typeDef, mdb.GetOrAddString("Id"),
                mdb.GetOrAddBlob(new byte[] { 0x10, 0x01, 0x01, 0x13, 0x00, 0x13, 0x00 }));
        }
        mdb.AddMethodSpecification(specTarget, mdb.GetOrAddBlob(new byte[] { 0x0A, 0x01, genericArg }));
        return Serialize(mdb);
    }
}
