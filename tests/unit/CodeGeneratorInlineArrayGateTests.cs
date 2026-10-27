using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// P3-2: the CodeGenerator "g:" local-declaration INLINE-ARRAY gate. The
// compiler-synthesized buffer local is the <PrivateImplementationDetails>
// NESTED type (<PrivateImplementationDetails>+__InlineArrayN`1 -- the C# 13
// span-params lowering's backing); the gate must require that COMBINATION.
// A USER generic struct whose name merely CONTAINS "__InlineArray"
// ("Cases.__InlineArray4`1") is a real translated type: its instantiation
// local must declare as the concrete R77 struct clone (the value path), not
// the dead ref carrier the old Contains("__InlineArray") gate produced (the
// misdeclaration turned the struct's int32 slots into object writes).
//------------------------------------------------------------------------------
public class CodeGeneratorInlineArrayGateTests
{
    [Fact]
    public void Generate_UserGenericNamedInlineArrayLocal_DeclaresConcreteStruct()
    {
        var exe = Synthesize();
        var types = MetadataLoader.LoadAssembly(exe);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = Directory.GetFiles(Path.Combine(dir, "src"), "*.cpp", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
                .Aggregate((a, b) => a + b);

            // The user struct instantiation local declares as the concrete
            // clone ({Sanitize(def)}_{suffix} -- Sanitize collapses '.'/'`').
            Assert.Contains("Cases___InlineArray4_1_int loc0;", cpp);
            // The dead ref carrier was the misdeclaration under the old gate.
            Assert.DoesNotContain("Il2CppObject* loc0", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // Synthetic PE (same harness as MethodBodyReaderTests.GenericInstanceLocal,
    // driven through the FULL MetadataLoader + CodeGenerator pipeline):
    // TypeDef row 1 = Cases.__InlineArray4`1 (generic VALUE type, one T),
    // TypeDef row 2 = Cases.Holder with static Run() holding ONE local of
    // TypeSpec __InlineArray4`1<int32> [15 11 04 01 08 referenced as 0x12 +
    // TypeSpec coded index (row 1, tag TypeSpec=2 -> 0x06)], TypeDef row 3 =
    // Program with the entry Main.
    private static string Synthesize()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var valueTypeRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("ValueType"));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        var inlineArrDef = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("__InlineArray4`1"), valueTypeRef,
            default(FieldDefinitionHandle), default(MethodDefinitionHandle));
        mdb.AddGenericParameter(inlineArrDef, GenericParameterAttributes.None, mdb.GetOrAddString("T"), 0);

        var tsBlob = new BlobBuilder();
        tsBlob.WriteByte(0x15);
        tsBlob.WriteByte(0x11);
        tsBlob.WriteCompressedInteger(0x04);
        tsBlob.WriteCompressedInteger(1);
        tsBlob.WriteByte(0x08);
        var spec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(tsBlob));

        var localsBlob = new BlobBuilder();
        localsBlob.WriteByte(0x07);
        localsBlob.WriteCompressedInteger(1);
        localsBlob.WriteByte(0x12);
        localsBlob.WriteCompressedInteger(0x06);
        var localsSig = mdb.AddStandaloneSignature(mdb.GetOrAddBlob(localsBlob));

        // Holder::Eat(int32) -- a user static the body calls with the local as
        // its argument (a written-but-never-read local is dropped by the
        // dead-store pass and never reaches the declaration loop).
        var eatMemberRef = mdb.AddMemberReference(MetadataTokens.TypeDefinitionHandle(2), mdb.GetOrAddString("Eat"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x01, 0x08 }));

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        // The body must keep a LIVE reference to the local: a constant store
        // is folded away by the const-prop pass and a written-never-read
        // local by the dead-store pass -- so the local copies a PARAMETER
        // (non-constant) and feeds the user call (the same referenced-slot
        // declaration path the real buffer local's initobj/element stores
        // exercise).
        encoder.LoadArgument(0);
        encoder.StoreLocal(0);
        encoder.LoadLocal(0);
        encoder.Call(eatMemberRef);
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var runOffset = stream.AddMethodBody(encoder, 0, localsSig, MethodBodyAttributes.None);

        // Eat's instructions ride the SAME builder the PEBuilder serializes.
        var encEat = new InstructionEncoder(il);
        encEat.OpCode(ILOpCode.Ret);
        il.Align(4);
        var eatOffset = stream.AddMethodBody(encEat, 0, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var run = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Run"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x01, 0x08 }), runOffset, default(ParameterHandle));
        var eat = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Eat"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x01, 0x08 }), eatOffset, default(ParameterHandle));
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Holder"), objRef, default, run);

        var enc2 = new InstructionEncoder(il);
        enc2.OpCode(ILOpCode.Ret);
        il.Align(4);
        var mainOffset = stream.AddMethodBody(enc2, 0, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), mainOffset, default(ParameterHandle));
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString(""), mdb.GetOrAddString("Program"), objRef, default, main);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        var path = Path.Combine(Path.GetTempPath(), "inlinearr_gate_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(path, blob.ToArray());
        return path;
    }
}
