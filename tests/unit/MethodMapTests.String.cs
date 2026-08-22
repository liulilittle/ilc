using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.String op_Equality / op_Inequality dispatch tests (M5 wiring).
//
// Both operators are STATIC 2-arg forms (bool op_*(string, string)) and map
// to the runtime's ordinal static comparator icall_string_equals_static
// (il2cpp_string_ext2.h / stringext2.cpp):
//
//   op_Equality   -> il2cpp::runtime::icall_string_equals_static
//   op_Inequality -> !il2cpp::runtime::icall_string_equals_static
//                    (no runtime inequality icall exists; the C++ logical-NOT
//                    prefix binds to the whole call expression, so the
//                    emitted code is !(eq(a, b))).
//
// The icall is null-aware and content-based -- exactly the net48 string ==/!=
// contract (null == null -> true, exactly one null -> false, both non-null ->
// ordinal unit comparison; != is the negation of each row).
public class MethodMapTestsString
{
    [Fact]
    public void Resolve_OpEquality_MapsToEqualsIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "op_Equality", Sig(0x02, 0x0E, 0x0E)), "op_Equality");

        Assert.Equal("il2cpp::runtime::icall_string_equals_static", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { false, false }, resolved.ParamIsRef);
    }

    [Fact]
    public void Resolve_OpInequality_MapsToNegatedEqualsIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "op_Inequality", Sig(0x02, 0x0E, 0x0E)), "op_Inequality");

        Assert.Equal("!il2cpp::runtime::icall_string_equals_static", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { false, false }, resolved.ParamIsRef);
    }

    [Fact]
    public void Resolve_OpEquality_NonTwoArg_ThrowsLoud()
    {
        // op_Equality is always 2-arg in C#; an unexpected arity must not
        // fall through to a silent wrong mapping.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "String", "op_Equality", Sig(0x02, 0x0E)), "op_Equality"));

        Assert.Contains("String.op_Equality with 1 arg(s) not wired in M5", ex.Message);
    }

    [Fact]
    public void Resolve_OpInequality_NonTwoArg_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "String", "op_Inequality", Sig(0x02, 0x0E)), "op_Inequality"));

        Assert.Contains("String.op_Inequality with 1 arg(s) not wired in M5", ex.Message);
    }

    //--------------------------------------------------------------------------
    // TrimStart/TrimEnd -- net48 has ONLY the params-char[] overload; Roslyn
    // (optimize) lowers a no-arg call to TrimStart(Array.Empty<char>()).
    // The char[] trim-set argument is DROPPED at emission (the runtime
    // icall is whitespace-only) but counted in ParamCount so the fold's
    // shadow stack stays in lockstep with the IL.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_TrimStart_CharArrayArg_DropsArgAtEmission()
    {
        // TrimStart(char[]) -- SZARRAY 0x1D + 0x03 char element byte. The Sig
        // helper cannot express an SZARRAY parameter (it counts each byte as
        // one param -- 0x1D,0x03 would yield count=2 and the array's element
        // byte would be read as a second param, running out of bounds), so
        // the raw blob is written out: DEFAULT header, count=1, string return,
        // one SZARRAY(char) param.
        var resolved = ResolveMember(Synthesize("System", "String", "TrimStart",
            new byte[] { 0x00, 0x01, 0x0E, 0x1D, 0x03 }), "TrimStart");

        Assert.Equal("il2cpp::runtime::icall_string_trim_start", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_TrimEnd_CharArrayArg_DropsArgAtEmission()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "TrimEnd",
            new byte[] { 0x00, 0x01, 0x0E, 0x1D, 0x03 }), "TrimEnd");

        Assert.Equal("il2cpp::runtime::icall_string_trim_end", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_TrimStart_NoArgs_KeepsZeroParamMapping()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "TrimStart", Sig(0x0E)), "TrimStart");

        Assert.Equal("il2cpp::runtime::icall_string_trim_start", resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Null(resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Equals(string, StringComparison) -- the INSTANCE overload. The IL
    // pushes [this, string, StringComparison] (3 values; Roslyn
    // devirtualizes the call to plain `call` on literal receivers), so the
    // mapping MUST be IsInstance (the fold pops this on plain `call`) with
    // the comparisonType crossing as int32_t to the MODE-AWARE runtime
    // primitive (il2cpp_string_ext2.h:96 icall_string_equals_culture --
    // strcompare fix, 2026-08-07: the old ordinal-only mapping DROPPED the
    // mode and broke every OrdinalIgnoreCase/CurrentCulture assertion).
    // ParamCount = the explicit arity (2); the cast list carries the
    // receiver slot + the string arg + the "i" comparison.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_EqualsStringComparison_IsInstanceCarriesComparisonArg()
    {
        // Equals(string, StringComparison) -- the enum is a valuetype in
        // the signature: byte 0x11 + TypeHandle (consumed by RawSignature,
        // so ps[1] == 0x11).
        var resolved = ResolveMember(Synthesize("System", "String", "Equals", EnumSig(0x02, 0x0E)), "Equals");

        Assert.Equal("il2cpp::runtime::icall_string_equals_culture", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_EqualsStaticTwoString_StaysStaticNoCasts()
    {
        // static string.Equals(string, string) -- 0x0E, 0x0E: the plain
        // ordinal comparator, both args kept, no receiver slot.
        var resolved = ResolveMember(Synthesize("System", "String", "Equals", Sig(0x02, 0x0E, 0x0E)), "Equals");

        Assert.Equal("il2cpp::runtime::icall_string_equals_static", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Null(resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_EqualsTwoArg_UnknownParamType_ThrowsLoud()
    {
        // A 2-param Equals with an unexpected parameter byte must not fall
        // through to a silent wrong mapping.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "String", "Equals", Sig(0x02, 0x03, 0x03)), "Equals"));

        Assert.Contains("String.Equals 2-arg param type 0x03 not wired in M5", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Join<T>(string, IEnumerable<T>) -- the GENERIC method shape
    // (randomcase: string.Join(",", byte[]) -> Join<byte>). The MemberRef
    // signature carries the GENERIC calling convention (0x10) +
    // GenericParameterCount (0x01) BEFORE the real param count: RawSignature
    // must consume the GenericParameterCount or every byte after it
    // misparses (0x01 read as the count, 0x02 read as the return byte...).
    // The IEnumerable<byte> parameter encodes as GENERICINST (0x15) +
    // CLASS TypeHandle + genArg count + MVAR (0x1E + index) -- ps[1] must
    // surface as 0x15 for the Join mapping (the string[] 0x1D sibling).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_JoinGenericByte_MapsToJoinIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "Join",
            new byte[] { 0x10, 0x01, 0x02, 0x0E, 0x0E, 0x15, 0x12, 0x05, 0x01, 0x1E, 0x00 }), "Join");

        Assert.Equal("il2cpp::runtime::icall_string_join", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_JoinStringArray_StillMapsAfterGenericFix()
    {
        // Regression: the non-generic string[] shape must be unaffected by
        // the GenericParameterCount consumption. Blob: DEFAULT header,
        // count=2, ret string, string separator, SZARRAY(string) values --
        // the SZARRAY element byte (0x0E) must be present (the Sig helper
        // cannot express an SZARRAY parameter, so the raw blob is written
        // out).
        var resolved = ResolveMember(Synthesize("System", "String", "Join",
            new byte[] { 0x00, 0x02, 0x0E, 0x0E, 0x1D, 0x0E }), "Join");

        Assert.Equal("il2cpp::runtime::icall_string_join", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // String.Split(char[] separator) -- the separator-array form (the RT-SPLIT
    // wiring, 2026-08-09). The runtime icall_string_split declaration takes
    // (Il2CppString* self, Il2CppArray* separators): the receiver crosses
    // with the "s" cast, the char[] SZARRAY with the "a" cast, and the
    // result is a string[] reference (ReturnTypeName "System.String[]").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_SplitCharArray_MapsToSplitIcall()
    {
        // Split(char[]): ret string[] (SZARRAY 0x1D + string element byte
        // 0x0E), one char[] SZARRAY param (0x1D + char element byte 0x03) --
        // hand-built blob (the Sig helper has no SZARRAY return).
        var resolved = ResolveMember(Synthesize("System", "String", "Split",
            new byte[] { 0x00, 0x01, 0x1D, 0x0E, 0x1D, 0x03 }), "Split");

        Assert.Equal("il2cpp::runtime::icall_string_split", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "s", "a" }, resolved.ParamTypeNames);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_SplitStringArrayOptions_MapsToSplitOptionsIcall()
    {
        // Split(string[], StringSplitOptions) -- the 2-arg options overload
        // (the RT-SPL2 / strcomp2 wiring). The runtime
        // icall_string_split_options takes (Il2CppString* self, Il2CppArray*
        // separators, int32_t options): the receiver crosses with the "s"
        // cast, the string[] SZARRAY with the "a" cast, the options enum with
        // the "" raw int32 passthrough. The runtime disambiguates char[] vs
        // string[] separators by the array's isRefElem flag.
        var resolved = ResolveMember(Synthesize("System", "String", "Split",
            new byte[] { 0x00, 0x02, 0x1D, 0x0E, 0x1D, 0x0E, 0x11, 0x05 }), "Split");

        Assert.Equal("il2cpp::runtime::icall_string_split_options", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "a", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_SplitCharArrayOptions_MapsToSplitOptionsIcall()
    {
        // Split(char[], StringSplitOptions): the char[] SZARRAY (0x1D + char
        // element byte 0x03) + the StringSplitOptions enum -- the same
        // icall_string_split_options mapping as the string[] overload.
        var resolved = ResolveMember(Synthesize("System", "String", "Split",
            new byte[] { 0x00, 0x02, 0x1D, 0x0E, 0x1D, 0x03, 0x11, 0x05 }), "Split");

        Assert.Equal("il2cpp::runtime::icall_string_split_options", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "a", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // String.Replace -- the ps dispatch (strrep-b23 fix). The runtime
    // icall_string_replace is CHAR/CHAR only (il2cpp_string_ext.h:88:
    // (Il2CppObject*, char16_t, char16_t)); the char form maps to it with
    // the "c" char16_t casts. The STRING/STRING overload maps to the runtime
    // icall_string_replace_s_s (strcomp2, rtrepl-b23 -- il2cpp_string_ext.h:90)
    // with the "s" Il2CppString* casts on all three slots (receiver self,
    // oldValue, newValue).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Replace_CharChar_MapsToReplaceIcall()
    {
        // Replace(char, char): ret string (0x0E), two char params (0x03).
        var resolved = ResolveMember(Synthesize("System", "String", "Replace", Sig(0x0E, 0x03, 0x03)), "Replace");

        Assert.Equal("il2cpp::runtime::icall_string_replace", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "c", "c" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Replace_StringString_MapsToReplaceStringIcall()
    {
        // Replace(string, string): ret string (0x0E), two string params
        // (0x0E). Wired to icall_string_replace_s_s with the "s" Il2CppString*
        // casts; returnTypeName tracks the result string for downstream calls.
        var resolved = ResolveMember(Synthesize("System", "String", "Replace", Sig(0x0E, 0x0E, 0x0E)), "Replace");

        Assert.Equal("il2cpp::runtime::icall_string_replace_s_s", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "s", "s" }, resolved.ParamTypeNames);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_Replace_MixedTypes_ThrowsLoud()
    {
        // A mixed (string, char) shape is not a real String.Replace overload;
        // an unexpected param combo must not fall to a silent wrong mapping.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "String", "Replace", Sig(0x0E, 0x0E, 0x03)), "Replace"));

        Assert.Contains("String.Replace", ex.Message);
    }

    //--------------------------------------------------------------------------
    // String.Format -- the ps dispatch (M5 strfmt wiring). The 1-arg format
    // only form, the 2/3/4-arg (string, object[, ...]) forms, and the params
    // object[] form each route to a distinct runtime icall
    // (icall_string_format_0/1/2/3 + icall_string_format); every shape is
    // STATIC, returns a managed string, keeps all args.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Format_OneArg_MapsToFormatZero()
    {
        // Format(string) -- the format-only overload.
        var resolved = ResolveMember(Synthesize("System", "String", "Format", Sig(0x0E, 0x0E)), "Format");

        Assert.Equal("il2cpp::runtime::icall_string_format_0", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Format_TwoArgObject_MapsToFormatOne()
    {
        // Format(string, object) -- the 2-param object form (0x1C).
        var resolved = ResolveMember(Synthesize("System", "String", "Format", Sig(0x0E, 0x0E, 0x1C)), "Format");

        Assert.Equal("il2cpp::runtime::icall_string_format_1", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Format_ThreeArgObject_MapsToFormatTwo()
    {
        // Format(string, object, object) -- the 3-param object form.
        var resolved = ResolveMember(Synthesize("System", "String", "Format", Sig(0x0E, 0x0E, 0x1C, 0x1C)), "Format");

        Assert.Equal("il2cpp::runtime::icall_string_format_2", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Format_FourArgObject_MapsToFormatThree()
    {
        // Format(string, object, object, object) -- the 4-param object form.
        var resolved = ResolveMember(Synthesize("System", "String", "Format", Sig(0x0E, 0x0E, 0x1C, 0x1C, 0x1C)), "Format");

        Assert.Equal("il2cpp::runtime::icall_string_format_3", resolved.CppName);
        Assert.Equal(4, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Format_ParamsObjectArray_MapsToFormatArray()
    {
        // Format(string, params object[]) -- the SZARRAY form (0x1D + 0x1C
        // element byte, raw blob like the Join string[] shape).
        var resolved = ResolveMember(Synthesize("System", "String", "Format",
            new byte[] { 0x00, 0x02, 0x0E, 0x0E, 0x1D, 0x1C }), "Format");

        Assert.Equal("il2cpp::runtime::icall_string_format", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Format_UnknownArity_ThrowsLoud()
    {
        // A 5-arg Format must not fall through to a silent wrong mapping.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "String", "Format", Sig(0x0E, 0x0E, 0x1C, 0x1C, 0x1C, 0x1C)), "Format"));

        Assert.Contains("String.Format with 5 arg(s) not wired in M5", ex.Message);
    }

    [Fact]
    public void Resolve_Format_TwoArgNonObject_MapsToParamsArray()
    {
        // Format(string, string[]) -- the SZARRAY(string) shape resolves to
        // the same params icall as object[] (the resolver cannot see the
        // element type; the runtime reads the payload generically).
        var resolved = ResolveMember(Synthesize("System", "String", "Format",
            new byte[] { 0x00, 0x02, 0x0E, 0x0E, 0x1D, 0x0E }), "Format");

        Assert.Equal("il2cpp::runtime::icall_string_format", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // PadLeft / PadRight -- net48 pad family (work order #32). Both are
    // INSTANCE methods returning a string; the 1-arg (int32 totalWidth)
    // form binds the 2-param icall overload (space default) and the 2-arg
    // (int32, char) form the 3-param overload -- the runtime returns the
    // SAME instance when totalWidth <= Length (net48, probed) and throws
    // ArgumentOutOfRangeException for negative widths.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_PadLeftInt_MapsToPadLeftIcall()
    {
        // PadLeft(int32) -- string return (0x0E), int32 param (0x08).
        var resolved = ResolveMember(Synthesize("System", "String", "PadLeft", Sig(0x0E, 0x08)), "PadLeft");

        Assert.Equal("il2cpp::runtime::icall_string_pad_left", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_PadLeftIntChar_MapsToPadLeftIcall()
    {
        // PadLeft(int32, char) -- char param byte 0x03.
        var resolved = ResolveMember(Synthesize("System", "String", "PadLeft", Sig(0x0E, 0x08, 0x03)), "PadLeft");

        Assert.Equal("il2cpp::runtime::icall_string_pad_left", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "i", "c" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_PadRightInt_MapsToPadRightIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "PadRight", Sig(0x0E, 0x08)), "PadRight");

        Assert.Equal("il2cpp::runtime::icall_string_pad_right", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_PadRightIntChar_MapsToPadRightIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "PadRight", Sig(0x0E, 0x08, 0x03)), "PadRight");

        Assert.Equal("il2cpp::runtime::icall_string_pad_right", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "i", "c" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_PadLeft_UnknownArity_ThrowsLoud()
    {
        // An unexpected arity must not fall through to a silent wrong mapping.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "String", "PadLeft", Sig(0x0E, 0x08, 0x08, 0x08)), "PadLeft"));

        Assert.Contains("String.PadLeft with 3 arg(s) not wired in M5", ex.Message);
    }

    //--------------------------------------------------------------------------
    // net48 audit §5 items 1-3 (String-domain high-priority gaps):
    //   .ctor(char, int32)          -> icall_string_ctor_repeat
    //   IndexOfAny(char[], int, int) -> icall_string_index_of_any_range
    //   LastIndexOfAny(char[], int, int) -> icall_string_last_index_of_any_range
    // All three net48 boundary semantics are csc-v4.0.30319-oracle-verified
    // (count<0 AOORE, anyOf null ANE, (uint)startIndex>Length AOORE,
    // (uint)count>Length-startIndex AOORE; LastIndexOfAny Length==0 -> -1
    // short-circuits BEFORE the bounds checks).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_CtorCharInt_MapsToCtorRepeatIcall()
    {
        // new string('*', n): void return (0x01), char param (0x03), int32
        // count (0x08). Instance ctor; the receiver slot stays (the runtime
        // icall takes this_ and ignores it), char -> "c", count -> "i".
        var resolved = ResolveMember(Synthesize("System", "String", ".ctor", Sig(0x01, 0x03, 0x08)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_string_ctor_repeat", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "c", "i" }, resolved.ParamTypeNames);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_CtorWrongShape_ThrowsLoud()
    {
        // A String ctor that is not (char, int32) must not silently fall
        // through to a wrong mapping.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "String", ".ctor", Sig(0x01, 0x0E)), ".ctor"));

        Assert.Contains("String ctor with 1 arg(s) not wired", ex.Message);
    }

    //--------------------------------------------------------------------------
    // stralloc-b23: the char-array ctors (new string(char[]) and
    // new string(char[], int, int)) -- the runtime icalls allocate a fresh
    // copy of the window; net48-probe bounds semantics are runtime-side.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_CtorCharArray_MapsToCharArrayIcall()
    {
        // new string(char[] value): void return, one char[] SZARRAY param
        // (0x1D + element byte 0x03).
        var resolved = ResolveMember(Synthesize("System", "String", ".ctor",
            new byte[] { 0x00, 0x01, 0x01, 0x1D, 0x03 }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_string_ctor_char_array", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "" }, resolved.ParamTypeNames);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_CtorCharArrayRange_MapsToCharArrayRangeIcall()
    {
        // new string(char[] value, int32 startIndex, int32 length): the
        // char[] SZARRAY + two int32s.
        var resolved = ResolveMember(Synthesize("System", "String", ".ctor",
            new byte[] { 0x00, 0x03, 0x01, 0x1D, 0x03, 0x08, 0x08 }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_string_ctor_char_array_range", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i", "i" }, resolved.ParamTypeNames);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_IndexOfAny_CharArrayRange_MapsToAnyRangeIcall()
    {
        // IndexOfAny(char[], int32, int32): int32 return (0x08), the char[]
        // SZARRAY param (0x1D + 0x03 element byte -- raw blob like the
        // TrimStart shape), then two int32s (0x08, 0x08). The array passes as
        // Il2CppObject* (join passthrough); the ints get "i" casts.
        var resolved = ResolveMember(Synthesize("System", "String", "IndexOfAny",
            new byte[] { 0x00, 0x03, 0x08, 0x1D, 0x03, 0x08, 0x08 }), "IndexOfAny");

        Assert.Equal("il2cpp::runtime::icall_string_index_of_any_range", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_LastIndexOfAny_CharArrayRange_MapsToAnyRangeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "String", "LastIndexOfAny",
            new byte[] { 0x00, 0x03, 0x08, 0x1D, 0x03, 0x08, 0x08 }), "LastIndexOfAny");

        Assert.Equal("il2cpp::runtime::icall_string_last_index_of_any_range", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "i", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_IndexOfAny_OneArg_Wires_RuntimeWrapper()
    {
        // net9/10 campaign contract (updated from the old LOUD pin): the
        // 1-arg IndexOfAny(char[]) wires to the runtime wrapper
        // icall_string_index_of_any (startIndex=0/count=Length defaults,
        // stringext.cpp) -- proven by icallclassic on every framework
        // version (net48..net10).
        var resolved = ResolveMember(Synthesize("System", "String", "IndexOfAny",
            new byte[] { 0x00, 0x01, 0x08, 0x1D, 0x03 }), "IndexOfAny");

        Assert.Equal("il2cpp::runtime::icall_string_index_of_any", resolved.CppName);
    }

    [Fact]
    public void Resolve_IndexOfAny_WrongShape_ThrowsLoud()
    {
        // A string-parameter IndexOfAny does not exist in any BCL surface
        // -- LOUD, not silent. Sig layout: [0x00 header, count=1, ret=I4,
        // params={0x0E string}].
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "String", "IndexOfAny",
                new byte[] { 0x00, 0x01, 0x08, 0x0E }), "IndexOfAny"));

        Assert.Contains("String.IndexOfAny with 1 arg(s) not wired", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodRefResolverDispatchTests / MethodMapTests.StringExt3)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Signature blob: header 0x00 (DEFAULT), param count, return byte, then
    // one byte per parameter (the raw bytes RawSignature decodes).
    private static byte[] Sig(byte ret, params byte[] ps)
    {
        var bytes = new byte[3 + ps.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret;
        Array.Copy(ps, 0, bytes, 3, ps.Length);
        return bytes;
    }

    // Signature ending in an ENUM (StringComparison) parameter: the valuetype
    // byte 0x11 followed by its TypeHandle -- a compressed TypeDefOrRef coded
    // index. The synthesized PE holds exactly ONE TypeRef (row 1, the type
    // being declared -- "System.String"), so the coded index is (1 << 2) | 1
    // = 0x05 (TypeRef tag) -- the StringExt3 precedent.
    private static byte[] EnumSig(byte ret, params byte[] leading)
    {
        var bytes = new byte[3 + leading.Length + 2];
        bytes[0] = 0x00;
        bytes[1] = (byte)(leading.Length + 1);
        bytes[2] = ret;
        Array.Copy(leading, 0, bytes, 3, leading.Length);
        bytes[3 + leading.Length] = 0x11;
        bytes[3 + leading.Length + 1] = 0x05;
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
