using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

// C4716 (MULTI-D batch find, delegate2 msvc): a throw-only NON-void method
// (IL ends with throw -- no 'ret' falls through) emitted a C++ function whose
// end-of-body was reachable WITHOUT a return statement. il2cpp_throw_new is
// NOT declared [[noreturn]] (a plain void signature keeps the generated
// `catch` dispatch an ordinary C++ call), so MSVC's end-of-function flow
// check reported C4716 ("must return a value") and the msvc half of the
// delegate2 build failed. The emitter now appends the return type's zero
// value on the throw-tail fall-through.
public class C4716TrailingReturnTests
{
    [Fact]
    public void Generate_ThrowOnlyNonVoidMethod_EmitsTrailingReturnZero()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_c4716_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, Synthesize().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Cases_Program.cpp"));

            // throw-only int32 method: the body ends on il2cpp_throw_new and
            // must close with the trailing `return 0;` (MSVC C4716).
            var throwsOnly = MethodSegment(cpp, "System.Int32 Cases.Program::ThrowsOnly()");
            Assert.Contains("il2cpp_throw_new", throwsOnly);
            Assert.Contains("return 0;", throwsOnly);

            // throw-only reference-typed method: the same fix with nullptr.
            var throwsRef = MethodSegment(cpp, "Cases.Program::ThrowsRef()");
            Assert.Contains("il2cpp_throw_new", throwsRef);
            Assert.Contains("return nullptr;", throwsRef);

            // control: a normal non-void method (IL 'ret' falls through) must
            // NOT get a spurious trailing zero-return.
            var returnsVal = MethodSegment(cpp, "Cases.Program::ReturnsVal()");
            Assert.Contains("return 5LL;", returnsVal);
            Assert.DoesNotContain("return 0;", returnsVal);
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private static string MethodSegment(string cpp, string header)
    {
        foreach (var p in cpp.Split("// IL method:"))
            if (p.Contains(header, StringComparison.Ordinal))
                return p;
        throw new InvalidOperationException("method segment not found: " + header);
    }

    private static MemoryStream Synthesize()
    {
        var mdb = NewBuilder();
        var objRef = mdb.AddTypeReference(GetOrAddMscorlib(mdb), mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var stream = new MethodBodyStreamEncoder(new BlobBuilder());

        // static int32 ThrowsOnly() { throw null; } -- the C4716 shape.
        var to = new InstructionEncoder(new BlobBuilder());
        to.OpCode(ILOpCode.Ldnull);
        to.OpCode(ILOpCode.Throw);
        var toOff = stream.AddMethodBody(to, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var throwsOnly = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("ThrowsOnly"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x08 }), // static int32 ()
            toOff, default(ParameterHandle));

        // static string ThrowsRef() { throw null; } -- throw-only reference return.
        var tr = new InstructionEncoder(new BlobBuilder());
        tr.OpCode(ILOpCode.Ldnull);
        tr.OpCode(ILOpCode.Throw);
        var trOff = stream.AddMethodBody(tr, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var throwsRef = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("ThrowsRef"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x0E }), // static string ()
            trOff, default(ParameterHandle));

        // static int32 ReturnsVal() { return 5; } -- control: normal non-void.
        var rv = new InstructionEncoder(new BlobBuilder());
        rv.OpCode(ILOpCode.Ldc_i4_5);
        rv.OpCode(ILOpCode.Ret);
        var rvOff = stream.AddMethodBody(rv, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var returnsVal = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("ReturnsVal"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x08 }), // static int32 ()
            rvOff, default(ParameterHandle));

        // static void Main() { } -- keeps entry.cpp generation honest.
        var me = new InstructionEncoder(new BlobBuilder());
        me.OpCode(ILOpCode.Ret);
        var meOff = stream.AddMethodBody(me, 0, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), // static void ()
            meOff, default(ParameterHandle));

        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Program"), objRef, default, throwsOnly);
        return Serialize(mdb, stream.Builder);
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

    private static MemoryStream Serialize(MetadataBuilder mdb, BlobBuilder ilStream)
    {
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, ilStream);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
