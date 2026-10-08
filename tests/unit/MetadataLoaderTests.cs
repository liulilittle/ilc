using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Model;
using Xunit;

public class MetadataLoaderTests
{
    [Fact]
    public void LoadAssembly_FindsTypesAndStatics()
    {
        // Uses the fixtures build output from Task 3 Step 1
        var asm = MetadataLoader.LoadAssembly(FixturePath());
        Assert.Contains(asm, t => t.FullName == "Calc.Program");
        var program = asm.Single(t => t.FullName == "Calc.Program");
        Assert.Contains(program.StaticFields, f => f.Name == "counter");
        Assert.Contains(program.Methods, m => m.Name == "Main");
    }

    // The test host's working directory is the bin output directory
    // (E:\il2cpp\tests\unit\bin\Debug\net8.0), so relative paths cannot resolve
    // fixtures directly. Walk up from AppContext.BaseDirectory to the repo root to
    // locate the fixed relative path.
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

    //--------------------------------------------------------------------------
    // Devirt S1 (G1): TypeDef.IsSealed parses TypeAttributes.Sealed (0x1).
    // Fixture: the real net48 virtualsealed case exe -- Square/Tri are declared
    // `sealed`, Shape/Rect are not. IsAbstract must also parse (a static class
    // is abstract+sealed; IsEffectivelySealed excludes abstract).
    //--------------------------------------------------------------------------
    [Fact]
    public void LoadAssembly_ParsesSealedAndAbstractFlags()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string exe = null!;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "cases", "virtualsealed", "obj", "Release", "net48", "virtualsealed.exe");
            if (File.Exists(candidate)) { exe = candidate; break; }
            dir = dir.Parent;
        }
        Assert.NotNull(exe);

        var types = MetadataLoader.LoadAssembly(exe);
        // The closed-world DerivedCount (G3) is computed by the vtable planner
        // pass that CodeGenerator always runs before emission -- the unit test
        // must mirror that pipeline to observe IsEffectivelySealed correctly.
        Il2Cpp.Transpiler.Emit.VTablePlanner.PlanAll(types);
        var square = types.Single(t => t.FullName == "Square");
        var tri = types.Single(t => t.FullName == "Tri");
        var shape = types.Single(t => t.FullName == "Shape");
        var rect = types.Single(t => t.FullName == "Rect");

        Assert.True(square.IsSealed);
        Assert.True(tri.IsSealed);
        Assert.False(shape.IsSealed);
        Assert.False(rect.IsSealed);

        // closed-world sealing: Square/Tri have no subclass in the set, Shape
        // and Rect do -> IsEffectivelySealed follows (used by the emit branch).
        Assert.True(square.IsEffectivelySealed);
        Assert.True(tri.IsEffectivelySealed);
        Assert.False(rect.IsEffectivelySealed); // Square : Rect
        Assert.False(shape.IsEffectivelySealed); // Rect/Square/Tri : Shape
    }

    //--------------------------------------------------------------------------
    // refl2 plan D8 (reflops d1_prop): the PropertyMap/Property tables load
    // into the PropertyDef model -- name, full type name (decoded from the
    // PropertySig return slot), getter/setter METHOD NAMES (the typeinfo
    // property table references accessors by name through the SAME methods
    // table -- single source of truth), and the indexer parameter count.
    // Fixture: tests/fixtures/props (Counter/SubCounter with manual/auto/
    // static/computed/virtual/override properties).
    //--------------------------------------------------------------------------
    private static string PropsFixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "out", "props.dll");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "fixture props.dll not found; run scripts/build-fixtures.ps1 first (expects repo root/tests/fixtures/out/props.dll)");
    }

    [Fact]
    public void LoadAssembly_ParsesProperties_WithAccessorNamesAndType()
    {
        var asm = MetadataLoader.LoadAssembly(PropsFixturePath());
        var counter = asm.Single(t => t.FullName == "AuditFix.Counter");
        // Every Counter property decodes its full type name + accessor names.
        var value = counter.Properties.Single(p => p.Name == "Value");
        Assert.Equal("System.Int32", value.TypeName);
        Assert.Equal("get_Value", value.GetterName);
        Assert.Equal("set_Value", value.SetterName);
        Assert.Equal(0, value.IndexParamCount);
        var auto = counter.Properties.Single(p => p.Name == "Auto");
        Assert.Equal("get_Auto", auto.GetterName);
        Assert.Equal("set_Auto", auto.SetterName);
        var shared = counter.Properties.Single(p => p.Name == "Shared");
        Assert.Equal("System.Int32", shared.TypeName);
        // Doubled: computed getter ONLY (no setter) -> SetterName null.
        var doubled = counter.Properties.Single(p => p.Name == "Doubled");
        Assert.Equal("get_Doubled", doubled.GetterName);
        Assert.Null(doubled.SetterName);
    }

    [Fact]
    public void LoadAssembly_ParsesProperties_DerivedAndVirtual()
    {
        var asm = MetadataLoader.LoadAssembly(PropsFixturePath());
        var sub = asm.Single(t => t.FullName == "AuditFix.SubCounter");
        // SubCounter declares its own Tag (override) + Extra (non-virtual).
        Assert.Equal(2, sub.Properties.Count);
        Assert.Contains(sub.Properties, p => p.Name == "Tag" && p.GetterName == "get_Tag" && p.SetterName == "set_Tag");
        Assert.Contains(sub.Properties, p => p.Name == "Extra" && p.GetterName == "get_Extra" && p.SetterName == "set_Extra");
        // Property-free types carry an empty property list (table stays empty).
        var props = asm.Single(t => t.FullName == "AuditFix.Props");
        Assert.Empty(props.Properties);
    }
}
