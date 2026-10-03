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
// sharing (the user-mandated architecture, GENERIC-SHARE-DESIGN.md 搂2).
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
}
