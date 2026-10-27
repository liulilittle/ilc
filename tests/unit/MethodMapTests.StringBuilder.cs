using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.StringBuilder dispatch tests: prove the StringBuilder domain
// routes the AppendLine / AppendFormat / Replace families to their runtime
// icalls (il2cpp_stringbuilder.h), that the chain contract holds (every
// member returns the SAME builder: ReturnsValue + ReturnsRef with
// ReturnTypeName "System.Text.StringBuilder"), and that close-but-wrong
// signatures reject LOUDLY (NotSupportedException -- see BclResolve's wrap).
//
// Signature blobs are the raw bytes RawSignature consumes; ret byte 0x1C
// (object) avoids the 0x11/0x12 ret-handle consumption (the dispatch ignores
// the ret byte). CLASS params (0x12) carry a TypeDefOrRef coded-index byte
// after them.
public class MethodMapStringBuilderTests
{
    //--------------------------------------------------------------------------
    // AppendLine -- () and (string) both chain
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_AppendLine_NoArgs_MapsToAppendLineIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendLine", Sig(0x1C)), "AppendLine");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_line", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendLine_String_MapsToAppendLineStringIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendLine", Sig(0x1C, 0x0E)), "AppendLine");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_line_s", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { true }, resolved.ParamIsRef);
        AssertChains(resolved);
    }

    //--------------------------------------------------------------------------
    // AppendFormat -- the (string, object[, object[, object]]) family
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_AppendFormat_OneArg_MapsToAppendFormat1Icall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
            Sig(0x1C, 0x0E, 0x1C)), "AppendFormat");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_1", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { true, true }, resolved.ParamIsRef);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendFormat_TwoArgs_MapsToAppendFormat2Icall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
            Sig(0x1C, 0x0E, 0x1C, 0x1C)), "AppendFormat");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_2", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendFormat_ThreeArgs_MapsToAppendFormat3Icall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
            Sig(0x1C, 0x0E, 0x1C, 0x1C, 0x1C)), "AppendFormat");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_3", resolved.CppName);
        Assert.Equal(4, resolved.ParamCount);
        AssertChains(resolved);
    }

    //--------------------------------------------------------------------------
    // AppendFormat -- close-but-wrong signatures reject LOUDLY
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_AppendFormat_NonObjectSecondArg_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
                Sig(0x1C, 0x0E, 0x08)), "AppendFormat")); // (string, int) does not exist

        Assert.Contains("System.Text.StringBuilder::AppendFormat", ex.Message);
    }

    [Fact]
    public void Resolve_AppendFormat_FormatProviderOneArg_MapsToProvider1Icall()
    {
        // (IFormatProvider, string, object) -- ps[0] is a CLASS, not string.
        // Hand-built blob: the CLASS param (0x12) consumes its TypeRef coded
        // index (0x05 = the synthetic PE's single TypeRef row), so the param
        // count is 3, not 4 (the Sig helper cannot express this).
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
            new byte[] { 0x00, 0x03, 0x1C, 0x12, 0x05, 0x0E, 0x1C }), "AppendFormat");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_1_provider", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal("System.Text.StringBuilder", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_AppendFormat_FormatProviderThreeArgs_MapsToProvider3Icall()
    {
        // (IFormatProvider, string, object, object, object): the three-arg
        // provider form (the Sig helper cannot express the CLASS param, so the
        // blob is hand-built: 0x12 + TypeRef index 0x05, string, 3 objects).
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
            new byte[] { 0x00, 0x05, 0x1C, 0x12, 0x05, 0x0E, 0x1C, 0x1C, 0x1C }), "AppendFormat");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_3_provider", resolved.CppName);
        Assert.Equal(5, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Replace -- (string, string) and (char, char)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Replace_StringString_MapsToReplaceStringIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "Replace",
            Sig(0x1C, 0x0E, 0x0E)), "Replace");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_replace_s_s", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { true, true }, resolved.ParamIsRef);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_Replace_CharChar_MapsToReplaceCharIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "Replace",
            Sig(0x1C, 0x03, 0x03)), "Replace");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_replace_c_c", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "c", "c" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { true, false, false }, resolved.ParamIsRef);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_Replace_MixedTypes_ThrowsLoud()
    {
        // (string, char) -- no such net48 overload
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "StringBuilder", "Replace",
                Sig(0x1C, 0x0E, 0x03)), "Replace"));

        Assert.Contains("System.Text.StringBuilder::Replace", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Dispatch miss within the domain -- falls back to the generated-name path
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_UnwiredMember_FallsBackToGeneratedName()
    {
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "Frobnicate", Sig(0x1C, 0x0E)), "Frobnicate");

        Assert.StartsWith("System_Text_StringBuilder_Frobnicate_", resolved.CppName);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // P25f (dhprobe): System.Runtime.CompilerServices.DefaultInterpolatedStringHandler
    // -- the net6+ interpolation lowering. ctor writes a fresh StringBuilder
    // into the address-taken handler slot; AppendLiteral / ToStringAndClear /
    // AppendFormatted(string) forward to the builder icalls (rv FALSE for
    // appends -- the IL members return void). The AppendFormatted<int32>
    // GENERIC path is covered by the dhprobe e2e (a MethodSpec -- not
    // synthesizable through these helpers).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_HandlerCtor_TwoInts_MapsToInterpCtor()
    {
        var resolved = ResolveMember(Synthesize("System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", ".ctor",
            Sig(0x1C, 0x08, 0x08)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_interp_handler_ctor", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_HandlerAppendLiteral_String_MapsToBuilderAppend()
    {
        var resolved = ResolveMember(Synthesize("System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", "AppendLiteral",
            Sig(0x1C, 0x0E)), "AppendLiteral");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_s", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.False(resolved.ReturnsValue); // the IL member is void
    }

    [Fact]
    public void Resolve_HandlerAppendFormatted_String_MapsToBuilderAppend()
    {
        // The non-generic AppendFormatted(string) overload (the generic
        // AppendFormatted<int32> MethodSpec route is e2e-covered).
        var resolved = ResolveMember(Synthesize("System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", "AppendFormatted",
            Sig(0x1C, 0x0E)), "AppendFormatted");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_s", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_HandlerToStringAndClear_MapsToBuilderToString()
    {
        var resolved = ResolveMember(Synthesize("System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", "ToStringAndClear",
            Sig(0x1C)), "ToStringAndClear");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_to_string", resolved.CppName);
        Assert.Equal(0, resolved.ParamCount);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal("System.String", resolved.ReturnTypeName);
    }

    // P25h: the generic VALUE-type AppendFormatted forms (decimal) -- the
    // MethodSpec carries the VALUETYPE arg; the generic-arg NAME table names
    // System.Decimal, so the resolver boxes the value slot and routes to the
    // boxed-object append icall (the composite engine renders the box).
    [Fact]
    public void Resolve_HandlerAppendFormatted_DecimalOneArg_MapsToBoxedAppendO()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(
            "System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", "AppendFormatted",
            new byte[] { 0x00, 0x01, 0x1C, 0x1E, 0x00 },
            new byte[] { 0x0A, 0x01, 0x11, 0x09 },
            "System", "Decimal"));

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_o", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_HandlerAppendFormatted_DecimalWidth_MapsToBoxedAppendOW()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(
            "System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", "AppendFormatted",
            new byte[] { 0x00, 0x02, 0x1C, 0x1E, 0x00, 0x08 },
            new byte[] { 0x0A, 0x01, 0x11, 0x09 },
            "System", "Decimal"));

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_o_w", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_HandlerAppendFormatted_DecimalWidthFormat_MapsToBoxedAppendOWF()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(
            "System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", "AppendFormatted",
            new byte[] { 0x00, 0x03, 0x1C, 0x1E, 0x00, 0x08, 0x0E },
            new byte[] { 0x0A, 0x01, 0x11, 0x09 },
            "System", "Decimal"));

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_o_wf", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // net6+ handler / span-params / CompositeFormat wiring (impl-sb). The
    // reachable netcore-only Append/AppendLine/AppendFormat shapes:
    //   A  AppendLine(ref AppendInterpolatedStringHandler) + provider variant
    //   B  AppendFormat(string[, provider], params ReadOnlySpan<object?>)
    //   C  AppendFormat<TArg...>(provider, CompositeFormat, TArg...)
    //   D  Append(ReadOnlySpan<char>) / Append(ref handler)
    // The nested handler owner routes through the DefaultInterpolatedStringHandler
    // mapping (the ctor binds the REAL builder into the handler slot).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_AppendLine_Handler_MapsToAppendLineIcall()
    {
        // AppendLine(ref AppendInterpolatedStringHandler): the BYREF(0x10)
        // VALUETYPE(0x11) handler param. The handler appends already landed in
        // the builder (the ctor bound it), so the outer call only appends the
        // newline; the handler address is popped in lockstep and dropped.
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendLine",
            new byte[] { 0x00, 0x01, 0x1C, 0x10, 0x11, 0x05 }), "AppendLine");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_line", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "drop" }, resolved.ParamTypeNames);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendLine_ProviderHandler_MapsToAppendLineIcall()
    {
        // AppendLine(IFormatProvider, ref handler): the provider slot is
        // popped and dropped (the newline needs no culture).
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendLine",
            new byte[] { 0x00, 0x02, 0x1C, 0x12, 0x05, 0x10, 0x11, 0x05 }), "AppendLine");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_line", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "drop", "drop" }, resolved.ParamTypeNames);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_Append_Handler_MapsToNoOpAppendS()
    {
        // Append(ref AppendInterpolatedStringHandler): the handler content is
        // already in the builder -- the outer call is a no-op that returns the
        // builder (append_s with a null literal appends nothing and chains).
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "Append",
            new byte[] { 0x00, 0x01, 0x1C, 0x10, 0x11, 0x05 }), "Append", receiverIsAddress: true);

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_s", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "", "drop" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "nullptr" }, resolved.AppendLiterals);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_Append_SpanChar_MapsToAppendCharArrayKernel()
    {
        // Append(ReadOnlySpan<char>): the span carrier is the backing object
        // (string / char[] identity materialized by the fold) -- the runtime
        // span kernel appends its units.
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "Append",
            new byte[] { 0x00, 0x01, 0x1C, 0x15, 0x11, 0x05, 0x01, 0x03 }), "Append");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_span", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendFormat_SpanParams_MapsToArgsArrayKernel()
    {
        // AppendFormat(string, params ReadOnlySpan<object?>) -- the span tail
        // (0x15) rides the SAME argsArray composite kernel the params-object[]
        // form uses (the fold materializes the object[] from the inline array).
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
            new byte[] { 0x00, 0x02, 0x1C, 0x0E, 0x15, 0x11, 0x05, 0x01, 0x1C }), "AppendFormat");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendFormat_ProviderSpanParams_MapsToProviderArgsArrayKernel()
    {
        // AppendFormat(IFormatProvider, string, params ReadOnlySpan<object?>).
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
            new byte[] { 0x00, 0x03, 0x1C, 0x12, 0x05, 0x0E, 0x15, 0x11, 0x05, 0x01, 0x1C }), "AppendFormat");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_provider", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendFormat_CompositeFormat_ParamsArray_MapsToProviderArgsArrayKernel()
    {
        // AppendFormat(IFormatProvider, CompositeFormat, params object?[]) --
        // the second CLASS slot is CompositeFormat (0x12 0x12), the tail the
        // SZARRAY params form.
        var resolved = ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
            new byte[] { 0x00, 0x03, 0x1C, 0x12, 0x05, 0x12, 0x05, 0x1D, 0x1C }), "AppendFormat");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_provider", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendFormat_CompositeFormat_OneGenericArg_MapsToProvider1()
    {
        // AppendFormat<TArg0>(IFormatProvider, CompositeFormat, TArg0) -- the
        // MethodSpec closes TArg0; the closed value rides the per-slot box cast.
        var resolved = ResolveMethodSpec(SynthesizeGeneric(
            "System.Text", "StringBuilder", "AppendFormat",
            new byte[] { 0x30, 0x01, 0x03, 0x1C, 0x12, 0x05, 0x12, 0x05, 0x1E, 0x00 },
            new byte[] { 0x0A, 0x01, 0x08 },
            "System", "Int32"));

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_1_provider", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "", "", "box:i" }, resolved.ParamTypeNames);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendFormat_CompositeFormat_TwoGenericArgs_MapsToProvider2()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(
            "System.Text", "StringBuilder", "AppendFormat",
            new byte[] { 0x30, 0x02, 0x04, 0x1C, 0x12, 0x05, 0x12, 0x05, 0x1E, 0x00, 0x1E, 0x01 },
            new byte[] { 0x0A, 0x02, 0x08, 0x08 },
            "System", "Int32"));

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_2_provider", resolved.CppName);
        Assert.Equal(4, resolved.ParamCount);
        AssertChains(resolved);
    }

    [Fact]
    public void Resolve_AppendFormat_CompositeFormat_DecimalArg_BoxesDecimal()
    {
        // A closed VALUE-type TArg boxes through the BCL decimal box (the
        // composite engine renders the box).
        var resolved = ResolveMethodSpec(SynthesizeGeneric(
            "System.Text", "StringBuilder", "AppendFormat",
            new byte[] { 0x30, 0x01, 0x03, 0x1C, 0x12, 0x05, 0x12, 0x05, 0x1E, 0x00 },
            new byte[] { 0x0A, 0x01, 0x11, 0x09 },
            "System", "Decimal"));

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_format_1_provider", resolved.CppName);
        Assert.Equal(new[] { "", "", "", "box:dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_AppendFormat_CompositeFormat_NoMethodSpec_ThrowsLoudGenArgsNull()
    {
        // The CompositeFormat TArg shape riding a PLAIN MemberReference (the
        // generic 0x30 signature without a MethodSpecification token): the
        // resolver decodes ps=[0x12,0x12,0x1E] with genArgs=null -- the closed
        // TArg carrier cannot be generated without the MethodSpec decode, so
        // the arm must reject LOUD (never a silent provider-kernel fallback
        // with uncast slots).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
                new byte[] { 0x30, 0x01, 0x03, 0x1C, 0x12, 0x05, 0x12, 0x05, 0x1E, 0x00 }), "AppendFormat"));

        Assert.Contains("genArgs=null", ex.Message);
        Assert.Contains("LOUD whitelist (", ex.Message);
    }

    [Fact]
    public void Resolve_AppendFormat_CompositeFormat_FourGenericArgs_ThrowsLoud()
    {
        // netcore defines TArg0..TArg2 generic overloads only -- a 4-slot
        // closed instantiation must reject LOUD (the kernel family tops at 3).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMethodSpec(SynthesizeGeneric(
                "System.Text", "StringBuilder", "AppendFormat",
                new byte[] { 0x30, 0x04, 0x06, 0x1C, 0x12, 0x05, 0x12, 0x05, 0x1E, 0x00, 0x1E, 0x01, 0x1E, 0x02, 0x1E, 0x03 },
                new byte[] { 0x0A, 0x04, 0x08, 0x08, 0x08, 0x08 },
                "System", "Int32")));

        Assert.Contains("LOUD whitelist (", ex.Message);
    }

    //--------------------------------------------------------------------------
    // The NESTED handler owner (System.Text.StringBuilder+AppendInterpolatedStringHandler)
    // routes into the DefaultInterpolatedStringHandler mapping: the ctor BINDS
    // the real builder into the handler slot (icall_interp_handler_bind), the
    // appends deref:r into it, and the provider ctor drops the provider
    // (documented net6+ deviation).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_NestedHandlerCtor_ThreeArgs_MapsToBindKernel()
    {
        // Hand-built blob (the Sig helper cannot express a CLASS param's
        // trailing TypeDefOrRef index): (int, int, StringBuilder).
        var resolved = ResolveMember(SynthesizeNested("System.Text", "StringBuilder", "AppendInterpolatedStringHandler",
            ".ctor", new byte[] { 0x00, 0x03, 0x01, 0x08, 0x08, 0x12, 0x05 }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_interp_handler_bind", resolved.CppName);
        Assert.Equal(3, resolved.ParamCount);
        Assert.Equal(new[] { "", "drop", "drop", "" }, resolved.ParamTypeNames);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_NestedHandlerCtor_Provider_MapsToBindKernel()
    {
        // (int, int, StringBuilder, IFormatProvider) -- the provider overload.
        var resolved = ResolveMember(SynthesizeNested("System.Text", "StringBuilder", "AppendInterpolatedStringHandler",
            ".ctor", new byte[] { 0x00, 0x04, 0x01, 0x08, 0x08, 0x12, 0x05, 0x12, 0x05 }), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_interp_handler_bind", resolved.CppName);
        Assert.Equal(4, resolved.ParamCount);
        Assert.Equal(new[] { "", "drop", "drop", "", "drop" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_NestedHandlerAppendLiteral_MapsToBuilderAppend()
    {
        var resolved = ResolveMember(SynthesizeNested("System.Text", "StringBuilder", "AppendInterpolatedStringHandler",
            "AppendLiteral", Sig(0x01, 0x0E)), "AppendLiteral");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_s", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "deref:r" }, resolved.ParamTypeNames);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_NestedHandlerAppendFormatted_String_MapsToBuilderAppend()
    {
        var resolved = ResolveMember(SynthesizeNested("System.Text", "StringBuilder", "AppendInterpolatedStringHandler",
            "AppendFormatted", Sig(0x01, 0x0E)), "AppendFormatted");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_s", resolved.CppName);
        Assert.Equal(new[] { "deref:r" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_NestedHandlerAppendFormatted_Int32_MapsToAppendI()
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericNested(
            "System.Text", "StringBuilder", "AppendInterpolatedStringHandler", "AppendFormatted",
            new byte[] { 0x30, 0x01, 0x01, 0x01, 0x1E, 0x00 },
            new byte[] { 0x0A, 0x01, 0x08 },
            "System", "Int32"));

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_i", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "deref:r" }, resolved.ParamTypeNames);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_NestedHandlerAppendFormatted_Decimal_MapsToBoxedAppendO()
    {
        var resolved = ResolveMethodSpec(SynthesizeGenericNested(
            "System.Text", "StringBuilder", "AppendInterpolatedStringHandler", "AppendFormatted",
            new byte[] { 0x30, 0x01, 0x01, 0x01, 0x1E, 0x00 },
            // The genArg TypeRef is ROW 3 of the synthetic table (outer=1,
            // nested=2, Decimal=3) -> coded index (3<<2)|1 = 0x0D.
            new byte[] { 0x0A, 0x01, 0x11, 0x0D },
            "System", "Decimal"));

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_o", resolved.CppName);
        Assert.Equal(new[] { "deref:r", "box:dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_NestedHandlerToStringAndClear_MapsToBuilderToString()
    {
        var resolved = ResolveMember(SynthesizeNested("System.Text", "StringBuilder", "AppendInterpolatedStringHandler",
            "ToStringAndClear", Sig(0x1C)), "ToStringAndClear");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_to_string", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // The surviving catch-alls stay LOUD and now carry the defensive marker:
    // no reference assembly defines these shapes.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_AppendFormat_UnwiredArity_DefensiveMarker()
    {
        // 6 args -- no net48/netcore AppendFormat defines this arity.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "StringBuilder", "AppendFormat",
                Sig(0x1C, 0x0E, 0x1C, 0x1C, 0x1C, 0x1C, 0x1C)), "AppendFormat"));

        Assert.Contains("defensive; no reference assembly defines this shape", ex.Message);
    }

    [Fact]
    public void Resolve_Insert_UnwiredValueParam_DefensiveMarker()
    {
        // Insert(int, <TypeHandle 0x40 value>) -- the Insert value-param
        // catch-all (no reference assembly defines the shape).
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "StringBuilder", "Insert",
                new byte[] { 0x00, 0x02, 0x1C, 0x08, 0x40, 0x05 }), "Insert"));

        Assert.Contains("defensive; no reference assembly defines this shape", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same harness as the other
    // MethodMapTests: real ECMA-335 assembly whose only MemberReference is
    // the member under test)
    //--------------------------------------------------------------------------
    private static void AssertChains(ResolvedMethod resolved)
    {
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Text.StringBuilder", resolved.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // Append/Insert value families (char[], decimal, narrow/unsigned ints,
    // float, object) and Equals(object).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_AppendCharArray_MapsToAppendCArray()
    {
        var m = ResolveMember(Synthesize("System.Text", "StringBuilder", "Append", new byte[] { 0x00, 0x01, 0x1C, 0x1D, 0x03 }), "Append");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_c_array", m.CppName);
        Assert.Equal(new[] { "", "a" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_AppendDecimal_MapsToAppendObjectBoxedDecimal()
    {
        var m = ResolveMember(Synthesize("System.Text", "StringBuilder", "Append",
            new byte[] { 0x00, 0x01, 0x1C, 0x11, 0x05 }), "Append");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_o", m.CppName);
        Assert.Equal(new[] { "", "box:dec" }, m.ParamTypeNames);
    }

    [Theory]
    [InlineData(0x09, "il2cpp::runtime::icall_stringbuilder_append_u4", "u4")]
    [InlineData(0x0B, "il2cpp::runtime::icall_stringbuilder_append_u8", "q")]
    public void Resolve_AppendUnsigned_MapsToUnsignedKernel(byte param, string cppName, string cast)
    {
        var m = ResolveMember(Synthesize("System.Text", "StringBuilder", "Append", Sig(0x1C, param)), "Append");

        Assert.Equal(cppName, m.CppName);
        Assert.Equal(new[] { "", cast }, m.ParamTypeNames);
    }

    [Theory]
    [InlineData(0x04, "il2cpp::runtime::icall_stringbuilder_insert_i", "i")]
    [InlineData(0x05, "il2cpp::runtime::icall_stringbuilder_insert_i", "i")]
    [InlineData(0x06, "il2cpp::runtime::icall_stringbuilder_insert_i", "i")]
    [InlineData(0x07, "il2cpp::runtime::icall_stringbuilder_insert_i", "i")]
    [InlineData(0x09, "il2cpp::runtime::icall_stringbuilder_insert_u4", "u4")]
    [InlineData(0x0B, "il2cpp::runtime::icall_stringbuilder_insert_u8", "q")]
    [InlineData(0x0C, "il2cpp::runtime::icall_stringbuilder_insert_d", "d")]
    [InlineData(0x1D, "il2cpp::runtime::icall_stringbuilder_insert_c_array", "a")]
    [InlineData(0x1C, "il2cpp::runtime::icall_stringbuilder_insert_o", "")]
    public void Resolve_InsertValueFamilies_MapToKernels(byte param, string cppName, string cast)
    {
        var m = ResolveMember(Synthesize("System.Text", "StringBuilder", "Insert", InsertSig(param)), "Insert");

        Assert.Equal(cppName, m.CppName);
        Assert.Equal(new[] { "", "i", cast }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_InsertDecimal_MapsToInsertObjectBoxedDecimal()
    {
        var m = ResolveMember(Synthesize("System.Text", "StringBuilder", "Insert",
            new byte[] { 0x00, 0x02, 0x1C, 0x08, 0x11, 0x05 }), "Insert");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_insert_o", m.CppName);
        Assert.Equal(new[] { "", "i", "box:dec" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_EqualsObject_MapsToReferenceEquality()
    {
        var m = ResolveMember(Synthesize("System.Text", "StringBuilder", "Equals", Sig(0x02, 0x1C)), "Equals");

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_equals_obj", m.CppName);
        Assert.Equal(new[] { "", "" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // LOUD-whitelist self-identification (2026-10-26). Every net48 StringBuilder
    // overload is wired (see MethodMap.StringBuilder.cs); the surviving catch-alls
    // are shapes net48 does not define. Each must announce its whitelist
    // category + the net48 Reference Source line it was checked against.
    //--------------------------------------------------------------------------
    public static IEnumerable<object[]> LoudWhitelistShapes() => new[]
    {
        new object[] { ".ctor",        new byte[] { 0x08, 0x08, 0x08 } },                  // 3 args (no such ctor)
        new object[] { "Append",       new byte[] { 0x1C, 0x1C, 0x1C, 0x1C } },            // 4 args
        new object[] { "Insert",       new byte[] { 0x08, 0x1C, 0x1C, 0x1C, 0x1C } },      // 5 args
        new object[] { "AppendLine",   new byte[] { 0x0E, 0x0E } },                        // 2 args
        new object[] { "AppendFormat", new byte[] { 0x0E } },                              // 1 arg
        new object[] { "Replace",      new byte[] { 0x0E, 0x0E, 0x0E } },                  // 3 args
        new object[] { "CopyTo",       new byte[] { 0x08, 0x08 } },                        // 2 args (net48: 4)
        new object[] { "EnsureCapacity", new byte[] { 0x08, 0x08 } },                      // 2 args
        new object[] { "Equals",       new byte[] { 0x1C, 0x1C } },                        // 2 args
    };

    [Theory]
    [MemberData(nameof(LoudWhitelistShapes))]
    public void Resolve_CatchAlls_AreSelfIdentifyingLoudWhitelists(string name, byte[] ps)
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Text", "StringBuilder", name, Sig(0x01, ps)), name));

        Assert.Contains("LOUD whitelist (", ex.Message);
        Assert.Contains("net48 RefSrc mscorlib/system/text/stringbuilder.cs:", ex.Message);
    }

    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        return ResolveMember(pe, memberName, receiverIsAddress: false);
    }

    // The fold's first-pass resolve passes "the stack top is an ADDRESS"
    // (arg0IsAddr -- the ldloca byref argument shape). The handler overloads
    // dispatch on it (a 0x11 VALUETYPE byte is either a decimal VALUE arg or
    // the BYREF-wrapped nested handler address).
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, bool receiverIsAddress)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(), null, receiverIsAddress, null, null);
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), param count (=
    // ps.Length), return byte, then one byte per parameter (the raw bytes
    // RawSignature decodes; CLASS params 0x12 carry a trailing coded-index
    // byte the decoder consumes).
    // Insert(int, value) signature: the char[] value needs its element byte.
    private static byte[] InsertSig(byte value) =>
        value == 0x1D
            ? new byte[] { 0x00, 0x02, 0x1C, 0x08, 0x1D, 0x03 }
            : Sig(0x1C, 0x08, value);

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

    // A NESTED-type MemberRef: the resolution scope is the DECLARING TypeRef
    // (StringBuilder) and the nested name carries an empty namespace -- the
    // resolver's GetFullName walks the scope chain to
    // "System.Text.StringBuilder+AppendInterpolatedStringHandler".
    private static MemoryStream SynthesizeNested(string ns, string typeName, string nestedName,
        string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var outerRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        var nestedRef = mdb.AddTypeReference(outerRef, default(StringHandle), mdb.GetOrAddString(nestedName));
        mdb.AddMemberReference(nestedRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
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

    // Resolve a MethodSpecification token (the generic-instantiation call shape).
    private static ResolvedMethod ResolveMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(), null, false, null, null);
    }

    // MemberReference + MethodSpecification. The generic signature names a
    // SECOND TypeRef (row 2 -> coded index 0x09) so GenericArgFullNames can
    // resolve the closed value-type name (e.g. System.Decimal).
    private static MemoryStream SynthesizeGeneric(string ns, string typeName, string memberName,
        byte[] memberRefSignature, byte[] genericSignature, string genArgNs, string genArgName)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString(genArgNs), mdb.GetOrAddString(genArgName));
        var memberRef = mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(memberRefSignature));
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(genericSignature));
        return Serialize(mdb);
    }

    // The nested-handler MethodSpec form: the underlying MemberRef's parent is
    // a NESTED TypeRef (scope = the StringBuilder TypeRef), so the owner full
    // name is the '+' form the resolver's scope walk produces.
    private static MemoryStream SynthesizeGenericNested(string ns, string typeName, string nestedName,
        string memberName, byte[] memberRefSignature, byte[] genericSignature, string genArgNs, string genArgName)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var outerRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        var nestedRef = mdb.AddTypeReference(outerRef, default(StringHandle), mdb.GetOrAddString(nestedName));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString(genArgNs), mdb.GetOrAddString(genArgName));
        var memberRef = mdb.AddMemberReference(nestedRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(memberRefSignature));
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(genericSignature));
        return Serialize(mdb);
    }
}

