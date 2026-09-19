using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

// A2 (dtdefect001): the DateTimeOffset value-type ctors take the result
// slot as an Il2CppDateTimeOffset* OUT parameter
// (icall_dtoffset_ctor / icall_dtoffset_ctor_ymd) -- the vctor emission
// must address the slot ((&tN)), the decimal/biginteger out-pointer
// precedent. Passing the value produced
// "cannot convert 'Il2CppDateTimeOffset' to 'Il2CppDateTimeOffset*'".
public class EmitVctorAddressTests
{
    [Fact]
    public void Generate_DtoffsetCtor_AddressesResultSlot()
    {
        // Make: ldloc.0(DateTime) ldloc.1(TimeSpan)
        // newobj DateTimeOffset::.ctor(DateTime,TimeSpan); pop; ret.
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_vctor_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, Synthesize().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var generated = File.ReadAllText(Path.Combine(dir, "src", "Cases_Maker.cpp"));
            Assert.Contains("icall_dtoffset_ctor((&", generated);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // TypeRefs: DateTime (row 1 -> coded 0x05), TimeSpan (row 2 -> 0x09),
    // DateTimeOffset (row 3 -> 0x0D). Locals [DateTime, TimeSpan].
    private static MemoryStream Synthesize()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var dtRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTime"));
        var tsRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("TimeSpan"));
        var dtoRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTimeOffset"));

        // .ctor(DateTime, TimeSpan): instance, 2 params (0x11 VALUETYPE +
        // coded index each), void return.
        var ctorSig = new BlobBuilder();
        ctorSig.WriteByte(0x20);
        ctorSig.WriteCompressedInteger(2);
        ctorSig.WriteByte(0x01);
        ctorSig.WriteByte(0x11);
        ctorSig.WriteCompressedInteger(0x05);
        ctorSig.WriteByte(0x11);
        ctorSig.WriteCompressedInteger(0x09);
        var ctor = mdb.AddMemberReference(dtoRef, mdb.GetOrAddString(".ctor"), mdb.GetOrAddBlob(ctorSig));

        // Local signature: 0x07, count 2, DateTime, TimeSpan.
        var localsBlob = new BlobBuilder();
        localsBlob.WriteByte(0x07);
        localsBlob.WriteCompressedInteger(2);
        localsBlob.WriteByte(0x11);
        localsBlob.WriteCompressedInteger(0x05);
        localsBlob.WriteByte(0x11);
        localsBlob.WriteCompressedInteger(0x09);
        var localsSig = mdb.AddStandaloneSignature(mdb.GetOrAddBlob(localsBlob));

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        encoder.OpCode(ILOpCode.Ldloc_0);
        encoder.OpCode(ILOpCode.Ldloc_1);
        encoder.OpCode(ILOpCode.Newobj);
        encoder.Token(MetadataTokens.GetToken(ctor));
        encoder.OpCode(ILOpCode.Pop);
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 3, localsSig, MethodBodyAttributes.None);
        var make = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Make"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Maker"), objRef, default, make);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
