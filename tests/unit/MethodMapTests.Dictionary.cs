using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Dictionary dispatch tests: prove the System.Collections.Generic.
// Dictionary`2 domain (MethodMap.Dictionary.cs -> TryMapDictionary) maps the
// Dictionary<string,int> expansion to the runtime GC-object-wrapper icalls
// declared in src/runtime/include/internal/dictsi_obj.h / dictsi.h
// (namespace il2cpp::runtime):
//
//   .ctor()                    -> icall_dict_si_ctor(self, 0)  (AppendZeroArg)
//   .ctor(int32)               -> icall_dict_si_ctor(self, capacity)
//   Add(TKey,TValue)           -> icall_dict_si_add
//   set_Item(TKey,TValue)      -> icall_dict_si_set   (insert-or-replace)
//   get_Item(TKey)             -> icall_dict_si_get   (KNF on a miss)
//   TryGetValue(TKey,out TVal) -> icall_dict_si_try_get_value
//   ContainsKey(TKey)          -> icall_dict_si_contains_key
//   Remove(TKey)               -> icall_dict_si_remove
//   get_Count()                -> icall_dict_si_get_count
//   Clear()                    -> icall_dict_si_clear
//
// The MemberRef signatures are the GENERIC DEFINITION signatures (TKey/TValue
// arrive as ELEMENT_TYPE_VAR 0x13 with a compressed index, consumed by
// ReadParamByte) -- the real net48 dictcase.exe blobs, probe-verified:
//   .ctor       20 00 01
//   Add         20 02 01 13 00 13 01      -> ps [13,13]
//   get_Count   20 00 08
//   TryGetValue 20 02 02 13 00 10 13 01   -> ps [13,13] (BYREF consumes elem)
//   ContainsKey 20 01 02 13 00            -> ps [13]
//   get_Item    20 01 13 01 13 00         -> ps [0x01]! the RETURN is TValue
//               (0x13), and RawSignature does not consume a VAR-return index,
//               so the index byte leaks into ps[0] -- get_Item guards on
//               COUNT ONLY (the List get_Item precedent).
//   set_Item    20 02 01 13 00 13 01      -> ps [13,13]
//   Remove      20 01 02 13 00            -> ps [13]
//   Clear       20 00 01
//
// Receiver handling: the Dictionary<string,int> instance rides in a managed
// reference slot (Il2CppObject*), so the instance receiver cast is ""
// (pass-through); the int32 value casts "i"; the TryGetValue out int is the
// ldloca ADDRESS ("" passthrough -- the icall writes through the pointer).
//
// Instantiation guard: a folded receiver instance name other than
// "System.Collections.Generic.Dictionary`2_string_int" is a known-unwireable
// instantiation and must fail LOUDLY (NotSupportedException), never silently
// fall to the generated-name path. A T-shaped parameter that is not VAR
// (0x13) is not the Dictionary`2 definition signature at all and must also
// be LOUD.
public class MethodMapTestsDictionary
{
    //--------------------------------------------------------------------------
    // Dictionary<string,int> -- instance members map to icall_dict_si_*
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Dictionary_CtorParameterless_AppendsZeroArgument()
    {
        // real dictcase blob: 20 00 01 (HAS_THIS, 0 params, void)
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x00, 0x01)), ".ctor",
            receiverType: "System.Collections.Generic.Dictionary`2_string_int");

        Assert.Equal("il2cpp::runtime::icall_dict_si_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
        Assert.False(resolved.PrependZeroArg);
        Assert.True(resolved.AppendZeroArg);
    }

    [Fact]
    public void Resolve_Dictionary_CtorCapacity_MapsToCtor()
    {
        // capacity ctor: the int32 param is the ONE concrete byte (0x08)
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x01, 0x01, 0x08)), ".ctor",
            receiverType: "System.Collections.Generic.Dictionary`2_string_int");

        Assert.Equal("il2cpp::runtime::icall_dict_si_ctor", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_Add_MapsToDictSiAdd()
    {
        // instance void Add(TKey,TValue): real blob 20 02 01 13 00 13 01
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
            receiverType: "System.Collections.Generic.Dictionary`2_string_int");

        Assert.Equal("il2cpp::runtime::icall_dict_si_add", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_SetItem_MapsToDictSiSet()
    {
        // indexer SET: same VAR [13,13] shape as Add (insert-or-replace)
        var resolved = ResolveMember(Synthesize("set_Item", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "set_Item",
            receiverType: "System.Collections.Generic.Dictionary`2_string_int");

        Assert.Equal("il2cpp::runtime::icall_dict_si_set", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_GetItem_MapsToDictSiGet()
    {
        // indexer GET: real blob 20 01 13 01 13 00. The RETURN is TValue
        // (0x13 0x01) -- RawSignature does not consume a VAR-return index, so
        // the leaked index byte occupies ps[0]; the mapping guards on COUNT
        // ONLY (List get_Item precedent).
        var resolved = ResolveMember(Synthesize("get_Item", Blob(0x20, 0x01, 0x13, 0x01, 0x13, 0x00)), "get_Item",
            receiverType: "System.Collections.Generic.Dictionary`2_string_int");

        Assert.Equal("il2cpp::runtime::icall_dict_si_get", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_TryGetValue_MapsToDictSiTryGetValue()
    {
        // bool TryGetValue(TKey, out TValue): real blob 20 02 02 13 00 10 13 01
        // -- the BYREF (0x10) consumes its element (VAR 0x13), so ps is
        // [13,13] like Add; the out param is the ldloca ADDRESS ("").
        var resolved = ResolveMember(Synthesize("TryGetValue", Blob(0x20, 0x02, 0x02, 0x13, 0x00, 0x10, 0x13, 0x01)), "TryGetValue",
            receiverType: "System.Collections.Generic.Dictionary`2_string_int");

        Assert.Equal("il2cpp::runtime::icall_dict_si_try_get_value", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    [Theory]
    [InlineData("ContainsKey", "il2cpp::runtime::icall_dict_si_contains_key")]
    [InlineData("Remove", "il2cpp::runtime::icall_dict_si_remove")]
    public void Resolve_Dictionary_OneVarParamMembers_MapToBoolIcalls(string name, string expectedIcall)
    {
        // bool ContainsKey(TKey) / bool Remove(TKey): real blob 20 01 02 13 00
        var resolved = ResolveMember(Synthesize(name, Blob(0x20, 0x01, 0x02, 0x13, 0x00)), name,
            receiverType: "System.Collections.Generic.Dictionary`2_string_int");

        Assert.Equal(expectedIcall, resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_GetCount_MapsToDictSiGetCount()
    {
        // instance int32 get_Count(): real blob 20 00 08
        var resolved = ResolveMember(Synthesize("get_Count", Blob(0x20, 0x00, 0x08)), "get_Count",
            receiverType: "System.Collections.Generic.Dictionary`2_string_int");

        Assert.Equal("il2cpp::runtime::icall_dict_si_get_count", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Dictionary_Clear_MapsToDictSiClear()
    {
        // instance void Clear(): real blob 20 00 01
        var resolved = ResolveMember(Synthesize("Clear", Blob(0x20, 0x00, 0x01)), "Clear",
            receiverType: "System.Collections.Generic.Dictionary`2_string_int");

        Assert.Equal("il2cpp::runtime::icall_dict_si_clear", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Instantiation guard -- a non-int32 (non-string->int32) instantiation is
    // LOUD, not silent
    //--------------------------------------------------------------------------
    // Phase-2 absorption: the string->string expansion rides the shared
    // generic dictgen table through the ref-key twins (kind literals
    // ["0","0"]) -- the dictss shim is deleted.
    [Fact]
    public void Resolve_Dictionary_Add_StringStringReceiver_MapsToDictGenAddO()
    {
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_add_o", resolved.CppName);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_GetCount_StringStringReceiver_MapsToDictGenGetCount()
    {
        var resolved = ResolveMember(Synthesize("get_Count", Blob(0x20, 0x00, 0x08)), "get_Count",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_count", resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Dictionary_GetItem_StringStringReceiver_ReturnsRef()
    {
        // get_Item blob 20 01 13 01 13 00 -> ps [0x01]; the value is a managed
        // reference (ReturnsRef, no ResultKind).
        var resolved = ResolveMember(Synthesize("get_Item", Blob(0x20, 0x01, 0x13, 0x01, 0x13, 0x00)), "get_Item",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_o", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Null(resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Dictionary_TryGetValue_StringStringReceiver_MapsToDictGenTryGetValueO()
    {
        // TryGetValue 20 02 02 13 00 10 13 01 -> ps [13,13]; the out value is
        // the ldloca ADDRESS ("" passthrough -- the icall writes the managed
        // reference through the pointer).
        var resolved = ResolveMember(Synthesize("TryGetValue", Blob(0x20, 0x02, 0x02, 0x13, 0x00, 0x10, 0x13, 0x01)), "TryGetValue",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_try_get_value_o", resolved.CppName);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_Values_StringStringReceiver_MapsToDictGenGetValuesO()
    {
        var resolved = ResolveMember(Synthesize("get_Values", Blob(0x20, 0x00, 0x12, 0x05)), "get_Values",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_values_o", resolved.CppName);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // T4: Dictionary<Uri,string> -- the CLASS-argument-NOT-LAST expansion rides
    // the shared generic dictgen table through the ref-key twins (kind
    // literals ["2","0"]): the Uri key normalizes via icall_uri_to_string
    // inside the twin (verbatim rebuild; for canonical probe inputs identical
    // to AbsoluteUri) then follows the string-kind path. Keyless arms
    // (.ctor/get_Count/Clear/get_Values) map directly to the shared gen icalls.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Dictionary_Add_UriReceiver_MapsToDictGenAddO()
    {
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_add_o", resolved.CppName);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_GetItem_UriReceiver_ReturnsRef()
    {
        var resolved = ResolveMember(Synthesize("get_Item", Blob(0x20, 0x01, 0x13, 0x01, 0x13, 0x00)), "get_Item",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_o", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Null(resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Dictionary_ContainsKey_UriReceiver_MapsToDictGenContainsKeyO()
    {
        var resolved = ResolveMember(Synthesize("ContainsKey", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "ContainsKey",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_contains_key_o", resolved.CppName);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_TryGetValue_UriReceiver_MapsToDictGenTryGetValueO()
    {
        var resolved = ResolveMember(Synthesize("TryGetValue", Blob(0x20, 0x02, 0x02, 0x13, 0x00, 0x10, 0x13, 0x01)), "TryGetValue",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_try_get_value_o", resolved.CppName);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_GetCount_UriReceiver_MapsToDictGenGetCount()
    {
        // Keyless arm: shared with the string->string domain (no key arg).
        var resolved = ResolveMember(Synthesize("get_Count", Blob(0x20, 0x00, 0x08)), "get_Count",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_count", resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Dictionary_Ctor_UriReceiver_MapsToDictGenCtor()
    {
        // Keyless arm: the parameterless ctor allocates through the shared
        // hashtable descriptor (BclAllocExpr exact key).
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x00, 0x01)), ".ctor",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_ctor", resolved.CppName);
    }

    [Fact]
    public void Resolve_Dictionary_Add_ConcreteShape_ThrowsLoud()
    {
        // A CONCRETE string/int-shaped parameter list (0x0E 0x08) is NOT the
        // Dictionary`2 definition signature (TKey/TValue always arrive as VAR
        // 0x13) -- LOUD, not mapped.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x0E, 0x08)), "Add",
                receiverType: "System.Collections.Generic.Dictionary`2_string_int"));

        Assert.Contains("System.Collections.Generic.Dictionary`2::Add", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Negative: unwired members fall back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Dictionary_UnwiredMember_FallsBackToGeneratedName()
    {
        // GetHashCode (inherited from Object) is NOT in the wired surface -- it
        // must fall to the generated-name path, not be mis-mapped.
        var resolved = ResolveMember(Synthesize("GetHashCode", Blob(0x20, 0x00, 0x08)), "GetHashCode");

        Assert.StartsWith("System_Collections_Generic_Dictionary_2_GetHashCode_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the resolver)
    //--------------------------------------------------------------------------
    //--------------------------------------------------------------------------
    // Dictionary<int,string> (Phase-1 dictgen) -- the int->string expansion
    // rides the SINGLE shared generic icall family icall_dict_gen_* with kind
    // codes appended as literals (keyKind=1 int32-boxed, valueKind=0 string-ref;
    // appendLiterals). The key arrives int32 ("i" cast, boxed inside the icall);
    // the string value/reference returns are managed references (ReturnsRef).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Dictionary_Ctor_IntStringReceiver_MapsToDictGenCtor()
    {
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x00, 0x01)), ".ctor",
            receiverType: "System.Collections.Generic.Dictionary`2_int_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_ctor", resolved.CppName);
        Assert.True(resolved.AppendZeroArg);
    }

    [Fact]
    public void Resolve_Dictionary_Add_IntStringReceiver_MapsToDictGenAdd()
    {
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
            receiverType: "System.Collections.Generic.Dictionary`2_int_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_add", resolved.CppName);
        Assert.Equal(new[] { "", "i", "" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "1", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_SetItem_IntStringReceiver_MapsToDictGenSet()
    {
        var resolved = ResolveMember(Synthesize("set_Item", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "set_Item",
            receiverType: "System.Collections.Generic.Dictionary`2_int_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_set", resolved.CppName);
        Assert.Equal(new[] { "", "i", "" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "1", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_GetItem_IntStringReceiver_ReturnsRef()
    {
        var resolved = ResolveMember(Synthesize("get_Item", Blob(0x20, 0x01, 0x13, 0x01, 0x13, 0x00)), "get_Item",
            receiverType: "System.Collections.Generic.Dictionary`2_int_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "1", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_TryGetValue_IntStringReceiver_MapsToDictGenTryGetValue()
    {
        var resolved = ResolveMember(Synthesize("TryGetValue", Blob(0x20, 0x02, 0x02, 0x13, 0x00, 0x10, 0x13, 0x01)), "TryGetValue",
            receiverType: "System.Collections.Generic.Dictionary`2_int_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_try_get_value", resolved.CppName);
        Assert.Equal(new[] { "1", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_ContainsKey_IntStringReceiver_MapsToDictGenContainsKey()
    {
        var resolved = ResolveMember(Synthesize("ContainsKey", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "ContainsKey",
            receiverType: "System.Collections.Generic.Dictionary`2_int_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_contains_key", resolved.CppName);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "1", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_GetCount_IntStringReceiver_MapsToDictGenGetCount()
    {
        var resolved = ResolveMember(Synthesize("get_Count", Blob(0x20, 0x00, 0x08)), "get_Count",
            receiverType: "System.Collections.Generic.Dictionary`2_int_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_count", resolved.CppName);
    }

    [Fact]
    public void Resolve_Dictionary_GetEnumerator_IntStringReceiver_MapsToDictGenGetEnumerator()
    {
        var resolved = ResolveMember(Synthesize("GetEnumerator", Blob(0x20, 0x00, 0x08)), "GetEnumerator",
            receiverType: "System.Collections.Generic.Dictionary`2_int_string");

        Assert.Equal("il2cpp::il2cpp_dict_gen_get_enumerator", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Collections.Generic.Dictionary`2.Enumerator_int_string", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Kvp_GetKey_IntStringPair_MapsToDictGenKvpGetKey()
    {
        var resolved = ResolveMember(Synthesize("get_Key", Blob(0x20, 0x00, 0x08), "KeyValuePair`2"), "get_Key",
            receiverType: "System.Collections.Generic.KeyValuePair`2_int_string");

        Assert.Equal("il2cpp::il2cpp_kvp_gen_int_string_get_key", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_Kvp_GetValue_IntStringPair_MapsToDictGenKvpGetValue()
    {
        var resolved = ResolveMember(Synthesize("get_Value", Blob(0x20, 0x00, 0x12, 0x05), "KeyValuePair`2"), "get_Value",
            receiverType: "System.Collections.Generic.KeyValuePair`2_int_string");

        Assert.Equal("il2cpp::il2cpp_kvp_gen_int_string_get_value", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
    }

    [Fact]
    public void Resolve_Dispose_IntStringEnumerator_MapsToDictGenEnumeratorDispose()
    {
        // foreach-finally: constrained callvirt System.IDisposable::Dispose
        // (real blob 20 00 01) with the tracked int_string enumerator hint.
        var resolved = ResolveMember(Synthesize("Dispose", Blob(0x20, 0x00, 0x01), "IDisposable", "System"), "Dispose",
            receiverType: "System.Collections.Generic.Dictionary`2.Enumerator_int_string");

        Assert.Equal("il2cpp::il2cpp_dict_gen_enumerator_dispose", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Phase-2 absorption: string->string / Uri->string arms ride the ref-key
    // twins (kind literals ["0","0"] / ["2","0"]); the shared kind-free
    // get_Count/Clear stay on the gen icalls.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Dictionary_Add_StringStringReceiver_MapsToDictGenAddO_KindLiterals()
    {
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_add_o", resolved.CppName);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "0", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_GetItem_StringStringReceiver_MapsToDictGenGetO()
    {
        var resolved = ResolveMember(Synthesize("get_Item", Blob(0x20, 0x01, 0x13, 0x01, 0x13, 0x00)), "get_Item",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_o", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(new[] { "0", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_Add_UriReceiver_MapsToDictGenAddO_KindLiterals()
    {
        var resolved = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_add_o", resolved.CppName);
        Assert.Equal(new[] { "2", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_Ctor_StringStringReceiver_MapsToDictGenCtor()
    {
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x00, 0x01)), ".ctor",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_ctor", resolved.CppName);
        Assert.True(resolved.AppendZeroArg);
    }

    [Fact]
    public void Resolve_Dictionary_GetKeys_StringStringReceiver_MapsToDictGenGetKeysO()
    {
        var resolved = ResolveMember(Synthesize("get_Keys", Blob(0x20, 0x00, 0x12, 0x05)), "get_Keys",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_keys_o", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
    }

    [Fact]
    public void Resolve_Dictionary_GetValues_UriReceiver_MapsToDictGenGetValuesO()
    {
        var resolved = ResolveMember(Synthesize("get_Values", Blob(0x20, 0x00, 0x12, 0x05)), "get_Values",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_get_values_o", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // Phase-2 absorption lock: every remaining key-taking ss/uristr arm rides
    // the ref-key twins (never the deleted dictss/uristr shims); keyless arms
    // stay on the shared gen icalls. ss GetEnumerator stays LOUD (parity with
    // the old dictss gap -- Keys/Values snapshots cover the surface).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Dictionary_SetItem_StringStringReceiver_MapsToDictGenSetO()
    {
        var resolved = ResolveMember(Synthesize("set_Item", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "set_Item",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_set_o", resolved.CppName);
        Assert.Equal(new[] { "", "", "" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "0", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_SetItem_UriReceiver_MapsToDictGenSetO()
    {
        var resolved = ResolveMember(Synthesize("set_Item", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "set_Item",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_set_o", resolved.CppName);
        Assert.Equal(new[] { "2", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_Remove_StringStringReceiver_MapsToDictGenRemoveO()
    {
        var resolved = ResolveMember(Synthesize("Remove", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Remove",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_remove_o", resolved.CppName);
        Assert.Equal(new[] { "0", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_Remove_UriReceiver_MapsToDictGenRemoveO()
    {
        var resolved = ResolveMember(Synthesize("Remove", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "Remove",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_remove_o", resolved.CppName);
        Assert.Equal(new[] { "2", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_ContainsKey_StringStringReceiver_MapsToDictGenContainsKeyO()
    {
        var resolved = ResolveMember(Synthesize("ContainsKey", Blob(0x20, 0x01, 0x02, 0x13, 0x00)), "ContainsKey",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_contains_key_o", resolved.CppName);
        Assert.Equal(new[] { "0", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void Resolve_Dictionary_Clear_StringStringReceiver_MapsToDictGenClear()
    {
        var resolved = ResolveMember(Synthesize("Clear", Blob(0x20, 0x00, 0x01)), "Clear",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_clear", resolved.CppName);
    }

    [Fact]
    public void Resolve_Dictionary_Clear_UriReceiver_MapsToDictGenClear()
    {
        var resolved = ResolveMember(Synthesize("Clear", Blob(0x20, 0x00, 0x01)), "Clear",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_clear", resolved.CppName);
    }

    [Fact]
    public void Resolve_Dictionary_CtorCapacity_StringStringReceiver_MapsToDictGenCtor()
    {
        var resolved = ResolveMember(Synthesize(".ctor", Blob(0x20, 0x01, 0x01, 0x08)), ".ctor",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");

        Assert.Equal("il2cpp::runtime::icall_dict_gen_ctor", resolved.CppName);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Dictionary_GetEnumerator_StringStringReceiver_StaysLoud()
    {
        // ss GetEnumerator was never wired (neither dictss nor gen) -- LOUD.
        Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("GetEnumerator", Blob(0x20, 0x00, 0x08)), "GetEnumerator",
                receiverType: "System.Collections.Generic.Dictionary`2_string_string"));
    }

    [Fact]
    public void Resolve_Dictionary_SsUriArms_NeverEmitDeletedShimNames()
    {
        // The absorbed dictss/uristr shim icalls must never be emitted.
        var ssAdd = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
            receiverType: "System.Collections.Generic.Dictionary`2_string_string");
        var uriAdd = ResolveMember(Synthesize("Add", Blob(0x20, 0x02, 0x01, 0x13, 0x00, 0x13, 0x01)), "Add",
            receiverType: "System.Collections.Generic.Dictionary`2_System.Uri_string");

        Assert.StartsWith("il2cpp::runtime::icall_dict_gen_", ssAdd.CppName);
        Assert.StartsWith("il2cpp::runtime::icall_dict_gen_", uriAdd.CppName);
    }

    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null, null);
    }

    // The raw MemberRef signature blob, verbatim (the real net48 dictcase.exe
    // blobs, probe-verified): header 0x20 (HAS_THIS), param count, return
    // byte, then the parameter types with their trailing data.
    private static byte[] Blob(params byte[] bytes) => bytes;

    private static MemoryStream Synthesize(string memberName, byte[] signature, string ownerName = "Dictionary`2",
        string ownerNamespace = "System.Collections.Generic")
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ownerNamespace),
            mdb.GetOrAddString(ownerName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
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
}
