using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Model;
using Il2Cpp.Transpiler.Opt;
using Xunit;

//------------------------------------------------------------------------------
// GenericUsageAnalyzer tests -- compile-pruning verdicts for generic-type
// sharing (the user-mandated architecture, GENERIC-SHARE-DESIGN.md §2).
//
// Covers the dominant multi-type-parameter template shapes:
//   Pair<T1,T2>    -- pure object-compatible slot access  -> both params share
//   PairArr<T1,T2> -- `new T1[4]` (newarr T1)             -> conservative
//                     whole-definition per-instance fallback (the directed T1
//                     must NOT silently become an object slot)
//   PairCon<T1,T2> -- `where T1 : IComparable`           -> conservative
//                     whole-definition per-instance fallback
//
// Box<T> / VirtBase<T> (genericvirtdeep fixture): unconstrained storage-only
// templates must be shareable.
//------------------------------------------------------------------------------
public class GenericUsageAnalyzerTests
{
    private static string CaseExePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "cases", name, "obj", "Release", "net48", name + ".exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"fixture {name}.exe not found; build the case first");
    }

    private static (List<TypeDef> Types, MetadataReader Md, PEReader Pe) Load(string name)
    {
        var exe = CaseExePath(name);
        var stream = File.OpenRead(exe);
        var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var types = MetadataLoader.LoadAssembly(exe);
        return (types, md, pe);
    }

    private static Func<MethodDef, MethodBodyInfo?> BodyReader(PEReader pe, MetadataReader md, TypeDef def)
        => m =>
        {
            try { return MethodBodyReader.Read(pe, md, def, m); }
            catch { return null; }
        };

    [Fact]
    public void Pair_MultiTemplate_PureObjectAccess_BothParamsShare()
    {
        var (types, md, pe) = Load("paircase");
        var pair = types.Single(t => t.Name == "Pair`2" && !t.IsNested);
        var mask = GenericUsageAnalyzer.GetShareMask(md, pair, BodyReader(pe, md, pair));
        Assert.NotNull(mask);
        Assert.Equal(new[] { true, true }, mask);
     }

    [Fact]
    public void PairArr_MultiTemplate_NewarrT1_BlocksWholeDefinition()
    {
        var (types, md, pe) = Load("paircase");
        var pairArr = types.Single(t => t.Name.StartsWith("PairArr", StringComparison.Ordinal) && t.GenericArity == 2 && !t.IsNested);
        // new T1[4] is a directed use of parameter 0; the conservative phase
        // refuses the whole definition (never a wrong in-between).
        Assert.Null(GenericUsageAnalyzer.GetShareMask(md, pairArr, BodyReader(pe, md, pairArr)));
    }

    [Fact]
    public void PairCon_MultiTemplate_WhereConstraint_BlocksWholeDefinition()
    {
        var (types, md, pe) = Load("paircase");
        var pairCon = types.Single(t => t.Name.StartsWith("PairCon", StringComparison.Ordinal) && t.GenericArity == 2 && !t.IsNested);
        Assert.Null(GenericUsageAnalyzer.GetShareMask(md, pairCon, BodyReader(pe, md, pairCon)));
    }

    [Fact]
    public void Box_Unconstrained_StorageOnly_IsShareable()
    {
        var (types, md, pe) = Load("genericvirtdeep");
        var box = types.Single(t => t.Name.StartsWith("Box", StringComparison.Ordinal) && t.GenericArity == 1 && !t.IsNested);
        var mask = GenericUsageAnalyzer.GetShareMask(md, box, BodyReader(pe, md, box));
        Assert.NotNull(mask);
        Assert.Equal(new[] { true }, mask);
    }

    [Fact]
    public void VirtBase_Unconstrained_VirtualPassthrough_IsShareable()
    {
        var (types, md, pe) = Load("genericvirtdeep");
        var virtBase = types.Single(t => t.Name.StartsWith("VirtBase", StringComparison.Ordinal) && t.GenericArity == 1 && !t.IsNested);
        var mask = GenericUsageAnalyzer.GetShareMask(md, virtBase, BodyReader(pe, md, virtBase));
        Assert.NotNull(mask);
        Assert.Equal(new[] { true }, mask);
    }

    //--------------------------------------------------------------------------
    // P-G3 method-level sharing channel (GENERIC-SHARE-DESIGN.md §5): a
    // NON-generic class whose METHODS are generic (SwapRef<T> -- a byref T slot
    // swap) is a sharing candidate. The verdict for an arity-0 def is the empty
    // mask (vacuously all-true); MethodMvarShareable gates it on:
    //   (a) every MVAR parameter being a BYREF T slot,
    //   (b) no MVAR return,
    //   (c) body MVAR uses being pure ldobj/stobj slot moves.
    //--------------------------------------------------------------------------
    [Fact]
    public void SwapRef_ByrefSlotSwap_MethodLevelShareable()
    {
        var (types, md, pe) = Load("swapcase");
        var swap = types.Single(t => t.Name == "Swap" && !t.IsNested);
        var mask = GenericUsageAnalyzer.GetShareMask(md, swap, BodyReader(pe, md, swap));
        Assert.NotNull(mask); // arity-0: empty mask == shareable via method channel
        Assert.Empty(mask);
        Assert.True(GenericUsageAnalyzer.IsObjectShareable(md, swap, BodyReader(pe, md, swap)));
    }

    // P-G3 method-level gate (GENERIC-SHARE-DESIGN.md §5.1 rule ①): a generic
    // METHOD with a BY-VALUE MVAR parameter (Id<T>(T x)) is NOT shareable --
    // only byref-wrapped MVAR slots are object-ABI compatible. The whole
    // definition stays per-instance.
    [Fact]
    public void Id_ByValueMvarParam_BlocksMethodLevelShare()
    {
        var (types, md, pe) = Load("genlist");
        var genList = types.Single(t => t.Name == "GenList" && !t.IsNested);
        Assert.Null(GenericUsageAnalyzer.GetShareMask(md, genList, BodyReader(pe, md, genList)));
    }

    //--------------------------------------------------------------------------
    // P-G4 F3 (method-level MethodSpec/FieldToken MVAR scan): a byref-only
    // generic method passes rules ①/②, but its body may still pass its OWN
    // MVAR into a per-instance generic method as a MethodSpec type argument,
    // or touch a field whose declaring type carries the MVAR. Both are
    // directed uses the scan must refuse (make the whole definition stay on
    // the per-instance path, never a wrong shared object ABI).
    //--------------------------------------------------------------------------
    [Fact]
    public void ByrefOnlyMethod_BodyCallsPerInstanceGenericWithOwnMvar_BlocksShare()
    {
        var (types, md, pe) = Load("mvarusecase");
        var caller = types.Single(t => t.Name == "Passer2" && !t.IsNested);
        // a body that calls Tool2.Identity<!!0> (MethodSpec genArg = 0x1E)
        // needs the exact type -> the definition must NOT be shareable.
        Assert.Null(GenericUsageAnalyzer.GetShareMask(md, caller, BodyReader(pe, md, caller)));
    }

    [Fact]
    public void ByrefOnlyMethod_BodyStoresThroughMvarDeclaringType_BlocksShare()
    {
        var (types, md, pe) = Load("mvarusecase");
        var caller = types.Single(t => t.Name == "FieldUser2" && !t.IsNested);
        // ldflda/stfld on Box2`1<!!0>::item -- the field's declaring
        // TypeSpec carries the MVAR -> refusal.
        Assert.Null(GenericUsageAnalyzer.GetShareMask(md, caller, BodyReader(pe, md, caller)));
    }

    //--------------------------------------------------------------------------
    // P-G5 FINDING-1 (MethodToken MemberRef-Parent-TypeSpec-MVAR): a byref-only
    // generic method whose body `new`s a generic instance through a
    // MemberReference whose PARENT TypeSpec carries the method's own MVAR
    // (Maker.Make<T>() => new Box2<T>() -- the GenericInst return 0x15 wraps
    // the MVAR so rule ① returnIsVar does NOT catch it, and the body's only
    // token is the newobj MemberRef) must be refused: the MethodToken branch
    // must mirror the FieldToken branch and scan the member-ref parent TypeSpec
    // for 0x1E.
    //--------------------------------------------------------------------------
    [Fact]
    public void ByrefOnlyMethod_NewobjParentTypeSpecCarryingOwnMvar_BlocksShare()
    {
        var (types, md, pe) = Load("mvarusecase");
        var maker = types.Single(t => t.Name == "Maker" && !t.IsNested);
        Assert.Null(GenericUsageAnalyzer.GetShareMask(md, maker, BodyReader(pe, md, maker)));
    }

    //--------------------------------------------------------------------------
    // P2-D5 assertion (the D5-4 surface #3 decisioner relaxation): a generic
    // METHOD with a PURE MVAR return (0x1E) is NOW shareable when every MVAR
    // parameter is BYREF-wrapped (the byref T slots are Il2CppObject** object
    // slots) and the body is pure slot moves -- the definition-side MVAR return
    // is the boxed object slot (Il2CppObject*) and the call site unboxes by
    // concrete T (the return-slot unbox bridge, StackFold). PickUser2.Pick<T>
    // (ref T a) => a is exactly that shape -> the arity-0 def gets the empty
    // (vacuously all-true) share mask via the method-level channel.
    //--------------------------------------------------------------------------
    [Fact]
    public void MvarReturnMethod_ByrefParameter_IsShareable()
    {
        var (types, md, pe) = Load("mvarusecase");
        var picker = types.Single(t => t.Name == "PickUser2" && !t.IsNested);
        var mask = GenericUsageAnalyzer.GetShareMask(md, picker, BodyReader(pe, md, picker));
        Assert.NotNull(mask);
        Assert.Empty(mask);
        Assert.True(GenericUsageAnalyzer.IsObjectShareable(md, picker, BodyReader(pe, md, picker)));
    }

    // P2-D5: the SAME verdict on the mvarreturncase e2e fixture's Picker (the
    // call-site unbox bridge is verified at runtime by the case itself).
    [Fact]
    public void Picker_MvarReturnByrefSlot_IsShareable()
    {
        var (types, md, pe) = Load("mvarreturncase");
        var picker = types.Single(t => t.Name == "Picker" && !t.IsNested);
        var mask = GenericUsageAnalyzer.GetShareMask(md, picker, BodyReader(pe, md, picker));
        Assert.NotNull(mask);
        Assert.Empty(mask);
    }

    //--------------------------------------------------------------------------
    // P-G4 D7 -- TypeSpecCarriesMvar blob coverage. The scanner's TypeSig macro
    // tree lives in TypeSigMvarScanCore; the D7 surface is the TypeSpec SHAPE
    // knots that wrap a METHOD-level MVAR (0x1E): the 0x1B GenericInst knot,
    // the 0x14 multidimensional ARRAY, and the 0x1D SZARRAY. Each shape must
    // report true when it wraps 0x1E, false when it wraps a CLASS-level 0x13
    // VAR, and false when the blob cannot be decoded (conservative, "prefer explicit specialization").
    // Blobs are hand-synthesized (MetadataBuilder TypeSpec rows, no compiler can
    // emit an MVAR in a standalone TypeSpec); the private scanner is reached via
    // reflection (the same NonPublic|Static pattern as CodeGeneratorTests.
    // ArrayElementType).
    //--------------------------------------------------------------------------
    [Fact]
    public void TypeSpecCarriesMvar_GenericInstKnot_0x1B_WrappingMvar_IsTrue()
    {
        using var ts = SynthesizeTypeSpec(0x1B, 0x1E, 0x00); // 0x1B knot -> MVAR#0
        Assert.True(InvokeTypeSpecCarriesMvar(ts.Md, ts.Token));
    }

    [Fact]
    public void TypeSpecCarriesMvar_MultidimArray_0x14_WrappingMvar_IsTrue()
    {
        // 0x14 ARRAY: element 0x1E MVAR, then ArrayShape (rank 1, 0 sizes/lo).
        using var ts = SynthesizeTypeSpec(0x14, 0x1E, 0x00, 0x01, 0x00, 0x00);
        Assert.True(InvokeTypeSpecCarriesMvar(ts.Md, ts.Token));
    }

    [Fact]
    public void TypeSpecCarriesMvar_Szarray_0x1D_WrappingMvar_IsTrue()
    {
        using var ts = SynthesizeTypeSpec(0x1D, 0x1E, 0x00); // T[] where T is the method MVAR
        Assert.True(InvokeTypeSpecCarriesMvar(ts.Md, ts.Token));
    }

    [Fact]
    public void TypeSpecCarriesMvar_SzarrayWrappingClassLevelVar_0x13_IsFalse()
    {
        // 0x1D SZARRAY wrapping 0x13 VAR -- a CLASS-level parameter, not a
        // method MVAR; the shared-object path can keep the default.
        using var ts = SynthesizeTypeSpec(0x1D, 0x13, 0x00);
        Assert.False(InvokeTypeSpecCarriesMvar(ts.Md, ts.Token));
    }

    [Fact]
    public void TypeSpecCarriesMvar_UndecodableTruncatedKnot_IsFalse()
    {
        // 0x1B knot with nothing after it: the recursive decode hits EOF and
        // must be reported as undecodable -> conservative false (no MVAR).
        using var ts = SynthesizeTypeSpec(0x1B);
        Assert.False(InvokeTypeSpecCarriesMvar(ts.Md, ts.Token));
    }

    //--------------------------------------------------------------------------
    // G1 (array-argument cells): element-aware encoding + per-instance
    // judgment. The SZARRAY (0x1D) generic-argument producers encode the
    // element as a bracket token ("[int]" / "[string]" / "[Cases.Foo]" --
    // the four-place ReadM3ArrayArg rule, MethodBodyReader.SimpleGenericArgSuffix
    // is the local-kind authority), so Box<int[]> != Box<string[]>. An
    // array-typed argument must NEVER be collapsed onto the shared "object"
    // clone (element info would be lost -- net48 keeps distinct closed types).
    //--------------------------------------------------------------------------
    [Fact]
    public void IsArrayArgToken_BracketTokens_Detected()
    {
        Assert.True(GenericUsageAnalyzer.IsArrayArgToken("[int]"));
        Assert.True(GenericUsageAnalyzer.IsArrayArgToken("[string]"));
        Assert.True(GenericUsageAnalyzer.IsArrayArgToken("[Cases.Foo]"));
        Assert.False(GenericUsageAnalyzer.IsArrayArgToken("int"));
        Assert.False(GenericUsageAnalyzer.IsArrayArgToken("Cases.Foo"));
    }

    [Fact]
    public void CollapseInstantiation_ArrayArg_NotCollapsedToObject()
    {
        // Box`1 is in the share set, but an ARRAY argument is directed: the
        // closure must stay per-instance (element-aware clone), never the
        // shared "object" collapse.
        GenericUsageAnalyzer.SharedGenericDefs = new HashSet<string>(StringComparer.Ordinal) { "Box`1" };
        try
        {
            var args = new[] { "[int]" };
            Assert.False(GenericUsageAnalyzer.CollapseInstantiation("Box`1", args));
            Assert.Equal("[int]", args[0]); // token survives intact
            var strArgs = new[] { "[string]" };
            Assert.False(GenericUsageAnalyzer.CollapseInstantiation("Box`1", strArgs));
        }
        finally { GenericUsageAnalyzer.SharedGenericDefs = null; }
    }

    [Fact]
    public void CollapseInstantiation_PlainRefArg_StillCollapses()
    {
        // Control: a NON-array reference argument (a user class) still
        // collapses onto the shared object clone (the G1 guard is array-only).
        GenericUsageAnalyzer.SharedGenericDefs = new HashSet<string>(StringComparer.Ordinal) { "Box`1" };
        try
        {
            var args = new[] { "Cases.Shape" };
            Assert.True(GenericUsageAnalyzer.CollapseInstantiation("Box`1", args));
            Assert.Equal("object", args[0]);
        }
        finally { GenericUsageAnalyzer.SharedGenericDefs = null; }
    }

    // A one-TypeSpec PE whose blob is the given signature bytes; disposable to
    // own the PEReader lifetime.
    private sealed class SynthTypeSpec : IDisposable
    {
        private readonly PEReader _pe;
        public SynthTypeSpec(byte[] sig)
        {
            var mdb = new MetadataBuilder();
            mdb.AddModule(0, mdb.GetOrAddString("ts.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
                default(GuidHandle), default(GuidHandle));
            var spec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(sig));
            var root = new MetadataRootBuilder(mdb, "v4.0.30319");
            var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                    machine: Machine.I386,
                    imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
                root, new BlobBuilder());
            var blob = new BlobBuilder();
            peBuilder.Serialize(blob);
            _pe = new PEReader(new MemoryStream(blob.ToArray()));
            Md = _pe.GetMetadataReader();
            Token = MetadataTokens.GetToken(spec);
        }
        public MetadataReader Md { get; }
        public int Token { get; }
        public void Dispose() => _pe.Dispose();
    }

    private static SynthTypeSpec SynthesizeTypeSpec(params byte[] sig) => new(sig);

    private static bool InvokeTypeSpecCarriesMvar(MetadataReader md, int token)
    {
        var method = typeof(GenericUsageAnalyzer).GetMethod(
            "TypeSpecCarriesMvar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, new object[] { md, token })!;
    }
}
