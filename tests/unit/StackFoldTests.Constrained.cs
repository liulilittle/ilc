using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// constrained. prefix support (e2e-recheck4 G3 -- guidcase/threadcase blocker).
// The 0xFE16 prefix (ECMA III.4.2) is Roslyn's standard emission for value-type
// virtual calls: `ldloca <slot>; constrained. T; callvirt M`. guidcase.exe IL
// dump: `constrained. System.Guid (0x0100000D); callvirt instance string
// System.Object::ToString()` -- the MemberRef owner is System.Object (value
// types route through Object::ToString), the constrained type provides the
// receiver's declared type.
//
// PAIRING CONTRACT: the decoder (IlDecoder.cs) consumes the prefix and emits
// it as a no-op marker (OpCode Nop, TypeToken operand preserved). StackFold
// never sees a raw Constrained instruction (its main switch has no case --
// default LOUD) and the callvirt's existing receiver-shape machinery does all
// the work: the ldloca receiver (LocAddrExpr) sets arg0IsAddr, so the value-
// type mapping picks "deref:*" (Int32::ToString -> icall_int32_to_string with
// deref:i); a reference-type constrained receiver behaves exactly like a
// plain callvirt (icall_object_to_string).
//------------------------------------------------------------------------------
public class StackFoldTestsConstrained
{
    [Fact]
    public void Fold_LdlocaConstrainedCallvirt_Int32ToString_DerefReceiver()
    {
        // ldc.i4.5; stloc.0; ldloca.s 0; constrained. System.Int32;
        // callvirt instance string System.Int32::ToString(); ret
        using var pe = Synthesize(BodyKind.LdlocaConstrainedCallvirtInt32);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var call = Assert.Single(FindCallExprs(stmts));
        Assert.Equal("il2cpp::runtime::icall_int32_to_string", call.Resolved!.CppName);
        // the ldloca receiver is an ADDRESS -> the deref cast, not unbox
        Assert.Equal("deref:i", call.Resolved.ParamTypeNames![0]);
        Assert.IsType<LocAddrExpr>(call.Args[0]);
    }

    [Fact]
    public void Fold_LdlocaCallvirt_Int32ToString_NoConstrained_Identical()
    {
        // control: the same shape WITHOUT the constrained prefix must fold to
        // the identical mapping (the prefix has no stack effect; the decoder
        // pairing must not disturb the fold).
        using var pe = Synthesize(BodyKind.LdlocaCallvirtInt32);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var call = Assert.Single(FindCallExprs(stmts));
        Assert.Equal("il2cpp::runtime::icall_int32_to_string", call.Resolved!.CppName);
        Assert.Equal("deref:i", call.Resolved.ParamTypeNames![0]);
    }

    [Fact]
    public void Fold_LdlocaConstrainedCallvirt_ObjectOwnerToString_GuidReceiver_MapsToGuidIcall()
    {
        // The REAL Roslyn value-type shape (guidcase ildump): the callvirt
        // MemberRef parent is System.Object (value types route through
        // Object::ToString), the constrained. TypeToken carries the receiver's
        // declared type. Before the constrained-recvType fold this resolved to
        // a generated name and the vtable emitter crashed with "callvirt owner
        // System.Object not in translated types" (CodeGenerator VirtualTarget).
        // ldc.i4.5; stloc.0; ldloca.s 0; constrained. System.Guid;
        // callvirt instance string System.Object::ToString(); ret
        using var pe = Synthesize(BodyKind.LdlocaConstrainedCallvirtObjectToStringGuid);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var call = Assert.Single(FindCallExprs(stmts));
        Assert.Equal("il2cpp::il2cpp_guid_to_string", call.Resolved!.CppName);
        Assert.True(call.Resolved.ReturnsRef); // string result
        Assert.IsType<LocAddrExpr>(call.Args[0]);
        // the receiver ADDRESS carries the EXPLICIT "g" byte-pointer cast
        // -- the exact MethodMap.Guid.cs ToString shape (Guid.cs:56 returns
        // new[] { "g" }, CodeGenerator renders it (uint8_t*)(...)). NOT the
        // raw "" passthrough: the interlockedcase fix (2026-08-05) made the
        // Guid byte-slot cast explicit because the old blanket ""-cast broke
        // Interlocked's int32_t*/int64_t* location params.
        Assert.Equal("g", call.Resolved.ParamTypeNames![0]);
    }

    [Fact]
    public void Fold_LdlocaConstrainedCallvirt_ObjectOwnerToString_DateTimeReceiver_MapsToDateTimeIcall()
    {
        // DateTime ToString on a value slot: same Object-owner constrained
        // shape (dt.ToString()); the constrained TypeToken (System.DateTime)
        // must route to the DateTime icall with the deref:dt receiver cast
        // (MethodMap.DateTime.cs ToString shape).
        // ldc.i4.5; stloc.0; ldloca.s 0; constrained. System.DateTime;
        // callvirt instance string System.Object::ToString(); ret
        using var pe = Synthesize(BodyKind.LdlocaConstrainedCallvirtObjectToStringDateTime);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var call = Assert.Single(FindCallExprs(stmts));
        Assert.Equal("il2cpp::runtime::icall_datetime_to_string", call.Resolved!.CppName);
        Assert.True(call.Resolved.ReturnsRef); // string result
        Assert.IsType<LocAddrExpr>(call.Args[0]);
        Assert.Equal("deref:dt", call.Resolved.ParamTypeNames![0]);
    }

    [Fact]
    public void Fold_ConstrainedCallvirt_StringReceiver_EqualsPlainCallvirt()
    {
        // reference-type constrained receiver (System.String): ECMA says the
        // call behaves exactly like an unconstrained callvirt. ldstr "abc";
        // constrained. System.String; callvirt Object::ToString(); ret.
        using var constrained = Synthesize(BodyKind.ConstrainedCallvirtStringToString);
        using var plain = Synthesize(BodyKind.PlainCallvirtStringToString);
        var md1 = constrained.GetMetadataReader();
        var md2 = plain.GetMetadataReader();

        var withPrefix = FindCallExprs(Fold(constrained, md1));
        var withoutPrefix = FindCallExprs(Fold(plain, md2));

        var call = Assert.Single(withPrefix);
        var control = Assert.Single(withoutPrefix);
        Assert.Equal("il2cpp::runtime::icall_object_to_string", call.Resolved!.CppName);
        Assert.Equal(control.Resolved!.CppName, call.Resolved.CppName);
        Assert.Equal(control.Resolved.ParamTypeNames, call.Resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Test harness.
    //--------------------------------------------------------------------------
    private static List<Stmt> Fold(PEReader pe, MetadataReader md)
    {
        var main = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Main");
        var bodyInfo = pe.GetMethodBody(md.GetMethodDefinition(main).RelativeVirtualAddress);
        var body = IlDecoder.DecodeMethod(bodyInfo!.GetILBytes()!, md);
        return StackFold.Fold(body, new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());
    }

    // OptimizeTemps can inline a single-use call into the Ret -- scan both
    // statement shapes for emitted CallExprs. (Both tested calls return
    // values, so CallStmt never occurs here.)
    private static IEnumerable<CallExpr> FindCallExprs(List<Stmt> stmts) => stmts
        .SelectMany(st => st switch
        {
            AssignStmt a when a.Value is CallExpr c => new[] { c },
            RetStmt r when r.Value is CallExpr c => new[] { c },
            _ => Array.Empty<CallExpr>(),
        });

    private enum BodyKind
    {
        LdlocaConstrainedCallvirtInt32,  // ldloca.s + constrained + callvirt Int32::ToString
        LdlocaCallvirtInt32,             // control: same without constrained
        ConstrainedCallvirtStringToString, // ldstr + constrained String + callvirt Object::ToString
        PlainCallvirtStringToString,     // control: ldstr + callvirt Object::ToString
        LdlocaConstrainedCallvirtObjectToStringGuid,     // ldloca.s + constrained Guid + callvirt Object::ToString
        LdlocaConstrainedCallvirtObjectToStringDateTime, // ldloca.s + constrained DateTime + callvirt Object::ToString
    }

    private static PEReader Synthesize(BodyKind kind)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var int32Ref = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Int32"));
        var strRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("String"));
        var guidRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Guid"));
        var dateTimeRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTime"));
        var us = mdb.GetOrAddUserString("abc");

        // instance string Int32::ToString() -- 0 params, string return.
        var int32ToString = mdb.AddMemberReference(int32Ref, mdb.GetOrAddString("ToString"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x0E }));
        // instance string Object::ToString() -- 0 params, string return.
        var objToString = mdb.AddMemberReference(objRef, mdb.GetOrAddString("ToString"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x0E }));

        var int32Token = MetadataTokens.GetToken(int32Ref);
        var int32ToStringToken = MetadataTokens.GetToken(int32ToString);
        var objToStringToken = MetadataTokens.GetToken(objToString);

        // anchor TypeDef so Main resolves (dummy field + method per the
        // TypeDef methodList range convention)
        var dummyField = mdb.AddFieldDefinition(FieldAttributes.Private | FieldAttributes.Static,
            mdb.GetOrAddString("s_dummy"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
        var dummy = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Dummy"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), 0, default(ParameterHandle));

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        switch (kind)
        {
            case BodyKind.LdlocaConstrainedCallvirtInt32:
                encoder.OpCode(ILOpCode.Ldc_i4_5);
                encoder.OpCode(ILOpCode.Stloc_0);
                encoder.LoadLocalAddress(0);
                Constrained(encoder, int32Token);
                Callvirt(encoder, int32ToStringToken);
                break;
            case BodyKind.LdlocaCallvirtInt32:
                encoder.OpCode(ILOpCode.Ldc_i4_5);
                encoder.OpCode(ILOpCode.Stloc_0);
                encoder.LoadLocalAddress(0);
                Callvirt(encoder, int32ToStringToken);
                break;
            case BodyKind.ConstrainedCallvirtStringToString:
                Ldstr(encoder, us);
                Constrained(encoder, MetadataTokens.GetToken(strRef));
                Callvirt(encoder, objToStringToken);
                break;
            case BodyKind.PlainCallvirtStringToString:
                Ldstr(encoder, us);
                Callvirt(encoder, objToStringToken);
                break;
            case BodyKind.LdlocaConstrainedCallvirtObjectToStringGuid:
                encoder.OpCode(ILOpCode.Ldc_i4_5);
                encoder.OpCode(ILOpCode.Stloc_0);
                encoder.LoadLocalAddress(0);
                Constrained(encoder, MetadataTokens.GetToken(guidRef));
                Callvirt(encoder, objToStringToken);
                break;
            case BodyKind.LdlocaConstrainedCallvirtObjectToStringDateTime:
                encoder.OpCode(ILOpCode.Ldc_i4_5);
                encoder.OpCode(ILOpCode.Stloc_0);
                encoder.LoadLocalAddress(0);
                Constrained(encoder, MetadataTokens.GetToken(dateTimeRef));
                Callvirt(encoder, objToStringToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Base"), objRef, dummyField,
            mdb.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
                mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle)));

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }

    private static void Constrained(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Constrained);
        e.Token(token);
    }

    private static void Callvirt(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Callvirt);
        e.Token(token);
    }

    private static void Ldstr(InstructionEncoder e, UserStringHandle us)
    {
        e.LoadString(us);
    }
}
