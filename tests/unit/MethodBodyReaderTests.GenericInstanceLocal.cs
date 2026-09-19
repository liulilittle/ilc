using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// R77 clone discovery for primitive instantiation args (A3/A4
// genericiface/genericvirtdeep): a `Box<string>` local must decode to the
// "g:Box`1:string" local kind so EnsureClone synthesizes Box_1_string.
// Previously only 0x11/0x12 (user classes/structs) produced kinds, so
// `new Box<string>()` never cloned and its calls folded to the int-typed
// symbols (g++ "Box_1_string_class was not declared" + int32/Object
// conversion errors).
//------------------------------------------------------------------------------
public class MethodBodyReaderTestsGenericInstanceLocal
{
    [Fact]
    public void Read_BoxStringLocal_DecodesGenericInstanceKind()
    {
        using var pe = Synthesize();
        var md = pe.GetMetadataReader();
        var typeDef = new TypeDef { Name = "Holder", Namespace = "Cases" };
        var methodDef = new MethodDef
        {
            Name = "Run",
            Owner = typeDef,
            ParamCount = 0,
        };

        var body = MethodBodyReader.Read(pe, md, typeDef, methodDef);

        Assert.Single(body.LocalKinds);
        Assert.Equal("g:Cases.Box`1:string", body.LocalKinds[0]);
    }

    // Synthetic PE: TypeDef Box`1 (row 1) + Holder/Run with one local of
    // TypeSpec Box<string> [15 12 04 01 0E] referenced as 0x12 + TypeSpec
    // coded index (row 1, tag TypeSpec=2 -> 0x06).
    private static PEReader Synthesize()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        // Generic definition Box`1 in this assembly (TypeDef row 1 ->
        // TypeDefOrRef coded 0x04).
        var boxDef = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Box`1"), objRef,
            default(FieldDefinitionHandle), default(MethodDefinitionHandle));

        // TypeSpec Box<string> (TypeSpec row 1 -> OrSpec coded 0x06).
        var tsBlob = new BlobBuilder();
        tsBlob.WriteByte(0x15);
        tsBlob.WriteByte(0x12);
        tsBlob.WriteCompressedInteger(0x04);
        tsBlob.WriteCompressedInteger(1);
        tsBlob.WriteByte(0x0E);
        var spec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(tsBlob));

        // Local signature: 0x07, count 1, 0x12 CLASS + TypeSpec coded 0x06.
        var localsBlob = new BlobBuilder();
        localsBlob.WriteByte(0x07);
        localsBlob.WriteCompressedInteger(1);
        localsBlob.WriteByte(0x12);
        localsBlob.WriteCompressedInteger(0x06);
        var localsSig = mdb.AddStandaloneSignature(mdb.GetOrAddBlob(localsBlob));

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 0, localsSig, MethodBodyAttributes.None);
        var run = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Run"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Holder"), objRef, default, run);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }
}
