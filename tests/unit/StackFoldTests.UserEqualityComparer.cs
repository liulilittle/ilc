using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using Il2Cpp.Transpiler.Emit;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// caudit2: EqualityComparer<T>.Default for a USER struct (G6-N second layer).
//
// `struct St : IEquatable<St>` + `EqualityComparer<St>.Default.Equals(a, b)`
// must fold to the user's own `St::Equals(St)` implementation -- the exact IR
// a source-level `a.Equals(b)` produces (net48's default comparer routes to
// IEquatable<T>.Equals; the probe's St implements it). The fold is:
//   1. `call get_Default` on EqualityComparer`1<user T> -> a typed null
//      receiver marker (pseudo-type), no runtime singleton needed;
//   2. `callvirt Equals(T,T)` whose receiver is that marker -> a direct
//      user-method call with this = &a and the single `other` argument = b
//      (the comparer receiver is DROPPED).
// A receiver that is NOT the folded Default marker (e.g. a user-supplied
// comparer variable) must NOT fold -- it keeps the ordinary callvirt path.
//------------------------------------------------------------------------------
public class StackFoldTestsUserEqualityComparer
{
    private const string StFullName = "Cases.St";
    private const string EqualsParamTypes = "V:Cases.St";

    [Fact]
    public void Fold_EqualityComparerOfUserStructDefaultEquals_BecomesUserEqualsCall()
    {
        using var pe = Synthesize(BodyKind.DefaultEqualsDirect);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var call = Assert.Single(FindCallExprs(stmts));
        Assert.Equal(
            MethodRefResolver.UserMethodCppName(StFullName, "Equals", EqualsParamTypes),
            call.Resolved!.CppName);
        Assert.StartsWith("Cases_St_Equals_", call.Resolved.CppName);
        Assert.True(call.Resolved.IsInstance);
        Assert.True(call.Resolved.ReturnsValue);
        Assert.False(call.Resolved.ReturnsRef);
        // this = the ADDRESS of the first T argument (value-type ABI), the
        // single explicit argument is the second T value (the comparer
        // receiver is gone -- only two args survive).
        Assert.Equal(2, call.Args.Count);
        Assert.IsType<LocAddrExpr>(call.Args[0]);
        Assert.IsType<LocExpr>(call.Args[1]);
        // no leftover EqualityComparer call in the statement list
        Assert.DoesNotContain(stmts, s => s is AssignStmt { Value: CallExpr c2 }
            && c2.Resolved!.CppName.Contains("EqualityComparer", StringComparison.Ordinal));
    }

    [Fact]
    public void Fold_EqualityComparerOfUserStructDefaultEquals_NoDefaultGetterCallEmitted()
    {
        using var pe = Synthesize(BodyKind.DefaultEqualsDirect);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        // get_Default folds to the tracked null marker -- no CallExpr for it
        // (the only call is the user Equals).
        Assert.DoesNotContain(stmts, s => s is AssignStmt { Value: CallExpr c2 }
            && c2.Resolved!.CppName.Contains("get_Default", StringComparison.Ordinal));
    }

    [Fact]
    public void Fold_UntrackedComparerReceiver_DoesNotFold()
    {
        // A comparer VARIABLE (receiver not tracked as the folded get_Default
        // marker) must keep the ordinary (loud) callvirt path -- the fold must
        // never rewrite a user-supplied comparer to a.Equals(b). The unmapped
        // BCL owner stays the P26 loud exception (same as the pre-fix base).
        using var pe = Synthesize(BodyKind.UntrackedReceiver);
        var md = pe.GetMetadataReader();

        var ex = Assert.Throws<NotSupportedException>(() => Fold(pe, md));
        Assert.Contains("::Equals", ex.Message);
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
        var st = new TypeDef { Name = "St", Namespace = "Cases", IsValueType = true };
        st.Methods.Add(new MethodDef { Name = "Equals", Owner = st, ParamCount = 1, ParamTypes = EqualsParamTypes });
        return StackFold.Fold(body, new MethodContext { ArgCount = 0 }, md, new[] { st });
    }

    private static IEnumerable<CallExpr> FindCallExprs(List<Stmt> stmts) => stmts
        .SelectMany(st => st switch
        {
            AssignStmt a when a.Value is CallExpr c => new[] { c },
            RetStmt r when r.Value is CallExpr c => new[] { c },
            _ => Array.Empty<CallExpr>(),
        });

    private enum BodyKind
    {
        DefaultEqualsDirect, // call get_Default; ldloc.0; ldloc.1; callvirt Equals; ret
        UntrackedReceiver,   // ldloc.2 (untyped comparer); ldloc.0; ldloc.1; callvirt Equals; ret
    }

    private static PEReader Synthesize(BodyKind kind)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var eqDefRef = mdb.AddTypeReference(mscorlib,
            mdb.GetOrAddString("System.Collections.Generic"), mdb.GetOrAddString("EqualityComparer`1"));

        // St TypeDef (value type; the synthetic model supplies the Equals method).
        var stDef = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("St"), default(EntityHandle),
            default(FieldDefinitionHandle), default(MethodDefinitionHandle));

        // EqualityComparer`1<Cases.St> TypeSpec: 0x15 GENERICINST, 0x12 CLASS
        // (the generic definition -- a TypeRef), arg count 1, 0x11 VALUETYPE
        // (the user TypeDef -- the REAL type identity lost by the generic-
        // instance name channel which collapses VALUETYPE args to "valuetype").
        var tsBlob = new BlobBuilder();
        tsBlob.WriteByte(0x15);
        tsBlob.WriteByte(0x12);
        tsBlob.WriteCompressedInteger(CodedIndex(eqDefRef));
        tsBlob.WriteCompressedInteger(1);
        tsBlob.WriteByte(0x11);
        tsBlob.WriteCompressedInteger(CodedIndex(stDef));
        var eqSt = mdb.AddTypeSpecification(mdb.GetOrAddBlob(tsBlob));

        // static EqualityComparer`1<St> get_Default(): 00 00 12 <coded ts>
        var getDefaultSig = new BlobBuilder();
        getDefaultSig.WriteByte(0x00);
        getDefaultSig.WriteCompressedInteger(0);
        getDefaultSig.WriteByte(0x12);
        getDefaultSig.WriteCompressedInteger(CodedIndex(eqSt));
        var getDefault = mdb.AddMemberReference(eqSt, mdb.GetOrAddString("get_Default"),
            mdb.GetOrAddBlob(getDefaultSig));

        // instance bool Equals(!0, !0) at the instantiation: 20 02 01 11 <coded st> 11 <coded st>
        var equalsSig = new BlobBuilder();
        equalsSig.WriteByte(0x20);
        equalsSig.WriteCompressedInteger(2);
        equalsSig.WriteByte(0x01);
        equalsSig.WriteByte(0x11);
        equalsSig.WriteCompressedInteger(CodedIndex(stDef));
        equalsSig.WriteByte(0x11);
        equalsSig.WriteCompressedInteger(CodedIndex(stDef));
        var equals = mdb.AddMemberReference(eqSt, mdb.GetOrAddString("Equals"),
            mdb.GetOrAddBlob(equalsSig));

        var getDefaultToken = MetadataTokens.GetToken(getDefault);
        var equalsToken = MetadataTokens.GetToken(equals);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        switch (kind)
        {
            case BodyKind.DefaultEqualsDirect:
                encoder.OpCode(ILOpCode.Call);
                encoder.Token(getDefaultToken);
                break;
            case BodyKind.UntrackedReceiver:
                encoder.LoadLocal(2); // untracked receiver
                break;
        }
        encoder.LoadLocal(0); // a
        encoder.LoadLocal(1); // b
        encoder.OpCode(ILOpCode.Callvirt);
        encoder.Token(equalsToken);
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 3, default(StandaloneSignatureHandle), MethodBodyAttributes.None);

        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Probe"), objRef, default(FieldDefinitionHandle),
            mdb.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
                mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle)));

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var peBlob = new BlobBuilder();
        peBuilder.Serialize(peBlob);
        return new PEReader(new MemoryStream(peBlob.ToArray()));
    }

    // TypeDefOrRef coded index (ECMA II.24.2.6): (row << 2) | tag
    // (TypeDef 0, TypeRef 1, TypeSpec 2).
    private static int CodedIndex(EntityHandle handle)
    {
        var token = MetadataTokens.GetToken(handle);
        var row = token & 0xFFFFFF;
        var tag = (token >> 24) switch
        {
            0x02 => 0, // TypeDef
            0x01 => 1, // TypeRef
            0x1B => 2, // TypeSpec
            _ => throw new InvalidOperationException($"not a TypeDefOrRef handle: 0x{token:X8}"),
        };
        return (row << 2) | tag;
    }
}
