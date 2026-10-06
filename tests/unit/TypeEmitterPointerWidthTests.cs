using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// G1 (cross-platform arch, root gate) fingerprint probes: the transpiler must
// lay out object instances with the TARGET pointer width (--arch), not the
// HOST width. Before the G1 implementation these probes are RED -- the layout
// is frozen at the host width (IntPtr.Size == 8), so an x86 (pointerSize == 4)
// layout yields 8-byte InstanceSize/field offsets instead of the required
// 4-byte grid.
//
// Model-level probes drive TypeEmitter directly with TypeDef.PointerSize set to
// the requested target width (the same value CodeGenerator.Generate stamps
// from --arch); the CLI-level probe exercises the full chain
// (--arch x86 -> PointerSize 4 -> layout/bitmap/offsets + the entry static_assert
// interlock) end to end on the calc fixture.
public class TypeEmitterPointerWidthTests
{
    // Fixture: Calc.Node { int V; Node Next; } -> slots 1 (value), 2 (ref);
    // bits: bit 2 = GC reference -> bitmap 0b100; slot count 3 (header + 2).
    private static TypeDef MakeNode()
    {
        var t = new TypeDef { Name = "Node", Namespace = "Calc" };
        t.InstanceFields.Add(new FieldDef { Name = "V", Owner = t, TypeKind = FieldTypeKind.Int32 });
        t.InstanceFields.Add(new FieldDef { Name = "Next", Owner = t, TypeKind = FieldTypeKind.Reference, FieldTypeName = "Calc.Node" });
        return t;
    }

    [Fact]
    public void Layout_PointerWidth4_InstanceSizeAndOffsetsAtFourBytes()
    {
        var t = MakeNode();
        t.PointerSize = 4; // --arch x86: target pointer width 4

        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(1, t.InstanceFields[0].SlotIndex);
        Assert.Equal(2, t.InstanceFields[1].SlotIndex);
        Assert.Equal(0b100u, t.RefFieldBitmap);
        Assert.Equal(1, t.RefFieldCount);
        // 3 slots (header + V + Next) at 4 bytes each = 12, NOT 24 (host 8).
        Assert.Equal(3 * 4, t.InstanceSize);
        // Self-consistency: the target runtime derives the refFieldBitmap bit
        // count as instanceSize / sizeof(void*) -- it must equal the slot count
        // on the target width (a host-width InstanceSize would make a 32-bit
        // runtime scan twice as many bitmap bits).
        Assert.Equal(3, t.InstanceSize / t.PointerSize);

        var code = TypeEmitter.EmitClassStruct(t);
        Assert.Contains("slot 1 at offset 4", code);
        Assert.Contains("slot 2 at offset 8", code);
    }

    [Fact]
    public void Layout_PointerWidth8_KeepsX64Layout()
    {
        var t = MakeNode();
        t.PointerSize = 8; // --arch x64 / arm64: byte-identical to the host layout

        TypeEmitter.LayoutInstanceFields(t);
        Assert.Equal(3 * IntPtr.Size, t.InstanceSize);
        Assert.Equal(24, t.InstanceSize);
        Assert.Equal(0b100u, t.RefFieldBitmap);
        Assert.Contains("slot 2 at offset 16", TypeEmitter.EmitClassStruct(t));
    }

    [Fact]
    public void Descriptor_PointerWidth4_RefFieldBitmapValue()
    {
        var t = MakeNode();
        t.PointerSize = 4;
        TypeEmitter.LayoutInstanceFields(t);
        var code = TypeEmitter.EmitClassDescriptor(t);
        Assert.Contains("0b100u", code); // bit 2 = slot 2 is a GC reference
    }

    [Fact]
    public void Cli_ArchX86_GeneratesFourByteLayoutAndStaticAssertInterlock()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_g1_arch_" + Guid.NewGuid().ToString("N"));
        try
        {
            var code = Program.RunMain(new[] { "--arch", "x86", FixturePath(), dir });
            Assert.Equal(0, code);

            var entry = File.ReadAllText(Path.Combine(dir, "src", "entry.cpp"));
            // Double-sided interlock: the generated C++ must be built for the
            // SAME pointer width the transpiler laid out with.
            Assert.Contains("static_assert(sizeof(void*) == 4", entry);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Cli_NoArch_PreservesHostWidthLayout()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_g1_arch_" + Guid.NewGuid().ToString("N"));
        try
        {
            var code = Program.RunMain(new[] { FixturePath(), dir });
            Assert.Equal(0, code);

            var entry = File.ReadAllText(Path.Combine(dir, "src", "entry.cpp"));
            Assert.Contains($"static_assert(sizeof(void*) == {IntPtr.Size}", entry);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
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
        throw new FileNotFoundException("fixture calc.dll not found; run scripts/build-fixtures.ps1 first");
    }
}