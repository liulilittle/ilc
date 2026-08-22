using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Decimal dispatch tests: prove the System.Decimal domain
// (TryMapDecimal in MethodMap.Decimal.cs + the TryMapFormat custom-format
// branch) routes each member to the matching decimal.h icall with the
// runtime's POINTER-shaped 16-byte value passing:
//
//   decimal op_Addition(a, b)      -> il2cpp::runtime::icall_decimal_add
//                                     (ResultKind "dec": the AssignStmt
//                                     special case prepends the result slot)
//   decimal op_UnaryNegation(a)    -> il2cpp::runtime::icall_decimal_negate
//   bool op_Equality/Inequality    -> icall_decimal_equals / !equals
//   decimal Parse(string)          -> il2cpp::runtime::icall_decimal_parse
//   bool TryParse(string, out d)   -> il2cpp::runtime::icall_decimal_try_parse
//   decimal .ctor(int) / 5-arg     -> icall_decimal_ctor_int / ctor_5
//   string ToString()              -> icall_decimal_to_string
//   string ToString(string format) -> icall_decimal_to_string_custom (Format)
//   int32 CompareTo(decimal)       -> icall_decimal_compare
//
// The runtime treats a decimal as an opaque 16-byte value (Il2CppDecimal);
// every icall takes RAW POINTERS to value slots (const Il2CppDecimal*), so
// every decimal ARGUMENT and RECEIVER carries the "dec" ParamTypeNames cast
// -- the CodeGenerator "dec" shape-aware cast passes ADDRESS expressions
// (ldloca/ldsflda) through uncast and address-of VALUE expressions (a
// 16-byte struct does not decay to a pointer, CodeGenerator.cs "dec"
// branch). Producing icalls carry the "dec" ResultKind marker (the
// generated-code slot type is Il2CppDecimal -- TypeCppName entry + "dec"
// temp kind, CodeGenerator).
//
// Known-unwireable overloads reject LOUDLY (NotSupportedException with the
// "{owner}::{name}" prefix -- see BclResolve's wrap):
//   * op_LessThan/op_GreaterThan/op_LessThanOrEqual/op_GreaterThanOrEqual
//     -- the emitter cannot stack comparisons on icall_decimal_compare.
//   * Equals(object)/CompareTo(object) -- no runtime box class yet (T1).
//   * .ctor(float/double), ToDouble/ToSingle -- VarDecFromR4/R8 deferred.
//
// The PEs are synthesized with System.Reflection.Metadata's own writer (the
// same reader pipeline the transpiler uses); valuetype (0x11) params carry
// the TypeRef coded index (row 1 -> 0x05) after the 0x11 byte.
public class MethodMapDecimalTests
{
    //--------------------------------------------------------------------------
    // op_Addition -- static, two decimal addresses, "dec" result kind
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_OpAddition_MapsToAddIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Addition",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x11, 0x05 }), "op_Addition");

        Assert.Equal("il2cpp::runtime::icall_decimal_add", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "dec", "dec" }, resolved.ParamTypeNames);
        Assert.Equal("dec", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Decimal_OpSubtraction_MapsToSubIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Subtraction",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x11, 0x05 }), "op_Subtraction");

        Assert.Equal("il2cpp::runtime::icall_decimal_sub", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Decimal_OpDivision_MapsToDivIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Division",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x11, 0x05 }), "op_Division");

        Assert.Equal("il2cpp::runtime::icall_decimal_div", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Decimal_OpRemainder_MapsToRemIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Remainder",
            new byte[] { 0x00, 0x02, 0x11, 0x05, 0x11, 0x05, 0x11, 0x05 }), "op_Remainder");

        Assert.Equal("il2cpp::runtime::icall_decimal_rem", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Decimal_OpUnaryNegation_MapsToNegateIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_UnaryNegation",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x11, 0x05 }), "op_UnaryNegation");

        Assert.Equal("il2cpp::runtime::icall_decimal_negate", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "dec" }, resolved.ParamTypeNames);
        Assert.Equal("dec", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // Ordering comparisons -- compare-stacking in PREFIX form (the emitter
    // formats {CppName}({args}); `cmp > 0` is expressed as `0 < cmp(...)`,
    // the op_Inequality "!"-prefix precedent -- decimal-domain.md 2.1's
    // "compare stacking" design)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_OpGreaterThan_MapsToComparePrefix()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_GreaterThan",
            new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05 }), "op_GreaterThan");

        Assert.Equal("0 < il2cpp::runtime::icall_decimal_compare", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Decimal_OpLessThan_MapsToComparePrefix()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_LessThan",
            new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05 }), "op_LessThan");

        Assert.Equal("0 > il2cpp::runtime::icall_decimal_compare", resolved.CppName);
    }

    [Fact]
    public void Resolve_Decimal_OpLessThanOrEqual_MapsToComparePrefix()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_LessThanOrEqual",
            new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05 }), "op_LessThanOrEqual");

        Assert.Equal("0 >= il2cpp::runtime::icall_decimal_compare", resolved.CppName);
    }

    [Fact]
    public void Resolve_Decimal_OpGreaterThanOrEqual_MapsToComparePrefix()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_GreaterThanOrEqual",
            new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05 }), "op_GreaterThanOrEqual");

        Assert.Equal("0 <= il2cpp::runtime::icall_decimal_compare", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // op_Implicit -- integer widening folds to the ctor icalls (result slot
    // prepended by the AssignStmt "dec" special case)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_OpImplicitInt_MapsToCtorIntIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Implicit",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x08 }), "op_Implicit");

        Assert.Equal("il2cpp::runtime::icall_decimal_ctor_int32", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Decimal_OpImplicitLong_MapsToCtorLongIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Implicit",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x0A }), "op_Implicit");

        Assert.Equal("il2cpp::runtime::icall_decimal_ctor_int64", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // Equality -- == maps to the equals icall, != to the "!"-negated form
    // (Guid op_Inequality precedent)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_OpEquality_MapsToEqualsIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Equality",
            new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05 }), "op_Equality");

        Assert.Equal("il2cpp::runtime::icall_decimal_equals", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "dec", "dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Decimal_OpInequality_MapsToNegatedEqualsIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "op_Inequality",
            new byte[] { 0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05 }), "op_Inequality");

        Assert.Equal("!il2cpp::runtime::icall_decimal_equals", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // ToString -- parameterless maps to the G-default icall (receiver address
    // uncast); ToString(string format) routes through TryMapFormat to the
    // custom-format icall
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_ToString_MapsToStringIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "ToString", Sig(0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_decimal_to_string", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
        Assert.Equal(new[] { "dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Decimal_ToStringFormat_MapsToCustomIcall()
    {
        // string ToString(string format): the TryMapFormat owner branch wins
        // (Format is tried BEFORE Decimal in the dispatch chain). The raw
        // resolve path carries no receiver address hint, so the receiver cast
        // is the BOXED form (unbox:dec) -- the deref:dec form appears when
        // the fold supplies receiverIsAddress (ldloca receivers).
        var resolved = ResolveMember(Synthesize("System", "Decimal", "ToString",
            Sig(0x0E, 0x0E)), "ToString");

        Assert.Equal("il2cpp::runtime::icall_decimal_to_string_custom", resolved.CppName);
        Assert.Equal(new[] { "unbox:dec", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Parse / TryParse -- Parse's result slot is prepended by the AssignStmt
    // special case (ResultKind "dec"); TryParse's out param is the ldloca
    // address, passed through uncast
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_Parse_MapsToParseIcall()
    {
        // static decimal Parse(string): ret valuetype + coded index, param
        // string.
        var resolved = ResolveMember(Synthesize("System", "Decimal", "Parse",
            new byte[] { 0x00, 0x01, 0x11, 0x05, 0x0E }), "Parse");

        Assert.Equal("il2cpp::runtime::icall_decimal_parse", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal(new[] { "s" }, resolved.ParamTypeNames);
        Assert.Equal("dec", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Decimal_TryParse_MapsToTryParseIcall()
    {
        // static bool TryParse(string, out decimal): bool ret, string param,
        // BYREF (0x10) out param -- the out-address is the icall's buffer.
        var resolved = ResolveMember(Synthesize("System", "Decimal", "TryParse",
            new byte[] { 0x00, 0x02, 0x02, 0x0E, 0x10, 0x11, 0x05 }), "TryParse");

        Assert.Equal("il2cpp::runtime::icall_decimal_try_parse", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "s", "" }, resolved.ParamTypeNames);
        Assert.Null(resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // CompareTo / Compare -- the compare icall, receiver/args uncast
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_CompareTo_MapsToCompareIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "CompareTo",
            new byte[] { 0x00, 0x01, 0x08, 0x11, 0x05 }), "CompareTo");

        Assert.Equal("il2cpp::runtime::icall_decimal_compare", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "dec", "dec" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Decimal_StaticCompare_MapsToCompareIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "Compare",
            new byte[] { 0x00, 0x02, 0x08, 0x11, 0x05, 0x11, 0x05 }), "Compare");

        Assert.Equal("il2cpp::runtime::icall_decimal_compare", resolved.CppName);
        Assert.False(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Decimal_EqualsObject_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Decimal", "Equals", Sig(0x02, 0x1C)), "Equals"));

        Assert.Contains("System.Decimal::Equals", ex.Message);
    }

    //--------------------------------------------------------------------------
    // ctors -- the vctor path passes the value slot as the first argument
    // (write-through; NO ResultIsReceiver, NO ResultKind -- the temp type
    // comes from ValueTypeNewExpr -> TypeCppName)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_CtorInt_MapsToCtorIntIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", ".ctor",
            Sig(0x01, 0x08)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_decimal_ctor_int32", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("dec", resolved.ResultKind); // blocker A: the AssignStmt "dec" special case needs the marker
    }

    [Fact]
    public void Resolve_Decimal_Ctor5_MapsToCtor5Icall()
    {
        // void .ctor(int lo, int mid, int hi, bool sign, byte scale) -- the
        // 5-arg shape is the IL form of a decimal literal (1.5m).
        var resolved = ResolveMember(Synthesize("System", "Decimal", ".ctor",
            Sig(0x01, 0x08, 0x08, 0x08, 0x02, 0x03)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_decimal_ctor_5", resolved.CppName);
        Assert.Equal(5, resolved.ParamCount);
        Assert.Equal("dec", resolved.ResultKind); // blocker A: 5-arg literal ctor must carry the marker
        // blocker B: instance + void -- the ldloca receiver rides the stack as
        // args[0] and the fold takes the void-CallStmt path (LocAddrExpr ->
        // (&slot) write-through; the IL ctor pushes nothing).
        Assert.True(resolved.IsInstance);
        Assert.False(resolved.ReturnsValue);
    }

    [Fact]
    public void Resolve_Decimal_CtorUInt_MapsToCtorUIntIcall()
    {
        // decimal(uint32): the unsigned 32-bit ctor (decimal.h:99). Roslyn
        // emits the RAW U4 byte (0x09), not a VALUETYPE (MethodSignatureCoder
        // audit H1).
        var resolved = ResolveMember(Synthesize("System", "Decimal", ".ctor",
            Sig(0x01, 0x09)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_decimal_ctor_uint32", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("dec", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Decimal_CtorULong_MapsToCtorULongIcall()
    {
        // decimal(uint64): the unsigned 64-bit ctor (decimal.h:101) -- RAW U8
        // byte (0x0B).
        var resolved = ResolveMember(Synthesize("System", "Decimal", ".ctor",
            Sig(0x01, 0x0B)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_decimal_ctor_uint64", resolved.CppName);
        Assert.Equal(1, resolved.ParamCount);
        Assert.Equal("dec", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Decimal_CtorFloat_Wires_VarDecFromR4()
    {
        // netcore21+ audit closure (updated from the old LOUD pin): the
        // float/double conversion ctors wire to the VarDecFromR4/R8
        // implementations (decimal.cpp -- 15-significant-digit rounding).
        var resolved = ResolveMember(Synthesize("System", "Decimal", ".ctor", Sig(0x01, 0x0C)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_decimal_ctor_float", resolved.CppName);
        Assert.Equal("dec", resolved.ResultKind);
    }

    //--------------------------------------------------------------------------
    // ToDouble -- VarR8FromDec (wired: the exact 96-bit decomposition)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Decimal_ToDouble_Wires_VarR8FromDec()
    {
        var resolved = ResolveMember(Synthesize("System", "Decimal", "ToDouble",
            new byte[] { 0x00, 0x01, 0x0D, 0x11, 0x05 }), "ToDouble");

        Assert.Equal("il2cpp::runtime::icall_decimal_to_double", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as MethodMapGuidTests)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
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
