using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

// SYMLN-1: Portable PDB -> #line emission. Three surfaces are covered:
//   1. the PDB loader against the REAL Roslyn-produced fixture PDB (calc.pdb,
//      built with <DebugType>portable</DebugType>),
//   2. the fallback contract (an assembly with NO .pdb -> no #line), and
//   3. a FULLY SYNTHETIC assembly + portable PDB (MetadataBuilder +
//      PortablePdbBuilder) with controlled line numbers, proving the exact
//      "#line N "file.cs"" text the generator emits.
public class SymLnTests
{
    [Fact]
    public void LoadPortablePdb_CalcFixture_MapsIlOffsetsToLines()
    {
        var map = MetadataLoader.LoadPortablePdb(FixturePath());
        Assert.NotNull(map);
        Assert.Equal(3, map!.MethodCount); // Main, Add, .cctor (3 sequence-point methods in calc.pdb)

        var addToken = FindMethodToken("Calc", "Program", "Add");
        var add = map.Lookup(addToken, 0); // IL offset 0 = the Add body's first instruction
        Assert.True(add.HasValue);
        Assert.Equal(19, add.Value.Line); // calc.cs:19  `return a + b;`
        Assert.EndsWith("calc.cs", add.Value.DocumentPath);

        var mainToken = FindMethodToken("Calc", "Program", "Main");
        var main = map.Lookup(mainToken, 0);
        Assert.True(main.HasValue);
        Assert.Equal(11, main.Value.Line); // calc.cs:11  `int x = Add(3, 4);`
    }

    [Fact]
    public void LoadPortablePdb_CalcFixture_LookupSelectsLargestOffsetAtOrBefore()
    {
        // The Add method has ONE sequence point (offset 0 -> line 19): every
        // statement offset inside the method body resolves to it.
        var map = MetadataLoader.LoadPortablePdb(FixturePath())!;
        var addToken = FindMethodToken("Calc", "Program", "Add");
        Assert.Equal(19, map.Lookup(addToken, 0)?.Line);
        Assert.Equal(19, map.Lookup(addToken, 3)?.Line); // ret@3 inside Add's IL range
        Assert.Equal(19, map.Lookup(addToken, 999)?.Line); // past the body: last entry applies
        Assert.Null(map.Lookup(addToken, -1)); // negative offset: no entry
    }

    [Fact]
    public void Generate_WithPortablePdb_EmitsLineDirectives()
    {
        var types = MetadataLoader.LoadAssembly(FixturePath());
        var dir = TempDir();
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath(), MetadataLoader.LoadPortablePdb(FixturePath()));
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "Calc_Program.cpp"));

            // Main body: #line 11 (int x = Add(3,4)) then #line 12 (WriteLine).
            Assert.Contains("#line 11 ", cpp);
            Assert.Contains("#line 12 ", cpp);
            // Add body: #line 19 (return a + b;).
            Assert.Contains("#line 19 ", cpp);
            // The directive carries the normalized source document path.
            Assert.Contains("calc.cs\"", cpp);
            // The directive precedes its statement.
            Assert.True(cpp.IndexOf("#line 19 ", StringComparison.Ordinal)
                < cpp.IndexOf("return (arg0 + arg1);", StringComparison.Ordinal),
                "#line 19 must precede the Add return statement");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_WithoutPdb_EmitsNoLineDirectives()
    {
        // Copy the fixture to a temp dir WITHOUT its .pdb: the loader must
        // return null and the generator must emit no #line (fallback contract
        // for hand-written .il input).
        var tmp = TempDir();
        var gen = TempDir();
        try
        {
            var asmNoPdb = Path.Combine(tmp, "calc.dll");
            File.Copy(FixturePath(), asmNoPdb);
            Assert.Null(MetadataLoader.LoadPortablePdb(asmNoPdb));

            var types = MetadataLoader.LoadAssembly(asmNoPdb);
            CodeGenerator.Generate(types, gen, asmNoPdb, MetadataLoader.LoadPortablePdb(asmNoPdb));
            var cpp = File.ReadAllText(Path.Combine(gen, "src", "Calc_Program.cpp"));
            Assert.DoesNotContain("#line", cpp);
        }
        finally
        {
            Directory.Delete(tmp, true);
            Directory.Delete(gen, true);
        }
    }

    [Fact]
    public void Generate_SyntheticAssemblyWithoutPdb_EmitsNoLineDirectives()
    {
        // The loader must survive a synthetic assembly that has NO debug
        // directory at all (returns null, never throws).
        var asm = SynthesizeNoPdbAssembly();
        try
        {
            Assert.Null(MetadataLoader.LoadPortablePdb(asm.AssemblyPath));
            var types = MetadataLoader.LoadAssembly(asm.AssemblyPath);
            var gen = TempDir();
            try
            {
                CodeGenerator.Generate(types, gen, asm.AssemblyPath, null);
                var cpp = File.ReadAllText(Path.Combine(gen, "src", "Sym_Case.cpp"));
                Assert.DoesNotContain("#line", cpp);
            }
            finally
            {
                Directory.Delete(gen, true);
            }
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(asm.AssemblyPath)!, true);
        }
    }

    [Fact]
    public void LoadPortablePdb_SyntheticPdb_MapsControlledOffsets()
    {
        var synth = SynthesizeWithPdb();
        try
        {
            var map = MetadataLoader.LoadPortablePdb(synth.AssemblyPath);
            Assert.NotNull(map);
            Assert.Equal(1, map!.MethodCount);
            Assert.True(map.Contains(synth.MainToken));

            Assert.Equal(40, map.Lookup(synth.MainToken, 0)?.Line);
            Assert.Equal(41, map.Lookup(synth.MainToken, 1)?.Line); // stloc.0@1
            Assert.Equal(43, map.Lookup(synth.MainToken, 3)?.Line); // stloc.1@3
            Assert.Equal(47, map.Lookup(synth.MainToken, 7)?.Line); // ret@7
            Assert.Equal(47, map.Lookup(synth.MainToken, 100)?.Line); // beyond: last entry
            Assert.Equal("Case.cs", map.Lookup(synth.MainToken, 7)?.DocumentPath);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(synth.AssemblyPath)!, true);
        }
    }

    [Fact]
    public void Generate_SyntheticPdb_EmitsExactLineDirectives()
    {
        var synth = SynthesizeWithPdb();
        try
        {
            var map = MetadataLoader.LoadPortablePdb(synth.AssemblyPath);
            var types = MetadataLoader.LoadAssembly(synth.AssemblyPath);
            var gen = TempDir();
            try
            {
                CodeGenerator.Generate(types, gen, synth.AssemblyPath, map);
                var cpp = File.ReadAllText(Path.Combine(gen, "src", "Sym_Case.cpp"));

                // The 2-arg body cannot be constant-folded -> three statements,
                // each mapped to its own controlled C# line.
                Assert.Contains("#line 41 \"Case.cs\"", cpp); // loc0 = arg0 (stloc.0@1)
                Assert.Contains("#line 43 \"Case.cs\"", cpp); // loc1 = arg1 (stloc.1@3)
                Assert.Contains("#line 47 \"Case.cs\"", cpp); // return (loc0 + loc1) (ret@7)
            }
            finally
            {
                Directory.Delete(gen, true);
            }
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(synth.AssemblyPath)!, true);
        }
    }

    //--------------------------------------------------------------------------
    // Helpers
    //--------------------------------------------------------------------------

    private static int FindMethodToken(string ns, string typeName, string methodName)
    {
        using var pe = new PEReader(File.OpenRead(FixturePath()));
        var md = pe.GetMetadataReader();
        foreach (var tdh in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdh);
            if (md.GetString(td.Namespace) != ns || md.GetString(td.Name) != typeName) continue;
            foreach (var mh in td.GetMethods())
            {
                if (md.GetString(md.GetMethodDefinition(mh).Name) == methodName)
                    return MetadataTokens.GetToken(mh);
            }
        }
        throw new InvalidOperationException($"method {ns}.{typeName}.{methodName} not found in fixture");
    }

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "out", "calc.dll");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "fixture calc.dll not found; run scripts/build-fixtures.ps1 first (expects repo root/tests/fixtures/out/calc.dll)");
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "symln_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // A synthetic assembly whose method body is
    //   ldarg.0; stloc.0; ldarg.1; stloc.1; ldloc.0; ldloc.1; add; ret
    // (static int Main(int a, int b) -> a + b) -- deliberately parameter-based
    // so StackFold cannot constant-fold it and every statement keeps its own
    // SourceOffset. Sequence points map IL offsets 0..7 to C# lines 40..47 in
    // "Case.cs".
    private sealed record SynthResult(string AssemblyPath, int MainToken);

    private static SynthResult SynthesizeWithPdb()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var asmPath = Path.Combine(dir, "Case.dll");
        var pdbPath = Path.Combine(dir, "Case.pdb");

        // Assembly metadata (type-system tables only, like a real Release build).
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("Case.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"), new Version(4, 0, 0, 0),
            default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        var il = new BlobBuilder();
        var enc = new InstructionEncoder(il);
        enc.OpCode(ILOpCode.Ldarg_0); enc.OpCode(ILOpCode.Stloc_0);
        enc.OpCode(ILOpCode.Ldarg_1); enc.OpCode(ILOpCode.Stloc_1);
        enc.OpCode(ILOpCode.Ldloc_0); enc.OpCode(ILOpCode.Ldloc_1);
        enc.OpCode(ILOpCode.Add); enc.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        // LOCAL_SIG: 2 locals, both int32.
        var localSig = mdb.AddStandaloneSignature(mdb.GetOrAddBlob(new byte[] { 0x07, 0x02, 0x08, 0x08 }));
        var bodyOffset = stream.AddMethodBody(enc, 2, localSig, MethodBodyAttributes.None);
        // Default calling convention, 2 params, returns int32, params (int32, int32).
        var main = mdb.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x02, 0x08, 0x08, 0x08 }), bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Sym"), mdb.GetOrAddString("Case"), objRef, default(FieldDefinitionHandle), main);

        // Standalone portable PDB metadata (debug tables only).
        var pdbMdb = new MetadataBuilder();
        var sha1 = new Guid("FF1816EC-AA5E-4D10-87F7-6F4963833460"); // Debug_HashAlgorithm.Sha1
        var csLang = new Guid("3F5162F8-07C6-3D02-9B15-9B4A1B25A4A0"); // Debug_Language.CSharp
        var doc = pdbMdb.AddDocument(pdbMdb.GetOrAddDocumentName("Case.cs"), pdbMdb.GetOrAddGuid(sha1),
            pdbMdb.GetOrAddBlob(Array.Empty<byte>()), pdbMdb.GetOrAddGuid(csLang));
        var spBlob = new BlobBuilder();
        var sps = new (int Offset, int Line, int Col, int EndCol)[]
        {
            (0, 40, 1, 6), (1, 41, 1, 6), (2, 42, 1, 6), (3, 43, 1, 6),
            (4, 44, 1, 6), (5, 45, 1, 6), (6, 46, 1, 6), (7, 47, 1, 6),
        };
        EncodeSequencePoints(spBlob, sps);
        pdbMdb.AddMethodDebugInformation(doc, pdbMdb.GetOrAddBlob(spBlob));

        // The PE's CodeView debug-directory entry and the PDB's own id share
        // one content id (TryOpenAssociatedPortablePdb verifies the match).
        var contentId = new BlobContentId(Guid.NewGuid(), 0x12345678);

        var tableCounts = new int[MetadataTokens.TableCount];
        tableCounts[(int)TableIndex.Module] = 1;
        tableCounts[(int)TableIndex.TypeRef] = 1;
        tableCounts[(int)TableIndex.TypeDef] = 1;
        tableCounts[(int)TableIndex.MethodDef] = 1;
        tableCounts[(int)TableIndex.StandAloneSig] = 1;
        var typeSystemRowCounts = ImmutableArray.Create(tableCounts);

        var debugDir = new DebugDirectoryBuilder();
        debugDir.AddCodeViewEntry("Case.pdb", contentId, portablePdbVersion: 0x0100);
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(machine: Machine.I386,
            imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il, debugDirectoryBuilder: debugDir);
        var pdbBuilder = new PortablePdbBuilder(pdbMdb, typeSystemRowCounts, main, _ => contentId);

        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        File.WriteAllBytes(asmPath, blob.ToArray());
        var pdbBlob = new BlobBuilder();
        pdbBuilder.Serialize(pdbBlob);
        File.WriteAllBytes(pdbPath, pdbBlob.ToArray());

        return new SynthResult(asmPath, MetadataTokens.GetToken(main));
    }

    // Same assembly shape WITHOUT a debug directory / PDB (the no-symbols path).
    private static SynthResult SynthesizeNoPdbAssembly()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var asmPath = Path.Combine(dir, "Case.dll");

        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("Case.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"), new Version(4, 0, 0, 0),
            default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        var il = new BlobBuilder();
        var enc = new InstructionEncoder(il);
        enc.OpCode(ILOpCode.Ldarg_0); enc.OpCode(ILOpCode.Stloc_0);
        enc.OpCode(ILOpCode.Ldarg_1); enc.OpCode(ILOpCode.Stloc_1);
        enc.OpCode(ILOpCode.Ldloc_0); enc.OpCode(ILOpCode.Ldloc_1);
        enc.OpCode(ILOpCode.Add); enc.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var localSig = mdb.AddStandaloneSignature(mdb.GetOrAddBlob(new byte[] { 0x07, 0x02, 0x08, 0x08 }));
        var bodyOffset = stream.AddMethodBody(enc, 2, localSig, MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x02, 0x08, 0x08, 0x08 }), bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Sym"), mdb.GetOrAddString("Case"), objRef, default(FieldDefinitionHandle), main);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(machine: Machine.I386,
            imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll), root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        File.WriteAllBytes(asmPath, blob.ToArray());

        return new SynthResult(asmPath, MetadataTokens.GetToken(main));
    }

    // ECMA-335 portable PDB sequence-point blob encoding (the .NET 8
    // SequencePointCollection reader contract): local-signature header, then
    // per record -- delta IL offset (absolute for the first), deltaLines
    // (end-start line), deltaColumns, start-line and start-column deltas
    // (absolute for the first, signed-zigzag after).
    private static void EncodeSequencePoints(BlobBuilder bb, (int Offset, int Line, int Col, int EndCol)[] sps)
    {
        bb.WriteCompressedInteger(0); // local signature rid: none
        for (var i = 0; i < sps.Length; i++)
        {
            var sp = sps[i];
            bb.WriteCompressedInteger(i == 0 ? sp.Offset : sp.Offset - sps[i - 1].Offset);
            bb.WriteCompressedInteger(0); // deltaLines: single-line points (end == start)
            bb.WriteCompressedInteger(sp.EndCol - sp.Col);
            if (i == 0)
            {
                bb.WriteCompressedInteger(sp.Line);
                bb.WriteCompressedInteger(sp.Col);
            }
            else
            {
                bb.WriteCompressedInteger(ZigZag(sp.Line - sps[i - 1].Line));
                bb.WriteCompressedInteger(ZigZag(sp.Col - sps[i - 1].Col));
            }
        }
    }

    private static int ZigZag(int n) => (n << 1) ^ (n >> 31);
}
