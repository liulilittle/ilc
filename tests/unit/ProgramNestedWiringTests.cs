using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Xunit;

//------------------------------------------------------------------------------
// ProgramNestedWiringTests -- the Program.RunMain pipeline wiring for
// TypeEmitter.ExpandNestedTypes: MetadataLoader skips IsNested TypeDefs while
// every reference point (newobj / field-access cast / method-name hash)
// resolves nested types by their SHORT metadata name ("Node" for
// "Cases.Recurse+Node"). Without the ExpandNestedTypes call between
// LoadAssembly and Generate, the generated C++ references Node_class but
// never defines it (the recurse e2e gap). These tests run the REAL CLI entry
// so the WIRING ITSELF is under test -- unlike TypeEmitterNestedTests, which
// call ExpandNestedTypes directly and cannot catch a missing Program.cs call.
//------------------------------------------------------------------------------
public class ProgramNestedWiringTests
{
    [Fact]
    public void RunMain_NestedType_EmitsNodeClassDefinition()
    {
        var tmpDll = Path.GetTempFileName();
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_nested_" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(tmpDll, SynthesizeNestedWithBodies().ToArray());

            var code = Program.RunMain(new[] { tmpDll, dir });
            Assert.Equal(0, code);

            // The nested type must emit its own translation unit with the
            // descriptor definition (previously: referenced, never defined).
            var nodeCpp = File.ReadAllText(Path.Combine(dir, "src", "Node.cpp"));
            Assert.Contains("Il2CppClass Node_class", nodeCpp);

            // Struct layout + descriptor declaration must reach generated.h
            // (cross-TU visibility for field-access casts and entry.cpp).
            var generatedH = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("struct Node", generatedH);
            Assert.Contains("int32_t Id;", generatedH);
            Assert.Contains("extern Il2CppClass Node_class;", generatedH);

            // entry.cpp registers the descriptor so the runtime/GC see the type.
            var entry = File.ReadAllText(Path.Combine(dir, "src", "entry.cpp"));
            Assert.Contains("il2cpp_class_register(&Node_class);", entry);
        }
        finally
        {
            Directory.Delete(dir, true);
            File.Delete(tmpDll);
        }
    }

    // Synthesize a PE with a nested type that has a REAL method body:
    //   Cases.Recurse (top-level static class, static void Main() -- EmitMain
    //                  writes entry.cpp only when a Main type exists)
    //     + Node       (nested class, int field Id, .ctor())
    // Unlike TypeEmitterNestedTests' synthesis, MethodBodyReader throws on
    // RVA=0 bodies, so every method gets a 1-byte `ret` body. The method body
    // stream is written into the ilStream BlobBuilder (ManagedPEBuilder has
    // no separate body-stream parameter -- dotnet/runtime ManagedPEBuilder:
    // the ilStream IS the method body stream, laid out at the start of .text).
    private static MemoryStream SynthesizeNestedWithBodies()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("nestedwiring.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var ctorSig = mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }); // instance, 0 params, void
        var mainSig = mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }); // static, 0 params, void

        // Method bodies: `ret`. AddMethodBody returns the offset in the
        // ilStream builder; AddMethodDefinition stores it as the method's RVA.
        var ilStream = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(ilStream);
        var code = new BlobBuilder();
        var instr = new InstructionEncoder(code);
        instr.OpCode(ILOpCode.Ret);
        var bodyOffset = bodyEncoder.AddMethodBody(instr, maxStack: 8);

        // Field/method list threading (ECMA-335 II.22.37): every type's
        // fieldList/methodList marks the FIRST row it owns; a nil mid-assembly
        // list would break the ranges (same contract as TypeEmitterNestedTests).
        // Field rows: Id=1. Method rows: Recurse.Main=1, Node..ctor=2.
        var nodeField = mdb.AddFieldDefinition(FieldAttributes.Public,
            mdb.GetOrAddString("Id"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 })); // int32
        var mainMethod = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"), mainSig, bodyOffset, default(ParameterHandle));
        var nodeCtor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"), ctorSig, bodyOffset, default(ParameterHandle));

        var recurse = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Recurse"), objRef, nodeField, mainMethod);
        var node = mdb.AddTypeDefinition(TypeAttributes.NestedPublic,
            default(StringHandle), mdb.GetOrAddString("Node"), objRef, nodeField, nodeCtor);
        mdb.AddNestedType(node, recurse);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, ilStream, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
