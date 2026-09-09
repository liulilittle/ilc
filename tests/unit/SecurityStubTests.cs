using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// Security stubs verification (r127 Security residual - stub declarations + linkage)
public class SecurityStubTests
{
    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures", "Security_stub.exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Security_stub.exe fixture not found");
    }

    [Fact]
    public void Generate_SecurityContextFrame_Push_Pop_Link()
    {
        var exe = CaseExePath("delegate2");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("struct System_Reflection_SecurityContextFrame;", header);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_RuntimeHelpers_ProbeForSufficientStack_Link()
    {
        var exe = CaseExePath("delegate2");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "entry.cpp"));
            Assert.Contains("System_Runtime_CompilerServices_RuntimeHelpers_ProbeForSufficientStack_822160b172e6de22", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_Guid_NewGuid_Link()
    {
        var exe = CaseExePath("delegate2");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "System_Guid.cpp"));
            Assert.Contains("System_Guid_NewGuid_03c02fe8f2eb79a9", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_Marshal_CopyToManaged_Link()
    {
        var exe = CaseExePath("delegate2");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "entry.cpp"));
            Assert.Contains("System_Runtime_InteropServices_Marshal_CopyToManaged_462fd10303ea5155", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_Security_PInvoke_Link()
    {
        var exe = CaseExePath("delegate2");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "System_Security_AccessControl_Win32.cpp"));
            Assert.Contains("ConvertSecurityDescriptorToStringSecurityDescriptorW", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_Exception_GetObjectData_Link()
    {
        var exe = CaseExePath("delegate2");
        var types = MetadataLoader.LoadAssembly(exe);
        using (var pe = new PEReader(File.OpenRead(exe)))
            TypeEmitter.ExpandNestedTypes(pe.GetMetadataReader(), types);
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, exe);
            var cpp = File.ReadAllText(Path.Combine(dir, "src", "System_Exception.cpp"));
            Assert.Contains("System_Exception_GetObjectData_d5c9505bfa2a7d3a", cpp);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string CaseExePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "cases", name, "obj", "Release", "net48", name + ".exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"fixture {name}.exe not found; build the case first (dotnet build tests/cases/{name}/{name}.csproj -c Release)");
    }
}
