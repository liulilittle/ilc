using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

// Guid value-slot wiring: System.Guid's icalls WRITE through their output
// buffer (void il2cpp_guid_new_guid(uint8_t bytes[16]) /
// il2cpp_guid_parse_strict(Il2CppString*, uint8_t bytes[16]) -- guid.h has
// no struct wrapper for the icall ABI, the buffer IS the raw 16 bytes). The
// emitter must:
//   * declare Guid locals as the 16-byte il2cpp::Il2CppGuid VALUE struct
//     (the same type the method signature / fields name)
//   * emit `t = NewGuid()` as the statement form
//     `il2cpp_guid_new_guid(reinterpret_cast<uint8_t*>(&t));` (the icall takes
//     the slot ADDRESS, it cannot appear as an assignment RHS)
//   * emit Guid value copies as plain struct assignments (Il2CppGuid is POD)
//   * include guid.h in generated.h
// A "guid"-kind call reaching an EXPRESSION position (the fold inlined the
// single-use temp into e.g. `return Guid.Parse(s)`) must reject LOUDLY.
public class GuidSlotWiringTests
{
    [Fact]
    public void Generate_NewGuidAndParse_WriteThroughValueSlots()
    {
        // Cases.GenGuid.Main:
        //   Guid g1 = Guid.NewGuid();  (call NewGuid; stloc.0)
        //   Guid g2 = Guid.Parse("x"); (ldstr "x"; call Parse; stloc.1)
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_guid_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, Synthesize(new byte[] { 0x00, 0x00, 0x01 }).ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var generated = File.ReadAllText(Path.Combine(dir, "src", "Cases_GenGuid.cpp"));

            // Guid locals are declared as the 16-byte Il2CppGuid value struct
            // (the same type the method signature and field slots name).
            Assert.Contains("il2cpp::Il2CppGuid loc0;", generated);
            Assert.Contains("il2cpp::Il2CppGuid loc1;", generated);
            // NewGuid/Parse emit as STATEMENT calls with the slot ADDRESS as the
            // output buffer (never `t = fn(...)` -- the icalls return void and
            // take raw uint8_t*).
            Assert.Contains("il2cpp::il2cpp_guid_new_guid(reinterpret_cast<uint8_t*>(&loc0));", generated);
            // The "x" user string rides the StringPool T3 static table
            // (il2cpp::g_strPool[0] -- the single-load pooled form, not the
            // pre-pooling per-use il2cpp_string_intern_literal call).
            Assert.Contains("il2cpp::il2cpp_guid_parse_strict(reinterpret_cast<Il2CppString*>(il2cpp::g_strPool[0]), reinterpret_cast<uint8_t*>(&loc1));", generated);
            // The slot declarations must NOT be int32 (the pre-wiring shape
            // silently truncated the 16-byte value).
            Assert.DoesNotContain("int32_t loc0", generated);
            Assert.DoesNotContain("int32_t loc1", generated);

            // generated.h must include guid.h (the runtime support header).
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("#include \"guid.h\"", header);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_ExpressionPositionGuidCall_IsHandledViaSlot()
    {
        // Cases.GenGuidRet.M: `return Guid.NewGuid();` -- the fold's
        // single-use temp inlining used to sink the NewGuid call into the
        // return statement, where the void pointer-writing icall could not
        // compile (the emitter rejected LOUDLY at the expression position).
        // DEC-GAP (batch 106): the OptimizeTemps rule-3 inlining SKIPS
        // value-slot icalls (dec/guid/ip4 ResultKinds), so the call stays in
        // its assignment statement and the method returns the resulting
        // Il2CppGuid value slot. A by-value Guid return IS supported: the
        // signature names il2cpp::Il2CppGuid (TypeCppName) and the body
        // returns the value struct.
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_guid_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, Synthesize(new byte[] { 0x00, 0x00, 0x11, 0x05 }, returnCall: true).ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cppPath = Path.Combine(dir, "src", "Cases_GenGuidRet.cpp");
            Assert.True(File.Exists(cppPath), "the Guid-returning method must be emitted (no stub skip)");
            var cpp = File.ReadAllText(cppPath);
            // The method is transpiled, not a NotSupportedException stub.
            Assert.DoesNotContain("not transpiled", cpp);
            Assert.Contains("il2cpp::Il2CppGuid Cases_GenGuidRet_M_", cpp);
            // NewGuid writes through the slot ADDRESS (raw uint8_t* ABI).
            Assert.Contains("il2cpp::il2cpp_guid_new_guid(reinterpret_cast<uint8_t*>(&", cpp);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // TypeRef row 1 = System.Guid (coded index 0x05 in the signature blobs);
    // MemberRef row 1 = Guid::NewGuid (sig 00 00 11 05 -- static, 0 params,
    // ret valuetype coded 0x05), row 2 = Guid::Parse(string) (sig
    // 00 01 11 05 0E); the TypeDef Cases.GenGuid carries the Main body.
    private static MemoryStream Synthesize(byte[] mainSig, bool returnCall = false)
    {
        var mdb = NewBuilder();
        var guidRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Guid"));
        var newGuid = mdb.AddMemberReference(guidRef, mdb.GetOrAddString("NewGuid"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x11, 0x05 }));
        var parse = mdb.AddMemberReference(guidRef, mdb.GetOrAddString("Parse"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x11, 0x05, 0x0E }));
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        if (returnCall)
        {
            encoder.OpCode(ILOpCode.Call);
            encoder.Token(MetadataTokens.GetToken(newGuid));
            encoder.OpCode(ILOpCode.Ret);
        }
        else
        {
            encoder.OpCode(ILOpCode.Call);
            encoder.Token(MetadataTokens.GetToken(newGuid));
            encoder.OpCode(ILOpCode.Stloc_0);
            encoder.LoadString(mdb.GetOrAddUserString("x"));
            encoder.OpCode(ILOpCode.Call);
            encoder.Token(MetadataTokens.GetToken(parse));
            encoder.OpCode(ILOpCode.Stloc_1);
            encoder.OpCode(ILOpCode.Ret);
        }
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, returnCall ? 1 : 2, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString(returnCall ? "M" : "Main"),
            mdb.GetOrAddBlob(mainSig),
            bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString(returnCall ? "GenGuidRet" : "GenGuid"), objRef, default, main);
        return Serialize(mdb, il);
    }

    private static MetadataBuilder NewBuilder()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        return mdb;
    }

    private static AssemblyReferenceHandle GetOrAddMscorlib(MetadataBuilder mdb) =>
        mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));

    private static MemoryStream Serialize(MetadataBuilder mdb, BlobBuilder? ilStream = null)
    {
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, ilStream ?? new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
