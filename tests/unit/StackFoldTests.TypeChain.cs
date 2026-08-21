using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// Type-chain fold tests (typeof(T) consumer calls -- the Ldtoken fold's
// follow-up gaps). The GetTypeFromHandle fold pushes the type's FULL-NAME
// string; the consuming call folds further:
//
//   * Type::get_Name on the folded value -> the SHORT name (net48:
//     typeof(int).Name == "Int32" -- truncate the full name at the last
//     '.'; "Cases.Base" has no dot, keeps the whole name).
//   * Type::GetTypeCode (static, 1-param form) on the folded value -> the
//     typecode CONSTANT (net48 values, mirror of the runtime's
//     icall_object_get_type_code table in typecheck.cpp: Int32=9,
//     Boolean=3, Char=4, Double=14, String=18, everything else -> 1).
//   * Console::WriteLine(object) whose argument is a folded Type value
//     (or any bare string literal) -> the write_line_s path -- the value
//     IS a string (the Console domain's tracked-box switch gets the hint
//     arg0Type="System.String").
//
// Controls: the folds are gated on the FOLDED string being the stack top
// (prevIns == the GetTypeFromHandle call). A GetTypeFromHandle RESULT that
// is a runtime temp is NOT compile-time folded (the runtime short-name
// icall for get_Name, the typecode icall for GetTypeCode), the 0-param
// instance GetTypeCode form stays LOUD, and WriteLine(object) with a
// non-string untracked argument stays LOUD.
//------------------------------------------------------------------------------
public class StackFoldTestsTypeChain
{
    //--------------------------------------------------------------------------
    // GetTypeCode: typeof(int)/typeof(string)/typeof(Boolean) chains fold to
    // the typecode constant; typeof(Cases.Base) (TypeDef, non-primitive)
    // folds to TypeCode.Object = 1 (the runtime's fallback).
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_GetTypeCode_Int32Chain_FoldsToTypecodeConstant()
    {
        using var pe = Synthesize(BodyKind.GetTypeCodeInt32);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(9, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_GetTypeCode_StringChain_FoldsToTypecodeConstant()
    {
        using var pe = Synthesize(BodyKind.GetTypeCodeString);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(18, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_GetTypeCode_BooleanChain_FoldsToTypecodeConstant()
    {
        using var pe = Synthesize(BodyKind.GetTypeCodeBoolean);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(3, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_GetTypeCode_BaseTypeDefChain_FoldsToObjectFallback()
    {
        // typeof(Cases.Base).GetTypeCode() -> TypeCode.Object == 1 (the
        // runtime table's fallback -- net48: 1 for a class).
        using var pe = Synthesize(BodyKind.GetTypeCodeBase);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(1, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }

    //--------------------------------------------------------------------------
    // get_Name: the folded full name truncates to the SHORT name (net48
    // typeof(int).Name == "Int32"). "Cases.Base" has no namespace dot --
    // the whole name is the short name.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_GetName_Int32Chain_FoldsToShortName()
    {
        using var pe = Synthesize(BodyKind.GetNameInt32);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("Int32", Assert.IsType<StringConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_GetName_BaseTypeDefChain_KeepsWholeName()
    {
        using var pe = Synthesize(BodyKind.GetNameBase);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("Base", Assert.IsType<StringConstExpr>(ret.Value).Value);
    }

    //--------------------------------------------------------------------------
    // get_Name with a System.Type-parented MemberRef (Mono-style emission;
    // Roslyn emits System.Reflection.MemberInfo) folds the same way.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_GetName_TypeParentedMemberRef_FoldsToShortName()
    {
        using var pe = Synthesize(BodyKind.GetNameInt32TypeParented);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("Int32", Assert.IsType<StringConstExpr>(ret.Value).Value);
    }

    //--------------------------------------------------------------------------
    // get_Name on a NON-folded Type value (a runtime GetTypeFromHandle
    // result) is NOT compile-time truncated -- the runtime short-name icall
    // is emitted (the truncation happens at runtime; the constant-gated
    // fold above only handles the folded full-name string).
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_GetName_NonFoldedReceiver_EmitsShortNameIcall()
    {
        // ldc.i4.1; call GetTypeFromHandle; callvirt get_Name; ret
        using var pe = Synthesize(BodyKind.GetNameNonFolded);
        var md = pe.GetMetadataReader();
        var tokens = PeTokens(pe);

        var stmts = Fold(pe, md);

        // the GetTypeFromHandle icall assigns a temp; the short-name icall is
        // inlined into the ret by OptimizeTemps (single-use call) -- collect
        // calls from both shapes
        var allCalls = new List<CallExpr>();
        foreach (var s in stmts)
        {
            if (s is AssignStmt a && a.Value is CallExpr ac) allCalls.Add(ac);
            else if (s is RetStmt r && r.Value is CallExpr rc) allCalls.Add(rc);
        }
        var shortNameCall = Assert.Single(
            allCalls.Where(c => c.Resolved?.CppName == "il2cpp::runtime::il2cpp_type_get_short_name"));
        Assert.Equal($"call_{tokens.GetName}", shortNameCall.Function);
    }

    //--------------------------------------------------------------------------
    // GetTypeCode on a NON-folded Type value: no constant to fold -- the
    // typecode icall is emitted (the descriptor-ABI emission; the C++
    // mismatch is the Type domain's documented runtime-path limitation).
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_GetTypeCode_NonFoldedArg_EmitsIcall()
    {
        // ldc.i4.1; call GetTypeFromHandle; call Type::GetTypeCode(Type); ret
        using var pe = Synthesize(BodyKind.GetTypeCodeNonFolded);
        var md = pe.GetMetadataReader();
        var tokens = PeTokens(pe);

        var stmts = Fold(pe, md);

        // the GetTypeFromHandle icall assigns a temp; the typecode icall is
        // inlined into the ret by OptimizeTemps (single-use call) -- collect
        // calls from both shapes
        var allCalls = new List<CallExpr>();
        foreach (var s in stmts)
        {
            if (s is AssignStmt a && a.Value is CallExpr ac) allCalls.Add(ac);
            else if (s is RetStmt r && r.Value is CallExpr rc) allCalls.Add(rc);
        }
        var typecodeCall = Assert.Single(
            allCalls.Where(c => c.Resolved?.CppName == "il2cpp::runtime::il2cpp_type_get_typecode"));
        Assert.Equal($"call_{tokens.GetTypeCode1}", typecodeCall.Function);
    }

    //--------------------------------------------------------------------------
    // The 0-param INSTANCE GetTypeCode form (IConvertible impl) is not part
    // of the fold (the static 1-param form is); it stays LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_GetTypeCode_InstanceForm_ThrowsLoud()
    {
        using var pe = Synthesize(BodyKind.GetTypeCodeInstance);
        var md = pe.GetMetadataReader();

        var ex = Assert.Throws<NotSupportedException>(() => Fold(pe, md));
        Assert.Contains("GetTypeCode", ex.Message);
        Assert.Contains("instance", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Console.WriteLine(typeof(X)): the folded Type VALUE is a string --
    // the object-typed argument routes to write_line_s (net48 prints the
    // type's full name).
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_WriteLine_ObjectWithFoldedType_RoutesToStringIcall()
    {
        using var pe = Synthesize(BodyKind.WriteLineFoldedType);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.Equal("il2cpp::runtime::icall_console_write_line_s", stmt.Resolved!.CppName);
        Assert.Equal("System.Int32", Assert.IsType<StringConstExpr>(stmt.Args[0]).Value);
    }

    [Fact]
    public void Fold_WriteLine_ObjectWithLiteralString_RoutesToStringIcall()
    {
        // Console.WriteLine((object)"abc") -- a bare string literal through
        // the object-typed overload (identity conversion, no box): the
        // literal IS a string, same hint.
        using var pe = Synthesize(BodyKind.WriteLineLiteralObject);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.Equal("il2cpp::runtime::icall_console_write_line_s", stmt.Resolved!.CppName);
        Assert.Equal("abc", Assert.IsType<StringConstExpr>(stmt.Args[0]).Value);
    }

    //--------------------------------------------------------------------------
    // The full net48 chain: Console.WriteLine(typeof(int).Name) -- the
    // get_Name fold produces the short name, WriteLine(string) prints it.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_WriteLine_GetNameChain_EmitsStringWriteLine()
    {
        using var pe = Synthesize(BodyKind.WriteLineGetNameChain);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.Equal("il2cpp::runtime::icall_console_write_line_s", stmt.Resolved!.CppName);
        Assert.Equal("Int32", Assert.IsType<StringConstExpr>(stmt.Args[0]).Value);
    }

    //--------------------------------------------------------------------------
    // Control: WriteLine(object) with a non-string untracked argument (null)
    // folds to the runtime object printer icall_console_write_line_object
    // (M7 hashtable2/hashtablecase, 2026-08-09): null prints only the line
    // terminator (net48 TextWriter.WriteLine(object) semantics).
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_WriteLine_ObjectWithNull_MapsToObjectPrinter()
    {
        using var pe = Synthesize(BodyKind.WriteLineNull);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);
        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.Equal("il2cpp::runtime::icall_console_write_line_object", stmt.Resolved!.CppName);
        Assert.Equal(1, stmt.Args.Count);
    }

    //--------------------------------------------------------------------------
    // Test harness -- same synthetic-PE pipeline as StackFoldTests.Ldtoken.
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

    private enum BodyKind
    {
        GetTypeCodeInt32,       // ldtoken(Int32) + GTFH + call GetTypeCode(Type)
        GetTypeCodeString,      // ldtoken(String) + GTFH + call GetTypeCode(Type)
        GetTypeCodeBoolean,     // ldtoken(Boolean) + GTFH + call GetTypeCode(Type)
        GetTypeCodeBase,        // ldtoken(TypeDef Base) + GTFH + call GetTypeCode(Type)
        GetNameInt32,           // ldtoken(Int32) + GTFH + callvirt get_Name
        GetNameBase,            // ldtoken(TypeDef Base) + GTFH + callvirt get_Name
        GetNameInt32TypeParented, // like GetNameInt32 with a System.Type-parented get_Name
        GetNameNonFolded,       // ldc.i4.1 + GTFH + callvirt get_Name
        GetTypeCodeNonFolded,   // ldc.i4.1 + GTFH + call GetTypeCode(Type)
        GetTypeCodeInstance,    // ldtoken(Int32) + GTFH + call GetTypeCode()
        WriteLineFoldedType,    // ldtoken(Int32) + GTFH + call WriteLine(object)
        WriteLineLiteralObject, // ldstr "abc" + call WriteLine(object)
        WriteLineGetNameChain,  // ldtoken(Int32) + GTFH + callvirt get_Name + call WriteLine(string)
        WriteLineNull,          // ldnull + call WriteLine(object)
    }

    private sealed record TypeChainTokens
    {
        public required int BaseTypeDef { get; init; }
        public required int Int32TypeRef { get; init; }
        public required int GetTypeFromHandle { get; init; }
        public required int GetName { get; init; }
        public required int GetNameTypeParented { get; init; }
        public required int GetTypeCode1 { get; init; }
        public required int GetTypeCode0 { get; init; }
        public required int WriteLineObject { get; init; }
        public required int WriteLineString { get; init; }
    }

    private static PEReader Synthesize(BodyKind kind)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var typeRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Type"));
        var int32Ref = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Int32"));
        var stringRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("String"));
        var boolRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Boolean"));
        var rthRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("RuntimeTypeHandle"));
        var consoleRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Console"));

        // Type.GetTypeFromHandle(RuntimeTypeHandle) : Type -- static, 1 param
        var gtfhSig = new BlobBuilder();
        gtfhSig.WriteByte(0x00);
        gtfhSig.WriteCompressedInteger(1);
        gtfhSig.WriteByte(0x12);
        gtfhSig.WriteCompressedInteger(CodedIndex(typeRef));
        gtfhSig.WriteByte(0x11);
        gtfhSig.WriteCompressedInteger(CodedIndex(rthRef));
        var getTypeFromHandle = mdb.AddMemberReference(typeRef, mdb.GetOrAddString("GetTypeFromHandle"),
            mdb.GetOrAddBlob(gtfhSig));
        // Type::get_Name() : string -- instance, 0 params. Roslyn emits the
        // MemberRef parent as the DECLARING type System.Reflection.MemberInfo
        // (probe IL dump evidence: callvirt MemberInfo::get_Name) -- the
        // fold accepts both parents.
        var nameSig = new BlobBuilder();
        nameSig.WriteByte(0x00);
        nameSig.WriteCompressedInteger(0);
        nameSig.WriteByte(0x0E);
        var getName = mdb.AddMemberReference(typeRef, mdb.GetOrAddString("get_Name"), mdb.GetOrAddBlob(nameSig));
        var memberInfoRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System.Reflection"), mdb.GetOrAddString("MemberInfo"));
        var getNameMi = mdb.AddMemberReference(memberInfoRef, mdb.GetOrAddString("get_Name"), mdb.GetOrAddBlob(nameSig));
        // Type.GetTypeCode(Type) : int32 -- STATIC, 1 param (the fold target)
        var tc1Sig = new BlobBuilder();
        tc1Sig.WriteByte(0x00);
        tc1Sig.WriteCompressedInteger(1);
        tc1Sig.WriteByte(0x08);
        tc1Sig.WriteByte(0x12);
        tc1Sig.WriteCompressedInteger(CodedIndex(typeRef));
        var getTypeCode1 = mdb.AddMemberReference(typeRef, mdb.GetOrAddString("GetTypeCode"), mdb.GetOrAddBlob(tc1Sig));
        // Type.GetTypeCode() : int32 -- INSTANCE, 0 params (IConvertible impl)
        var tc0Sig = new BlobBuilder();
        tc0Sig.WriteByte(0x00);
        tc0Sig.WriteCompressedInteger(0);
        tc0Sig.WriteByte(0x08);
        var getTypeCode0 = mdb.AddMemberReference(typeRef, mdb.GetOrAddString("GetTypeCode"), mdb.GetOrAddBlob(tc0Sig));
        // Console.WriteLine(object) : void -- static, 1 param
        var wlOSig = new BlobBuilder();
        wlOSig.WriteByte(0x00);
        wlOSig.WriteCompressedInteger(1);
        wlOSig.WriteByte(0x01);
        wlOSig.WriteByte(0x1C);
        var writeLineObject = mdb.AddMemberReference(consoleRef, mdb.GetOrAddString("WriteLine"), mdb.GetOrAddBlob(wlOSig));
        // Console.WriteLine(string) : void -- static, 1 param
        var wlSSig = new BlobBuilder();
        wlSSig.WriteByte(0x00);
        wlSSig.WriteCompressedInteger(1);
        wlSSig.WriteByte(0x01);
        wlSSig.WriteByte(0x0E);
        var writeLineString = mdb.AddMemberReference(consoleRef, mdb.GetOrAddString("WriteLine"), mdb.GetOrAddBlob(wlSSig));

        // The TypeDef's method list is the type's method-range START (the
        // range runs to the next TypeDef's list / table end) -- a dummy
        // method created BEFORE the type starts the range; Main (created
        // last, below) falls inside it (single-type assembly, same shape as
        // the StackFoldTestsLdtoken harness).
        var dummy = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Dummy"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), 0, default(ParameterHandle));
        _ = dummy;
        var baseTypeDef = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Base"), objRef, default(FieldDefinitionHandle),
            dummy);

        var tokens = new TypeChainTokens
        {
            BaseTypeDef = MetadataTokens.GetToken(baseTypeDef),
            Int32TypeRef = MetadataTokens.GetToken(int32Ref),
            GetTypeFromHandle = MetadataTokens.GetToken(getTypeFromHandle),
            GetName = MetadataTokens.GetToken(getNameMi),
            GetNameTypeParented = MetadataTokens.GetToken(getName),
            GetTypeCode1 = MetadataTokens.GetToken(getTypeCode1),
            GetTypeCode0 = MetadataTokens.GetToken(getTypeCode0),
            WriteLineObject = MetadataTokens.GetToken(writeLineObject),
            WriteLineString = MetadataTokens.GetToken(writeLineString),
        };

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        switch (kind)
        {
            case BodyKind.GetTypeCodeInt32:
                LdToken(encoder, tokens.Int32TypeRef);
                Call(encoder, tokens.GetTypeFromHandle);
                Call(encoder, tokens.GetTypeCode1);
                break;
            case BodyKind.GetTypeCodeString:
                LdToken(encoder, MetadataTokens.GetToken(stringRef));
                Call(encoder, tokens.GetTypeFromHandle);
                Call(encoder, tokens.GetTypeCode1);
                break;
            case BodyKind.GetTypeCodeBoolean:
                LdToken(encoder, MetadataTokens.GetToken(boolRef));
                Call(encoder, tokens.GetTypeFromHandle);
                Call(encoder, tokens.GetTypeCode1);
                break;
            case BodyKind.GetTypeCodeBase:
                LdToken(encoder, tokens.BaseTypeDef);
                Call(encoder, tokens.GetTypeFromHandle);
                Call(encoder, tokens.GetTypeCode1);
                break;
            case BodyKind.GetNameInt32:
                LdToken(encoder, tokens.Int32TypeRef);
                Call(encoder, tokens.GetTypeFromHandle);
                Callvirt(encoder, tokens.GetName);
                break;
            case BodyKind.GetNameBase:
                LdToken(encoder, tokens.BaseTypeDef);
                Call(encoder, tokens.GetTypeFromHandle);
                Callvirt(encoder, tokens.GetName);
                break;
            case BodyKind.GetNameInt32TypeParented:
                LdToken(encoder, tokens.Int32TypeRef);
                Call(encoder, tokens.GetTypeFromHandle);
                Callvirt(encoder, tokens.GetNameTypeParented);
                break;
            case BodyKind.GetNameNonFolded:
                encoder.OpCode(ILOpCode.Ldc_i4_1);
                Call(encoder, tokens.GetTypeFromHandle);
                Callvirt(encoder, tokens.GetName);
                break;
            case BodyKind.GetTypeCodeNonFolded:
                encoder.OpCode(ILOpCode.Ldc_i4_1);
                Call(encoder, tokens.GetTypeFromHandle);
                Call(encoder, tokens.GetTypeCode1);
                break;
            case BodyKind.GetTypeCodeInstance:
                LdToken(encoder, tokens.Int32TypeRef);
                Call(encoder, tokens.GetTypeFromHandle);
                Call(encoder, tokens.GetTypeCode0);
                break;
            case BodyKind.WriteLineFoldedType:
                LdToken(encoder, tokens.Int32TypeRef);
                Call(encoder, tokens.GetTypeFromHandle);
                Call(encoder, tokens.WriteLineObject);
                break;
            case BodyKind.WriteLineLiteralObject:
                encoder.LoadString(mdb.GetOrAddUserString("abc"));
                Call(encoder, tokens.WriteLineObject);
                break;
            case BodyKind.WriteLineGetNameChain:
                LdToken(encoder, tokens.Int32TypeRef);
                Call(encoder, tokens.GetTypeFromHandle);
                Callvirt(encoder, tokens.GetName);
                Call(encoder, tokens.WriteLineString);
                break;
            case BodyKind.WriteLineNull:
                encoder.OpCode(ILOpCode.Ldnull);
                Call(encoder, tokens.WriteLineObject);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);

        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        _ = main;

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }

    // Re-read the synthesized tokens back from the PE (guards against
    // builder/reader drift). The two GetTypeCode / two WriteLine MemberRefs
    // share a NAME -- key by (name, arity, first-param byte).
    private static TypeChainTokens PeTokens(PEReader pe)
    {
        var md = pe.GetMetadataReader();
        var typeRefs = md.TypeReferences.ToDictionary(
            h => md.GetString(md.GetTypeReference(h).Name), h => MetadataTokens.GetToken(h));
        var members = new Dictionary<(string Parent, string Name, int Params, byte P0), int>();
        foreach (var h in md.MemberReferences)
        {
            var mr = md.GetMemberReference(h);
            var parent = mr.Parent.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                _ => "?",
            };
            var sig = md.GetBlobReader(mr.Signature);
            sig.ReadSignatureHeader();
            var pc = sig.ReadCompressedInteger();
            var ret = sig.ReadByte();
            if (ret is 0x11 or 0x12 or 0x40) _ = sig.ReadTypeHandle(); // CLASS/VALUETYPE rets consume a coded index
            var p0 = pc > 0 ? sig.ReadByte() : (byte)0;
            members[(parent, md.GetString(mr.Name), pc, p0)] = MetadataTokens.GetToken(h);
        }
        var types = md.TypeDefinitions.ToDictionary(
            h => md.GetString(md.GetTypeDefinition(h).Name), h => MetadataTokens.GetToken(h));
        return new TypeChainTokens
        {
            BaseTypeDef = types["Base"],
            Int32TypeRef = typeRefs["Int32"],
            GetTypeFromHandle = members[("System.Type", "GetTypeFromHandle", 1, 0x11)],
            GetName = members[("System.Reflection.MemberInfo", "get_Name", 0, 0)],
            GetNameTypeParented = members[("System.Type", "get_Name", 0, 0)],
            GetTypeCode1 = members[("System.Type", "GetTypeCode", 1, 0x12)],
            GetTypeCode0 = members[("System.Type", "GetTypeCode", 0, 0)],
            WriteLineObject = members[("System.Console", "WriteLine", 1, 0x1C)],
            WriteLineString = members[("System.Console", "WriteLine", 1, 0x0E)],
        };
    }

    private static void LdToken(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Ldtoken);
        e.Token(token);
    }

    private static void Call(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Call);
        e.Token(token);
    }

    private static void Callvirt(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Callvirt);
        e.Token(token);
    }

    private static int CodedIndex(EntityHandle h)
    {
        var row = MetadataTokens.GetToken(h) & 0xFFFFFF;
        var tag = h.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            _ => throw new ArgumentException($"handle kind {h.Kind}"),
        };
        return (row << 2) | tag;
    }
}
