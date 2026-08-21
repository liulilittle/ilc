using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

// G2 (docs/audit/recurse-recheck.md): callvirt System.Object::ToString on an
// UNTRACKED receiver -- a plain `object no = null;` local, which the fold's
// objTypes tracking does NOT record (objTypes covers newobj/box/GetType
// results only). Without a receiver hint TryMapObject returns null for
// Object::ToString, the call falls through to the generated-name path and
// the vtable emitter throws "callvirt owner System.Object not in translated
// types" (f14b blocker in the exdeep case). The fold must fall back to the
// Object::ToString mapping (icall_object_to_string -- klass/box dispatch,
// NRE on null) instead of the vtable path when the receiver type is unknown.
//
// PEs are synthesized with System.Reflection.Metadata's writer (same
// pipeline the transpiler reads), mirroring the DelegateSupportTests shape:
// Cases.Program.Main with the exact `object no = null; no.ToString();`
// IL (ldnull; stloc.0; ldloc.0; callvirt Object::ToString; stloc.1; ret).
public class StackFoldTestsObjectToString
{
    [Fact]
    public void Generate_UntrackedObjectLocalToString_MapsToObjectToStringIcall()
    {
        var cpp = GenerateMain(SynthesizeToStringMain("ToString", Sig(0x0E)));

        // the untracked `object` local's ToString resolves to the runtime
        // direct icall -- NOT the generated-name path, NOT vtable dispatch
        Assert.Contains("icall_object_to_string", cpp);
        Assert.DoesNotContain("System_Object_ToString_", cpp);
        Assert.DoesNotContain("->klass->vtable", cpp);
    }

    [Fact]
    public void Generate_UntrackedObjectLocalGetHashCode_KeepsExistingMapping()
    {
        // Negative guard: the fallback is ToString-specific -- an untracked
        // receiver on another Object virtual (GetHashCode) keeps its existing
        // receiverType-free mapping (icall_object_get_hash_code) and must NOT
        // route through the vtable path either.
        var cpp = GenerateMain(SynthesizeToStringMain("GetHashCode", Sig(0x08)));

        Assert.Contains("icall_object_get_hash_code", cpp);
        Assert.DoesNotContain("System_Object_GetHashCode_", cpp);
        Assert.DoesNotContain("->klass->vtable", cpp);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the transpiler)
    //--------------------------------------------------------------------------
    private static string GenerateMain(MemoryStream pe)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_objts_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, pe.ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            return File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private static byte[] Sig(byte ret)
    {
        // RawSignature shape: header 0x00, param count 0, return byte.
        return new[] { (byte)0x00, (byte)0x00, ret };
    }

    // Cases.Program.Main: ldnull; stloc.0; ldloc.0; callvirt <Object member>;
    // stloc.1; ret -- the exact `object no = null; no.<member>();` shape.
    private static MemoryStream SynthesizeToStringMain(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var member = mdb.AddMemberReference(objRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));

        var stream = new MethodBodyStreamEncoder(new BlobBuilder());
        var mainEnc = new InstructionEncoder(new BlobBuilder());
        mainEnc.OpCode(ILOpCode.Ldnull);
        mainEnc.OpCode(ILOpCode.Stloc_0);
        mainEnc.OpCode(ILOpCode.Ldloc_0);
        mainEnc.OpCode(ILOpCode.Callvirt);
        mainEnc.Token(MetadataTokens.GetToken(member));
        mainEnc.OpCode(ILOpCode.Stloc_1);
        mainEnc.OpCode(ILOpCode.Ret);
        var bodyOffset = stream.AddMethodBody(mainEnc, 2, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef, default, main);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, stream.Builder);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
